# PR-2: Joined Count Narrowing + Nested-Join Candidate Leak Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make inner-join `Count()` return the same row count `Execute()` produces (it currently returns the un-narrowed left candidate count), and close the last known leak window: the nested-join seeded candidate set stranded when the inner plan throws before its base executes.

**Architecture:** `CountCoreJoined` gains the `ExecuteIndexedInner` narrowing step that `ExecuteCoreJoined` already runs (indexed-inner resolvers narrow candidates via `RetainNonNullSlots`). The nested-candidates fix adds candidate cleanup to `ExecuteCoreJoinedKeyed`'s existing `finally` — contingent on first PROVING candidates are disposed by-ref on the success path (investigation task), so the cleanup cannot double-return.

**Tech Stack:** .NET 9/10, NUnit 4, `LeakAssert`/tracking-pool harness, BenchmarkDotNet gate.

## Global Constraints

- **Perf gate:** `Execute()`/`ExecutePooled()` paths must be untouched instruction-wise; `Count()` on inner joins deliberately does more work (user decision 2026-07-20) — quantify it with a before/after BDN short-job run and put the numbers in the PR body. Allocation columns byte-identical on Execute benchmarks.
- Candidate cleanup must be provably exactly-once — a double `ValueSet` return corrupts the shared pool. The tracking-pool violation counter is the enforcement: any double-return fails the leak suite.
- Branch: `fix/joined-count-and-nested-candidates` off `main`.

---

### Task 1: Count() narrowing — failing test first

**Files:**
- Modify: `tests/Prague.Core.Tests/Leaks/QueryJoinLeakTests.cs` (the pinned Count test)
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` (`CountCoreJoined`, ~line 1714)

**Interfaces:**
- Consumes: `JoinedResultContaier.PrepareIndexedInner/ExecuteIndexedInner/Dispose` (ResolverChain.cs), `_leftQuery.CountBase()`.
- Produces: `Count()` on a joined query with indexed-inner resolvers == `Execute().Count`.

- [ ] **Step 1: Flip the pinned expectation to the correct semantics**

In `QueryJoinLeakTests.InnerJoinOne_Indexed_Count_DoesNotLeakValuesArray`, change:

```csharp
			var count = _left.Query().InnerJoinOne(_rightHalf, _rightHalfUniqueIndex).Count();
			Assert.That(count, Is.EqualTo(Size));
```
to:
```csharp
			var count = _left.Query().InnerJoinOne(_rightHalf, _rightHalfUniqueIndex).Count();
			Assert.That(count, Is.EqualTo(Size / 2), "inner-join Count must equal Execute's row count");
```
and update the comment above it (it currently documents the OLD left-candidate semantics — replace with: `// Count runs the same indexed-inner narrowing as Execute, so it reports matched rows.`).

- [ ] **Step 2: Run — expect FAIL (500 ≠ 250)**

```bash
dotnet test tests/Prague.Core.Tests -f net9.0 --filter "FullyQualifiedName~InnerJoinOne_Indexed_Count" --nologo
```
Expected: FAIL `Expected: 250 But was: 500`.

- [ ] **Step 3: Add the narrowing step to `CountCoreJoined`**

`src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` — current body:

```csharp
		var container = new JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TJoinResult>(
			ref _resolverChain, true, false, 0, int.MaxValue, _manyCount);
		try {

			container.PrepareIndexedInner(ref this);
			return _leftQuery.CountBase();
		} finally {
			container.Dispose();
		}
```
becomes:
```csharp
		var container = new JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TJoinResult>(
			ref _resolverChain, true, false, 0, int.MaxValue, _manyCount);
		try {
			container.PrepareIndexedInner(ref this);
			// Same narrowing Execute runs: indexed-inner resolvers drop non-matching lefts from
			// the candidate set (RetainNonNullSlots), so CountBase counts matched rows.
			container.ExecuteIndexedInner(ref this);
			return _leftQuery.CountBase();
		} finally {
			container.Dispose();
		}
```

- [ ] **Step 4: Run the flipped test + the whole leak fixture — expect PASS, zero leak/violation failures**

```bash
dotnet test tests/Prague.Core.Tests -f net9.0 --filter "FullyQualifiedName~Leaks" --nologo
```
Expected: all pass (the `finally container.Dispose()` from PR #23 already returns the dictionary + values the narrowing populates).

- [ ] **Step 5: Check chained/Many coverage — add one Count test for a JoinMany chain**

Append to `QueryJoinLeakTests`:

```csharp
	[Test]
	public void InnerJoinMany_Count_MatchesExecuteCount() =>
		LeakAssert.Balanced(() => {
			int executed;
			using (var results = _left.Query().Where(static l => l.Id < 50).InnerJoinMany(_rightMany, _manyIndex).ExecutePooled())
				executed = results.Count;
			var counted = _left.Query().Where(static l => l.Id < 50).InnerJoinMany(_rightMany, _manyIndex).Count();
			Assert.That(counted, Is.EqualTo(executed));
		});
```

Run it; if it fails because `InnerJoinMany` narrowing lives outside `ExecuteIndexedInner`, investigate which container step performs Many-narrowing (`RetainNonEmptyManySlots` — see `JoinResults.generated.cs`) and mirror it in `CountCoreJoined`; the acceptance bar is Count == Execute count for every inner family, leak-balanced.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs tests/Prague.Core.Tests/Leaks/QueryJoinLeakTests.cs
git commit -m "Inner-join Count() runs indexed-inner narrowing - Count now equals Execute's row count"
```

---

### Task 2: Quantify the Count cost (perf gate)

**Files:**
- Create: `benchmarks/Prague.Benchmarks/JoinCountBenchmarks.cs`

- [ ] **Step 1: Add a benchmark for joined Count (before merging, run it on main AND this branch)**

```csharp
namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Prague.Core;

[MemoryDiagnoser]
public class JoinCountBenchmarks {
	private InMemoryDataCache<int, BenchLeft> _left = null!;
	private InMemoryDataCache<int, BenchRight> _right = null!;
	private CacheUniqueIndex<int, BenchRight, int> _rightIndex = null!;

	[GlobalSetup]
	public void Setup() {
		_left = new InMemoryDataCache<int, BenchLeft>();
		_right = new InMemoryDataCache<int, BenchRight>();
		_rightIndex = _right.AddKeyValueIndex<int>(static (_, v) => v.LeftId);
		for (var i = 0; i < 10_000; i++) {
			_left.AddOrUpdate(i, new BenchLeft { Id = i });
			if (i % 2 == 0)
				_right.AddOrUpdate(i, new BenchRight { Id = i, LeftId = i });
		}
	}

	[Benchmark(Baseline = true)]
	public int ExecutePooled_InnerJoin() {
		using var r = _left.Query().InnerJoinOne(_right, _rightIndex).ExecutePooled();
		return r.Count;
	}

	[Benchmark]
	public int Count_InnerJoin() => _left.Query().InnerJoinOne(_right, _rightIndex).Count();
}

public sealed class BenchLeft : ICacheEquatable<BenchLeft>, ICacheClonable<BenchLeft> {
	public int Id { get; init; }
	public bool CacheEquals(BenchLeft? other) => other is not null && other.Id == Id;
	public int CacheGetHashCode() => Id;
	public BenchLeft Clone() => new() { Id = Id };
}

public sealed class BenchRight : ICacheEquatable<BenchRight>, ICacheClonable<BenchRight> {
	public int Id { get; init; }
	public int LeftId { get; init; }
	public bool CacheEquals(BenchRight? other) => other is not null && other.Id == Id && other.LeftId == LeftId;
	public int CacheGetHashCode() => HashCode.Combine(Id, LeftId);
	public BenchRight Clone() => new() { Id = Id, LeftId = LeftId };
}
```

- [ ] **Step 2: Run on this branch, then `git stash` → run on main → `git stash pop`**

```bash
dotnet run -c Release --project benchmarks/Prague.Benchmarks -f net9.0 -- --filter "*JoinCountBenchmarks*" --job short
```
Expected: `Count_InnerJoin` on this branch costs more than on main (it now narrows — that is the approved semantics change) but MUST stay ≤ `ExecutePooled_InnerJoin` (it skips materialization); `ExecutePooled_InnerJoin` itself must be at parity with main (mean within error, allocation identical). Put both tables in the PR body.

- [ ] **Step 3: Commit**

```bash
git add benchmarks/Prague.Benchmarks/JoinCountBenchmarks.cs
git commit -m "Benchmark joined Count vs ExecutePooled"
```

---

### Task 3: Nested-join seeded-candidates window — investigate, then fix

**Files:**
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` (`ExecuteCoreJoinedKeyed`, ~line 1751)
- Test: `tests/Prague.Core.Tests/Leaks/QueryJoinLeakTests.cs`

**Interfaces:**
- Consumes: `UnsafeSeedCandidates(ValueSet<TLeftKey,...>)` (CacheQueryBuilder.cs:1771), `_leftQuery.Candidates` (`ICandidatesExecutor`), `ValueSet.IsInitlized`/`Dispose()`.
- Produces: no API change; the keyed pipeline's `finally` also releases a still-owned candidate set.

- [ ] **Step 1: PROVE the by-ref dispose invariant (do not skip)**

Read `ExecutePaired` (CacheQueryBuilder.cs ~1546-1561) and the unpaired `ExecuteBase`→`TryGet` consumption path (~660-700), and answer in the task notes: *when the base execution disposes candidates, does the dispose null the fields of the SAME `ValueSet` storage that `_leftQuery.Candidates` reads (by-ref), so `Candidates.IsInitlized` is false afterwards?* Evidence: `TryGet(ref container, ref _candidates, _filter)` passes by ref (context/joins.md), and `ValueSet.Dispose` nulls its array fields (ValueSet.cs:549-551).

Expected: yes → proceed. If the answer is no (dispose happens on a copy), STOP this task and record the finding in the plan — the fix below would double-return and must be redesigned (e.g., an explicit `CandidatesConsumed` flag on the executor).

- [ ] **Step 2: Write the failing leak test**

The window: nested JoinMany whose INNER plan has an indexed-inner resolver that throws before base execution. Build on the existing `_rightMany`/`_manyIndex` fixtures; the inner plan needs its own indexed-inner join with a throwing filter:

```csharp
	[Test]
	public void NestedJoinMany_InnerPlanThrowsBeforeBase_DoesNotLeakSeededCandidates() =>
		LeakAssert.Balanced(() => {
			try {
				_left.Query().Where(static l => l.Id < 50)
					.JoinMany(_rightMany, _manyIndex, static q => q.InnerJoinOne(
						/* right cache + unique index reachable from the nested builder */
						throw new InvalidOperationException("hostile nested filter")))
					.ExecutePooled();
				Assert.Fail("nested filter must throw");
			} catch (InvalidOperationException) {
			}
		});
```

The exact nested-builder syntax must be taken from `tests/Prague.Core.Tests/Join/JoinManyNestedCoreTests.cs` (read it first — it shows how a nested inner plan is composed); the requirement is: the throw must originate INSIDE the inner plan's `PrepareIndexedInner`/`ExecuteIndexedInner`, after `UnsafeSeedCandidates` has run. If a plain filter-throw cannot reach that window through the public API, drive `ExecuteCoreJoinedKeyed` directly via `InternalsVisibleTo` with a seeded candidate set and a resolver chain whose indexed-inner step throws.

- [ ] **Step 3: Run — expect FAIL (leaked ValueSet arrays reported with rent stacks)**

- [ ] **Step 4: Add candidate cleanup to the keyed pipeline's finally**

In `ExecuteCoreJoinedKeyed`, extend the `finally` added in PR #23:

```csharp
		} finally {
			// ExtractKeyedResults defaults the container's dict/disposer, so on success this
			// is a no-op; on a throw it returns everything the container still owns.
			container.Dispose();
			// A seeded/auto-populated candidate set the base execution never consumed is still
			// ours: base execution disposes candidates BY REF (verified Task 3 Step 1), so
			// IsInitlized==true here means unconsumed — exactly-once is guaranteed.
			var candidates = _leftQuery.Candidates;
			if (candidates.IsInitlized)
				candidates.Dispose();
		}
```

Note: `candidates` is a struct copy — `Dispose()` returns the arrays and nulls the COPY's fields. That is safe here only because nothing observes `_leftQuery.Candidates` after this frame (the builder dies with the call); the tracking pool's violation counter in the leak suite is the proof.

- [ ] **Step 5: Run the new test + the full leak fixture + full Core suite — all green, zero violations**

```bash
dotnet test tests/Prague.Core.Tests --nologo
```

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs tests/Prague.Core.Tests/Leaks/QueryJoinLeakTests.cs
git commit -m "Release seeded candidates when the nested inner plan throws before base execution"
```

---

### Task 4: PR

- [ ] **Step 1: Full suite both TFMs** (`dotnet test Prague.Tests.slnf --nologo`) — 0 failures.
- [ ] **Step 2: Push, open PR with BOTH benchmark tables (main vs branch) in the body**

```bash
git push -u origin fix/joined-count-and-nested-candidates
gh pr create --title "Inner-join Count() narrows like Execute; close the nested-join seeded-candidates leak window" --body-file /tmp/pr2-body.md
```
(Compose `/tmp/pr2-body.md` from this plan's summary + the two benchmark tables; end with the 🤖 footer.)

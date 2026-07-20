# PR-3: Dead Code Removal + SortedArraySet Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `TopKHeap` and `CompoundIndex.SeekAndTakePooled` (zero callers, latent double-return/ownership risks); make `SortedArraySet`'s enumerators safe against double-Dispose and boxed-path use-after-return.

**Architecture:** Pure deletions (zero perf risk) plus two contained fixes inside `SortedArraySet`: a fire-once release in the ref-struct `Enumerator`, and refcount participation + finalizer backstop for `BoxedEnumerator`. `SortedArraySet` has zero src callers, so nothing outside the type and its tests/benchmarks is touched.

**Tech Stack:** .NET 9/10, NUnit 4, `LeakAssert`/tracking-pool harness.

## Global Constraints

- Deletions must be verified caller-free at execution time (grep steps below) — do not trust this plan's snapshot.
- `SortedArraySet` is public API: no signature changes, behavior fixes only.
- Enumerator fixes are on cold paths (Dispose/GetEnumerator once per enumeration) — no benchmark gate needed, but `SortedArraySetBenchmarks` must still compile.
- Branch: `chore/dead-code-and-sortedarrayset` off `main`.

---

### Task 1: Delete `TopKHeap`

**Files:**
- Delete: `src/Prague.Core/Collections/TopKHeap.cs`
- Modify: `tests/Prague.Core.Tests/Leaks/CollectionLeakTests.cs` (remove `TopKHeap_PushDrainDispose_Balanced`)

- [ ] **Step 1: Verify zero callers**

```bash
grep -rn "TopKHeap" src tests benchmarks --include="*.cs" | grep -v obj
```
Expected: only `TopKHeap.cs` itself and the one leak test. If ANY other hit appears, STOP and re-plan (the type is in use).

- [ ] **Step 2: Delete the file and the leak test; build + Core tests**

```bash
git rm src/Prague.Core/Collections/TopKHeap.cs
# remove the TopKHeap_PushDrainDispose_Balanced test method from CollectionLeakTests.cs
dotnet build Prague.sln --nologo -v q && dotnet test tests/Prague.Core.Tests --nologo
```
Expected: clean build (also proves no benchmark referenced it), tests green.

- [ ] **Step 3: Commit** — `git commit -am "Remove TopKHeap (zero callers, latent struct-copy double-return)"`

---

### Task 2: Delete `CompoundIndex.SeekAndTakePooled`

**Files:**
- Modify: `src/Prague.Core/Collections/CompoundIndex.cs:58-73` (delete the method + its doc comment)

- [ ] **Step 1: Verify zero callers**

```bash
grep -rn "SeekAndTakePooled" src tests benchmarks --include="*.cs" | grep -v obj
```
Expected: only the definition. (The unpooled `SeekAndTake` and `SeekFilterAndTake` stay — they take caller-owned buffers.)

- [ ] **Step 2: Delete, build, test** (same commands as Task 1 Step 2). Expected: green.

- [ ] **Step 3: Commit** — `git commit -am "Remove SeekAndTakePooled (zero callers; bare-tuple pool ownership transfer)"`

---

### Task 3: SortedArraySet — fire-once ref-struct enumerator Dispose

**Files:**
- Modify: `src/Prague.Core/Collections/SortedArraySet.cs:40-54` (Enumerator.Dispose), `:30-38` (ctor context)
- Test: `tests/Prague.Core.Tests/Leaks/CollectionLeakTests.cs`

Background: `Enumerator.Dispose` (line 47-53) calls `_owner.ReleasePooledArray(_items)` gated only on `_isPooled`; a second Dispose double-decrements `_pooledRefCount`, prematurely returning the array while the set still uses it (use-after-return, and a tracking-pool violation once the owner disposes too).

- [ ] **Step 1: Write the failing test**

Add to `CollectionLeakTests`:

```csharp
	[Test]
	public void SortedArraySet_EnumeratorDoubleDispose_DoesNotDoubleRelease() =>
		LeakAssert.Balanced(static () => {
			var set = new SortedArraySet<int>();
			for (var i = 0; i < 100; i++)
				set.Add(i);
			var enumerator = set.GetEnumerator();
			enumerator.MoveNext();
			enumerator.Dispose();
			enumerator.Dispose(); // must be a no-op, not a second refcount decrement
			Assert.That(set.Contains(50), Is.True, "set must still be usable");
			set.Dispose();
		});
```

- [ ] **Step 2: Run — expect FAIL** (violation: the premature `Return` fires while the set is live, then `set.Dispose()` double-returns)

```bash
dotnet test tests/Prague.Core.Tests -f net9.0 --filter "FullyQualifiedName~SortedArraySet_EnumeratorDoubleDispose" --nologo
```

- [ ] **Step 3: Make Dispose fire-once** — in the `Enumerator` ref struct, `_isPooled` is already a mutable field; clear it on first release:

```csharp
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose()
		{
			if (_isPooled)
			{
				_isPooled = false; // fire-once: a second Dispose must not decrement the refcount again
				_owner.ReleasePooledArray(_items);
			}
		}
```
(Match the file's existing Allman brace style — this file predates the house style; don't reformat unrelated lines.)

- [ ] **Step 4: Run — expect PASS.** Then run the whole leak fixture. **Step 5: Commit** — `git commit -am "SortedArraySet enumerator Dispose is fire-once"`

---

### Task 4: SortedArraySet — boxed enumerator joins the refcount

**Files:**
- Modify: `src/Prague.Core/Collections/SortedArraySet.cs:56-88` (BoxedEnumerator), `:193-201` (IEnumerable GetEnumerator), plus the acquire/release helpers (~150-230 — read them first)

Background: the `IEnumerable<T>` path constructs `BoxedEnumerator(_items, _count)` with NO refcount; a grow or `Dispose` during boxed enumeration returns `_items` to the pool under the enumerator (use-after-return read).

- [ ] **Step 1: Read `SortedArraySet.cs:150-230`** and note the exact acquire pattern the ref-struct `GetEnumerator` uses to bump `_pooledRefCount` (expected: an `Interlocked.Increment` when `_pooledArray != null`). The boxed path must use the same helper; add one if the increment is inline.

- [ ] **Step 2: Write the failing test**

```csharp
	[Test]
	public void SortedArraySet_DisposeDuringBoxedEnumeration_DefersArrayReturn() =>
		LeakAssert.Balanced(static () => {
			var set = new SortedArraySet<int>();
			for (var i = 0; i < 100; i++)
				set.Add(i);
			var boxed = ((IEnumerable<int>)set).GetEnumerator();
			boxed.MoveNext();
			set.Dispose(); // must NOT return the array while boxed still reads it
			Assert.That(boxed.MoveNext(), Is.True);
			boxed.Dispose(); // last owner: releases here
		});
```
Expected failure mode today: no violation is *guaranteed* (the read is silent), so also assert balance — with no refcount, `set.Dispose()` returns the array and `boxed.Dispose()` releases nothing; the test documents intent and catches the double-release once boxed participates. If the pre-fix run happens to pass, keep the test anyway (it pins the fixed contract) and note it in the PR.

- [ ] **Step 3: Implement** — `BoxedEnumerator` takes the owner, acquires on construction, releases exactly once, finalizer backstop:

```csharp
	private sealed class BoxedEnumerator : IEnumerator<T>, IEnumerator, IDisposable
	{
		private readonly SortedArraySet<T> _owner;
		private readonly T[] _items;
		private readonly int _count;
		private readonly bool _isPooled;
		private int _released; // 0 = holds a refcount, 1 = released
		private int _index;

		public T Current => _items[_index];

		object? IEnumerator.Current => Current;

		internal BoxedEnumerator(SortedArraySet<T> owner, T[] items, int count, bool isPooled)
		{
			_owner = owner;
			_items = items;
			_count = count;
			_isPooled = isPooled;
			_index = -1;
		}

		public bool MoveNext() => ++_index < _count;

		public void Reset() => _index = -1;

		public void Dispose()
		{
			DisposeCore();
			GC.SuppressFinalize(this);
		}

		~BoxedEnumerator() => DisposeCore();

		private void DisposeCore()
		{
			if (_isPooled && Interlocked.Exchange(ref _released, 1) == 0)
				_owner.ReleasePooledArray(_items);
		}
	}
```
and the `IEnumerable<T>.GetEnumerator()` site (~193-201) must acquire the refcount before constructing (mirror the ref-struct path's acquire, passing the same `isPooled` it observed). Adapt member names to what Step 1 found.

- [ ] **Step 4: Run both new tests + leak fixture + full Core suite — green, zero violations.**
- [ ] **Step 5: Commit** — `git commit -am "SortedArraySet boxed enumerator participates in the pooled-array refcount"`

---

### Task 5: PR

- [ ] Full suite (`dotnet test Prague.Tests.slnf --nologo`) green both TFMs; benchmarks compile (`dotnet build benchmarks/Prague.Benchmarks -c Release --nologo -v q`).
- [ ] Push + `gh pr create --title "Remove dead pooled code (TopKHeap, SeekAndTakePooled); harden SortedArraySet enumerators"` with a body summarizing the three tasks and noting zero Release-path perf impact (deletions + cold-path enumerator fixes). End with the 🤖 footer.

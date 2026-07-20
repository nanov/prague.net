# PR-1: ValueDictionary Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the buggy (and caller-less) delegate `Filter` overload, pin the struct overload's compaction-cursor semantics with a regression test, and add Debug-only guards for the two Release-mode memory-corruption windows.

**Architecture:** Pure deletion + Debug.Assert additions in `ValueDictionary`/`ValueSet`; one new direct-unit test file. No Release-mode behavior change anywhere, so no benchmark gate is needed — verified by the allocation/timing-sensitive tests already in the suite.

**Tech Stack:** .NET 9/10, NUnit 4, existing `LeakAssert`/tracking-pool harness.

## Global Constraints

- No Release-mode perf change: guards are `Debug.Assert` only (user decision 2026-07-20).
- House style: tabs w2, file-scoped namespaces, usings inside, `var`, `_camelCase` fields.
- Branch: `fix/valuedictionary-filter-cursor` off `main`.
- Full `dotnet test Prague.Tests.slnf` green on net9.0 + net10.0 before PR.

---

### Task 1: Delete the delegate `Filter` overload (it reads the wrong element)

**Files:**
- Modify: `src/Prague.Core/Collections/ValueDictionary.cs:224-255` (delete method)
- Test: `tests/Prague.Core.Tests/DataStructures/ValueDictionaryFilterTests.cs` (create)

**Interfaces:**
- Consumes: `ValueDictionary<TKey,TValue,TKeyComparer>(bool shouldPool, int expectedCount)`, `Add(TKey, TValue)`, `internal void Filter<TFilter>(TFilter) where TFilter : struct, IValueDictionaryFilter<TKey,TValue>`
- Produces: nothing new — removes `public void Filter<TArg>(Predicate<TArg> predicate, TArg arg)`.

Background: `Filter<TArg>` (line 233) passes `ref Unsafe.Add(ref valuesRef, write)` — the **write** cursor — to the predicate while testing the key at **read**; once a row has been dropped (`write < read`) the predicate inspects the wrong element's value. The struct overload (line 201) correctly uses `read`. All production callers (`JoinResults.generated.cs` RetainNonNullSlots/RetainNonEmptyManySlots families) use the struct overload.

- [ ] **Step 1: Confirm the delegate overload has zero callers**

Run:
```bash
grep -rn "Filter(" src tests benchmarks --include="*.cs" | grep -v obj | grep -v "new Right\|new .*NonNullFilter\|new .*NonEmptyManyFilter" | grep -i "predicate\|, arg"
grep -rn "delegate.*Predicate" src/Prague.Core --include="*.cs" | grep -v obj
```
Expected: no call sites of `Filter<TArg>(Predicate<TArg>, TArg)`; note where the `Predicate` delegate type is declared.

**Fallback:** if a caller IS found, do NOT delete — instead change line 233's `ref Unsafe.Add(ref valuesRef, write)` to `ref Unsafe.Add(ref valuesRef, read)`, delete the stale `// set.Contains(...)` comment, and keep Steps 2-5's test as the regression test for that caller's path.

- [ ] **Step 2: Write the cursor-semantics regression test (pins the struct overload)**

Create `tests/Prague.Core.Tests/DataStructures/ValueDictionaryFilterTests.cs`:

```csharp
namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ValueDictionaryFilterTests {
	private readonly struct DropEvensAndVerifyPairing : IValueDictionaryFilter<int, string> {
		public bool Keep(int key, ref string value) {
			// The value handed in must be THE value stored under `key` — after a drop has
			// occurred, a wrong-cursor implementation hands us a stale/duplicate row here.
			Assert.That(value, Is.EqualTo($"v{key}"), "filter saw a value that does not belong to its key");
			return key % 2 != 0;
		}
	}

	[Test]
	public void Filter_AfterDrops_PredicateAlwaysSeesTheValueBelongingToItsKey() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 16);
		for (var i = 0; i < 10; i++)
			dict.Add(i, $"v{i}");

		dict.Filter(new DropEvensAndVerifyPairing());

		Assert.That(dict.Count, Is.EqualTo(5));
		foreach (var key in dict.Keys.ToArray())
			Assert.That(key % 2, Is.Not.Zero);
		dict.Dispose(true);
	}
}
```

Note: if `IValueDictionaryFilter.Keep`'s signature differs (check `src/Prague.Core/Collections/ValueDictionary.cs` for the interface declaration), adapt the struct to the exact signature; the assertion inside `Keep` is the point of the test.

- [ ] **Step 3: Run it — expect PASS (struct overload is correct; this pins it)**

```bash
dotnet test tests/Prague.Core.Tests -f net9.0 --filter "FullyQualifiedName~ValueDictionaryFilterTests" --nologo
```
Expected: PASS. (If it fails, stop — the struct overload has the same bug and the fix is `read` for the value ref, then re-run.)

- [ ] **Step 4: Delete `Filter<TArg>(Predicate<TArg>, TArg)` (ValueDictionary.cs:224-255) and, if declared in the same file and now unused, the `Predicate<TArg>` delegate declaration**

- [ ] **Step 5: Build + full Core tests**

```bash
dotnet build src/Prague.Core/Prague.Core.csproj --nologo -v q
dotnet test tests/Prague.Core.Tests --nologo
```
Expected: 0 errors, all green both TFMs (742+ passed).

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/Collections/ValueDictionary.cs tests/Prague.Core.Tests/DataStructures/ValueDictionaryFilterTests.cs
git commit -m "Remove wrong-cursor delegate Filter overload; pin struct-filter cursor semantics"
```

---

### Task 2: Debug-only guards for the two corruption windows

**Files:**
- Modify: `src/Prague.Core/Collections/ValueDictionary.cs` (`Add`, ~line 51)
- Modify: `src/Prague.Core/Collections/ValueSet.cs` (`Add` → `AddIfNotPresent` entry, near line 129)

**Interfaces:**
- Consumes: existing `Debug.Assert` pattern already present in `ValueDictionary.Add` (line 52).
- Produces: no API change; Debug builds fail fast instead of corrupting memory.

- [ ] **Step 1: Strengthen `ValueDictionary.Add`'s existing assert message and add a metadata-alive assert**

In `ValueDictionary.Add`, immediately after the existing capacity assert (line 52), add:

```csharp
		Debug.Assert(_metadata != null, "ValueDictionary used after Dispose");
```

(The existing `Debug.Assert(Count < _values.Length, "ValueDictionary capacity exceeded")` stays; both compile out in Release.)

- [ ] **Step 2: Add the use-after-dispose assert to `ValueSet.Add`**

In `ValueSet.Add(T item)` (line 129, expression-bodied to `AddIfNotPresent`): convert to a block body if needed and add at the top:

```csharp
	public bool Add(T item) {
		// _size == 0 after Dispose while IsInitlized stays true: FastMod with size 0 produces a
		// garbage bucket and Unsafe.Add writes out of bounds. Debug-only by design (2026-07-20).
		Debug.Assert(!IsInitlized || _size > 0, "ValueSet used after Dispose");
		return AddIfNotPresent(item);
	}
```

Check the actual guard condition against the file first: the dispose path sets `_size = 0` (ValueSet.cs:122) — if `_size` is also legitimately 0 pre-initialization, gate the assert on the same field `Dispose` clears so a fresh set does not trip it (e.g. assert `_valuesArray != null || _lastIndex == 0` — pick the invariant that distinguishes "fresh" from "disposed" after reading lines 100-130).

- [ ] **Step 3: Build Debug + Release, run Core tests (Debug default)**

```bash
dotnet build src/Prague.Core/Prague.Core.csproj --nologo -v q
dotnet build src/Prague.Core/Prague.Core.csproj -c Release --nologo -v q
dotnet test tests/Prague.Core.Tests --nologo
```
Expected: both builds clean, tests green — proving no legitimate path trips the asserts.

- [ ] **Step 4: Commit**

```bash
git add src/Prague.Core/Collections/ValueDictionary.cs src/Prague.Core/Collections/ValueSet.cs
git commit -m "Debug-only fail-fast for use-after-dispose and over-capacity Add"
```

---

### Task 3: PR

- [ ] **Step 1: Full suite both TFMs**

```bash
dotnet test Prague.Tests.slnf --nologo
```
Expected: 0 failures across Core/Generated/Kafka/Integration on net9.0 + net10.0.

- [ ] **Step 2: Push and open PR**

```bash
git push -u origin fix/valuedictionary-filter-cursor
gh pr create --title "ValueDictionary correctness: remove wrong-cursor Filter overload, Debug fail-fast guards" --body "See docs/superpowers/plans/2026-07-20-pr1-valuedictionary-correctness.md. No Release-mode behavior or perf change (deletion + Debug.Assert only).

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

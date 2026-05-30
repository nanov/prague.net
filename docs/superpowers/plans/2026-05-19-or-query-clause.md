# Or-Query Clause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a two-branch `.Or(b1, b2)` disjunction clause to the fluent query API on `CacheQueryBuilderCombined`, polymorphic over the executor (unpaired and paired narrowing cores), with a `NarrowOnlyQuery<TCache>` discriminator that restricts each branch to index-narrowing only.

**Architecture:** Discriminator hierarchy gets a new `IIndexNarrower` base (extracted under `IBaseFilterable`); `UseIndex`/`WithXxx` constraints migrate to it. Executors that participate in `Or` implement `IOrCapable<TSelf>` (with `CreateBranch` + `OrWith`). Two new `ValueSet<T>` ref-ref primitives (`IntersectWith` and `UnionWith` taking two ref ValueSets) drive the actual merge — the union path is mark-and-sweep via `IncrementalIntersecter<T, …>` already in the codebase. The `.Or` extension methods are thin plumbing.

**Tech Stack:** .NET 9, C# 13, NUnit (Core.Tests and Generated.Tests), Roslyn source generator for codegen `WithXxx` constraint flip.

**Spec:** `docs/superpowers/specs/2026-05-19-or-query-clause-design.md`

---

## File Structure

**New files (under `src/Prague.Core/`):**
- `TypeSystem/IIndexNarrower.cs` — new marker interface (base of `IBaseFilterable`).
- `TypeSystem/NarrowOnlyQuery.cs` — discriminator struct for `Or` branches.
- `QueryBuilders/IOrCapable.cs` — `IOrCapable<TSelf>` interface declaration.
- `QueryBuilders/CacheQueryBuilder.Or.Extensions.cs` — `.Or(b1, b2)` + `.Or(b1, b2, arg)` extension overloads.

**Modified files:**
- `src/Prague.Core/TypeSystem/QueryDiscriminator.cs` — `IBaseFilterable` extends `IIndexNarrower`.
- `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` — flip `UseIndex` constraint to `IIndexNarrower`; add `IOrCapable<…>` impl to `CacheQueryBuilderCoreCombined<TKey, TValue>` and `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>`.
- `src/Prague.Core/Collections/ValueSet.cs` — add `IntersectWith(ref v1, ref v2)` and `UnionWith(ref v1, ref v2)` overloads.
- `src/Prague.Codegen/CacheGenerator.cs` — flip emitted `WithXxx` constraint from `IBaseFilterable` to `IIndexNarrower`.

**Test files (NUnit):**
- `tests/Prague.Core.Tests/Collections/ValueSetOrPrimitivesTests.cs` — ValueSet ref-ref overloads.
- `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs` — unpaired `Or` end-to-end via raw `UseIndex`.
- `tests/Prague.Core.Tests/Query/OrClausePairedCoreTests.cs` — paired core `IOrCapable` smoke.
- `tests/Prague.Generated.Tests/Query/OrClauseGeneratedTests.cs` — `Or` with codegen `WithXxx` on a `[DataCache]` model.

---

## Task 1: Add `IIndexNarrower` interface

**Files:**
- Create: `src/Prague.Core/TypeSystem/IIndexNarrower.cs`
- Modify: `src/Prague.Core/TypeSystem/QueryDiscriminator.cs` (line 4)

- [ ] **Step 1: Create the new marker interface**

```csharp
namespace Prague.Core.TypeSystem;

/// <summary>
/// Marker for discriminators that admit candidate-narrowing extensions
/// (UseIndex, WithXxx, Or). Base of <see cref="IBaseFilterable"/> — every
/// IBaseFilterable discriminator is also an IIndexNarrower, so existing
/// call sites keep working transitively.
/// </summary>
public interface IIndexNarrower { }
```

- [ ] **Step 2: Refactor `IBaseFilterable` to extend it**

Open `src/Prague.Core/TypeSystem/QueryDiscriminator.cs` and replace:
```csharp
public interface IBaseFilterable { }
```
with:
```csharp
public interface IBaseFilterable : IIndexNarrower { }
```

- [ ] **Step 3: Build to verify no source-level breakage**

Run: `dotnet build Prague.sln`
Expected: PASS (existing discriminators `ExecutableQuery<TCache>` and `NonExecutableQuery<TCache>` already implement `IBaseFilterable`, so they pick up `IIndexNarrower` transitively).

- [ ] **Step 4: Commit**

```bash
git add src/Prague.Core/TypeSystem/IIndexNarrower.cs \
        src/Prague.Core/TypeSystem/QueryDiscriminator.cs
git commit -m "$(cat <<'EOF'
refactor(typesystem): extract IIndexNarrower as base of IBaseFilterable

Non-breaking — every existing IBaseFilterable discriminator transitively
gains IIndexNarrower. Subsequent commits will migrate UseIndex/WithXxx
extension constraints down to the new base and add NarrowOnlyQuery for
Or-branch builders.
EOF
)"
```

---

## Task 2: Migrate `UseIndex` extension constraints from `IBaseFilterable` to `IIndexNarrower`

**Files:**
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` (every `UseIndex` overload — start at line 1263; there are several overloads for single-value, multi-value span, multi-value `ref ValueSet`, with keySelector, etc.)

- [ ] **Step 1: Find every `UseIndex` extension that constrains on `IBaseFilterable`**

Run:
```bash
grep -n "where TDiscriminator : struct, IBaseFilterable" \
    src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs
```
Expected: a list of line numbers. Note each one — most or all should be `UseIndex` overloads (the `Where` extension also uses this constraint and is intentionally NOT migrated).

- [ ] **Step 2: For each `UseIndex` overload, change `IBaseFilterable` to `IIndexNarrower`**

Inspect each match from Step 1. If the surrounding method name starts with `UseIndex`, edit the constraint:
```csharp
// before:
where TDiscriminator : struct, IBaseFilterable
// after:
where TDiscriminator : struct, IIndexNarrower
```
Leave `Where` extensions untouched (they belong on `IBaseFilterable`).

- [ ] **Step 3: Add `using Prague.Core.TypeSystem;` if not already imported**

Look at the top of `CacheQueryBuilder.cs`. The `using` block lives inside the namespace per `.editorconfig`. Add the line if it's missing.

- [ ] **Step 4: Build to verify all existing call sites still compile**

Run: `dotnet build Prague.sln`
Expected: PASS. Because `IBaseFilterable : IIndexNarrower`, every existing `where TDiscriminator : IBaseFilterable` constraint on callers is still satisfied for any discriminator that satisfies `IIndexNarrower`. The migration is non-breaking.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test Prague.Tests.slnf`
Expected: all green (no behavior change).

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs
git commit -m "refactor(queries): migrate UseIndex constraints to IIndexNarrower"
```

---

## Task 3: Migrate codegen `WithXxx` constraints from `IBaseFilterable` to `IIndexNarrower`

**Files:**
- Modify: `src/Prague.Codegen/CacheGenerator.cs` — locate the template string that emits the `where TDiscriminator : struct, IBaseFilterable` constraint on `WithXxx` extensions.

- [ ] **Step 1: Find the constraint string in the generator template**

Run:
```bash
grep -n "IBaseFilterable" src/Prague.Codegen/CacheGenerator.cs
```
Note each line. Generator output strings typically appear inside `$"…"` interpolated string literals or string builders that emit method declarations. You need the one(s) that appear inside the emission of `WithXxx`/`UseIndex`-style extension methods.

- [ ] **Step 2: Replace `IBaseFilterable` with `IIndexNarrower` in those template strings**

For each match that appears inside generator emission code for index-narrowing extensions (`WithXxx`, generated UseIndex helpers), swap the literal `IBaseFilterable` → `IIndexNarrower`.

Be careful not to touch any reference to `IBaseFilterable` outside the generator template strings — e.g., comments or non-generator type references should stay.

- [ ] **Step 3: Build the generator and re-build consumers**

Run: `dotnet build Prague.sln`
Expected: PASS. The generator now emits `IIndexNarrower` constraints; consumers regenerate `*.generated.cs` files; those files compile because `ExecutableQuery<TCache>` etc. all satisfy `IIndexNarrower` transitively.

- [ ] **Step 4: Run all generated tests**

Run: `dotnet test tests/Prague.Generated.Tests`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Codegen/CacheGenerator.cs
git commit -m "refactor(codegen): emit IIndexNarrower constraint on WithXxx"
```

---

## Task 4: Add `NarrowOnlyQuery<TCache>` discriminator

**Files:**
- Create: `src/Prague.Core/TypeSystem/NarrowOnlyQuery.cs`

- [ ] **Step 1: Create the discriminator struct**

```csharp
namespace Prague.Core.TypeSystem;

/// <summary>
/// Discriminator used inside Or-branch builders. Implements only the
/// markers that admit candidate-narrowing extensions (UseIndex, WithXxx,
/// nested Or). Deliberately does NOT implement IBaseFilterable, IBaseJoinable,
/// IInnerJoinable, ISortable, IExecutableQuery — so Where, Join, Sort, and
/// Execute* are compile-unreachable inside an Or branch.
/// </summary>
public readonly struct NarrowOnlyQuery<TCache> : IIndexNarrower, ICacheCarrier<TCache>
{
	public TCache Cache { get; }

	public NarrowOnlyQuery(TCache cache) {
		Cache = cache;
	}
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Prague.sln`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Core/TypeSystem/NarrowOnlyQuery.cs
git commit -m "feat(typesystem): add NarrowOnlyQuery<TCache> discriminator for Or branches"
```

---

## Task 5: Add `ValueSet<T>.IntersectWith(ref v1, ref v2)` primitive — failing test

**Files:**
- Test: `tests/Prague.Core.Tests/Collections/ValueSetOrPrimitivesTests.cs`

- [ ] **Step 1: Create the failing test**

```csharp
namespace Prague.Core.Tests.Collections;

using NUnit.Framework;
using Prague.Core.Collections;

[TestFixture]
public class ValueSetOrPrimitivesTests
{
	[Test]
	public void IntersectWith_TwoRefs_KeepsElementsInUnion() {
		var self = new ValueSet<int>();
		foreach (var v in new[] { 1, 2, 3, 4, 5 }) self.Add(v);

		var v1 = new ValueSet<int>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);

		var v2 = new ValueSet<int>();
		foreach (var v in new[] { 5, 9 }) v2.Add(v);

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(3));
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);
		Assert.That(self.Contains(5), Is.True);
		Assert.That(self.Contains(1), Is.False);
		Assert.That(self.Contains(4), Is.False);
		Assert.That(self.Contains(9), Is.False);

		self.Dispose();
		v1.Dispose();
		v2.Dispose();
	}

	[Test]
	public void IntersectWith_TwoRefs_OneUninitialized_KeepsElementsInOther() {
		var self = new ValueSet<int>();
		foreach (var v in new[] { 1, 2, 3, 4 }) self.Add(v);

		var v1 = new ValueSet<int>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);

		var v2 = default(ValueSet<int>); // uninitialized

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(2));
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);

		self.Dispose();
		v1.Dispose();
	}

	[Test]
	public void IntersectWith_TwoRefs_BothUninitialized_LeavesSelfUnchanged() {
		var self = new ValueSet<int>();
		foreach (var v in new[] { 1, 2, 3 }) self.Add(v);
		var beforeCount = self.Count;

		var v1 = default(ValueSet<int>);
		var v2 = default(ValueSet<int>);

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(beforeCount));
		self.Dispose();
	}
}
```

- [ ] **Step 2: Run — expected to fail (compile error)**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~ValueSetOrPrimitivesTests`
Expected: FAIL with "no method `IntersectWith` taking `(ref ValueSet<int>, ref ValueSet<int>)`".

- [ ] **Step 3: Commit failing test**

```bash
git add tests/Prague.Core.Tests/Collections/ValueSetOrPrimitivesTests.cs
git commit -m "test(valueset): failing test for IntersectWith(ref, ref) overload"
```

---

## Task 6: Implement `ValueSet<T>.IntersectWith(ref v1, ref v2)`

**Files:**
- Modify: `src/Prague.Core/Collections/ValueSet.cs` (near the existing single-arg `IntersectWith` at line 320)

- [ ] **Step 1: Add the new overload**

Inspect the existing `IntersectWith<TOther, TInto>(TInto, ref ValueSet<TOther>)` and the `IncrementalIntersecter<TFrom, TInto>` ref struct (around line 1185). Add a same-type sibling that takes two ref ValueSets and marks via both:

```csharp
public void IntersectWith(ref ValueSet<T> v1, ref ValueSet<T> v2) {
	if (!v1.IsInitlized && !v2.IsInitlized) return;
	if (Count == 0) return;
	using var intersecter = new IncrementalIntersecter<T, IdentityInto>(ref this);
	if (v1.IsInitlized) MarkAll(ref intersecter, ref v1);
	if (v2.IsInitlized) MarkAll(ref intersecter, ref v2);
}

private static void MarkAll(ref IncrementalIntersecter<T, IdentityInto> intersecter, ref ValueSet<T> other) {
	foreach (var item in other) intersecter.IntersectWith(item);
}
```

If the existing intersecter API differs (e.g., the method is named `Mark` or `Hit` instead of `IntersectWith`), adapt the helper. If an `IdentityInto` trait does not exist in this file, search for the existing same-type intersect helper and use its trait type:
```bash
grep -n "IInto" src/Prague.Core/Collections/ValueSet.cs | head -20
```

- [ ] **Step 2: Run the test from Task 5**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~ValueSetOrPrimitivesTests`
Expected: PASS (all three tests).

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Core/Collections/ValueSet.cs
git commit -m "feat(valueset): IntersectWith(ref v1, ref v2) — keeps elements in v1 OR v2"
```

---

## Task 7: Add `ValueSet<T>.UnionWith(ref v1, ref v2)` — failing test

**Files:**
- Modify: `tests/Prague.Core.Tests/Collections/ValueSetOrPrimitivesTests.cs`

- [ ] **Step 1: Add the failing test**

Append to the test class:
```csharp
[Test]
public void UnionWith_TwoRefs_AddsBoth() {
	var self = new ValueSet<int>();
	self.Add(1);

	var v1 = new ValueSet<int>();
	foreach (var v in new[] { 2, 3 }) v1.Add(v);

	var v2 = new ValueSet<int>();
	foreach (var v in new[] { 3, 4 }) v2.Add(v);

	self.UnionWith(ref v1, ref v2);

	Assert.That(self.Count, Is.EqualTo(4));
	Assert.That(self.Contains(1), Is.True);
	Assert.That(self.Contains(2), Is.True);
	Assert.That(self.Contains(3), Is.True);
	Assert.That(self.Contains(4), Is.True);

	self.Dispose();
	v1.Dispose();
	v2.Dispose();
}

[Test]
public void UnionWith_TwoRefs_OneUninitialized_AddsOther() {
	var self = new ValueSet<int>();
	var v1 = new ValueSet<int>();
	foreach (var v in new[] { 2, 3 }) v1.Add(v);
	var v2 = default(ValueSet<int>);

	self.UnionWith(ref v1, ref v2);

	Assert.That(self.Count, Is.EqualTo(2));
	Assert.That(self.Contains(2), Is.True);
	Assert.That(self.Contains(3), Is.True);

	self.Dispose();
	v1.Dispose();
}

[Test]
public void UnionWith_TwoRefs_BothUninitialized_NoOp() {
	var self = new ValueSet<int>();
	self.Add(1);
	var v1 = default(ValueSet<int>);
	var v2 = default(ValueSet<int>);

	self.UnionWith(ref v1, ref v2);

	Assert.That(self.Count, Is.EqualTo(1));
	self.Dispose();
}
```

- [ ] **Step 2: Run — expected to fail**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~ValueSetOrPrimitivesTests`
Expected: 3 new failing tests.

- [ ] **Step 3: Commit failing test**

```bash
git add tests/Prague.Core.Tests/Collections/ValueSetOrPrimitivesTests.cs
git commit -m "test(valueset): failing test for UnionWith(ref, ref) overload"
```

---

## Task 8: Implement `ValueSet<T>.UnionWith(ref v1, ref v2)`

**Files:**
- Modify: `src/Prague.Core/Collections/ValueSet.cs` (next to existing `UnionWith(ref ValueSet<T>)` at line 293)

- [ ] **Step 1: Add overload**

```csharp
public void UnionWith(ref ValueSet<T> v1, ref ValueSet<T> v2) {
	if (v1.IsInitlized) UnionWith(ref v1);
	if (v2.IsInitlized) UnionWith(ref v2);
}
```

Simple delegation to the existing single-arg `UnionWith`. The existing single-arg should already handle the "self is uninitialized" case (per the codebase convention) — if it doesn't, follow the convention used by Add/UnionWith elsewhere to lazily initialize.

- [ ] **Step 2: Run the Task 7 tests**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~ValueSetOrPrimitivesTests`
Expected: all six tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Core/Collections/ValueSet.cs
git commit -m "feat(valueset): UnionWith(ref v1, ref v2) overload"
```

---

## Task 9: Add `IOrCapable<TSelf>` interface

**Files:**
- Create: `src/Prague.Core/QueryBuilders/IOrCapable.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Prague.Core.QueryBuilders;

/// <summary>
/// Executor capability for the Or clause. Implementers expose:
///   * CreateBranch() — returns a fresh sibling executor that shares
///     inert state (data cache, filter) but starts with empty candidates.
///     The branch lambda then narrows it via WithXxx / UseIndex / nested Or.
///   * OrWith(in branch1, in branch2) — merges the two branch executors'
///     candidate sets into self. Branch candidate storage is disposed by
///     the implementation.
/// </summary>
public interface IOrCapable<TSelf> where TSelf : struct, IOrCapable<TSelf>
{
	TSelf CreateBranch();
	void OrWith(in TSelf branch1, in TSelf branch2);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Prague.sln`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Core/QueryBuilders/IOrCapable.cs
git commit -m "feat(queries): IOrCapable<TSelf> — executor capability for Or"
```

---

## Task 10: Implement `IOrCapable<…>` on `CacheQueryBuilderCoreCombined<TKey, TValue>` — failing tests

**Files:**
- Test: `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs`

- [ ] **Step 1: Create the test file with three failing tests**

```csharp
namespace Prague.Core.Tests.Query;

using NUnit.Framework;
using Prague.Core;
using Prague.Core.QueryBuilders;

[TestFixture]
public class OrClauseUnpairedCoreTests
{
	// Use the existing minimal POCO test model from JoinOneNewCoreTests if it
	// exists; otherwise mirror its setup. Replace `TestKey`/`TestValue` below
	// with whatever the existing core-test convention is.

	[Test]
	public void OrWith_FirstFalse_IntersectsAgainstUnion() {
		// Setup: outer with narrowed Candidates {1,2,3,4,5}
		// branch1 candidates {2,3}, branch2 candidates {5,9}
		// Expected after OrWith: {2,3,5}
		// Implementation: instantiate CacheQueryBuilderCoreCombined<TKey,TValue>,
		// seed Candidates manually via UseIndex or direct field access, call OrWith.
		Assert.Fail("TODO: implement once IOrCapable lands on the core");
	}

	[Test]
	public void OrWith_FirstTrue_UnionsBranches() {
		// Setup: outer with _first=true, Candidates uninitialized.
		// branch1 candidates {1,2}, branch2 candidates {3,4}.
		// Expected: outer.Candidates = {1,2,3,4}, _first = false.
		Assert.Fail("TODO");
	}

	[Test]
	public void OrWith_BothBranchesUninitialized_NoOp() {
		// Setup: outer with _first=true, branches both uninitialized.
		// Expected: outer unchanged.
		Assert.Fail("TODO");
	}
}
```

> **Note for the executing engineer:** because `Candidates` and `_first` are `private` on `CacheQueryBuilderCoreCombined`, the Core test project needs either `InternalsVisibleTo` or test seams. Per CLAUDE.md: "InternalsVisibleTo: Core exposes internals to *.Tests" — so flipping `Candidates` and `_first` to `internal` (one-line change in `CacheQueryBuilder.cs:21`) is the cleanest path. Do that as part of this task.

- [ ] **Step 2: Flip `Candidates` and `_first` from `private` to `internal` on `CacheQueryBuilderCoreCombined`**

Edit `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` around line 17–21:
```csharp
// before:
private Predicate<TValue>? _filter;
private bool _disposed;
private bool _first;
private ValueSet<TKey> Candidates;
// after:
internal Predicate<TValue>? _filter;
internal bool _disposed;
internal bool _first;
internal ValueSet<TKey> Candidates;
```

- [ ] **Step 3: Fill in the test bodies**

Replace each `Assert.Fail("TODO")` body with the actual setup-act-assert. Construct the executor directly:
```csharp
var cache = TestCacheFactory.Create(/* seed data so KeyIndex has the keys you want */);
var outer = new CacheQueryBuilderCoreCombined<int, TestValue>(cache);
// Manually set candidates for the "first=false" test:
outer.Candidates = new ValueSet<int>();
foreach (var k in new[] { 1, 2, 3, 4, 5 }) outer.Candidates.Add(k);
outer._first = false;

var branch1 = outer.CreateBranch();
branch1.Candidates = new ValueSet<int>();
foreach (var k in new[] { 2, 3 }) branch1.Candidates.Add(k);
branch1._first = false;

var branch2 = outer.CreateBranch();
branch2.Candidates = new ValueSet<int>();
foreach (var k in new[] { 5, 9 }) branch2.Candidates.Add(k);
branch2._first = false;

outer.OrWith(in branch1, in branch2);

Assert.That(outer.Candidates.Count, Is.EqualTo(3));
Assert.That(outer.Candidates.Contains(2), Is.True);
Assert.That(outer.Candidates.Contains(3), Is.True);
Assert.That(outer.Candidates.Contains(5), Is.True);
```

Adapt similarly for the other two tests. Use whatever `TestCacheFactory` / model exists in Core.Tests for `InMemoryDataCache<TKey, TValue>` setup; otherwise borrow from `JoinOneNewCoreTests`.

- [ ] **Step 4: Run — expected to fail**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~OrClauseUnpairedCoreTests`
Expected: FAIL — `CreateBranch` and `OrWith` not defined on `CacheQueryBuilderCoreCombined`.

- [ ] **Step 5: Commit failing tests**

```bash
git add tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs \
        src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs
git commit -m "test(or): failing tests for unpaired core IOrCapable impl"
```

---

## Task 11: Implement `IOrCapable<…>` on `CacheQueryBuilderCoreCombined`

**Files:**
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` (around `CacheQueryBuilderCoreCombined` declaration at line 11)

- [ ] **Step 1: Add the interface to the struct's declaration**

```csharp
// before:
public struct CacheQueryBuilderCoreCombined<TKey, TValue>
	: IDisposable, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
// after:
public struct CacheQueryBuilderCoreCombined<TKey, TValue>
	: IDisposable, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>,
	  IOrCapable<CacheQueryBuilderCoreCombined<TKey, TValue>>
```

- [ ] **Step 2: Implement `CreateBranch` and `OrWith` as members of the struct**

Append the two methods inside the struct body:
```csharp
public CacheQueryBuilderCoreCombined<TKey, TValue> CreateBranch() {
	var branch = this;          // shallow copy of struct fields (inert references reused)
	branch.Candidates = default;
	branch._first = true;
	branch._disposed = false;
	return branch;
}

public void OrWith(
	in CacheQueryBuilderCoreCombined<TKey, TValue> branch1,
	in CacheQueryBuilderCoreCombined<TKey, TValue> branch2)
{
	// `in` rules out direct field mutation on branch1/branch2; use Unsafe.AsRef
	// just like the existing in-parameter idioms in this file (search for
	// `Unsafe.AsRef(in ` to confirm the pattern).
	ref var b1 = ref System.Runtime.CompilerServices.Unsafe.AsRef(in branch1);
	ref var b2 = ref System.Runtime.CompilerServices.Unsafe.AsRef(in branch2);
	try {
		var b1Init = b1.Candidates.IsInitlized;
		var b2Init = b2.Candidates.IsInitlized;
		if (!b1Init && !b2Init) return;            // both no-op → entire Or no-op
		if (_first) {
			Candidates.UnionWith(ref b1.Candidates, ref b2.Candidates);
			_first = false;
		} else {
			Candidates.IntersectWith(ref b1.Candidates, ref b2.Candidates);
		}
	} finally {
		if (b1.Candidates.IsInitlized) b1.Candidates.Dispose();
		if (b2.Candidates.IsInitlized) b2.Candidates.Dispose();
	}
}
```

- [ ] **Step 3: Run the Task 10 tests**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~OrClauseUnpairedCoreTests`
Expected: all three pass.

- [ ] **Step 4: Run full Core.Tests to confirm no regressions**

Run: `dotnet test tests/Prague.Core.Tests`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs
git commit -m "feat(queries): IOrCapable impl on CacheQueryBuilderCoreCombined"
```

---

## Task 12: Add `Or` extension (identity, two-branch) — failing test

**Files:**
- Test: `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs` — append

- [ ] **Step 1: Append the end-to-end fluent-API test**

```csharp
[Test]
public void Or_EndToEnd_NarrowsBeforeOr_IntersectsWithUnion() {
	var cache = TestCacheFactory.Create(/* keys 1..10 etc. */);

	// Seed cache so:
	// - keys with Country=12: {1, 2, 3, 4}
	// - keys with City=1: {1, 5}
	// - keys with City=2: {2, 6}

	using var result = cache.Query()
		.UseIndex(cache.CountryIndex, 12)
		.Or(
			b => b.UseIndex(cache.CityIndex, 1),
			b => b.UseIndex(cache.CityIndex, 2))
		.Execute();

	// outer narrowed to {1,2,3,4} ∩ ({1,5} ∪ {2,6}) = {1,2}
	Assert.That(result.Count, Is.EqualTo(2));
	Assert.That(result.Keys, Is.EquivalentTo(new[] { 1, 2 }));
}
```

> Adapt `cache.CountryIndex` / `cache.CityIndex` / `TestCacheFactory.Create` to whatever the existing Core.Tests test fixture provides. If no suitable POCO model exists, mirror the setup pattern from `tests/Prague.Core.Tests/Join/JoinOneNewCoreTests.cs` and seed an `InMemoryDataCache<int, MinimalValue>` directly with `AddKeyValueIndex` for the two indexes.

- [ ] **Step 2: Run — expected to fail**

Run: `dotnet test tests/Prague.Core.Tests --filter Or_EndToEnd_NarrowsBeforeOr`
Expected: FAIL — `Or` extension does not exist.

- [ ] **Step 3: Commit failing test**

```bash
git add tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs
git commit -m "test(or): failing end-to-end Or test"
```

---

## Task 13: Implement `Or(b1, b2)` extension

**Files:**
- Create: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.Or.Extensions.cs`

- [ ] **Step 1: Create the extension class**

```csharp
namespace Prague.Core.QueryBuilders;

using Prague.Core.TypeSystem;

public static class CacheQueryBuilderCombinedOrExtensions
{
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult, TCache>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				 CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				 CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2)
		where TDiscriminator : struct, IIndexNarrower, ICacheCarrier<TCache>
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	{
		ref var outer = ref System.Runtime.CompilerServices.Unsafe.AsRef(in source);

		var b1Exec = outer._leftQuery.CreateBranch();
		var b2Exec = outer._leftQuery.CreateBranch();
		var ok = false;
		try {
			var b1Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>(
				new NarrowOnlyQuery<TCache>(outer._discriminator.Cache),
				b1Exec,
				outer._resolverChain,
				outer._manyCount);
			var b2Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>(
				new NarrowOnlyQuery<TCache>(outer._discriminator.Cache),
				b2Exec,
				outer._resolverChain,
				outer._manyCount);

			b1Builder = b1(b1Builder);
			b2Builder = b2(b2Builder);

			outer._leftQuery.OrWith(in b1Builder._leftQuery, in b2Builder._leftQuery);
			ok = true;
		} finally {
			if (!ok) {
				if (b1Exec.Candidates.IsInitlized) b1Exec.Candidates.Dispose();
				if (b2Exec.Candidates.IsInitlized) b2Exec.Candidates.Dispose();
			}
		}
		return outer;
	}
}
```

> **Notes:**
> - The constructor of `CacheQueryBuilderCombined<...>` takes `(TDiscriminator, TLeftQuery, TResolverChain, int manyCount)` per the existing generated `Query()` shape. Confirm by reading `CacheQueryBuilder.cs:1141–1152`.
> - `outer._discriminator.Cache` works because `TDiscriminator : ICacheCarrier<TCache>`.
> - `outer._leftQuery` is the internal field name for the executor slot (NOT `Executor`).

- [ ] **Step 2: Run the Task 12 test**

Run: `dotnet test tests/Prague.Core.Tests --filter Or_EndToEnd_NarrowsBeforeOr`
Expected: PASS.

- [ ] **Step 3: Run full Core.Tests**

Run: `dotnet test tests/Prague.Core.Tests`
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.Or.Extensions.cs
git commit -m "feat(queries): Or(b1, b2) extension on the combined builder"
```

---

## Task 14: Add `Or(b1, b2, arg)` `TArg` overload — failing test + impl

**Files:**
- Test: `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs` — append
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.Or.Extensions.cs`

- [ ] **Step 1: Append failing test**

```csharp
[Test]
public void Or_TArg_PassesStateToBothBranches() {
	var cache = TestCacheFactory.Create();
	var state = (CityA: 1, CityB: 2);

	using var result = cache.Query()
		.UseIndex(cache.CountryIndex, 12)
		.Or(
			static (b, s) => b.UseIndex(b._discriminator.Cache.CityIndex, s.CityA),
			static (b, s) => b.UseIndex(b._discriminator.Cache.CityIndex, s.CityB),
			state);

	Assert.That(result.Count, Is.EqualTo(2));
}
```

> Adjust how the index is accessed via `b._discriminator.Cache` to whatever the discriminator's `Cache` property exposes in the test fixture.

- [ ] **Step 2: Run — expected to fail**

Run: `dotnet test tests/Prague.Core.Tests --filter Or_TArg_PassesStateToBothBranches`
Expected: FAIL.

- [ ] **Step 3: Implement the `TArg` overload**

Append to `CacheQueryBuilder.Or.Extensions.cs`:
```csharp
public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
	Or<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult, TCache, TArg>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> source,
		Func<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg,
			 CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
		Func<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg,
			 CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2,
		TArg arg)
	where TDiscriminator : struct, IIndexNarrower, ICacheCarrier<TCache>
	where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TExecutor>
	where TResolverChain : struct, IResolvers
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
{
	ref var outer = ref System.Runtime.CompilerServices.Unsafe.AsRef(in source);
	var b1Exec = outer._leftQuery.CreateBranch();
	var b2Exec = outer._leftQuery.CreateBranch();
	var ok = false;
	try {
		var b1Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>(
			new NarrowOnlyQuery<TCache>(outer._discriminator.Cache),
			b1Exec, outer._resolverChain, outer._manyCount);
		var b2Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>(
			new NarrowOnlyQuery<TCache>(outer._discriminator.Cache),
			b2Exec, outer._resolverChain, outer._manyCount);

		b1Builder = b1(b1Builder, arg);
		b2Builder = b2(b2Builder, arg);

		outer._leftQuery.OrWith(in b1Builder._leftQuery, in b2Builder._leftQuery);
		ok = true;
	} finally {
		if (!ok) {
			if (b1Exec.Candidates.IsInitlized) b1Exec.Candidates.Dispose();
			if (b2Exec.Candidates.IsInitlized) b2Exec.Candidates.Dispose();
		}
	}
	return outer;
}
```

- [ ] **Step 4: Run all Or tests**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~OrClauseUnpairedCoreTests`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs \
        src/Prague.Core/QueryBuilders/CacheQueryBuilder.Or.Extensions.cs
git commit -m "feat(queries): Or<TArg> overload for zero-alloc static-lambda branches"
```

---

## Task 15: Edge-case tests — outer `_first == true`, nested `Or`, no-op branch

**Files:**
- Modify: `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs`

- [ ] **Step 1: Append three more tests**

```csharp
[Test]
public void Or_OuterFirstTrue_SeedsFromBranchesUnion() {
	// Query().Or(b => b.UseIndex(City, 1), b => b.UseIndex(City, 2))
	// — no prior narrowing. Expect outer.Candidates = {1,5} ∪ {2,6} = {1,2,5,6}.
	var cache = TestCacheFactory.Create();
	using var result = cache.Query()
		.Or(
			b => b.UseIndex(cache.CityIndex, 1),
			b => b.UseIndex(cache.CityIndex, 2))
		.Execute();
	Assert.That(result.Keys, Is.EquivalentTo(new[] { 1, 5, 2, 6 }));
}

[Test]
public void Or_NestedOr_ThreeWayUnion() {
	var cache = TestCacheFactory.Create();
	using var result = cache.Query()
		.UseIndex(cache.CountryIndex, 12)   // {1,2,3,4}
		.Or(
			b => b.UseIndex(cache.CityIndex, 1),                                   // {1,5}
			b => b.Or(
				c => c.UseIndex(cache.CityIndex, 2),                               // {2,6}
				c => c.UseIndex(cache.CityIndex, 3)))                              // {3,7}
		.Execute();
	// {1,2,3,4} ∩ ({1,5} ∪ {2,6} ∪ {3,7}) = {1,2,3}
	Assert.That(result.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }));
}

[Test]
public void Or_OneBranchNoOp_OuterIntersectsWithOther() {
	var cache = TestCacheFactory.Create();
	using var result = cache.Query()
		.UseIndex(cache.CountryIndex, 12)   // {1,2,3,4}
		.Or(
			b => b.UseIndex(cache.CityIndex, 1),  // {1,5}
			b => b /* no-op */)
		.Execute();
	// no-op branch excluded — outer ∩ {1,5} = {1}
	Assert.That(result.Keys, Is.EquivalentTo(new[] { 1 }));
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~OrClauseUnpairedCoreTests`
Expected: all three should pass — semantics are entirely driven by the existing implementations (UnionWith ref-ref, IntersectWith ref-ref, IsInitlized check).

If a test fails, investigate the specific code path (likely an edge case in `UnionWith(ref ValueSet<T>)` when self is uninitialized) and fix.

- [ ] **Step 3: Commit**

```bash
git add tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs
git commit -m "test(or): edge cases — first=true seed, nested Or, no-op branch"
```

---

## Task 16: Implement `IOrCapable<…>` on `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>`

**Files:**
- Modify: `src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs` (around line 610–643)
- Test: `tests/Prague.Core.Tests/Query/OrClausePairedCoreTests.cs`

- [ ] **Step 1: Create paired-core failing test**

```csharp
namespace Prague.Core.Tests.Query;

using NUnit.Framework;
using Prague.Core.QueryBuilders;

[TestFixture]
public class OrClausePairedCoreTests
{
	[Test]
	public void PairedCore_OrWith_IntersectsByKey() {
		// Direct unit test of PairedCacheQueryBuilderCoreCombined.OrWith
		// without going through Or-extension or builders.
		// Seed self.candidates with pairs {(L1,K1), (L1,K2), (L2,K3)}.
		// branch1 = {(_,K1)}, branch2 = {(_,K3)}.
		// Result: self.candidates should keep pairs whose .Key ∈ {K1, K3}
		//         = {(L1,K1), (L2,K3)}.
		Assert.Fail("TODO: implement once paired IOrCapable lands");
	}
}
```

> Fill the test body once the API is in. Use `Unsafe.AsRef(in field)` if `_candidates` is internal-but-not-public.

- [ ] **Step 2: Flip `_candidates` to internal** (if not already)

In `CacheQueryBuilder.cs:619` change `private ValueSet<JoinedKeyPair<TLeft, TKey>> _candidates;` to `internal ValueSet<JoinedKeyPair<TLeft, TKey>> _candidates;`.

- [ ] **Step 3: Add `IOrCapable<…>` to the struct's declaration**

```csharp
public struct PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>
	: IDisposable, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>,
	  IOrCapable<PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>>
```

- [ ] **Step 4: Implement `CreateBranch` and `OrWith`**

Inside the struct body:
```csharp
public PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue> CreateBranch() {
	var branch = this;
	branch._candidates = default;
	branch._disposed = false;
	return branch;
}

public void OrWith(
	in PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue> branch1,
	in PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue> branch2)
{
	ref var b1 = ref System.Runtime.CompilerServices.Unsafe.AsRef(in branch1);
	ref var b2 = ref System.Runtime.CompilerServices.Unsafe.AsRef(in branch2);
	try {
		if (!b1._candidates.IsInitlized && !b2._candidates.IsInitlized) return;
		_candidates.IntersectWith(ref b1._candidates, ref b2._candidates);
	} finally {
		if (b1._candidates.IsInitlized) b1._candidates.Dispose();
		if (b2._candidates.IsInitlized) b2._candidates.Dispose();
	}
}
```

> The paired core has no `_first` flag — its candidates are seeded at construction (e.g., via `UseIndexAsPairs`). `OrWith` always intersects.

- [ ] **Step 5: Fill the test body**

Replace `Assert.Fail("TODO")` with the actual setup-act-assert pattern, populating `_candidates` directly via `JoinedKeyPair<TLeft, TKey>` instances.

- [ ] **Step 6: Run paired test**

Run: `dotnet test tests/Prague.Core.Tests --filter FullyQualifiedName~OrClausePairedCoreTests`
Expected: PASS.

- [ ] **Step 7: Run full Core.Tests**

Run: `dotnet test tests/Prague.Core.Tests`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add src/Prague.Core/QueryBuilders/CacheQueryBuilder.cs \
        tests/Prague.Core.Tests/Query/OrClausePairedCoreTests.cs
git commit -m "feat(queries): IOrCapable on PairedCacheQueryBuilderCoreCombined"
```

---

## Task 17: Negative-compile checks — `Or` after `Sort`, `Where`/`Sort`/`Execute` inside a branch

**Files:**
- Modify: `tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs` — add a method that contains commented-out illegal code with `#error`-like markers, plus an inline expectation comment.

> **Approach:** the Prague convention is small, hand-rolled compile-fail sentinels rather than a full analyzer. Add a region with snippets that *should not* compile and document them as TODOs for a future analyzer.

- [ ] **Step 1: Add a commented "negative cases" region documenting the expected compile failures**

```csharp
#region Negative cases — must NOT compile (verify manually until an analyzer rule lands)
//
// // 1. Or after Sort — TExecutor changes type, no IOrCapable impl.
// cache.Query().Sort(x => x.Foo).Or(b => b, b => b);    // expected: CS0...
//
// // 2. Where inside a branch — NarrowOnlyQuery is not IBaseFilterable.
// cache.Query().UseIndex(...).Or(b => b.Where(v => true), b => b);    // expected: CS0...
//
// // 3. Sort inside a branch.
// cache.Query().UseIndex(...).Or(b => b.Sort(...), b => b);    // expected: CS0...
//
// // 4. Execute inside a branch.
// cache.Query().UseIndex(...).Or(b => b.Execute(), b => b);    // expected: CS0...
//
#endregion
```

- [ ] **Step 2: Uncomment one snippet at a time and run `dotnet build`**

For each illegal snippet:
1. Uncomment the line.
2. Run `dotnet build tests/Prague.Core.Tests`.
3. Confirm a compile error.
4. Re-comment.

If any snippet compiles, the type-system shape is wrong — investigate the marker interfaces / constraint set.

- [ ] **Step 3: Leave the snippets commented out and commit**

```bash
git add tests/Prague.Core.Tests/Query/OrClauseUnpairedCoreTests.cs
git commit -m "test(or): document negative compile-cases (manual verification)"
```

---

## Task 18: Generated.Tests — `Or` with codegen `WithXxx`

**Files:**
- Create: `tests/Prague.Generated.Tests/Query/OrClauseGeneratedTests.cs`

- [ ] **Step 1: Find a `[DataCache]` POCO model in Generated.Tests already exposing two indexed fields**

Run: `grep -rln "DataCacheIndex" tests/Prague.Generated.Tests | head -10` — pick a fixture with at least two indexed fields (Country + City equivalent).

- [ ] **Step 2: Add the test**

```csharp
namespace Prague.Generated.Tests.Query;

using NUnit.Framework;
// using statements for the chosen test model + its generated cache type

[TestFixture]
public class OrClauseGeneratedTests
{
	[Test]
	public void Or_WithCodegenExtensions_ProducesCorrectUnion() {
		// Seed the cache via its codegen API.
		// Run Query().WithCountry(12).Or(b => b.WithCity(1), b => b.WithCity(2)).Execute()
		// Assert the result.
		Assert.Fail("TODO — wire to chosen [DataCache] fixture");
	}
}
```

- [ ] **Step 3: Fill in setup/act/assert using the chosen fixture**

Use the `WithCountry`/`WithCity` (or equivalent) emitted extensions, seed enough rows for the expected union to be non-trivial.

- [ ] **Step 4: Run**

Run: `dotnet test tests/Prague.Generated.Tests --filter FullyQualifiedName~OrClauseGeneratedTests`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test Prague.Tests.slnf`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add tests/Prague.Generated.Tests/Query/OrClauseGeneratedTests.cs
git commit -m "test(or): codegen WithXxx + Or integration test"
```

---

## Task 19: Final verification

- [ ] **Step 1: Full test suite**

Run: `dotnet test Prague.Tests.slnf`
Expected: all green; new Or test counts visible.

- [ ] **Step 2: Build release**

Run: `dotnet build Prague.Publish.slnf -c Release`
Expected: PASS.

- [ ] **Step 3: Sanity-check the bench harness still builds**

Run: `dotnet build benchmarks/Prague.Benchmarks -c Release`
Expected: PASS.

- [ ] **Step 4: If everything is green, mark the spec status**

Edit `docs/superpowers/specs/2026-05-19-or-query-clause-design.md` and change:
```
Status: Draft — pending review
```
to:
```
Status: Implemented — see commits on branch …
```

- [ ] **Step 5: Final commit**

```bash
git add docs/superpowers/specs/2026-05-19-or-query-clause-design.md
git commit -m "docs(specs): mark Or-clause spec as implemented"
```

---

## Self-review checklist

Before claiming done, verify each spec requirement maps to at least one task above:

- [x] `Or(b1, b2)` identity overload — Tasks 12-13
- [x] `Or(b1, b2, arg)` `TArg` overload — Task 14
- [x] `NarrowOnlyQuery<TCache>` discriminator — Task 4
- [x] `IIndexNarrower` marker (extracted from `IBaseFilterable`) — Task 1
- [x] `UseIndex`/`WithXxx` constraint migrated — Tasks 2, 3
- [x] `IOrCapable<TSelf>` interface — Task 9
- [x] Unpaired core `IOrCapable` impl — Task 11
- [x] Paired core `IOrCapable` impl — Task 16
- [x] `ValueSet.IntersectWith(ref, ref)` primitive — Tasks 5-6
- [x] `ValueSet.UnionWith(ref, ref)` primitive — Tasks 7-8
- [x] `Or` no-op when both branches uninitialized — covered by ref-ref primitives + Task 15 test
- [x] `Or` seeds via union when outer `_first == true` — Task 15 first test
- [x] Nested `Or` — Task 15 second test
- [x] No-op branch excluded from union — Task 15 third test
- [x] Negative compile cases (Sort/Where/Join/Execute in branch) — Task 17
- [x] Codegen integration — Task 18

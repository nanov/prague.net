# `Or` — disjunction clause on candidate narrowing

Date: 2026-05-19
Status: Implemented — feature branch feat/prague-website-docs (3006ad5..54e6b91)

## Goal

Add a disjunction (`Or`) clause to the fluent query API so users can express `A AND (B OR C)` candidate predicates over indexed fields, with the same hot-path zero-allocation invariants the rest of the query pipeline upholds.

```csharp
_authorCache.Query()
    .WithCountry(12)
    .Or(b => b.WithCity(1),
        b => b.WithCity(2))
    .Sort(...)
    .Execute();
// candidates = {Country == 12} ∩ ({City == 1} ∪ {City == 2})
```

## Non-goals

- No `And(...)` companion. Top-level conjunction is already expressible as chained `WithXxx`. Sugar can be added later without breaking changes.
- No `Where` / `Sort` / `Join` / `Execute*` inside branches. Branches are **index-narrowing only**.
- No variadic `params ReadOnlySpan<...>` overload. Two-branch is the fixed arity; >2-way unions are expressed by nesting.
- No branch-level `TArg` per-branch state — the `TArg` overload passes one user-state value shared by both branches (the common case; per-branch state can use capturing lambdas if needed).
- No optimizer that picks between push-down and post-union strategies. One implementation strategy (push-down) is chosen unconditionally; profiling can revisit later.

## Public API

Two new extension methods on the combined builder, constrained on `IIndexNarrower` + `ICacheCarrier<TCache>` (see "Marker-interface refactor" below). All three discriminators — `ExecutableQuery<TCache>`, `NonExecutableQuery<TCache>`, and the new `NarrowOnlyQuery<TCache>` — implement `IIndexNarrower`, so `Or` is reachable on each (including inside an `Or` branch).

```csharp
// Identity (two branches, no shared state)
public static CacheQueryBuilderCombined<TQuery, TExecutor, TCache, TKey, TValue>
    Or<TQuery, TExecutor, TCache, TKey, TValue>(
        this CacheQueryBuilderCombined<TQuery, TExecutor, TCache, TKey, TValue> source,
        Func<NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>, NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>> b1,
        Func<NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>, NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>> b2)
    where TQuery : IIndexNarrower, ICacheCarrier<TCache>
    where TExecutor : struct, ICandidatesFilterer<TKey, TValue>, IOrCapable<TExecutor>;

// With user state (two branches, one shared TArg)
public static CacheQueryBuilderCombined<TQuery, TExecutor, TCache, TKey, TValue>
    Or<TQuery, TExecutor, TCache, TKey, TValue, TArg>(
        this CacheQueryBuilderCombined<TQuery, TExecutor, TCache, TKey, TValue> source,
        Func<NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>, TArg, NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>> b1,
        Func<NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>, TArg, NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>> b2,
        TArg arg)
    where TQuery : IIndexNarrower, ICacheCarrier<TCache>
    where TExecutor : struct, ICandidatesFilterer<TKey, TValue>, IOrCapable<TExecutor>;
```

The constraint `TExecutor : struct, ICandidatesFilterer<TKey, TValue>` is the type-system guarantee that `Or` is reachable exactly when the builder is in the candidate-narrowing phase:

- `CacheQueryBuilderCoreCombined<TKey, TValue>` — implements `ICandidatesFilterer<TKey, TValue>` ✓ → `Or` callable.
- Sorted / joined / post-`Execute*` executors — do NOT implement it → `Or` is **compile-unreachable** after those transitions.
- Paired core `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>` — does NOT implement it (paired is an internal join-resolver concern, not the user-facing `Or` surface).

Because the constraint is on a marker interface, `Or` is also reachable inside `JoinOneNew` filter callbacks: those callbacks operate on a builder produced by `Cache.Query().AsNonExecutable()`, whose discriminator is `NonExecutableQuery<TCache>` and whose executor remains `CacheQueryBuilderCoreCombined<TKey, TValue>` — both required markers are present, so `.Or(...)` composes naturally inside a filter lambda.

`NarrowBranchBuilder<TCache, TKey, TValue, TExecutor>` is a type alias (or convenience wrapper) for:
```csharp
CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TCache, TKey, TValue>
```
i.e. the branch builder's executor type **matches** the outer's executor — so a paired-outer produces paired branches, an unpaired-outer produces unpaired branches, etc. The same executor implementation handles its own narrowing.

### Three or more branches

Composed via nesting — `Or` is itself in scope inside a branch because `NarrowOnlyQuery<TCache>` carries the marker that admits `Or`:

```csharp
.Or(b => b.WithCity(1),
    b => b.Or(c => c.WithCity(2), c => c.WithCity(3)));
// → {City == 1} ∪ ({City == 2} ∪ {City == 3})
```

## Semantics

| Aspect | Behavior |
|---|---|
| Position in pipeline | Same as `WithXxx` — anywhere before `Sort`/`Join`/`Execute*`. Post-`Sort`/post-`Join` unreachability is **enforced at the type level** by the `TExecutor : ICandidatesFilterer<TKey, TValue>` constraint, which only the unpaired narrowing core satisfies. Returns the same builder instance, so chaining further `WithXxx`/`Or`/`Where` after `Or` is unaffected. Also reachable inside `JoinOneNew` filter callbacks (their executor is the same unpaired narrowing core under a `NonExecutableQuery<TCache>` discriminator). |
| Outer candidates effect | If `_first == false` (outer narrowed): `Candidates.IntersectWith(ref b1, ref b2)`. If `_first == true` and any branch is initialized: `Candidates.UnionWith(ref b1, ref b2)`, then `_first ← false`. Skips the KeyIndex copy — the branches' union is already a subset of KeyIndex. |
| Outer `Where` predicate | Untouched — `Or` is candidate-narrowing only; `_filter` continues to run at materialization. Identical to how `WithXxx` is `_filter`-agnostic today. |
| No-op branch (lambda didn't call any narrowing op) | Branch's candidates stay uninitialized → excluded from the union. `Or(b1=City1, b2=no-op)` reduces to `outer ∩ City1`. `Or(b1=no-op, b2=no-op)` is the entire-Or no-op (outer unchanged; outer's `_first` flag preserved). |
| Branch starting state | Always fresh — `CreateBranch` returns an executor with empty/uninitialized candidates and `_first = true` (unpaired). First `WithXxx` inside the branch seeds from its index. |
| Exception inside a branch lambda | Propagates out; outer builder is unchanged; both branches' working sets are disposed via `finally`. |

## Marker-interface refactor

Two parallel hierarchies — one on the **discriminator** side (`TQuery`), one on the **executor** side (`TExecutor`) — are tightened so the constraint set for `Or` (and any future narrow-only operation) maps onto exactly the right combination of builder states.

### Discriminator side

Today's discriminator hierarchy gates `Where`, `UseIndex`, and `WithXxx` on the same `IBaseFilterable` marker. The `Or` clause requires a discriminator that admits `UseIndex`/`WithXxx` but **not** `Where`. Introduce a new base interface and push the narrowing constraints down one level:

```csharp
// New — base for any discriminator that admits candidate-narrowing extensions.
public interface IIndexNarrower { }

// Existing — refactored to extend the new base. Adds Where on top.
public interface IBaseFilterable : IIndexNarrower { }
```

| Extension family | Constraint today | Constraint after refactor |
|---|---|---|
| `UseIndex(...)` (single + multi-value, span and `ref ValueSet` overloads) | `IBaseFilterable` | `IIndexNarrower` |
| Codegen-emitted `WithXxx(...)` | `IBaseFilterable` | `IIndexNarrower` |
| `Or(...)` (this clause) | n/a — new | `IIndexNarrower` |
| `Where(...)` | `IBaseFilterable` | `IBaseFilterable` (unchanged) |
| `Sort(...)` / `Join…` / `Execute*` | their existing markers | unchanged |

Because `IBaseFilterable` extends `IIndexNarrower`, every existing call site that constrained on `IBaseFilterable` for narrowing extensions continues to compile — non-breaking refactor.

### Executor side

The existing `ICandidatesFilterer<TKey, TValue>` interface already distinguishes narrowing-phase executors from sorted / joined / post-execute variants. `Or` reuses it as its executor-side constraint — no new marker for that purpose.

Coverage:

| Executor | Implements `ICandidatesFilterer<TKey, TValue>` | `Or` reachable? |
|---|---|---|
| `CacheQueryBuilderCoreCombined<TKey, TValue>` | yes | **yes** |
| `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>` | yes | **yes** (each executor implements its own `Or`/`Clone` — see below) |
| Sorted / joined / post-`Execute*` executors | no | no — compile-unreachable |

### Executor-polymorphic `Or` — the algorithm lives on the executor

The actual narrowing is **the executor's responsibility**. The extension method is thin plumbing: it asks the executor to clone itself for each branch, hands the clones to the branch lambdas (wrapped in a `NarrowOnlyQuery<TCache>` builder), then asks the outer executor to absorb the two branch executors. Each executor type — unpaired, paired, and any future narrowing variant — implements `Or` against its own native candidate-set shape.

```csharp
// New — implemented by every executor that supports the Or operation.
// Self-typed so the operation signature stays strongly typed per executor.
public interface IOrCapable<TSelf> where TSelf : struct, IOrCapable<TSelf>
{
    // Construct a fresh "branch" executor — shares inert state (_dataCache,
    // _filter reference, etc.) with self, but starts with empty/uninitialized
    // candidates. The branch lambda will narrow it from scratch via WithXxx.
    TSelf CreateBranch();

    // Apply (branch1 ∪ branch2) ∩ self → self. For unpaired with no current
    // candidates (_first == true), result is branch1 ∪ branch2 (since self
    // represents "everything"). Disposes branch1/branch2 candidate storage.
    void OrWith(in TSelf branch1, in TSelf branch2);
}
```

The merge uses a new `ValueSet<T>` primitive — `IntersectWith(ref ValueSet<T> v1, ref ValueSet<T> v2)` — that walks self once and keeps elements present in (v1 ∪ v2) via the `IncrementalIntersecter<T, …>` mark-and-sweep pattern already used by multi-value `UseIndex`. No `UnionWith` step needed on the narrowed path; one allocation-free pass over self.

Implementations:

| Executor | `CreateBranch` | `OrWith` |
|---|---|---|
| `CacheQueryBuilderCoreCombined<TKey, TValue>` | shallow-copy struct; reset `Candidates = default`, `_first = true`. | If neither branch initialized → no-op. Else if `_first == true`: `Candidates.UnionWith(ref b1, ref b2)` and flip `_first = false`. Else: `Candidates.IntersectWith(ref b1, ref b2)`. Always disposes branch candidates. |
| `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>` | shallow-copy struct; reset `_candidates = default`. | Always intersect (paired is always seeded — no `_first` flag): `_candidates.IntersectWith(ref branch1._candidates, ref branch2._candidates)`. Disposes branch candidates. |

Because `Or` is polymorphic on the executor, both unpaired and paired contexts work without any v1 caveat. New narrowing executors implement `IOrCapable<TSelf>` (a couple dozen lines) and `Or` becomes immediately available on them.

## Branch surface — `NarrowOnlyQuery<TCache>` discriminator

A new discriminator added next to `ExecutableQuery<TCache>` and `NonExecutableQuery<TCache>`:

```csharp
public readonly struct NarrowOnlyQuery<TCache> : IIndexNarrower, ICacheCarrier<TCache>;
```

It implements **only** `IIndexNarrower` (+ `ICacheCarrier<TCache>`) — not `IBaseFilterable`, not the markers for sort / join / execute. By reusing `CacheQueryBuilderCombined<TQuery, TExecutor, TCache, TKey, TValue>` parameterized by `NarrowOnlyQuery<TCache>`, the branch builder inherits:

- `WithXxx` (codegen-emitted) extensions — constrained on `IIndexNarrower`. ✓
- `UseIndex(...)` core — constrained on `IIndexNarrower`. ✓
- `Or(...)` (this clause) — constrained on `IIndexNarrower`, so nested `Or` falls out for free. ✓

Compile-time unreachable on a `NarrowOnlyQuery<TCache>` builder:

- `Where(...)` — requires `IBaseFilterable`, which `NarrowOnlyQuery` does NOT implement.
- `Sort(...)`, `Join…` / `InnerJoin…`, `Execute*` — each requires its own existing marker that `NarrowOnlyQuery` does NOT implement.

## Execution algorithm

Two layers: the **extension method** (plumbing — same for every executor) and the **executor's `IOrCapable.OrWith`** (algorithm — per executor type).

### Extension-method plumbing

```text
function Or(outer, branch1Lambda, branch2Lambda):
    var b1Exec = outer._leftQuery.CreateBranch();
    var b2Exec = outer._leftQuery.CreateBranch();
    var ok = false;
    try:
        var b1Builder = wrapAsNarrowOnlyBuilder(b1Exec);
        var b2Builder = wrapAsNarrowOnlyBuilder(b2Exec);
        b1Builder = branch1Lambda(b1Builder);
        b2Builder = branch2Lambda(b2Builder);
        outer._leftQuery.OrWith(in b1Builder._leftQuery, in b2Builder._leftQuery);
        ok = true;
    finally:
        if (!ok):
            disposePooledStateOf(b1Exec);
            disposePooledStateOf(b2Exec);
        // success path: OrWith took ownership and disposed appropriately
    return outer;
```

The extension never touches the candidate set directly. It only knows how to create branch executors and hand them off — the executor implements the actual set algebra. Branch lambdas operate on `WithXxx`, `UseIndex`, and nested `Or`, all of which mutate the candidate set through the executor's own narrowing primitives.

### Executor algorithm — unpaired core (`CacheQueryBuilderCoreCombined<TKey, TValue>`)

```text
OrWith(self, branch1, branch2):
    try:
        bool b1Init = branch1.Candidates.IsInitlized
        bool b2Init = branch2.Candidates.IsInitlized
        if (!b1Init && !b2Init):
            return                                       // both branches no-op → Or no-op
        if (self._first):
            // outer unbounded → result is just the branches' union
            self.Candidates.UnionWith(ref branch1.Candidates, ref branch2.Candidates)
            self._first = false
        else:
            // outer narrowed → keep elements in (b1 ∪ b2)
            self.Candidates.IntersectWith(ref branch1.Candidates, ref branch2.Candidates)
    finally:
        if (branch1.Candidates.IsInitlized) branch1.Candidates.Dispose()
        if (branch2.Candidates.IsInitlized) branch2.Candidates.Dispose()
```

Two symmetric ValueSet primitives drive the algorithm: `UnionWith(ref v1, ref v2)` for the "outer unbounded" seed path, and `IntersectWith(ref v1, ref v2)` for the "outer narrowed" intersect path. Each tolerates uninitialized inputs (uninitialized branch is excluded from the union).

### Executor algorithm — paired core (`PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>`)

Paired has no `_first` flag — its candidate set is seeded at construction (e.g. via `UseIndexAsPairs`). `OrWith` always intersects:

```text
OrWith(self, branch1, branch2):
    try:
        self._candidates.IntersectWith(ref branch1._candidates, ref branch2._candidates)
    finally:
        if (branch1._candidates.IsInitlized) branch1._candidates.Dispose()
        if (branch2._candidates.IsInitlized) branch2._candidates.Dispose()
```

Equality on `JoinedKeyPair<TLeft, TKey>` is by `.Key` alone, so the mark-and-sweep intersect operates on key identity automatically.

### New `ValueSet<T>` primitives

Two new ref-ref overloads:

**`void IntersectWith(ref ValueSet<T> v1, ref ValueSet<T> v2)`**
- If neither `v1` nor `v2` is initialized → no-op.
- Otherwise walk the receiver once via `IncrementalIntersecter<T, Identity>`; mark each receiver slot if present in any initialized branch; sweep unmarked on disposal.
- Result: receiver retains elements in (initialized branches' union). Uninitialized branch excluded.
- Zero temp allocation (bitmap stackalloc'd below `StackAllocThreshold`, ArrayPool above).

**`void UnionWith(ref ValueSet<T> v1, ref ValueSet<T> v2)`**
- If neither initialized → no-op (receiver stays uninitialized).
- Otherwise initialize receiver if empty, then add elements from each initialized branch. Internally an `UnionWith(ref v1)` followed by `UnionWith(ref v2)` (skipping uninitialized branches). May ownership-transfer one of the branches into the receiver when receiver is empty and exactly one branch is initialized — implementation may optimize this later; the spec only requires the post-condition `self = init(self) ∪ init-or-empty(v1) ∪ init-or-empty(v2)`.

Existing single-arg `IntersectWith(ref ValueSet<T>)` and `UnionWith(ref ValueSet<T>)` are unchanged.

### Leak-safety

Mirrors the `handedOff` guard used by `JoinOneNew*Resolver` execute paths:

- Extension-method `try/finally` disposes both branch executors' pooled storage on lambda exception or any `OrWith` failure that throws before claiming candidates.
- `OrWith`'s own `try/finally` covers in-progress disposal during the swap.
- `ValueSet.Dispose` is no-op on uninitialized state (`IsInitlized` guard).

### Hot-path / allocation budget

Per `Or(...)` invocation:

- 0 array allocations from the variadic surface (fixed arity, no `params`).
- 0 closure allocations when both branches are `static` lambdas. With `TArg` overload, 0 closure allocations regardless of capture.
- 2 pooled buffer rents for the working branch sets (one cloned-or-uninitialized set per branch). Both returned on `Dispose`.
- 1 pooled buffer rent for the running `result`. Handed off to `outer._candidates`; old outer buffer returned in the swap.
- Net: 1 pooled buffer "kept" (replaces old outer), 2 transient rents, no GC allocations.

When a branch becomes empty (e.g., `WithCity(...)` finds no matches against a cloned outer set), `result.UnionWith(emptySet)` is a single guard-and-return.

## Edge cases

| Case | Behavior |
|---|---|
| Outer `_first == true`, at least one branch narrowed | `Candidates.UnionWith(ref b1, ref b2)` produces the union directly; `_first` flipped to `false`. No KeyIndex copy. |
| Outer `_first == true`, both branches no-op | Entire `Or` is no-op; outer's `_first` flag preserved (no premature seeding). |
| Outer `_first == false`, one branch no-op | No-op branch excluded from union — outer ∩ branch_narrowed. Equivalent to having called `.WithSomething(...)` directly on outer. |
| Outer `_first == false`, both branches no-op | Outer unchanged. `IntersectWith(ref v1, ref v2)` short-circuits when neither branch is initialized. |
| Branch lambda throws | Caught by `finally`, working set disposed, exception propagates, outer untouched. |
| Branch lambda returns a different builder than the one passed in | The returned builder's `_candidates` are taken as the branch result. Lambda signature already enforces matching types; nothing special needed. |
| Deeply nested `Or` (e.g., 5 levels) | Works by construction — each level allocates 2 transient buffers + 1 result; total transient buffers = O(depth × 2). Pool handles this. |

### "No-op branch is excluded from union" — built-in

The semantics are: a branch whose lambda didn't narrow (post-lambda `Candidates.IsInitlized == false`) is excluded from the union entirely — *not* treated as "everything". This is the natural fall-out of the `IntersectWith(ref v1, ref v2)` primitive ignoring uninitialized branches. `Or(b => b.WithCity(2), b => b /* no-op */)` reduces to `outer ∩ City2`. `Or(no-op, no-op)` is the entire-Or no-op. No special detection or short-circuit logic is needed.

## TArg overload — zero-alloc shared state

Mirrors the `JoinOneFilterWithArg<TBuilder, TArg>` pattern used by `JoinOneNew`:

```csharp
.Or(static (b, a) => b.WithCity(a.CityA),
    static (b, a) => b.WithCity(a.CityB),
    (CityA: 1, CityB: 2));
```

When both lambdas are `static`, the C# compiler caches the delegate instances (no per-call closure allocations). The `TArg` is passed by value through both invocations. For ref-counted or large `TArg`, callers should prefer a small struct (the codebase's existing convention).

Per-branch independent state is **not** supported by this overload — if branches need different state, use capturing lambdas (allocates per call) or compose: `.Or(b => b.WithCity(stateA), b => b.WithCity(stateB))` reads each from its enclosing scope.

## File layout

New files under `src/Prague.Core/`:

- `QueryBuilders/CacheQueryBuilder.Or.Extensions.cs` — both `Or` extension overloads (identity + `TArg`). Pure plumbing: clone executors, wrap as branch builders, invoke lambdas, delegate to `outer.Executor.OrWith(...)`.
- `QueryBuilders/NarrowOnlyQuery.cs` — discriminator struct and the new `IIndexNarrower` marker interface.
- `QueryBuilders/IOrCapable.cs` — `IOrCapable<TSelf>` interface (`Clone`, `OrWith`).

Modified files:

- `QueryBuilders/CacheQueryBuilder.cs` —
  - Discriminator side: introduce `IIndexNarrower`; make `IBaseFilterable` extend it; move `UseIndex` (all overloads) and other narrowing extensions from `IBaseFilterable` to `IIndexNarrower` constraints. `Where` stays on `IBaseFilterable`. `ExecutableQuery<TCache>` / `NonExecutableQuery<TCache>` are unchanged at the source level — they still implement `IBaseFilterable`, gaining `IIndexNarrower` transitively.
  - Executor side: reuse existing `ICandidatesFilterer<TKey, TValue>` as the narrowing-phase marker. Add `IOrCapable<TSelf>` and implement it on both `CacheQueryBuilderCoreCombined<TKey, TValue>` and `PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>` (each with its own `Clone` and `OrWith` against its native candidate-set type).
- `Collections/ValueSet.cs` — add two ref-ref overloads: `IntersectWith(ref ValueSet<T> v1, ref ValueSet<T> v2)` (mark-and-sweep against the union, via existing `IncrementalIntersecter<T, …>`) and `UnionWith(ref ValueSet<T> v1, ref ValueSet<T> v2)` (initialize-if-empty then add elements from each initialized branch). Existing single-arg overloads are unchanged.
- Codegen (`Prague.Codegen`) — change the constraint clauses on emitted `WithXxx` extensions from `IBaseFilterable` to `IIndexNarrower`. Mechanical: the constraint name is set in one template location and used in every emitted extension. After the change, regenerate consumers and rerun `Prague.Generated.Tests`.

## Test surface

Tests live in `tests/Prague.Core.Tests/Query/` (or follow the existing convention for query-related tests; if `Query/` doesn't exist, create it). Framework: NUnit (matches `Prague.Core.Tests`).

Required cases:

1. **Two-branch identity** — `Or(b => b.WithA(1), b => b.WithA(2))` returns rows with `A ∈ {1,2}`.
2. **Outer ∩ Or** — `.WithCountry(12).Or(b1, b2)` returns only rows where country matches AND `(b1 ∪ b2)` matches; rows passing only `b1` but not `WithCountry(12)` are excluded.
3. **Empty branch** — one branch's index lookup misses; result equals the other branch (intersected with outer).
4. **Both branches empty** — result is empty.
5. **Universe branch (no `WithXxx` in branch)** — short-circuits; outer is returned unchanged.
6. **Outer empty (`_first == false`, 0 candidates)** — `IntersectWith(ref v1, ref v2)` walks an empty receiver — no marks, no sweeps; outer stays empty.
6b. **Outer `_first == true`, one branch narrows** — `UnionWith(ref b1, ref b2)` runs directly; outer.Candidates = branch_narrowed, `_first = false`. No KeyIndex copy.
7. **Nested `Or`** — three-way union via `Or(b1, b => b.Or(b2, b3))` matches `b1 ∪ b2 ∪ b3`.
8. **`Or` then `WithXxx`** — narrowing after `Or` further intersects.
9. **`Or` with outer `Where`** — `Where(...)` predicate runs at materialization and is unaffected by `Or`.
10. **`Or` then `Sort`/`Execute`** — chain continues normally.
11. **Branch throws** — exception propagates, no rented buffers leaked (verify via pool instrumentation if available; otherwise smoke-test with repeated runs).
12. **`TArg` overload** — static lambdas accept and use the shared arg correctly; no captured-closure allocations (verified by allocation-counting test or BenchmarkDotNet check if available).
13. **`NarrowOnlyQuery` branch surface** — compile-time test that `Where`, `Sort`, `Execute*`, `Join…` are NOT reachable inside a branch lambda (negative compile-test or analyzer rule).
14. **`Sort().Or(...)` does not compile** — `Or` is unreachable post-`Sort` (and post-`Join…`) because sorted/joined executors don't implement `ICandidatesFilterer` / `IOrCapable`. Verify via a compile-fail test or analyzer assertion.
15. **`Or` inside a `JoinOneNew` filter callback** — composes correctly; both `WithXxx` and `Or` operate on the right cache's unpaired narrowing core under a `NonExecutableQuery<TCache>` discriminator.
16. **Paired-core `Or` smoke test** — exercise `Or` reachable on `PairedCacheQueryBuilderCoreCombined`'s `IOrCapable` impl (whether through a public API path or a direct core-level test).

Additional surface in `Prague.Generated.Tests` if a `[DataCache]` model is needed to exercise generated `WithXxx` extensions inside branches.

## Risks

- **Marker-interface refactor scope.** The constraint move from `IBaseFilterable` to `IIndexNarrower` touches every `UseIndex` overload and every codegen-emitted `WithXxx`. Mechanical and non-breaking for callers (because `IBaseFilterable : IIndexNarrower`), but the diff is wide. Codegen regeneration + full test pass (`Prague.Generated.Tests`, `Prague.Core.Tests`) is mandatory.
- **`IOrCapable.CreateBranch` semantics.** Shallow-copies the executor struct, then resets the candidate field to `default` (uninitialized) and `_first` to `true` (unpaired only). Shared inert state (`_dataCache`, `_filter` reference) is fine because branches never mutate them. Getting this wrong (e.g., leaving the original candidates pointer in place) double-disposes pooled storage.
- **`ValueSet` ref-ref overloads.** `IntersectWith(ref, ref)` reuses `IncrementalIntersecter<T, …>`; `UnionWith(ref, ref)` reuses the existing single-arg `UnionWith` twice. Both must handle uninitialized inputs.
- **Pool corruption.** The `handedOff` guard pattern from `JoinOneNew` resolvers is mandatory — `ValueSet.Dispose` swallows double-`ArrayPool.Return` (and corrupts the pool) silently.
- **Branch builder mutability.** `CacheQueryBuilderCombined` is a struct; the branch lambda receives one by value, narrows it via `WithXxx` returning a new value, returns the final value. Implementation must take the returned builder's `Executor` field rather than re-reading the original. (Codegen `WithXxx` already follows this convention.)

## Open items

None at design time. Exact marker-interface names beyond `IIndexNarrower` / `IBaseFilterable` / `ICacheCarrier<TCache>` will be confirmed against `CacheQueryBuilder.cs` at the start of the implementation plan; their identities don't affect the design.

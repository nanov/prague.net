# Prepared queries: a build-once / execute-on-demand command model

> **Status:** PoC design, branch `poc/prepared-query`. Lives next to the existing builder; nothing
> existing changes behavior.
> **Goal:** a query that is *described* once (optionally with parameters), stored, and executed
> any number of times, from any thread, with byte-identical results to the eager builder and
> zero allocations per execution.

## 0. Why the current builder cannot do this

The builder is half eager, half deferred, and single-shot:

| Stage | Today | Where |
|---|---|---|
| Narrowing (`UseIndex` / codegen `WithXxx`) | **Eager.** Intersects the index into `Candidates` (a rented `ValueSet`) the moment the method is called. The builder *is* the partially executed query. | `CacheQueryBuilderCoreCombined.UseIndexInternal`, `CacheQueryBuilder.cs:120-330` |
| `Where(predicate)` | Eager-ish. Composes closures: `_filter = v => current(v) && predicate(v)` — one closure allocation per chained `Where`. | `CacheQueryBuilder.cs:680` |
| `Or(b1, b2)` | Eager. Runs both branches against child intersecters immediately. | `OrWith`, `CacheQueryBuilder.cs:64` |
| `JoinOne` / `JoinMany` / `Sort` | **Already deferred.** Each appends a resolver struct to the type-level chain `Resolvers<TPrev, TResolver>`; nothing touches the right cache until `Execute`. | `*.Extensions.cs`, `Resolvers.cs`, `ResolverChain.cs` |
| `Execute*` / `Count` | Runs resolvers, materializes, then **disposes the builder** (`Candidates.Dispose(); _disposed = true`). A second `Execute` throws. | `CacheQueryBuilderCoreCombined.Execute`, `:703` |

So the deferred half exists and is reusable. The work is to make the *narrowing half* a recorded
description, and to make the whole thing a reusable value.

## 1. Shape: a prepared query is a recorder in front of the existing executor

The key reuse trick: `CacheQueryBuilderCombined<TDiscriminator, TLeftQuery, TKey, TValue,
TResolverChain, TResult>` is generic over its left query, constrained only on
`TLeftQuery : struct, ICandidatesExecutor<TKey, TValue>`. **Every join and sort extension binds on
that constraint, not on the eager core type.** If the prepared builder plugs a *recording* left
query into the same combined builder, all 5,600 lines of `JoinOne`/`JoinMany`/`Sort` overloads
work unchanged and are not duplicated.

```
                 ┌── build time (any thread, once) ──────────────────────────────┐
cache.Prepare()  │ CacheQueryBuilderCombined<ExecutableQuery<TCache>,            │
                 │     NarrowerChain<…>,   ◄── recorder: grows by type per op    │
                 │     TKey, TValue,                                             │
                 │     Resolvers<…>,       ◄── unchanged, already deferred       │
                 │     TResult>                                                  │
  .UseIndex(idx, v)      → NarrowerChain<Prev, IndexEq<TIndexKey>>              (records idx + v)
  .UseIndex(idx, a => a.X) → NarrowerChain<Prev, IndexEqArg<TIndexKey, TArgs>> (records idx + selector)
  .Where(p)              → NarrowerChain<Prev, Filter<TValue>>                  (records p; no closure composition)
  .JoinOne(...)/.Sort()  → existing extensions, untouched
  .Build()               → PreparedQuery<TArgs, TResult>   (one allocation, here, never again)
                 └───────────────────────────────────────────────────────────────┘

                 ┌── execute time (any thread, N times) ─────────────────────────┐
prepared.Execute(in args)
   1. copy the stored builder struct onto the stack           (value copy, no alloc)
   2. var core = new CacheQueryBuilderCoreCombined(cache);    (the EAGER core, stack-local)
   3. chain.Replay(ref core, in args);                        (calls the same UseIndexInternal/WhereInternal
                                                               the eager builder calls, in the same order)
   4. wrap: new CacheQueryBuilderCombined<…, CacheQueryBuilderCoreCombined, …>(disc, core, resolversCopy, manyCount)
   5. existing ExecuteCoreSimple / ExecuteCoreJoined / *Top   (100% shared execution)
                 └───────────────────────────────────────────────────────────────┘
```

**Semantics fall out by construction.** The replay produces exactly the eager core state the
eager builder would have had after the same calls, then runs the exact same execution code. There
is no second query engine to keep in sync. Differential tests (§6) pin it, but the argument for
parity is structural.

## 2. The narrower chain (the recorded part)

Mirror of `Resolvers<TPrev, T>`:

```csharp
public interface INarrower<TKey, TValue, TArgs> {
	void Apply(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, in TArgs args);
}

public struct NarrowerChain<TPrev, TNarrower, TKey, TValue, TArgs>
	: ICandidatesExecutor<TKey, TValue>, IPreparedNarrowers<TKey, TValue, TArgs>
	where TPrev : struct, IPreparedNarrowers<TKey, TValue, TArgs>
	where TNarrower : struct, INarrower<TKey, TValue, TArgs> {
	private TPrev _prev; private TNarrower _narrower;
	public void Replay(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, in TArgs args) {
		_prev.Replay(ref core, in args);
		_narrower.Apply(ref core, in args);
	}
	// ICandidatesExecutor members exist only to satisfy the join/sort extension constraints.
	// They are never called: execution always goes through the replayed eager core.
	void ExecuteBase<T>(ref T c) => throw new InvalidOperationException("prepared query: call Build()");
	…
}
```

Narrower structs, one per eager entry point (all are thin: a field or two plus a call into the
existing `ICandidatesFilterer` method):

| Narrower | Captures | Replays |
|---|---|---|
| `IndexEq<TIndexKey>` | `CacheKeyValueIndex` ref, `TIndexKey` | `UseIndexInternal(index, value)` |
| `IndexEqArg<TIndexKey, TArgs>` | index ref, `Func<TArgs, TIndexKey>` (static lambda: cached delegate, no per-exec alloc) | `UseIndexInternal(index, selector(args))` |
| `IndexIn<TIndexKey>` / `IndexInArg` | index ref, `TIndexKey[]` or `Func<TArgs, ReadOnlyMemory<TIndexKey>>` | `UseIndexInternal(index, span)` |
| `ListIndexEq…` | same, for `CacheKeyValueListIndex` | |
| `RangeArg<TIndexKey, TArgs, TRb>` | range index ref, `Func<RangeQueryBuilder<TIndexKey>, TArgs, TRb>` | already the eager signature (`UseIndex(index, rangeBuilder, args)`) — reuse verbatim |
| `KeySet` | `CacheKeySetIndex` ref | `UseIndexInternal(index)` |
| `Filter<TValue>` | `Predicate<TValue>` | `WhereInternal(predicate)` |
| `Or<TB1, TB2>` | two sub-chains (themselves `IPreparedNarrowers`) | `OrWith` with branch adaptors that replay the sub-chains into the branch builders |

The `ICandidatesFilterer` methods these call are `internal` on an interface the eager core
implements explicitly; the narrowers live in `Prague.Core`, so they reach them the same way the
existing extension methods do.

Why a type chain and not a fixed-capacity op array: ops are generic over `TIndexKey`; storing them
without boxing in one homogeneous buffer needs a byte-slot union plus `Unsafe` reinterpretation.
The type chain gets the JIT to specialize and inline each `Apply` per closed type — the same reason
`Resolvers<,>` is a chain.

## 3. Parameters

Two flavors, both zero-alloc at execute time:

- **Bound:** value captured at build. `prepared.Execute()` (`TArgs = Unit`).
- **Parameterized:** `cache.Prepare<TArgs>()` where `TArgs` is a user struct (or tuple).
  Narrowers take `Func<TArgs, TIndexKey>` — the house precedent is the range overload's
  `(rb, args) => …, args` static-lambda pattern; the compiler caches static lambdas, so the
  delegate is allocated once at build. Selector invocation is one delegate call (~1–2 ns) per
  narrower per execution. If that ever shows, the upgrade is `TSelector : struct, IArgSelector<TArgs,
  TIndexKey>` for full devirtualization; the chain shape does not change.

**Parameterized `Where` is the one place parity and zero-alloc conflict.** The eager core's
filter is a `Predicate<TValue>`; a `Func<TValue, TArgs, bool>` becomes one only via a closure over
`args` (one allocation per execution). Options:

1. PoC v1: `Where` accepts only `Predicate<TValue>` (constants / static lambdas). Parameterized
   filtering goes through indexes. Zero-alloc holds. **Recommended for the PoC.**
2. Later: generalize the eager core's `_filter` into a struct-generic filter slot
   (`TFilter : struct, IValueFilter<TValue>`), which also removes today's `&&` closure allocation
   in `WhereInternal`. Invasive in `CacheQueryBuilderCoreCombined`, so a separate PR.

**Chosen:** a third way that keeps the core's `Predicate<TValue>` slot untouched. `Where(Func<TValue,
TArgs, bool>)` records a `FilterArgNarrower`; at replay it rents a per-thread pooled `ArgPredicate`
box (`[ThreadStatic]` stack in `ArgPredicatePool<TValue, TArgs>`) whose `Predicate<TValue>` was created
once and reads `func`/`args` off the box, so binding is two field writes and no allocation. The
execution takes a mark before replay and resets to it in a `finally` after `Execute`/`Count` (the core
applies the filter during execution, not replay); stack discipline makes it re-entrancy-safe and
thread-static storage makes it thread-safe. Option 2 remains the way to remove the eager `&&` closure.

## 4. Storing the command: the type-name problem

The built type is unnameable in user code (`CacheQueryBuilderCombined<ExecutableQuery<…>,
NarrowerChain<NarrowerChain<…>>, …, Resolvers<Resolvers<…>>, JoinResult<…>>`). `var` works for
locals, not for fields. Decision:

```csharp
public abstract class PreparedQuery<TArgs, TResult> {
	public abstract QueryResults<TResult> Execute(in TArgs args, int skip = 0, int take = int.MaxValue);
	public abstract QueryResults<TResult> ExecutePooled(in TArgs args, int skip = 0, int take = int.MaxValue);
	public abstract int Count(in TArgs args);
}
internal sealed class PreparedQuery<TBuilder, TArgs, TResult> : PreparedQuery<TArgs, TResult> { TBuilder _builder; … }
```

`Build()` allocates exactly one object holding the struct chain by value. Execution is one virtual
call (monomorphic at any given call site, so dynamic PGO guards and devirtualizes it), after which
everything inside is fully specialized. Per-execution allocation: the same as the eager builder
today, i.e. zero on pooled paths.

Thread safety: `_builder` is never mutated after `Build()`; every `Execute` copies it to the stack.
Resolver structs carry per-execution scratch (`JoinOneResolver._narrowedValues`, disposed in the
existing `finally`s), which is why the copy is mandatory, not an optimization. One prepared query
can execute concurrently on many threads. Reads keep today's lock-free / documented-staleness
semantics; nothing about the concurrency model changes.

## 5. Surface

Core (raw `InMemoryDataCache`, what `Prague.Core.Tests` exercises, no codegen):

```csharp
var byStatus = cache.Prepare<(Status status, int minQty)>()
	.UseIndex(statusIndex, static a => a.status)
	.UseIndex(qtyIndex, static (rb, a) => rb.Gte(a.minQty), static a => a)   // range: eager signature reused
	.JoinOne(customerFk, customers)                                          // existing extension, unchanged
	.Sort(comparer)                                                          // existing extension, unchanged
	.Build();

using var page = byStatus.ExecutePooled((Status.Active, 10), skip: 0, take: 50);
```

Codegen (second step, after Core works): emit `Prepare()` / `Prepare<TArgs>()` on the generated
wrapper and, for each `[DataCacheIndex]`, `WithXxx(value)` and `WithXxx(Func<TArgs, T>)` overloads
constrained on the prepared discriminator. Same emission loop as today's `WithXxx`, different
target method. FK-driven `JoinWith{T}` needs nothing: it binds on `ICandidatesExecutor`.

Type-state is unchanged: the same discriminator markers (`IIndexNarrower`, `IBaseFilterable`,
`IBaseJoinable`, `ISortable`, `IExecutableQuery`) gate what is legal at each point, so a prepared
query can be narrowed, filtered, joined and sorted in exactly the orders the eager one allows,
and `Build()` is only reachable on `IExecutableQuery`.

## 6. Tests and pins

- **Differential parity** (`Prague.Core.Tests/Prepared/`): for every shape family already covered
  by `Query/`, `Join/`, `OrClauseUnpairedCoreTests`, `ClassicVsBoundedDifferentialTests`: build the
  eager query and the prepared one from the same inputs, assert identical `Count`, identical row
  sequence (including `SortBounded` encounter-order tie rules), identical `Truncated`/`TotalCount`.
- **Reuse:** execute the same prepared query 3× with different args and interleaved cache
  mutations; assert each run equals a fresh eager run at that moment.
- **Concurrency:** N threads executing one prepared instance concurrently against a writer;
  assert no exceptions and per-run parity with an eager run (same staleness envelope as today).
- **Allocation:** `TopKAllocationCoreTests`-style pin: 0 B per `ExecutePooled` on unique, list,
  range, joinOne, joinMany, sort-bounded shapes. BDN case in `benchmarks/` next to
  `TopKExecuteBenchmarks`, eager vs prepared, to show the per-execution cost delta (expected:
  within noise; the replay does exactly the eager work plus one delegate call per parameter).
- **Leaks:** `LeakAssert.Balanced` around prepared execution, exception paths included (a throwing
  selector must not strand the replayed `Candidates`).

## 7. Steps

> **Progress — shipped on `poc/prepared-query`.** Steps 1–6 below are done; nothing in the eager
> builder changed behavior. What landed, in order:
>
> - **Steps 1–3 (recorder, narrowers, joined path):** `src/Prague.Core/QueryBuilders/Prepared/` —
>   `PreparedNarrowers<TKey,TValue,TArgs,TChain>` recorder in the left-query slot of the ordinary
>   `CacheQueryBuilderCombined`, `NarrowerLink` type chain, narrowers for unique / list / symmetric
>   list equality, `IndexIn` (`ReadOnlyMemory<T>` / `T[]` / `Func<TArgs, ReadOnlyMemory<T>>`), range
>   (bound and `(rb, args)`), key-set, last-updated after / after-until / `Func<TArgs,long>`, constant
>   `Where`; `Prepare()` / `Prepare<TArgs>()` on the raw cache; `Build()` on simple, joined
>   (`Resolvers<…>`) and sorted (`Sort` / `SortBounded`) chains, routing to `ExecuteCoreSimple` /
>   `ExecuteCoreJoined` / `*Top`; `Execute` / `ExecuteCloned` / `ExecutePooled` / `ExecutePooledCloned` /
>   `Count` taking `in TArgs`, plus the `NoArgs` convenience overloads.
> - **Step 4 (`Or`):** `PreparedNarrowOnly<TCache>` branch discriminator, `OrNarrower` +
>   `PreparedOrBranch` replaying the recorded sub-chains through the eager core's `OrWith`; narrowing
>   overloads generalized over `TDiscriminator : IIndexNarrower`; `INarrower.Apply` / `Replay` require
>   `IOrCapable<TKey,TValue,TCore>` on the core.
> - **`If` / `IfElse` (prepared-only conditional narrowing):** `PreparedConditionalBranch<TCache>`
>   discriminator (`IBaseFilterable`, so `UseIndex` / `Where` / `Or` / nested `If` bind; joins, sort
>   and `Build` do not), `IfNarrower` / `IfElseNarrower` replaying a frozen sub-chain only when
>   `condition(args)` holds, a narrow-only `If` family for use inside an `Or` branch.
> - **Parameterized `Where`** — `Where(Func<TValue,TArgs,bool>)` records a `FilterArgNarrower`; at
>   replay it binds `func`/`args` into a per-thread pooled `ArgPredicate` box (§3, "chosen"), so the
>   eager core's `Predicate<TValue>` slot is untouched and the execution allocates nothing where the
>   eager spelling pays 89 B for a capturing closure.
> - **Step 5 (codegen):** prepared entry points and branch seeds carry a free `TCache` (raw cache or
>   generated wrapper) — `Prepare<TCache,TKey,TValue,TArgs>(cache, carrier)`, `Build` / `Sort` /
>   `SortBounded` generic over `TCache`, `Or` / `If` / `IfElse` overloaded per receiver discriminator.
>   `CacheGenerator` emits `Prepare()` / `Prepare<TArgs>()` on every wrapper plus
>   `XxxCachePreparedQueryExtensions` with the prepared twin of every `WithXxx` / `WithoutXxx` /
>   `WithKey` / `UpdatedAfter` overload, scoped by `ICacheCarrier<XxxCache>` so they bind at top level
>   and inside branches. FK `JoinWith{T}` needed no change.
> - **`Match` (prepared-only tag dispatch):** `.Match(static a => a.Mode, m => m.Case(tag, b => …)
>   .Case(…).Default(b => …))` — the prepared twin of a C# `switch` over type-preserving reassignments.
>   The selector runs per execution; the arms run once at build against the `If` seed builders
>   (`PreparedConditionalBranch` at top level / inside conditionals, `PreparedNarrowOnly` inside `Or`)
>   and accumulate as a type chain with no arity limit (`EmptyArms` → `MatchArms<TPrev,TArm,…>` per
>   `Case`, optional `DefaultArm` tail; `Case` / `Default` are extensions constrained on
>   `IOpenMatchArms`, so an arm after `Default` is a compile error). First equal tag in declaration
>   order wins (a duplicate tag resolves to its first arm), else the default, else no-op; the compare is
>   `EqualityComparer<TTag>.Default` (enums do not implement `IEquatable<T>`). `Describe` emits
>   `NarrowerKind.Match` with the tags in `Value` (`MatchArmTags`) and a child per arm; the frozen
>   planner replays it. **`Eval` was considered and rejected** — an opaque per-execution callback that
>   narrows the core directly would work, but the recorded arms keep the plan analyzable
>   (`Explain()` shows every arm), which is the whole point of `BuildFrozen()`.
> - **Optional-bounds range:** `UseIndex(range, from: Func<TArgs,T?>, to: Func<TArgs,T?>,
>   fromInclusive = true, toInclusive = true)` and the bound `UseIndex(range, T? from, T? to, …)`
>   (`RangeOptionalArgNarrower` / `RangeOptionalRefArgNarrower` / `RangeOptionalNarrower`), plus the
>   generated `WithXxx(from:, to:, …)` twins on Range indexes. Replay hands the eager `UseIndexCore` an
>   `OptionalRange<T>` (two `RangeValue`s, `None` for an open side) through a cached pass-through
>   delegate. The core has no `(None, None)` arm — it throws `UnreachableException` — so both-`null`
>   skips the core call, which is exactly the eager query that never called the range (pinned).
> - **Tests:** `tests/Prague.Core.Tests/Prepared/` (293: differential, reuse, leak, concurrency,
>   allocation parity, `Or`, `If`, `Match`, optional range, frozen) and
>   `tests/Prague.Generated.Tests/Prepared/` (1210 generated tests in the project).
> - **Step 6 (docs + benchmark):** `context/query.md` and `context/generated.md` sections, README
>   "Prepared Queries" section, `benchmarks/Prague.Benchmarks/PreparedQueryBenchmarks.cs` (eager vs
>   prepared per shape, results in `benchmarks/Prague.Benchmarks/RESULTS.MD`): every ratio within noise
>   of 1.00, allocations equal except the parameterized-`Where` shape (89 B eager → 0 B prepared),
>   `Build()` of a four-narrower command 88 B once.

1. `NarrowerChain` + `INarrower` + `IndexEq`/`IndexEqArg`/`Filter` + `PreparedQuery<TArgs,TResult>`
   + `cache.Prepare()` on the raw cache + `Build()` + `Execute/ExecutePooled/Count` for the
   **simple** (no-join) path. Differential tests for unique/list index shapes. Allocation pin.
2. Range, `IndexIn`, key-set, symmetric list variants. Range tests.
3. Joined path: `Build()` on chains with `Resolvers<…>` — routes to `ExecuteCoreJoined` /
   `*Top`. Confirm inner-join `PrepareIndexedInner` sees replayed candidates (replay happens before
   the combined builder is constructed, so ordering is preserved). Join differential tests.
4. `Or` narrower with sub-chain branches. Or differential tests.
5. Codegen: `Prepare*` + `WithXxx` overloads on wrappers; `Prague.Generated.Tests` parity.
6. Docs: `context/query.md` section, README example.

Step 1 is the go/no-go: it proves the recorder-in-front-of-executor shape compiles against the
existing generic constraints and hits 0 B/op.

### Deferred

Considered and consciously left out of this branch; each is a separate PR if it earns its place:

- **Flatten `Build()` into a `NarrowOp[]` command.** Today the description is a closed generic type
  chain; a homogeneous op array (byte-slot union + `Unsafe` reinterpretation per `TIndexKey`) would
  give a prepared query an identity (equality / hashing), make a registry of prepared queries
  possible, and open the door to an optimizer that reorders narrowers by selectivity. Not needed for
  parity or zero-alloc, and it trades away the JIT specialization the chain gets for free.
  *Started as `BuildFrozen()` (§8): the chain is described into metadata at build time and kept, so
  nothing is traded away; the optimizer half begins with the point-lookup executor.*
- **Struct filter type parameter on the eager core** (`TFilter : struct, IValueFilter<TValue>` in
  `CacheQueryBuilderCoreCombined`). Replaces the per-thread `ArgPredicate` pool and also removes the
  eager `&&` closure allocation on the second chained `Where`. Invasive in the eager core, so its own
  change.
- **`Eval(Action<core, args>)` / opaque per-execution narrowing.** Rejected in favour of `Match`
  (§7 progress): a callback that narrows the replayed core directly cannot be described, so
  `BuildFrozen()` would lose the plan; recorded `Match` arms cover the dispatch use case and stay
  inspectable.
- **Conditional resolvers** — joins or `Sort` / `SortBounded` inside an `If` branch or `Match` arm. Decided against
  for now: the resolver chain is part of the *result type*, so a conditional join would have to
  produce a union result shape (or a nullable right side) that the eager builder does not have, and
  parity by construction would be lost. `If` stays a narrowing-only construct.
- **`out long max` last-updated forms.** The eager `UpdatedAfter(..., out long max)` overloads that
  report the newest timestamp seen have no prepared twin; a prepared execution would need a
  per-execution out-channel (a result-side field or an `ExecuteWithMax` terminal).
- **`Or(b1, b2, arg)` state overload.** The eager zero-alloc spelling passes explicit state to
  static branch lambdas; the prepared branches already read the execution arguments, so the overload
  is redundant there and was not mirrored.

## 8. `BuildFrozen()` — stage 1

> **Status:** shipped on `poc/prepared-query` after §7. Starts the "flatten / identity / optimizer"
> item from the Deferred list without giving up the typed chain.

`BuildFrozen()` is a sibling of every `Build()` overload (simple, sorted-simple, joined,
sorted-joined) returning `FrozenQuery<TArgs,TResult> : PreparedQuery<TArgs,TResult>` — a drop-in
with the same five terminals — that does three things at build time: flattens the recorded chain into
inspectable metadata, picks a specialized executor when the plan shape allows, and otherwise binds
exactly today's replay.

### Descriptors

`INarrower.Describe(List<NarrowerDescriptor>)` and `INarrowerChain.Describe(...)` are implemented by
every narrower struct and by `EmptyNarrowers` / `NarrowerLink` (prev, then self, so the list is in
replay order). A `NarrowerDescriptor` carries `Kind` (`UniqueEq`, `UniqueIn`, `ListEq`, `ListIn`,
`ListInProjected`, `Range`, `KeySet`, `LastUpdatedAfter`, `LastUpdatedBetween`, `Filter`, `FilterArg`,
`Or`, `If`, `IfElse`, `Match`), `IsParameterized`, the index as `object` (identity), the bound value boxed, the
selector / range builder / condition as `Delegate`, the filter delegate, and for the composites the
sub-chains described recursively in `Children`. The descriptor also keeps an internal `Source` — the
narrower that produced it — which is how the planner recovers `TIndexKey` without reflection: the
unique-equality narrowers implement `IPointLookupSource<TKey,TValue,TArgs>` and construct the closed
executor themselves.

`FrozenQuery` keeps **both** the typed chain (inside the replay executor, for the fallback and for
future executors that want JIT-specialized sub-steps) and the `PlanInfo` (descriptors, `HasResolvers`,
`IsSorted`, `Executor` name). `Explain()` prints the ops and the chosen executor; `Plan` is internal
(tests).

### Executor selection

`FrozenPlanner` decides once, in `BuildFrozen()`. The frozen class is
`FrozenQuery<TArgs,TResult,TExecutor> where TExecutor : struct, IFrozenExecutor<TArgs,TResult>` —
the `IPreparedSimplePlan` static-abstract strategy pattern — so the one virtual `Execute*` call lands in
a body specialized per executor and the constrained call on the struct field devirtualizes. Executors:

| Executor | Shape | Body |
|---|---|---|
| `PointLookupExecutor<TKey,TValue,TArgs,TIndexKey>` | simple, unsorted, ops == `[UniqueEq]` then zero or more `[Filter \| FilterArg]` | index probe → store probe → filters in order → 0/1-row `QueryResults` |
| `ReplaySimpleExecutor<…,TPlan>` | every other simple shape (incl. sorted) | `PreparedReplay.RunSimple` — the `PreparedSimpleQuery` body |
| `ReplayJoinedExecutor<…,TPlan>` | every joined shape | `PreparedReplay.RunJoined` — the `PreparedJoinedQuery` body |

The eligibility rule is deliberately narrow: a `Where` *before* the unique step, a second index after
it, any multi-value / range (fixed or optional-bounds) / key-set op, any `Or` / `If` / `Match`, a sort
or a join all fall back. Stage 1
proves the machinery on one shape.

### The point-lookup fast path

`key = selector is null ? bound : selector(args)`; `index.TryGetValue(key, out entityKey)`;
`cache.TryGet(entityKey, out value)`; the filters in build order, short-circuiting like the eager `&&`
composition — a constant `Predicate<TValue>` called directly, a `Func<TValue,TArgs,bool>` called with
`args` directly (no `ArgPredicatePool` box). Materialization reproduces `SimpleResultContainer` for a
one-candidate set step for step: `skip > 1` → `EmptyWithTotalCount(1)`; otherwise a one-slot
`QueryResults` from `PragueArrayPool<T>.Pool` (pooled) or `new T[1]`, clone-on-add when no slice was
requested, `SliceLeaveTotalCount(skip, min(take, 1 - skip))` when one was, clone-in-place after the
slice; a miss or a rejected row returns the shared `Empty`. `Count` is 0 or 1 with no rent at all.

### Measured (Apple M4 Pro, .NET 9, default job; full tables in `benchmarks/Prague.Benchmarks/RESULTS.MD`)

| Shape | Eager | `Build()` | `BuildFrozen()` | Ratio | Alloc |
|---|---:|---:|---:|---:|---|
| Unique, bound | 134.3 ns | 130.8 ns | 26.5 ns | 0.20 | 0 B |
| Unique, parameterized | 132.3 ns | 130.0 ns | 18.8 ns | 0.14 | 0 B |
| Unique + constant `Where` | 149.4 ns | 134.4 ns | 24.4 ns | 0.16 | 0 B |
| Unique + parameterized `Where` | 156.1 ns (88 B) | 143.6 ns | 19.1 ns | 0.12 | 0 B |
| ListWhere / Range / JoinOne (fallback) | — | 1.00 | within error bars of `Build()` | — | equal |

Tests: `tests/Prague.Core.Tests/Prepared/FrozenQueryTests.cs` (executor selection, exhaustive eager /
prepared / frozen differential over found / not-found / filter pass / fail × four `Execute*` variants
× five `(skip,take)` pages with clone-identity checks, fallback parity on nine shapes, reuse across
mutations, 8×2000 concurrent lookups, leak balance incl. a throwing arg filter, a `Describe` shape
test over every narrower kind with nested `Or` / `If`) plus three frozen pins in
`PreparedQueryAllocationTests` (frozen ≤ prepared; 0 B pooled in Release).

### Stage 2 — measured optimizations

> **Status:** shipped on `poc/prepared-query` after stage 1. Every item below was implemented, given
> benchmark rows next to the eager (baseline) / `Build()` / `BuildFrozen()` rows in
> `FrozenQueryBenchmarks`, and kept or reverted on the numbers (`RESULTS.MD`, "stage 2"). `BuildFrozen()`
> now takes an optional `FrozenOptions`; the defaults keep the eager row sequence, the one optimization
> that changes encounter order is opt-in. `Explain()` names the executor, lists the active optimizations
> and prints their live state (the fused filter's current order and sample count, the capacity hint).

Apple M4 Pro, .NET 9, `--inProcess`, default job; 100k rows, list buckets of 1k (Group) and 100 (Tier).

#### 1. Fused filters + adaptive ordering — **kept** (`FuseFilters`, `AdaptiveFilterOrdering`, both default on)

`FusedFilter<TValue,TArgs>` (`FusedFilter.cs`) folds the plan's top-level `Where`s — two or more — into one
predicate. The eager core composes a second `Where` through a `&&` closure allocated per execution
(96 B) and every parameterized filter rented its own predicate box; the fused plan hands the core one
`Predicate<TValue>`: a delegate cached on the fused object when every filter is constant, otherwise one
pooled box per execution (`ArgPredicatePool.RentFused`) that loops the steps with the execution's
arguments. The chain replays through the new `INarrowerChain.ReplayIndexOnly`, whose `NarrowerLink`
skips the `FilterNarrower` / `FilterArgNarrower` links by a `typeof` test the JIT folds per closed
chain — the least invasive way to keep the fused `Where`s from running twice; filters inside `If` /
`Match` branches stay in their sub-chains. The fused predicate is set on the core *before* the index
replay, so an `Or` that auto-seeds sees the filter exactly as an eager `Where(..).Or(..)` does.

*Adaptive order.* Predicates are pure, so their order changes only how many calls a rejected row
costs. Every 256th execution (the first included) runs sampled: per step, calls and rejections are
counted and the first 4096 calls are timed with `Stopwatch`; at the end of that execution the steps are
re-ranked by rejection rate per unit cost and a changed order is published as a fresh immutable
`FusedOrdering` (the steps permuted into evaluation order) through `Volatile.Write`. Unsampled
executions read the ordering once and write nothing — no shared writes, no false sharing; the counters
are plain ints (advisory, racy by design). Chosen over DuckDB's permutation trials because it is
deterministic and testable, and because per-step statistics are argument-independent while whole-phase
timings vary with the bucket size the arguments select. Two findings shaped it: (a) a sampled execution
that timed every call cost ~80 µs, which at 1/32 sampling was a 20% regression on a 9 µs query — hence
the 4096-call timing cap and the 1/256 rate; (b) `Stopwatch` cannot tell a field read from an integer
modulo (both ~1–2 ns under a ~10 ns read overhead), so the cost is the timed mean minus the measured
timer overhead clamped to a 2 ns floor — cost separates expensive predicates (string ops) from cheap
ones, selectivity decides among the cheap — and a step is promoted only when its score beats the
other's by 1.5×: a swap between predicates of similar selectivity saves ~N·Δreject calls and measured a
loss of the same size to branch prediction (`ListTwoWheres`, Flag 67% vs Score%10 90%: reordering was
7% slower than the fixed order). Exceptions: a throwing predicate may throw earlier or later than under
the eager order and a non-total predicate (one guarded by an earlier `Where`) may be called on rows it
never saw — `AdaptiveFilterOrdering = false` restores the declared order (documented in the XML docs).

| Shape | Eager | `Build()` | `BuildFrozen()` | fixed order | Frozen alloc |
|---|---:|---:|---:|---:|---|
| list + 2 constant `Where`s | 9.52 µs (97 B) | 9.53 µs (97 B) | 8.55 µs (0.90) | 8.34 µs (0.88) | 0 B |
| list + 3 `Where`s, most selective last | 10.38 µs (193 B) | 11.48 µs (193 B) | 7.04 µs (0.68) | 10.15 µs (0.98) | 0 B |
| list + 2 parameterized `Where`s | 12.24 µs (257 B) | 13.49 µs (97 B) | 11.63 µs (0.95) | — | 0 B |

The two-constant row's win is fusion (no closure) plus the capacity hint (#2); with the 1.5× margin the
order stays declared there, and the 2% between the adaptive and the fixed-order row is the sampling
itself. The three-`Where` row is the adaptive win: the 99%-rejecting predicate moves to the front and
predicate calls drop from ~2,300 to ~1,000 per execution. The two-arg row removes two of the eager
path's five delegate hops per row and both boxes' `&&` closure.

#### 2. Capacity hints — **kept** (`CapacityHints`, default on)

`FrozenHints` (`FrozenHints.cs`) remembers the candidate set's high-water mark (`ValueSet.HighWaterMark`
= `_lastIndex`, the slots ever used, i.e. the seed size before later steps pruned it) and the next
execution pre-creates `core.Candidates` at that capacity before the replay (`FrozenReplay.PreSize`) — the
set the eager core would otherwise create lazily, at the default size, on its first index step. Hinted
plans are equality-seeded, so that step always runs and only unions into / prunes the set: the state is
the lazy path's minus the rehashes, and the eager core itself is untouched (a first version added a
`_candidateCapacityHint` field and an `InitCandidates()` helper to the core; reverted in favour of the
pre-created set once a default-job run showed the frozen `list ∩ range` replay — a plan with no
optimization active — 5% slower than `Build()`, see "Executor split" below). Plain-int advisory state:
grows to the observed size (capped
at 1<<20) and shrinks to the observed size after eight consecutive executions under half the hint, so a
one-off spike cannot pin an oversized rental. Only equality / membership-seeded plans get a hint: a
range seed's size says nothing about the next execution's bounds, composites may not seed at all, and a
filter-only plan never rents a set. `SimpleResultContainer` already sizes the result buffer from
`Candidates.Count`, so only the candidate set benefits.

| Shape | Eager | `Build()` | `BuildFrozen()` | hints off |
|---|---:|---:|---:|---:|
| list bucket (1k) + `Where` | 8.39 µs | 8.48 µs | 7.12 µs (0.85) | 8.46 µs (1.01) |
| list ∩ list, eager intersection | 9.42 µs | 9.32 µs | 8.08 µs (0.86) | — |

A 1k bucket rents its three arrays once at 1,103 slots instead of rehashing 47 → 97 → 197 → 397 → 797 →
1,597 with a rent / copy / return each time.

#### 3. Single compaction → adaptive intersection — **kept in a different form** (`AdaptiveIntersection`, default on)

The hypothesis was that one mark-and-prune bitmap over the seeded set with a single compaction beats
the eager per-step `IntersectWith`. Investigated: both remove by slot (`RemoveAt` / `Remove` leave holes
the enumerator skips), so both keep the seed's encounter order and the transform is result-identical.
Built as `IndexStepsExecutor` (`IndexStepsExecutor.cs`): the declared first step seeds the core the eager
way, then a top-level `IncrementalIntersecter` over `core.Candidates` and a child core in intersecter
mode (the `OrWith` construction) take the remaining steps, and `Dispose()` compacts once. Equality steps
implement the internal `IIndexStep` (cardinality probe + `Apply` on the concrete core) and are boxed
once at build; the executor is generic over `TPlan` like the replay, so sorted shapes qualify too.

Measured as literally "compact once", it was **not** a win: `list(1k) ∩ list(100)` fell from 8.3 to
5.4 µs but `list(100) ∩ list(1k)` doubled from 1.25 to 2.46 µs. The reason is which side is walked: the
eager `IntersectWith(PooledSet)` walks the *candidate set* probing the bucket — O(|candidates|) — while the
intersecter's first step walks the *bucket* probing the set — O(|bucket|); compaction cost is secondary.
So the kept form reads each remaining step's bucket size (`CacheKeyValueListIndex.TryGetCount`, one
probe) and takes the bitmap way only when the smallest bucket is smaller than the seeded set, with that
step first (later steps `RetainOnly` over the marks, O(|marked|)); otherwise it runs the eager way. Key-set
plans keep the eager intersection: the key-set step in intersecter mode always unions marks (it is
written for an `Or` branch's first step). Range steps are not equality steps and are not covered
(`list ∩ range` stays the plain stage-1 replay: 401.6 µs frozen against 400.9 eager / 406.0 `Build()`).

| Shape | Eager | `Build()` | `BuildFrozen()` | eager intersection (hints only) |
|---|---:|---:|---:|---:|
| list(1k) ∩ list(100) | 9.42 µs | 9.32 µs | **4.33 µs (0.46)** | 8.08 µs (0.86) |
| list(100) ∩ list(1k) | 1.36 µs | 1.33 µs | 1.27 µs (0.93) | 1.25 µs (0.92) |

A bug found on the way and pinned: the intersecter takes its `stackalloc` buffer as an already-zeroed
bitmap, so the executor must not carry `[SkipLocalsInit]` — a stale bit is a phantom mark
(`AdaptiveIntersection_ListList_ListListUnique_WithFilters_LikeEager_InOrder` caught it).

#### 4. Smallest-bucket seeding — **kept, opt-in** (`ReorderIndexNarrowers = true`)

Same executor: when every top-level op is an equality step or a filter, each step's cardinality is read
per execution and the smallest seeds (a unique step reports 0 / 1 and always wins; a missing key seeds
an empty set and every later step skips), the others intersect into it as above. Same set and `Count`
as eager; encounter order follows the seeding bucket, which is why it is opt-in and pinned on sorted
row sets (`Reorder_*` tests) rather than sequences.

| Shape | Eager | `BuildFrozen()` (default) | `ReorderIndexNarrowers` |
|---|---:|---:|---:|
| list(1k) ∩ list(100) | 9.42 µs | 4.33 µs | **1.26 µs (0.13)** |
| list(100) ∩ list(1k) | 1.36 µs | 1.27 µs | 1.24 µs (0.91) |

#### 5. The bound-vs-parameterized point lookup gap — **diagnosed, left**

Two probe rows: `Unique_FrozenDirectCall` calls the bound plan's virtual `ExecutePooled(default(NoArgs))`
directly, `Unique_FrozenBoundIntArgs` binds the value in a plan whose `TArgs` is `int`. Bound `NoArgs`
through the extension 27.2 ns, direct call 23.7 ns, bound with `int` args 19.5 ns, parameterized `int`
25.5 ns (short job). So the bound *value* is not the cost — the `int`-args bound plan is the fastest row — and the
`NoArgs` extension hop is ~3.5 ns of it; the rest is the `NoArgs` instantiation itself (an empty struct
passed by `in` through the generic virtual terminal). Not worth a special case at this size; recorded.

#### Executor split

A plan that gets neither a fused filter nor a hint binds the stage-1 `ReplaySimpleExecutor` /
`ReplayJoinedExecutor` (which call `PreparedReplay`, the `Build()` body) rather than the
`OptimizedReplay*Executor` twins that call `FrozenReplay`. The replay inlines the whole narrower chain
into one frame, and the extra parameters and null-checks of the optimized frame measured as +5% on the
frozen `list(1k) ∩ range(60k)` row in a default job (416 vs 398 µs eager / `Build()`; the stage-1 tree
measured 399) although nothing in them executed. With the split that row is back on the `Build()` body.

#### Eager-core additions

Two internal one-liners, both reads: `ValueSet.HighWaterMark` (`ValueSet.cs`) and
`CacheKeyValueListIndex.TryGetCount` (`Indexing.cs`). `CacheQueryBuilder.cs` is unchanged;
`Prague.Generated.Tests` unchanged and green.

#### Not observed as stable: in-process range rows

Across long `--inProcess` runs one range row (`Range_Prepared`, `OptionalRange_Prepared` or
`ListRange_Frozen`) intermittently measured 2–3× its neighbours with ~100 B/op, a different row each
run; re-running the category alone put it back at 1.00 every time (six re-runs). Two of the affected
rows are stage-1 code paths the change does not touch; the stage-1 tree did not show it in three runs
(one full), so it is not ruled out that the larger set of generic instantiations on this branch makes a
runtime slow path (generic-dictionary / virtual-stub resolution for the range narrower's constrained
generic call in `__Canon`-shared code) more likely in a long process. Left open; the per-category and
default-job numbers are what is reported.

### Stage 3 — the pipeline executor, steps 1–2

> **Status:** shipped on `poc/prepared-query` after stage 2. Design in
> `2026-09-09-frozen-pipeline-executor-design.md`; this is its §13 steps 1 (index probe APIs and
> cardinality signals) and 2 (the pipeline for equality / range seeds with probes and fused
> predicates, unsorted, fixed seed, driving `SimpleResultContainer`). Numbers in
> `benchmarks/Prague.Benchmarks/RESULTS.MD`, "stage 3".

**Step 1 — index APIs** (`Collections/PooledSet.cs`, `Collections/PooledBTree.cs`, `Indexing.cs`,
`InMemoryDataCache.cs`): `PooledSet.TryGetSlot(item, out slot)` (the enumerator's slot, gate-pinned;
false on absent / disposed), `CacheKeyValueListIndex.HasKeySelector` / `TryGetBucket(key, out bucket)`,
`CacheRangeIndex.KeyOf(key, value)` / `EstimateCount(in from, in to)`, `CacheKeySetIndex.Matches(key, value)`
/ `Count` / `CopyKeysTo<TSink>(ref sink)` (the locked copy `AddKeyTo` does, into an `IKeySink<TKey>`),
`LastUpdatedIndex.EstimateCount(after[, untilInclusive])`, `PooledBTree.EstimateCount(from, fromIncl, to,
toIncl)` / `EstimateCountFrom` / `EstimateCountTo`. The estimator is the design's fractional-rank
descent (two gate-pinned descents recording `Σ childIndex / Π childCounts` plus the leaf position), with
one addition: a chain of at most eight leaves between the bounds is summed exactly, because the right
spine of a sequentially filled tree is half-empty at every level and a pure rank estimate on a small
window there missed 2× (`DuplicateRuns` test, 37 vs 100). The descents are pinned (the design said no
pin is needed): a retired internal node's arrays are nulled on reclaim, and a lock-free descent must
not read one. Tests: `PooledBTreeEstimateCountTests` (uniform, shuffled, duplicate runs, after
deletes: within 2×; single-key and single-leaf windows exact), `PooledSetTryGetSlotTests` (slot =
enumeration position, after free-list reuse, disposed → false, 4 readers × 1 s churn), `IndexProbeApiTests`
(every API against the public surface, `TryGetBucket` + `Contains` / `TryGetSlot` under a writer moving
rows across buckets).

**Step 2 — the pipeline** (`QueryBuilders/Prepared/Pipeline/`): `PipelineExecutor<TKey,TValue,TArgs,TResolver>`
is the fourth `IFrozenExecutor`. The non-composite narrowers implement `IPipelineStepSource` and build
one `IPipelineStep` each — `UniqueEqStep`, `UniqueInStep`, `ListEqStep`, `ListInStep` (bound,
parameterized and projected), `RangeStep` (all five range narrowers reduce to two `RangeValue`s per
execution), `KeySetStep`, `LastUpdatedStep` (after / between, raw and global, all three time types) —
boxed once at build. Per execution: `Bind` resolves the arguments into a type-erased `StepBinding`
(two 16-byte slots for unmanaged index keys, two object slots for reference keys, a span's backing
object + offset + length for `In` spans) inside a stack `PipelineFrame`; the **first active** step seeds,
its keys copied out under one gate pin into `SeedKeys` (256 int keys on the stack, `PragueArrayPool`
above, multi-bucket seeds deduplicated through a `ValueSet` as the eager `UnionWith` chain is) — the
eager `_first` rule, so the encounter order is eager's; every later active step probes each candidate
key-side (unique: `TryGetValue` + `Equals`; last-updated: `TryGetLastUpdated`) or value-side (list:
`KeySelector(value) == k`; range: `KeyOf(value)` against the bounds with the eager `IndexSkip`
exclusions; key-set: `Matches(value)`); then the top-level filters run directly (`FilterStep[]`, the
stage-2 `FusedFilter` with adaptive order when there are two or more, no `ArgPredicatePool`); then
`SimpleResultContainer` is driven exactly as the eager core drives it. `Count` is the same walk without
a container. An empty unique `In` span short-circuits to zero rows (eager's `Clear()`); an empty list
`In` span and an unbounded optional range are inactive steps; every step inactive → the eager store
walk, keys copied out. Frame lifetime is one `try/finally`: the seed buffer, the dedupe set, a
projected step's rented key array and the container are all returned whatever throws.

*Planner rule.* Simple, unsorted, `Pipeline = true` (default), not `ReorderIndexNarrowers`, one to
sixteen index steps all of which build a step (a `TIndexKey` the binding can hold; no range step under
`IndexSideProbes`) → `Pipeline`. `[UniqueEq, Filter*]` keeps `PointLookup`. Composites, sorts, joins,
filter-only plans and the rejected key shapes → the stage-2 selection (`IndexSteps` / `Replay`).
`ReorderIndexNarrowers` stays on `IndexStepsExecutor` until the pipeline's free seed (design step 3).
`Explain()` prints `executor: Pipeline`, the seed rule and each step's probe side.

*Staleness (design §14.1).* Value-side probes judge a row by the value they return: under a writer
parked between the store write and the index writes (`InMemoryDataCache.AddIndexForTests` +
`FrozenPipelineTests.PausingIndex`) the pipeline never returns a row whose value contradicts the query
and returns a row whose current value qualifies, where eager does the opposite in both directions.
`FrozenOptions.IndexSideProbes = true` switches list and key-set probes to the index (`bucket.Contains`,
`Contains`) and reproduces the eager window exactly; a plan with a range probe replays under it (the
key-side twin is a window walk). Both are inside the documented contract.

*Not in this step.* The small-probe order-preserving seed (§3.4) and free seed for `Count` (step 3), so
`list(1k) ∩ list(100)` walks the 1k bucket and probes the tier on the value — faster than eager but
slower than the stage-2 adaptive intersection (kept reachable with `Pipeline = false`, `ListList_FrozenIndexSteps`);
composites (step 4); `JoinOne` fusion (step 5); the `SortBounded` feed (step 6). The two production
shapes — three list indexes → `SortBounded` → `JoinOne`, and time window → two lists → `SortBounded`
→ two `JoinOne`s (last-updated and range-on-timestamp variants) — replay in this step and are pinned
eager == prepared == frozen in `PreparedQueryProductionShapeDifferentialTests`, with benchmark rows as
the baseline for steps 3 / 5 / 6.

*Two pre-existing eager bugs the new tests surfaced, fixed minimally:* `SimpleResultContainer.BuildResults`
handed the rented buffer off before `CloneInPlace` / the sort ran, so a throwing `Clone()` or comparer
on the clone-after-slice path stranded the array (now handed off last); all four `JoinOne*Resolver.Clone`
dereferenced a missing outer right (`item = item.Clone()`) under `Execute*Cloned` (now null-checked).

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar (§12) | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| `ListWhere` (1k bucket + `Flag`) | 9.79 µs | 8.65 µs | 7.02 µs | 0.72 (1.39×) | ≥ 1.3× kept | 0 B |
| `ListList` (1k ∩ 100, fixed walk) | 9.63 µs | 9.58 µs | 8.91 µs | 0.93 (1.08×) | ~1.3× expected — short; step 3 (stage-2 `IndexSteps` 4.38 µs, `Reorder` 1.29 µs still available) | 0 B |
| `ListListReversed` (100 ∩ 1k) | 1.33 µs | 1.31 µs | 1.03 µs | 0.78 (1.29×) | ≥ 1.2× kept | 0 B |
| `ListRange` (1k ∩ 60k window) | 406.7 µs | 407.4 µs | **12.09 µs** | 0.03 (33.6×) | ≥ 5× kept | 0 B |
| `Range` (1k window) | 12.94 µs | 12.96 µs | 8.82 µs | 0.68 (1.47×) | ≥ 1.3× kept | 0 B |
| `OptionalRange` | 12.96 µs | 13.21 µs | 8.78 µs | 0.68 (1.48×) | ≥ 1.3× kept | 0 B |
| `ListKeySet` (1k ∩ a third of the rows) | 12.31 µs | 12.15 µs | 9.90 µs | 0.80 (1.24×) | ≥ 1.4× **missed** (delegate predicate per candidate on the ~6 µs floor) | 0 B |
| `ListLastUpdated` (1k ∩ newest half) | 224.3 µs | 219.4 µs | **9.86 µs** | 0.04 (22.7×) | ≥ 5× kept | 0 B |
| `Count_ListWhere` | 6.92 µs | 6.76 µs | 5.77 µs | 0.83 (1.20×) | ≥ 1.1× kept | 0 B |
| `Count_ListList` | 8.93 µs | 8.74 µs | 8.41 µs | 0.94 (1.06×) | ≥ 5× — step 3 (free seed) | 0 B |
| `Count_ListRange` | 434.0 µs | 433.5 µs | **10.14 µs** | 0.02 (42.8×) | — | 0 B |
| `Unique` / `UniqueArg` / `UniqueWhere` / `UniqueArgWhere` | 132–166 ns | 129–138 ns | 27.2 / 18.8 / 19.1 / 19.3 ns | 0.12–0.21 | within noise of stage 1 | 0 B |
| `ListTwoWheres` / `ListThreeWheres` / `ListTwoArgWheres` | 8.14 / 9.87 / 10.90 µs | 9.66 / 11.68 / 13.44 µs | 7.39 / 7.77 / 12.58 µs | 0.91 / 0.79 / 1.15 | stage-2 rows, now pipelined; `ListTwoArgWheres` at 1.15 of eager (its stage-2 replay was 0.95 — the two arg filters and the tuple copy per row; noted for step 3's probe/predicate ordering) | 0 B |
| `JoinOne` / `Match` (replay) | 31.3 / 8.51 µs | 32.2 / 8.46 µs | 30.6 / 8.44 µs | 0.98 / 0.99 | within noise | = |
| `ListListListSortBoundedJoinOne` (A) / `Count_` | 17.70 / 11.00 µs | 17.36 / 10.83 µs | 16.11 / 9.69 µs | 0.91 / 0.88 | replay baseline for steps 3 / 5 / 6 | 1 B / 0 B |
| `TimeWindowListListSortBoundedJoinTwo` (B) / `Count_` | 34.56 / 21.64 µs | 34.39 / 21.48 µs | 34.04 / 21.57 µs | 0.98 / 1.00 | replay baseline | 6 B / 3 B |
| `TimeRangeListListSortBoundedJoinTwo` (B, range on timestamp) / `Count_` | 37.33 / 24.95 µs | 37.17 / 24.86 µs | 37.00 / 24.87 µs | 0.99 / 1.00 | replay baseline (`Count_` re-run alone: the sequential run hit the in-process artifact at 1.92) | 6 B / 3 B |

Tests: `FrozenPipelineTests` (27: parity on every non-composite shape × four `Execute*` variants × five
pages with clone identity and `Count`; staleness in both directions for range / list / key-set probes
with the key-side twin; 8 × 2000 concurrent executions against a writer; leak balance incl. throwing
selector / predicate / `Clone()`, pool-path seeds; executor selection and `Explain`),
`FrozenPipelineAllocationTests` (8), `PreparedQueryProductionShapeDifferentialTests` (4),
`PooledBTreeEstimateCountTests` (7), `PooledSetTryGetSlotTests` (4), `IndexProbeApiTests` (5).
`Prague.Core.Tests` 1398 → 1457 per target framework, 0 failures; `Prague.Generated.Tests` unchanged and green.


### Stage 3 — step 3: seed selection

*Design:* pipeline design §3.3 (free seed), §3.4 (small-probe order-preserving seed), §8 (classic
`Sort`), §13 step 3; the executor's docs in `Pipeline/PipelineExecutor.cs`.

*What executes.* `Bind` records the active steps; `ChooseSeed` picks one of three modes (`SeedMode`):
**fixed** — the first active step, the eager `_first` rule — and, when that step is `PooledSet`-backed
(list equality, key-set) and another active equality step's live signal is at most half its own
(`2 × small ≤ first`), **small-probe** — the small step is walked instead, each survivor is located in
the first step's set with `PooledSet.TryGetSlot`, and the survivors are sorted by slot (`(slot, key)`
scratch: 256 slots on the stack, `PragueArrayPool` above) — the first step's own sequence at the
small step's cost; **free** — the smallest signal seeds (unique 0/1 always wins; ties → declared
order; an exact zero is the empty result with no walk; a range / last-updated B+tree estimate replaces
an exact count only when `2 × estimate < exact`) — for `Count` always, for `Execute*` under a classic
`Sort` (the rows are fully sorted afterwards) or the opt-in `ReorderIndexNarrowers`. A walked step is
index-judged; a step whose walk stands in for the first step's (the small step, a moved free seed)
keeps its value-side probe on the survivors. `Signal` per step: unique `TryGetValue` → 0/1, unique `In`
→ span length, list → the bound bucket's live count (the bucket is now looked up once at `Bind` for
every list step, not only key-side ones), list `In` → the sum of bucket counts, key-set → `Count`,
range / last-updated → `EstimateCount` (`ExactSignal = false`). `Explain()` prints the seed rule and the
last execution's decision (`last seed: step 1 ListEq (signal 6), probe: slot-sorted into step 0 ListEq
(signal 34)`), recorded only when the decision changes — no per-execution shared write.

*Seed copy.* `PooledSet.CopyKeysTo<TSink>` copies a bucket in one gate-pinned pass: the generation once,
`LastIndex` once (acquire), plain loads over the slots (the enumerator's per-slot `Volatile.Read` is an
acquire load on ARM); 546 vs 761 ns per dense 1k bucket (0.72×), 169 vs 221 at 300. Multi-bucket seeds
go through `DedupeSink` (a `ValueSet` admits each key once — the eager `UnionWith` order) into the same
copy; `CacheKeySetIndex.CopyKeysTo` uses it under its lock. Reader safety is the enumerator's:
the pin keeps the generation alive, the acquire of `LastIndex` orders every slot below it after its
publication, a dead slot (HashCode < 0) or a reference key cleared by a concurrent remove is skipped,
multi-word struct keys keep the version-guarded copy.

*Planner rule.* Simple, `Pipeline = true`, one to sixteen non-composite index steps, **unsorted or
under a classic `Sort`** (`TResolver.IsSorter && !AllowsBounded` — the sorter runs inside the eager
`SimpleResultContainer<…, SortResolver>` the pipeline drives) → `Pipeline`, with or without
`ReorderIndexNarrowers`. `SortBounded` (`ExecuteCoreSimpleTop`) replays until step 6. The stage-2
`IndexStepsExecutor` and `FrozenOptions.AdaptiveIntersection` are retired: every plan they served takes
the pipeline (§13 step 7, first half); `FrozenHints` stays for replayed plans, `FusedFilter` for both.

*Not in this step.* Composites (step 4), `JoinOne` fusion (step 5), the `SortBounded` feed (step 6) —
the production shapes A / B replay, their unsorted narrowing part is the `ListListList` row below.

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3, step 3")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar (§12, step 3) | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| `ListList` (1k ∩ 100, small-probe seed, eager order) | 9.09 µs | 9.04 µs | **1.72 µs** | 0.19 (5.3×) | ≥ 2× kept | 0 B |
| `ListList_FrozenReorder` (free seed, tier order) | — | — | 1.35 µs | 0.15 | the slot sort is the difference | 0 B |
| `ListListReversed` (100 ∩ 1k, fixed = small) | 1.31 µs | 1.30 µs | 0.99 µs | 0.76 (1.32×) | re-measure (step 2: 1.03) | 0 B |
| `ListListList` (1k ∩ 333 ∩ 111, shape A narrowing) | 11.70 µs | 11.58 µs | **2.25 µs** | 0.19 (5.2×) | ≥ 3× kept | 0 B |
| `Count_ListList` (free seed) | 8.90 µs | 8.75 µs | **1.06 µs** | 0.12 (8.4×) | ≥ 5× kept | 0 B |
| `Sort_ListList` (classic `Sort`, free seed + container sort) | 10.51 µs | 10.39 µs | **2.21 µs** | 0.21 (4.75×) | ≥ 2× kept | 0 B |
| `ListWhere` (1k bucket + `Flag`) | 8.47 µs | 8.44 µs | 7.72 µs | 0.91 (1.10×) | ≥ 1.5× **missed** — the 1k `TryGet` + delegate floor; PGO band 7.1–8.6 | 0 B |
| `Count_ListWhere` | 6.64 µs | 6.58 µs | 6.77 µs | 1.02 | same floor (step 2: 5.77, PGO-favoured) | 0 B |
| `ListKeySet` (1k ∩ a third) | 11.95 µs | 12.00 µs | 10.37 µs | 0.87 (1.15×) | re-measure; same floor | 0 B |
| `ListRange` / `Count_` / `ListLastUpdated` / `Range` / `OptionalRange` | 401 / 424 / 219 / 12.7 / 12.8 µs | = | 12.5 / 11.1 / 10.8 / 8.8 / 9.1 µs | 0.03 / 0.03 / 0.05 / 0.69 / 0.71 | within noise of step 2 | 0 B |
| `ListTwoArgWheres` | 10.88 µs | 13.32 µs | 13.59 µs | 1.25 | tuple args copied per candidate through the fused arg predicates (step 2: 1.15) — carried to the filter work | 0 B |
| `Unique*`, `ListTwoWheres`, `ListThreeWheres`, `JoinOne`, `Match`, shapes A / B (+ `Count_`) | — | — | — | within noise of step 2 | replay for A / B | = |

Tests: `FrozenPipelineSeedTests` (16: small-probe on every shape and every argument set against eager
incl. unique-second, key-set-first, collection-backed overlapping buckets, list `In` + unique `In` +
range + filters; the fixed walk kept for range / last-updated / unique first; signals followed across
mutations; the pool path; free seed row-set equality and `Count` on every shape; classic `Sort` byte-
identical with a total comparer and tie groups with a tie comparer across pages; `SortBounded` still
replays; leak balance on every seed path under throwing selector / predicate / comparer / `Clone`; 8
readers against a writer), `FrozenPipelineAllocationTests` +4 (both `list ∩ list` orders, three lists,
`Sort`), the staleness pin extended to the free `Count`. `Prague.Core.Tests/Prepared` 373 in Release
(net9.0), full Core suite in Debug on both frameworks and `Prague.Generated.Tests` 1210 green (numbers
in the commit).


### Stage 3 — step 6: the `SortBounded` feed

*Design:* pipeline design §8 (`SortBounded` → the bounded containers, fixed seed), §7.3 (sort before
the joins), §9 (the planner rule), §13 step 6; the executors' docs in `Pipeline/PipelineExecutor.cs`
and `Pipeline/PipelineJoinedExecutor.cs`.

*What executes.* The pipeline pass now lives in `PipelineCore<TKey,TValue,TArgs>` (bind, seed choice,
seed copy, walk, count, release — a readonly struct held by value), and two thin executors drive the
eager containers with it. **Simple** (`PipelineExecutor`): the eager `ExecuteCoreSimpleTop` gate is
applied per call — `skip >= 0 && take >= 0 && take != int.MaxValue && skip + take <= int.MaxValue` on the
arguments, `TResolver.IsSorter && AllowsBounded && OrdersByLeftValues` decided once at build — and the
pass feeds `TopKSimpleResultContainer` (`Init(seedCount)`, `Add`, `Seal`, `BuildResults`) or, when the
gate fails, `SimpleResultContainer` exactly as before. The seed stays **fixed** for `SortBounded`
(`ReorderIndexNarrowers` is the only way to move it, as for unsorted plans): the bounded container
stamps each row's encounter ordinal as its tie-breaker, and the fixed seed makes those ordinals
eager's, so a page is byte-identical with a tie comparer too. A probe-free plan (one active step, direct
filters) runs `WalkPlain`, a loop as small as the eager store walk, so the container's `Add` chain
inlines. **Joined** (`PipelineJoinedExecutor`): for a chain of one innermost bounded left-value sorter
followed by outer joins only (`Accepts`: the eager `TopKProbeProcessor` conditions plus "no inner
join"; the planner also requires `manyCount == 0`), a finite page runs the pass into
`TopKJoinedBaseContainer`, `Drain`s, `MaterializeTopK`s the page into `JoinedResultContaier` and runs
`ExecuteJoinsBounded` — the join resolvers as they run today, unfused, over the page rows only; an
unbounded or negative page runs the pass into the classic `JoinedResultContaier` and `ExecuteJoins`
(stable sort, crop, joins). `Count` is the pipeline's count (an outer join counts every left). The
resolver chain is copied onto the stack per execution (resolvers carry scratch), as the prepared joined
query does. Inner joins (their narrowing pass reads the eager candidate set), `JoinMany` (design §7.2),
a sort after a join, classic `Sort` before a join and unsorted joins replay until step 5.

*Also in this step.* `ConcurrentCacheStore.TryGetValue` and `InMemoryDataCache.TryGet` carry
`AggressiveInlining` (the store's existing pattern for its hot helpers): the pipeline's per-key lookup
is now the loop body as it is in the store's own bulk walks — `Count_SortBounded_Frozen` 7.5 → 5.4 µs,
`SortBounded_Frozen` 16.3 → 15.0. `JoinedResultContaier.BuildResults` handed its buffer off *before*
`CloneElements`, so a throwing `Clone()` on a pooled cloned joined page stranded the values array (the
eager path too); it now hands off after the clone, the simple container's order.

*Not in this step.* `JoinOne` fusion (step 5 — shape B's two fills are 9.4 of its 26.8 µs), composites
(step 4), inner joins and `JoinMany` on the pipeline.

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3, step 6")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar (§12 / step 6) | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| `SortBounded` (1k bucket, page 0..20) | 15.85 µs | 15.79 µs | 14.96 µs | 0.94 (1.06×) | ≥ 1.1× **missed by a hair** — 1k × (lookup + cold `Score` read + heap compare) both ways; only the candidate-set build is saved | 0 B |
| `SortBounded_ListList` (1k ∩ 100, page 0..20) | 11.49 µs | 11.58 µs | **3.66 µs** | 0.32 (3.1×) | the small-probe seed, eager's page byte for byte | 0 B |
| `Count_SortBounded` | 5.09 µs | 4.95 µs | 5.39 µs | 1.06 | parity — eager's count never dereferences a value | 0 B |
| Shape A `ListListListSortBoundedJoinOne` (page 20..40) | 17.05 µs | 16.84 µs | **7.71 µs** | 0.45 (2.2×) | ≥ 2× kept; the `JoinOne` fill over 20 rows is ~5 µs of it (step 5) | 0 B |
| `Count_` shape A | 10.94 µs | 10.64 µs | **1.41 µs** | 0.13 (7.7×) | free seed, no container | 0 B |
| Shape B `TimeWindowListListSortBoundedJoinTwo` (page 20..40) | 34.04 µs | 33.47 µs | 26.82 µs | 0.79 (1.27×) | ≥ 1.5× **missed** — the two unfused `JoinOne` fills are 9.4 µs (step 5) | 0 B |
| Shape B without its joins (new `TimeWindowListListSortBounded`) | 25.06 µs | — | 17.44 µs | 0.70 (1.44×) | the narrowing floor: the window is already the smallest seed; every key pays a lookup + two selector compares | 0 B |
| Shape B `_FrozenIndexSide` (list steps probed on their buckets) | — | — | 29.07 µs | 0.85 | slower — refutes key-side probing here (design §2.3); value-side default stands | 0 B |
| `Count_` shape B | 21.70 µs | 21.33 µs | **13.99 µs** | 0.64 (1.55×) | ≥ 1.5× kept | 0 B |
| Shape B-range `TimeRangeListListSortBoundedJoinTwo` / `Count_` | 37.77 / 25.09 µs | 37.25 / 25.09 µs | 27.93 / **16.76 µs** | 0.74 / 0.67 | 1.35× / 1.50× — same reading as B (re-measured on the final tree) | 0 B |
| `ListList` / `ListListList` / `Sort_ListList` / `ListWhere` | 9.39 / 11.48 / 10.34 / 8.49 µs | = | 1.70 / 2.18 / 2.14 / 7.42 µs | 0.18 / 0.19 / 0.21 / 0.87 | within noise of step 3 or better | 0 B |

Tests: `FrozenPipelineSortBoundedTests` (11: total and tie comparers byte-identical to eager on list,
list + where(s), list ∩ list / ∩ list ∩ list / ∩ unique (small-probe), range, list ∩ range, key-set,
list ∩ key-set, list ∩ last-updated × every `Execute*` variant × ten pages incl. `take = int.MaxValue`,
skip past the end and `take = 0`, with clone identity; pages partition; `ReorderIndexNarrowers` keeps
the set and the contract; executor selection for the simple and joined rules incl. inner / `JoinMany` /
sort-after-join / classic-`Sort`-before-join → replay and the `Explain()` lines; shapes A and B (both
time-index kinds) on rows and joined values, every variant and page, clone identity of every side,
pages partition across mutations; leak balance under a throwing comparer on the heap, collect-all and
classic paths, a throwing selector, a throwing predicate and a throwing `Clone()` on the simple and
joined pooled cloned pages, eager twin included; 8 readers × a churning writer over the simple and
joined plans), `FrozenPipelineAllocationTests` +2 (`SortBounded` list / list ∩ list pages, the
collect-all page and the classic fallback; shapes A / B pages and A's classic fallback — 0 B each in
Release), `PreparedQueryProductionShapeDifferentialTests` asserts `Pipeline`, the step-3 / stage-1 / 2
executor-selection assertions updated. `Prague.Core.Tests/Prepared` 386 in Release (net9.0), 1099 + 386
Release and the full Core suite in Debug on net9.0, `Prague.Generated.Tests` 1210 green.

### Stage 3 — step 5: `JoinOne` fusion

*Design:* pipeline design §7.1 (fusing `JoinOne`), §7.3 (sort after join), §9 (the planner rule), §13
step 5; the executor's doc in `Pipeline/PipelineJoinedExecutor.cs`, the resolver contract in
`QueryBuilders/IFusableJoinOne.cs`.

*What executes.* A `JoinOne` right lookup is a point read per left, so the pipeline does it itself
instead of letting the resolver build a pair set and run a paired bulk read. The four `JoinOne`
families implement `IFusableJoinOne<TLeftKey,TLeftValue,TRightValue>` — `CanFuse` (true only for
`NoFilter`, since a filter callback is a builder lambda over the paired core) and `TryLookupRight`, one
chain of the resolver's own reads: PK-to-PK → selector → right store; right-unique → right index →
store; left-unique → the left index's `Reverse` → selector → store; left-symmetric → `Reverse` →
optional right index → store. The eager resolvers are untouched; the fill reaches them through two new
`IJoinResolver` members, `UnsafeFillFusedRows` (write this resolver's slot of every row in the
accessor, one lookup per row, clone when the container clones on add, and prune the rows an *inner*
join left without a right) and `UnsafeNarrowFused` (compact a collected `(key, value)` run to the lefts
that have a right). Both are **per-resolver, not per-row**: the chain is walked once per resolver, so
the generic-chain dispatch is paid once instead of per row — the per-row variant measured *slower* than
the unfused paired read (`JoinOne` 42.8 µs against eager's 31.3) and was replaced.

`PipelineJoinedExecutor` now drives two flows. **Classic** (unsorted, a classic `Sort` anywhere, or an
unbounded / negative page): the pass fills the joined container with the lefts, `FillFused(mask)` runs
each fused resolver over every row — an inner one prunes and the total becomes the survivors', which is
what eager's candidate narrowing produces — and `ExecuteJoins(mask)` runs the sorter, the crop and any
unfused resolver. The fill precedes the sorter on purpose: an inner join's rows must be gone before it
sorts. **Bounded** (an innermost `SortBounded` over the left value and a finite page — the eager
`ExecuteCoreJoinedTop` gate): with outer joins only the pass feeds `TopKJoinedBaseContainer` directly;
with an inner fused join it collects the matched pairs into a pooled `RowBuffer`, each inner resolver
compacts them in place, and the survivors enter the heap in that order, so the encounter ordinals — the
bounded tie-breaker — are the ones eager's narrowed base walk stamps. Either way only the page rows are
looked up (§7.3). `Count` is the pass's count for an outer chain and the same collect-and-narrow for an
inner one (keys only, no values, no container).

*Planner.* `FrozenPlanner.Joined` no longer requires a sort: a joined plan takes the pipeline when its
narrowing is a pipeline plan, `manyCount == 0`, there is at most one sorter, and every join either
fuses or — only in the step-6 shape, an innermost bounded left-value sorter followed by outer joins —
keeps its paired read through the `fusedMask` (`ExecuteWithAccessorProcessor` skips the fused
positions; a fill walk runs exactly those). Any `JoinMany`, an unfusable *inner* join, or an unfusable
join outside that shape replays. `Explain()` prints `joins: n (fused: f, unfused: u, …)`.

*One shape is deliberately held back.* An **inner left-symmetric** join's eager fan-out creates the rows
grouped by right key — its pair set is keyed by the lookup key, so every left of a bucket is emitted
together — which a per-left lookup cannot reproduce; the two agree as sets, never as sequences. Stage 3's
rule is that the frozen sequence is eager's unless the caller opts out, so such a chain **replays** and
stays byte-identical, and the new `FrozenOptions.FuseSymmetricInnerJoins` opts into the fused-and-reordered
form exactly as `ReorderIndexNarrowers` does for the seed. Outer left-symmetric joins fuse as normal (their
rows already exist; the fan-out only fills them), and so do inner PK-to-PK / right-unique / left-unique
joins, whose pair sets are seeded from the candidate set in candidate order — which is the pipeline's seed
order, so their sequences match byte for byte. It costs nothing measured: no benchmark row uses an inner
left-symmetric join (`InnerJoinOne` is PK-to-PK, 17.73 µs / 1.96× with the rule in place against 17.68 /
1.94× without it — noise).

*Also in this step.* The four resolvers' inner paths took an eager-core fix: on an empty pair set they
narrowed the candidates to nothing but left the rows an *earlier* chained inner resolver had already
created, so a chain like `InnerJoinOne(a).InnerJoinOne(b)` could emit rows with a default `Left` when
`b` matched nothing. They now run the same `RetainNonNullSlots` post-walk the non-empty path runs
(minimal, additive, eager-visible fix). **No pre-existing test covered it** — the whole eager Core suite
(1099) and `Prague.Generated.Tests` (1214) pass with the fix reverted — so it got its own eager-only pin,
`tests/Prague.Core.Tests/Join/JoinOneChainedInnerEmptyPairSetCoreTests.cs`: three of its six tests
(PK-to-PK, right-unique, left-symmetric-via-right-index) fail with the fix reverted, each with the exact
symptom `rows[i].Left is null`. The other two families reach the branch only defensively — their pair
seeding reads the *left* index, which always emits a pair for a live candidate — and are pinned through
the non-empty-but-no-hits path instead.

*Not in this step.* `JoinMany` (design §7.2), composites (step 4), the joined bounded container's own
cost (see below).

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3, step 5")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar (§12 / step 5) | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| `JoinOne` (1k list → outer left-sym) | 31.67 µs | 30.57 µs | **20.57 µs** | 0.65 (1.54×) | ≥ 1.5× **kept** | 0 B |
| `InnerJoinOne` (1k list → inner PK, ¼ rights missing) | 34.34 µs | 34.05 µs | **17.68 µs** | 0.51 (1.94×) | ≥ 1.5× **kept** | 0 B |
| `JoinOneChained` (two fused joins) | 45.12 µs | 44.78 µs | **26.83 µs** | 0.59 (1.68×) | new row | 0 B |
| `JoinOneFiltered` (fallback) | 21.29 µs | 21.18 µs | 19.77 µs | 0.93 | within noise of `Build()` **kept** | 0 B |
| Shape A `ListListListSortBoundedJoinOne` | 17.23 µs | 16.99 µs | **7.27 µs** | 0.42 (2.37×) | ≥ 2.5× **missed** (step 6: 2.2×) | 0 B |
| `Count_` shape A | 10.87 µs | 10.70 µs | **1.50 µs** | 0.14 (7.2×) | — | 0 B |
| Shape B `TimeWindowListListSortBoundedJoinTwo` | 33.93 µs | 33.69 µs | **24.73 µs** | 0.73 (1.37×) | ≥ 1.5× **missed** (step 6: 1.27×) | 0 B |
| Shape B without its joins | 24.96 µs | — | 17.63 µs | 0.71 | the narrowing floor | 0 B |
| `Count_` shape B | 21.69 µs | 21.55 µs | **12.60 µs** | 0.58 (1.72×) | ≥ 1.5× kept | 0 B |
| Shape B-range `TimeRangeListListSortBoundedJoinTwo` | 36.97 µs | 36.78 µs | **24.49 µs** | 0.66 (1.51×) | ≥ 1.5× **kept** (step 6: 1.35×) | 0 B |

*Why A and B still miss.* Both bars were set from step 6's decomposition, which read the whole
join-vs-no-join delta (9.4 µs on B) as "the two unfused fills". Measured here that was wrong. B
decomposes as `Count` (same narrowing, no container, no joins) **12.60**, plus the *simple* bounded
container and page (the no-join row) **17.63** (+5.03), plus the *joined* bounded container and both
fused fills **24.73** (+7.10) — of which the 40 page lookups are ≈0.3 µs and the fills as a whole ≈2.1 µs
(26.82 → 24.73). The residual ≈6.6 µs is the eager joined bounded container the pipeline drives
unchanged and eager pays identically: a heap of `(key, left, ordinal)` triples compared through
`TopKPairComparer` → `IResolvers.CompareLeftValues<TLeft>`, a generic method reached through the 4-link
nested chain type, plus `MaterializeTopK` into a `ValueDictionary`. The simple container does the same
work for the same rows in 5.03 µs through a direct, devirtualized comparer. Fixing it lifts both engines
together (removing 5 µs from each side of B gives 1.47×); clearing the bar needs a frozen-only bounded
joined container — step 7, not here.

Tests: `FrozenPipelineJoinTests` (10: the four families × outer/inner × identity/selector × the four
`Execute*` variants × eight pages incl. `take = int.MaxValue` and a page past the end, with clone
identity of both sides; missing rights — outer null slot, inner row dropped and `Count` equal to eager's;
chained pairs and triples with every outer/inner mix; `Sort` and `SortBounded` after a fused join over a
joined field and before it; the bounded inner flow with a tie comparer, pages partitioning the whole;
executor selection and `Explain` for every fallback — a filtered `JoinOne` unsorted / after a classic
`Sort` → replay, the same after a `SortBounded` → pipeline with `fused: 1, unfused: 1`, an unfusable
inner → replay, `JoinMany` anywhere → replay, a composite narrowing → replay; leak balance under a
throwing selector / predicate / joined comparer / `Clone()` across the classic, bounded and cloned paths
with the eager twins; 8 readers × a writer churning customers, invoices, shipments and the lefts; and the
inner left-symmetric rule — byte-identical on the default replay path across every variant and page, same
rows and `Count` under `FuseSymmetricInnerJoins`),
`FrozenPipelineAllocationTests` +1 (outer, inner, chained, sort-after-join, bounded-inner — 0 B each),
the eager-only `Join/JoinOneChainedInnerEmptyPairSetCoreTests` (6) for the resolver fix,
`PreparedGeneratedFrozenJoinTests` (4: the generated FK `JoinWith{T}` reverse one-to-one and forward
many-to-one, outer and inner, fused after a generated narrowing step and under a `SortBounded`;
`JoinWith` one-to-many is a `JoinMany` and replays), the stage-1 and step-6 executor-selection
assertions updated for the shapes that now fuse. `Prague.Core.Tests/Prepared` 397 in Release (net9.0),
397 + 1106 in Debug on net9.0, `Prague.Generated.Tests` 1214 green.


### Stage 3 — step 7: the frozen bounded joined container

*Design:* pipeline design §8 (`SortBounded` → the bounded containers), §7.3 (sort after join), §13 step
7; the container's doc in `QueryBuilders/Prepared/Pipeline/FrozenTopKJoinedContainer.cs`, the chain hook
in `QueryBuilders/ResolverChain.cs` (`IResolvers.WithSorter`, `ISorterVisitor`).

*The problem, measured first.* Step 5 read shape B as `Count` **12.60** → the *simple* bounded page
**17.63** (+5.03) → the *joined* bounded page with both fused fills **24.73** (+7.10), and put ≈6.6 µs of
that on the eager joined bounded container's comparison path. Reproduced on this tree before any change:
12.60 → 17.67 → 23.98 (+6.31). A throwaway probe (`BoundedComparerProbeBenchmarks`, kept) runs exactly
the shapes' bounded workload — 100 candidates into a heap of 40 plus the ascending drain, ~600
comparisons — through each comparer alone: **3.86 µs** with the sorter as a struct type parameter, 4.99
through a one-link chain, 7.44 through two (shape A's) and **9.61** through three (shape B's). The
3-link penalty, +5.75 µs, plus the join-free row's 17.67, is 23.42 against the 23.98 measured; the ≈0.6
µs left over is `MaterializeTopK`, the 40 page lookups and the joined `BuildResults`. So the attribution
was right in substance and slightly wrong in detail: `MaterializeTopK` is *not* a meaningful part of it,
the per-link generic hop is. `IResolvers.CompareLeftValues<TLeft>` is a generic method over a reference
`TLeft`, so each link is a constrained call into a `__Canon`-shared body that needs its generic context
and never inlines through to the user comparer — ~2-4 ns per link per comparison. Carrying the key in
the heap triple costs nothing (3.86 vs 3.91 µs).

*What executes.* `IResolvers` gains `WithSorter<TVisitor>(ref TVisitor)`, the sorter-only twin of
`Execute<TExecutor>`: the same JIT-folded `TResolver.IsSorter` test per link that `CompareLeftValues`
uses, walked **once per execution** instead of once per link per comparison, handing the sorter to an
`ISorterVisitor` statically typed. `PipelineJoinedExecutor.ExecuteTop` now builds a `BoundedFeeder` (a
ref struct holding the pass's spans, the args by `ref readonly`, the chain by `ref` and the joined
container as a laundered pointer) and calls `chain.WithSorter(ref feeder)`; inside `Visit<TSorter>` the
whole comparison-bound half runs — `FrozenTopKJoinedContainer<TKey,TValue,TSorter>.Init`, the pass (or,
with an inner fused join, the pooled `RowBuffer` collect + narrow), `Drain`, `MaterializeTopK` — with
every compare going through `TopKSorterPairComparer`, which holds the sorter itself. The new container
is the eager `TopKJoinedBaseContainer` line for line otherwise: the same heap-vs-collect-all plan choice,
the same encounter ordinals, the same `Seal` total, the same `Drain` contract, the same
"the heap buffer never transfers ownership" rule. The eager container is untouched and still serves
`ExecuteCoreJoinedTop`; the only edits to eager types are the two additive `WithSorter` implementations
on `Resolvers<…>`. The gate is unchanged — an unbounded or negative page still takes the classic joined
flow — and so is the inner-narrowing-before-the-heap rule, so tie ordinals stay eager's.

*Not in this step.* `JoinMany` (design §7.2), composites (step 4), the `SortBounded` simple row's 1.1×
floor (RESULTS, step 6).

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3, step 7")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| Shape A `ListListListSortBoundedJoinOne` | 17.06 µs | 16.85 µs | **4.88 µs** | 0.29 (**3.50×**) | ≥ 2.5× **kept** (step 5: 2.37×) | 0 B |
| Shape B `TimeWindowListListSortBoundedJoinTwo` | 33.81 µs | 33.64 µs | **17.85 µs** | 0.53 (**1.89×**) | ≥ 1.5× **kept** (step 5: 1.37×) | 0 B |
| Shape B-range `TimeRangeListListSortBoundedJoinTwo` | 37.10 µs | 37.07 µs | **17.65 µs** | 0.48 (**2.10×**) | ≥ 1.5× **kept** (step 5: 1.51×) | 0 B |
| Shape B without its joins | 25.23 µs | — | 17.62 µs | 0.70 | unchanged — and shape B is now 0.23 µs above it | 0 B |
| `Count_` shape A / B / B-range | 10.74 / 21.66 / 24.50 µs | = | 1.42 / 12.99 / 17.13 µs | 0.13 / 0.60 / 0.70 | no container on this path; drift only | 0 B |
| Shape B `_FrozenIndexSide` | — | — | 20.03 µs | 1.12 vs frozen | still slower — value-side default stands | 0 B |
| `JoinOne` / `InnerJoinOne` / `JoinOneChained` / `JoinOneFiltered` | 30.51 / 33.80 / 44.49 / 21.40 µs | = | 19.97 / 17.69 / 26.79 / 19.76 µs | 0.65 / 0.52 / 0.60 / 0.92 | classic flow, untouched — within noise of step 5 | 0 B |
| `SortBounded` / `SortBounded_ListList` / `ListListList` | 16.12 / 11.62 / 11.37 µs | = | 14.74 / 3.85 / 2.21 µs | 0.91 / 0.33 / 0.19 | simple containers, untouched | 0 B |

The headline: **a joined bounded page now costs what the simple one costs.** Shape B with two joins is
17.85 µs against 17.62 for the same narrowing and page without joins.

Tests: `FrozenPipelineJoinTests` +1 (step 7 — a class comparer and a tying class comparer as the
innermost bounded sorter, one join and a three-join chain, the mixed `fused: 1, unfused: 1` step-6 mask,
all four `Execute*` variants × eight pages incl. `take = int.MaxValue` (the classic fallback), a page past
the end and `take = 0`, byte-identical to eager and to prepared on rows and every joined slot with clone
identity of both sides, `Count` three ways, pages of 1 / 3 / 7 partitioning the eager whole; and a
throwing class comparer on the heap page, the collect-all page and the unbounded classic page with the
eager twin, under `LeakAssert.Balanced`), `FrozenPipelineAllocationTests` +2 pins (the joined bounded page
and its collect-all page with a class comparer — 0 B). Every existing joined `SortBounded` test
(`FrozenPipelineSortBoundedTests`' shapes A / B, ties, pages-partition, leak and 8-reader suites;
`FrozenPipelineJoinTests`' bounded inner / chained / tie-comparer suites; the generated twins) now runs
through the new container unchanged. `Prague.Core.Tests/Prepared` 398 in Release (net9.0), 398 + 1106 in
Debug on net9.0, `Prague.Generated.Tests` 1214 green. No codegen or T4 change.


### Stage 3 — step 4: composites as pipeline steps

*Design:* pipeline design §5 (5.1 `Or`, 5.2 `If` / `Match`), §9 (fallback matrix), §13 step 4; the
step doc in `QueryBuilders/Prepared/Pipeline/Steps/OrStep.cs`, `Pipeline/PipelineTree.cs`,
`Pipeline/PipelinePlanner.cs`.

*What executes.* A composite is either a **node** (`If` / `IfElse` / `Match`, `PipelineTree.cs`) or a
**step** (`Or`, `OrStep.cs`), and both were built once at plan time (`PipelinePlanner`), not carried
over from the descriptor tree at execution.

`If` / `IfElse` / `Match` — bind-time activation (§5.2): `PipelineCore.Bind`, on a plan with a
`PipelineTree`, walks `BindNodes` instead of the flat step loop; a `SelectNode` calls its
`IBranchSelector<TArgs>.Select(in args)` once (an `IfSelector` wraps the descriptor's own
`Func<TArgs,bool>`; a `MatchSelector<TTag>` calls the descriptor's `Func<TArgs,TTag>` then a linear
scan of the arms' tags under `EqualityComparer<TTag>.Default` — the replay's own "first declared tag
wins" rule) and, for the chosen arm (or none), recurses into that arm's nodes — so an untaken arm's
leaf steps are never `Bind`-called and never enter the active list: no probe, no signal read, no cost
in the pass. A `Where` inside an arm becomes a **branch filter** (`FilterNode`, a byte index into
the plan's `FilterStep<TValue,TArgs>[]`), applied by `Walk` after the top-level (fused) filters,
in the taken arm's declared position — order-irrelevant for pure predicates, matching the top-level
fused-filter contract. An unmatched `Match` with no `Default` binds nothing: the replay's own no-op
(`_first` stays untouched, the next narrower seeds).

`Or` (§5.1) — `OrStep<TKey,TValue,TArgs>` carries its branches (each a `PipelineNode<TArgs>[]` plus
the byte indices of its own leaf steps) and is bound through `PipelineCore.BindOr`, not through the
per-step `Bind`: every branch's nodes are walked in **branch mode** (`BindBranch`, mirroring the eager
intersecter core — an empty list `In` span *empties* the branch here, where at the top level it is a
no-op, because the eager branch core's `IntersectValues` on an empty span clears the intersecter), and
the step's own activation and its per-branch outcome (narrowed / empty / active mask / needs-probe)
are packed into its `StepBinding` (two 16-byte binding slots repurposed: a pointer to the frame's
binding array, one first-leaf byte per branch, the active mask, and flags). A branch that is itself a
sole nested `Or` (`b => b.Or(...)`) flattens: its own branches join the parent's branch list without
counting as a narrowing of the parent, reproducing the eager nested `OrWith`'s bit-union without
touching the enclosing branch's `_first`. As a **probe**, a candidate passes when any active branch's
leaves all pass (key-side on the key, value-side on the fetched value) — the eager bitmap mark-then-
prune is a pure set operation, so the row-by-row disjunction of conjunctions is exactly the same set.
As the **seed** (an `Or` that is the query's first narrowing): by default the eager Or-first result —
the whole store walked once, in store order, kept to the union of the active branches' first leaves —
can be reproduced by building that union into a `ValueSet` first, then a second store walk
(`SeedCollectors.Filtered`) copying out only the admitted rows: the eager sequence byte for byte, at one
hash probe per store row — that is `FrozenOptions.OrSeed = false`. **The default is `OrSeed = true`**: the
union is walked directly, in branch order (the design's §5.1 recommendation, confirmed by the reviewer —
the eager order is the store's hash order, which resizes change and no caller can rely on), and so is
every `Count`, classic `Sort`, and any free or small-probe seed that moves the walk off the `Or` itself.
A branch with more than
one active leaf keeps its own probe after either seed (its first leaf is a superset of the branch), via
`OrStep.ProbeAfterSeed`.

*Planner rule* (`PipelinePlanner<TKey,TValue,TArgs>.TryPlan`, replacing stage 3's `FrozenPlanner.
TryPipeline`): walks the descriptor tree once, building the flat step array (every non-composite
narrower, wherever it sits — top level, inside an arm, inside a branch) and, only if any composite is
present, the `PipelineTree` shape. Rejected (replay): more steps or branch filters than the plan's
budget, an `Or` with more than eight branches after flattening, a narrower whose `IPipelineStepSource`
returns null under the options (unmanaged key > 16 B, `IndexSideProbes` with a range step), an `Or`
nested beside another narrower in its branch (only a *sole* nested `Or` flattens — the eager
`OrWith`'s bit-union has no narrowing bit to inherit otherwise), and — the one shape a per-row probe
provably cannot reproduce — a `Range` / `KeySet` / `LastUpdatedAfter` / `LastUpdatedBetween` step
anywhere but the first position of an `Or` branch: the eager branch core *marks* those (unions their
walk into the branch's bitmap, dropping it if the branch was already cleared) where it *intersects* a
preceding unique / list step, a set-level rule with no per-candidate equivalent. First-position range
/ key-set / last-updated steps are unaffected (they still *intersect*, like the top level) and take
the pipeline.

*Ordering.* `If` / `Match` never change order: the taken arm's steps enter the same active list a
top-level step would, at the same position. `Or` as a probe or as a non-first seed changes nothing
either (it is exactly another step in the walk). The one exception to the byte-identical-by-default
contract steps 3/5/6 keep is an `Or` that is the query's **first** narrowing: by default (`OrSeed`) its
rows come in the union's branch order, not the store's hash order — same set, same `Count`; `OrSeed =
false` restores the eager sequence through the filtered store walk. The default was chosen on review
because the eager Or-first order is not a property anyone can build on (it moves whenever the store
resizes) while the cost of reproducing it is the whole store walk (1.38× against 39×).

*The composite-free tax, found and removed.* The first cut of this step taxed plans that carry no
composite at all: the general `Walk` loop had gained a `branchFilters.Length > 0` test **per surviving
row** (always false for a plan without a taken arm's `Where`), and `ChooseSeed` had gained a
`steps[seed].Kind == NarrowerKind.Or` interface call **per execution** (always false for a plan with no
`Or`). Shape B measured +3–6% for it. Both are gone: the branch-filter test is hoisted out of the loop
into the once-per-execution dispatcher — a plan without branch filters runs the pre-step-4 `Walk` /
`WalkPlain` bodies byte for byte, and only a plan whose taken arm has a `Where` runs the new
`WalkWithBranchFilters` — and the `Or` test is gated on a `_hasOr` flag read once at build. Two more
per-execution costs were taken off the composite path itself: `PipelineNode.NodeKind` is a field set
at construction rather than a virtual property (the binder switches on it once per node), and a
`SelectNode` whose selector is the sealed `IfSelector<TArgs>` calls it through a typed field resolved
at build — a direct, inlinable call — instead of through `IBranchSelector<TArgs>` (a `Match` selector
stays behind the interface: it is generic over a `TTag` the node never names). The frame's two new
fields (`ActiveFilters`, `ActiveFilterCount`, 20 bytes beside a ~1 KB binding array) stay: they are
zeroed once per execution by the frame constructor and a flat plan never reads them.

*Not in this step.* Nothing deferred within composites' own scope — `Or` (seed and probe), `If` /
`IfElse` / `Match` (bind-time activation, branch filters, nesting) are all in the pipeline now. The
one carved-out shape (a marking step not first in an `Or` branch) replays, matching eager exactly;
`JoinMany`, an unfusable join, and an inner left-symmetric join inside an `Or` / `If` / `Match` still
replay per steps 5-6's own rules, unaffected by this step.

#### Measured (Apple M4 Pro, .NET 9, `--inProcess`, default job, one category per run; full tables in `RESULTS.MD`, "stage 3, step 4")

| Row | Eager | `Build()` | `BuildFrozen()` | Ratio | Bar (§12, step 4) | Frozen alloc |
|---|---:|---:|---:|---:|---|---|
| `Or` (two 1k buckets, first; default `OrSeed`: the union's order) | 1,027–1,047 µs | 1,015–1,047 µs | **26.6–28.0 µs** | 0.03 (**36.6–39.4×**) | ≥ 1.3× kept | 0 B |
| `Or_FrozenEagerOrder` (`OrSeed = false`: the eager sequence) | — | — | 756–794 µs | 0.72–0.77 (1.29–1.38×) | the opt-out, ungated; two runs straddle 1.3× | 0 B |
| `Count_Or` (always the union) | 976.96 µs | = | **21.94 µs** | 0.02 (**44.5×**) | — | 0 B |
| `OrAfterList` (1k bucket, then `Or` of two uniques — small-probe seed) | 7.16 µs | 7.20 µs | **0.24 µs** | 0.03 (**30.2×**) | ≥ 1.3× kept | 0 B |
| `IfTaken` (1k bucket, then a taken unique — small-probe seed) | 3.08 µs | 3.06 µs | **0.11 µs** | 0.04 (**27.4×**) | ≥ 10× kept | 0 B |
| `IfSkipped` (1k bucket, `If` skipped) | 10.0–10.2 µs | 10.0–10.1 µs | 7.75–8.75 µs | 1.15–1.31× | ≥ 1.4× **missed** — the composite bind path (tree walk + condition delegate) on a plan with almost no walk; not the row loop (RESULTS) | 0 B |
| `Match` (three arms, parameterized) | 8.35–8.63 µs | 8.53 µs | **6.58–7.12 µs** (5 runs, median ≈ 6.9) | 0.79 (**1.19–1.30×**, median ≈ 1.25×) | ≥ 1.2× kept on the median, missed on the slowest run — the row swings ~10% run to run | 0 B |
| `Count_Match` | 6.69 µs | — | 6.30 µs | 0.94 (1.06×) | no bar set; reported | 0 B |
| `ListList`, `ListListList`, `Sort_ListList`, `SortBounded`(+`_ListList`), `JoinOne`, `InnerJoinOne`, `JoinOneChained`, shapes A / B / B-range (+ `Count_` twins) | — | — | — | after the tax fix: shape A 4.92 µs, B **17.64 µs** (step 7: 17.85–17.98), B-range 20.1–20.7 µs — the same row reads **20.33 µs on the pre-step-4 tree on the same machine**, so its 17.65 in the step-7 table was another run's number, not a level step 4 lost | re-measured, no regression |

Tests: `FrozenPipelineCompositeTests` (new: `Or` first/after-a-narrower/small-probe/no-op-branches/
nested-flattening/branch-with-two-narrowers/marking-steps-replay/empty-`In`-in-a-branch, `If` / `IfElse`
first/alone/after-a-list/with-a-`Where`-branch/two-op-branch/nested/`Or`-inside-`If`/`If`-inside-`Or`,
`Match` every arm/default/unmatched/duplicate-tag/nested/inside-`Or`, all × the four `Execute*` variants
× five pages × unsorted/`Sort`/`SortBounded`/fused-joins; `Count` and `OrSeed` set parity; `Explain()`
shape/arm/branch-mask output including the fallback matrix; `LeakAssert.Balanced` under a throwing
condition, tag selector, arm selector, branch predicate, comparer and `Clone()`; eight readers against
a churning writer), `+1` pin group in `FrozenPipelineAllocationTests` (`Composites_...`, all 0 B), and
`PreparedGeneratedFrozenCompositeTests` (the generated `WithXxx` twins of `Or` / `If` / `IfElse` /
`Match`, nested, sort-bounded-joined). `Prague.Core.Tests/Prepared` 429 in Release (net9.0), 429 + 1106
in Debug on net9.0 (one pre-existing unrelated skip), `Prague.Generated.Tests` 1223 green. No codegen or
T4 change.


## 9. Relation to the event-loop research

A `PreparedQuery<TArgs, TResult>` is exactly what a per-cache loop would dequeue: an immutable
description plus arguments, executed on whichever thread owns the structures, producing a
materialized result. Nothing here commits to that model; it just removes the "queries execute as
they are built" obstacle noted in `2026-09-09-single-threaded-event-loop-research.md` §2.

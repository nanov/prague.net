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

## 9. Relation to the event-loop research

A `PreparedQuery<TArgs, TResult>` is exactly what a per-cache loop would dequeue: an immutable
description plus arguments, executed on whichever thread owns the structures, producing a
materialized result. Nothing here commits to that model; it just removes the "queries execute as
they are built" obstacle noted in `2026-09-09-single-threaded-event-loop-research.md` §2.

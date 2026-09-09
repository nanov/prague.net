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

### Stage 2

In order of expected return:

1. **Constant-`Where` fusion** — fold a run of constant predicates into one typed step (and, on the
   replay executor, hand the eager core a single pre-composed predicate so the per-execution `Compose`
   closure on the second `Where` disappears).
2. **Capacity hints** — the descriptors know the index kind; a list-equality plan can size the
   candidate set and the result buffer from the live bucket count instead of the defaults.
3. **Mutation version + memoization** — a per-cache write counter lets a frozen query with bound
   arguments return a cached materialized page while the counter is unchanged.
4. **Materialized sets** — keep a frozen query's candidate set maintained incrementally by the
   cache's write path (the event-loop shape of §9), so execution is a copy-out.

Also open: the bound (`NoArgs`) point lookup is a consistent ~8 ns slower than the parameterized one
and the `NoArgs` forwarders are not the cause; and `PlanInfo` is the natural seat for equality /
hashing (the "identity" half of the deferred item).

## 9. Relation to the event-loop research

A `PreparedQuery<TArgs, TResult>` is exactly what a per-cache loop would dequeue: an immutable
description plus arguments, executed on whichever thread owns the structures, producing a
materialized result. Nothing here commits to that model; it just removes the "queries execute as
they are built" obstacle noted in `2026-09-09-single-threaded-event-loop-research.md` §2.

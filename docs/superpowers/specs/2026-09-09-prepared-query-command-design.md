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
> - **Tests:** `tests/Prague.Core.Tests/Prepared/` (194: differential, reuse, leak, concurrency,
>   allocation parity, `Or`, `If`) and `tests/Prague.Generated.Tests/Prepared/`.
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
- **Struct filter type parameter on the eager core** (`TFilter : struct, IValueFilter<TValue>` in
  `CacheQueryBuilderCoreCombined`). Replaces the per-thread `ArgPredicate` pool and also removes the
  eager `&&` closure allocation on the second chained `Where`. Invasive in the eager core, so its own
  change.
- **Conditional resolvers** — joins or `Sort` / `SortBounded` inside an `If` branch. Decided against
  for now: the resolver chain is part of the *result type*, so a conditional join would have to
  produce a union result shape (or a nullable right side) that the eager builder does not have, and
  parity by construction would be lost. `If` stays a narrowing-only construct.
- **`out long max` last-updated forms.** The eager `UpdatedAfter(..., out long max)` overloads that
  report the newest timestamp seen have no prepared twin; a prepared execution would need a
  per-execution out-channel (a result-side field or an `ExecuteWithMax` terminal).
- **`Or(b1, b2, arg)` state overload.** The eager zero-alloc spelling passes explicit state to
  static branch lambdas; the prepared branches already read the execution arguments, so the overload
  is redundant there and was not mirrored.

## 8. Relation to the event-loop research

A `PreparedQuery<TArgs, TResult>` is exactly what a per-cache loop would dequeue: an immutable
description plus arguments, executed on whichever thread owns the structures, producing a
materialized result. Nothing here commits to that model; it just removes the "queries execute as
they are built" obstacle noted in `2026-09-09-single-threaded-event-loop-research.md` §2.

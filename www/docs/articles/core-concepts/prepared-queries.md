---
title: Prepared and Frozen Queries
---

# Prepared and Frozen Queries

The eager builder (`Query()…Execute()`) plans and executes a query in one breath. A **prepared** query
separates the two: describe the query once, optionally with a parameter struct, store it in a field, and
execute it any number of times from any thread. A **frozen** query goes one step further and plans the
execution once too, binding the description to an executor built for that exact shape.

```csharp
public sealed class ProductSearch {
    private readonly FrozenQuery<(int dept, int brand), JoinResult<Product, ProductInfo?>> _page;

    public ProductSearch(ProductCache products) {
        _page = products.Prepare<(int dept, int brand)>()
            .WithDepartmentId(static a => a.dept)
            .WithBrandId(static a => a.brand)
            .SortBounded(new ByReleaseDateDesc())
            .JoinWithProductInfo()
            .BuildFrozen();                                  // the only allocation
    }

    public int CountPage(int dept, int brand) {
        using var page = _page.ExecutePooled((dept, brand), skip: 0, take: 20);   // 0 B, any thread
        return page.Count;
    }
}
```

`Prepare()` and `Prepare<TArgs>()` exist on every generated cache and on the raw `InMemoryDataCache`
(`UseIndex(index, static a => …)` instead of `WithXxx`). Every `WithXxx` / `WithoutXxx` / `WithKey` /
`UpdatedAfter` overload has a prepared twin taking a `Func<TArgs, T>`. Write the selectors as `static`
lambdas so the delegate is created once, at build.

## Two terminals

| | `Build()` | `BuildFrozen()` |
|---|---|---|
| Returns | `PreparedQuery<TArgs, TResult>` | `FrozenQuery<TArgs, TResult>` |
| Per execution | replays the description into a fresh eager core | runs a pre-bound executor |
| Rows, counts, paging, clone timing, pooling | identical to eager | identical to eager |
| Row order | identical to eager | **not guaranteed unless sorted** (see below) |
| Allocation per pooled execution | 0 B | 0 B |
| When to use | you need the eager sequence byte for byte | everything else |

## The arguments struct

**`TArgs` must be a struct** — a tuple, a `readonly record struct`, a primitive such as `int`, or `NoArgs`
for the parameterless `Prepare()`. A class is a compile error: the whole prepared path is constrained
`where TArgs : struct`, which keeps every generic over it JIT-specialized.

`Execute`, `ExecutePooled`, `Count` and the `*Cloned` variants take `in TArgs` plus the usual `skip` /
`take`. Every selector callback receives `TArgs` by value and runs once per execution, so no spelling
changes there. **One callback is different: the arg `Where` predicate.** It runs once per candidate row,
so it takes `TArgs` by `in` through the `ArgFilter<TValue, TArgs>` delegate, and the lambda must spell
the modifier:

```csharp
.Where(static (p, in a) => p.Price >= a.minPrice)   // required form
.Where(static (p, a) => p.Price >= a.minPrice)      // does not compile (CS1676)
```

Prefer a `readonly struct` or `readonly record struct` for `TArgs`: `in` removes the copy at the call,
but reading a *property* of a non-readonly struct through an `in` reference copies it again inside the
lambda. Field access on a `ValueTuple` is free.

## Branching inside a query

Prepared queries record their structure rather than running it, so a query can branch on its arguments
and the planner still sees every arm.

**`Or`** — the union of two narrowing branches, exactly as in the eager builder.

**`Match`**, in two forms. Every `Match` **must end in a `Default`**: the arm chain is type-state, so an
open chain, an empty one, or a `Case` after `Default` is a compile error, and exactly one arm always runs.

```csharp
// Tag form — a switch. One selector call per execution, then constant compares; first equal tag wins.
.Match(static a => a.Mode, m => m
    .Case(Mode.ByDepartment, b => b.WithDepartmentId(static a => a.dept))
    .Case(Mode.ByBrand,      b => b.WithBrandId(static a => a.brand))
    .Default(b => b.Where(static p => p.Featured)))

// Guard form — if / else if / else. No selector; guards run in order until one is true.
.Match(m => m
    .Case(static a => a.minPrice > 0, b => b.Where(static (p, in a) => p.Price >= a.minPrice))
    .Case(static a => a.inStockOnly,  b => b.Where(static p => p.Stock > 0))
    .Default())                                       // "nothing matched, narrow nothing", stated
```

**`If(cond, branch)` / `IfElse(cond, then, otherwise)`** are sugar over a one-`Case` guard `Match`; their
signatures are unchanged.

What a branch may contain: `Or` branches take `WithXxx` / `UseIndex` (bound or parameterized), nested
`Or`, and narrow-only `If` / `Match`. `If` branches and `Match` arms take all of that plus `Where` and
nested `Or` / `If` / `Match`. Joins, `Sort` / `SortBounded` and the terminal are top-level only.

## What `BuildFrozen()` does

At build time the planner classifies the chain and binds one of three executors:

- **Point lookup** — a unique-index equality followed only by filters: one hash lookup per execution.
- **Pipeline** — everything else it can take. One index step's keys are copied out (the *seed*), every
  other step becomes an O(1) probe on the key or on the fetched value, filters are fused into the pass,
  `Sort` / `SortBounded` feed the top-k container directly, and fusable joins do one right lookup per
  row inside the same pass. No candidate set, no intersection.
- **Replay** — the prepared behaviour, for shapes the pipeline does not take yet (nested `JoinMany`,
  two sorters, a marking step that is not first in an `Or` branch, filter-only plans, and a few more).

`Explain()` prints the plan and its live state: the executor (`executor: Pipeline`, `PointLookup` or
`Replay`), each step with its kind and binding (`step 0 ListEq (bound)`, `step 1 ListEq (arg)`), the
sort mode (`sort: bounded`), which joins fused, and the seed chosen on the last execution with the
signal that chose it — for example `last seed: step 1 ListEq (signal 34), free: smallest signal`, or
under `PreserveEagerOrder` `last seed: step 0 ListEq (signal 0), fixed: first active step`. Read it once
after building; if it says `Replay`, the shape is not on the pipeline yet.

### The ordering contract

**Order is not part of the contract unless you ask for it.** An unsorted frozen result carries no
row-order guarantee, and the ties of a sorted one are unspecified. A `Sort` / `SortBounded` with a
**total** comparer is byte-identical to eager. Everything else is unchanged: same rows, same `Count` /
`TotalCount` / `Truncated`, pages that are slices of the same whole, same clone timing and pooling.

That freedom is what pays. The pipeline seeds from whichever step is narrowest *for this execution's
arguments*, not the one declared first, so a thousand-row bucket followed by a hundred-row bucket walks
the hundred rows whichever way round you wrote it. If you need the eager sequence back, for a snapshot
diffed row by row or a golden test, set `PreserveEagerOrder`:

```csharp
.BuildFrozen(new FrozenOptions { PreserveEagerOrder = true });
```

### `FrozenOptions`

| Flag | Default | Effect |
|---|---|---|
| `Pipeline` | `true` | allow the pipeline executor at all; `false` forces replay |
| `FuseFilters` | `true` | run all `Where` predicates as one fused pass |
| `AdaptiveFilterOrdering` | `true` | reorder fused predicates by their observed selectivity |
| `CapacityHints` | `true` | size result containers from the previous execution |
| `IndexSideProbes` | `false` | probe list steps on the index side instead of the fetched value |
| `PreserveEagerOrder` | `false` | pin the seed to the first declared step and keep the eager sequence everywhere |

## Measured

Raw cache, 100k rows, 1k-row list buckets, Apple M4 Pro, .NET 9, `--inProcess`, pooled and disposed;
every frozen row 0 B per execution (`benchmarks/Prague.Benchmarks/FrozenQueryBenchmarks.cs`, tables in
`RESULTS.MD`).

| Shape | Eager | `Build()` | `BuildFrozen()` | Ratio |
|---|---:|---:|---:|---:|
| unique lookup, parameterized | 132 ns | 129 ns | **18.0 ns** | 7.3× |
| list bucket + `Where` | 8.62 µs | 8.58 µs | **5.84 µs** | 1.5× |
| list ∩ list (1k ∩ 100, largest declared first) | 9.26 µs | 9.11 µs | **1.29 µs** | 7.2× |
| list ∩ list ∩ list (1k ∩ 333 ∩ 111) | 11.2 µs | 11.1 µs | **1.78 µs** | 6.3× |
| list ∩ 60k-row range window | 398 µs | 396 µs | **11.2 µs** | 35× |
| "everything since T" ∩ list | 216 µs | 216 µs | **9.66 µs** | 22× |
| `Or` of two 1k buckets, first narrowing | 976 µs | 983 µs | **26.9 µs** | 36× |
| `SortBounded` page of 20 over list ∩ list | 11.3 µs | 11.4 µs | **3.42 µs** | 3.3× |
| 3 lists → `SortBounded(page)` → `JoinOne` | 17.1 µs | 16.9 µs | **3.77 µs** | 4.5× |
| …the same shape's `Count` | 11.0 µs | 10.8 µs | **1.42 µs** | 7.7× |
| `Sort` over a 1k bucket → `JoinMany` | 118 µs | 119 µs | **14.3 µs** | 8.3× |
| generated `JoinWith{Ref}` (forward many-to-one) | 51.4 µs | — | **25.5 µs** | 2.0× |
| generated reverse one-to-one after `SortBounded(page)` | 31.0 µs | — | **16.5 µs** | 1.9× |

## Rules of thumb

- Store the built query in a field. `Build()` / `BuildFrozen()` is the only allocation; both results are
  immutable and thread-safe.
- Use `BuildFrozen()` unless you need the eager row sequence; then use `Build()` or `PreserveEagerOrder`.
- Make `TArgs` a `readonly record struct`; spell arg `Where` predicates `static (v, in a) => …`.
- Every `Match` ends in `Default(...)` or `Default()`.

See also: [Query Engine](query-engine.md), [Sorted Paging](sorted-paging.md), [Joins](joins.md).

---
name: prague-queries
description: "Query Prague in-memory caches in .NET: cache.Query() lanes and Or, Execute vs ExecutePooled with mandatory Dispose of QueryResults<T>, Sort vs SortBounded paging, Prepare()/Build()/BuildFrozen() with the frozen row-order contract, JoinWith{T}/InnerJoinWith{T}/CountJoinWith{T} joins, DataCacheRegistryBuilder, global last-update queries, the concurrency contract. Use when writing or reviewing code that reads a Prague cache, pages an API over one, builds a FrozenQuery, or joins caches. Declaring caches lives in prague-modeling."
license: MIT
metadata:
  package: Prague
  docs: https://nanov.github.io/prague.net/docs/articles/core-concepts/query-engine.html
---

# Prague: querying

Reads go through the generated `XxxCache` (see `prague-modeling` for declaring it). Verified against Prague 0.6.0; when this file and the code disagree, the code wins. `using Prague.Core;`.

## Eager queries

```csharp
using var orders = cache.Query()
	.WithCustomerId(42)                                   // most selective lane first
	.WithCreatedAtUnixMs(q => q.Gte(yesterdayUnixMs))
	.ExecutePooled();
foreach (var order in orders) { ... }
```

- Each `WithXxx` is a lane. Lanes intersect (AND); the first empty lane short-circuits. On the eager path the lane order is the plan: put the most selective first.
- `WithXxx(T | T? | ReadOnlySpan<T> | T[] | List<T>)`: a null `T?` skips the lane; several values = OR within the lane. `WithKey(...)` narrows by primary key.
- Range lanes: `q => q.Gte(a).Lt(b)` is `[a, b)`. Open with `Gt`/`Gte` or `Lt`/`Lte`, optionally close the other side; a single value is `Gte(x).Lte(x)`, a disjunction the query-level `.Or(...)`. A sentinel-keyed range (`?? long.MinValue`, see `prague-modeling`) opens with `Gt(long.MinValue)` or `Gte(Math.Max(from, long.MinValue + 1))` so the null rows stay out.
- `.Or(b1, b2)` or `.Or(static (b, a) => ..., static (b, a) => ..., arg)`: union of two branches, intersected with the rest. Branches take `WithXxx` / `UseIndex` / nested `Or` only.
- `.Where(v => ...)` filters survivors after all lanes; it never uses an index.
- `.UseIndex(cache.SomeIndex, value)` narrows by an index field; the only form on the raw `cache.Cache`.
- Point lookup without a query: `cache.Cache.TryGet(key, out var value)`; many keys: `cache.Cache.TryGet(keys, span)`.

Terminals:
- `Execute()` / `ExecuteCloned()` `(int skip = 0, int take = int.MaxValue)`: allocating; no dispose needed.
- `ExecutePooled()` / `ExecutePooledCloned()`, same parameters: rented from the pool; **always `using var`**.
- `Count()`: no parameters, materializes nothing.

`QueryResults<T>` is a readonly struct and is the list itself: `Count`, `TotalCount` (before paging), `Truncated`, indexer, `AsSpan()`, `Slice`, `ToArray()`, `ToList()`, `ToDictionary(...)`, ref enumerator. `results.Map(f)` disposes the source and returns a new result of the same pooled kind: dispose the mapped one. Project into a `readonly record struct` row type: it lives inline in the pooled buffer, where a `record class` row is one heap object per row per call. Rows of an unmapped result are the cached instances: read them. To change one, clone, change the clone, write the clone (`prague-modeling`, Changing a cached row); `ExecuteCloned` / `ExecutePooledCloned` return copies.

## Sorting and paging

- `.Sort(cmp)`: full stable sort, for whole results or large takes.
- `.SortBounded(cmp)`: top-k for a page. 2.3-8.6x faster for small `take`; slower and rents O(N) once `take` is about a quarter of the matches or more.
- Both break ties by encounter order. Use a **total** comparer (ending in the primary key, like a generated `XxxCache.{Name}Comparer` whose last column is the key) so pages are identical across plans and runs.
- A generated `XxxCache.{Name}Comparer` (from `[DataCacheSort]`, see `prague-modeling`) is the default: a sealed class with ordinal string columns, reached through `IComparer<T>`, one interface call per comparison and no per-row allocation. A sort that belongs to one endpoint, or a 10k+/s hot path, gets a `readonly struct : IComparer<T>` passed by value: the comparer travels as a type parameter, so `Sort` and `SortBounded` run with no interface dispatch and 0 B. Compare strings in it with `string.CompareOrdinal`.
- After `Sort`/`SortBounded`, only an outer, unfiltered `JoinWith{T}()` binds.

## Prepared and frozen queries

For a hot, repeated query shape: plan once, keep the result in a field (immutable, thread-safe), execute with arguments.

```csharp
private readonly FrozenQuery<(int CustomerId, OrderStatus Status), JoinResult<Order, Customer?>> _page;

_page = orders.Prepare<(int CustomerId, OrderStatus Status)>()
	.WithCustomerId(static a => a.CustomerId)
	.WithStatus(static a => a.Status)
	.SortBounded(OrderCache.ByCreatedAtComparer)
	.JoinWithCustomer()
	.BuildFrozen();

using var page = _page.ExecutePooled((customerId, status), skip: 0, take: 20);   // 0 B
```

Rules (compile errors otherwise):
- `TArgs : struct`: a tuple, `readonly record struct`, `int`, or `Prepare()` for no arguments.
- Selectors are `static a => a.X`. Range: `With{Range}(static (rb, a) => rb.Gte(a.Since))` or `With{Range}(static a => a.From, static a => a.To)` (null = open side).
- An argument `Where` spells `in`: `.Where(static (v, in a) => v.Price >= a.Min)`; stored, it is an `ArgFilter<TValue, TArgs>`.
- Branching: `.If(cond, b => ...)`, `.IfElse(cond, t, e)`, `.Match(static a => a.Mode, m => m.Case(Mode.X, b => ...).Default(b => ...))`. Every `Match` ends with `.Default(...)` or `.Default()`.
- Joins, `Sort`/`SortBounded` and `Build` never go inside a branch; `Where` goes in `If`/`Match` arms, not `Or` branches.
- No `Execute*` on the builder; the terminals are `Build()` and `BuildFrozen()`.

`Build()` → `PreparedQuery`: replays into the eager engine, same rows and order as eager.
`BuildFrozen([FrozenOptions])` → `FrozenQuery`: planned once into a `PointLookup`, `Pipeline` (single pass, 0 B) or `Replay` executor; `Explain()` prints the plan. Both run with `Execute` / `ExecutePooled` / `ExecuteCloned` / `ExecutePooledCloned` `(in args, skip, take)` and `Count(in args)`.

**Frozen ordering contract.** Same rows, `Count`, `TotalCount` and `Truncated` as eager, but an **unsorted** frozen result has no row-order guarantee, and ties in a sorted one are unspecified. With a total comparer the output equals eager. If callers need eager order without a sort: `.BuildFrozen(new FrozenOptions { PreserveEagerOrder = true })`, at about eager cost. Leave the other `FrozenOptions` flags at their defaults.

## Joins

Generated from `[DataCacheForeignKey<T>]` (see `prague-modeling` for which cache gets which method).

```csharp
using var rows = orders.Query()
	.WithCustomerId(42)
	.JoinWithCustomer()                     // outer: rows kept without a customer
	.ExecutePooled();
foreach (var row in rows)
	Console.WriteLine($"{row.Left.OrderId} by {row.Right?.Name ?? "unknown"}");
```

- Rows are `JoinResult<TLeft, T1[, T2, ...]>` with `Left`, `Right` (the first joined slot), `Right2`, `Right3`, and so on.
- Outer joins keep the row with a null or empty slot; `InnerJoinWith{T}()` drops it. `CountJoinWith{T}()` / `InnerCountJoinWith{T}()` turn a `QueryResults` slot into an `int`.
- Filtered: `.JoinWith{T}(q => q.WithStatus(x))` or `.JoinWith{T}(static (q, arg) => ..., arg)`; the callback builder takes `WithXxx` / `UseIndex` / `Or` only.
- At most 5 joins per query. Pooled join results share one buffer: dispose the outer `QueryResults` exactly once.
- Caches from `DataCacheRegistryBuilder` or `AddKafkaCaches` are wired automatically. Hand-built caches need one call first: `orders.AddJoinedCaches(customers, lines, ...)`.

## Registry without Kafka

```csharp
var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // pulls in FK-reachable caches
var orders = registry.GetCache<OrderCache>();
var customers = registry.GetCache<CustomerCache>();
```

`DataCacheRegistry` has no public constructor. The builder wires joins and shared global indexes.

## Changed since a watermark

With `[DataCacheGlobalLastUpdateIndex<TIndex>]` on the cache's primary key: `cache.Query().UpdatedAfter(afterMs [, out long maxMs])` (also `DateTime`/`DateTimeOffset`, and `(after, untilInclusive)`). "After" is strictly greater. On a non-key group key read the index itself: `registry.GetGlobalIndex<TIndex>().Index.GetValuesGt(ms)` returns the changed group keys. Watermarks across caches are not transactional: take the minimum.

## Concurrency

One writer per cache, lock-free readers, no snapshot isolation. A query never throws because of a concurrent write, but it may include or miss a row written mid-query, and two join legs may disagree.

## Common mistakes

| Wrong | Right |
|---|---|
| `ExecutePooled()` without dispose | `using var results = ...ExecutePooled();` |
| `row.Right1` | `row.Right`, then `Right2`, `Right3` |
| `q => q.Between(a, b)`, `q.Eq(x)` | `q => q.Gte(a).Lt(b)`; disjunction via `.Or(...)` |
| `.Map(...)` on the builder | execute first, then `results.Map(...)` |
| `results.Items` | `QueryResults` is the list: `Count`, indexer, `AsSpan()` |
| `.SortByPriority()` | `.Sort(OrderCache.ByPriorityComparer)` or `SortBounded` |
| paging with a non-total comparer | end the comparer with the primary key |
| `Gte(from)` on a sentinel-keyed range with `from = long.MinValue` | `Gt(long.MinValue)`, or clamp `from` to `long.MinValue + 1` |
| `record class` rows from `Map` on a hot path | `readonly record struct` rows |
| `Prepare<MyArgsClass>()` | make `TArgs` a struct, record struct or tuple |
| `.Where((v, a) => ...)` on a prepared builder | `.Where(static (v, in a) => ...)` |
| `Match` without `Default` | end with `.Default(...)` or `.Default()` |
| `Where` / `Sort` / join inside an `Or` branch | move it outside the branch |
| relying on row order of an unsorted `BuildFrozen()` | sort with a total comparer, or `PreserveEagerOrder = true` |
| building a prepared/frozen query per request | build once, store in a field |
| `new DataCacheRegistry()` | `new DataCacheRegistryBuilder().Register<..>().Build()` |
| changing a returned row in place | `row.Clone()`, change the clone, `AddOrUpdate` it |

`Prague.Api` exposes caches over HTTP for inspection: `app.MapPragueEndpoints()` (`GET /prague`, `/prague/{CacheName}`, `/prague/{CacheName}/{id}`); `Prague.Api.UI` adds `AddPragueUI()` / `MapPragueUI()`.

Read next: [query engine](https://nanov.github.io/prague.net/docs/articles/core-concepts/query-engine.html), [sorted paging](https://nanov.github.io/prague.net/docs/articles/core-concepts/sorted-paging.html), [page an API](https://nanov.github.io/prague.net/docs/articles/how-to/page-an-api.html), [prepared queries](https://nanov.github.io/prague.net/docs/articles/core-concepts/prepared-queries.html), [joins](https://nanov.github.io/prague.net/docs/articles/core-concepts/joins.html), [incremental sync](https://nanov.github.io/prague.net/docs/articles/how-to/incremental-sync.html), [ordering and determinism](https://nanov.github.io/prague.net/docs/articles/explanation/ordering-and-determinism.html).

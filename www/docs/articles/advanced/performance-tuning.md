---
title: Performance Tuning
---

# Performance Tuning

Prague is engineered for low-latency reads under sustained write load. This page collects the guidance that lets you actually realize that on the hot path.

## Always use `ExecutePooled`

The pooled execute path uses `ArrayPool.Shared` to lease the result buffer; the allocating path goes through `new T[]`. Under a workload of 100k queries per second the difference is gen-0 GC pressure measured in megabytes-per-second vs. effectively zero.

```csharp
using var results = cache.Query().WithCustomerId(42).ExecutePooled();
```

`using` is non-negotiable — without disposal, the rented array is not returned to the pool and you regress to the allocating path's GC profile (worse, because the pool's internal bag keeps growing).

If you must hold results across an `await` (where `using` doesn't fit cleanly), copy into your own buffer with `ToPooledArray()` and dispose explicitly.

## Sort index order matters

The planner walks `WithXxx` / `UseIndex` calls in declaration order. Place the most selective index first to keep the intermediate set small:

```csharp
// Good — Email is unique, so we narrow to ≤1 candidate immediately
cache.Query()
    .WithEmail(target)                  // unique
    .WithCreatedAtUnixMs(q => q.Gte(t)) // range
    .ExecutePooled();

// Worse — the range walk produces many candidates, then we intersect with the unique hit
cache.Query()
    .WithCreatedAtUnixMs(q => q.Gte(t)) // range scan
    .WithEmail(target)                  // unique intersect
    .ExecutePooled();
```

There is no runtime cost-based optimizer; the order in source is the order in execution.

## Pick the right index variant

- **`Unique`** when the value is genuinely unique. The runtime can short-circuit after the first hit.
- **`Many`** when many entities share a value; the index returns a pooled span.
- **`Range`** when you need ordered traversal (`Gte`, `Between`). Backed by a `PooledBTree`; per-node walks are O(log n).
- **`[DataCacheValueIndex(Eq, K)]`** when you only ever query `Property == K` for a small set of `K`s. The key-set bitmap is faster than a full `Many` index and stores no values.
- **`[DataCacheHasValueIndex]`** when you filter on "has a value at all" (nullable column, soft-delete flag). The bitmap is O(1) and lookup-free.

## Conditional updates

`AddOrUpdate(value)` compares the incoming value to the resident one via `CacheEquals` and short-circuits to `UpdateType.Same` when they're equal — no index churn, no after-handler invocation. Mark monotonic-but-irrelevant fields with `[DataCacheIgnoreEquality]` so they don't force `Update` results on every emit. See [Conditional Updates](../core-concepts/conditional-updates.md).

## Static lambdas in filters

Join filters and key selectors accept a `TArg` form that lets the JIT devirtualize the lambda:

```csharp
// Good — static lambda + arg, zero allocation per call
.JoinWithAuthor(static (q, status) => q.WithStatus(status), authorStatus)

// Worse — closure-capturing lambda; allocates each call
.JoinWithAuthor(q => q.WithStatus(authorStatus))
```

The same applies to `WithKeyFilter` / `WithValueFilter` predicates and join key selectors — prefer `static` lambdas that close over no state.

## Avoid `ToList()` / `ToArray()`

Both copy the pooled buffer into a regular allocation. Iterate the `QueryResults<T>` directly or use `AsSpan()` for span-based math:

```csharp
using var prices = orderCache.Query().WithCustomerId(42).ExecutePooled();
decimal sum = 0;
foreach (var o in prices.AsSpan())
    sum += o.Total;
```

For interop with `IEnumerable<T>` APIs, prefer `AsSpan()`-backed iteration over `ToList()` if the consumer accepts spans.

## Sort over candidates, not the cache

`Sort` runs after intersection on the candidate set, not on the full cache. Prefer narrowing before sorting:

```csharp
// Good — sort ~100 candidates
cache.Query().WithCustomerId(42).Sort(OrderCache.ByDateComparer).ExecutePooled(0, 25);

// Wasteful — sort millions
cache.Query().Sort(OrderCache.ByDateComparer).ExecutePooled(0, 25);
```

The second form *will* sort the entire cache before slicing the top 25.

## Stats and observability

Each cache exposes `Statistics`:

```csharp
var stats = cache.Statistics;
Console.WriteLine($"size={stats.LiveSize} indices={stats.Indexes.Count}");
foreach (var (name, info) in stats.Indexes)
    Console.WriteLine($"  {name}: {info.LiveKeysSize}/{info.LiveValuesSize}");
```

`LiveSize` / `LiveKeysSize` / `LiveValuesSize` are read-through (no snapshot). For periodic monitoring, call `DataCacheStatisticsMarshall.TakeSnapshot(stats)` and read `stats.Size` / `stats.Indexes[name].KeysSize`.

Kafka caches additionally expose `KafkaDataCacheStatistics` with consumer lag, last poll time, and live message counters. The OpenTelemetry integration in `Prague.Kafka.OpenTelemetry` exports these as metrics — register it via `services.AddPragueKafkaInstrumentation()`.

## Benchmarks

The reference benchmarks live in `benchmarks/Prague.Benchmarks`. Run them as a sanity check on your hardware before drawing conclusions from production numbers:

```bash
dotnet run -c Release --project benchmarks/Prague.Benchmarks
```

The headline reads/sec number (~15.9M) comes from the concurrent `HeavyJoinPooledBenchmarks` (10 reader threads + concurrent writers, pooled 3-level join) — it is the sum of result rows returned divided by wall-clock seconds. The `<100ns` indexed-lookup figure is a single-key lookup on a single physical core. Both use `BenchmarkDotNet` overhead corrections and were measured on an Apple M4 Pro; absolute numbers vary by hardware, so scale them down for shared-tenancy machines.

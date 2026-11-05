---
title: Performance Tuning
description: Hot-path guidance for Prague - pooled results, frozen queries, index order and choice, SortBounded, static lambdas, statistics and benchmarks.
---

# Performance Tuning

Prague is built for low-latency reads under sustained write load. This page collects the habits that let you realize that on the hot path. Every block runs against the sample domain and is asserted in `samples/Prague.Samples/Advanced/PerformanceTuning.cs`.

## Always use `ExecutePooled`

The pooled execute path rents the result buffer from `ArrayPool<T>`; the allocating path goes through `new T[]`. Under a workload of 100k queries per second the difference is gen-0 GC pressure measured in megabytes per second versus effectively zero.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#execute-pooled)]

`using` is non-negotiable — without disposal the rented array never returns to the pool and you regress to the allocating path's GC profile (worse, because the pool keeps handing out fresh arrays).

If you must hold rows across an `await`, where `using` does not fit, copy them out and let the pooled result go at once. `ToArray()` copies into a regular array and disposes the result. `ToPooledArray(ArrayPool<Order>.Shared)` moves the rows into a buffer rented from a pool *you* name, so you can return it when you are done:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#hold-across-await)]

The parameterless `ToPooledArray()` rents from Prague's internal pool, which you cannot return to; use it only when the caller owns the array for the rest of the process. Returning either array to a pool it did not come from corrupts that pool.

## Freeze hot, repeated shapes

A query that runs the same shape over and over belongs in a field as a frozen query. `Prepare<TArgs>()…BuildFrozen()` plans once, binds a `PointLookup` / `Pipeline` / `Replay` executor, and executes at 0 B from any thread — 3–36× over the eager builder on indexed shapes, 18 ns for a unique-key point lookup. Row order is not guaranteed unless you sort with a total comparer. See [Prepared and Frozen Queries](../core-concepts/prepared-queries.md).

## Index order matters on the eager path

The eager planner walks `WithXxx` / `UseIndex` calls in declaration order and intersects as it goes. Put the most selective lane first so the intermediate set stays small:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#index-order)]

There is no cost-based optimizer on the eager path; the order in source is the order in execution. A frozen query seeds from the narrowest lane for each execution's arguments, whichever way round you declared them.

## Pick the right index variant

- **`Unique`** when the value is genuinely unique. One hash lookup, at most one candidate.
- **`Many`** (the `[DataCacheIndex]` default) when many entities share a value; the lane returns the key's whole bucket.
- **`Range`** when you need ordered traversal (`q => q.Gte(a).Lt(b)`). Backed by a pooled B+tree; walks are O(log n) to the first bound, then sequential.
- **`[DataCacheValueIndex(CompareOp.Equal, K)]`** when you only ever query `Property == K` for a small set of `K`s. The key set stores keys only and the lane is one set lookup.
- **`[DataCacheHasValueIndex]`** / **`[DataCacheHasNotValueIndex]`** when you filter on "has a value at all" (nullable column, soft-delete flag). Same key-set mechanism.

See [Index Types](../core-concepts/index-types.md) for the full catalogue.

## Conditional updates

`AddOrUpdate(value)` compares the incoming value to the resident one via the generated `CacheEquals` and short-circuits to `UpdateType.Same` when they are equal: no index churn, and after-handlers receive `Same`, which a projector ignores in one branch. Mark monotonic-but-irrelevant fields with `[DataCacheIgnoreEquality]` so they do not force `Update` results on every emit. See [Conditional Updates](../core-concepts/conditional-updates.md).

## Static lambdas in join filters

Every `JoinWith{X}(filter)` has a `TArg` twin that passes a value into the filter instead of capturing it:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#join-filter-static)]

The eager builder rebuilds the filter on every query execution, so a captured local costs a closure allocation per call. A `static` lambda plus an argument is compiled once into a static field and allocates nothing.

## Static lambdas in Kafka filters

The rationale is different on the ingestion side. A `WithKeyFilter` / `WithValueFilter` predicate is constructed **once**, while the handler is built, and held for the process lifetime — a capturing closure there allocates at startup, not per message. What you still want is a predicate that reads DI state without resolving or capturing anything on the hot path, and the state-factory overloads give you exactly that:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#filters-di-state)]

State arrives as an argument rather than a capture, so the lambda can be `static` and the ingestion path allocates nothing. See [Kafka Integration](kafka-integration.md#filters) for the full set of overloads and their lifecycle caveats.

## Avoid `ToList()` / `ToArray()`

Both copy the pooled buffer into a regular allocation. Iterate the `QueryResults<T>` directly, or use `AsSpan()` for span-based math:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#avoid-tolist)]

For interop with `IEnumerable<T>` APIs, prefer `AsSpan()`-backed iteration over `ToList()` whenever the consumer accepts a span.

## Sort over candidates, not the cache — and page with `SortBounded`

Sorting runs after intersection on the candidate set, not on the full cache. Narrow before sorting, and use `SortBounded` for a page: it keeps the top `skip + take` rows instead of sorting everything and is 2.3–8.6× faster than `Sort` when `take` is a small slice of the matches. Use `Sort` when you take a quarter or more of the result, or the whole of it. Both plans allocate 0 B per query with a `readonly struct` comparer; the generated `ByXxxComparer` fields are `IComparer<T>` references, which cost an interface call per comparison but still no per-row allocation. Both break ties by encounter order, so they return the same rows in the same order — the choice is purely cost.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#sort-bounded-page)]

`TotalCount` on the page reports the matches before paging (20 for the one-customer query, 30 for the whole-cache one). See [Sorted Paging](../core-concepts/sorted-paging.md) for the measured crossover.

## Stats and observability

Each cache exposes `Statistics`. The default constructor wires a no-op collector so the counters cost nothing; pass `collectStatistics: true` to turn them on:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/PerformanceTuning.cs#statistics)]

`LiveSize` / `LiveKeysSize` / `LiveValuesSize` are read-through (no snapshot). For periodic monitoring call `DataCacheStatisticsMarshall.TakeSnapshot(stats)` and read `stats.Size` / `stats.Indexes[name].KeysSize` / `.ValuesSize`. `KeysSize` is exact; `ValuesSize` is an approximation of the values held under all keys of a `Many` index — alert on trends, not on the exact figure.

On the Kafka side, `KafkaCachesStatistics` (injectable) holds one `KafkaCachesConsumerStatistics` per consumer (assigned partitions, broker latency, messages and bytes received, initial-load time) and, under it, one `KafkaDataCacheStatistics` per cache (topic name, size, messages received, fetch-queue depth). `Prague.Kafka.OpenTelemetry` exports these as metrics — register it on the meter provider with `.WithMetrics(m => m.AddPragueKafkaInstrumentation())`. The `GET /prague` endpoint of `Prague.Api` serves the same statistics as JSON. See [Kafka Integration](kafka-integration.md#opentelemetry-metrics).

## Benchmarks

The reference benchmarks live in `benchmarks/Prague.Benchmarks`. Run them as a sanity check on your hardware before drawing conclusions from production numbers:

```bash
dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories <category>
```

Run one category at a time with nothing else on the machine; rows in the 5–10 µs range move more than the deltas usually being claimed. Every measured table lives in `benchmarks/Prague.Benchmarks/RESULTS.MD`, and `perf/` holds a regression tripwire against per-machine baselines.

The headline reads/sec number (11M+) comes from the concurrent `HeavyJoinPooledBenchmarks` (10 reader threads + concurrent writers, pooled 3-level join) — it is the sum of result rows returned divided by wall-clock seconds. The `<100 ns` indexed-lookup figure is a single-key lookup on a single physical core; a frozen unique-key point lookup is 18 ns. All figures are from an Apple M4 Pro; absolute numbers vary by hardware, so scale them down for shared-tenancy machines.

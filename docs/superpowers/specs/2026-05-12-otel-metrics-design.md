# OpenTelemetry Metrics for Prague Kafka

**Status:** Draft
**Date:** 2026-05-12
**Scope:** Add OpenTelemetry metric instrumentation for the Kafka consumer/cache subsystem of Prague, sourced from existing in-memory statistics. No new measurement instrumentation on hot paths.

---

## Goal

Expose a minimal, alert-friendly set of OpenTelemetry metrics for each Kafka consumer and each cache it owns, plus index sizes, drawn entirely from statistics already collected on `KafkaCachesConsumerStatistics`, `KafkaDataCacheStatistics`, and `DataCacheIndexStatistics`. All instruments are observable (callback-based) — measurements happen only when an exporter pulls. Zero hot-path cost.

Consumers wire Prague into their existing OpenTelemetry pipeline:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddPragueKafkaInstrumentation()       // → "prague.kafka.*"
        .AddPrometheusExporter());
```

The default prefix is empty (`""`). Pass a prefix to override:

```csharp
m.AddPragueKafkaInstrumentation();             // → "prague.kafka.*"
m.AddPragueKafkaInstrumentation("acme.");      // → "acme.prague.kafka.*"
```

---

## Metric set

Seven instruments. All are observable. Tag keys are stable; tag values come from existing names (consumer cluster name, cache type name, index name).

### Per consumer (tags: `consumer`)

| Metric | Kind | Unit | Source |
|---|---|---|---|
| `{prefix}prague.kafka.consumer.partitions.assigned` | ObservableUpDownCounter\<long\> | `{partition}` | `KafkaCachesConsumerStatistics.AssignedPartitions` |
| `{prefix}prague.kafka.consumer.broker.rtt.p99` | ObservableGauge\<long\> | `ms` | `KafkaCachesConsumerStatistics.BrokerLatencyMs` (max p99 across brokers, librdkafka `brokers.*.rtt.p99` µs → ms) |
| `{prefix}prague.kafka.consumer.health` | ObservableGauge\<int\> | (`0` / `1`) | Derived: `1` when healthy, `0` when any of `IsFatalLatched` / `HasLostPartitions` / poll-stall is true. Poll-stall = `Stopwatch.GetElapsedTime(LastPollTimestamp) > KafkaCachesHealthOptions.PollLoopHeartbeatTimeout` |

### Per cache (tags: `consumer`, `cache`)

| Metric | Kind | Unit | Source |
|---|---|---|---|
| `{prefix}prague.kafka.cache.size` | ObservableUpDownCounter\<long\> | `{item}` | `KafkaDataCacheStatistics.LiveSize` (current row count, not snapshot) |
| `{prefix}prague.kafka.cache.messages.received` | ObservableCounter\<long\> | `{message}` | `KafkaDataCacheStatistics.TotalMessagesReceived` (cumulative, from librdkafka per-topic `rxmsgs`) |
| `{prefix}prague.kafka.cache.health` | ObservableGauge\<int\> | (`0` / `1`) | Derived: `1` when healthy, `0` when any of `IsLoopFaulted` / `AssignedPartitionCount == 0` / processing-timeout / initial-load-incomplete is true. Processing-timeout = `LastProcessingStartTimestamp != 0 && elapsed > KafkaCachesHealthOptions.HandlerProcessingTimeout`. Initial-load-incomplete = registry's loading completion not yet resolved AND `InitialLoadTime == default`. |

### Per index (tags: `consumer`, `cache`, `index`, `kind=keys|values`)

| Metric | Kind | Unit | Source |
|---|---|---|---|
| `{prefix}prague.kafka.cache.index.size` | ObservableUpDownCounter\<long\> | `{item}` | `DataCacheIndexStatistics.LiveKeysSize` (when `kind=keys`) / `LiveValuesSize` (when `kind=values`) |
| `{prefix}prague.kafka.cache.index.capacity` | ObservableUpDownCounter\<long\> | `{slot}` | `DataCacheIndexStatistics.LiveCapacitySlots` — slots currently rented by the index's backing sets, maintained from PooledSet cold paths (first allocation / Grow / Dispose; zero hot-path cost). No `kind` tag — capacity is one number per index. Utilization = `size{kind=values}` / `capacity`; a persistently low ratio on a large index is the slack alarm that previously required a memory dump to see. Reports 0 for index kinds that do not track slot capacity (range/unique). |

### Health semantics

Both health gauges follow Prometheus `up`-style: `1` = healthy, `0` = unhealthy. No reason tag — the metric is a binary alarm; consumers wanting the cause go to the `/health` endpoint, whose `HealthCheckResult.Data` already carries the failure reasons.

Alert rules:
- `min by (consumer) (prefix_prague_kafka_consumer_health) == 0`
- `min by (consumer, cache) (prefix_prague_kafka_cache_health) == 0`

### What we deliberately do **not** expose

| Signal | Reason for exclusion |
|---|---|
| `InitialLoadTime` (per consumer & per cache) | One-shot duration, useless as a time series. Still collected on stats for diagnostic surfaces. |
| `consumer.brokers.up` | Already folded into `consumer.health` (when 0 brokers up, consumer is unhealthy via the predicates the health endpoint computes). Reduces redundancy. |
| `consumer.broker.throttle` / `consumer.broker.queue_latency` | Throttle is rare (quotas-only). Queue latency is producer-leaning. `rtt.p99` is the single best "is the broker healthy" signal. |
| `consumer.caches.loading` | Covered by `cache.health` predicate `initial_load_incomplete`. |
| `consumer.poll.age` | Covered by `consumer.health` predicate `poll_stalled`. |
| `cache.partitions.assigned` | With 1 partition per topic in current deployments, this is binary; the zero-partitions case is already covered by `cache.health` predicate `no_partitions`. |
| `cache.fetchq.messages` / `cache.fetchq.bytes` / `cache.fetch_state` | Operational backpressure signals; with healthy 1-partition topics they're either zero or noisy. Health predicates cover the alert-worthy cases. (Note: today these fields only capture the first partition's value — a pre-existing limitation unrelated to this work.) |
| `cache.handler.processing.age` | Folded into `cache.health` predicate `processing_timeout`. |
| `bytes.received` (consumer or cache) | Operationally low-value vs. `messages.received`; tail of network rate is rarely actionable. |
| `consumer.messages.received` | Sum of `cache.messages.received{consumer=X}` — derivable in any backend, avoids double-counting. |

---

## Wiring

### Public API

Single extension method, in a dedicated project so the OTel package dependency is opt-in:

**Same project** (`Prague.Kafka`), new namespace `Prague.Kafka.OpenTelemetry`. Add `OpenTelemetry.Api` package reference to `Prague.Kafka.csproj`. (Decision: keep one project for now; split later if the transitive dep becomes a concern.)

```csharp
namespace Prague.Kafka.OpenTelemetry;

public static class PragueKafkaInstrumentationExtensions {
    public static MeterProviderBuilder AddPragueKafkaInstrumentation(
        this MeterProviderBuilder builder, string prefix = "") {

        builder.AddMeter(PragueKafkaMetricsReporter.MeterName);
        builder.AddInstrumentation(sp => new PragueKafkaMetricsReporter(
            sp.GetRequiredService<KafkaCachesStatistics>(),
            sp.GetRequiredService<IOptions<KafkaCachesHealthOptions>>().Value,
            sp.GetRequiredService<IDataCacheRegistry>(),
            prefix));
        return builder;
    }
}
```

`KafkaCachesStatistics` is already registered as a singleton by `AddKafkaCaches`. `KafkaCachesHealthOptions` provides the `PollLoopHeartbeatTimeout` / `HandlerProcessingTimeout` thresholds for the health predicates. `IDataCacheRegistry.LoadingCompletion` is used to detect initial-load-incomplete.

### Reporter

```csharp
namespace Prague.Kafka.OpenTelemetry;

internal sealed class PragueKafkaMetricsReporter : IDisposable {
    internal const string MeterName = "Prague.Kafka";
    private static readonly string MeterVersion =
        typeof(PragueKafkaMetricsReporter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(PragueKafkaMetricsReporter).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private readonly Meter _meter;
    private readonly KafkaCachesStatistics _statistics;
    private readonly KafkaCachesHealthOptions _healthOptions;
    private readonly IDataCacheRegistry _registry;

    // Cached TagLists — no per-measurement allocations.
    private readonly QuickLookupCache<string, TagList> _consumerTags = new();
    private readonly QuickLookupCache<(string consumer, string cache), TagList> _cacheTags = new();
    private readonly QuickLookupCache<(string consumer, string cache, string index, bool isKeys), TagList> _indexTags = new();

    // Reused Measurement buffers — sized to the current scope counts.
    private Measurement<long>[] _consumerLongBuffer = Array.Empty<Measurement<long>>();
    private Measurement<int>[]  _consumerIntBuffer  = Array.Empty<Measurement<int>>();
    private Measurement<long>[] _cacheLongBuffer    = Array.Empty<Measurement<long>>();
    private Measurement<int>[]  _cacheIntBuffer     = Array.Empty<Measurement<int>>();
    private Measurement<long>[] _indexLongBuffer    = Array.Empty<Measurement<long>>();

    public PragueKafkaMetricsReporter(
        KafkaCachesStatistics statistics,
        KafkaCachesHealthOptions healthOptions,
        IDataCacheRegistry registry,
        string prefix) {

        _statistics = statistics;
        _healthOptions = healthOptions;
        _registry = registry;
        _meter = new Meter(MeterName, MeterVersion);

        var p = prefix; // alias for readability

        _meter.CreateObservableUpDownCounter($"{p}prague.kafka.consumer.partitions.assigned",
            ObserveConsumerPartitionsAssigned, "{partition}",
            "Number of partitions currently assigned to this consumer");

        _meter.CreateObservableGauge($"{p}prague.kafka.consumer.broker.rtt.p99",
            ObserveConsumerBrokerRttP99, "ms",
            "Max-of-p99 broker round-trip time across brokers");

        _meter.CreateObservableGauge($"{p}prague.kafka.consumer.health",
            ObserveConsumerHealth, null,
            "1 when consumer is healthy, 0 when any of fatal / partitions_lost / poll_stalled");

        _meter.CreateObservableUpDownCounter($"{p}prague.kafka.cache.size",
            ObserveCacheSize, "{item}",
            "Current row count of the cache");

        _meter.CreateObservableCounter($"{p}prague.kafka.cache.messages.received",
            ObserveCacheMessagesReceived, "{message}",
            "Cumulative messages received from Kafka for this cache's topic");

        _meter.CreateObservableGauge($"{p}prague.kafka.cache.health",
            ObserveCacheHealth, null,
            "1 when cache is healthy, 0 when any of loop_faulted / no_partitions / processing_timeout / initial_load_incomplete");

        _meter.CreateObservableUpDownCounter($"{p}prague.kafka.cache.index.size",
            ObserveIndexSize, "{item}",
            "Current key/value count of the index");
    }

    // Callback signatures:
    //   Measurement<T>[]  ObserveXxx() — returns the reused buffer trimmed to actual count
    //   Each callback acquires TagList via QuickLookupCache.GetOrAddRef (ref readonly, no copy)
    //   The reporter snapshots dict references; mutation (new consumers/caches) is rare (init-time)

    public void Dispose() => _meter.Dispose();
}
```

### Health predicate evaluation

The two health observables compute the same predicates that `KafkaCachesHealthEvaluator` uses, but without surfacing the reason:

**Consumer healthy iff**
- `!stats.IsFatalLatchedUnsafe`
- `!stats.HasLostPartitionsUnsafe`
- `Stopwatch.GetElapsedTime(stats.LastPollTimestampUnsafe) <= _healthOptions.PollLoopHeartbeatTimeout`

**Cache healthy iff**
- `!cacheStats.IsLoopFaultedUnsafe`
- `cacheStats.AssignedPartitionCountUnsafe > 0`
- `cacheStats.LastProcessingStartTimestampUnsafe == 0 || Stopwatch.GetElapsedTime(cacheStats.LastProcessingStartTimestampUnsafe) <= _healthOptions.HandlerProcessingTimeout`
- `_registry.LoadingCompletion.IsCompletedSuccessfully` (initial load done for at least this cache — best surrogate; per-cache initial-load completion is implicit when registry resolves)

**Shared predicate helper (in-scope refactor).** To avoid drift between the health checks' verdict construction and the metrics reporter's bool evaluation, factor the individual predicates out of `KafkaCachesHealthEvaluator` into a new static class:

```csharp
namespace Prague.Kafka.Health;

public static class KafkaCachesHealthPredicates {
    // Consumer-level
    public static bool IsFatal(KafkaCachesConsumerStatistics s) => s.IsFatalLatched;
    public static bool IsPollStalled(KafkaCachesConsumerStatistics s, TimeSpan t)
        => Stopwatch.GetElapsedTime(s.LastPollTimestamp) >= t;
    public static bool HasLostPartitions(KafkaCachesConsumerStatistics s) => s.HasLostPartitions;
    public static bool HasBrokersDown(KafkaCachesConsumerStatistics s, int min) => s.BrokerUpCount < min;
    public static bool HasIncompleteInitialLoad(KafkaCachesConsumerStatistics s) => s.CachesLoadingCount > 0;

    // Per-cache
    public static bool IsLoopFaulted(KafkaDataCacheStatistics c) => c.IsLoopFaulted;
    public static bool HasProcessingTimeout(KafkaDataCacheStatistics c, TimeSpan t)
        => c.LastProcessingStartTimestamp != 0 && Stopwatch.GetElapsedTime(c.LastProcessingStartTimestamp) >= t;
    public static bool HasNoPartitionAssigned(KafkaDataCacheStatistics c) => c.AssignedPartitionCount < 1;

    // Aggregates — used by metrics reporter
    public static bool IsConsumerHealthy(KafkaCachesConsumerStatistics s, KafkaCachesHealthOptions opts)
        => !IsFatal(s) && !IsPollStalled(s, opts.PollLoopHeartbeatTimeout)
        && !HasLostPartitions(s) && !HasBrokersDown(s, opts.MinBrokersUp)
        && !HasIncompleteInitialLoad(s);

    public static bool IsCacheHealthy(KafkaDataCacheStatistics c, KafkaCachesHealthOptions opts)
        => !IsLoopFaulted(c) && !HasNoPartitionAssigned(c)
        && !HasProcessingTimeout(c, opts.HandlerProcessingTimeout);
}
```

`KafkaCachesHealthEvaluator.Evaluate()` is refactored to call the individual predicate functions (preserves "which one failed" for the verdict's failure list). The metrics reporter calls only the aggregate `IsConsumerHealthy` / `IsCacheHealthy` — zero allocations, single source of truth. Adding a new predicate later updates both consumers automatically.

**Per-scrape evaluation cost.** Within one scrape, predicates are evaluated once per consumer (consumer.health callback) and once per cache (cache.health callback). All other instrument callbacks read existing fields directly without touching predicates. Total: `O(C + C×N)` bool computations per scrape, all volatile reads, zero allocations.

### Hot-path discipline

Per the house performance rules (`high-performance-net`):

- No `Counter.Add` on the producer/consumer paths. All instruments are observable.
- No `string` allocations during callback — `TagList` cached per dimension combo via `QuickLookupCache.GetOrAddRef` (returns `ref readonly`).
- No per-callback enumeration allocation — preallocated `Measurement<long>[]` / `Measurement<int>[]` buffers grown only when scope counts grow.
- No `IEnumerable<Measurement<T>>` LINQ chains. Callbacks return `ArraySegment<Measurement<T>>` or use the `Action<Instrument, Measurement<T>>` callback overload to write directly into the measurement batch.
- Single static `Meter` instance per process. `Dispose` only at provider teardown.

The expected callback cost when the exporter scrapes (e.g., 10 s Prometheus interval) is bounded by:
`O(consumers) + O(caches) + O(caches × indexes × 2)` reads of pre-existing volatile fields. No locks, no allocations after warm-up.

---

## QuickLookupCache port

Port `QuickLookupCache<TKey, TValue>` from `Prague.Orders.IdGenerator.Internal` into:

**`src/Prague.Core/Collections/QuickLookupCache.cs`** — namespace `Prague.Core.Collections`, visibility `internal`.

Carry over:
- `GetOrAdd<TParam>(TKey, Func<TKey, TParam, TValue>, TParam)`
- `GetOrAddRef<TParam>(TKey, Func<TKey, TParam, TValue>, TParam)` returning `ref readonly TValue`
- `Snapshot` property exposing the underlying `FrozenDictionary` (used for debug/inspection — keep for parity)

Drop:
- `QuickLookupCacheConverterFactory` / `QuickLookupCacheConverter` (Prague doesn't use this type for JSON serialization)

Same CAS loop, same double-checked read, same XML doc. After this lands, the three inline copy-swap sites (`KafkaCachesStatistics.GetOrAddConsumer`, `KafkaCachesConsumerStatistics.AddCache`, `DataCacheStatistics.AddIndex`) become candidates for consolidation in a follow-up — out of scope for this work but enabled by it.

---

## Cardinality budget

| Time series | Cardinality formula | Typical |
|---|---|---|
| Consumer metrics (3 × `{consumer}`) | `3 × C` | 3–9 (1–3 clusters) |
| Cache metrics (3 × `{consumer, cache}`) | `3 × C × N` | 30–300 |
| Index metric (1 × `{consumer, cache, index, kind}`) | `1 × C × N × I × 2` | 100–2000 |

Where `C` = consumers, `N` = caches per consumer, `I` = avg indexes per cache. All dimensions are bounded at compile/configuration time — no data-driven label cardinality, no per-message tags.

---

## Out of scope

- Tracing (`ActivitySource`). Can be added later via a sibling `Add*Tracing()` extension.
- Producer-side metrics. The current statistics infrastructure only tracks consumer/cache state; producers are a separate concern.
- Fixing the per-topic fetch-queue first-partition-only aggregation in `KafkaCacheConsumer.UpdateTopicStats`. Tracked separately; doesn't matter while topics have 1 partition.
- Reducing the three inline copy-swap idioms to use `QuickLookupCache`. Tracked separately.
- Disposing the `Meter` on consumer/cache teardown beyond reporter disposal. The static `Meter` lives as long as the process; reporter holds it as an instance for testability.

---

## Open questions for review

1. **Separate project (`Prague.Kafka.OpenTelemetry`)** vs. inline in `Prague.Kafka`. Spec assumes separate; flip if you'd rather not split.
2. **Health predicate factoring** — does extracting `KafkaCachesHealthPredicates` belong in this work or a prior refactor? Spec assumes in-scope.
3. **Meter version string** — pin to the lib's `AssemblyInformationalVersion` or hard-code? Spec assumes the former, read via reflection once at static init.

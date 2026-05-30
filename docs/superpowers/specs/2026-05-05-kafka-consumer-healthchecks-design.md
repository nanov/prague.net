# Kafka Consumer Healthchecks — Design

Date: 2026-05-05
Status: Approved (pending implementation plan)

## Goal

Add proper health checks for the Prague Kafka consumer (`KafkaCacheConsumer`), surfaced as two `IHealthCheck` implementations (liveness + readiness) shipped from `Prague.Kafka`. The same state is also exposed on the existing `KafkaCachesStatistics` types for users who do not use `Microsoft.Extensions.Diagnostics.HealthChecks`.

The first indicator — partition assignments — is already partially tracked. This design extends that with the additional signals required to declare the consumer "live" and "ready" with high confidence, while strictly preserving the library's hot-path performance characteristics.

## Non-goals

- Per-cache `IHealthCheck` instances. A single liveness + single readiness check covers all caches; per-cache attribution comes through `HealthCheckResult.Data` only when failing.
- Channel-pressure or broker-latency thresholds as gating signals. They remain in `Statistics` for observability but do not flip health status.
- A new package or assembly. All additions live inside `Prague.Kafka`.
- Backwards-compat shims for callers of the old statistics API — additions are purely additive.

## Surface

```csharp
namespace Prague.Kafka.Health;

public sealed class KafkaCachesHealthOptions {
    public TimeSpan PollLoopHeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan HandlerProcessingTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public int      MinBrokersUp             { get; set; } = 1;
}

public sealed class KafkaCachesLivenessHealthCheck   : IHealthCheck { ... }
public sealed class KafkaCachesReadinessHealthCheck  : IHealthCheck { ... }
```

DI extension on `IHealthChecksBuilder`:

```csharp
services.AddHealthChecks()
    .AddPragueKafkaLiveness ("prague-kafka-live")
    .AddPragueKafkaReadiness("prague-kafka-ready");
```

The existing `KafkaCachesStatistics` / `KafkaCachesConsumerStatistics` / `KafkaDataCacheStatistics` types gain new public read-only properties exposing the underlying state (see "State placement" below).

## Health logic

**Liveness** (any failing predicate → `Unhealthy`):

1. `IsFatalLatched == false` on the consumer.
2. `Stopwatch.GetElapsedTime(LastPollTimestamp) < PollLoopHeartbeatTimeout`.
3. For every cache:
   - `IsLoopFaulted == false`, AND
   - `LastProcessingStartTimestamp == 0` (idle — short-circuits the elapsed check; `0` is never a valid `Stopwatch.GetTimestamp()` value) OR `Stopwatch.GetElapsedTime(LastProcessingStartTimestamp) < HandlerProcessingTimeout`.

**Readiness** = liveness, AND:

4. `CachesLoadingCount == 0` (initial load complete for the consumer).
5. For every cache: `AssignedPartitionCount >= 1`.
6. `BrokerUpCount >= MinBrokersUp` (default 1).
7. `HasLostPartitions == false` (auto-clears on next successful assign).

Mapping to `HealthCheckResult`:

- Liveness fail → `HealthStatus.Unhealthy`. k8s liveness probe should restart the pod.
- Readiness fail (with liveness passing) → `HealthStatus.Degraded`. k8s readiness probe pulls the pod from the LB without restart.
- Both pass → `HealthStatus.Healthy`.

Failed results include a `Data` dictionary with the failing predicate names + values (cache name where applicable) for diagnosability.

## State placement

State lives on existing types — no new aggregator. Single writer per field, plain reads/writes (no `Volatile`, no `Interlocked`).

**`KafkaDataCacheStatistics`** (per cache, `src/Prague.Kafka/Statistics.cs:113`):

```csharp
internal long LastProcessingStartTimestamp;   // 0 = idle; written by ChannelLoop
internal bool IsLoopFaulted;                  // latched on terminal catch in ChannelLoop
internal int  AssignedPartitionCount;         // written by rebalance handlers
```

**`KafkaCachesConsumerStatistics`** (per consumer, `src/Prague.Kafka/Statistics.cs:24`):

```csharp
internal long LastPollTimestamp;              // written after each consumer.Consume return
internal bool IsFatalLatched;                 // written at fatal sites + on any handler fault
internal int  BrokerUpCount;                  // written from librdkafka stats snapshot
internal bool HasLostPartitions;              // set in lost handler, cleared on next assign
```

Each gets a public read-only property mirroring the existing pattern (`AssignedPartitions`, `TotalMessagesReceived`, etc.).

`CachesLoadingCount` already exists on `KafkaCachesConsumerStatistics` — reused as-is.

## Hot-path changes

Two surgical edits, both zero-allocation, ~15–25 ns per Kafka message in aggregate.

**`KafkaCacheConsumer.Consume`** (`src/Prague.Kafka/IO/KafkaCacheConsumer.cs:444`):

```csharp
var consumeResult = consumer.Consume(ct);
_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();
```

**`KafkaCacheHandler<>.ChannelLoop`** (`src/Prague.Kafka/IO/KafkaCacheConsumer.cs:258`):

```csharp
while (reader.TryRead(out var result)) {
    _statistics.LastProcessingStartTimestamp = Stopwatch.GetTimestamp();
    try {
        // ... existing IsPartitionEOF + key deserialize + dispatch ...
    }
    finally {
        _statistics.LastProcessingStartTimestamp = 0;
    }
}
```

The terminal `catch (Exception e)` block in `ChannelLoop` (line ~313) sets:

```csharp
_statistics.IsLoopFaulted = true;
_consumerStatistics.IsFatalLatched = true;
```

before calling the existing `_logger.ChannelConsumptionError`. `_consumerStatistics` is a new readonly field passed in via the handler constructor.

## Rebalance handler changes

Existing handlers in `KafkaCacheConsumer` already track `_assignedPartitions`. Extend each to also update per-cache `AssignedPartitionCount` and consumer-level `HasLostPartitions`:

For each rebalance event, the partition list may span multiple topics. Iterate the list once and look up the per-cache `KafkaDataCacheStatistics` by `partition.Topic` (same lookup pattern as the existing watermark code in `SetPartitionsAssignedHandler`).

- **`SetPartitionsAssignedHandler`**: for each assigned partition, increment that cache's `AssignedPartitionCount`; set consumer-level `HasLostPartitions = false`.
- **`SetPartitionsRevokedHandler`**: for each revoked partition, decrement that cache's `AssignedPartitionCount`.
- **`SetPartitionsLostHandler`**: for each lost partition, decrement that cache's `AssignedPartitionCount`; set consumer-level `HasLostPartitions = true`.

## Fatal-latch trigger sites

`IsFatalLatched` is set at every site that already aborts the consumer:

- Fatal `KafkaException` (`Error.IsFatal`).
- App-fatal codes via `KafkaErrorHandling.IsErrorFatal`.
- The general `Exception` re-throw in `Consume`.
- `_manualReset.TrySetException` callers.
- **New:** any handler `ChannelLoop` terminal catch (one bad handler ⇒ consumer is unsafe).

The latch is one-way — once `true`, it stays `true` for the lifetime of the consumer instance.

## librdkafka stats extension

Extend `LibrdkafkaBrokerStats` (`Statistics.cs:194`):

```csharp
[JsonPropertyName("state")]
public string? State { get; init; }
```

In `KafkaCachesConsumerStatistics.UpdateFromLibrdkafkaStats`, count brokers with `State == "UP"` and assign to `BrokerUpCount`. The existing `LibrdkafkaStatsJsonContext` source-gen already covers it — no new JSON registration needed.

## Performance

**Hot path (per-message):**

- 1× `Stopwatch.GetTimestamp()` (~5–10 ns) + 1× plain `long` write at the start.
- 1× plain `long` write (`= 0`) at end-of-iteration.
- Total: ~15–25 ns per message, zero allocation, zero boxing, zero virtual dispatch, zero contention (single writer per field).
- No `Volatile.Write` / `Interlocked` — single 64-bit-aligned writer + occasional reader, no ordering relationship to other state, sub-second visibility tolerated by 3-second threshold.

**Health-check read path** (called every ~10s by the framework):

- **Zero allocations on the Healthy path.** Returns `HealthCheckResult.Healthy()` with no description and no `Data`.
- Allocations only when reporting failure: a `Dictionary<string, object>` with failing predicate names + values. Failure is rare; context is worth a Gen0 allocation.
- No LINQ — `foreach` over the existing `FrozenDictionary<string, KafkaDataCacheStatistics>` (`KafkaCachesConsumerStatistics.Caches`).
- No `string.Format` / interpolation. Static reason constants (`"poll_loop_stalled"`, `"handler_processing_timeout"`, `"broker_down"`, `"loaded_partial"`, `"no_assignment"`, `"partitions_lost"`, `"fatal_latched"`).
- No `IConsumer.Assignment` calls (would allocate a `List<TopicPartition>` inside Confluent.Kafka).
- Cache name / topic name comes from the existing handler instance — no new strings.

## Tests

Two layers.

**Unit (`tests/Prague.Kafka.Tests`, new project):**

Drives `KafkaCachesLivenessHealthCheck` / `KafkaCachesReadinessHealthCheck` directly against synthetic `KafkaCachesConsumerStatistics` / `KafkaDataCacheStatistics` instances. No broker.

- Each predicate independently flips `Healthy → Unhealthy`/`Degraded` with the right `Data` reason.
- Fatal latch never recovers (set → reset attempts ignored).
- `HasLostPartitions` auto-clears on next `AssignedPartitionCount` increment.
- Liveness vs readiness split:
  - Initial-load-not-done → liveness Healthy, readiness Degraded.
  - Poll-loop stalled → both Unhealthy.
  - Broker down → liveness Healthy, readiness Degraded.
- Healthy path verified zero-alloc via `GC.GetAllocatedBytesForCurrentThread()` delta around `CheckHealthAsync`.

**Integration (`tests/Prague.Kafka.IntegrationTests`):**

- Kill broker → `BrokerUpCount` drops to 0 → readiness `Degraded`; restart broker → recovers.
- Slow after-handler exceeding `HandlerProcessingTimeout` → liveness `Unhealthy` with the cache's name in `Data`.
- Cooperative-sticky rebalance does NOT trigger `HasLostPartitions`.
- Force lost-partitions (session timeout) → readiness `Degraded`; next assign clears it.

## Out of scope

- Channel-pressure metric as a gating signal (informational via existing stats only).
- Broker latency / throttle thresholds as gates.
- Per-cache `IHealthCheck` instances.
- BenchmarkDotNet micro-bench for the watchdog writes (can be added later if desired; expected to be in noise).
- `IOptions<KafkaCachesHealthOptions>` per-cache override surface (YAGNI).

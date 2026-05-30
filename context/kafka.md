# Prague.Kafka — overview

> **Read when:** orienting in the Kafka layer (lifecycle, extension points, TestAdaptor). For specifics jump to a topic file below.

Kafka consumer/producer integration, SerDe, filters, health, background worker.

## Lifecycle & extension points

- `KafkaCachesBackgroundWorker` drives consumption.
- Caches expose `ICacheAfterHandler`, `IEnrichable`, `IKafkaConfigurable` extension points.
- After-handlers fire on the **live phase only** (not during the initial load). Conditional updates (`UpdateType.Same`) suppress phantom churn — change detection compares structural equality (driven by `[DataCacheIgnoreEquality]`).

## Topic map

| Topic | File |
|-------|------|
| Message filters (header/key/value), `FilterDecision`, treatAsDelete | [`kafka-filters.md`](kafka-filters.md) |
| Numeric header SerDe + `PragueMessagePack` isolation | [`kafka-serde.md`](kafka-serde.md) |
| Liveness/readiness health checks | [`kafka-health.md`](kafka-health.md) |

## `*Unsafe` accessor convention

Raw, unsynchronized reads on `KafkaCachesConsumerStatistics` are exposed via `*Unsafe` properties (`BrokerUpCountUnsafe`, `LastPollTimestampUnsafe`, `IsFatalLatchedUnsafe`, `HasLostPartitionsUnsafe`). Backing fields are `internal`; only the `*Unsafe` accessors are public. Follow this when adding observable-but-unlocked state.

## TestAdaptor (`Prague.Kafka.TestAdaptor`)

In-memory Kafka cluster, drop-in for real Kafka. `AddKafkaCacheTestCluster()` wires `KafkaCacheTestBuilderProvider`. `InjectDump(path)` loads a MessagePack `.pkd` and replays records + a partition EOF; `InjectDumps(path)` batch-loads a directory. `AddCacheTopics()` auto-registers topics from `IDataCacheRegistry` (deferred to `Init(IServiceProvider)`). `TestKafkaCluster` distributes round-robin among consumers sharing a `group.id`; consumers without one get a unique UUID (independent semantics). `Consume(int millisecondsTimeout)` returns `null` on timeout (matches Confluent, does not throw). `KafkaCacheTestBuilderProviderMarshall` bridges `internal` interface members for other assemblies.

## Tests

`Prague.Kafka.Tests` (NUnit), `Prague.Kafka.TestAdaptor.Tests`, `Prague.Kafka.IntegrationTests` (Testcontainers Kafka).

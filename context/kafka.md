# Prague.Kafka — overview

> **Read when:** orienting in the Kafka layer (lifecycle, raw zero-copy path, extension points). For specifics jump to a topic file below.

Kafka consumer/producer integration, SerDe, filters, health, OpenTelemetry, background worker. Built on the **`Nanov.Kafka` fork** of Confluent.Kafka (namespace `Nanov.Kafka`) for its raw, span-based `ConsumeRaw` / `RawProduce` APIs.

## Lifecycle & extension points

- `KafkaCachesBackgroundWorker` drives consumption; `AddKafkaCaches(...)` wires everything (`PragueMessagePack.Options` is configured inline at registration — see [`kafka-serde.md`](kafka-serde.md)).
- Caches expose `ICacheAfterHandler`, `IEnrichable`, `IKafkaConfigurable` extension points.
- After-handlers fire on the **live phase only** (not during the initial load). Conditional updates (`UpdateType.Same`) suppress phantom churn — change detection compares structural equality (driven by `[DataCacheIgnoreEquality]`).
- Each instance consumes under a unique `group.id` (`KafkaCaches.InstanceId`), so every instance fully loads every topic (broadcast, not partitioned work-sharing).
- **Multi-partition load handshake is per partition.** `KafkaCacheHandler` keeps one entry per assigned partition (`_partitionWatermarks`: high watermark at assignment, replaced by `LOADED` once that partition's EOF reaches it) and a pending count; `TrackAssignedPartition` registers from the assign callback, `TryCompletePartitionLoad` runs on each EOF, and the cache goes live (`FlushRawLoadBufferAndGoLive`) only when the last pending partition completes. Both run on the consume thread (rebalance callbacks fire inside `ConsumeRaw`), so no locking. A re-assigned partition keeps its state; a partition first seen after go-live returns false and logs `PartitionAddedAfterLoad` — adding partitions at runtime is unsupported (keys re-map, so per-key order is lost). Ordering contract: Prague applies records in consume order and never compares timestamps, so per-key order requires every write for a key on one partition (default keyed partitioner). The load target is the topic's partition count, read once from metadata before `Subscribe` (`ResolveExpectedPartitionCounts`, via a `DependentAdminClientBuilder` on the consumer handle), so a cache waits for partitions not yet assigned; a lookup failure or unlisted topic leaves it 0 and the target falls back to the assigned count. Consequence: under a shared `group.id` an instance holding part of a topic never goes live (pinned by `SharedGroup_InstanceAssignedPartOfTheTopic_DoesNotGoLive`). The count is published as `KafkaDataCacheStatistics.PartitionCount`, and readiness's `NoPartitionAssigned` fires on assigned < count, so a partial revoke after load is visible too. `UpdateTopicStats` sums fetch-queue count/size over real partitions (skipping librdkafka's `-1`) and reports the least advanced `FetchState`. Covered by `MultiPartitionLoadTests` (integration, 3 partitions, one empty) and `PartitionLoadHandshakeTests` (unit). History: until 2026-11 a single overwritten `HighWatermarkOffset` let the first EOF declare the whole cache loaded.

## Raw zero-copy path (consumer + producer)

Both lanes are **raw-only** — the legacy typed consumer/producer pipelines and the `System.Threading.Channels` path were removed.

- **Consumer** (`IO/KafkaCacheConsumer.cs`): a dedicated long-running thread runs `ConsumeRawLoop`, calling `consumer.ConsumeRaw(...)` → a `RawMessage` whose `Key` / `Value` / `Headers` are native librdkafka **spans**. `KafkaCacheHandler.DispatchRaw` deserializes + enriches directly off those spans (all span reads finish before the message is disposed), cutting per-message allocation from ~312 B to ~56 B. Topic strings are never materialized on the hot path — `ResolveHandler` does a UTF-8→char span lookup against a `FrozenDictionary…AlternateLookup<ReadOnlySpan<char>>`.
  - **Load phase:** materialized values are object-compacted by key in a `ValueCompactingBuffer<TKey,TValue>` (capacity 50), flushed to the cache in batches and at partition EOF (`FlushRawLoadBufferAndGoLive`, which then starts the live worker).
  - **Live phase:** per-handler ring buffer `AsyncValueSpWorker<RawWorkItem, RawLiveHandler>` (capacity 64) from the `Nanov.HighPerformance.Threading.Workers` package receives `{Kind, Key, Value, TimestampMs}` slots. The consume loop runs on the ThreadPool and holds no thread while the ring is empty. `RawLiveHandler.HandleAsync` reads the slot, `Release()`s it synchronously (a ref-struct scope can't cross `await`), then returns the after-handler chain; a cache without after-handlers completes every item synchronously. `Clear` drops the released slot's references; `OnError` logs `LiveApplyError`. Shutdown relies on the worker's `Dispose` contract (a loop that does not exit within 1 s faults `Completion` with `TimeoutException`), pinned by `LiveWorkerDisposeContractTests`.
- **Producer** (`IO/KafkaCacheProducer.cs`): `IRawProducer.RawProduce(topic, keySpan, valueSpan, in KafkaHeaders)` (managed `MSG_F_COPY` path → 0 managed B/op). Keys/values serialize into pooled `ScratchArrayWriterManager<T>` writers (`Internal/ScratchArrayWriter.cs`); an empty value span is a tombstone (`Delete`). `Local_QueueFull` is retried (≤5×) with a `ValueSpinWait` backoff that bails on cancellation. The native no-copy path (`ProduceNoCopy`) is parked as backlog.

`IKafkaCacheBuilderProvider` (`KafkaBuilderProvider.cs`) abstracts `NewRawConsumerBuilder` / `NewRawProducerBuilder` → `.BuildRaw()`.

## OpenTelemetry (`Prague.Kafka.OpenTelemetry`)

`AddPragueKafkaInstrumentation(MeterProviderBuilder)` registers `PragueKafkaMetricsReporter`, which exports per-cache / per-consumer metrics off `KafkaCachesConsumerStatistics` / `KafkaDataCacheStatistics`. `ReusableMeasurementSource` keeps the observable-callback path allocation-free.

## Topic map

| Topic | File |
|-------|------|
| Message filters (header/key/value), `FilterDecision`, treatAsDelete | [`kafka-filters.md`](kafka-filters.md) |
| Numeric header SerDe + `PragueMessagePack` isolation | [`kafka-serde.md`](kafka-serde.md) |
| Liveness/readiness health checks | [`kafka-health.md`](kafka-health.md) |

## `*Unsafe` accessor convention

Raw, unsynchronized reads on `KafkaCachesConsumerStatistics` are exposed via `*Unsafe` properties (`BrokerUpCountUnsafe`, `LastPollTimestampUnsafe`, `IsFatalLatchedUnsafe`, `HasLostPartitionsUnsafe`). Backing fields are `internal`; only the `*Unsafe` accessors are public. Follow this when adding observable-but-unlocked state.

## Tests

- `Prague.Kafka.Tests` (NUnit) — broker-free unit coverage of filters (DI-aware state overloads, registration order, build failures), SerDe, builder/registration, health. **No enrichment coverage here** — that lives only in the integration tests.
- `Prague.Kafka.IntegrationTests` (NUnit + **Testcontainers.Kafka**) — behavioral coverage against a real broker via a dual-cluster fixture: lifecycle, header/key/value filters, enrichment, tombstones, numeric-header round-trips, MessagePack isolation, health, self-consume. This is the in-tree replacement for the removed in-memory TestAdaptor.

> The in-memory `Prague.Kafka.TestAdaptor` project (and its `.Tests`) were removed. `CacheGenerator` still emits TestAdaptor extension methods **conditionally** — only when an assembly defining `Prague.Kafka.TestAdaptor.KafkaCacheTestBuilderProviderMarshall` is referenced — so that codegen branch is currently dormant.

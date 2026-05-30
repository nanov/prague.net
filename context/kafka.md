# Prague.Kafka — overview

> **Read when:** orienting in the Kafka layer (lifecycle, raw zero-copy path, extension points). For specifics jump to a topic file below.

Kafka consumer/producer integration, SerDe, filters, health, OpenTelemetry, background worker. Built on the **`Nanov.Confluent.Kafka` fork** (keeps the `Confluent.Kafka` namespace) for its raw, span-based `ConsumeRaw` / `RawProduce` APIs.

## Lifecycle & extension points

- `KafkaCachesBackgroundWorker` drives consumption; `AddKafkaCaches(...)` wires everything (`PragueMessagePack.Options` is configured inline at registration — see [`kafka-serde.md`](kafka-serde.md)).
- Caches expose `ICacheAfterHandler`, `IEnrichable`, `IKafkaConfigurable` extension points.
- After-handlers fire on the **live phase only** (not during the initial load). Conditional updates (`UpdateType.Same`) suppress phantom churn — change detection compares structural equality (driven by `[DataCacheIgnoreEquality]`).
- Each instance consumes under a unique `group.id` (`KafkaCaches.InstanceId`), so every instance fully loads every topic (broadcast, not partitioned work-sharing).

## Raw zero-copy path (consumer + producer)

Both lanes are **raw-only** — the legacy typed consumer/producer pipelines and the `System.Threading.Channels` path were removed.

- **Consumer** (`IO/KafkaCacheConsumer.cs`): a dedicated long-running thread runs `ConsumeRawLoop`, calling `consumer.ConsumeRaw(...)` → a `RawMessage` whose `Key` / `Value` / `Headers` are native librdkafka **spans**. `KafkaCacheHandler.DispatchRaw` deserializes + enriches directly off those spans (all span reads finish before the message is disposed), cutting per-message allocation from ~312 B to ~56 B. Topic strings are never materialized on the hot path — `ResolveHandler` does a UTF-8→char span lookup against a `FrozenDictionary…AlternateLookup<ReadOnlySpan<char>>`.
  - **Load phase:** materialized values are object-compacted by key in a `ValueCompactingBuffer<TKey,TValue>` (capacity 50), flushed to the cache in batches and at partition EOF (`FlushRawLoadBufferAndGoLive`, which then starts the live worker).
  - **Live phase:** per-handler ring-buffer worker `RawLiveWorker : AsyncValueBufferedWorker<RawWorkItem>` (capacity 64) receives `{Kind, Key, Value, TimestampMs}` slots. `ProcessAsync` reads the slot, `Release()`s it synchronously, then returns the async after-handler chain from a nested helper (a ref-struct scope can't cross `await` — see `Internal/AsyncValueBufferedWorker.cs`).
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

- `Prague.Kafka.Tests` (NUnit) — unit coverage of filters, SerDe, enrichment, builder/registration.
- `Prague.Kafka.IntegrationTests` (NUnit + **Testcontainers.Kafka**) — behavioral coverage against a real broker via a dual-cluster fixture: lifecycle, header/key/value filters, enrichment, tombstones, numeric-header round-trips, MessagePack isolation, health, self-consume. This is the in-tree replacement for the removed in-memory TestAdaptor.

> The in-memory `Prague.Kafka.TestAdaptor` project (and its `.Tests`) were removed. `CacheGenerator` still emits TestAdaptor extension methods **conditionally** — only when an assembly defining `Prague.Kafka.TestAdaptor.KafkaCacheTestBuilderProviderMarshall` is referenced — so that codegen branch is currently dormant.

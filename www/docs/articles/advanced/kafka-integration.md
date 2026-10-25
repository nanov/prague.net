---
title: Kafka Integration
description: Wire Prague caches to compacted Kafka topics - topic settings, AddKafkaCaches, filters, after-handlers, producer, health checks and OpenTelemetry.
---

# Kafka Integration

`Prague.Kafka` connects Prague caches to compacted Kafka topics. One background worker per Kafka cluster drives a consumer that funnels messages into the right cache by topic.

Every code block on this page compiles in `samples/Prague.Samples/Advanced/KafkaIntegration.cs`. The registration blocks run as tests against a `ServiceCollection` and assert the container they build; the three blocks that only make sense against a live cluster — awaiting the initial load, mapping health endpoints, producing — are compiled wiring examples and are not executed.

## Kafka model in one paragraph

Prague assumes each cache is backed by a single compacted Kafka topic **with one partition**, where the message key maps to the cache's primary key. Tombstones (`null` value) delete; non-null values upsert. Partition EOF marks the boundary between *initial load* (replay of the topic state) and *live* (subsequent live updates); the load handshake completes on the first partition EOF, so a multi-partition topic goes live before the load has finished (not enforced in code). Consumer group offset commit, partition rebalance, and broker failover are delegated to the Kafka client (`Nanov.Kafka`) and librdkafka. If you want a refresher, read the Confluent intro to log compaction — you only need to know that, and the rest of this page makes sense.

## Creating a topic

Prague never creates topics (the producer runs with auto-creation off), so create each one in advance. The suggested settings for a Prague topic, as Terraform:

```hcl
"orders" = {
  partitions = 1
  config = {
    "cleanup.policy"            = "compact"
    "max.compaction.lag.ms"     = "86400000"
    "max.message.bytes"         = "1048588"
    "min.cleanable.dirty.ratio" = "0.1"
    "min.insync.replicas"       = "2"
    "retention.bytes"           = "-1"
    "retention.ms"              = "345600000"
    "segment.bytes"             = "104857600"
    "segment.ms"                = "300000"
  }
}
```

| Setting | Value | Why |
| --- | --- | --- |
| `partitions` | 1 | The whole topic is one ordered log, read to a single EOF. |
| `cleanup.policy` | `compact` | Kafka keeps the last value per key, which is what an initial load replays. |
| `segment.ms` | 5 minutes | The active segment is never compacted. Rolling it every 5 minutes makes new writes compactable within minutes. |
| `segment.bytes` | 100 MiB | Rolls a busy topic's segment before 5 minutes are up. |
| `min.cleanable.dirty.ratio` | 0.1 | The cleaner runs once 10% of the log is superseded (Kafka's default is 0.5). |
| `max.compaction.lag.ms` | 1 day | A superseded value is compacted within a day, even when the dirty ratio is not reached. |
| `max.message.bytes` | 1048588 | Kafka's default: the largest record batch, after gzip, the broker accepts. |
| `min.insync.replicas` | 2 | A write needs two in-sync replicas. Use a replication factor of 3 so one broker can be down. |
| `retention.bytes` | -1 | No size limit. |
| `retention.ms` | 4 days | Applies only when `cleanup.policy` includes `delete`. With `compact` alone, Kafka deletes nothing by age. |

The compaction settings keep an initial load short: the fewer superseded values left on the topic, the fewer the consumer replays before EOF.

`min.insync.replicas` matters because Prague's producer uses librdkafka's default `acks=all`. When fewer than two replicas are in sync, the broker rejects the write, and the fire-and-forget producer only logs it (see [Producer](#producer)). On a single-broker development cluster (replication factor 1), leave `min.insync.replicas` out, or every write fails.

## Wiring up

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#wiring-up)]

Per-cluster options live in `KafkaCachesOptions` (namespace `Prague.Kafka.Options`):

| Property | Meaning |
| --- | --- |
| `BootstrapServers` | Required. Comma-separated broker list. |
| `ClientSettings` | Forwarded to librdkafka (`group.id`, `auto.offset.reset`, SASL settings, etc.). |
| `Vars` | String-string map; values are substituted into topic names as `[v:key]`. |

Multiple clusters are supported by calling `AddKafkaCaches` more than once with different `configsSectionName` arguments (the default section is `"kafkaCaches"`); each cluster has its own consumer + producer + handler set.

Global, library-wide options live in `KafkaCachesGlobalOptions`:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#global-options)]

## Cache registration

`AddCache<TCacheEntity, TKey, TValue>` registers the cache with the consumer. The topic can come from the entity's attribute, from a literal, or from a resolver that runs against the container; `AddInternalCache<...>` registers a cache that participates in the DI registry but is **not** consumed from Kafka — useful for caches you populate manually (test fixtures, computed views):

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#cache-registration)]

The default topic template comes from `[DataCacheTopic]`, or is `Cache.<ClassName>` when the entity has no attribute. Two placeholder forms are resolved in every topic name — the attribute's, a literal's and a resolver's alike: `[v:name]` reads `KafkaCachesOptions.Vars["name"]`, `[e:NAME]` reads the environment variable `NAME`. An unknown name is left in place verbatim. The generated cache exposes the raw template as a constant:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#topic-template)]

## Filters

`KafkaCacheHandlerBuilder` exposes filter builder methods that compose with AND:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#filters)]

`WithHeaderEqualsFilter` / `WithHeaderNotEqualsFilter` take `string`, `int` or `long` values (one or more; several values are an OR). Numeric headers are read MessagePack-first with a fallback to raw 4/8-byte values for backward compatibility; the generic `WithHeaderFilter<T>` deserializes the header via MessagePack and hands the value to your predicate (`passOnNull` decides what a missing header does). Header filters run before the key is deserialized and have no `treatAsDelete`.

`WithValueFilter(Func<TValue, bool> predicate, bool treatAsDelete = false)` runs the predicate against the **deserialized** cache entity and admits the message only when it returns `true`. It is a plain ingestion-time predicate — *not* an indexed query, so the body is arbitrary C# (combine conditions with `||` / `&&` inside the single lambda; there is no "OR filter" at ingestion). It is evaluated **exactly once per message, as that message is consumed**, and never against records that are already in the cache. Use it to keep only the records you care about (e.g. a status, a tenant, a non-empty field). Header and key filters are evaluated first, so the value is deserialized only for messages that already passed them.

- **Tombstones** (null-value delete messages) **skip the key and value filters entirely and still delete** the key — neither predicate is evaluated for a message that carries no value. A delete is the log's statement that the key is gone, and an ingress predicate cannot meaningfully judge it. Two header-side rules are the deliberate exceptions: a filter that saw its header and **explicitly rejected** the value still drops the tombstone (that is how a consumer selects a sub-stream of a shared topic), and so does the producer self-filter. A header that is merely **missing** does not — see `WithHeaderExistsFilter` above.
- All filter methods (header, key, value) compose with **AND**; multiple `WithValueFilter` calls must all pass, and multiple `WithHeaderExistsFilter` calls all require their header (up to 64 distinct names).
- **Every predicate runs on one thread.** All filters for every cache in the same `AddKafkaCaches` section are evaluated synchronously on the single long-running consume thread. A predicate that blocks or does I/O stalls the initial load *and* the live tail of every other cache in that section.
- **A filter is not an authorization boundary.** It is a retention / load-shedding device: cached entries reflect the state that was in force when they were ingested. Dynamic visibility policy belongs in a reader over `Query()`, where narrowing and widening both take effect immediately.

- **Initial load**: rejected messages are silently dropped — except a tombstone, which still removes the key.
- **Live phase**: rejected messages still fire `ICacheAfterHandler.Handle(UpdateType.Filtered, ...)` so projectors can observe them. A tombstone fires `UpdateType.Delete` instead when the key was resident, and nothing at all when it was not.

### Filters that need DI state

Key, value, and header filters each have an overload that resolves state from the container. The factory runs **exactly once**, while the handler is built, against the root `IServiceProvider`; its result is handed to the predicate on every message, so nothing is resolved from DI on the ingestion path. With three services of your own —

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#filter-state-services)]

— the four shapes look like this:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#filters-di-state)]

- **Prefer a snapshot over a live service.** `sp => sp.GetRequiredService<IAllowList>().OrderIds.ToFrozenSet()` is fixed for the process lifetime, which makes a restart the single well-defined way to reload it — see *Filter lifecycle* below. Capturing the live service instead means the predicate reads its current state on every message, while entries admitted under the old state stay cached regardless.
- **Write the predicate as a `static` lambda.** State arrives as an *argument*, not a capture, so a `static` lambda closes over nothing; Roslyn caches it in a static field and you pay one delegate per process and nothing per message.
- **Spell out the type argument on the `<TService>` overloads.** It is not inferable from an untyped lambda — you get `CS0411`. Write `.WithValueFilter<IRegionPolicy>(static (policy, o) => …)`, and both type arguments for the header overload.
- **`sp` is the root provider.** A service registered as `Scoped` throws at build time with a Prague message pointing you at a singleton registration or the snapshot overloads. A `Transient` is resolved once and then captured for the process lifetime.

> **Startup ordering.** The state factory runs while hosted services are being **constructed**, which precedes the `StartAsync` of *every* hosted service — only the initial cache *load* is ordered by registration. A state service that populates itself in its own `StartAsync` or `BackgroundService` is still empty when the factory reads it, so make sure the state is fully materialized by then.

### `treatAsDelete` — derive tombstones from a filter

Both `WithValueFilter(predicate, treatAsDelete)` and `WithKeyFilter(predicate, treatAsDelete)` accept an optional `treatAsDelete` flag (default `false`). By default a rejected message is dropped without touching the cache (any previously-cached value for that key stays). Pass `treatAsDelete: true` to instead **treat a rejection as a tombstone for the key** — useful when the stream carries soft-deletes as ordinary records (a `Cancelled` status, say) rather than as null-value tombstones:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#treat-as-delete)]

Every key/value filter chain evaluates to a shared `FilterDecision` — `Accept`, `Skip`, or `Delete`:

- **Live phase**: a `Delete` removes the key from the cache and fires `ICacheAfterHandler.Handle(UpdateType.Delete, ...)` (with the old value), exactly as a null-value tombstone would — *not* `UpdateType.Filtered`. If the key was not present, nothing fires (consistent with a delete of an absent key).
- **Initial load**: the key is left absent (removed if an earlier batch had added it); no after-handler fires during load.
- **Multiple filters compose with AND, first-reject wins**: the first filter (in registration order) to reject decides the outcome. A message failing a plain filter is skipped even if a later `treatAsDelete` filter would also have rejected it; only a message whose *first* rejecting filter is a `treatAsDelete` filter becomes a tombstone. This holds across the key and value chains alike.
- A thrown predicate is treated as a plain reject (skip), never as a delete.

> **Key vs value `treatAsDelete`.** A value can change over time, so `treatAsDelete` on a value filter naturally evicts a key whose record stopped qualifying. A **key is immutable**, so `treatAsDelete` on a key filter only evicts an already-cached key when the predicate reads **state that changed after the key was admitted** — a live service captured through the `WithKeyFilter<TService>` overload, or a mutable closure — and a *new* message for that key later arrives and is rejected. For a pure key predicate, or for a snapshot (which is immutable for the process lifetime), it is effectively inert; reload such a filter by restarting the process. **Header filters do not support `treatAsDelete`** — they are evaluated before the key is deserialized.

### Filter lifecycle — what a filter change does and does not do

Filters are an **ingress gate**: the predicate is evaluated exactly once per message, as it is consumed, and is never re-applied to cache state that has already been materialized. Changing a filter — by editing the predicate, or by mutating the state it reads — therefore does not retroactively re-filter what is already in memory:

- **Narrowing** (fewer records qualify) evicts only through `treatAsDelete`, and only when a *new* message arrives for the affected key. On a compacted topic with no further writes for that key, that means never.
- **Widening** (more records qualify) resurrects nothing, ever. Records dropped earlier are not in memory and are never replayed — Prague subscribes and reads forward; it never seeks back over a partition it has already consumed.

The supported way to re-admit them is a **process restart**, and it works out of the box: Prague commits no offsets (`EnableAutoCommit = false`, `EnableAutoOffsetStore = false`), joins under a fresh per-process group id by default, and resets to `Earliest` — so a restarted process re-reads each topic in full under the new state. Configuring `group.id` explicitly does not break this (Prague writes no offset for that group either), but it does make instances split partitions instead of each loading the whole topic.

## After-handlers

`WithAfterHandler<T>()` registers `T` as the cache's `ICacheAfterHandler<TKey, TValue>`:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#after-handler-registration)]

`OrderProjector` is the handler shown in [Conditional Updates](../core-concepts/conditional-updates.md), which also explains the `UpdateType` enum. After-handlers run only in the live phase. Multiple handlers per cache run sequentially; exceptions in one don't skip the others.

## Enrichment

Two property attributes fill fields from the message rather than its payload: `[DataCacheFromTimestamp]` on a `long` property stores the Kafka message timestamp, and `[DataCacheHeader("tenant-id")]` stores a header value (MessagePack-first numeric SerDe for `int` / `long`).

## Awaiting initial load

`AddKafkaCaches` registers a hosted service whose `StartAsync` returns only once every cache has reached its initial-load EOF, so a generic host does not finish starting (and an ASP.NET Core host does not start serving) before the caches are loaded. A load that stalls fails startup with a `TimeoutException` naming the caches still waiting, when the host's startup token fires.

To wait for the load somewhere else — a host that starts its hosted services concurrently, or a test that builds the provider itself — await it explicitly. This is wiring for a running cluster, compiled but not executed by the samples:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#await-initial-load)]

## Health checks

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#health-checks)]

| Check | Predicate | Catches |
| --- | --- | --- |
| Liveness | `FatalLatched` | fatal Kafka error; consumer unsafe |
| | `PollLoopStalled` | poll loop silent longer than `PollLoopHeartbeatTimeout` |
| | `HandlerLoopFaulted` | a per-cache worker terminated with an exception |
| | `HandlerProcessingTimeout` | one message in flight longer than `HandlerProcessingTimeout` |
| Readiness (= liveness +) | `InitialLoadIncomplete` | a cache has not reached initial-load EOF |
| | `NoPartitionAssigned` | a cache topic has zero partitions assigned |
| | `BrokersDown` | fewer than `MinBrokersUp` brokers `UP` per librdkafka statistics |
| | `PartitionsLost` | last rebalance lost partitions and nothing was re-assigned |

`HealthCheckResult.Data` carries `<consumer>.failures` and `<consumer>.caches` so the output names the culprit. The healthy path allocates nothing. Disable the broker predicate (`MinBrokersUp = 0`) if you run with `StatisticsEnabled = false`. The default `MapHealthChecks` returns 200 for `Degraded`; map it to 503 for a Kubernetes readiness probe (wiring example, needs a `WebApplication`):

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#health-endpoints)]

## OpenTelemetry metrics

Opt-in observable instruments sourced from the statistics the consumer already collects — no hot-path cost, no allocations per scrape after warm-up. The extension lives in `Prague.Kafka.OpenTelemetry` and is registered on the `MeterProviderBuilder`; add your exporter after it:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#otel-metrics)]

| Metric | Kind | Tags |
| --- | --- | --- |
| `prague.kafka.consumer.partitions.assigned` | UpDownCounter | `consumer` |
| `prague.kafka.consumer.broker.rtt.p99` | Gauge (ms) | `consumer` |
| `prague.kafka.consumer.health` | Gauge (0/1) | `consumer` |
| `prague.kafka.cache.size` | UpDownCounter | `consumer`, `cache` |
| `prague.kafka.cache.messages.received` | Counter | `consumer`, `cache` |
| `prague.kafka.cache.health` | Gauge (0/1) | `consumer`, `cache` |
| `prague.kafka.cache.index.size` | UpDownCounter | `consumer`, `cache`, `index`, `kind=keys\|values` |
| `prague.kafka.cache.index.capacity` | UpDownCounter | `consumer`, `cache`, `index` |

The `health` gauges share their predicates with the health checks (`1` = healthy); the health endpoint carries the reasons. `index.size / index.capacity` is the utilization of an index's pooled sets.

## Producer

`AddKafkaCaches` also registers a `KafkaCacheProducer` (namespace `Prague.Kafka.IO`) per configuration section, as a **keyed** singleton under the section name (`"kafkaCaches"` by default) — there is no unkeyed registration, so inject it with `[FromKeyedServices]`:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#producer-service)]

`Produce` takes any `[DataCache]` type (it needs the generated `IEnrichable<T>`); the topic does not have to be one this process consumes. A section with no cache at all is a producer-only registration: its consumer subscribes to nothing and its initial load completes at once.

The producer is fire-and-forget. `Produce` and `Delete` return once the message is queued in librdkafka: there is no delivery report and no awaitable result, and a broker-side failure is only logged. It is built from the section's `ClientSettings` (the same dictionary the consumer uses) with gzip compression and topic auto-creation off; acks, idempotence and retries are librdkafka's defaults (`acks=all`, idempotence off) unless `ClientSettings` sets them. Only a full local queue (`Local_QueueFull`) is retried, up to five times with a short backoff, before it throws. `Dispose` flushes; after it, `Produce` and `Delete` do nothing.

When a cache is registered with `AddCache` (not `AddInternalCache`), the generated `CacheMarshall.Produce(cache, key, value)` / `CacheMarshall.Delete(cache, key)` and the two extension methods below use that cache's producer and resolved topic, so no topic string is needed:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#produce-through-the-cache)]

`AddOrUpdateAndProduce` is the conditional form: it produces only when the local `AddOrUpdate` reported a change (see [Conditional Updates](../core-concepts/conditional-updates.md)). `AddOrUpdateAndProduce(document, force: true)` keeps that local write conditional but always produces, which is how you re-publish the current state onto the topic. `Produce` itself always writes — there is no producer-side dedup. If you need it, consult `cache.Cache.TryGet(...)` before calling `Produce`.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#produce-forced)]

`ForceProduceAll()` is the whole-cache version of that re-publish, and it is a repair tool rather than part of normal writing: it snapshots every resident record (taking every store lock while the snapshot is copied), then produces one record per key whether or not anything changed, and returns how many it produced. Reach for it to re-seed a topic after a retention or compaction mishap, to bootstrap a downstream consumer that cannot replay, or to re-publish state written under an older schema. The snapshot is taken once, before the first produce, so writes that land during the pass are not included; the pass does not wait for delivery. Consumers receive the records through their ordinary equality gate, so a replica that already holds the same state reports `Same` for each one, and this process's own consumer skips them as self-produced.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#produce-all)]

**Your own writes are not applied to your own cache.** Every consumer drops messages stamped with its own producer instance id, so a process that calls `Produce` or `Delete` directly does not see the result in its local cache until it restarts. For deletes, use the generated `cache.RemoveAndProduce(key)` instead: it removes the key locally *and* publishes the tombstone. The tombstone is published whether or not the key was resident locally — a key excluded by an ingress filter, or not loaded yet, still gets its delete — and the `bool` it returns says only whether the local cache held the key.

## MessagePack isolation

Prague routes every SerDe call through `PragueMessagePack.Options`, an internally-owned `MessagePackSerializerOptions`. Host mutations of `MessagePackSerializer.DefaultOptions` do not affect Prague's wire format. To extend the resolver chain (custom formatters for your entity types), use the global options builder — the third `AddKafkaCaches` argument:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#messagepack-resolver)]

The lambda's input is the default Prague composite (`PragueStringInterningFormatter` + `PragueDateTimeResolver` + `TypelessContractlessStandardResolver`); return whatever composite you want active for SerDe. Put your own resolvers before `defaultResolver` so they win.

### String interning

Prague interns every string it deserializes, so a value repeated across thousands of entities ("EUR", "active", a country code) is held once. The pool is keyed by the UTF-8 bytes on the wire: a repeated value costs a hash and a byte compare, with no decode and no allocation. Entries are weak, so a string the cache no longer references is collected. Strings over 256 UTF-8 bytes are decoded without interning.

High-cardinality values such as ids or free text gain nothing from the pool and only churn it. Opt such a property out with `[DataCacheStringIntern(false)]` and declare it `partial` (C# 13+). The source generator writes the property's implementation and attaches a non-interning formatter to it:

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#string-intern-opt-out)]

The opt-out applies under Prague's default resolver composite, and under a `WithMessagePackResolver` chain that puts your resolvers before `defaultResolver`. It does not apply when MessagePack's source-generated resolver (`GeneratedMessagePackResolver`) comes first: that generator cannot see the attribute Prague generates, so a `[MessagePackObject]` property it serves stays interned. The attribute on a non-partial property is a compile error (CACHE053), whatever its value; so is the attribute on a non-string property (CACHE052).

## Testing

There is no in-memory broker substitute (the former `Prague.Kafka.TestAdaptor` was removed). Test against a real broker — Prague's own integration tests use Testcontainers. Two things to set up:

- **Give each consumer under test its own `group.id`** (`ClientSettings["group.id"]`). The default group id is the process's instance id, shared by every Prague consumer in the process, so two providers built in one test run would otherwise join one group and split the partitions between them.
- **A process does not consume what it produced.** The self-filter drops every message stamped with this process's `X-Producer-Id`, from any provider in the process. To load a cache with what code under test produced in the same process, read the raw messages and re-produce them without that header, or produce the fixtures with a plain Kafka producer.

## HTTP inspection

`Prague.Api` adds `app.MapPragueEndpoints()`: `GET /prague` (consumer and cache statistics), `GET /prague/{CacheName}` (accepts the generated query-string API, e.g. `?customerId=42&total>100`) and `GET /prague/{CacheName}/{id}`. `Prague.Api.UI` adds `builder.AddPragueUI()` + `app.MapPragueUI()`, a MudBlazor front end over the same registry.

## Next

- [Global Last Update Index](global-last-update-index.md) — cross-cache change-tracking for incremental sync.
- [Performance Tuning](performance-tuning.md) — hot-path guidance.

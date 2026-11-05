---
name: prague-kafka
description: "Feed Prague in-memory caches from Kafka with Prague.Kafka: services.AddKafkaCaches(...) and builder.AddCache<TCache,TKey,TValue>(), [DataCacheTopic] templates with [v:name]/[e:NAME] placeholders, topic requirements (compacted, one partition), awaiting the initial load, write-through with AddOrUpdateAndProduce/RemoveAndProduce, KafkaCacheProducer, MessagePack resolvers, health checks and OpenTelemetry metrics. Use when wiring a Prague cache to a Kafka topic, registering caches in DI or hosting, producing to a cache topic, or setting up health and metrics. Filters and after-handlers live in prague-handlers, tests in prague-testing."
license: MIT
metadata:
  package: Prague.Kafka
  docs: https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html
---

# Prague.Kafka

Hydrates `[DataCache]` caches (see `prague-modeling`) from Kafka and keeps them live. Verified against Prague 0.6.0; when this file and the code disagree, the code wins. Namespaces: `Prague.Kafka`, plus `Prague.Core` for the cache types.

## The model

- One **compacted** topic per cache, exactly **one partition**. Not enforced: a multi-partition topic goes live before every partition has loaded. Prague never creates topics.
- Message key = cache key; a null value (tombstone) deletes the key.
- Every process consumes the whole topic and commits no offsets, so a restart is a full reload. Each process gets its own group id by default. Setting a shared `group.id` makes instances split partitions, which is almost always wrong.

## Registration

```csharp
services.AddKafkaCaches(
	o => {
		o.BootstrapServers = "kafka:9092";
		o.ClientSettings["group.id"] = "orders-view";       // any librdkafka setting
		o.Vars["env"] = "prod";                             // [v:env] in a topic template
	},
	builder => {
		builder.AddCache<OrderCache, string, Order>();                          // topic from [DataCacheTopic]
		builder.AddCache<CustomerCache, int, Customer>("customers.[v:env]");   // explicit template
	});

var orders = provider.GetRequiredService<OrderCache>();
```

- Topic template: `[DataCacheTopic("orders.[v:env]")]` on the class (default `Cache.<ClassName>`). `[v:name]` resolves from `o.Vars`, `[e:NAME]` from environment variables. The template is `XxxCache.TopicNameTemplate`.
- `builder.AddCache<X, K, V>(sp => topic)` computes the topic from DI. `builder.AddInternalCache<X, K, V>()` registers a cache in the registry without consuming it.
- Optional third argument: `opts => opts.WithMessagePackResolver(def => CompositeResolver.Create(Mine.Instance, def))`. Put your resolvers before `def`. Mutating `MessagePackSerializer.DefaultOptions` has no effect on Prague.
- `AddKafkaCaches("sectionName", ...)` registers another cluster; one call per cluster.
- SASL/OAUTHBEARER: set `o.OAuthBearerTokenRefreshHandler`.

Payload-adjacent attributes on the POCO: `[DataCacheFromTimestamp]` on a `long` stores the message timestamp; `[DataCacheHeader("tenant-id")]` stores a header value. Prague interns every deserialized string; opt a high-cardinality string out with `[DataCacheStringIntern(false)]` on a `partial` property.

## Filters and after-handlers

Chained on `AddCache<...>()`: `.WithHeaderEqualsFilter(...)`, `.WithKeyFilter(...)`, `.WithValueFilter(..., treatAsDelete: true)`, `.WithAfterHandler<T>()`. Semantics, `UpdateType` and pitfalls: see `prague-handlers`.

## Initial load

The hosted service's `StartAsync` returns once every cache reaches end-of-topic. Outside it: `await host.DataCachesLoadCompletion();`. A seeder, projector or job that reads loaded caches derives from `KafkaCachesBackgroundService(IDataCacheRegistry)`: its `ExecuteAsync` starts after `registry.LoadingCompletion`, whatever order the host starts services in. `host.StopAsync(token)` cancels the consume loops and drains each cache's live worker, bounded by the token.

## Producing

A process never consumes its own writes (an `X-Producer-Id` header self-filter). Write through the cache so the local copy changes too:

```csharp
orders.AddOrUpdateAndProduce(order);               // local write; produces only if the value changed
orders.AddOrUpdateAndProduce(order, force: true);  // produces always
orders.RemoveAndProduce(order.OrderId);            // local remove + tombstone
```

`orders.ForceProduceAll()` re-seeds the topic with every resident record: a repair tool, not a routine call. The raw producer is a keyed singleton: `[FromKeyedServices("kafkaCaches")] KafkaCacheProducer producer`, then `producer.Produce(topic, key, value)` / `producer.Delete(topic, key)`. It is fire-and-forget: no delivery report, failures are logged.

## Health and metrics

```csharp
services.AddHealthChecks()
	.AddPragueKafkaLiveness(tags: ["live"])
	.AddPragueKafkaReadiness(tags: ["ready"]);
meterProviderBuilder.AddPragueKafkaInstrumentation();
```

- `KafkaCachesHealthOptions`: `PollLoopHeartbeatTimeout` 3 s, `HandlerProcessingTimeout` 5 s, `MinBrokersUp` 1. `MinBrokersUp` needs librdkafka statistics (on by default); set it to 0 if you disable them.
- ASP.NET's `MapHealthChecks` returns 200 for Degraded. Map Degraded to 503 for a Kubernetes readiness probe.
- Metrics: `prague.kafka.consumer.{partitions.assigned, broker.rtt.p99, health}`, `prague.kafka.cache.{size, messages.received, health, index.size}`.

## Testing

Query and handler logic needs no broker; Kafka paths need Testcontainers with one `group.id` per consumer. See `prague-testing`.

## Common mistakes

| Wrong | Right |
|---|---|
| `services.AddPragueKafka(...).AddConsumer(...)` | `services.AddKafkaCaches(o => ..., b => b.AddCache<..>())` |
| `b.AddOrderCache()` | `b.AddCache<OrderCache, string, Order>()` |
| `OrderCache.Topic` | `OrderCache.TopicNameTemplate` |
| `[DataCacheTopic("a.{version}")]` | `[DataCacheTopic("a.[v:version]")]` + `o.Vars["version"]` |
| multi-partition cache topic | recreate it with one partition |
| a `BackgroundService` registered after `AddKafkaCaches` to run on loaded caches | derive from `KafkaCachesBackgroundService` |
| expecting the local cache to see `producer.Produce(...)` | `AddOrUpdateAndProduce` / `RemoveAndProduce` |
| a shared `group.id` across instances | leave the default, or one id per instance |
| mutating `MessagePackSerializer.DefaultOptions` | `WithMessagePackResolver(...)` on `AddKafkaCaches` |
| a consumer producing from its own partial copy of the contract | only the topic's owner produces, from the full contract |

## Read next

[Kafka integration](https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html), [host with DI](https://nanov.github.io/prague.net/docs/articles/how-to/host-with-di.html), [write a filter](https://nanov.github.io/prague.net/docs/articles/how-to/write-a-filter.html), [react to changes](https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html), [health and metrics](https://nanov.github.io/prague.net/docs/articles/how-to/health-and-metrics.html), [test without a broker](https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html), and <https://nanov.github.io/prague.net/llms-full.txt>.

---
name: prague-kafka
description: "Feed Prague in-memory caches from Kafka with Prague.Kafka: services.AddKafkaCaches(...) and builder.AddCache<TCache,TKey,TValue>(), [DataCacheTopic] templates with [v:name]/[e:NAME] placeholders, topic requirements (compacted, one partition), awaiting the initial load, write-through with AddOrUpdateAndProduce/RemoveAndProduce, KafkaCacheProducer, MessagePack resolvers, health checks and OpenTelemetry metrics. Use when wiring a Prague cache to a Kafka topic, registering caches in DI or hosting, producing to a cache topic, or setting up health and metrics. Filters and after-handlers live in prague-handlers, tests in prague-testing."
license: MIT
metadata:
  package: Prague.Kafka
  docs: https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html
---

# Prague.Kafka

Hydrates `[DataCache]` caches (see `prague-modeling`) from Kafka and keeps them live. Verified against Prague 0.9.0; when this file and the code disagree, the code wins. Namespaces: `Prague.Kafka`, plus `Prague.Core` for the cache types.

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

The hosted service's `StartAsync` returns once every cache reaches end-of-topic. Outside it: `await host.DataCachesLoadCompletion();`. A seeder, projector or job that reads loaded caches is a `BackgroundService` whose `ExecuteAsync` opens with `await registry.LoadingCompletion.WaitAsync(stoppingToken);`. `StartAsync` returns at that `await`, so it works registered before or after `AddKafkaCaches`, and shutdown cancels the wait. Wait for the load only there: the host starts hosted services one at a time, so a `StartAsync` that waits, registered first, blocks the Kafka worker that completes the load and the host never starts. `host.StopAsync(token)` cancels the consume loops and drains each cache's live worker, bounded by the token.

## Producing

A process never consumes its own writes (an `X-Producer-Id` header self-filter). Write through the cache so the local copy changes too:

```csharp
orders.AddOrUpdateAndProduce(order);               // local write; produces only if the value changed
orders.AddOrUpdateAndProduce(order, force: true);  // produces always
orders.RemoveAndProduce(order.OrderId);            // local remove + tombstone
```

Write a new instance: a resident row changed in place compares equal to itself, so `AddOrUpdateAndProduce` produces nothing and the change never leaves the process. Clone, change the clone, write the clone (`prague-modeling`, Changing a cached row).

`orders.ForceProduceAll()` re-seeds the topic with every resident record: a repair tool, not a routine call. The raw producer is a keyed singleton: `[FromKeyedServices("kafkaCaches")] KafkaCacheProducer producer`, then `producer.Produce(topic, key, value)` / `producer.Delete(topic, key)`. It is fire-and-forget: no delivery report, failures are logged.

## Health and metrics

```csharp
services.AddHealthChecks()
	.AddPragueKafkaLiveness(tags: ["live"])     // Unhealthy: the consumer is dead or stuck
	.AddPragueKafkaReadiness(tags: ["ready"]);  // Degraded: loading, no partition, brokers down
services.AddOpenTelemetry().WithMetrics(m => m.AddPragueKafkaInstrumentation());   // meter "Prague.Kafka"

app.MapHealthChecks("/health/live", new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() {
	Predicate = r => r.Tags.Contains("ready"),
	ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 503, [HealthStatus.Unhealthy] = 503 },
});
```

- `MapHealthChecks` serves Degraded as 200, so a readiness probe would keep routing to a loading pod: map Degraded to 503 as above, or pass `failureStatus: HealthStatus.Unhealthy` to `AddPragueKafkaReadiness`.
- Liveness fails on `FatalLatched`, `PollLoopStalled` (the consume thread silent for `PollLoopHeartbeatTimeout`, 3 s: a slow filter or deserializer does it), `HandlerLoopFaulted`, `HandlerProcessingTimeout` (one message in an after-handler for 5 s). A restart reloads every topic from the start: fix the slow code rather than raising the timeout.
- Readiness fails on `InitialLoadIncomplete`, `NoPartitionAssigned`, `BrokersDown`, `PartitionsLost`, and is Unhealthy when liveness fails too. Give Prague its own tagged endpoints: a database check on the same endpoint makes a long replay look like an outage.
- `MinBrokersUp` (1) and `broker.rtt.p99` need librdkafka statistics, on by default; with them off, set `MinBrokersUp = 0` or readiness never passes.
- **The endpoints are silent during the initial load.** The Kafka worker's `StartAsync` returns after the load, and ASP.NET starts its server after every hosted service has started, so `/health/*` refuses connections until the data is in. A Kubernetes liveness probe then restarts the pod mid-load and the new pod reloads from scratch, again. Pick one:
  - a `startupProbe` on `/health/live` whose `failureThreshold × periodSeconds` exceeds the worst-case load time;
  - `services.Configure<HostOptions>(o => o.ServicesStartConcurrently = true)`: the server answers during the load, liveness judges the consumer, and readiness reports `InitialLoadIncomplete` until the data is in. Every hosted service then also starts before the load, so each one awaits `LoadingCompletion` in `ExecuteAsync`, as above.
- Instruments (prefix with `AddPragueKafkaInstrumentation("acme")`): `prague.kafka.consumer.{partitions.assigned, broker.rtt.p99, health}` tagged `consumer`; `prague.kafka.cache.{size, messages.received, health}` tagged `consumer`, `cache`; `prague.kafka.cache.index.{size, capacity}` tagged also `index` (size also `kind` = keys/values). The `health` gauges are 1/0 from the same predicates as the checks, so an alert and the probe agree.

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
| relying on registration order to run after the load | `await registry.LoadingCompletion.WaitAsync(stoppingToken)` first in `ExecuteAsync` |
| waiting for the load in a hosted service's `StartAsync` | wait in `ExecuteAsync`; `StartAsync` must return |
| deriving from `KafkaCachesBackgroundService` (obsolete) | a `BackgroundService` that waits in `ExecuteAsync`, as above |
| expecting the local cache to see `producer.Produce(...)` | `AddOrUpdateAndProduce` / `RemoveAndProduce` |
| `row.Status = x; orders.AddOrUpdateAndProduce(row);` on a resident row | `var next = row.Clone(); next.Status = x; orders.AddOrUpdateAndProduce(next);` |
| a shared `group.id` across instances | leave the default, or one id per instance |
| mutating `MessagePackSerializer.DefaultOptions` | `WithMessagePackResolver(...)` on `AddKafkaCaches` |
| a consumer producing from its own partial copy of the contract | only the topic's owner produces, from the full contract |
| a liveness probe with no `startupProbe` on a default host | a `startupProbe` sized to the load, or `ServicesStartConcurrently = true` |
| readiness mapped with the default status codes | Degraded → 503, or `failureStatus: HealthStatus.Unhealthy` |
| raising `HandlerProcessingTimeout` for a slow handler | enqueue the slow work and return (see `prague-handlers`) |

## Read next

[Kafka integration](https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html), [host with DI](https://nanov.github.io/prague.net/docs/articles/how-to/host-with-di.html), [write a filter](https://nanov.github.io/prague.net/docs/articles/how-to/write-a-filter.html), [react to changes](https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html), [health and metrics](https://nanov.github.io/prague.net/docs/articles/how-to/health-and-metrics.html), [test without a broker](https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html), and <https://nanov.github.io/prague.net/llms-full.txt>.

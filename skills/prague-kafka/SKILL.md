---
name: prague-kafka
description: "Wiring Prague caches to Kafka: AddKafkaCaches, [DataCacheTopic], AddOrUpdateAndProduce, WithMessagePackResolver, multi-partition topics, AddPragueKafkaLiveness/Readiness. Use when registering caches, waiting for the initial load, or producing to a cache topic. Not for filters (use prague-handlers)."
license: MIT
metadata:
  applies-to: "Prague >= 0.9.0"
  verified: 2026-11-19
  docs: https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html
---

# Prague.Kafka

Hydrates `[DataCache]` caches (see `prague-modeling`) from Kafka and keeps them live. Namespaces: `Prague.Kafka` (registration, `PragueMessagePack`), `Prague.Kafka.IO` (`KafkaCacheProducer`), `Prague.Kafka.Health`, `Prague.Kafka.OpenTelemetry`, plus `Prague.Core` for the cache types.

## The model

- One **compacted** topic per cache, usually with one partition (more: see [Multi-partition topics](#multi-partition-topics-since-0100)). Prague never creates topics.
- Message key = cache key; a null value (tombstone) deletes the key.
- Every process consumes the whole topic and commits no offsets, so a restart is a full reload. Each process gets its own group id by default. Setting a shared `group.id` makes instances split partitions, which is almost always wrong.

## Registration

```csharp
services.AddKafkaCaches(
	o => {
		o.BootstrapServers = "kafka:9092";
		o.ClientSettings["client.id"] = "orders-view";      // any librdkafka setting; leave group.id alone
		o.Vars["env"] = "prod";                             // [v:env] in a topic template
	},
	builder => {
		builder.AddCache<OrderCache, string, Order>();                          // topic from [DataCacheTopic]
		builder.AddCache<CustomerCache, int, Customer>("customers.[v:env]");   // explicit template
	});

var orders = provider.GetRequiredService<OrderCache>();
```

- Topic template: `[DataCacheTopic("orders.[v:env]")]` on the class (default `Cache.<ClassName>`), exposed as the static `XxxCache.TopicNameTemplate`; the instance property `orders.Topic` is the resolved name once the cache is registered. `[v:name]` resolves from `o.Vars`, `[e:NAME]` from environment variables. Placeholders expand in topic templates only: `BootstrapServers` and `ClientSettings` take plain values.
- `builder.AddCache<X, K, V>(sp => topic)` computes the topic from DI. `builder.AddInternalCache<X, K, V>()` registers a cache in the registry (joinable) without consuming it. The generator also emits `builder.AddOrderCache()` / `AddInternalOrderCache()` as shorthands.
- `KafkaCachesOptions` is always bound from the `kafkaCaches` configuration section first (so DI needs an `IConfiguration`), and the options lambda applies on top. `AddKafkaCaches("sectionName", ...)` registers another cluster from its own section; one call per cluster.
- MessagePack resolvers go in the last argument: `AddKafkaCaches(o => ..., b => ..., opts => opts.WithMessagePackResolver(def => CompositeResolver.Create(Mine.Instance, def)))`. Put your resolvers before `def`. The setting is process-wide (`PragueMessagePack.Options`); mutating `MessagePackSerializer.DefaultOptions` has no effect on Prague.
- SASL/OAUTHBEARER: set `o.OAuthBearerTokenRefreshHandler`.
- A cache reached only through a foreign key is in the registry, not in DI: register `services.AddSingleton(sp => sp.GetRequiredService<IDataCacheRegistry>().GetCache<CustomerCache>())` to inject it.

Payload attributes (`[DataCacheFromTimestamp]`, `[DataCacheHeader]`, `[DataCacheStringIntern(false)]`): see `prague-modeling`.

## Initial load

The hosted service's `StartAsync` returns once every cache reaches end-of-topic. To load before `host.Run()` (a tool, a test), `await host.DataCachesLoadCompletion();`.

A seeder, projector or job that reads loaded caches is a `BackgroundService` whose `ExecuteAsync` opens with the wait:

```csharp
public sealed class PendingOrdersLogger(IDataCacheRegistry registry, OrderCache orders) : BackgroundService {
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		await registry.LoadingCompletion.WaitAsync(stoppingToken);
		// the caches are loaded from here on
	}
}
```

`StartAsync` returns at that `await`, so it works registered before or after `AddKafkaCaches`, and shutdown cancels the wait. Wait for the load only there: the host starts hosted services one at a time, so a `StartAsync` that waits, registered first, blocks the Kafka worker that completes the load and the host never starts. `host.StopAsync(token)` cancels the consume loops and drains each cache's live worker, bounded by the token.

## Producing

Only the topic's owner produces to a cache topic. A process never consumes its own writes (an `X-Producer-Id` header self-filter), so write through the cache and the local copy changes too:

```csharp
orders.AddOrUpdateAndProduce(order);               // local write; produces only if the value changed
orders.AddOrUpdateAndProduce(order, force: true);  // produces always
orders.RemoveAndProduce(order.OrderId);            // local remove + tombstone
invoices.AddOrUpdateAndProduce(invoice, now, static (next, resident, ts) => next.ChangedAtUnixMs = ts); // stamp, then produce (since 0.10.0)
```

The on-write callback runs only when the local write goes ahead (after the equality check), so the produced record is the stamped one and an unchanged write produces nothing. It has no `force` form. The rules are in `prague-modeling`, Stamping a write.

Write a new instance: a resident row changed in place compares equal to itself, so `AddOrUpdateAndProduce` produces nothing and the change never leaves the process. Clone, change the clone, write the clone (`prague-modeling`, Changing a cached row).

`orders.ForceProduceAll()` re-seeds the topic with every resident record: a repair tool, not a routine call. The raw producer is a keyed singleton: `[FromKeyedServices("kafkaCaches")] KafkaCacheProducer producer` (the key is the section name), then `producer.Produce(topic, key, value)` / `producer.Delete(topic, key)`. It is fire-and-forget: no delivery report, failures are logged, and the local cache does not see the write.

## Multi-partition topics (since 0.10.0)

Skip this for one-partition topics. The essentials:
- Fix the partition count at creation; adding partitions to a live topic re-maps keys and is unsupported.
- The load waits for every partition of the topic. With a shared `group.id`, an instance that holds part of the topic never finishes loading.
- A key's partition is immutable. Writes without a partition go to the key's stored partition; the partition-first overloads (`AddOrUpdateAndProduce(2, order)`, `RemoveAndProduce(2, key)`) throw before the local write when the partition does not exist or differs from the stored one.

Details, readiness and the exceptions: [references/multi-partition.md](references/multi-partition.md).

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

Three things that compile and still fail in production:
- `MapHealthChecks` serves Degraded as 200, so a readiness probe keeps routing to a loading pod. Map Degraded to 503 as above, or pass `failureStatus: HealthStatus.Unhealthy` to `AddPragueKafkaReadiness`.
- **The endpoints are silent during the initial load.** The Kafka worker's `StartAsync` returns after the load, and ASP.NET starts its server after every hosted service has started, so `/health/*` refuses connections until the data is in. A Kubernetes liveness probe then restarts the pod mid-load, and the new pod reloads from scratch. Pick one:
  - a `startupProbe` on `/health/live` whose `failureThreshold × periodSeconds` exceeds the worst-case load time;
  - `services.Configure<HostOptions>(o => o.ServicesStartConcurrently = true)`: the server answers during the load, and readiness reports `InitialLoadIncomplete` until the data is in. Every hosted service then also starts before the load, so each one waits in `ExecuteAsync` as in [Initial load](#initial-load).
- Liveness fails when the consume thread is silent for 3 s (`PollLoopHeartbeatTimeout`) or one message spends 5 s in an after-handler (`HandlerProcessingTimeout`). A slow filter, deserializer or handler does it, and the restart reloads every topic: fix the slow code rather than raising the timeout.

Give Prague its own tagged endpoints: a database check on the same endpoint makes a long replay look like an outage. Every failure reason, `KafkaCachesHealthOptions` and the instrument names: [references/health-and-metrics.md](references/health-and-metrics.md).

## Testing

Query and handler logic needs no broker; Kafka paths need Testcontainers with one `group.id` per consumer. See `prague-testing`.

## Common mistakes

| Wrong | Right |
|---|---|
| `services.AddPragueKafka(...).AddConsumer(...)` | `services.AddKafkaCaches(o => ..., b => b.AddCache<..>())` |
| `OrderCache.Topic` (static) | `OrderCache.TopicNameTemplate` for the template, `orders.Topic` on the instance for the resolved name |
| `[DataCacheTopic("a.{version}")]` | `[DataCacheTopic("a.[v:version]")]` + `o.Vars["version"]` |
| `[v:env]` in `BootstrapServers` or `ClientSettings` | a plain value; placeholders expand in topic templates only |
| relying on registration order to run after the load | `await registry.LoadingCompletion.WaitAsync(stoppingToken)` first in `ExecuteAsync` |
| waiting for the load in a hosted service's `StartAsync` | wait in `ExecuteAsync`; `StartAsync` must return |
| deriving from `KafkaCachesBackgroundService` (obsolete) | a `BackgroundService` that waits in `ExecuteAsync`, as above |
| expecting the local cache to see `producer.Produce(...)` | `AddOrUpdateAndProduce` / `RemoveAndProduce` |
| `ticket.State = x; tickets.AddOrUpdateAndProduce(ticket);` on a resident row | `var next = ticket.Clone(); next.State = x; tickets.AddOrUpdateAndProduce(next);` |
| a shared `group.id` across instances | leave the default, or one id per instance |
| mutating `MessagePackSerializer.DefaultOptions` | `WithMessagePackResolver(...)` in `AddKafkaCaches`' last argument |
| a consumer producing from its own partial copy of the contract | only the topic's owner produces, from the full contract |
| adding partitions to a cache topic in use | recreate the topic with the new count and restart consumers |
| a liveness probe with no `startupProbe` on a default host | a `startupProbe` sized to the load, or `ServicesStartConcurrently = true` |
| readiness mapped with the default status codes | Degraded → 503, or `failureStatus: HealthStatus.Unhealthy` |
| raising `HandlerProcessingTimeout` for a slow handler | enqueue the slow work and return (see `prague-handlers`) |

## Read next

[Kafka integration](https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html), [host with DI](https://nanov.github.io/prague.net/docs/articles/how-to/host-with-di.html), [health and metrics](https://nanov.github.io/prague.net/docs/articles/how-to/health-and-metrics.html), and <https://nanov.github.io/prague.net/llms-full.txt>.

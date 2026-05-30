---
title: Kafka Integration
---

# Kafka Integration

`Prague.Kafka` connects Prague caches to compacted Kafka topics. One background worker per Kafka cluster drives a consumer that funnels messages into the right cache by topic.

## Kafka model in one paragraph

Prague assumes each cache is backed by a single compacted Kafka topic where the message key maps to the cache's primary key. Tombstones (`null` value) delete; non-null values upsert. Partition EOF marks the boundary between *initial load* (replay of the topic state) and *live* (subsequent live updates). Consumer group offset commit, partition rebalance, and broker failover are delegated to `Confluent.Kafka` and librdkafka. If you want a refresher, read the Confluent intro to log compaction — you only need to know that, and the rest of this page makes sense.

## Wiring up

```csharp
services.AddKafkaCaches(
    o => {
        o.BootstrapServers = "kafka:9092";
        o.ClientSettings["group.id"] = "my-service";
        o.Vars["env"] = "prod";              // available for topic-template interpolation
    },
    builder => {
        builder.AddCache<OrderCache, string, Order>();
        builder.AddCache<CustomerCache, int, Customer>("customers-{env}");
    });
```

Per-cluster options live in `KafkaCachesOptions`:

| Property | Meaning |
| --- | --- |
| `BootstrapServers` | Required. Comma-separated broker list. |
| `ClientSettings` | Forwarded to librdkafka (`group.id`, `auto.offset.reset`, SASL settings, etc.). |
| `Vars` | String-string map; values are substituted into topic name templates as `{key}`. |

Multiple clusters are supported by calling `AddKafkaCaches` more than once with different `configsSectionName` arguments; each cluster has its own consumer + producer + handler set.

Global, library-wide options live in `KafkaCachesGlobalOptions`:

```csharp
services.Configure<KafkaCachesGlobalOptions>(o => {
    o.StatisticsEnabled = true;           // default; required for broker-state health
    o.StatisticsIntervalSeconds = 60;
});
```

## Cache registration

`AddCache<TCacheEntity, TKey, TValue>` registers the cache with the consumer:

```csharp
builder.AddCache<OrderCache, string, Order>();               // default topic template
builder.AddCache<OrderCache, string, Order>("orders-v2");    // override topic name
builder.AddCache<OrderCache, string, Order>(sp =>            // resolver
    sp.GetRequiredService<IOptions<TopicNamingOptions>>().Value.Orders);
```

The default topic template comes from `[DataCacheTopic]` (or `"Cache.{ClassName}"` if no attribute). `{env}`-style placeholders are resolved against `KafkaCachesOptions.Vars`.

`AddInternalCache<...>` registers a cache that participates in the DI registry but is **not** consumed from Kafka — useful for caches you populate manually (test fixtures, computed views).

## Filters

`KafkaCacheHandlerBuilder` exposes filter builder methods that compose with AND:

```csharp
builder.AddCache<OrderCache, string, Order>()
    .WithHeaderEqualsFilter("region", "EU")
    .WithHeaderEqualsFilter("region", "EU", "UK")             // OR (string)
    .WithHeaderEqualsFilter("priority", 1, 2, 3)              // OR (int)
    .WithHeaderNotEqualsFilter("source", "synthetic")
    .WithHeaderExistsFilter("tenantId")
    .WithHeaderFilter<DateTime>("dispatchedAt",
        ts => ts > DateTime.UtcNow.AddDays(-7),
        passOnNull: false)
    .WithKeyFilter(static k => !k.StartsWith("tmp-"))
    .WithValueFilter(static o => o.Status == OrderStatus.Active);
```

Numeric overloads (`int`, `long`) compare the header bytes directly without deserialization. The generic `WithHeaderFilter<T>` deserializes via MessagePack first; if that fails, falls back to raw little/big-endian bytes for backward compatibility.

`WithValueFilter(Func<TValue, bool> predicate, bool treatAsDelete = false)` runs the predicate against the **deserialized** cache entity and admits the message only when it returns `true`. It is a plain ingestion-time predicate — *not* an indexed query, so the body is arbitrary C# (combine conditions with `||` / `&&` inside the single lambda; there is no "OR filter" at ingestion). Use it to keep only the records you care about (e.g. a status, a tenant, a non-empty field). Header and key filters are evaluated first, so the value is deserialized only for messages that already passed them.

- **Tombstones** (null-value delete messages) **skip the value filter entirely and still delete** the key — the predicate is never evaluated for a message that carries no value.
- All filter methods (header, key, value) compose with **AND**; multiple `WithValueFilter` calls must all pass.

- **Initial load**: rejected messages are silently dropped.
- **Live phase**: rejected messages still fire `ICacheAfterHandler.Handle(UpdateType.Filtered, ...)` so projectors can observe them.

### `treatAsDelete` — derive tombstones from a filter

Both `WithValueFilter(predicate, treatAsDelete)` and `WithKeyFilter(predicate, treatAsDelete)` accept an optional `treatAsDelete` flag (default `false`). By default a rejected message is dropped without touching the cache (any previously-cached value for that key stays). Pass `treatAsDelete: true` to instead **treat a rejection as a tombstone for the key** — useful when the stream carries soft-deletes as ordinary records (e.g. `Status == Deleted`) rather than as null-value tombstones:

```csharp
builder.AddCache<OrderCache, string, Order>()
    // An order that is no longer Active is removed from the cache.
    .WithValueFilter(static o => o.Status == OrderStatus.Active, treatAsDelete: true);
```

Every key/value filter chain evaluates to a shared `FilterDecision` — `Accept`, `Skip`, or `Delete`:

- **Live phase**: a `Delete` removes the key from the cache and fires `ICacheAfterHandler.Handle(UpdateType.Delete, ...)` (with the old value), exactly as a null-value tombstone would — *not* `UpdateType.Filtered`. If the key was not present, nothing fires (consistent with a delete of an absent key).
- **Initial load**: the key is left absent (removed if an earlier batch had added it); no after-handler fires during load.
- **Multiple filters compose with AND, first-reject wins**: the first filter (in registration order) to reject decides the outcome. A message failing a plain filter is skipped even if a later `treatAsDelete` filter would also have rejected it; only a message whose *first* rejecting filter is a `treatAsDelete` filter becomes a tombstone. This holds across the key and value chains alike.
- A thrown predicate is treated as a plain reject (skip), never as a delete.

> **Key vs value `treatAsDelete`.** A value can change over time, so `treatAsDelete` on a value filter naturally evicts a key whose record stopped qualifying. A **key is immutable**, so `treatAsDelete` on a key filter only evicts an already-cached key when the predicate closes over **mutable external state** (e.g. a tenant allow-list that shrinks) and a *new* message for that key later arrives and is rejected. For a pure key predicate it is effectively inert (a rejected key was never cached). **Header filters do not support `treatAsDelete`** — they are evaluated before the key is deserialized.

## After-handlers

```csharp
builder.AddCache<OrderCache, string, Order>()
    .WithAfterHandler<OrderProjector>();

public sealed class OrderProjector : ICacheAfterHandler<string, Order>
{
    public ValueTask Handle(UpdateType updateType, string key, Order? newValue, Order? oldValue)
    {
        // ... your projection logic
        return ValueTask.CompletedTask;
    }
}
```

After-handlers run only in the live phase. Multiple handlers per cache run sequentially; exceptions in one don't skip the others. See [Conditional Updates](../core-concepts/conditional-updates.md) for the `UpdateType` enum semantics.

## Awaiting initial load

By default the host starts servicing requests as soon as DI is ready, before any cache is loaded. If you need the caches warm before serving:

```csharp
var host = builder.Build();
await host.DataCachesLoadCompletion();      // returns once every cache reaches EOF
await host.RunAsync();
```

## Health checks

```csharp
services.AddHealthChecks()
    .AddPragueKafkaLiveness()       // tag: "live"
    .AddPragueKafkaReadiness();     // tag: "ready"

services.Configure<KafkaCachesHealthOptions>(o => {
    o.PollLoopHeartbeatTimeout   = TimeSpan.FromSeconds(3);   // liveness
    o.HandlerProcessingTimeout   = TimeSpan.FromSeconds(5);   // liveness
    o.MinBrokersUp               = 1;                         // readiness
});
```

- **Liveness** fails if the consumer's poll loop hasn't returned within `PollLoopHeartbeatTimeout`, or any handler has a message in-flight beyond `HandlerProcessingTimeout`.
- **Readiness** fails until every cache reaches initial-load EOF and `MinBrokersUp` brokers are in the `UP` state per librdkafka stats. Disable the broker predicate (`MinBrokersUp = 0`) if you run with `StatisticsEnabled = false`.

## Producer

The DI registration also wires a `KafkaCacheProducer`:

```csharp
public sealed class OrderService
{
    private readonly KafkaCacheProducer _producer;
    public OrderService(KafkaCacheProducer producer) => _producer = producer;

    public void Place(Order o)
    {
        _producer.Produce("orders", o.OrderId, o);   // upsert
        // _producer.Delete("orders", o.OrderId);    // tombstone
    }
}
```

The producer always writes — there is no producer-side dedup. If you need it, consult `cache.Cache.TryGet(...)` before calling `Produce`.

## MessagePack isolation

Prague routes every SerDe call through `PragueMessagePack.Options`, an internally-owned `MessagePackSerializerOptions`. Host mutations of `MessagePackSerializer.DefaultOptions` do not affect Prague's wire format. To extend the resolver chain (custom formatters for your entity types), use the global options builder:

```csharp
services.AddKafkaCaches(
    o => o.BootstrapServers = "kafka:9092",
    builder => { /* caches */ },
    options => options.WithMessagePackResolver(defaultResolver =>
        CompositeResolver.Create(MyCustomResolver.Instance, defaultResolver)));
```

The lambda's input is the default Prague composite (`StringInterningFormatter` + `PragueDateTimeResolver` + `TypelessContractlessStandardResolver`); return whatever composite you want active for SerDe.

## Testing without a broker

Use `Prague.Kafka.TestAdaptor`:

```csharp
services.AddKafkaCacheTestCluster();

// in a test:
var provider = host.Services.GetRequiredService<IKafkaCacheTestBuilderProvider>();
provider.InjectDump("fixtures/orders.pkd");   // replay a MessagePack dump + EOF
```

The dump format is the same one `cache.CreateDump(path)` produces in production — round-trip safe.

## Next

- [Global Last Update Index](global-last-update-index.md) — cross-cache change-tracking for incremental sync.
- [Performance Tuning](performance-tuning.md) — hot-path guidance.

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
    .WithValueFilter(static o => o.Status == OrderStatus.Active)
    .WithKeyFilter(                                           // state resolved from DI, once
        static sp => sp.GetRequiredService<IAllowList>().Ids.ToFrozenSet(),
        static (allow, k) => allow.Contains(k));
```

Numeric overloads (`int`, `long`) compare the header bytes directly without deserialization. The generic `WithHeaderFilter<T>` deserializes via MessagePack first; if that fails, falls back to raw little/big-endian bytes for backward compatibility.

`WithValueFilter(Func<TValue, bool> predicate, bool treatAsDelete = false)` runs the predicate against the **deserialized** cache entity and admits the message only when it returns `true`. It is a plain ingestion-time predicate — *not* an indexed query, so the body is arbitrary C# (combine conditions with `||` / `&&` inside the single lambda; there is no "OR filter" at ingestion). It is evaluated **exactly once per message, as that message is consumed**, and never against records that are already in the cache. Use it to keep only the records you care about (e.g. a status, a tenant, a non-empty field). Header and key filters are evaluated first, so the value is deserialized only for messages that already passed them.

- **Tombstones** (null-value delete messages) **skip the key and value filters entirely and still delete** the key — neither predicate is evaluated for a message that carries no value. A delete is the log's statement that the key is gone, and an ingress predicate cannot meaningfully judge it. Two header-side rules are the deliberate exceptions: a filter that saw its header and **explicitly rejected** the value still drops the tombstone (that is how a consumer selects a sub-stream of a shared topic), and so does the producer self-filter. A header that is merely **missing** does not — see `WithHeaderExistsFilter` below.
- All filter methods (header, key, value) compose with **AND**; multiple `WithValueFilter` calls must all pass, and multiple `WithHeaderExistsFilter` calls all require their header (up to 64 distinct names).
- **Every predicate runs on one thread.** All filters for every cache in the same `AddKafkaCaches` section are evaluated synchronously on the single long-running consume thread. A predicate that blocks or does I/O stalls the initial load *and* the live tail of every other cache in that section.
- **A filter is not an authorization boundary.** It is a retention / load-shedding device: cached entries reflect the state that was in force when they were ingested. Dynamic visibility policy belongs in a reader over `Query()`, where narrowing and widening both take effect immediately.

- **Initial load**: rejected messages are silently dropped — except a tombstone, which still removes the key.
- **Live phase**: rejected messages still fire `ICacheAfterHandler.Handle(UpdateType.Filtered, ...)` so projectors can observe them. A tombstone fires `UpdateType.Delete` instead when the key was resident, and nothing at all when it was not.

### Filters that need DI state

Key, value, and header filters each have an overload that resolves state from the container. The factory runs **exactly once**, while the handler is built, against the root `IServiceProvider`; its result is handed to the predicate on every message, so nothing is resolved from DI on the ingestion path:

```csharp
builder.AddCache<OrderCache, string, Order>()
    // Snapshot — immutable for the process lifetime.
    .WithKeyFilter(
        static sp => sp.GetRequiredService<IAllowList>().Ids.ToFrozenSet(),
        static (allow, key) => allow.Contains(key))
    // Several services at once: a named tuple's element names survive into the predicate.
    .WithKeyFilter(
        static sp => (allow: sp.GetRequiredService<IAllowList>(), clock: sp.GetRequiredService<IClock>()),
        static (s, key) => s.allow.Contains(key) && s.clock.IsOpen)
    // Sugar for a single service — note the explicit type argument.
    .WithValueFilter<IRegionPolicy>(static (policy, o) => policy.Allows(o.Region))
    // Header state filter: both type arguments are explicit.
    .WithHeaderFilter<IClock, long>("dispatchedAt",
        static sp => sp.GetRequiredService<IClock>(),
        static (clock, ts) => ts >= clock.CutoffUnixMs);
```

- **Prefer a snapshot over a live service.** `sp => sp.GetRequiredService<IAllowList>().Ids.ToFrozenSet()` is fixed for the process lifetime, which makes a restart the single well-defined way to reload it — see *Filter lifecycle* below. Capturing the live service instead means the predicate reads its current state on every message, while entries admitted under the old state stay cached regardless.
- **Write the predicate as a `static` lambda.** State arrives as an *argument*, not a capture, so a `static` lambda closes over nothing; Roslyn caches it in a static field and you pay one delegate per process and nothing per message.
- **Spell out the type argument on the `<TService>` overloads.** It is not inferable from an untyped lambda — you get `CS0411`. Write `.WithKeyFilter<IAllowList>(static (allow, key) => allow.Contains(key))`, and both type arguments for the header overload.
- **`sp` is the root provider.** A service registered as `Scoped` throws at build time with a Prague message pointing you at a singleton registration or the snapshot overloads. A `Transient` is resolved once and then captured for the process lifetime.

> **Startup ordering.** The state factory runs while hosted services are being **constructed**, which precedes the `StartAsync` of *every* hosted service — only the initial cache *load* is ordered by registration. A state service that populates itself in its own `StartAsync` or `BackgroundService` is still empty when the factory reads it, so make sure the state is fully materialized by then.

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

> **Key vs value `treatAsDelete`.** A value can change over time, so `treatAsDelete` on a value filter naturally evicts a key whose record stopped qualifying. A **key is immutable**, so `treatAsDelete` on a key filter only evicts an already-cached key when the predicate reads **state that changed after the key was admitted** — a live service captured through the `WithKeyFilter<TService>` overload, or a mutable closure — and a *new* message for that key later arrives and is rejected. For a pure key predicate, or for a snapshot (which is immutable for the process lifetime), it is effectively inert; reload such a filter by restarting the process. **Header filters do not support `treatAsDelete`** — they are evaluated before the key is deserialized.

### Filter lifecycle — what a filter change does and does not do

Filters are an **ingress gate**: the predicate is evaluated exactly once per message, as it is consumed, and is never re-applied to cache state that has already been materialized. Changing a filter — by editing the predicate, or by mutating the state it reads — therefore does not retroactively re-filter what is already in memory:

- **Narrowing** (fewer records qualify) evicts only through `treatAsDelete`, and only when a *new* message arrives for the affected key. On a compacted topic with no further writes for that key, that means never.
- **Widening** (more records qualify) resurrects nothing, ever. Records dropped earlier are not in memory and are never replayed — Prague subscribes and reads forward; it never seeks back over a partition it has already consumed.

The supported way to re-admit them is a **process restart**, and it works out of the box: Prague commits no offsets (`EnableAutoCommit = false`, `EnableAutoOffsetStore = false`), joins under a fresh per-process group id by default, and resets to `Earliest` — so a restarted process re-reads each topic in full under the new state. Configuring `group.id` explicitly does not break this (Prague writes no offset for that group either), but it does make instances split partitions instead of each loading the whole topic.

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

**Your own writes are not applied to your own cache.** Every consumer drops messages stamped with its own producer instance id, so a process that calls `Produce` or `Delete` directly does not see the result in its local cache until it restarts. For deletes, use the generated `cache.RemoveAndProduce(key)` instead: it removes the key locally *and* publishes the tombstone. The tombstone is published whether or not the key was resident locally — a key excluded by an ingress filter, or not loaded yet, still gets its delete — and the `bool` it returns says only whether the local cache held the key.

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

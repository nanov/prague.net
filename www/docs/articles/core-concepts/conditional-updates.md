---
title: Conditional Updates
---

# Conditional Updates

Every write to a Prague cache flows through `AddOrUpdate(value)` or `AddOrUpdate(value, timestampMs)`. The cache decides whether the write is a logical no-op, an update, or an insert by comparing the incoming value to the resident one — and surfaces that decision to downstream observers.

## The `UpdateType` enum

```csharp
public enum UpdateType
{
    Filtered = 0,   // rejected by header/key/value filter (Kafka path, live phase only)
    Same     = 1,   // key exists; value is structurally equal to resident
    Add      = 2,   // new key
    Update   = 3,   // key exists; value differs
    Delete   = 4,   // key removed (Kafka tombstone)
}
```

`Same` is the conditional-update outcome. When a producer re-emits the current state of an entity (a common pattern in change-feed pipelines), the cache compares the incoming POCO to the stored one and reports `Same` instead of `Update`. The reference in storage is not swapped, no indices are re-keyed, no after-handlers see a phantom update.

## How equality is decided

Prague does **not** call `object.Equals`. Every cached value type must implement `ICacheEquatable<T>`:

```csharp
public interface ICacheEquatable<in T>
{
    bool CacheEquals(T? other);
    int CacheGetHashCode();
}
```

The generator emits `CacheEquals` for `[DataCache]` types by walking every public property and comparing field-by-field. Members marked `[DataCacheIgnoreEquality]` are skipped — useful for monotonic timestamps and audit fields that change per emit but don't represent a state change.

```csharp
[DataCache]
public partial class Order
{
    [DataCacheKey] public required string OrderId { get; init; }
    public required decimal Total { get; init; }

    [DataCacheIgnoreEquality]                       // ignored by CacheEquals
    public long LastSeenAtUnixMs { get; init; }
}
```

If two writes of the same `Order` differ only in `LastSeenAtUnixMs`, the second is reported as `Same`.

## Observing the outcome

`ICacheAfterHandler<TKey, TValue>` is the per-cache hook:

```csharp
public interface ICacheAfterHandler<in TKey, in TValue>
{
    ValueTask Handle(UpdateType updateType, TKey key, TValue? newValue, TValue? oldValue);
}
```

Register one per cache via the Kafka handler builder:

```csharp
builder.AddCache<OrderCache, string, Order>()
       .WithAfterHandler<OrderProjector>();
```

`OrderProjector` is invoked **only during the live phase** — i.e. after the consumer has crossed the partition EOF of the initial load. The initial replay does not fire handlers; `Same` results still fire during live processing so projectors can use them as keepalives.

Multiple handlers per cache are allowed (every `AddSingleton<ICacheAfterHandler<TKey, TValue>>` registration is resolved). They run sequentially; an exception in one does not skip the others (they are caught and logged).

## Producer-side dispatch

`KafkaCacheProducer.Produce(topic, key, value)` always writes — there is no conditional skip on the raw producer. The generated `cache.AddOrUpdateAndProduce(document)` is the conditional one: it applies the document locally and produces it only when the local `AddOrUpdate` reported a change.

## Forcing a write

Sometimes the current state has to be re-emitted *as a change* — to refresh `LastUpdated`, to re-run projectors on every replica, to re-stamp the persisted offset — without inventing a field that differs. That is a **forced write**:

```csharp
cache.AddOrUpdate(document, timestampMs, force: true, out _);  // local, unconditional
cache.AddOrUpdateAndProduce(document, force: true);            // local + Kafka, unconditional everywhere
producer.Produce("orders", o.OrderId, o, force: true);         // raw producer, header only
```

The forced overload is `AddOrUpdate(document, long timestampMs, bool force, out TValue? oldValue)` on a generated cache and `AddOrUpdate(key, value, long timestampMs, bool force, out TValue? oldValue)` on `InMemoryDataCache`; with `force: false` it is the ordinary conditional write.

A forced write replaces the resident value even when `CacheEquals` says equal, walks every index (the `LastUpdated` adapters re-stamp their group with the write timestamp; a custom-timestamp adapter re-reads the entity's own timestamp property, so an equal document leaves it unchanged), and — on the Kafka path — carries the `X-Prague-Force` header, so every consumer applies it the same way in both the load and the live phase. After-handlers see `Update` (or `Add` for a new key), never `Same`.

What force does **not** do:

- It does not bypass ingress policy. Header, key and value filters still run; a rejected forced message is skipped or deleted exactly as an unforced one would be.
- It does not touch tombstones — a delete is already unconditional.
- It does not re-apply on the producer that wrote it: the self-filter drops the echo, and the local apply happened in `AddOrUpdateAndProduce` itself.
- It does not fire after-handlers on the initial load; nothing does.

The header is presence-only (its value, `1`, is reserved). Any producer may stamp it. A consumer built before the header existed passes it as an unknown header and applies the message conditionally — it sees `Same`.

## Why this matters

Compacted Kafka topics naturally re-emit the latest state on each rebalance and rebuild. Without conditional updates a downstream projector would see thousands of `Update` events on startup that are no-ops. With them the count of `Update` events is exactly the number of state transitions across the topic's lifetime.

## Next

- [Joins](joins.md) — composing cross-cache views.
- [Kafka integration](../advanced/kafka-integration.md) — wiring filters and after-handlers.

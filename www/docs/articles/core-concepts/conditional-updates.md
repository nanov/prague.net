---
title: Conditional Updates
---

# Conditional Updates

Every write to a Prague cache flows through `AddOrUpdate(value)` or `AddOrUpdate(value, timestampMs)`. The cache decides whether the write is a logical no-op, an update, or an insert by comparing the incoming value to the resident one — and surfaces that decision to downstream observers.

## The `UpdateType` enum

```csharp
public enum UpdateType
{
    Filtered = 0,   // rejected by header/key filter (Kafka path, live phase only)
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

`KafkaCacheProducer.Produce(topic, key, value)` always writes — there is no conditional skip on the producer. If you want producer-side dedup, hold a local `OrderCache` and consult `TryGet` before producing.

## Why this matters

Compacted Kafka topics naturally re-emit the latest state on each rebalance and rebuild. Without conditional updates a downstream projector would see thousands of `Update` events on startup that are no-ops. With them the count of `Update` events is exactly the number of state transitions across the topic's lifetime.

## Next

- [Joins](joins.md) — composing cross-cache views.
- [Kafka integration](../advanced/kafka-integration.md) — wiring filters and after-handlers.

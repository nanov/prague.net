---
title: Conditional Updates
description: How AddOrUpdate uses generated deep equality to report Same instead of Update, the UpdateType enum, and after-handlers on the Kafka live phase.
---

# Conditional Updates

Every write to a Prague cache flows through `AddOrUpdate(value)` or `AddOrUpdate(value, timestampMs)`. The cache decides whether the write is a logical no-op, an update, or an insert by comparing the incoming value to the resident one — and surfaces that decision to the caller and to downstream observers. The `out` overload hands you both halves: `true` for `Add` or `Update`, `false` for `Same`, and the previous row when there was a change. Every block on this page is compiled and asserted in `samples/Prague.Samples/Concepts/ConditionalUpdates.cs`.

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#add-or-update-out)]

On `Same` the resident instance stays in place — the reference in storage is not swapped, no index is re-keyed, and `previous` is `null`.

## The `UpdateType` enum

`Prague.Kafka.UpdateType` names the five outcomes a write can have on the Kafka path:

| Value | | Meaning |
| --- | --- | --- |
| `Filtered` | 0 | Rejected by a header, key or value filter (live phase only). Carries **no key and no values** — `key` is `default`, both values `null`. |
| `Same` | 1 | Key exists; the value is structurally equal to the resident one. The cache was not changed. |
| `Add` | 2 | New key. `oldValue` is `null`. |
| `Update` | 3 | Key exists; the value differs. Both `newValue` and `oldValue` are set. |
| `Delete` | 4 | Key removed — by a tombstone, or by a filter registered with `treatAsDelete`. `newValue` is `null`, `oldValue` the removed row. Fires only when the key was resident. |

`Same` is the conditional-update outcome. When a producer re-emits the current state of an entity (a common pattern in change-feed pipelines), the cache compares the incoming POCO to the stored one and reports `Same` instead of `Update`. No after-handler sees a phantom update.

## How equality is decided

Prague does **not** call `object.Equals`. Every cached value type implements `ICacheEquatable<T>` — `bool CacheEquals(T? other)` and `int CacheGetHashCode()` — and the generator writes the implementation as a partial of your `[DataCache]` type; do not write it by hand. It walks every public property and compares property-by-property, recursing into nested objects and collections:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#cache-equals)]

Members marked `[DataCacheIgnoreEquality]` are skipped — useful for monotonic timestamps and audit fields that change per emit but don't represent a state change. `Product.LastSeenAtUnixMs` in the sample domain is one:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Product.cs)]

If two writes of the same `Product` differ only in `LastSeenAtUnixMs`, the second is reported as `Same` and the earlier value stays resident:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#ignore-equality)]

## Observing the outcome

`ICacheAfterHandler<TKey, TValue>` is the per-cache hook: `ValueTask Handle(UpdateType updateType, TKey key, TValue? newValue, TValue? oldValue)`. Which arguments are populated follows the table above:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#after-handler)]

Register one per cache via the Kafka handler builder; `WithAfterHandler<T>()` adds `T` as a singleton `ICacheAfterHandler<TKey, TValue>`:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#after-handler-registration)]

`OrderProjector` is invoked **only during the live phase** — i.e. after the consumer has crossed the partition EOF of the initial load. The initial replay does not fire handlers. During live processing a structurally equal re-emit reaches the handler as `UpdateType.Same` (with the incoming, equal value as `newValue`, not the instance the cache keeps, and `oldValue == null`), never as a phantom `Update`, so projectors can treat it as a keepalive or ignore it.

Multiple handlers per cache are allowed (every `ICacheAfterHandler<TKey, TValue>` registration is resolved). They run sequentially, awaited one after the other; an exception in one is caught and logged and does not skip the others.

## Producer-side dispatch

`KafkaCacheProducer.Produce(topic, key, value)` always writes — there is no conditional skip on the producer. The generated `cache.AddOrUpdateAndProduce(value)` is the conditional form: it writes locally and produces only when `AddOrUpdate` reported a change. `cache.RemoveAndProduce(key)` removes locally and always publishes the tombstone, resident or not. See [Kafka integration](../advanced/kafka-integration.md#producer).

When the current state has to go back onto the topic even though nothing changed locally (a fresh record for the compacted topic, a re-emit for consumers that are rebuilding or joining later), use `cache.AddOrUpdateAndProduce(value, force: true)`. The local write stays conditional; only the produce becomes unconditional. Consumers apply the record through their own equality gate, so a replica that already holds an equal value reports `Same`, exactly as for any re-emit.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#produce-forced)]

For the whole cache at once there is `cache.ForceProduceAll()`, which snapshots every resident record and produces each one unconditionally. It is a repair operation (re-seeding a topic, bootstrapping a consumer that cannot replay), not something to call in normal operation; the details are in [Kafka integration](../advanced/kafka-integration.md#producer).

## Changing a resident row

The equality gate compares the incoming value with the resident one. Hand it the resident instance itself and the two are always equal. So a row changed in place, through a query result, `TryGet` or an after-handler argument, and then written back is `Same`. The index still files it under its old value: a query by the old value returns a row that no longer matches, and a query by the new value misses it. After-handlers get no `Update`, and `AddOrUpdateAndProduce` produces nothing, so no other replica sees the change.

To change a row, clone it, change the clone and write the clone: `var next = current.Clone(); next.Status = …; cache.AddOrUpdate(next);`. The generated `Clone()` is deep. From then on `next` is the resident instance; the next change starts from another clone. `ExecuteCloned()` and `ExecutePooledCloned()` return copies that can be changed and written back. With `init` setters the compiler enforces the rule, and the next value is built with an object initializer. `ChangeAResidentRowTests` in `Prague.Generated.Tests` pins both the rule and the failure.

## Stamping a write

`AddOrUpdate` and `AddOrUpdateAndProduce` take an optional callback that changes the incoming row once the cache has decided to write it. It gets the incoming row and the resident one (`null` on an add), so a revision counter or a changed-at time can be derived from the current state without a separate `TryGet` that another writer could race.

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#on-write)]

The callback runs at most once per call, inside the store's lock for that key, after the equality check and before the row is stored. It does not run when the incoming row is cache-equal to the resident one, so nothing is stamped and nothing is produced. Indexes and the produced record see the row as the callback left it. A second writer to the same key waits for the lock and sees the first writer's stored row as `resident`, so a counter loses no increments. The lock covers the stored row only: index maintenance runs after it is released, as for every write.

- Change only the incoming row. The resident row is still indexed under its current values and readers may hold it.
- Do not change the key. The call throws `InvalidOperationException` and nothing is written.
- Do not read or write the cache from the callback; it runs while a lock is held.
- An exception from the callback leaves the cache unchanged.
- Put the stamped properties under `[DataCacheIgnoreEquality]`. A stamp that takes part in equality never matches the incoming row's stale value, so every write becomes an `Update`. The stamped properties need a `set` accessor; the rest of the row can stay `init`.
- Pass values through the `state` argument (`now` above) and keep the lambda `static`, so no closure is allocated per write.
- `AddOrUpdateAndProduce(value, force: true)` has no callback form: force only matters when the write reported no change, and then the callback does not run.

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/ConditionalUpdates.cs#on-write-entity)]

## Why this matters

Compacted Kafka topics naturally re-emit the latest state on each rebalance and rebuild. Without conditional updates a downstream projector would see thousands of `Update` events on startup that are no-ops. With them the count of `Update` events is exactly the number of state transitions across the topic's lifetime.

## Next

- [Joins](joins.md) — composing cross-cache views.
- [Kafka integration](../advanced/kafka-integration.md) — wiring filters and after-handlers.

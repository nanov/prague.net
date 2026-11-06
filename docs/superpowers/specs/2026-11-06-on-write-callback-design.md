# On-write callback for AddOrUpdate

Date: 2026-11-06
Status: Implemented (branch `feat/on-write-callback`)

## The feature

`AddOrUpdate` and `AddOrUpdateAndProduce` take an optional static callback that mutates the incoming document once the cache has decided to write it. The callback sees the resident value (or `null` on an add), so a caller can stamp a version, a modified-at time or a carried-over field in one atomic read-modify-write instead of `TryGet` followed by a write that can race.

```csharp
orders.AddOrUpdate(order, static (next, resident) => {
	next.Version = (resident?.Version ?? 0) + 1;
	next.UpdatedAt = DateTime.UtcNow;
});

orders.AddOrUpdateAndProduce(order, user, static (next, resident, u) => next.ModifiedBy = u.Name);
```

## Why it is safe

`ConcurrentCacheStore.TryAddOrUpdateInternal` (the value overload) evaluates `shouldAdd` under the bucket's `Monitor` lock, and the write follows in the same critical section. The retry loop goes round only when `tables != _tables`, which is checked before the bucket is scanned, so a retry never re-runs the predicate. A callback placed beside it therefore runs at most once per call. `ConcurrentDictionary` gives no such guarantee for its factories.

`InMemoryDataCache.AddOrUpdate` walks the indexes after the lock is released, from `r.Value` and `r.OldValue`. The indexes therefore see the mutated document, and so does `AddOrUpdateAndProduce`, which produces `document` after the local write.

## The contract

The callback runs inside the bucket lock, after the equality check and before the node is written.

| Outcome | Callback |
|---|---|
| Add (no resident value) | runs once, `resident == null` |
| Update (`!resident.CacheEquals(next)`) | runs once, `resident` is the current value |
| Same | does not run; nothing is written, the call returns `false` |

- **The decision is made before the callback.** A mutation of an equality-relevant field does not turn an Update into a Same or the reverse. A stamp written on every change (`UpdatedAt`) belongs on a `[DataCacheIgnoreEquality]` property. If it takes part in equality, the incoming document's stale or empty stamp never matches the resident's, so every write is an Update even when nothing else changed.
- **`force: true` on a Same outcome produces the incoming document un-stamped.** The callback did not run, because nothing was written.
- **Mutate `next`, never `resident`.** Index `Update` needs the resident's old field values to remove its entries, and readers hold that instance. This is the existing "clone, change the clone, write the clone" rule.
- **The key must not change.** The store would hold the document under the old key and the indexes under the new one, permanently. The generated overloads enforce this: after the callback returns, still inside the lock, the key is re-read and compared, and a mismatch throws `InvalidOperationException` before the node is written.
- **Stamped properties need a `set` accessor.** An `init` property cannot be assigned from the callback, and every model in `samples/` is `init`-only. The documented shape is an `init` row with its stamped properties `set` and `[DataCacheIgnoreEquality]`.
- **The callback must not touch the cache.** It runs under a bucket lock. Re-entering the same bucket rewrites the chain mid-scan; taking another bucket's lock can deadlock against `GrowTable`, which takes all of them.
- **An exception from the callback leaves the cache unchanged.** The lock is released in `finally` and the node is not written. `next` may be partly mutated; it is not resident.

### What the lock does not cover

The atomicity is the store's. Index maintenance runs after the bucket lock is released, so two writers racing on the same key can apply their index updates in the opposite order to their store writes. That race exists today and the callback does not widen it. What the callback adds is that each writer's callback sees the value the previous writer stored: a version counter loses no increments.

## The change

**`Collections/ConcurrentCacheStore.cs`** — the value overload of `TryAddOrUpdateInternal` becomes generic over a struct policy with static abstract members:

```csharp
internal interface IAddOrUpdatePolicy<TKey, TValue, TArgs> {
	static abstract bool ShouldUpdate(TKey key, TValue next, TValue current, ref TArgs args);
	static abstract void OnWrite(TKey key, TValue next, TValue? current, ref TArgs args);
}
```

`OnWrite` is called on the add path before the node is allocated and on the update path after `ShouldUpdate` returns `true`. The existing `AddOrUpdate(key, value, shouldUpdate)` callers move to a policy whose `OnWrite` is empty, which the JIT removes, so a write without a callback pays nothing per row. This is a query-path-adjacent hot path; re-measure the AddOrUpdate benchmarks A/B.

**`InMemoryDataCache.cs`**

- `AddOrUpdate(key, value, timestamp, Action<TValue, TValue?> onWrite, out TValue? oldValue)` and a `TState` variant taking `Action<TValue, TValue?, TState>`, plus the overloads without `oldValue`.
- The index walk, duplicated today between the two `AddOrUpdate` bodies, moves into one private helper that all overloads share.

**`Prague.Codegen/CacheGenerator.cs`**

- Generated cache (`:7927`): the callback overloads, keyed by `document.{Key}` / `document.GetKey()`. The key guard lives here, because only the generated code knows how to read the key: the user callback and its state ride in a struct `TState` to a static wrapper that calls the user callback and then compares the key. No closure, no allocation.
- `CacheProducerExtensions` (`:2597`): `AddOrUpdateAndProduce(document, onWrite)`, `(int partition, document, onWrite)` and their `TState` variants, through the existing `ResolvePartition` → stamp partition → write → produce path. No `force` combination: force only matters on a Same outcome, where the callback does not run.

**Kafka consume path** — unchanged. Consumers apply the already-mutated record from the log through their ordinary equality gate; this process's own consumer skips it as self-produced.

## Tests

`Prague.Core.Tests`
- Add: callback runs once with `resident == null`.
- Update: runs once with the resident value; Same: never runs.
- Indexes (unique, many, range) reflect the fields the callback set.
- A throwing callback leaves the store and every index unchanged.
- N threads incrementing a version on one key: final version == N.

`Prague.Generated.Tests`
- A callback that changes the key throws, and the store and indexes are unchanged.
- Overload surface compiles for a property key and a `GetKey()` key.

`Prague.Kafka.IntegrationTests`
- The produced payload carries the callback's mutation; a peer consumer holds the stamped document.
- `force: true` on Same produces the un-stamped document.

## Docs (same PR)

- `skills/prague-kafka/SKILL.md` and `skills/prague-modeling/SKILL.md`: the overloads and the mutate-`next`-only rule.
- `docs/llm-readme.txt`.
- A `#region` in `samples/Prague.Samples` and the matching docs article.

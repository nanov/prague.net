# Prague.Core — runtime overview

> **Read when:** orienting in the runtime layer. For specifics jump to a topic file below.

Hot-path runtime. Apply `high-performance-net` skill rules to everything here (Span/stackalloc/ArrayPool/ref struct/zero-alloc are invariants, not suggestions).

## InMemoryDataCache & IDataCache (the spine)

- `InMemoryDataCache<TKey, TValue>` is the storage + query engine. `TKey : notnull, IEquatable<TKey>`.
- It implements `IDataCache<InMemoryDataCache<TKey,TValue>, TKey, TValue>` **directly** — a raw cache participates in joins with no codegen wrapper (`Cache => this`; `Query()` uses the cache type itself as discriminator carrier via `ExecutableQuery<InMemoryDataCache<...>>`).
- `IDataCache<TCache, TKey, TValue>` (`IDataCacheEntity.cs`) exposes `Cache { get; }` (an `InMemoryDataCache<TKey,TValue>` **property**, not a field) and `Query()`. Resolver code reaches the inner cache via `rightCache.Cache` — never through `rightCache.Query()._leftQuery._dataCache`.
- Writes are conditional: `AddOrUpdate` asks the store to replace only when `!old.CacheEquals(new)` and reports `Same` otherwise (no index walk, no `LastUpdated` refresh). The overload `AddOrUpdate(key, value, timestamp, bool force, out oldValue)` picks the always-true predicate when `force` is set — the write is then unconditional and always returns `true` — and is the ordinary conditional write otherwise; every body fans out to the indexes through one `ApplyToIndexes` helper. `IDataCache<TKey,TValue>` carries the forced shape as `AddOrUpdate(document, timestamp, force, out old)`.

## Topic map

| Topic | File |
|-------|------|
| Index types, impls, symmetric/key-set indices, how queries use them | [`indexes.md`](indexes.md) |
| Query builder, candidate intersection, OR clause, pooled execution | [`query.md`](query.md) |
| Joins — resolver families, paired core, inner/chained | [`joins.md`](joins.md) |
| Internal collections (`ValueSet`/`ValueDictionary`/`PooledSet`/`IncrementalIntersecter`) | [`collections.md`](collections.md) |

## Tests

`tests/Prague.Core.Tests` — NUnit, raw-POCO against `InMemoryDataCache` with **no source generator / no `[DataCache]`**. `Join/` subdir mirrors every resolver shape.

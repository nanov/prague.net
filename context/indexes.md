# Indexes

> **Read when:** adding/changing an index, debugging an index plan, or wiring an attribute to an index impl. Cross-layer (attribute → impl → query/join usage).

## Attribute → implementation

Attribute enum `DataCacheIndexType`: `Many`, `Unique`, `Range` (+ `Symmetric = true` modifier). Plus the key-set attribute family. Codegen emits the field + the `Cache.AddXxxIndex(...)` constructor call (see [`generated.md`](generated.md)).

| Attribute | Index impl | Shape |
|-----------|-----------|-------|
| `[DataCacheIndex(Unique)]` | `CacheKeyValueIndex` / `CacheUniqueIndex<…,TLeftKey>` | 1:1 |
| `[DataCacheIndex(Many)]` | `CacheKeyValueListIndex` | 1:N |
| `[DataCacheIndex(Many, Symmetric=true)]` | `CacheSymmetricKeyValueListIndex` (maintains `.Reverse`: leftKey → indexValue) | 1:N + O(1) reverse |
| `[DataCacheIndex(Unique, Symmetric=true)]` | `CacheSymmetricUniqueIndex` (subclass of `CacheUniqueIndex`, has `.Reverse` which is itself an index) | 1:1 + reverse |
| `[DataCacheIndex(Range)]` | sorted B-tree index | range scans (`Gte`/`Lte`/…) |
| `[DataCacheValueIndex]` / `[DataCacheNoValueIndex]` | `CacheKeySetIndex` | keys matching / NOT matching a compare condition |
| `[DataCacheHasValueIndex]` / `[DataCacheHasNotValueIndex]` | `CacheKeySetIndex` | keys with non-null / null value |
| `[DataCacheGlobalLastUpdateIndex]` | LastUpdated index | group-keyed recency |

## How queries use indices

Each `WithXxx(...)` / `UseIndex(...)` on the builder is one candidate-narrowing lane; the runtime intersects lanes on a stackalloc bitmap and short-circuits on the first empty set — see [`query.md`](query.md).

Bulk-intersect primitives on the index types feed joins directly (avoid temp `ValueSet`):
- `CacheKeyValueIndex.IntersectValues<…>(…)` — incl. struct-`TSelector` overloads (JIT-devirtualized key transform, no delegate alloc; `IdentitySelector` folds to the no-selector path) and `IntersectValuesChain<…>` (tail selector applied AFTER `TryGetValue`).
- `CacheSymmetricKeyValueListIndex` exposes 8 bulk primitives (`IntersectValues`/`IntersectValuesVia` × identity/selector × `ref ValueSet`/`ReadOnlySpan`) that emit `JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>` from borrowed sets.
- `CacheKeySetIndex.IntersectWithPaired<TLeft>(ref ValueSet<JoinedKeyPair<TLeft,TKey>>)` — lock-protected in-place paired intersect.

## Symmetric (`.Reverse`)

`Symmetric = true` adds a reverse map enabling O(1) lookup from primary key back to index value. This is what makes **left-index-driven joins** possible (the left entity carries an index value, not the right's PK). Which symmetric variant drives which join family:

| Left-side attribute | Join family |
|---------------------|-------------|
| `[DataCacheIndex(Many, Symmetric=true)]` | left-symmetric-index (fan-out) |
| `[DataCacheIndex(Unique, Symmetric=true)]` | left-unique-index (bijective 1:1) |

Right-side `[DataCacheIndex(Unique)]` on an FK drives the right-unique-index family. Full join detail: [`joins.md`](joins.md).

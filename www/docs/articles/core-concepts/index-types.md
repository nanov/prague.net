---
title: Index Types
description: Unique, Many and Range indices, symmetric variants, predicate key-set indices, sort definitions and indexing external types in Prague.
---

# Index Types

Every query in Prague is planned over typed indices. Each `[DataCacheIndex]` (and its specialized cousins below) becomes a strongly-typed field on the generated cache class plus a corresponding `With{Property}(...)` builder method. The examples on this page run against the sample domain — `Order`, `Customer`, `Product` and two smaller types — and every block is compiled and asserted in `samples/Prague.Samples/Concepts/IndexTypes.cs`.

The `Order` entity declares most of what this page covers:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Order.cs#order-entity)]

## Primary index

`[DataCacheKey]` marks the PK property. It must be applied exactly once per `[DataCache]` type and drives the `TKey` of the underlying `InMemoryDataCache<TKey, TValue>` (`TKey : notnull, IEquatable<TKey>`). The builder narrows by it under two names — the property's own verb and the generic `WithKey(...)`:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#primary-key-lookup)]

## Secondary indices

`[DataCacheIndex(DataCacheIndexType.X)]` declares a secondary index. Three flavours exist:

| Variant | Generated field | Use case |
| --- | --- | --- |
| `Unique` | `CacheUniqueIndex<TKey, TValue, TIndexKey>` | O(1) equality; one entity per index value. |
| `Many` | `CacheKeyValueListIndex<TKey, TValue, TIndexKey>` | O(1) equality; many entities per index value. |
| `Range` | `CacheRangeIndex<TKey, TValue, TIndexKey>` | Ordered range scans over a pooled B-tree; property must implement `IComparable<T>`. |

`Many` is the default if you omit the index type. A `Many` index takes a per-key bucket capacity hint — `[DataCacheIndex(DataCacheIndexType.Many, 4)]`, or just `[DataCacheIndex(4)]` — which sizes each per-key bucket up front; the other two types reject it at compile time. `Product` carries one of each, plus a hinted `Brand` index:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Product.cs)]

Each flavour narrows differently: a `Unique` lane yields at most one row, a `Many` lane every row carrying the value, a `Range` lane an ordered walk from the bound.

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#secondary-index-lanes)]

`Unique` and `Many` emit `With{Property}(...)` overloads for a single value, a nullable value (`null` skips the lane and the query runs as if the call were not there), a `ReadOnlySpan<T>`, an array and a `List<T>`. A multi-value call is an OR within that lane:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#multi-value-lane)]

`Range` indices expose a sub-builder instead. It is type-state: open with `Gt` / `Gte` or `Lt` / `Lte`, then optionally close the other side. There is no `Between` or `Eq`; a disjunction is the query-level `.Or(...)` from [Query Engine](query-engine.md#or--disjunction).

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#range-sub-builder)]

### Reading an index directly

The index fields are public, and they answer with primary keys, not rows. `TryGetValue` on a `Unique` index, `GetValues` on a `Many` index, `GetValuesGte` / `GetValuesLt` / `GetValuesBetween` (both bounds inclusive) on a `Range` index, ordered by the index key:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#direct-index-reads)]

## Symmetric variants

`Symmetric = true` upgrades a `Many` index to `CacheSymmetricKeyValueListIndex<TKey, TValue, TIndexKey>` and a `Unique` index to `CacheSymmetricUniqueIndex<TKey, TValue, TIndexKey>`. The symmetric variant maintains a reverse map (`TKey → TIndexKey`), required for left-index-driven joins, where the left entity carries the index value rather than the right's primary key. `Product.CategoryId` above is declared that way explicitly. Foreign keys declared with `[DataCacheForeignKey<T>(ManyToOne)]` get the symmetric upgrade automatically — `Order.CustomerId` has no `[DataCacheIndex]` at all, yet `OrderCache.CustomerIdIndex` is a `CacheSymmetricKeyValueListIndex<string, Order, int>`. See [Joins](joins.md).

## Predicate-based key-set indices

When a query is repeatedly filtered by `value OP constant` against a property, a key-set index — the set of keys for which the predicate holds, evaluated once per write — is cheaper than a `Many` index: it stores keys only, and the lane is one set lookup. Prague offers four variants:

| Attribute | Builder method | What it tracks |
| --- | --- | --- |
| `[DataCacheHasValueIndex]` | `With{Property}()` | Keys where `Property != null` (for ref / nullable types) or `Property == true` (for `bool`). |
| `[DataCacheHasNotValueIndex]` | `Without{Property}()` | Keys where `Property == null` (for ref / nullable types) or `Property == false` (for `bool`). |
| `[DataCacheValueIndex(CompareOp, value)]` | `With{IndexName}()` | Keys where `Property OP value` is true. |
| `[DataCacheNoValueIndex(CompareOp, value)]` | `With{IndexName}()` | Keys where `Property OP value` is false. |

Each attribute emits exactly the verb in its row: a has-value index gives you `WithSettledAtUnixMs()` and nothing else; the complement needs its own `[DataCacheHasNotValueIndex]` (and its own set), which `ShipmentLeg` declares:

[!code-csharp[](../../../../samples/Prague.Samples/Models/ShipmentLeg.cs)]

The four lanes in use — a nullable, a `bool`, a named value index and a has-not-value index:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#key-set-lanes)]

All four are backed by `CacheKeySetIndex<TKey, TValue>`; the predicate is evaluated once per write. `CompareOp` is `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`. `IndexName` names the generated verb (a trailing `Index` is dropped) and defaults to the property, operator and value: `[DataCacheValueIndex(CompareOp.GreaterThan, 0)]` on `Stock` gives `WithStockGreaterThan0()`, and the same arguments on `[DataCacheNoValueIndex]` give `WithStockLessThanOrEqual0()`. `Product.Stock` sets it to `InStock` so the verb reads `WithInStock()`.

## Sort definitions

`[DataCacheSort("Name", "Prop:1", "OtherProp:-1")]` declares a class-level comparer that you can pass to `Sort(...)` or `SortBounded(...)` on a query. `:1` is ascending, `:-1` is descending; later properties break ties of earlier ones. Multiple sort attributes per class are allowed. `Order` declares `ByCreatedAt` (newest first, then `OrderId`) and `Product` declares `ByPrice`; the generator emits them as `OrderCache.ByCreatedAtComparer` and `ProductCache.ByPriceComparer`, `static readonly IComparer<T>` fields:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#sort-comparers)]

See [Sorted Paging](sorted-paging.md) for when to pick `Sort` over `SortBounded`. Any `IComparer<T>` works; a `readonly struct` comparer makes either sort allocation-free.

## External types

If you can't put attributes on the POCO (a protobuf- or OpenAPI-generated class), use the type-parameter form `[DataCache<T>(keyPropertyName)]` on a cache class of your own and declare the indices there with the property-name overload `[DataCacheIndex("PropertyName", DataCacheIndexType.X)]`. Two constraints:

- `T` must be `partial` and compiled with you: the generator completes it with the `CacheEquals` / `Clone` partials it needs. A type from a referenced assembly cannot be cached this way.
- Only `[DataCacheIndex(propertyName, …)]` is honoured on the cache class. The property-name constructors of the key-set attributes (`[DataCacheHasValueIndex("Prop")]`, `[DataCacheValueIndex("Prop", …)]`) compile but produce no index and no verb today.

[!code-csharp[](../../../../samples/Prague.Samples/Models/CarrierTrackingEvent.cs)]

The cache behaves like any other — the query below runs over the two `Many` indices declared above:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/IndexTypes.cs#external-type-query)]

## Global last-update index

`[DataCacheGlobalLastUpdateIndex<TIndex>]` on a property tracks the latest update per group key, shared across caches, and emits `UpdatedAfter(...)` on the cache whose group key is its primary key. `Order.CustomerId` and `Customer.Id` above both feed `CustomerActivityIndex`. See [Global Last Update Index](../advanced/global-last-update-index.md).

## Next

- [Query Engine](query-engine.md) — how the planner intersects these indices.
- [Joins](joins.md) — how indices participate in cross-cache joins.

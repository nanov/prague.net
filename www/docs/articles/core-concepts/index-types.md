---
title: Index Types
---

# Index Types

Every query in Prague is planned over typed indices. Each `[DataCacheIndex]` (and its specialized cousins below) becomes a strongly-typed field on the generated cache class plus a corresponding `With{Property}(...)` builder method.

## Primary index

`[DataCacheKey]` marks the PK property. It must be applied exactly once per `[DataCache]` type and drives the `TKey` of the underlying `InMemoryDataCache<TKey, TValue>`.

```csharp
[DataCacheKey]
public required string OrderId { get; init; }
```

## Secondary indices

`[DataCacheIndex(DataCacheIndexType.X)]` declares a secondary index. Three flavours exist:

| Variant | Generated field | Use case |
| --- | --- | --- |
| `Unique` | `CacheUniqueIndex<TKey, TValue, TIndexKey>` | O(1) equality; one entity per index value. |
| `Many` | `CacheKeyValueListIndex<TKey, TValue, TIndexKey>` | O(1) equality; many entities per index value. |
| `Range` | `CacheRangeIndex<TKey, TValue, TIndexKey>` | Ordered range scans; property must implement `IComparable<T>`. |

`Many` is the default if you omit `IndexType`.

```csharp
[DataCacheIndex(DataCacheIndexType.Unique)] public required string Email { get; init; }
[DataCacheIndex(DataCacheIndexType.Many)]   public required int CustomerId { get; init; }
[DataCacheIndex(DataCacheIndexType.Range)]  public required long CreatedAtUnixMs { get; init; }
```

Each emits three `WithProperty(...)` overloads:

```csharp
query.WithEmail("alice@example.com");
query.WithEmail(new[] { "alice@example.com", "bob@example.com" });   // ReadOnlySpan
query.WithEmail(new List<string> { "alice@example.com", "bob@example.com" });
```

`Range` indices instead expose a sub-builder:

```csharp
query.WithCreatedAtUnixMs(q => q.Gte(yesterdayMs));
query.WithCreatedAtUnixMs(q => q.Between(startMs, endMs));
query.WithCreatedAtUnixMs(q => q.Lt(nowMs));
```

The sub-builder operators are `Eq`, `Gt`, `Gte`, `Lt`, `Lte`, `Between`.

## Symmetric variants

`Symmetric = true` upgrades a `Many` index to `CacheSymmetricKeyValueListIndex<TKey, TValue, TIndexKey>` and a `Unique` index to `CacheSymmetricUniqueIndex<TKey, TValue, TIndexKey>`. The symmetric variant maintains a reverse map (`TKey → TIndexKey`), required for index-driven joins like `query.JoinOne(leftIndex, rightCache)`. See [Joins](joins.md).

```csharp
[DataCacheIndex(DataCacheIndexType.Many, Symmetric = true)]
public required int CustomerId { get; init; }
```

Foreign keys declared with `[DataCacheForeignKey<T>(ManyToOne)]` get symmetric upgrades automatically.

## Predicate-based key-set indices

When a query is repeatedly filtered by `value == constant` or `value != constant` against a property, an equality-only key-set index beats a `Unique`/`Many` index by trading storage for an O(1) bitmap. Prague offers four variants:

| Attribute | Builder method | What it tracks |
| --- | --- | --- |
| `[DataCacheHasValueIndex]` | `WithProperty()` | Keys where `Property != null` (for ref / nullable types) or `Property == true` (for bool). |
| `[DataCacheHasNotValueIndex]` | `WithoutProperty()` | Keys where `Property == null` (for ref / nullable types) or `Property == false` (for bool). |
| `[DataCacheValueIndex(CompareOp, value)]` | `With{IndexName}()` | Keys where `Property OP value` is true. |
| `[DataCacheNoValueIndex(CompareOp, value)]` | `Without{IndexName}()` | Keys where `Property OP value` is false. |

```csharp
[DataCacheHasValueIndex]
public DateTime? SettledAt { get; init; }

[DataCacheValueIndex(CompareOp.Equal, OrderStatus.Pending)]
public required OrderStatus Status { get; init; }
```

Both produce O(1) `CacheKeySetIndex` lookups; the predicate is evaluated once at insert time.

## Sort definitions

`[DataCacheSort("Name", "Prop:1", "OtherProp:-1")]` declares a class-level comparer that you can pass to the `Sort(...)` operator on a query. `:1` is ascending, `:-1` is descending. Multiple sort attributes per class are allowed:

```csharp
[DataCache]
[DataCacheSort("ByDate", nameof(CreatedAtUnixMs) + ":-1")]
[DataCacheSort("ByCustomerThenDate", nameof(CustomerId) + ":1", nameof(CreatedAtUnixMs) + ":-1")]
public partial class Order { ... }
```

The generator emits `OrderCache.ByDateComparer` and `OrderCache.ByCustomerThenDateComparer` as `static readonly IComparer<Order>`. Use them with `.Sort(OrderCache.ByDateComparer)`.

## External types

If you can't add attributes to the source POCO (third-party DTOs, generated proto classes), use the type-parameter form `[DataCache<T>]` plus class-level `[DataCacheIndex("PropertyName", DataCacheIndexType.X)]` and the property-name form of the predicate indices:

```csharp
[DataCache<ExternalDto>(nameof(ExternalDto.Id))]
[DataCacheIndex(nameof(ExternalDto.CustomerId), DataCacheIndexType.Many)]
[DataCacheValueIndex(nameof(ExternalDto.Status), CompareOp.Equal, OrderStatus.Pending)]
public partial class ExternalDtoCache;
```

The generated `Clone` / `Equals` machinery for the external type is also emitted on this class.

## Next

- [Query Engine](query-engine.md) — how the planner intersects these indices.
- [Joins](joins.md) — how indices participate in cross-cache joins.

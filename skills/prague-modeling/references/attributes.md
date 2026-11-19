# Other attributes and the generated surface

The attributes `SKILL.md` does not cover, with the rule for each that a build does not enforce. Full reference: [attributes](https://nanov.github.io/prague.net/docs/articles/reference/attributes.html).

| Attribute | Effect and rule |
|---|---|
| `[DataCacheSort("Name", "Prop:1", "Other:-1")]` | Class-level, repeatable. Emits `static readonly IComparer<T> NameComparer` (a sealed class; `string` columns compare ordinally); `:1` asc, `:-1` desc. End with the key so the order is total. When to use it and when a struct comparer: `prague-queries`, Sorting and paging. |
| `[DataCacheIgnoreEquality]` | Excluded from `CacheEquals`: for audit fields or timestamps that change on every emit without being a state change. `Clone()` still copies it. Stamped fields (see Stamping a write) carry it. |
| `[DataCacheTopic("orders.[v:env]")]` | Kafka topic template (default `Cache.<ClassName>`), exposed as `XxxCache.TopicNameTemplate`; see `prague-kafka`. |
| `[DataCacheFromTimestamp]` | Filled from the Kafka message timestamp on every consumed message: `long` (Unix ms), `DateTime` or `DateTimeOffset`, nullable allowed. Needs a `set` accessor. Consume-only: never written to a header. |
| `[DataCacheHeader("tenant-id")]` | Read from a Kafka header on consume, written back as that header on produce. Needs a `set` accessor. |
| `[DataCacheStringIntern(false)]` | Keeps a string out of the intern pool (ids, free text; interning is on by default for every deserialized string). The property is `partial` (CACHE053 otherwise): the generator supplies its implementing part. |
| `[DataCacheGlobalLastUpdateIndex<TIndex>]` | Group key of a cross-cache last-update watermark. `TIndex` is `public partial class X : IDataCacheGlobalLastUpdateIndex<TGroupKey>;` (the generator completes it). On the cache's primary key it adds `UpdatedAfter(...)` (see `prague-queries`). |
| `[DataCacheFrom<TSource>]` | Not a generator input: an analyzer plus IDE fix that writes `TSource`'s public properties into your class and records a hash; `DATACACHE002` warns when `TSource` changes. The properties are yours afterwards. |
| `[DataCacheJsonSerializables]` | Emits nothing today. For JSON, every project with a `[DataCache]` type gets `{RootNamespace}.Serialization.DataCachesResolver` (an `IJsonTypeInfoResolver` for `QueryResults<T>`), and `services.ConfigureDataCachesJsonOptions()` when ASP.NET Core is referenced. |

## Types you cannot annotate

For a generated class you own but cannot edit (protobuf, OpenAPI), put `[DataCache<TExternal>(nameof(TExternal.Id))]` on a `partial` cache class and declare indexes there with `[DataCacheIndex("PropertyName", DataCacheIndexType.Many)]`:

```csharp
[DataCache<CarrierTrackingEvent>(nameof(CarrierTrackingEvent.EventId))]
[DataCacheIndex(nameof(CarrierTrackingEvent.ShipmentId), DataCacheIndexType.Many)]
public partial class CarrierTrackingEventCache;
```

`TExternal` must be `partial` and in your compilation. At class level only `[DataCacheIndex]`, `[DataCacheIgnoreEquality]` and `[DataCacheTopic]` are read. The property-name forms of the key-set attributes compile but produce nothing, and foreign keys, sorts and global indexes have no class-level form.

## What the generator emits

For `Order`:
- `OrderCache` with `new OrderCache()`, `.Cache` (the raw `InMemoryDataCache<string, Order>`), one public `{Prop}Index` field per index, `AddOrUpdate(doc[, timestamp][, out old])` (the `out` form returns false when the value was equal), `AddOrUpdate` with an on-write callback (since 0.10.0), `Remove(key)`, `Query()`, `Prepare()` / `Prepare<TArgs>()`, `{Name}Comparer`, `TopicNameTemplate`, and `AddJoinedCaches(...)` when the type has foreign keys.
- On the entity: `Clone()` (deep), `CacheEquals(other)`, `CacheGetHashCode()`.
- With `Prague.Kafka` referenced: `AddOrUpdateAndProduce` / `RemoveAndProduce`, `PartitionCount`, and the registration shorthands `b.AddOrderCache()` / `b.AddInternalOrderCache()` (same as `AddCache<OrderCache, string, Order>()`). Generated for leaf types only: an entity with derived `[DataCache]` types gets no Kafka surface.

To read it, set `<PragueCacheShowGeneratedFiles>true</PragueCacheShowGeneratedFiles>` in the project; the files land in `Generated/`, excluded from compilation.

---
title: Attributes Reference
description: Every Prague attribute, its parameters and defaults, the exact members the source generator emits for it, the index storage it picks and the constraints it enforces.
---

# Attributes and what they generate

Every attribute lives in the `Prague.Core` namespace and ships in the `Prague.Attributes` package; the `Prague.Codegen` analyzer reads them and emits the cache. This page is the cheat sheet: one row per attribute, then one section per attribute with the parameters, the generated names and the rules. The examples are the compiled samples the rest of the docs use (`Order`, `Customer`, `Product`, …) plus a handful of models that exist only for this page; every block is a test that runs in CI. Diagnostics are listed by id in [Generator Diagnostics](diagnostics.md).

Two rules apply to all of them: the annotated type must be `partial` (the generator adds a second declaration to it), and it must be declared at namespace level, not nested in another type.

| Attribute | Purpose | Generated members |
| --- | --- | --- |
| `[DataCache]` | Marks the entity, optionally names the cache | `XxxCache` with `Cache`, `AddOrUpdate`, `Remove`, `TryGet`, `Query()`, `Prepare()`; `Clone()` and `CacheEquals` on the entity; `AddXxxCache()` for Kafka |
| `[DataCache<T>]` | Cache for a type you cannot annotate | The same surface on your own `partial` cache class |
| `[DataCacheKey]` | Primary key | `TKey`, `TryGet(key)`, `WithKey(…)`, `With{Key}(…)` |
| `[DataCacheIndex]` | `Unique` / `Many` / `Range` secondary index | `{Prop}Index` field, `With{Prop}(…)` |
| `[DataCacheForeignKey<T>]` | Relation to another cache | `{Prop}Index`, `With{Prop}(…)`, `JoinWith{T}()` / `InnerJoinWith{T}()` on both caches |
| `[DataCacheForeignKey<T, TSelector>]` | Relation whose value needs converting first | Same, forward direction only |
| `[DataCacheHasValueIndex]` | Key set: non-null, or `true` | `Has{Prop}Index`, `With{Prop}()` |
| `[DataCacheHasNotValueIndex]` | Key set: null, or `false` | `HasNot{Prop}Index`, `Without{Prop}()` |
| `[DataCacheValueIndex]` | Key set: compares with a constant | `{Prop}{Op}{Value}Index`, `With{Prop}{Op}{Value}()` |
| `[DataCacheNoValueIndex]` | Key set: the negation | `{Prop}{InvertedOp}{Value}Index`, `With…()` |
| `[DataCacheGlobalLastUpdateIndex<TIndex>]` | Cross-cache "what changed since" | `TIndex` partial, `AddGlobalIndexes(…)`, `UpdatedAfter(…)` when the group key is the PK |
| `[DataCacheGlobalLastUpdateIndex<TIndex, TFilter>]` | Same, counting only rows the filter admits | Same |
| `[DataCacheSort]` | Named comparer | `XxxCache.{Name}Comparer` |
| `[DataCacheIgnoreEquality]` | Exclude a property from `CacheEquals` | Changes `CacheEquals` / `CacheGetHashCode` (see the caveat) |
| `[DataCacheTopic]` | Kafka topic template | `XxxCache.TopicNameTemplate`, `TopicTemplate` |
| `[DataCacheHeader]` | Property ↔ Kafka header | `Enrich` / `Derich` on the entity |
| `[DataCacheFromTimestamp]` | Property ← message timestamp | `Enrich` on the entity |
| `[DataCacheFrom<T>]` | Copy properties from another type (IDE code fix) | Nothing at build time |
| `[DataCacheJsonSerializables]` | Marker on a `JsonSerializerContext` | Nothing (see the section) |

To read the generated code, set `<PragueCacheShowGeneratedFiles>true</PragueCacheShowGeneratedFiles>` in the project: the files land in `Generated/` next to the project (excluded from compilation). The samples project does the same with `EmitCompilerGeneratedFiles`.

## `[DataCache]`

Targets a class or record. Constructor `(string? cacheClassName = null)`, named property `CacheClassName`. The cache class is `{Type}Cache` in the entity's namespace unless you name it.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#data-cache)]

What the generator emits for `Order`:

- `partial class OrderCache : IDataCache<string, Order>, IDataCache<OrderCache, string, Order>, ICacheRegisterable<OrderCache>` with `Cache` (the raw `InMemoryDataCache<TKey, TValue>`), `Statistics`, two constructors (`DataCacheStatisticsCollector?` or `bool collectStatistics`), `AddOrUpdate(doc)`, `AddOrUpdate(doc, long timestamp)`, both with an `out TValue? oldDocument` twin that returns `false` when the stored value was equal, `Remove(key)`, `Remove(key, timestamp)`, `Remove(key, out value)`, `TryGet(key, out doc)`, `Query()`, `Prepare()` / `Prepare<TArgs>()`, `AddJoinedCaches(…)`, `AddGlobalIndexes(…)`, `TopicNameTemplate`, `QueryParser` and the static `Register` that `DataCacheRegistryBuilder.Register<OrderCache>()` calls.
- `OrderCacheQueryExtensions` (eager `With…`, `JoinWith…`, `UpdatedAfter`) and `OrderCachePreparedQueryExtensions` (the prepared twins) — one method family per attribute below.
- On the entity itself: `Clone()` (`ICacheClonable<T>`, deep copy), `CacheEquals(other)`, `CacheEquals(other, forceDeep)`, `CacheGetHashCode()` and the static `CacheEqualityComparer` (`ICacheEquatable<T>`), plus `IDataCacheItem<TKey, T>` and a `__PragueMetadata__` slot for the Kafka timestamp and offset. The bodies live in two per-assembly helpers, `Prague.Core.CacheCloneHelper_{Assembly}` and `CacheEqualityHelper_{Assembly}`.
- `{RootNamespace}.Serialization.DataCachesResolver`, an `IJsonTypeInfoResolver` that knows `QueryResults<T>` for every entity, and `ConfigureDataCachesJsonOptions(this IServiceCollection)` when ASP.NET Core's `ConfigureHttpJsonOptions` is in the compilation.
- With `Prague.Kafka` referenced: `KafkaCacheHandlersBuilderExtensions.AddOrderCache()` (plus a `string topicName` and a `Func<IServiceProvider, string>` overload), `AddInternalOrderCache()`, `IEnrichable<Order>` on the entity, `AddOrUpdateAndProduce` / `RemoveAndProduce` extension methods and the `CacheMarshall` used by dump and restore. The Kafka surface is generated for leaf types only; an entity with derived `[DataCache]` types gets none.

Rules: the type must be `partial` (CS0260 otherwise), namespace-level (CACHE004) and have exactly one `[DataCacheKey]` (CACHE001). Collections whose element or value type is `object` or `dynamic` cannot be cloned (CACHE005–007). A type with no `[DataCacheKey]` is accepted by the generator, which then assumes a `string` key and calls `document.GetKey()`; unless you implement `IDataCacheItem<TKey, T>` and provide `GetKey()` yourself the build fails with CS1061 — see [Generator Diagnostics](diagnostics.md#common-build-errors-that-are-not-diagnostics).

## `[DataCache<TCacheItem>]`

Targets the cache class, for an item type you cannot (or do not want to) annotate. Constructors `()` and `(string keyProperty)`, named property `KeyProperty`. The key is `KeyProperty`, else the item's own `[DataCacheKey]`, else its `IDataCacheItem<TKey, TValue>` implementation (CACHE013 when none applies, CACHE012 when the name is wrong, CACHE019 when the two disagree).

Indexes and the equality exclusion move to class level and name the property with `nameof`:

[!code-csharp[](../../../../samples/Prague.Samples/Reference/ReferenceModels.cs#external-cache)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#external-cache-usage)]

The generated surface is the same as for `[DataCache]`, including `Clone()` and `CacheEquals` — which are emitted as a second partial declaration **of the item type**. The item type must therefore be `partial` and live in the same compilation (CS0260 otherwise); a type from another assembly cannot be cached this way today. Only `[DataCacheIndex]`, `[DataCacheIgnoreEquality]` and `[DataCacheTopic]` are read at class level. The `propertyName` constructors on the key-set attributes compile but the generator does not act on them, and foreign keys, sorts and global indexes have no class-level form. A `[DataCacheTopic]` on the cache class is used unless the item type also carries one; if both do and differ, CACHE020.

## `[DataCacheKey]`

Targets exactly one property (CACHE001). Its type becomes `TKey` and must implement `IEquatable<TKey>` (`int`, `long`, `string`, `Guid`, a record struct …; a `byte[]` key fails with CS0311 in the generated code).

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#data-cache-key)]

Emits `TryGet(key, out doc)` on the cache, and on the builder `With{KeyProperty}(…)` and `WithKey(…)` over the cache's own key index, with the same overloads as a `Unique` index (single value, `ReadOnlySpan<T>`, `List<T>`). The key may additionally carry `[DataCacheIndex(Unique)]` or `[DataCacheIndex(Range)]`, never `Many` (CACHE021), and a `ManyToOne` foreign key is not allowed on it (CACHE040). A `OneToOne` foreign key on the key joins PK-to-PK without an index.

## `[DataCacheIndex]`

Targets a property (or, for `[DataCache<T>]`, the cache class). Property-level constructors: `()`, `(DataCacheIndexType indexType)`, `(DataCacheIndexType indexType, int initialCapacity)`, `(int initialCapacity)`. Class-level: `(string propertyName, DataCacheIndexType indexType[, int initialCapacity])` and `(string propertyName, string indexName, DataCacheIndexType indexType[, int initialCapacity])`. Named properties: `IndexType` (default `Many`), `Symmetric` (default `false`), `InitialCapacity` (default `-1`, the library default), `PropertyName`, `IndexName`.

| `DataCacheIndexType` | Field type on `XxxCache` | Builder method | Direct probe |
| --- | --- | --- | --- |
| `Unique` | `CacheUniqueIndex<TKey, T, TProp>` | `With{Prop}(value)`, `(ReadOnlySpan<TProp>)`, `(List<TProp>)` | `TryGetValue(value, out key)` |
| `Many` (default) | `CacheKeyValueListIndex<TKey, T, TProp>` | same three overloads; several values are an OR | `GetValues(value)` |
| `Range` | `CacheRangeIndex<TKey, T, TProp>` | `With{Prop}(q => q.Gte(a).Lt(b))` | `GetValuesGte`, `GetValuesLt`, `GetValuesBetween`, … |

The field is always `{Property}Index`. A `null` single value skips the lane; an empty span skips it too.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#index-unique)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#index-many)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#index-range)]

`Range` needs a property type that implements `IComparable<T>` or is a built-in comparable (CACHE002). `Symmetric = true` upgrades `Many` to `CacheSymmetricKeyValueListIndex` and `Unique` to `CacheSymmetricUniqueIndex`; both add a `Reverse` map from key to index value, which left-driven joins need ([Joins](../core-concepts/joins.md)):

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#index-symmetric)]

`InitialCapacity` sizes each per-value bucket of a `Many` index and is rejected on the other two types (CACHE051):

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#index-capacity)]

A `Many` index on a collection property (`List<T>`, `T[]`, …) indexes the entity under each element and is stored as `CacheCollectionSymmetricKeyValueListIndex`; `With{Prop}(element)` filters by one element. `IndexName` renames the field and the builder method (`PriceBandIndex` / `WithPriceBand` in the external example above) but is only read at class level — on a property the generator ignores it and the field stays `{Property}Index`. See [Index Types](../core-concepts/index-types.md) for how the planner uses them.

## `[DataCacheForeignKey<T>]`

Targets a property. Direct form: `(DataCacheJoinType joinType)` on the property that holds the foreign key; `T` is the referenced entity. Dual form: `(DataCacheJoinType joinType, string rightProperty)` on the anchor side (usually the referenced entity's key), naming the FK property on `T`; the generator treats it exactly like the direct form on `T.{rightProperty}` with the inverted cardinality (`OneToMany` here is `ManyToOne` there). Properties `JoinType`, `RightProperty`.

`T` must be `[DataCache]` (CACHE008) and the property type must equal `T`'s key type (CACHE009); a `List<TKey>` property is a collection FK and only supports `ManyToOne` (CACHE049, CACHE050). The attribute is not `AllowMultiple`: two `DataCacheForeignKey<T>` with the same `T` on one property is CS0579.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#foreign-key)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/ReferenceModels.cs#dual-form-fk)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#foreign-key-dual-form)]

What is emitted, by declaration:

| Declaration | Auto index on the declaring cache | Forward (`JoinWith{T}` on the declaring cache) | Reverse (`JoinWith{This}` on `T`'s cache) |
| --- | --- | --- | --- |
| `ManyToOne` on a scalar | `CacheSymmetricKeyValueListIndex` | yes, slot `T?` | yes, slot `QueryResults<This>` |
| `OneToOne` on a non-key scalar | `CacheSymmetricUniqueIndex` | yes, `T?` | yes, `This?` |
| `OneToOne` on the key | `CacheUniqueIndex` (unused by the join) | yes, PK-to-PK, `T?` | yes, `This?` |
| `OneToMany` on a scalar | `CacheKeyValueListIndex` | no — declare `ManyToOne` | yes, `QueryResults<This>` |
| `ManyToOne` on a `List<TKey>` | `CacheCollectionSymmetricKeyValueListIndex` | yes, `QueryResults<T>` | yes, `QueryResults<This>` |

Every row also gets `With{Prop}(…)` (the FK index is an ordinary index) and an `InnerJoinWith{T}` twin that drops lefts with no match. Each `JoinWith` / `InnerJoinWith` comes in three flavours — no filter, `q => q.With…()` filter, and filter plus a user argument — and, after `Sort` / `SortBounded`, the outer no-filter `JoinWith{T}()` only. A 1:N `JoinWith{T}(b => b.JoinWith{U}())` nests a continuation per right row. Chains go five joins deep; result slots are `Left`, `Right`, `Right2`, …

An explicit `[DataCacheIndex]` on the same property replaces the auto index: `Many` without `Symmetric` is upgraded with a warning (CACHE042), `Unique` or `Range` is an error (CACHE041). Caches built through `DataCacheRegistryBuilder` or `AddKafkaCaches` are wired to each other; standalone `new XxxCache()` instances need `left.AddJoinedCaches(right, …)` once. Details and allocation behaviour: [Joins](../core-concepts/joins.md).

## `[DataCacheForeignKey<T, TSelector>]`

The selector form, for a property whose value is not the referenced key but can be turned into one. `TSelector` is a `readonly struct` implementing `IForeignKeySelector<TFk, TPk>` (static abstract `TPk Select(TFk fk)`) where `TFk` is the property type and `TPk` the key type of `T` (CACHE045, CACHE046); it must be reachable from generated code (CACHE048). Only `OneToOne` and `ManyToOne` are allowed (CACHE047) and only the forward join is emitted — selectors are not invertible, so `T`'s cache gets no `JoinWith{This}`.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/ReferenceModels.cs#audit-event)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#foreign-key-selector)]

The auto index is keyed by the property's raw type (`CacheSymmetricKeyValueListIndex` for `ManyToOne`, `CacheSymmetricUniqueIndex` for `OneToOne`, none for `OneToOne` on the key), so `With{Prop}(rawValue)` filters without any join; the selector runs when the join resolves.

## `[DataCacheHasValueIndex]` and `[DataCacheHasNotValueIndex]`

Target a property that is a reference type, a nullable value type or `bool` (CACHE022 / CACHE023). Constructors `()`, and the class-level `(string propertyName)` / `(string propertyName, string indexName)` which the generator currently does not act on; named `PropertyName`, `IndexName`. Both emit a `CacheKeySetIndex<TKey, T>` — a set of keys maintained on every write, with `Contains(key)`, `GetKeys()` and `ApproximateCount`.

| Attribute | Field | Builder method | Members of the set |
| --- | --- | --- | --- |
| `[DataCacheHasValueIndex]` | `Has{Prop}Index` | `With{Prop}()` | non-null, or `true` for `bool` |
| `[DataCacheHasNotValueIndex]` | `HasNot{Prop}Index` | `Without{Prop}()` | null, or `false` for `bool` |

`IndexName` renames the field; the builder method is always derived from the property name.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#has-value-index)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#has-not-value-index)]

## `[DataCacheValueIndex]` and `[DataCacheNoValueIndex]`

Target a property. Constructor `(CompareOp op, object value)` where `value` is a compile-time constant (number, string, char, bool, enum); class-level `(string propertyName, CompareOp op, object value)` exists but is not processed. Named `IndexName`, `PropertyName`. `CompareOp` is `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`. Both emit a `CacheKeySetIndex<TKey, T>` with the predicate evaluated on every write; `NoValueIndex` keeps the keys that do **not** satisfy it. `AllowMultiple` — several per property are fine.

Default names are `{Prop}{Op}{Value}Index` for the field and `With{Prop}{Op}{Value}()` for the method, where `Value` is the constant rendered as an identifier (enum member name, `Neg5` for `-5`, `9_5` for `9.5`, strings with spaces, dashes and dots removed). `NoValueIndex` names the inverted operation (`GreaterThan` → `LessThanOrEqual`, `Equal` → `NotEqual`, …). With `IndexName`, the field is the name as given and the method is `With{IndexName}` minus a trailing `Index`.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#value-index)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#no-value-index)]

## `[DataCacheGlobalLastUpdateIndex<TIndex>]`

Targets a property — the group key. `TIndex` is a `public`, namespace-level `partial class` implementing `IDataCacheGlobalLastUpdateIndex<TGroupKey>` (CACHE030, CACHE035) and the property type must be `TGroupKey` (CACHE031). Constructors `()` — the timestamp is the one passed to `AddOrUpdate` (the Kafka message time when consuming) — and `(string timestampPropertyName)`, naming a `long`, `DateTime` or `DateTimeOffset` property to read instead. `AllowMultiple`: one property can feed several indexes, and several caches can feed the same index.

Emits:

- the `TIndex` partial: `Index` (a `LastUpdatedIndex<TGroupKey>`), `TryGetMin` / `TryGetMax` with and without the key, `TryGetMax(ReadOnlySpan<TGroupKey>, out long)`, `GetEntitiesCount(key)`;
- `XxxCache.AddGlobalIndexes(TIndex …)` on every cache that feeds it, called by the registry and by `AddKafkaCaches`, which also register the index as a singleton (`registry.GetGlobalIndex<TIndex>()`, or inject `TIndex`);
- when the group key is the cache's own key: `UpdatedAfter(long)`, `UpdatedAfter(DateTimeOffset)` and both with `out long max` on the eager builder, and on the prepared builder `UpdatedAfter` for `long`, `DateTime`, `DateTimeOffset`, `(after, untilInclusive)` windows and `Func<TArgs, long>` forms. If both a plain and a filtered index sit on the key, `UpdatedAfter` binds to the plain one.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#global-last-update-index)]

The whole mechanism, with the sync-loop it is designed for, is in [Global Last Update Index](../advanced/global-last-update-index.md).

## `[DataCacheGlobalLastUpdateIndex<TIndex, TFilter>]`

The filtered form. `TFilter` is a `readonly struct` implementing `IDataCacheGlobalLastUpdateFilter<TEntity>` for the annotated entity (CACHE032, CACHE033), reachable from generated code (CACHE034). Only entities for which `Include(entity)` is `true` count; membership is re-evaluated on every write, so an entity leaving the set is removed from the index and one entering it is added. Same constructors and emitted members as the plain form. The `AuditEvent` model above declares one plain and one filtered index, both reading their timestamp from `OccurredAtUnixMs`:

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#global-index-filtered)]

## `[DataCacheSort]`

Targets the class, `AllowMultiple`. Constructor `(string name, params string[] sortProperties)`; each entry is `"Property:1"` (ascending) or `"Property:-1"` (descending). Emits `public static readonly IComparer<T> {Name}Comparer` on the cache, comparing property by property and sorting `null` entities first. A `string` property compares ordinally (`string.CompareOrdinal`), so the order is the same under every culture and two distinct strings never compare equal; any other property type uses `Comparer<TProp>.Default`.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#sort)]

Pass it to `Sort(…)` or `SortBounded(…)` ([Sorted Paging](../core-concepts/sorted-paging.md)); end the list with the key so the order is total. A misspelled property fails the build inside the generated comparer (CS1061); an entry without a `:direction` is silently skipped, and a sort whose entries are all skipped emits no comparer at all (CS0117 where you use it).

## `[DataCacheIgnoreEquality]`

Targets a property; for `[DataCache<T>]` the cache class with `(string propertyName)` (CACHE017, CACHE018). Intended effect: the property is left out of the generated `CacheEquals` and `CacheGetHashCode`, so a re-emit that differs only in that property is reported as `UpdateType.Same` by `AddOrUpdate(doc, out old)` and by the Kafka after-handlers ([Conditional Updates](../core-concepts/conditional-updates.md)). `Clone()` still copies it.

The sample below shows the effect: the two products differ only in `LastSeenAtUnixMs`, so `CacheEquals` returns `true` and `AddOrUpdate` reports the write as `Same` without swapping the stored instance.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#ignore-equality)]

## `[DataCacheTopic]`

Targets the class. Constructor `()` gives `Cache.{ClassName}`; `(string topicNameTemplate)` sets the template. Emits `public const string TopicNameTemplate` and the instance property `TopicTemplate` on the cache. `[v:name]` placeholders are resolved against `KafkaCachesOptions.Vars` and `[e:NAME]` against environment variables when the consumer starts; `AddXxxCache()` / `AddCache<…>()` subscribe to the result unless given an explicit topic name or resolver.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#topic)]

One cache reads one compacted topic, which can have one or more partitions. The load phase ends when every partition has reached its EOF. The topic settings and the reasoning are in [Kafka Integration](../advanced/kafka-integration.md).

## `[DataCacheHeader]`

Targets a settable property; only has an effect when `Prague.Kafka` is referenced. Constructor `()` uses the property name as the header name, `(string headerName)` sets it. The generator adds `IEnrichable<T>` to the entity: `GetEnricher()` returns the consume-side enricher that fills every `[DataCacheHeader]` property from the message headers, and `Derich(entity, Headers)` / `Derich(entity, ref KafkaHeaders)` write them back when producing (`AddOrUpdateAndProduce`, dump and restore). `int`, `int?`, `long` and `long?` are MessagePack-encoded (the legacy raw 4- and 8-byte forms are still accepted on read), `string` is UTF-8, `Guid` is its 16 bytes, anything else goes through MessagePack with `PragueMessagePack.Options`.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#header-and-timestamp)]

The header SerDe and the filter hooks that read headers before deserializing are in [Kafka Integration](../advanced/kafka-integration.md#enrichment).

## `[DataCacheFromTimestamp]`

Targets a settable `long`, `DateTime` or `DateTimeOffset` property (nullable variants included); no parameters. The same generated enricher sets it from the Kafka message timestamp (Unix milliseconds for `long`, UTC for the date types) on every consumed message. It is consume-only: `Derich` writes no header for it. `AuditEvent.ReceivedAtUnixMs` in the model above is one.

## `[DataCacheFrom<TSource>]`

Targets the class. Constructors `()` and `(string sourceHash)`; named `Exclude` (property names to skip), `Only` (copy just these), `CopyAttributes` (default `true`). It is not a source-generator input but an analyzer plus code fix: with no hash the IDE reports `DATACACHE001` (Info) and offers "Generate properties from source type", which appends `public {Type} {Name} { get; set; }` for every public instance property of `TSource` not already declared or commented out, copies their attributes when asked, and writes the MD5 hash of the source's property list into the attribute. When the source changes, `DATACACHE002` (Warning) offers to regenerate. Nothing is emitted at build time; the properties are yours to edit afterwards, and `[DataCache]` turns the result into a cache like any other.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/ReferenceModels.cs#datacache-from)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#datacache-from-usage)]

## `[DataCacheJsonSerializables]`

Targets a `partial` `JsonSerializerContext`. The generator recognises the attribute as a target but emits nothing for it today; the context still needs its own `[JsonSerializable]` attributes or the System.Text.Json generator reports CS0534. What you get instead, for every project with a `[DataCache]` type, is `{RootNamespace}.Serialization.DataCachesResolver`, an `IJsonTypeInfoResolver` that answers for `QueryResults<T>` of each cache type and returns `null` for anything else, and `services.ConfigureDataCachesJsonOptions()` when ASP.NET Core is referenced.

[!code-csharp[](../../../../samples/Prague.Samples/Reference/ReferenceModels.cs#json-serializables)]

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#json-resolver)]

## Generated members by example

The `Order` entity carries most of the attributes on this page:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Order.cs#order-entity)]

and this test touches every member the generator produces for it, once:

[!code-csharp[](../../../../samples/Prague.Samples/Reference/Attributes.cs#generated-surface)]

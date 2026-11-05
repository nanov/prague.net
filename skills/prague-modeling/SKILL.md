---
name: prague-modeling
description: "Declare Prague in-memory caches in .NET: a consumer-owned [DataCache] partial copy of a topic's contract, [DataCacheKey], choosing [DataCacheIndex] Unique/Many/Range, key-set indexes, foreign keys for joins, [DataCacheSort], equality and [DataCacheIgnoreEquality], external types, global last-update indexes. Use when adding a cache for a topic or entity, choosing what to index, copying a message contract into a service, or when a [DataCache] class fails to compile. Querying lives in prague-queries, Kafka wiring in prague-kafka."
license: MIT
metadata:
  package: Prague
  docs: https://nanov.github.io/prague.net/docs/articles/core-concepts/index-types.html
---

# Prague: modelling a cache

Prague is an in-process, in-memory cache for .NET (net9.0/net10.0). A `partial` class marked `[DataCache]` gets a source-generated `XxxCache` with index storage and a compile-time-checked query builder. Not a server, not distributed, not SQL, no eviction.

Verified against Prague 0.6.0. When this file and the code disagree, the code wins: read the generated source under `obj/`.

## Packages

- `Prague`: the facade (Core + Attributes + Codegen + Kafka). Add this one.
- Broker-free: `Prague.Core` + `Prague.Attributes` + `Prague.Codegen` (as analyzer).
- `using Prague.Core;` for everything, attributes included. The project needs `<LangVersion>latest</LangVersion>`.

## Own the model: copy the contract

A service that consumes a topic declares its **own** `[DataCache]` copy of the message type. Do not reference the producer's contracts package for the cached type.

- A `[DataCache]` type must be `partial` and compiled in your project; a class from a referenced assembly cannot be cached at all, not even via `[DataCache<T>]`.
- Indexes are the consumer's decision: index what *this* service queries by.
- Hold only the properties this service reads. Fewer properties = less memory per row, and producer changes to fields you do not hold arrive as `Same` (no index write, no after-handler `Update`).

The wire is a MessagePack map keyed by **property name**, with no type name. So the copy:
- may use its own namespace, type name and enum types (enums travel as numbers; keep values aligned);
- may drop properties (skipped on read) or widen numbers (`int` → `long`);
- must spell every kept property **exactly** as the producer does: a renamed property silently reads as default, no error.

Test each copied property by deserializing a producer-shaped message with `PragueMessagePack.Options` (namespace `Prague.Kafka`; see `prague-testing`). Only the topic's owner produces to it: a message produced from a partial copy lacks the dropped fields and, on a compacted topic, replaces the full record for every consumer.

## Declaring

Hard rules (compile errors or silent failures):
- The class is `partial`.
- Exactly one property carries `[DataCacheKey]`; `TKey : notnull, IEquatable<TKey>`.
- Implement no interface by hand (no `IDataCacheItem`, no `GetKey()`, no `Equals`): the generator adds `CacheEquals`, `CacheGetHashCode`, `Clone`.
- The wrapper is `{ClassName}Cache`; `[DataCache("OrdersCache")]` renames it.
- A stored instance belongs to the cache: see [Changing a cached row](#changing-a-cached-row).

```csharp
[DataCache]
[DataCacheTopic("orders.[v:env]")]
[DataCacheSort("ByCreatedAt", "CreatedAtUnixMs:-1", "OrderId:1")] // OrderCache.ByCreatedAtComparer
public partial class Order {
	[DataCacheKey]
	public required string OrderId { get; init; }

	[DataCacheForeignKey<Customer>(DataCacheJoinType.ManyToOne)]   // join + index on CustomerId
	public required int CustomerId { get; init; }

	[DataCacheIndex(DataCacheIndexType.Range)]                     // sorted: Gte / Lt / ...
	public required long CreatedAtUnixMs { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]                      // 1:N
	public required OrderStatus Status { get; init; }

	[DataCacheHasValueIndex]                                       // WithSettledAtUnixMs() = non-null
	public long? SettledAtUnixMs { get; init; }
}
```

## Choosing indexes

Index the properties your queries narrow by; everything else is filtered with `.Where` on survivors. Each index costs memory and a write per change.

| Query shape | Declare | Verb |
|---|---|---|
| exact match, one row per value (`InvoiceNumber`) | `[DataCacheIndex(DataCacheIndexType.Unique)]` | `WithInvoiceNumber(x)` |
| exact match, many rows per value (`Status`, `CustomerId`) | `[DataCacheIndex]` (default `Many`); `(Many, capacity)` hints bucket size | `WithStatus(x)`, several values = OR |
| range or ordered walk (timestamps, prices); type is `IComparable<T>` | `[DataCacheIndex(DataCacheIndexType.Range)]` | `WithCreatedAtUnixMs(q => q.Gte(a).Lt(b))` |
| "has a value" / "is true" | `[DataCacheHasValueIndex]` | `WithXxx()` |
| "is null" / "is false" | `[DataCacheHasNotValueIndex]` (separate set) | `WithoutXxx()` |
| fixed predicate `Prop op constant` | `[DataCacheValueIndex(CompareOp.GreaterThan, 0, IndexName = "InStock")]` / `[DataCacheNoValueIndex(...)]` | `WithInStock()` / `WithoutInStock()` |
| a join from this cache | `[DataCacheForeignKey<T>(...)]` on the FK property | `JoinWith{T}()` |

A Range index needs a non-nullable comparable type: `[DataCacheIndex(Range)]` on `long?` is CACHE002. Index a computed, non-serialized key instead, with the sentinel below every real value:

```csharp
public long? DeliveredAtUnixMs { get; init; }

[IgnoreMember]                                                  // MessagePack: not on the wire
[DataCacheIndex(DataCacheIndexType.Range)]
public long DeliveredKey => DeliveredAtUnixMs ?? long.MinValue; // undelivered rows sort below any real window
```

Every query on such a key opens above the sentinel so the null rows stay out: `q => q.Gt(long.MinValue)` for "has a value", `q.Gte(Math.Max(from, long.MinValue + 1))` for a caller-supplied lower bound (`Gte(long.MinValue)` would count every undelivered row). `[DataCacheHasValueIndex]` on the nullable property is the cheaper lane when the query only asks "is set".

A key-set index (the last three rows) stores keys only and is cheaper than a `Many` index for a fixed predicate. `Symmetric = true` on `Unique`/`Many` adds a reverse map for left-index-driven joins; `ManyToOne` foreign keys get it automatically.

## Foreign keys and joins

Declare `[DataCacheForeignKey<TTarget>(DataCacheJoinType)]` on the property that holds the target's key. The FK is the index for that property: an explicit `[DataCacheIndex]` beside it is CACHE042 (`Many` without `Symmetric = true`; a warning, an error under `TreatWarningsAsErrors`) or CACHE041 (`Unique`/`Range`). To range-query the same value, index a separate property. Prefer `ManyToOne` (the holder's view; `OneToMany` is the legacy target's view).

| Declaration | Generated |
|---|---|
| `OneToOne` on a scalar FK | both caches: `JoinWith{Other}()`, slot `TRight?` |
| `ManyToOne` on a scalar FK | holder: `JoinWith{Target}()`, slot `TRight?`; target: `JoinWith{Holder}()`, slot `QueryResults<THolder>` |
| `ManyToOne` on `List<TKey>` | both directions, slot `QueryResults<TRight>` |

Using joins: see `prague-queries`.

## Other attributes

| Attribute | Effect |
|---|---|
| `[DataCacheSort("Name", "Prop:1", "Other:-1")]` | Class-level, repeatable. Emits `static readonly IComparer<T> NameComparer` (a sealed class; `string` columns compare ordinally); `:1` asc, `:-1` desc. End with the key so the order is total. The default comparer for a sort that is a property of the entity; a sort that belongs to one endpoint, or a 10k+/s hot path, gets a struct comparer (see `prague-queries`). |
| `[DataCacheIgnoreEquality]` | Excluded from `CacheEquals`: for audit fields or timestamps that change on every emit without being a state change. `Clone()` still copies it. |
| `[DataCacheGlobalLastUpdateIndex<TIndex>]` | Group key of a cross-cache last-update watermark. `TIndex` is `public partial class X : IDataCacheGlobalLastUpdateIndex<TGroupKey>;` (the generator completes it). |
| `[DataCacheTopic("orders.[v:env]")]` | Kafka topic template (default `Cache.<ClassName>`); see `prague-kafka`. |
| `[DataCacheFromTimestamp]` | `long` property filled from the Kafka message timestamp. |
| `[DataCacheHeader("tenant-id")]` | Property filled from a Kafka header. |
| `[DataCacheStringIntern(false)]` | Keeps a string out of the intern pool (ids, free text; interning is on by default). The property is `partial` (CACHE053 otherwise): the generator supplies its implementing part. |
| `[DataCacheJsonSerializables]` | On a partial `JsonSerializerContext`: registers `QueryResults<T>` for System.Text.Json source generation. |

## Changing a cached row

Every instance the cache holds or hands out is the cache's own: query rows, `TryGet` results, after-handler values, and any instance once you have passed it to `AddOrUpdate`. To change a row: **clone, change the clone, write the clone.**

```csharp
if (tickets.Cache.TryGet(id, out var current)) {
	var next = current.Clone();          // generated, deep: nested lists and objects are copied
	next.State = TicketState.Closed;
	tickets.AddOrUpdateAndProduce(next);  // or AddOrUpdate(next) for a local-only cache
}                                         // from here on, next is resident: leave it alone
```

A resident instance changed in place equals itself, so writing it back is `Same`. The index still files the row under the old value (`WithState(Open)` returns a row whose `State` is `Closed`, `WithState(Closed)` misses it), after-handlers get no `Update`, and `AddOrUpdateAndProduce` produces nothing, so other replicas never see the change. Changing it without writing back does the same damage.

With `init` setters the compiler enforces the rule; build the next value with an object initializer from the current one. `ExecuteCloned()` / `ExecutePooledCloned()` return copies that can be changed and written back.

## Types you cannot annotate

For a generated class you own but cannot edit (protobuf, OpenAPI), put `[DataCache<TExternal>(nameof(TExternal.Id))]` on a `partial` cache class and declare indexes there with `[DataCacheIndex("PropertyName", DataCacheIndexType.Many)]`. `TExternal` must be `partial` and in your compilation. At class level only `[DataCacheIndex]`, `[DataCacheIgnoreEquality]` and `[DataCacheTopic]` are read; the property-name forms of the key-set attributes compile but produce nothing, and foreign keys, sorts and global indexes have no class-level form.

## What the generator emits

For `Order`: `OrderCache : IDataCache<OrderCache, string, Order>` with `new OrderCache()`, `.Cache` (raw `InMemoryDataCache<string, Order>`), one public `{Prop}Index` field per index, `AddOrUpdate(doc[, timestampMs][, out old])`, `Remove(key)`, `Query()`, `Prepare()`, `{Name}Comparer`, `TopicNameTemplate`, and `AddJoinedCaches(...)` when the type has foreign keys. `AddOrUpdate` compares with `CacheEquals`: an equal value swaps nothing and the `out` overload returns false.

## Common mistakes

| Wrong | Right |
|---|---|
| `[DataCache]` class without `partial` | add `partial` |
| `[DataCache]` on (or `[DataCache<T>]` over) a type from a contracts package | copy the contract into this project |
| a copied property renamed to "fix" the producer's naming | keep the wire name exactly |
| implementing `IDataCacheItem` / `GetKey()` / `Equals` | remove; `[DataCacheKey]` is enough |
| indexing every property | index what queries narrow by; `.Where` the rest |
| `Many` index for a fixed predicate (`Stock > 0`) | `[DataCacheValueIndex(...)]` key-set index |
| `Range` on a `long?` (CACHE002) | computed `long` key with a `long.MinValue` sentinel; queries open with `Gt(long.MinValue)` |
| `[DataCacheIndex]` next to a `ManyToOne` FK (CACHE041/042) | the FK alone; a separate property for a range |
| `[DataCacheSort]` without a final key column | end with the key for a total order |
| editing generated files under `obj/` | change the attributes |
| changing a row from `TryGet` or a query, then `AddOrUpdate(row)` | `var next = row.Clone();` change `next`, `AddOrUpdate(next)` |
| changing an instance after passing it to `AddOrUpdate` | it is resident now: clone again |

Read next: [own your model](https://nanov.github.io/prague.net/docs/articles/how-to/own-your-model.html), [index types](https://nanov.github.io/prague.net/docs/articles/core-concepts/index-types.html), [conditional updates](https://nanov.github.io/prague.net/docs/articles/core-concepts/conditional-updates.html), [attributes reference](https://nanov.github.io/prague.net/docs/articles/reference/attributes.html), [generator diagnostics](https://nanov.github.io/prague.net/docs/articles/reference/diagnostics.html).

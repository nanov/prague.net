---
name: prague-modeling
description: "Declaring Prague caches: [DataCache] partial classes, [DataCacheKey], Unique/Many/Range and key-set indexes, [DataCacheForeignKey], Clone before write. Use when adding a cache type, copying a producer's message type, choosing indexes, or on a CACHE0xx error. Not for queries (use prague-queries)."
license: MIT
metadata:
  applies-to: "Prague >= 0.9.0, <= 0.10.0"
  verified: 2026-11-19
  docs: https://nanov.github.io/prague.net/docs/articles/core-concepts/index-types.html
---

# Prague: modelling a cache

Prague is an in-process, in-memory cache for .NET (net9.0/net10.0/net11.0). A `partial` class marked `[DataCache]` gets a source-generated `XxxCache` with index storage and a compile-time-checked query builder. Not a server, not distributed, not SQL, no eviction. When this file and the code disagree, the code wins: set `<PragueCacheShowGeneratedFiles>true</PragueCacheShowGeneratedFiles>` and read the generated files in `Generated/`.

## Packages

- `Prague`: the facade (Core + Attributes + Codegen + Kafka). Add this one.
- Broker-free: `Prague.Core` + `Prague.Attributes` + `Prague.Codegen` (as analyzer).
- `using Prague.Core;` for everything, attributes included. The project needs `<LangVersion>latest</LangVersion>`.

## Where the cache type comes from

The generator completes only `partial` classes compiled in the same project:

- A package that ships a `[DataCache]` class built with `Prague.Codegen` ships its `XxxCache` too. Reference it and use the cache as it is; its indexes are the package's.
- A plain contract class from a referenced assembly cannot be cached, not even through `[DataCache<T>]`. Declare a `[DataCache]` copy of it in this project, indexed for what this service queries by.

A copy holds only the properties this service reads. Fewer properties mean less memory per row, and producer changes to fields you do not hold arrive as `Same` (no index write, no after-handler `Update`). With the source type referenced, `[DataCacheFrom<TSource>]` on the copy offers an IDE fix that writes the properties, and `DATACACHE002` warns when the source changes.

The wire is a MessagePack map keyed by **property name**, with no type name. So the copy:
- may use its own namespace, type name and enum types (enums travel as numbers; keep values aligned);
- may drop properties (skipped on read) or widen numbers (`int` → `long`);
- must spell every kept property **exactly** as the producer does: a renamed property silently reads as default, no error.

Test each copied property by deserializing a producer-shaped message with `PragueMessagePack.Options` (see `prague-testing`). Only the topic's owner produces to it: a message produced from a partial copy lacks the dropped fields and, on a compacted topic, replaces the full record for every consumer.

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

	public required decimal Total { get; init; }                   // not indexed: .Where(o => o.Total > x)
}
```

## Choosing indexes

Index the properties your queries narrow by; everything else is filtered with `.Where` on survivors. Each index costs memory and a write per change.

| Query shape | Declare | Verb |
|---|---|---|
| exact match, one row per value (`InvoiceNumber`) | `[DataCacheIndex(DataCacheIndexType.Unique)]` | `WithInvoiceNumber(x)` |
| exact match, many rows per value (`Status`, `CustomerId`) | `[DataCacheIndex]` (default `Many`) | `WithStatus(x)`; an array or span of values = OR |
| range or ordered walk (timestamps, prices); type is `IComparable<T>` | `[DataCacheIndex(DataCacheIndexType.Range)]` | `WithCreatedAtUnixMs(q => q.Gte(a).Lt(b))` |
| "has a value" / "is true" | `[DataCacheHasValueIndex]` | `WithXxx()` |
| "is null" / "is false" | `[DataCacheHasNotValueIndex]` (its own set) | `WithoutXxx()` |
| fixed predicate `Prop op constant` | `[DataCacheValueIndex(CompareOp.GreaterThan, 0, IndexName = "InStock")]` | `WithInStock()` |
| its complement | `[DataCacheNoValueIndex(CompareOp.GreaterThan, 0, IndexName = "OutOfStock")]` (its own set) | `WithOutOfStock()`, never `WithoutInStock()` |
| a join from this cache | `[DataCacheForeignKey<T>(...)]` on the FK property | `JoinWith{T}()` |

A Range index needs a non-nullable comparable type: `[DataCacheIndex(Range)]` on `long?` is CACHE002. Index a computed, non-serialized key instead, with the sentinel below every real value:

```csharp
public long? DeliveredAtUnixMs { get; init; }

[IgnoreMember]                                                  // MessagePack: not on the wire
[DataCacheIndex(DataCacheIndexType.Range)]
public long DeliveredKey => DeliveredAtUnixMs ?? long.MinValue; // undelivered rows sort below any real window
```

Every query on such a key opens above the sentinel so the null rows stay out: `q => q.Gt(long.MinValue)` for "has a value", `q.Gte(Math.Max(from, long.MinValue + 1))` for a caller-supplied lower bound (`Gte(long.MinValue)` would count every undelivered row). `[DataCacheHasValueIndex]` on the nullable property is the cheaper lane when the query only asks "is set".

A key-set index (the has-value, value and no-value rows) stores keys only and is cheaper than a `Many` index for a fixed predicate. Without `IndexName` the verb spells out the predicate (`WithStockGreaterThan0()`, `WithStockLessThanOrEqual0()`); a value and a no-value index given the same `IndexName` do not compile. `Symmetric = true` on `Unique`/`Many` adds a reverse map for left-index-driven joins; `ManyToOne` foreign keys get it automatically.

## Foreign keys and joins

Declare `[DataCacheForeignKey<TTarget>(DataCacheJoinType)]` on the property that holds the target's key. The FK is the index for that property: an explicit `[DataCacheIndex]` beside it is CACHE042 (`Many` without `Symmetric = true`; a warning, an error under `TreatWarningsAsErrors`) or CACHE041 (`Unique`/`Range`). To range-query the same value, index a separate property. Prefer `ManyToOne` (the holder's view; `OneToMany` is the legacy target's view).

| Declaration | Generated |
|---|---|
| `OneToOne` on a scalar FK | both caches: `JoinWith{Other}()`, slot `TRight?` |
| `ManyToOne` on a scalar FK | holder: `JoinWith{Target}()`, slot `TRight?`; target: `JoinWith{Holder}()`, slot `QueryResults<THolder>` |
| `ManyToOne` on `List<TKey>` | both directions, slot `QueryResults<TRight>` |

`OneToOne` is a promise about the data: each target row has at most one holder. If several holders can carry the same target key (games of one sport type, orders of one customer), the FK is `ManyToOne`. A `OneToOne` over many-to-one data builds and writes without error but keeps one holder per target key in its index: `JoinWith{Holder}()` returns one holder, and `With{Fk}(x)` and an inner `JoinWith{Target}()` return only the last holder written for that key.

Using joins: see `prague-queries`.

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

## Stamping a write (since 0.10.0)

To derive fields from the resident row at write time (a revision, a changed-at time), pass an on-write callback instead of `TryGet` + write:

```csharp
invoices.AddOrUpdate(invoice, now, static (next, resident, ts) => {
	next.Revision = (resident?.Revision ?? 0) + 1;   // resident is null on an add
	next.ChangedAtUnixMs = ts;
});
```

- It runs at most once, under the store lock for the key, after `CacheEquals` decided to write and before the row is stored. It does not run on `Same`.
- Change only `next`. Never change `resident` and never the key (that throws, nothing is written). Leave the cache alone inside it.
- Stamped properties are `[DataCacheIgnoreEquality]` with a `set` accessor. An `init` property cannot be assigned from the callback; a stamped property inside equality makes every write an `Update`.
- Keep the lambda `static` and pass values through the state argument (`now` above).

## Other attributes

[references/attributes.md](references/attributes.md) covers `[DataCacheSort]`, `[DataCacheIgnoreEquality]`, `[DataCacheTopic]`, `[DataCacheFromTimestamp]`, `[DataCacheHeader]`, `[DataCacheStringIntern(false)]`, `[DataCacheGlobalLastUpdateIndex<T>]`, `[DataCache<TExternal>]` for generated types you cannot annotate, and what the generator emits. Read it before using any of them; two fail in ways the compiler explains badly:
- `[DataCacheFromTimestamp]` and `[DataCacheHeader]` properties need a `set` accessor. With `init`, the build fails inside the generated `EnrichExtensions` (CS8852).
- A field that changes on every emit without being a state change (an audit timestamp) turns every re-emit into an `Update` unless it carries `[DataCacheIgnoreEquality]`.

## Common mistakes

| Wrong | Right |
|---|---|
| `[DataCache]` class without `partial` | add `partial` |
| `[DataCache]` on (or `[DataCache<T>]` over) a type from a referenced assembly | reference a package that ships the cache, or declare a copy here |
| a copied property renamed to "fix" the producer's naming | keep the wire name exactly |
| implementing `IDataCacheItem` / `GetKey()` / `Equals` | remove; `[DataCacheKey]` is enough |
| indexing every property | index what queries narrow by; `.Where` the rest |
| `Many` index for a fixed predicate (`Stock > 0`) | `[DataCacheValueIndex(...)]` key-set index |
| `WithoutInStock()` after a `[DataCacheValueIndex]` | a `[DataCacheNoValueIndex]`, then `With{ItsIndexName}()` |
| `Range` on a `long?` (CACHE002) | computed `long` key with a `long.MinValue` sentinel; queries open with `Gt(long.MinValue)` |
| `OneToOne` FK where several rows can hold the same target key | `ManyToOne` |
| `[DataCacheIndex]` next to a `ManyToOne` FK (CACHE041/042) | the FK alone; a separate property for a range |
| `[DataCacheSort]` without a final key column | end with the key for a total order |
| editing generated files | change the attributes |
| changing a row from `TryGet` or a query, then `AddOrUpdate(row)` | `var next = row.Clone();` change `next`, `AddOrUpdate(next)` |
| changing an instance after passing it to `AddOrUpdate` | it is resident now: clone again |

Read next: [own your model](https://nanov.github.io/prague.net/docs/articles/how-to/own-your-model.html), [index types](https://nanov.github.io/prague.net/docs/articles/core-concepts/index-types.html), [conditional updates](https://nanov.github.io/prague.net/docs/articles/core-concepts/conditional-updates.html), [attributes reference](https://nanov.github.io/prague.net/docs/articles/reference/attributes.html), [generator diagnostics](https://nanov.github.io/prague.net/docs/articles/reference/diagnostics.html).

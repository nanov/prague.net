---
title: Global Last Update Index
---

# Global Last Update Index

The global last-update index tracks the timestamp of the most recent change for every entity in a cache, grouped by a property you nominate. It powers incremental sync APIs: "give me everything that changed since `t`."

## Why it's separate

Prague's normal indices are scoped to one cache. The global index sits **above** caches: one `IDataCacheGlobalLastUpdateIndex<TGroupKey>` instance can collect timestamps from many caches whose entities share a `TGroupKey`. A typical use case is a catalog-feed pipeline where `Listing`, `Offer`, and `OrderLine` caches all hold a `ListingId`; one global index keyed by `ListingId` becomes the single source of truth for "what changed in listing 42 since last sync."

## Declaring the index

Define a `partial` class that implements `IDataCacheGlobalLastUpdateIndex<TGroupKey>`. The interface is implemented for you by the generator:

```csharp
public partial class ListingActivityIndex : IDataCacheGlobalLastUpdateIndex<long>;
```

Then mark the group-key property on each contributing cache with `[DataCacheGlobalLastUpdateIndex<TIndex>]`:

```csharp
[DataCache]
public partial class Offer
{
    [DataCacheKey] public required string OfferId { get; init; }

    [DataCacheGlobalLastUpdateIndex<ListingActivityIndex>]
    public required long ListingId { get; init; }
}

[DataCache]
public partial class OrderLine
{
    [DataCacheKey] public required string OrderLineId { get; init; }

    [DataCacheGlobalLastUpdateIndex<ListingActivityIndex>]
    public required long ListingId { get; init; }
}
```

Both caches now report into `ListingActivityIndex` on every write.

## Timestamp source

By default the index uses the `timestamp` argument passed to `AddOrUpdate(value, timestamp)` (which, for Kafka-backed caches, is the message timestamp). If you'd rather drive it from an entity property:

```csharp
[DataCacheGlobalLastUpdateIndex<ListingActivityIndex>(nameof(UpdatedAtUnixMs))]
public required long ListingId { get; init; }

public required long UpdatedAtUnixMs { get; init; }
```

The property type must be `long` (Unix milliseconds) or `DateTime`/`DateTimeOffset`.

## Querying

`ListingActivityIndex` is registered as a singleton in DI:

```csharp
var index = serviceProvider.GetRequiredService<ListingActivityIndex>();
```

The index has a small public surface:

```csharp
public interface IDataCacheGlobalLastUpdateIndex<TGroupKey>
{
    LastUpdatedIndex<TGroupKey> Index { get; }
    bool TryGetMin(out long timestampMs, out TGroupKey key);
    bool TryGetMax(out long timestampMs, out TGroupKey key);
    int GetEntitiesCount(TGroupKey key);
}
```

To enumerate entities that changed after `t`, use the `UpdatedAfter` operator on a cache query:

```csharp
using var changedOffers = offerCache.Query()
    .UpdatedAfter(lastSyncUnixMs)
    .ExecutePooled();

using var changedOrderLines = orderLineCache.Query()
    .UpdatedAfter(lastSyncUnixMs, out var maxObservedUnixMs)
    .ExecutePooled();

await CommitSyncPoint(maxObservedUnixMs);
```

The `out long max` overload returns the highest observed timestamp in the result set so you can advance your sync watermark atomically. `DateTimeOffset` overloads exist for both shapes.

## Watermark semantics

- `UpdatedAfter(t)` returns entities with timestamp **strictly greater than `t`**.
- The cache's snapshot is read once at the start of `Execute*`; later mutations are not visible to the same enumeration.
- The index is not transactional across caches: if you query `offerCache` and `orderLineCache` separately, the watermarks you observe may differ by milliseconds. Take the **minimum** of the per-cache watermarks as your safe sync point.

## When to use it

The global index has a per-write cost (an extra B-Tree node insertion per contributing cache). It pays off when:

- You serve incremental APIs (`/api/changes?since=...`).
- You need to forward changes to a downstream system on a polling cadence.
- You feed an external search index that wants delta updates.

For "tail the topic in real time" use-cases, `ICacheAfterHandler` is the cheaper hook.

## Next

- [Performance Tuning](performance-tuning.md).

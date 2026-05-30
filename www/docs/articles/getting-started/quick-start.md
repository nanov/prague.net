---
title: Quick Start
---

# Quick Start

A working cache in three steps.

## 1. Define a POCO

Annotate any data class with `[DataCache]`. Mark its primary key with `[DataCacheKey]` and declare any indices you want to query by.

```csharp
using Prague.Core;

[DataCache]
public partial class Order
{
    [DataCacheKey]
    public required string OrderId { get; init; }

    [DataCacheIndex(DataCacheIndexType.Many)]
    public required int CustomerId { get; init; }

    [DataCacheIndex(DataCacheIndexType.Range)]
    public required long CreatedAtUnixMs { get; init; }

    public required decimal Total { get; init; }
}
```

The class must be `partial`. The source generator emits `OrderCache : IDataCache<OrderCache, string, Order>` alongside it, containing the index storage and a fluent `Query()` method.

## 2. Register the cache

For in-process scenarios with no Kafka:

```csharp
var registryBuilder = new DataCacheRegistryBuilder();
registryBuilder.Register<OrderCache>();
var registry = registryBuilder.Build();
var cache = registry.GetCache<OrderCache>();
```

For Kafka-backed caches, register through DI (see [Kafka integration](../advanced/kafka-integration.md)):

```csharp
services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092",
    builder => builder.AddCache<OrderCache, string, Order>());

var cache = serviceProvider.GetRequiredService<OrderCache>();
```

## 3. Write and query

```csharp
cache.Cache.AddOrUpdate(new Order {
    OrderId = "ORD-001",
    CustomerId = 42,
    CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    Total = 99.99m,
});

using var orders = cache.Query()
    .WithCustomerId(42)
    .WithCreatedAtUnixMs(q => q.Gte(yesterdayUnixMs))
    .ExecutePooled();

foreach (var order in orders)
    Console.WriteLine($"{order.OrderId} {order.Total}");
```

Notes:

- `WithCustomerId` and `WithCreatedAtUnixMs` are generated from the `[DataCacheIndex]` annotations — IntelliSense lists exactly the indices you declared.
- `ExecutePooled()` returns a disposable `QueryResults<Order>` backed by `ArrayPool`. Always `using` it.
- Index hits are intersected; if either narrows to zero, the other is skipped.

## Next

- [Index Types](../core-concepts/index-types.md) — the full menu of `[DataCacheIndex]` variants plus `[DataCacheValueIndex]`, `[DataCacheHasValueIndex]`, and friends.
- [Query Engine](../core-concepts/query-engine.md) — pooled vs allocating execution, `UseIndex`, sort, and intersection rules.
- [Joins](../core-concepts/joins.md) — `JoinWith{Other}` and `InnerJoinWith{Other}` for cross-cache lookups.

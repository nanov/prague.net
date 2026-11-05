---
name: prague-testing
description: "Test .NET code that uses Prague caches: broker-free unit tests with new XxxCache() or DataCacheRegistryBuilder seeded via AddOrUpdate, driving ICacheAfterHandler by hand, asserting on pooled QueryResults, frozen-vs-eager query tests, building the AddKafkaCaches container without a broker, verifying a copied contract against producer bytes, and Testcontainers integration tests. Use when writing or fixing tests for code that reads, filters or reacts to a Prague cache."
license: MIT
metadata:
  package: Prague
  docs: https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html
---

# Prague: testing

Almost everything that touches a Prague cache is testable without Kafka: the consumer only calls `AddOrUpdate` / `Remove` on the same cache your code reads. Verified against Prague 0.6.0. Examples use NUnit; the patterns are framework-neutral.

## Design for the seam

Code under test takes the generated cache types (`OrderCache`, `CustomerCache`) by constructor and knows nothing about where the data comes from. In production `AddKafkaCaches` fills them; in a test you do.

## What the suite proves

The suite is complete when every item below that applies has a test; each names the failure it exists to catch:
- **Every sorted query**: fixtures inserted in an order unlike the expected one (`Random.Shared.Shuffle(fixtures)`), the exact sequence asserted. Fixtures inserted in key order pass on encounter order alone and the sort is never exercised.
- **Every range query**: a row on each edge (`Gte` keeps it, `Lt` drops it), an empty window, and for a sentinel-keyed index (`?? long.MinValue`, see `prague-modeling`) a null row counted by no query, including one whose lower bound is `long.MinValue`.
- **Every frozen query**: the differential against eager below.
- **Every after-handler**: the five `UpdateType`s in a realistic sequence, and, when another service reads its state, the DI identity assertion below.
- **Every copied contract**: each property through the wire test below.
- **Every paged endpoint**: consecutive pages partition the result, `TotalCount` matches the whole.

## A stand-alone cache

```csharp
var orders = new OrderCache();                          // no registry, no Kafka, no DI
orders.AddOrUpdate(order);                              // the same call the consumer makes per message
orders.AddOrUpdate(other, timestamp: 1_700_000_000_000);   // when a last-update index matters
```

## Several caches, joins, global indexes

```csharp
var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // pulls in FK-reachable caches
var orders = registry.GetCache<OrderCache>();
var customers = registry.GetCache<CustomerCache>();
```

Two hand-built caches can be joined after one call: `orders.AddJoinedCaches(customers, ...)`.

## After-handlers

Call `Handle` with the sequence a topic would produce. Include `Same` (re-emit) and `Filtered` (`key` = default, both values null) so the handler is proven to ignore them:

```csharp
var projector = new OrderProjector();
await projector.Handle(UpdateType.Add, "ORD-001", pending, null);
await projector.Handle(UpdateType.Same, "ORD-001", pending, null);
await projector.Handle(UpdateType.Update, "ORD-001", paid, pending);
await projector.Handle(UpdateType.Delete, "ORD-001", null, paid);
await projector.Handle(UpdateType.Filtered, default!, null, null);
```

To test which `UpdateType` a write produces, use the cache: `AddOrUpdate(value, out var previous)` returns false for `Same`.

## Asserting on results

- `using var rows = ...ExecutePooled();` in tests too: `QueryResults<T>` is an `IReadOnlyList<T>`, so collection constraints work directly.
- Assert the **set** (`Is.EquivalentTo`) for an unsorted query: its order is not a contract, and a test that passes on insertion order breaks when the plan changes. Assert the **sequence** (`Is.EqualTo`) only for a sort with a total comparer, over shuffled fixtures.
- `rows.Map(f)` consumes `rows`; dispose the mapped result.

## Frozen against eager

The eager query is the specification of a frozen one. Compare over several argument values, including one with no matches, and `Count(in args)` beside the rows:

```csharp
var frozen = orders.Prepare<int>().WithCustomerId(static c => c).WithStatus(OrderStatus.Paid).BuildFrozen();
foreach (var customerId in new[] { 0, 1, 2, 99 }) {
	using var expected = orders.Query().WithCustomerId(customerId).WithStatus(OrderStatus.Paid).ExecutePooled();
	using var actual = frozen.ExecutePooled(customerId);
	Assert.That(actual.Select(o => o.OrderId), Is.EquivalentTo(expected.Select(o => o.OrderId)));
	Assert.That(actual.TotalCount, Is.EqualTo(expected.TotalCount));
	Assert.That(frozen.Count(customerId), Is.EqualTo(expected.TotalCount));
}
```

For a sequence assertion, build the frozen side with `new FrozenOptions { PreserveEagerOrder = true }` or sort with a total comparer.

## The real container, not started

`AddKafkaCaches` contacts no broker until its hosted service starts. Build the provider, resolve the cache singletons, seed them, and test the services the app would inject:

```csharp
services.AddSingleton<OpenTotals>();                     // read by an endpoint, fed by the cache
services.AddKafkaCaches(o => o.BootstrapServers = "unreachable:9092",
	b => b.AddCache<OrderCache, string, Order>().WithAfterHandler(sp => sp.GetRequiredService<OpenTotals>()));
services.AddSingleton(sp => sp.GetRequiredService<IDataCacheRegistry>().GetCache<CustomerCache>());   // FK-pulled caches are not DI services
using var provider = services.BuildServiceProvider();    // build and stop: resolving or starting IHostedService needs a broker
provider.GetRequiredService<OrderCache>().AddOrUpdate(order);
Assert.That(provider.GetRequiredService<ICacheAfterHandler<string, Order>>(), Is.SameAs(provider.GetRequiredService<OpenTotals>()));
```

The last line is the DI identity assertion: the handler the cache calls is the instance readers inject (`WithAfterHandler<T>()` beside `AddSingleton<T>()` makes two, see `prague-handlers`). The container needs `IConfiguration` and logging registered (`services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); services.AddLogging();`).

## A copied contract against producer bytes

When the cache type is your copy of another team's contract (see `prague-modeling`), test every copied property: serialize a producer-shaped object with `PragueMessagePack.Options` and deserialize your type. A misspelled property reads as default without error, so assert each value.

```csharp
using MessagePack;   // MessagePackSerializer
using Prague.Kafka;  // PragueMessagePack

var bytes = MessagePackSerializer.Serialize(producerShaped, PragueMessagePack.Options);
var parcel = MessagePackSerializer.Deserialize<Parcel>(bytes, PragueMessagePack.Options);
Assert.That(parcel.Carrier, Is.EqualTo("DHL"));
```

## When a broker is needed

Deserialization end to end, topic semantics, filters on real headers, initial load and health need Kafka. There is no in-memory broker. Use Testcontainers and:
- give each consumer under test its own `group.id` (the default is shared by every consumer in the process, so two providers would split partitions);
- produce fixtures with a plain Kafka producer: a process drops messages carrying its own `X-Producer-Id`, so it never consumes what Prague produced in the same process;
- create cache topics compacted, with one partition;
- `await host.DataCachesLoadCompletion()` before asserting.

## Common mistakes

| Wrong | Right |
|---|---|
| asserting row order of an unsorted query | `Is.EquivalentTo`, or sort with a total comparer |
| sorted-query fixtures inserted in the expected order | shuffle them first |
| `Is.TypeOf<T>` as the only check on a handler whose state is read elsewhere | `Is.SameAs` the injected instance |
| `ExecutePooled()` without `using` in a test | `using var rows = ...` |
| starting the hosted service in a unit test | build the provider and stop there |
| resolving an FK-pulled cache from DI | register a factory from `IDataCacheRegistry` |
| Kafka test reading what the same process produced via Prague | produce fixtures with a plain producer |
| two consumers in one test run sharing a group | one `group.id` per consumer |

Read next: [test without a broker](https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html), [own your model](https://nanov.github.io/prague.net/docs/articles/how-to/own-your-model.html), [Kafka integration: testing](https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html#testing).

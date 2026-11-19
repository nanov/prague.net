---
name: prague-testing
description: "Testing code that uses Prague caches: seeding caches and the registry without a broker, after-handlers called by hand, frozen-vs-eager checks, copied-contract wire tests, Testcontainers. Use when writing or fixing tests for Prague code. Not for production wiring (use prague-kafka)."
license: MIT
metadata:
  applies-to: "Prague >= 0.9.0"
  verified: 2026-11-19
  docs: https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html
---

# Prague: testing

Two layers, two kinds of test. Examples use NUnit; the patterns are framework-neutral.

- **Your code, no broker:** queries, after-handler logic called by hand, the DI wiring, the wire format of a copied contract.
- **The pipeline, real broker:** filters, `treatAsDelete`, tombstones, which `UpdateType` a message produces, handlers skipped during the initial load. The consumer decides all of these. A write through `AddOrUpdate` runs no filter and no after-handler, so no broker-free test reaches them.

## Design for the seam

Code under test takes the generated cache types (`OrderCache`, `CustomerCache`) by constructor and knows nothing about where the data comes from. In production `AddKafkaCaches` fills them; in a test you do.

## What the suite proves

The suite is complete when every item below that applies has a test; each names the failure it exists to catch:
- **Every sorted query**: fixtures inserted in an order unlike the expected one (`Random.Shared.Shuffle(fixtures)`), the exact sequence asserted. Fixtures inserted in key order pass on encounter order alone and the sort is never exercised.
- **Every range query**: a row on each edge (`Gte` keeps it, `Lt` drops it), an empty window, and for a sentinel-keyed index (`?? long.MinValue`, see `prague-modeling`) a null row counted by no query, including one whose lower bound is `long.MinValue`.
- **Every frozen query**: the differential against eager below.
- **Every after-handler**: the five `UpdateType`s in a realistic sequence, and, when another service reads its state, the DI identity assertion below.
- **Every filter and `treatAsDelete`**: a broker test that produces an accepted and a rejected message and asserts the cache and the `UpdateType` the handler received.
- **Every copied contract**: each property through the wire test below.
- **Every paged endpoint**: consecutive pages partition the result, `TotalCount` matches the whole.

## A stand-alone cache

```csharp
var orders = new OrderCache();                          // no registry, no Kafka, no DI
orders.AddOrUpdate(order);                              // the same call the consumer makes per message
orders.AddOrUpdate(other, timestamp: 1_700_000_000_000);   // when a last-update index matters
```

## Code that writes through the cache

`AddOrUpdateAndProduce` and `RemoveAndProduce` on a cache built with `new OrderCache()` or a registry make the local write, then throw `InvalidOperationException` ("Producer not configured"): no producer is attached outside `AddKafkaCaches`. Put the write behind a seam the test can replace (an `IOrderWriter` whose production implementation calls `AddOrUpdateAndProduce`, whose test implementation calls `AddOrUpdate`), and test what is produced in a broker test.

## Several caches, joins, global indexes

```csharp
var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // pulls in FK-reachable caches
var orders = registry.GetCache<OrderCache>();
var customers = registry.GetCache<CustomerCache>();
```

Two hand-built caches can be joined after one call: `orders.AddJoinedCaches(customers, ...)`.

## After-handlers

Call `Handle` with the sequence a topic would produce. Include `Same` (re-emit) and `Filtered` (`key` = default, both values null) so the handler is proven to ignore them. For a projection seeded after the load (the `OpenTotals` sample in `prague-handlers`), seed first and then deliver a live `Add` for a key the seed already counted: a delta-adding projection counts it twice.

```csharp
var orders = new OrderCache();
orders.AddOrUpdate(paid);                                    // in the topic before startup
var totals = new OpenTotals();
totals.Seed(orders);
await totals.Handle(UpdateType.Add, "ORD-001", paid, null);        // also seen live: counted once
await totals.Handle(UpdateType.Same, "ORD-001", paid, null);       // re-emit
Assert.That(totals.Get(42), Is.EqualTo(paid.Total));
await totals.Handle(UpdateType.Add, "ORD-002", pending, null);
await totals.Handle(UpdateType.Update, "ORD-001", cancelled, paid);
await totals.Handle(UpdateType.Filtered, default!, null, null);
Assert.That(totals.Get(42), Is.EqualTo(pending.Total));
await totals.Handle(UpdateType.Delete, "ORD-002", null, pending);
Assert.That(totals.Get(42), Is.Zero);
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

`AddKafkaCaches` creates no consumer until the hosted service is resolved. Build the provider, resolve the cache singletons, seed them, and test the services the app would inject. This proves the wiring, not the flow: seeding goes around the consumer, so the handler receives nothing.

```csharp
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());   // AddKafkaCaches binds options from it
services.AddSingleton<OpenTotals>();                     // read by an endpoint, fed by the cache
services.AddKafkaCaches(o => o.BootstrapServers = "unreachable:9092",
	b => b.AddCache<OrderCache, string, Order>().WithAfterHandler(sp => sp.GetRequiredService<OpenTotals>()));
services.AddSingleton(sp => sp.GetRequiredService<IDataCacheRegistry>().GetCache<CustomerCache>());   // FK-pulled caches are not DI services
using var provider = services.BuildServiceProvider();    // build and stop: resolving IHostedService creates the Kafka consumer
provider.GetRequiredService<OrderCache>().AddOrUpdate(order);
Assert.That(provider.GetRequiredService<ICacheAfterHandler<string, Order>>(), Is.SameAs(provider.GetRequiredService<OpenTotals>()));
```

The last line is the DI identity assertion: the handler the cache calls is the instance readers inject (`WithAfterHandler<T>()` beside `AddSingleton<T>()` makes two, see `prague-handlers`).

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

The pipeline, deserialization end to end, initial load and health need Kafka. There is no in-memory broker. Use Testcontainers (Docker); once the container is up, a test takes about a second. Then:
- give each consumer under test its own `group.id` (the default is shared by every consumer in the process, so two providers would split partitions);
- produce fixtures with a plain Kafka producer: a process drops messages carrying its own `X-Producer-Id`, so it never consumes what Prague produced in the same process;
- create cache topics compacted; one partition unless the test is about multi-partition loading;
- `await host.DataCachesLoadCompletion()` before producing live messages;
- poll for the result with a deadline (the live worker applies messages asynchronously), never assert right after producing.

## Common mistakes

| Wrong | Right |
|---|---|
| asserting row order of an unsorted query | `Is.EquivalentTo`, or sort with a total comparer |
| sorted-query fixtures inserted in the expected order | shuffle them first |
| `Is.TypeOf<T>` as the only check on a handler whose state is read elsewhere | `Is.SameAs` the injected instance |
| `ExecutePooled()` without `using` in a test | `using var rows = ...` |
| starting the hosted service in a unit test | build the provider and stop there |
| calling `AddOrUpdateAndProduce` on a stand-alone cache | `AddOrUpdate` behind a seam; produce only in a broker test |
| expecting `AddOrUpdate` on a seeded cache to call the after-handler | call `Handle` by hand; test the flow with a broker |
| a filter or `treatAsDelete` with only unit tests | one broker test per filter |
| resolving an FK-pulled cache from DI | register a factory from `IDataCacheRegistry` |
| Kafka test reading what the same process produced via Prague | produce fixtures with a plain producer |
| two consumers in one test run sharing a group | one `group.id` per consumer |

Read next: [test without a broker](https://nanov.github.io/prague.net/docs/articles/how-to/test-without-a-broker.html), [own your model](https://nanov.github.io/prague.net/docs/articles/how-to/own-your-model.html), [Kafka integration: testing](https://nanov.github.io/prague.net/docs/articles/advanced/kafka-integration.html#testing).

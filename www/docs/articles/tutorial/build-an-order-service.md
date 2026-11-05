---
title: "Tutorial: Build an Order Service"
description: Thirty minutes from annotated POCOs to an HTTP service that queries, joins and pages orders out of Kafka-fed caches, reacts to live shipments and reports health.
---

# Tutorial: Build an Order Service

You will build a small order-lookup service end to end: define the models, register their caches with the Kafka integration, wait for the initial load, query and join, put the hot path behind a frozen query, react to live shipment updates, and expose health checks and an HTTP endpoint. Every code block is a region of [`BuildAnOrderService.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/Tutorial/BuildAnOrderService.cs), which compiles with the repository. Steps that need a broker are plain methods; every other step runs as a test, so `dotnet test samples/Prague.Samples --filter FullyQualifiedName~Tutorial` passes without Kafka.

**Prerequisites:** the packages from [Installation](../getting-started/installation.md), .NET 9 or 10, and the [Quick Start](../getting-started/quick-start.md) if `[DataCache]` is new to you. A broker is needed only for the steps marked *broker*.

## 1. Define the models

The service uses the domain every page of these docs shares: `Order`, `Customer`, `OrderLine`, `Product` and `Shipment`. `Order` is the one that matters most here: it carries the topic template, a foreign key to `Customer`, a `Range` index on `CreatedAtUnixMs` and a named sort.

[!code-csharp[](../../../../samples/Prague.Samples/Models/Order.cs#order-entity)]

The other four are in [`samples/Prague.Samples/Models`](https://github.com/nanov/prague.net/tree/main/samples/Prague.Samples/Models). What you get for each: a generated `XxxCache` with index storage, `AddOrUpdate`, `Query()` and `Prepare()`; `CacheEquals` and `Clone()` on the entity; and, from the foreign keys, `JoinWithCustomer()`, `JoinWithOrderLine()` and `JoinWithShipment()` on `OrderCache`. Nothing to implement by hand.

**What to expect:** after a build, `OrderCache`, `CustomerCache`, `OrderLineCache`, `ShipmentCache` and `ProductCache` exist in your namespace, and `Customer` carries `CustomerActivityIndex`, a [global last-update index](../advanced/global-last-update-index.md) that both `Customer` and `Order` report into.

## 2. Register the caches (*broker*)

`AddKafkaCaches` takes the connection options and a builder that lists which caches to consume. Topic names are templates: `[v:name]` is filled from `Vars`, `[e:NAME]` from an environment variable. `Order` declares its template on the class; the others get theirs here.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-register-caches)]

`ShipmentNotifier` and `CustomerOrdersPage` are written in steps 6 and 7. Registering `OrderCache` also pulls `ProductCache` into the registry (through `OrderLine.ProductId`) so joins resolve; it is not consumed because nothing calls `AddCache` for it, and it is not a DI service either. Only the caches you `AddCache` are injectable.

**What to expect:** building the container contacts no broker, so this part is testable as-is. The test `RegistrationResolvesCachesWithoutABroker` resolves `OrderCache` and asserts `orders.Topic == "orders.prod"`. Consumption starts with the host.

## 3. Wait for the load (*broker*)

The hosted service `AddKafkaCaches` registers replays every topic to its end-of-partition before its `StartAsync` returns, so `app.Run()` does not accept requests on empty caches. If your host starts services concurrently, or you built the provider yourself, await the load explicitly.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-wait-for-load)]

**What to expect:** a log line per cache with its load time and size; the readiness check from step 8 flips to Healthy at the same moment. The replay and the live phase are described in [Kafka Integration](../advanced/kafka-integration.md).

## 4. Seed and query

From here the tutorial runs without a broker: it builds the same registry `AddKafkaCaches` would build and writes a few fixtures with `AddOrUpdate`, which is also what the consumer calls per message.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-seed)]

Each `WithXxx` call is a *lane* on an index; lanes are intersected, and only the survivors see `Where`. `Count()` never materializes rows.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-query-lanes)]

`Or` takes two or more branches, each a lane set of its own, and unions them before the intersection with the outer lanes.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-query-or)]

`SortBounded` with `skip`/`take` selects the page without sorting the rest; `TotalCount` is the size before paging. `OrderCache.ByCreatedAtComparer` comes from the `[DataCacheSort]` on `Order`: newest first, `OrderId` as the tie-break, so the order is total and pages never overlap.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-query-sort-page)]

**What to expect:** `recent` holds `ORD-001` only (`ORD-002` fails the `Total` filter, `ORD-003` the date); `actionable` holds `ORD-001` and `ORD-002`; the page prints `showing 2 of 3`. `ExecutePooled` hands back a buffer from a pool and the `using` returns it. [Query Engine](../core-concepts/query-engine.md) covers plan selection and the terminals; [Sorted Paging](../core-concepts/sorted-paging.md) covers `Sort` versus `SortBounded`.

## 5. Join orders with customers and lines

The foreign keys on `Order` and `OrderLine` generate the join methods. `JoinWithCustomer()` is many-to-one, so each row carries a `Customer?`; `JoinWithOrderLine()` follows `OrderLine.OrderId` back, so each row carries a `QueryResults<OrderLine>`.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-join)]

**What to expect:** three rows for customer 42; `ORD-001` has two lines, the others none (an empty, not null, `Right2`). One `Dispose` on the outer result returns every nested buffer. Inner joins, chained joins and the leak rules are in [Joins](../core-concepts/joins.md).

## 6. Freeze the hot path

The endpoint in step 8 answers one shape of question many times: *orders of customer X with status Y, newest first, one page*. Plan it once with `Prepare<TArgs>()` and `BuildFrozen()`; each execution then costs no allocation and no planning. The class below is what the endpoint injects.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-frozen-query)]

Two rules the code follows: lanes take `static` selectors over the argument struct, and nothing pooled leaves the method; the rows are copied into DTOs before the buffer goes back.

**What to expect:** `FrozenQueryServesPages` gets `ORD-001` for `(42, Paid)`, `ORD-002` for `(42, Pending)` and Bob's single order for `(7, Paid)`. Without a sort a frozen result has no row-order guarantee; this one is sorted, so pages match the eager query exactly. Plans, executors and `Explain()` are in [Prepared Queries](../core-concepts/prepared-queries.md).

## 7. React to live updates

An `ICacheAfterHandler<TKey, TValue>` runs after a message has been applied to the cache during the live phase; the initial replay never calls it. `UpdateType` tells you what happened: `Add`, `Update` (with `oldValue`), `Delete` (with `oldValue`), `Same` (a re-emit of the resident value) or `Filtered` (rejected by a filter: no key, no values).

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-shipment-notifier)]

It was registered in step 2 with `.WithAfterHandler<ShipmentNotifier>()`. Handlers run sequentially on the cache's own live worker, so keep them short and hand real work to a queue. A unit test drives one directly:

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-after-handler-test)]

**What to expect:** one notification; the `Same` call is ignored. [React to changes](../how-to/react-to-changes.md) covers each `UpdateType`, equality and the factory registration.

## 8. Health checks and an HTTP endpoint

Two checks ship with `Prague.Kafka`: liveness (the consumer is polling and no handler is stuck) and readiness (loaded, assigned, brokers up). Map them on separate paths, and map `Degraded` to 503; the default returns 200. Both endpoints stay silent until the initial load completes; before deploying to Kubernetes, read [probes during the initial load](../how-to/health-and-metrics.md#2-probes-during-the-initial-load).

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-health)]

The endpoint injects the frozen query from step 6 and the generated `CustomerCache`, which `AddKafkaCaches` registered as a singleton.

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-http-endpoint)]

`Program.cs` is the sum of the steps (*broker*):

[!code-csharp[](../../../../samples/Prague.Samples/Tutorial/BuildAnOrderService.cs#tutorial-program)]

**What to expect:** `EndpointServesDtosFromTheCache` hosts the endpoints in-process over the seeded caches and gets `ORD-001` from `GET /customers/42/orders?status=Paid&skip=0&take=10`, Ada from `GET /customers/42` and a 404 for `GET /customers/999`. With a broker, `GET /health/ready` answers 503 until step 3 completes and 200 afterwards. [Health and metrics](../how-to/health-and-metrics.md) explains each failure reason and the OpenTelemetry instruments.

## Where to go next

- [Page an API](../how-to/page-an-api.md): cursor pages and a total comparer for the list endpoint.
- [Host with DI](../how-to/host-with-di.md): `appsettings.json` binding, several clusters, graceful shutdown.
- [Inspect over HTTP](../how-to/inspect-over-http.md): mount `Prague.Api` to query any cache from a browser.
- [Test without a broker](../how-to/test-without-a-broker.md): the patterns this tutorial used to stay broker-free.

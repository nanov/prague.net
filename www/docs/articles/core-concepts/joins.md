---
title: Joins
description: Declare foreign keys with DataCacheForeignKey and query across Prague caches with generated JoinWith and InnerJoinWith methods, filters and chained joins.
---

# Joins

Prague supports compile-time-safe joins across `[DataCache]` types. The generator wires up the call surface based on `[DataCacheForeignKey<T>]` declarations; the runtime executes the join through one of four resolver families chosen by the relationship shape. The sample domain has four relationships — `Order → Customer` (many-to-one), `Shipment → Order` (one-to-one), `OrderLine → Order` and `OrderLine → Product` (many-to-one) — and every block on this page is compiled and asserted against them in `samples/Prague.Samples/Concepts/Joins.cs`.

## Declaring a relationship

Place `[DataCacheForeignKey<T>(JoinType)]` on the property that holds the foreign key. `Order` points at `Customer`:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Order.cs#order-entity)]

The generator emits:

- A symmetric many-index on `Order.CustomerId` (`OrderCache.CustomerIdIndex`), so `WithCustomerId(...)` works too.
- A forward `JoinWithCustomer()` method on `Order`'s query builder; the slot is `Customer?`.
- A reverse `JoinWithOrder()` method on `Customer`'s query builder; the slot is `QueryResults<Order>`.
- `InnerJoinWith{Other}` variants of both that drop lefts with no match.

Which cache gets which method depends on the declaration:

| Declaration | Emitted on | Result slot |
| --- | --- | --- |
| `OneToOne` on a scalar FK | both caches | `TRight?` |
| `OneToMany` on a scalar FK | the target cache only | `QueryResults<TRight>` |
| `ManyToOne` on a scalar FK | both caches | forward `TRight?`, reverse `QueryResults<TRight>` |
| `ManyToOne` on a `List<TKey>` | both caches | `QueryResults<TRight>` |

`DataCacheJoinType` values:

| Value | Meaning |
| --- | --- |
| `OneToOne` | Unique FK — one-to-one in both directions. |
| `ManyToOne` | Canonical: this entity holds the FK; many of us point at one of them. |
| `OneToMany` | Legacy alias; declared from the FK target's perspective. Prefer `ManyToOne`. |

`Shipment` declares the one-to-one and `OrderLine` two more many-to-ones:

[!code-csharp[](../../../../samples/Prague.Samples/Models/Shipment.cs)]

[!code-csharp[](../../../../samples/Prague.Samples/Models/OrderLine.cs)]

### Dual form

If you cannot place the attribute on the FK property (you control the target but not the type holding the key), place it on the target's primary key and name the FK property with the second constructor argument: `[DataCacheForeignKey<OrderLine>(DataCacheJoinType.OneToMany, nameof(OrderLine.OrderId))]` on `Order.OrderId` is treated exactly like the direct form on `OrderLine.OrderId` with the inverted cardinality.

### Selector form

When the FK property type doesn't match the target's PK type (a compound key, a tuple, a wider integer), use `[DataCacheForeignKey<T, TSelector>]` and supply a `readonly struct` that implements `IForeignKeySelector<TFk, TPk>`; the generator calls its static `Select` at join time to turn the property value into the target's key. `ShipmentLeg` stores its place in a route as `(ShipmentId, Sequence)` and joins to `Shipment` through the first half:

[!code-csharp[](../../../../samples/Prague.Samples/Models/ShipmentLeg.cs)]

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#selector-form-join)]

Rules of the selector form: `ManyToOne` goes on a non-key property (as above) and emits the FK index as usual, keyed by the property's own type; `OneToOne` is the only cardinality allowed on the `[DataCacheKey]` property itself (CACHE040 otherwise). Both emit the forward join only — there is no `ShipmentCache.JoinWithShipmentLeg()`. One limitation to know: when two left rows select the *same* target key, only one of them receives the right row today, so keep the selector one-to-one per query (legs of distinct shipments, as in the sample).

## Calling joins

Use the generated methods directly. The outer join keeps every left row and leaves the slot `null` when nothing matches:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#forward-join)]

`InnerJoinWithCustomer()` drops the orders whose customer is missing:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#inner-join)]

The reverse side of a many-to-one yields the many as a nested `QueryResults<T>`:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#reverse-join)]

When only the number of rights matters, `CountJoinWith{X}` yields the count as `int` without materialising the rights; `InnerCountJoinWith{X}` drops zero counts. Both take the same filter overloads as `JoinWith{X}`; the nested continuation form has no count variant:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#count-join)]

A one-to-one yields `TRight?` from either side; a one-to-many yields `QueryResults<T>` from the one side:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#one-to-one)]

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#one-to-many)]

Caches created through `DataCacheRegistryBuilder` or `AddKafkaCaches` are joined automatically; stand-alone `new XxxCache()` instances need `leftCache.AddJoinedCaches(rightCache, …)` once before joining — the generated signature takes exactly the caches the left's foreign keys point at:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#standalone-wiring)]

Chained joins are supported up to 15 slots per query:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#chained-joins)]

Each `JoinWith{X}` appends a typed slot; the result element type is `JoinResult<Order, Customer?, Shipment?>`. The fields are `Left`, `Right` (the first joined slot — there is no `Right1`), `Right2`, `Right3`, … Pooled join results rent one shared buffer; a single `Dispose()` on the outer `QueryResults` returns everything.

## Filtering the right side

Every `JoinWith{X}` accepts an optional filter lambda that builds a query against the right cache; for zero-allocation static lambdas, pass an argument through the `TArg` overload:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#join-filter)]

The filter callback receives a non-executable builder: the narrowing methods (`WithXxx`, `UseIndex`, `.Or`) and `Where` are visible, `Sort` and `Execute*` are not. The callback runs once per query, not per row. A right row that fails the filter leaves the slot `null` on an outer join and drops the left on an inner one.

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#join-filter-where)]

### Or inside the filter

The same `.Or(b1, b2)` clause from the [Query Engine](query-engine.md#or--disjunction) works inside a join filter, with the same rules — AND within a branch, union across branches, nesting for three or more disjuncts:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#join-filter-or)]

Internally the join's right-side narrowing runs on a paired-core builder, so the filter operates on a bitmap over the join's paired candidate set — no per-branch candidate-set allocations, SIMD-vectorized cross-branch union, mark-then-prune within branches.

## Sorting after joins

`Sort` / `SortBounded` can be applied either before or after a join. After a sorter, only the outer, unfiltered `JoinWith{T}()` binds — `InnerJoinWith{T}` and the filter overloads must come before it:

[!code-csharp[](../../../../samples/Prague.Samples/Concepts/Joins.cs#sort-then-join)]

Sorting before the join compares left values and the join fills the sorted rows (`SortBounded` then pages before joining, so only the page is joined). Sorting after the join compares `JoinResult<…>` rows and always runs the classic full sort.

## Resolver families (informational)

The generator picks a resolver based on the relationship shape; the call site is identical:

- **PK-to-PK** — left PK equals right PK. No FK declaration needed; useful for cross-cache views of the same key.
- **Right-unique-index** — right entity carries a unique FK back to the left (`Order → Shipment`: `Shipment.OrderId` is the unique FK).
- **Left-symmetric-index** — left entity carries a symmetric many FK (`ManyToOne`); join walks the index's reverse map.
- **Left-symmetric-unique-index** — bijective 1:1 via a symmetric unique FK on the left (`Shipment → Order`).

All four use a paired-execution core that intersects candidate keys with index hits and dispatches a single bulk read into the right cache. Pairs are guarded by a leak-safety contract that returns rented arrays exactly once. Lower-level `JoinOne(...)` / `InnerJoinOne(...)` / `JoinMany(...)` / `InnerJoinMany(...)` extensions exist for the raw cache; pass the cache wrapper, not `.Query()`.

## Next

- [Prepared and Frozen Queries](prepared-queries.md) — joins on a frozen query fuse into the single pass.
- [Kafka integration](../advanced/kafka-integration.md) — wiring caches to topics so the join sides stay in sync.
- [Performance tuning](../advanced/performance-tuning.md) — hot-path guidance for joined queries.

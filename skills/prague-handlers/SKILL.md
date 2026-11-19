---
name: prague-handlers
description: "Prague message filters and after-handlers: WithHeaderEqualsFilter, WithKeyFilter, WithValueFilter(treatAsDelete), ICacheAfterHandler, UpdateType, projections that include the loaded data. Use when filtering a cache's messages or reacting to its changes. Not for cache wiring (use prague-kafka)."
license: MIT
metadata:
  applies-to: "Prague >= 0.9.0, <= 0.10.0"
  verified: 2026-11-19
  docs: https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html
---

# Prague: filters and after-handlers

Both hang off a cache registered with `AddKafkaCaches` (see `prague-kafka`). Filters decide whether a message enters the cache; after-handlers run once the cache has applied it. When this file and the code disagree, the code wins. Namespaces `Prague.Kafka` and `Prague.Core`.

## Pipeline per message

1. Header filters (raw headers; nothing deserialized yet).
2. Key deserialization. A tombstone (null value) deletes the key here: no key or value filter sees it.
3. Key filters, then value deserialization and value filters.
4. `AddOrUpdate` / `Remove` on the cache: `CacheEquals` decides Add, Update or Same.
5. After-handlers, live phase only, sequentially, on the cache's live worker.

Everything up to step 4 runs on the single consume thread, for every message, including the initial replay.

## Filters

```csharp
b.AddCache<OrderCache, string, Order>()
	.WithHeaderEqualsFilter("tenant", "acme")                  // string | int | long; no allocation
	.WithHeaderEqualsFilter("region", "EU", "UK")              // several values = OR
	.WithHeaderNotEqualsFilter("source", "synthetic")
	.WithHeaderExistsFilter("tenant")                         // up to 64 names; tombstones exempt
	.WithHeaderFilter<long>("schema-version", v => v >= 3, passOnNull: false)   // MessagePack-decoded
	.WithKeyFilter(static key => !key.StartsWith("TEST-", StringComparison.Ordinal))
	.WithValueFilter(static o => o.Total > 0m);
```

- Filters AND-compose; the **first** rejecting filter decides the outcome.
- A rejection **skips** the message by default: the cache keeps whatever it held for that key.
- `treatAsDelete: true` (key and value filters only) turns a rejection into a delete of the resident row. Use it when the predicate means "should not be in the cache", e.g. soft deletes:
  `.WithValueFilter(static o => o.Status != OrderStatus.Cancelled, treatAsDelete: true)`.
  A plain filter placed before it still wins: a message it rejects is skipped, not deleted.
- A throwing predicate is a plain reject (skip), never a delete.
- Header filters have no `treatAsDelete`. A header value filter drops a tombstone that carries a rejected value; `WithHeaderExistsFilter` lets tombstones through.
- Filters never re-run over data already in memory. Changing a filter affects only later messages; restart to reload under the new filter.

Filters with DI state:

```csharp
// Snapshot: the factory runs once when the handler is built.
.WithValueFilter(
	static sp => sp.GetRequiredService<ICustomerAllowList>().CustomerIds.ToFrozenSet(),
	static (allowed, o) => allowed.Contains(o.CustomerId))
// Live service: resolved once, read per message; TService is spelled out.
.WithValueFilter<ICustomerAllowList>(static (allow, o) => allow.CustomerIds.Contains(o.CustomerId))
```

Keep predicates to a lookup or comparison: no IO, no locks, no allocation. They run on the consume thread, and one silent for 3 s fails liveness. Anything heavier belongs in an after-handler.

## After-handlers

A projection that other code reads, kept correct for the data already in the topic at startup:

```csharp
public sealed class OpenTotals : ICacheAfterHandler<string, Order> {
	private readonly Lock _lock = new();
	private readonly Dictionary<string, (int CustomerId, decimal Total)> _byOrder = [];   // each order's contribution
	private readonly Dictionary<int, decimal> _byCustomer = [];

	public decimal Get(int customerId) {
		lock (_lock)
			return _byCustomer.GetValueOrDefault(customerId);
	}

	public ValueTask Handle(UpdateType updateType, string key, Order? newValue, Order? oldValue) {
		switch (updateType) {
			case UpdateType.Add:
			case UpdateType.Update:
				lock (_lock)
					Apply(key, newValue);
				break;
			case UpdateType.Delete:
				lock (_lock)
					Apply(key, null);
				break;
		}                                   // Same changes nothing; Filtered has no key
		return ValueTask.CompletedTask;
	}

	public void Seed(OrderCache orders) {
		using var all = orders.Query().ExecutePooled();
		foreach (var order in all)
			lock (_lock)                    // re-read under the lock: a live update may have replaced the row since the query
				Apply(order.OrderId, orders.Cache.TryGet(order.OrderId, out var current) ? current : null);
	}

	private void Apply(string orderId, Order? order) {   // absolute per key, so applying a key twice is harmless
		if (_byOrder.Remove(orderId, out var old))
			_byCustomer[old.CustomerId] -= old.Total;
		if (order is { Status: OrderStatus.Pending or OrderStatus.Paid }) {
			_byOrder[orderId] = (order.CustomerId, order.Total);
			_byCustomer[order.CustomerId] = _byCustomer.GetValueOrDefault(order.CustomerId) + order.Total;
		}
	}
}

public sealed class OpenTotalsSeeder(IDataCacheRegistry registry, OrderCache orders, OpenTotals totals) : BackgroundService {
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		await registry.LoadingCompletion.WaitAsync(stoppingToken);
		totals.Seed(orders);
	}
}

services.AddSingleton<OpenTotals>();
services.AddHostedService<OpenTotalsSeeder>();
services.AddKafkaCaches(b => b.AddCache<OrderCache, string, Order>()
	.WithAfterHandler(sp => sp.GetRequiredService<OpenTotals>()));
```

| `UpdateType` | `key` | `newValue` | `oldValue` | When |
|---|---|---|---|---|
| `Add` | set | set | null | key was not resident |
| `Update` | set | set | set | resident, `CacheEquals` said different |
| `Same` | set | set (the incoming copy; the cache keeps its resident instance) | null | resident, `CacheEquals` said equal |
| `Delete` | set | null | set | tombstone or `treatAsDelete`; only if the key was resident |
| `Filtered` | **default** | null | null | a filter skipped the message |

Rules, each one visible in the sample:
- **One instance when other code reads the handler's state.** `WithAfterHandler<T>()` registers `T` only as `ICacheAfterHandler<TKey, TValue>`; a `services.AddSingleton<T>()` beside it is a second object, and readers injecting `T` watch the one that never receives a message. Register the instance once and pass it through the factory form, as above. The test that proves it is in `prague-testing`.
- **Live phase only.** The initial replay never calls handlers. Derive state that includes the loaded data by seeding from the cache in a dedicated `BackgroundService` that waits for `LoadingCompletion`, never as a side effect of an unrelated job. Live messages arrive between the load and the seed, so both paths write absolute per-key values under one lock rather than adding deltas.
- **Never read `key` on `Filtered`.** It is `default` (null for a string key).
- **Keep `Handle` short.** Every message of that cache waits behind it; liveness fails after `HandlerProcessingTimeout` (5 s). Enqueue slow work (`Channel<T>`) and return.
- **Treat `newValue`/`oldValue` as read-only.** On `Add`/`Update` `newValue` is the instance the cache now holds and queries return, and `oldValue` the one it replaced. To derive a new row from one, clone it (`prague-modeling`, Changing a cached row).
- **Readers are on other threads.** Handler state is written from the live worker thread; guard it with a lock or a concurrent structure.
- Several handlers per cache run one after the other; an exception is caught and logged and does not skip the others.

## Same vs Update

The cache compares the incoming value with the resident one using the generated `CacheEquals` (deep, property by property). An equal value is `Same`: the resident instance stays, no index changes. Use it to make projectors idempotent against re-emits. Fields marked `[DataCacheIgnoreEquality]`, and fields your cache type does not declare at all, never turn a message into an `Update`.

## Common mistakes

| Wrong | Right |
|---|---|
| reading `key` on `UpdateType.Filtered` | return early |
| `AddSingleton<T>()` + `WithAfterHandler<T>()` for state read elsewhere | `WithAfterHandler(sp => sp.GetRequiredService<T>())` |
| seeding a projection from an unrelated job, or before the load | a dedicated `BackgroundService` that waits for `LoadingCompletion` |
| adding deltas in both the seed and the handler | absolute per-key contributions under one lock |
| awaiting IO in `Handle` | enqueue and return |
| changing `newValue` in place | clone it, change the clone |
| a default (skip) filter for "no longer qualifies" | `treatAsDelete: true` |
| `treatAsDelete` filter after a plain filter that rejects the same messages | put the `treatAsDelete` filter first |
| IO or a DB call inside a filter predicate | snapshot via the factory overload, or a live in-memory service |
| relying on a filter change to clean resident rows | restart to reload |

Read next: [write a filter](https://nanov.github.io/prague.net/docs/articles/how-to/write-a-filter.html), [react to changes](https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html), [conditional updates](https://nanov.github.io/prague.net/docs/articles/core-concepts/conditional-updates.html).

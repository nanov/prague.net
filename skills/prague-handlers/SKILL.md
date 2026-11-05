---
name: prague-handlers
description: "Filter and react to Kafka messages in Prague caches: header/key/value filters (WithHeaderEqualsFilter, WithKeyFilter, WithValueFilter), treatAsDelete soft deletes, filters with DI state, ICacheAfterHandler<TKey,TValue> and every UpdateType (Filtered, Same, Add, Update, Delete), Same vs Update equality, derived projections that follow a cache, work that must start after the initial load. Use when writing a message filter, an after-handler or projection over a Prague cache, or when a handler misses or double-counts changes."
license: MIT
metadata:
  package: Prague.Kafka
  docs: https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html
---

# Prague: filters and after-handlers

Both hang off a cache registered with `AddKafkaCaches` (see `prague-kafka`). Filters decide whether a message enters the cache; after-handlers run once the cache has applied it. Verified against Prague 0.9.0; when this file and the code disagree, the code wins. Namespaces `Prague.Kafka` and `Prague.Core`.

## Pipeline per message

1. Header filters (raw headers; nothing deserialized yet).
2. Key deserialization, key filters.
3. Value deserialization, value filters. A tombstone (null value) skips steps 2-3 filters and deletes.
4. `AddOrUpdate` / `Remove` on the cache: `CacheEquals` decides Add, Update or Same.
5. After-handlers, live phase only, sequentially, on the cache's live worker.

Everything up to step 4 runs on the single consume thread, for every message, including the initial replay.

## Filters

```csharp
b.AddCache<OrderCache, string, Order>()
	.WithHeaderEqualsFilter("tenant", "acme")                  // string | int | long; UTF-8 compare, no allocation
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
- Header filters have no `treatAsDelete`. A tombstone can only be dropped by a header value filter.
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

Keep predicates to a lookup or comparison: no IO, no locks, no allocation. Anything heavier belongs in an after-handler.

## After-handlers

```csharp
public sealed class OrderProjector : ICacheAfterHandler<string, Order> {
	public ValueTask Handle(UpdateType updateType, string key, Order? newValue, Order? oldValue) {
		switch (updateType) {
			case UpdateType.Add: Apply(newValue!, +1); break;
			case UpdateType.Update: Apply(oldValue!, -1); Apply(newValue!, +1); break;
			case UpdateType.Delete: Apply(oldValue!, -1); break;
			case UpdateType.Same:          // re-emit of an equal value
			case UpdateType.Filtered:      // no key, no values
				break;
		}
		return ValueTask.CompletedTask;
	}
}

b.AddCache<OrderCache, string, Order>()
	.WithAfterHandler<OrderProjector>()                                         // singleton
	.WithAfterHandler(sp => new OrderAuditLog(sp.GetRequiredService<TimeProvider>()));   // factory
```

| `UpdateType` | `key` | `newValue` | `oldValue` | When |
|---|---|---|---|---|
| `Add` | set | set | null | key was not resident |
| `Update` | set | set | set | resident, `CacheEquals` said different |
| `Same` | set | set (the incoming copy; the cache keeps its resident instance) | null | resident, `CacheEquals` said equal |
| `Delete` | set | null | set | tombstone or `treatAsDelete`; only if the key was resident |
| `Filtered` | **default** | null | null | a filter skipped the message |

Rules:
- **One instance when other code reads the handler's state.** `WithAfterHandler<T>()` registers `T` only as `ICacheAfterHandler<TKey, TValue>`; a `services.AddSingleton<T>()` beside it is a second object, and readers injecting `T` watch the one that never receives a message. Register the shared instance once and hand it to the cache through the factory form:
  `services.AddSingleton<OpenTotals>();` then `.WithAfterHandler(sp => sp.GetRequiredService<OpenTotals>())`. The test that proves it is in `prague-testing`.
- **Live phase only.** The initial replay never calls handlers. To derive state that includes the loaded data, seed it from the cache after the load in a dedicated `BackgroundService` whose `ExecuteAsync` opens with `await registry.LoadingCompletion.WaitAsync(stoppingToken)` (see `prague-kafka`), never as a side effect of an unrelated job. Live messages can arrive between the load and the seed, so make both paths idempotent: keep each key's contribution (`key → (group, amount)`) and write absolute values under one lock, rather than adding deltas.
- **Never read `key` on `Filtered`.** It is `default` (null for a string key). Return early.
- **Keep `Handle` short.** Every message of that cache waits behind it; the liveness check fails after `HandlerProcessingTimeout` (5 s default). Enqueue slow work (`Channel<T>`) and return.
- **Treat `newValue`/`oldValue` as read-only.** On `Add`/`Update` `newValue` is the instance the cache now holds and queries return, and `oldValue` the one it replaced. To derive a new row from one, clone it (`prague-modeling`, Changing a cached row).
- Several handlers per cache run one after the other; an exception is caught and logged and does not skip the others.
- State written by a handler is written from the live worker thread; readers on other threads need a thread-safe structure.

## Same vs Update

The cache compares the incoming value with the resident one using the generated `CacheEquals` (deep, property by property). An equal value is `Same`: the resident instance stays, no index changes. Use it to make projectors idempotent against re-emits. Fields marked `[DataCacheIgnoreEquality]`, and fields your cache type does not declare at all, never turn a message into an `Update`.

## Common mistakes

| Wrong | Right |
|---|---|
| reading `key` on `UpdateType.Filtered` | return early |
| `AddSingleton<T>()` + `WithAfterHandler<T>()` for state read elsewhere | `WithAfterHandler(sp => sp.GetRequiredService<T>())` |
| relying on registration order to run "after the load" | `await registry.LoadingCompletion.WaitAsync(stoppingToken)` first in `ExecuteAsync` |
| awaiting IO in `Handle` | enqueue and return |
| expecting handlers during the initial load | build from the cache after load, then apply deltas |
| changing `newValue` in place | clone it, change the clone |
| a default (skip) filter for "no longer qualifies" | `treatAsDelete: true` |
| `treatAsDelete` filter after a plain filter that rejects the same messages | put the `treatAsDelete` filter first |
| IO or a DB call inside a filter predicate | snapshot via the factory overload, or a live in-memory service |
| relying on a filter change to clean resident rows | restart to reload |

Read next: [write a filter](https://nanov.github.io/prague.net/docs/articles/how-to/write-a-filter.html), [react to changes](https://nanov.github.io/prague.net/docs/articles/how-to/react-to-changes.html), [conditional updates](https://nanov.github.io/prague.net/docs/articles/core-concepts/conditional-updates.html), [host with DI](https://nanov.github.io/prague.net/docs/articles/how-to/host-with-di.html).

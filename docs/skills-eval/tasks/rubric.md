# Grading rubric

Score each item 0 (missing or wrong), 1 (partial), 2 (correct). Quote the file:line that justifies each score. Judge the code, not comments or the agent's claims. "Prague facts" below are ground truth for this library version.

## Prague facts (ground truth)
- `[DataCache]` classes must be `partial`; exactly one `[DataCacheKey]`.
- Kafka values are MessagePack maps keyed by property NAME; a consumer copy may drop properties but must keep names exactly. A consumer should cache only what it needs.
- Index types: `Unique` (1 row per value), `Many` (default), `Range` (ranges, ordering). `Where` never uses an index.
- `ExecutePooled()` results must be disposed (`using`). `Execute()` allocates and needs no dispose.
- `Sort`/`SortBounded` break ties by encounter order: a deterministic page needs a TOTAL comparer (ending in the primary key). `SortBounded` is the top-k form for small pages. `[DataCacheSort("Name", "Prop:-1", "Key:1")]` generates `XxxCache.NameComparer`.
- `Prepare<TArgs>()...BuildFrozen()` plans once; must be built once and stored (field), not per call. Unsorted frozen output has no order guarantee.
- Joins come from `[DataCacheForeignKey<T>(DataCacheJoinType.ManyToOne)]`; row fields are `Left`, `Right`. Hand-built caches need `AddJoinedCaches`; registry/AddKafkaCaches wire automatically.
- Filters: header filters `WithHeaderEqualsFilter`; a rejected message is SKIPPED by default (resident row stays); `treatAsDelete: true` removes it.
- After-handlers (`ICacheAfterHandler`) run in the LIVE phase only — NOT during the initial load. `UpdateType.Filtered` carries a default key and null values. `Same` means no change; on `Same` the key is set, `newValue` is the incoming equal copy (not the resident instance) and `oldValue` is null. Handlers run on a single worker; state read from other threads needs thread safety.
- Gate work on the initial load by awaiting `registry.LoadingCompletion` (with the stopping token) inside `BackgroundService.ExecuteAsync`, or `await host.DataCachesLoadCompletion()`. Waiting in a hosted service's `StartAsync` deadlocks the host when that service is registered before `AddKafkaCaches`; in 0.6.0 `KafkaCachesBackgroundService` waits there.
- Unit tests need no broker: `new XxxCache()` / `DataCacheRegistryBuilder`, seed with `AddOrUpdate`.

## T1 — shipment read model
1. Own copy of the contract in this project, `partial`, property names identical to the producer's.
2. Holds only needed properties (drops ScanEvents, InternalNotes, WeightGrams, Sender; Recipient reduced or kept minimal).
3. `TrackingNumber` lookup via a Unique index (not a scan / Where).
4. Carrier + Status narrowed by indexes (Many), not Where.
5. `DeliveredAtUnixMs` range via a Range index with `[from, to)` semantics (Gte/Lt), counting without materializing (Count()) or equivalent. A Range index cannot be on `long?` (compile error), so a non-nullable derived key is expected; undelivered rows must never be counted for ANY from/to (a sentinel that can fall inside the bounds = 1).
6. Stable paging: total comparer (CreatedAt desc then key), skip/take paging; SortBounded or Sort acceptable.
7. Pooled results disposed (or allocating Execute used correctly); no leaks.
8. Tests: broker-free, cover all three methods, assert sets where order is not guaranteed, assert order where sorted; build+tests pass.

## T2 — hot endpoint
1. Correct models: partial, keys, FK `[DataCacheForeignKey<Customer>(ManyToOne)]` on Order.CustomerId.
2. Query is prepared + frozen ONCE (field/static), not built per call; static selectors; struct args (tuple/record struct).
3. Deterministic order: total comparer (CreatedAt desc, then OrderId) applied with SortBounded/Sort and take 20.
4. Join to Customer via generated JoinWithCustomer (outer, so missing customer → null name), `Right` used correctly.
5. Pooled execution disposed; per-call allocation minimal apart from the returned list.
6. Thread-safety: no shared mutable state per call.
7. Tests: broker-free, cover order, missing customer, empty customer; join wiring correct (registry or AddJoinedCaches); build+tests pass.

## T3 — Kafka wiring and projection
1. AddKafkaCaches registration with topic template using a var (`[v:env]` + `o.Vars["env"]`) or equivalent correct topic.
2. Header filter `WithHeaderEqualsFilter("tenant", "acme")`.
3. Cancelled removal via `WithValueFilter(..., treatAsDelete: true)` (a plain skip filter or an after-handler hack is wrong/partial).
4. After-handler handles Add/Update/Delete correctly, never changes totals on Same (ignoring it or an idempotent re-set are both fine), and does NOT read key/values on Filtered.
5. Projection correct for pre-existing data: seeded/rebuilt from the cache after the initial load (handlers do not see the initial load), race-free against live messages arriving around the seed, and seeding owned by the projection (its own hosted service / load-completion hook), not a side effect of an unrelated job (that = 1).
6. Projection thread-safe for concurrent readers (e.g. ConcurrentDictionary / locks / immutable swap).
7. Background job starts after load by awaiting load completion in `ExecuteAsync`, independent of registration order (relying on order, or `KafkaCachesBackgroundService` on 0.6.0 = 1).
8. Tests: broker-free, drive Handle by hand incl. Same/Filtered/Delete; build+tests pass.

## Output
There are FOUR solutions per task here (W, X, Y, Z), each independent. Score all four.
Per task, per variant: item scores with one-line justification, total, then a 2-3 sentence comparison. Finally a summary table (task × variant totals) and the 3 most consequential differences across all tasks.

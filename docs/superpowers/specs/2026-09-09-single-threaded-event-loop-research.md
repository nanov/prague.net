# Research: single-threaded event loop per cache

> **Status:** research note, no decision taken. Written against `d781863` (main).
> **Question:** could Prague move from "single writer + lock-free readers on any thread" to
> "one owner thread per cache, everything else submits commands", and what would it take?

## 1. What the runtime does today

### Writers are already single-threaded per cache — by convention, not by construction

- **Load phase:** one `KafkaCacheConsumer` owns one dedicated thread (`ConsumeRawLoop`,
  `IO/KafkaCacheConsumer.cs:598`). That thread writes *every* cache the consumer feeds, via
  `ValueCompactingBuffer` flushes.
- **Live phase:** each handler owns a `RawLiveWorker` (SPSC ring, capacity 64, its own thread,
  `IO/KafkaCacheConsumer.cs:360`). The consume thread publishes `{Kind, Key, Value, Ts}` slots; the
  worker thread applies `AddOrUpdate`/`Remove` and then awaits the after-handler chain
  *before taking the next slot* (`AsyncValueBufferedWorker._processDoneEvt`). So writes to a given
  cache are serialized, and after-handlers stall that cache's ingestion while they await.
- Nothing in `InMemoryDataCache` enforces this. `AddOrUpdate`/`Remove` are public, and the store
  (`ConcurrentCacheStore`, a `ConcurrentDictionary` clone with striped locks) tolerates concurrent
  writers, but every index bucket type (`PooledSet`, `PooledBTree`) is documented single-writer.
  A second writer today is silent corruption, not an exception.

### Readers run on any thread, lock-free, with a documented staleness model

The machinery that makes that safe is the most intricate code in the repo:

| Component | Lines | Role |
|---|---|---|
| `Collections/ConcurrentCacheStore.cs` | 1469 | striped-lock dictionary for the value store and unique indexes |
| `Collections/PooledBTree.cs` | 1838 | range index; release-published nodes, `ReaderGate` retirement |
| `Collections/PooledSet.cs` | 617 | 1:N index bucket; volatile `Tables` generations, version-guarded copy-out, pin refcounts |
| `Collections/ConcurrentSortedSet.cs` | 534 | lazy skip list (older range/last-updated lanes) |
| `Collections/ReaderGate.cs` | 265 | process-wide RCU grace period: per-thread padded pin slots, finalizer-recycled, sealed limbo batches |
| `Collections/QuickLookupCache.cs`, `KeySetIndex._lock`, Kafka `*Unsafe` accessors | ~100 | assorted CAS / lock islands |
| Concurrency tests (`ReaderGateTests`, `ConcurrentReclamationStressTests`, `ConcurrentCacheStore*Tests`, `*ConcurrentMutationTests`, `ConcurrentSortedListTests`) | ~4,900 | pin the above |

About 10k lines exist only because a reader may be on the structure while the writer mutates it.

### Consistency is per-structure, never per-cache

`AddOrUpdate` (`InMemoryDataCache.cs:1436`) writes the store first, then walks `_indeces` one by
one. A concurrent reader can observe: the new value through `TryGet` but the old bucket through an
index; an index hit whose value has already moved on; a range scan that double-sees or skips an
entry mid-shift (`PooledBTree` doc header). Cross-cache joins read the right cache's live
structures directly (`JoinOneResolver.cs:338`, `Cache.Cache.KeyIndex`), so a join reflects no
single point in time for either side. The README's "Consistency: Immediate" is about network
round-trips, not about this.

### Results are already materialized

`QueryResults<T>` is a `T[]` (pooled or heap) of value references (`QueryResults.cs`). Values are
treated as immutable and replaced on update (`ICacheClonable`, `CacheEquals`). The only live views
that escape are the `IReadOnlyCollection<TKey>` returns of `GetValues*` on range/last-updated
indexes and the boxed `PooledSet` enumerator. This matters: a command model needs results that can
cross a thread boundary, and almost everything already can.

### Baseline numbers (perf/baseline/apple-m4pro-darwin.json, core-only)

| Metric | Value |
|---|---|
| unique lookup p50 | 216 ns |
| range scan p50 | 3.0 µs |
| joinOne p50 | 8.6 µs |
| joinMany p50 | 95 µs |
| ingest | 8.26 M ent/s |
| README concurrent read figure | 15.9 M rows/s, 10 reader threads + 2 writers |

## 2. What "event loop per cache" means here

One owner thread per cache (or per group of caches, §4). All mutation *and all reads of the
cache's structures* happen on that thread. Every other thread interacts by enqueuing a command and
awaiting its completion. Structures become plain single-threaded collections.

### Yes: queries become commands

Today `Query()` builds a struct chain describing the plan (index picks, keys, predicates, join
chain) on the caller thread, and `Execute()` runs it synchronously on the caller thread against
live structures. Under the loop model the builder is unchanged, and `Execute()` becomes a submit:

```
caller thread                        loop thread
-------------                        -----------
build query (struct chain)
enqueue { query, completion }  --->  dequeue
await completion                     run existing execution core (unchanged)
                                     materialize QueryResults<T> (already how it works)
receive QueryResults<T>        <---  complete
Dispose() pooled array (ArrayPool is thread-safe, so rent-on-loop / return-on-caller is fine)
```

What has to change in the query layer specifically:

- **`Execute()` / `ExecutePooled()` / `Count()` / `TryGet()`** become `ValueTask<…>`. Sync variants
  either block the caller (dangerous from the thread-pool) or are restricted to "already on the
  loop thread" (useful inside after-handlers). Public API break, major version.
- **Zero-alloc completions.** Need a pooled `IValueTaskSource` (`ManualResetValueTaskSourceCore`)
  per in-flight command. The command payload is the query struct chain, whose closed generic type is
  unique per query shape, so it cannot be dropped into a single `T[]` ring. Options: a per-shape
  typed command pool (JIT specialization, zero boxing, more types), or box the chain once per call
  (one allocation, kills the `0 B/query` pin in `TopKAllocationCoreTests`). This is the main
  hot-path design problem on the query side.
- **User lambdas run on the loop.** `Where(predicate)`, `[DataCacheIgnoreEquality]` equality,
  `Clone()`, comparers: a slow or throwing predicate now stalls that cache's ingestion. Today it
  only costs the caller.
- **Escaping live views** (`GetValues*` on `CacheRangeIndex` / last-updated indexes, boxed
  `PooledSet` enumerator) must become snapshots copied out on the loop.
- **`Prague.Api`** does `cache.TryGet` inline in endpoint handlers (`EndpointMappingExtensions.cs:23`)
  and becomes `await`.

### Writes: mostly already there

The `RawLiveWorker` *is* a per-cache single-consumer loop. It would become the loop, with its ring
widened from SPSC to MPSC (query submitters are many threads) and its slot type widened from
`RawWorkItem` to "write or query command". Load-phase writes stay on the consume thread until the
loop starts, as today, or get routed through the loop in batches.

One semantic decision hides here: **after-handlers**. Today the worker awaits each handler before
the next write. On a loop that also serves queries, either (a) queries queue behind a slow
`await` in user code, or (b) the loop keeps running while the handler is parked, which changes the
current write→handler ordering guarantee. Recommendation: (b) with handler continuations posted
back to the loop, so handler *bodies* still run on the loop but I/O waits don't block it.

## 3. What it buys and what it costs

### Buys

- Deletes or radically simplifies the ~10k lines in the table above. `ConcurrentCacheStore` →
  `Dictionary`/`ValueDictionary`; `PooledSet` → a plain pooled hash set with no generations,
  versions, pins or sentinel; `PooledBTree` keeps its algorithms and drops publication ordering
  and `ReaderGate` retirement; `ReaderGate` and `ConcurrentSortedSet` go away entirely.
- **Per-cache consistency for free.** A query sees the store and all indexes at one point in time
  because nothing interleaves with it. The staleness model, phantom-row class of bugs, and the
  "concurrent mutation" test families disappear.
- Pool leak-safety gets simpler: no reader can hold a retired array, so `handedOff` guards on
  joins and the grace-period drain in `LeakAssert` shrink.
- One place to add per-cache backpressure, queue-depth metrics, and tracing.

### Costs

- **Point-lookup latency.** A cross-thread hop is a store, a wake (or a spin), and a completion.
  With a spinning loop thread expect low single-digit µs end to end; with a parked thread woken by
  an event, tens of µs. Against a 216 ns lookup that is 10–100×. Complex queries (joinMany, 95 µs)
  barely notice. **This must be measured before anything else (§5, phase 0).**
- **Read throughput stops scaling with cores.** Today 10 reader threads read in parallel against
  one writer. One loop per cache means one core per cache for reads *and* writes combined, and all
  callers' queries serialize behind ingestion bursts. The 15.9 M rows/s figure will drop; how far
  depends on hop cost and batching.
- **Cross-cache joins are the hard problem** (§4).
- **Public API break** (`Execute` → async), plus codegen changes for every emitted `Execute`,
  `TryGet`, `StringQueryInternal`, and FK `JoinWith` surface.
- The README's headline claims (`<100ns`, `no lock contention on reads`, `near-linear read
  scalability`) need to be rewritten; this changes the product's identity, not just its internals.

## 4. Cross-cache joins decide the loop grain

A `JoinOne`/`JoinMany` running on cache A's loop reads cache B's key index directly. With one loop
per cache that is exactly the cross-thread access the model forbids. Realistic options:

| Option | How | Verdict |
|---|---|---|
| **A. One loop per join-group** | Caches that ever join share a loop. FK-attribute `JoinWith` edges are known at codegen time; ad-hoc `JoinOne(other, selector)` edges are runtime. Startup validation: joining across loops throws. | **Recommended.** Natural default grain is one loop per `KafkaCacheConsumer` (that thread already feeds all its caches); split caches that never join if a consumer's loop becomes hot. |
| B. One global loop | Redis model. All caches, one thread. | Simplest; total system throughput capped at one core. Acceptable for small deployments, not as the only mode. |
| C. Multi-hop commands | Left loop computes candidate keys, hops to right loop for lookups, hops back; N hops for an N-level join. | Not recommended. Moves complexity from the memory model into orchestration, and latency multiplies per level. |
| D. Loop owns writes, readers read published snapshots | Copy-on-write / epoch-published immutable structures; any thread reads the latest snapshot with no coordination. | Keeps read scaling and deletes `ReaderGate`, but replaces pooled mutable arrays with persistent structures: allocation per write batch, directly against the zero-alloc rules. Cross-cache joins still see two different snapshots. Worth a paragraph in the decision, not a prototype. |

Option A also gives the *strongest* consistency story: a join inside one loop sees both caches at
the same instant, which nothing offers today.

## 5. Proposed phases (each independently shippable)

0. **Measure the hop.** Add a BenchmarkDotNet case: MPSC ring + dedicated thread (spinning, and
   parked-on-event variants) executing `TryGet` and `joinOne` on behalf of 1 / 4 / 10 caller
   threads, against today's direct call. Run on M4 Pro and the linux-x64 CI class. **Decision gate:
   if p50 for a point lookup lands above ~2 µs with spinning, the loop cannot be the only read path
   and the design needs a fast lock-free `TryGet` lane or option D.**
1. **Make the single-writer invariant explicit.** Record an owner thread id per
   `InMemoryDataCache`; `Debug.Assert` on every mutator. No behavior change, no perf change in
   Release. Also removes the "second writer silently corrupts" foot-gun.
2. **Introduce `CacheLoop`** as the successor to `RawLiveWorker`: MPSC ring, command slot union
   (write | query), pooled `IValueTaskSource` completions, after-handler continuation policy,
   drain/shutdown, queue-depth metric in `Prague.Kafka.OpenTelemetry`. Writes route through it;
   reads unchanged.
3. **Add the async read surface** (`ExecuteAsync`, `TryGetAsync`, …) next to the sync one. Codegen
   emits both. Sync reads still go lock-free. Consumers migrate at their pace.
4. **Flip.** Sync reads become loop-thread-only (throw off-loop). Delete `ReaderGate`,
   `ConcurrentSortedSet`; replace `ConcurrentCacheStore` with a single-threaded store; strip
   generations/versions/pins from `PooledSet`; strip publication ordering from `PooledBTree`.
   Retire the concurrency test families; the leak tests stay.
5. **Join-groups.** Codegen emits the FK join graph; `AddKafkaCaches` assigns loops (default per
   consumer); startup validation for ad-hoc cross-loop joins.

Phases 1–3 are low-risk and useful even if phase 4 is never taken.

## 6. Open questions to settle before phase 2

- Spin vs park for the loop thread, and for how long. Spinning costs a core per loop.
- Backpressure when the command ring is full: block the submitter, return `Task` that awaits
  space, or reject. Kafka writes and API reads probably want different answers.
- Whether `Where` predicates and comparers are allowed to be arbitrary user code on the loop, or
  whether the API should push toward index-only narrowing plus caller-side filtering of the
  materialized result.
- Test strategy: `Prague.Core.Tests` call `InMemoryDataCache` directly from the NUnit thread. Either
  tests run "on the loop" via a test scheduler, or the raw cache keeps a synchronous mode with the
  owner-thread assertion.
- Whether to keep a lock-free `TryGet` fast lane permanently (hybrid) if phase 0 says the hop is
  too expensive for the point-read use case the README sells.

## 7. Bottom line

Feasible, and the write side is already 80% of the way there. The maintainability win is real and
concentrated: roughly 10k lines of the hardest code in the repo, plus a consistency model that is
currently "documented staleness" becoming "one point in time per loop". The price is paid entirely
on the read side: point-lookup latency multiplies, read throughput stops scaling with cores, and
joins force caches into shared loops. Phase 0 is cheap and decides whether the price is acceptable
for the workloads Prague actually serves.

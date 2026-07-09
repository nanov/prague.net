# Perf-neutral port of internal PR #56: B-tree leak, PooledSet corruption, range-index observability

**Date:** 2026-07-10
**Status:** Approved
**Upstream reference:** B2Tech-Git/internal-be-kafka-cache-lib PR #56 ("Fix memory leak and data corruption")

## Problem

Upstream diagnosed a production incident (process 0.9 GB → 2 GB; one `PooledBTree` range
index held 9,137,124 entries for 109,118 live items) and found three defects. All three
were confirmed present in prague.net by porting the upstream regression tests — **12
tests, 9 failing**, all deterministic and single-threaded:

1. **`PooledBTree` equal-key-run leak** (`src/Prague.Core/Collections/PooledBTree.cs`).
   When more than `LeafCapacity` (64) entries share one index key (e.g. millisecond
   timestamps), the run of equal keys spans multiple leaves after splits. Descent routes
   equal keys to the rightmost candidate leaf and `FindExact` scans only that leaf, so
   `Remove` silently returns `false` for pairs in earlier leaves of the run.
   `CacheRangeIndex.Update` ignores the result → every update adds a new entry and fails
   to remove the old one, forever. Same single-leaf blindness affects `Contains` and
   `Add`'s duplicate check. Related: `RangeFromExclusive` / `RangeCustom(includeFrom:
   false)` leak keys equal to the exclusive bound when the run continues into the next
   leaf. The internal write lock is irrelevant — this is descent logic, fully
   single-threaded. Repro: `PooledBTreeTests.UpdateChurn_RangeIndexPattern_*` fails at
   round 1, item 0.

2. **`PooledSet` use-after-return-to-pool** (`src/Prague.Core/Collections/PooledSet.cs`).
   `CacheKeyValueListIndex.GetValues` hands the live set to callers. The boxed interface
   enumerator takes **no** `_pooledRefCount`, while `Dispose`/`Grow` return the backing
   arrays to `ArrayPool.Shared`; `ArrayPool.Shared` returns to a thread-local slot, so
   the very next same-size rent on the same thread receives the enumerator's live array.
   Repro observed an enumeration yield `< 11, 9999, 9999 >` — foreign data, one thread,
   no concurrency required. Additional protocol defects: check-then-increment race in
   `GetEnumerator` vs `RetirePooledArrays`, and `Grow` never returns the grow-rented
   arrays at all (a permanent pool leak).

3. **`CacheRangeIndex` drift masking** (`src/Prague.Core/InMemoryDataCache.cs:548`).
   `GetCounters` reports the logically-maintained `ApproximateCount`, which tracks the
   *intended* state — exactly the number that hides the leak (109K reported vs 9.1M
   actual in the incident).

## Constraints (hard requirements)

- **Keep pooling.** Upstream's fix un-pools `PooledSet` (plain allocations, no-op
  `Dispose`) and stops repooling structurally removed B-tree nodes. Rejected here:
  `PooledSet` buckets are created/disposed per message when index keys churn
  (`Indexing.cs:250,262`), so un-pooling is a hot-path allocation regression.
- **No hot-path regression.** No new atomic ops, fences, branches-with-cost, or
  allocations on currently-correct paths. Verified by before/after BenchmarkDotNet runs
  (`[MemoryDiagnoser]`): no throughput regression outside noise, zero new allocs/op.
- **Concurrent readers are a supported contract** (decided): queries execute lock-free
  on app threads while the single Kafka worker writes. Reclamation must therefore be
  reader-safe — this is the piece upstream solved by un-pooling; we solve it with
  deferred reclamation instead.

## Design

### 1. `PooledBTree` correctness — fast path preserved

`Remove`, `Contains`, and `Add`'s duplicate check keep today's code as the fast path:
single rightmost descent (`FindLeafWithPath` / `FindLeaf`) + `LeafLowerBound` +
`FindExact`. Key invariant that makes the miss check free: **if `LeafLowerBound(leaf,
index) > 0`, a key `< index` precedes the run inside this leaf, so the run cannot extend
into earlier leaves — the fast-path miss is definitive.** The slow path triggers only
when the fast path misses AND `pos == 0` AND `leaf.Prev` ends with an equal key (one
extra comparison on an already-loaded value):

- `Remove` slow path: recursive `RemoveFromSubtree` DFS (ported from upstream) over the
  candidate child range `[FindChildIndexLeft(node, index), FindChildIndex(node, index)]`,
  keeping `ancestors`/`childIndices` valid for structural cleanup of whichever leaf holds
  the pair. Bounded by `MaxDepth`. `[MethodImpl(NoInlining)]` — cold.
- `Contains` / `Add` duplicate-check slow path: walk `leaf.Prev` while the previous
  leaf's last key compares equal, scanning each leaf's tail run. No path bookkeeping
  needed (no structural change). `Add` keeps rightmost descent and tail insertion —
  insertion order among equal keys is preserved as today (upstream switched to head
  insertion; we don't need to).

`RangeFromExclusive` and `RangeCustom(includeFrom: false)` are restructured as: prologue
loop advances leaves while `LeafUpperBound(leaf, start) == leaf.Count` (leaf entirely
`<= start`); once a leaf has `pos < Count`, every subsequent key in the chain is
`> start`, so the main loop is today's tight loop unchanged, `pos = 0` on advance. Zero
steady-state cost (the no-run case executes exactly today's instructions); no
per-iteration `pastRun` branch (simpler than upstream).

Unique-key workloads and single-leaf duplicate runs never enter any slow path.

### 2. `ReaderGate` — asymmetric deferred reclamation (new utility)

New internal class `ReaderGate` (`src/Prague.Core/Collections/ReaderGate.cs`, shared by
`PooledBTree` and `PooledSet` scoped readers):

- **Reader side (zero atomic ops):** each reader thread owns a cache-line-padded slot
  (`ThreadLocal`-registered, process-lifetime; slots of dead threads are leaked — a
  dead thread's slot reads 0 forever, so leaks are harmless and bounded by peak thread
  count). Pin = plain stores `slot.Depth++; slot.Sequence++`; unpin = plain store
  `slot.Depth--` (only the owning thread writes its slot; nested reads supported). No
  `Interlocked`, no fence.
- **Writer side (rare, batched, already under the structure's write lock):** retired
  nodes/arrays go to a limbo list owned by the writer. When parking a batch, the writer
  calls `Interlocked.MemoryBarrierProcessWide()` and snapshots the `(Depth, Sequence)`
  of every currently-pinned slot into the batch header. A batch is reclaimable once each
  snapshotted slot has either dropped to `Depth == 0` or advanced its `Sequence` (that
  reader unpinned at least once since the batch was parked; readers pinning *after* the
  barrier observe the unlink — standard RCU grace-period argument — so they can never
  reach the parked nodes and do not block reclamation). This makes drains immune to
  starvation under continuous overlapping reader traffic: global quiescence is not
  required, only per-reader progress.
- **Drain triggers:** attempted on every retire while limbo is non-empty (the barrier
  cost only applies then; empty limbo costs one null check), and on `Dispose`. Limbo is
  therefore bounded by the churn that occurs within one grace period.
- Fail-open by construction: a reader thread stuck inside a scan delays reclamation; it
  can never cause reuse-under-reader.

Gate users (scoped, synchronous, never escape the call): `PooledBTree.Range`,
`RangeFrom`, `RangeTo`, `RangeFromExclusive`, `RangeToExclusive`, `RangeCustom`,
`TryGetMin`, `TryGetMax`, `Contains`; `ValueSet.IntersectWith(PooledSet)` (both
variants); `PooledSet.Contains`/`ContainsWithHashCode` (reader-side callers).
Writer-side calls (under the write lock / single Kafka thread) never pin.

`PooledBTree.RemoveEmptyLeaf` / `RemoveFromParent` keep pooling: retire goes through the
gate's limbo instead of calling `ReturnToPool()` directly. A removed leaf's own
`Next`/`Prev` stay intact until actual reclamation, so a parked reader continues the
chain correctly (documented staleness: it may re-see or miss the removed entries,
exactly as today's model allows).

### 3. `PooledSet` — generation (`Tables`) snapshot, pooled arrays, refcount for escaping readers

Adopt upstream's `Tables` structure (all reader-visible state — `Buckets`, `Slots`,
`FreeNext`, `Size`, `FastModMultiplier`, `LastIndex` — behind one reference published
with a single volatile store), which closes the pre-existing out-of-bounds race where a
reader mixes old arrays with new bounds across `Grow`. Differences from upstream:

- **Arrays stay ArrayPool-rented.** `Tables` carries a packed state word:
  `pins (bits 0..29) | Retired (bit 30) | Returned (bit 31)`.
  - Acquire (escaping readers only): `Interlocked.Increment` on the state word, then
    check `Retired`; if set, decrement and re-read `_tables` (the backoff touches only
    the GC-managed `Tables` object, never the arrays). The owner's implicit pin (initial
    state = 1) makes acquire-vs-retire sound.
  - Release: `Interlocked.Decrement`; transition to `pins == 0 && Retired` returns the
    arrays exactly once via CAS to `Returned`.
  - `Grow` builds the new `Tables` from freshly rented arrays, publishes it volatile,
    then retires the old generation (releasing the owner pin) — fixing today's permanent
    leak of grow arrays. `Dispose` retires the current generation and becomes safe to
    call with outstanding enumerators.
- **Who pins:** only escaping readers — the struct `Enumerator` (its pin *replaces* the
  existing `Interlocked.Increment` in `GetEnumerator`, so no new cost) and the boxed
  `BoxedEnumerator` (gains the pin it fatally lacks today; this path already allocates).
  Scoped readers (`IntersectWith`, `Contains`) use the `ReaderGate` — zero cost.
  Writer ops (`Add`/`Remove` on the single writer thread) touch `_tables` directly, no
  pin: the writer cannot race its own retire.
- **In-place mutation safety under concurrent readers** (ported from upstream,
  near-free): publication order `Next` → `Value` → volatile `HashCode` → reachability
  (`LastIndex` for enumerators / bucket head for chains); acquire-read of `LastIndex` in
  snapshot capture; free-list links moved out of slots into `Tables.FreeNext` so a chain
  reader parked on a removed slot still reaches its live tail.
- **Version-guarded copy-out only where torn reads are possible.** Guard compiled per
  instantiation behind `static readonly bool AtomicCopy = IsReference<T> ||
  (!ContainsReferences<T> && Unsafe.SizeOf<T>() <= 8)` — JIT-folds to a constant.
  Atomic-copy `T` (`long`, `int`, `string` — the common Prague key types): no `Versions`
  array allocated, no version reads, enumeration loop identical to today's. Non-atomic
  `T` (e.g. `Guid`, composite key structs): `Versions` rented alongside slots; writer
  bumps the slot version before remove/reuse; readers copy out under version recheck
  (ABA-safe; same documented ARM64 residual window as upstream).

Failure containment: an abandoned (never-disposed) boxed enumerator pins its generation
forever — those arrays are eventually GC'd instead of re-pooled. Degraded pooling, never
corruption.

### 4. `CacheRangeIndex` observability

`GetCounters` returns `(ulong)_index.Length` (an O(1) field) for both outputs instead of
`ApproximateCount`, so index drift is visible in statistics rather than masked by them.
`ApproximateCount` add/remove bookkeeping is deleted (as upstream).

### 5. Contract documentation

Update the `PooledBTree` and `PooledSet` header comments to state the actual model:
single writer serialized by lock / ownership; lock-free readers with documented
staleness (transient skip/double-see during in-leaf shifts; snapshot-stale views);
reclamation deferred via `ReaderGate` / generation pins so readers never observe
recycled memory.

## Out of scope

- Right-sizing `PooledSet`'s initial capacity (127 slots is oversized for singleton
  buckets) — potential follow-up win, benchmarked separately.
- Striped/global gate tuning beyond one process-wide slot registry.
- The upstream `vlaues` parameter-name typo (cosmetic; may fix in passing).

## Testing

Already ported and failing (must flip green, unmodified):
- `tests/Prague.Generated.Tests/Cache/PooledBTreeTests.cs` — 7 duplicate-run tests
  (Remove/Contains/Add/RangeFromExclusive/RangeCustom/UpdateChurn), all failing today.
- `tests/Prague.Core.Tests/DataStructures/PooledSetLifetimeTests.cs` — 5 lifetime tests,
  2 failing today (`BoxedEnumerator_DisposeMidIteration_*`). The two tests use
  `set.Slots`; they will be updated to the new snapshot accessor as part of the rewrite.

New tests:
- `ReaderGate` unit tests (pin/unpin nesting, drain-only-when-quiescent, immediate
  return when no readers registered).
- `Tables` state-word protocol tests (acquire-after-retire backoff, exactly-once return,
  grow retirement returns old arrays, dispose with outstanding enumerator defers return).
- Concurrency stress tests (short, CI-safe, plus longer `[Explicit]` variants): single
  writer churning adds/removes/grows vs. parallel readers doing range scans /
  enumerations / intersects; assert no foreign values, no exceptions, bounded
  `Length`/`Count`.
- Full existing suites (`Prague.Tests.slnf`) stay green.

## Benchmarks (acceptance gate)

Baseline = `main`, candidate = fix branch; BenchmarkDotNet with `[MemoryDiagnoser]` on
the dev machine (Apple Silicon) and, if available, x64 Linux:

- `PooledBTreeBenchmarks` (existing) + new cases: unique-key add/remove churn,
  duplicate-run add/remove churn, range scans (inclusive + exclusive bounds),
  `TryGetMin/Max`, mixed write+scan.
- New `PooledSetBenchmarks`: add/remove/contains, struct + boxed enumeration, grow,
  `ValueSet.IntersectWith`.
- New end-to-end `CacheRangeIndexChurnBenchmarks`: the incident pattern
  (`Update` with shared timestamp keys).

Acceptance: no throughput regression outside run-to-run noise on currently-correct
paths; **zero new allocs/op** on those paths; pool return behavior demonstrably intact
(e.g. steady-state churn shows no unbounded rent growth). Results table included in the
PR description.

## Delivery

Single PR: source changes + ported/updated tests + new tests + benchmarks + results
table. Reference upstream PR #56 in the description.

# Single-hash index maintenance for InMemoryDataCache — design

**Date:** 2026-07-20
**Status:** approved

## Problem

Every `InMemoryDataCache` mutation hashes the main key `TKey` once in the primary store, then
re-hashes the *same key* in every index that stores it:

| Site | Function | Cost per mutation |
|---|---|---|
| `ConcurrentCacheStore.AddOrUpdate` → `GetHashCode(key)` (`ConcurrentCacheStore.cs:63`) | `DefaultKeyComparer<TKey>` | 1 (stored in `Node.Hashcode`, then discarded) |
| Each set index (`CacheKeyValueListIndex` & variants, `CacheKeySetIndex`) → inner `PooledSet<TKey, DefaultKeyComparer<TKey>>.Add/Remove` (`PooledSet.cs:348,400`) | **identical** `DefaultKeyComparer<TKey>` | 1 on Add/Remove, **2 on Update** (Add to new group + Remove from old) |
| Each btree index (`CacheRangeIndex` → `PooledBTree.Add/Remove/Update`) → `HashOf(value)` (`PooledBTree.cs:719`) | raw unmixed `GetHashCode()` (value types) / DJB2 (strings) | 1 on Add/Remove (+1 redundant recompute at `PooledBTree.cs:734`); 1–2 on Update (`PooledBTree.Update` locked move — exact count verified during implementation) |

For a model with a composite `(int,int)` PK, 2 set indexes, and 2 btree indexes, one Add hashes
the key 5×; one Update that moves every index group hashes it 7–9×. The read path already avoids
this (`ValueSet.UnionWith` consumes `PooledSet.Enumerator.CurrentHashCode` into
`AddIfNotPresent(item, hash)`, `ValueSet.cs:283`); the write path never got the same treatment.

Foundation: after the 2026-07-20 comparer cleanup, `ConcurrentCacheStore` hashes exclusively via
`DefaultKeyComparer<TKey>` — the same struct `PooledSet`/`ValueSet` use — so the store's hash is
bit-identical to what the set indexes recompute.

## Decisions (user-approved)

1. **Thread the store's mixed hash; un-mix at the btree.** `DefaultKeyComparer` and
   `ValueDictionary` stay untouched. (Rejected: "raw hash everywhere" — comparer returns raw,
   pow2 consumers self-mix — cleaner end-state but wider audit for identical benchmark numbers.
   Rejected: switching the btree tiebreak to the mixed hash — regresses the documented
   monotonic-append fast path for equal-key ascending-id batch inserts, `PooledBTree.cs:286-290`.)
2. **All mutation paths:** Add + Update + Remove. Shared plumbing; Update is the biggest winner.
3. **Benchmark first, then the change** — baseline numbers recorded on current code, delta
   reported after.
4. **Marvin32 becomes the only string hash; the btree's DJB2 tiebreak is retired.** DJB2 won the
   old comparison "compute DJB2 vs compute Marvin" (both O(length)); under threading the
   comparison is "compute DJB2 vs use the already-computed Marvin hash for free" — DJB2 loses.
   (Rejected: DJB2 in the main cache/comparer — non-randomized hashing on Kafka-fed, externally
   produced keys is a HashDoS downgrade, and the flood-rehash escape hatch that made DJB2 safe
   in the BCL was deliberately deleted by the comparer cleanup. Post-threading the payoff would
   be a fraction of one O(n) pass per mutation.)

## Design

### 1. Hash source — `ConcurrentCacheStore`

The store already computes the hash and keeps it in `Node.Hashcode`. Surface it:

- `UpdateResult` gains an `int KeyHash` field (readonly struct; stack-only, no allocation).
- Internal `TryRemove(TKey key, out TValue value, out int keyHash)` overload.
- Internal pre-hashed entry points mirroring the existing public ones (the private internals
  `TryAddOrUpdateInternal`/`UpdateOrRemoveInternal` already take `int hashcode`), for stores
  keyed by `TKey` (e.g. the global last-update index's inner store).

### 2. Interface — `ICacheIndex<TKey,TValue>`

Clean cutover of the three internal members (no overload accumulation):

```csharp
internal void Add(TKey key, int keyHash, TValue value, long timestampMs);
internal void Remove(TKey key, int keyHash, TValue value, long timestampMs);
internal void Update(TKey key, int keyHash, TValue originalValue, TValue newValue, long timestampMs);
```

`keyHash` is always `DefaultKeyComparer<TKey>.GetHashCode(key)` — the store's form (Fibonacci-
mixed for value types, raw Marvin32 for strings, raw for other ref types). All 9 implementors
are in-repo (`InMemoryDataCache.cs`, `Indexing.cs`); Prague.Codegen emits none. Implementors
keyed by group/index keys (`CacheUniqueIndex`, the four `LastUpdated*` adapters) ignore the
parameter. Implementors storing the main key consume it:

- `CacheKeyValueListIndex`, `CacheSymmetricKeyValueListIndex`,
  `CacheCollectionSymmetricKeyValueListIndex` → pre-hashed inner `PooledSet` calls
- `CacheKeySetIndex` → pre-hashed `PooledSet` calls
- `CacheRangeIndex` → pre-hashed `PooledBTree` calls

`InMemoryDataCache.AddOrUpdate/Remove` capture the hash from `UpdateResult.KeyHash` /
`TryRemove(..., out keyHash)` and pass it through the `_indeces` fan-out loop.

### 3. `PooledSet` — pre-hashed entry points

Internal `Add(T item, int hashCode)` / `Remove(T item, int hashCode)`: identical bodies to the
existing methods minus the `GetHashCode(item)` call. Existing methods delegate to them.
`Debug.Assert(hashCode == GetHashCode(item))` guards caller consistency. Valid only when the
caller hashed with the set's own comparer — all index call sites are
`PooledSet<TKey, DefaultKeyComparer<TKey>>`, satisfied by construction.

### 4. `PooledBTree` — Marvin unification + pre-hashed entry points with un-mix

**`HashOf(TValue)` loses its string special-case** (decision 4): strings fall through to the
ref-type branch (`value.GetHashCode()` = Marvin32). The method collapses to: value types → raw
`GetHashCode()`; ref types → `GetHashCode()`; null → 0. Placement inside duplicate-key runs
changes, which is behavior-neutral — run-internal order is documented unspecified, and Marvin's
per-process randomization is irrelevant to an in-memory tree. The monotonic-append correlation
for value types (the reason `HashOf` skips the Fibonacci mix) is untouched.

Internal pre-hashed `Add/Remove/Update(..., int keyHash)` variants recover today's exact raw
hash from the threaded store hash (branch JIT-folded per closed generic):

- `TValue.IsValueType` → `raw = (int)((uint)keyHash * FibonacciInverse)`. The mix
  (`raw * 2654435769U` mod 2³²) multiplies by an odd constant, hence is a bijection; the modular
  inverse recovers `raw` bit-exactly. One multiply replaces a full `GetHashCode()`.
- Ref types (including strings) → `DefaultKeyComparer` applies no mix to them; `keyHash` **is**
  the raw hash. Used directly.

The `FibonacciInverse` constant lives beside `2654435769U` (in `Utils`, next to the mix's home)
with a unit test asserting `unmix(mix(h)) == h` over boundary values and a random sweep.

Follow-ons folded in:
- Fix `PooledBTree.cs:734`: recomputes `HashOf(value)` although `valueHash` is already in a
  local — use the local.
- `StringTools.GetNonRandomizedHashCode` (DJB2): delete if `PooledBTree` was its last consumer
  (grep at implementation time; the comparer cleanup kept it solely for this tree).

### 5. Benchmark — `benchmarks/Prague.Benchmarks/CacheIndexMaintenanceBenchmarks.cs`

House style: `[MemoryDiagnoser]`, explicit workload-shape comments.

Two models, same index shape — 2 set indexes (`CategoryId`, `RegionId`), 2 btree indexes
(`Timestamp : long`, `Age : int`); entities implement `ICacheEquatable<T>`/`ICacheClonable<T>`:

- **Composite PK:** `readonly record struct (int A, int B)` — the cheap-hash floor.
- **String PK:** ~24-char keys (id-like, shared prefix) — the expensive-hash ceiling, and the
  validation of decision 4: the `Age` btree index has few distinct values → heavy duplicate
  runs → composite-mode descents re-hash stored string keys, measuring the DJB2→Marvin descent
  delta. A regression here falls back to a one-line revert of the `HashOf` string branch.

Set-index cardinality: groups of ~100 (genuinely "many"). Scenarios per model:

- `AddAll`: N fresh inserts into an empty cache (fresh cache per iteration via
  `[IterationSetup]`; N via `[Params]`, default 100_000).
- `UpdateAll`: prefilled cache; re-`AddOrUpdate` every key with shifted `CategoryId`/`RegionId`/
  `Timestamp`/`Age` so the Update path moves every index group (the double-hash case).
- `RemoveAll`: prefilled cache (repopulated per iteration via `[IterationSetup]`); `Remove` every
  key — covers the third threaded mutation path.

Methodology: benchmark lands and runs on current code first (baseline), the optimization lands
second, same benchmark re-run; both result tables recorded in the PR/changelog.

Expectation (honest): `(int,int)` gains measurable but modest (its hash is `HashCode.Combine`-
class); string-PK `UpdateAll` is where the win compounds. The benchmark decides.

**Benchmark verdict (2026-07-20, M4 Pro, N=100k, two confirming runs):** composites won
(AddAll −8.0%, UpdateAll −9.0%, RemoveAll −0.6%) but all string scenarios regressed
(AddAll +13.9%, UpdateAll +18.8%, RemoveAll +55.7%) — the Marvin32 stored-value descent
cost in duplicate runs swamps the saved incoming hash. Tripwire held; nothing shipped.

### 6. Leaf-stored hashes (decision 5, user-approved — replaces the DJB2-revert fallback)

`PooledBTree` materializes the tiebreak hash next to each stored value, eliminating stored-value
re-hashing entirely: composite-descent comparisons become array reads.

- `LeafNode` gains `int[] ValueHashes` (pool-rented, `LeafCapacity`); `InternalNode` gains
  `int[] SepValueHashes` (pool-rented, `InternalCapacity - 1`). Returned to the pool with their
  siblings in `ReturnToPool`.
- Invariant: `ValueHashes[i] == HashOf(Values[i])` for every live slot; separators mirror
  `SepValues`. Every write/shift/copy/split/merge of `Values`/`SepValues` moves the hash array
  in lockstep; hashes ride the existing publication order (written before the `Count`/link
  stores that make a slot reader-visible), inheriting the documented staleness model unchanged.
- Every `HashOf(<stored expression>)` call site is replaced by the corresponding array read
  (internal-node composite child search, leaf composite lower bound, `TryFindPair` probes,
  backwards prev-leaf scans, append-fast-path last-element check, `RepairPath`). After this,
  `HashOf` runs only on INCOMING values: public entry points and `Contains`.
- At insert, the hash is already in hand (threaded, or computed once in the public wrapper) —
  stored, never recomputed.
- Unique-key trees carry the arrays without reading them: +4 bytes/entry always-on. Lazy
  materialization at the duplicate transition was rejected — it would need an O(n) backfill
  pause under the write lock.
- With decision 4 (Marvin threaded in) plus this, the string btree mutation path computes zero
  string hashes.

## Non-goals

- `DefaultKeyComparer` / `ValueDictionary` unchanged (decision 1).
- Group-key (`TIndexKey`) hashing in index-owned stores — different key per index, nothing to share.
- Read/query paths — already hash-reusing where it matters.

## Risks & verification

- **Hash/function mismatch** (caller threads a hash produced by a different function): guarded by
  `Debug.Assert` in every pre-hashed entry point; index call sites use `DefaultKeyComparer`
  stores by construction.
- **Un-mix constant wrong**: pinned by the roundtrip unit test before anything consumes it.
- **String tiebreak switch reorders duplicate runs**: legal per the documented "order within a
  run is unspecified"; `PooledBTreeDifferentialTests` are order-agnostic against a reference
  model — they must pass unchanged. Any test found pinning run-internal order is a test bug.
- **Interface change fallout**: all implementors in-repo; tests calling the internal members
  directly (Indexing test suites) are updated with the change.
- **Behavioral regression**: existing index/differential test suites
  (`PooledBTreeDifferentialTests`, `CacheCollectionKeyValueListIndexTests`, hashing tests) must
  pass unchanged — the change is invisible except for fewer hash computations and the
  run-internal-order change above.

## Follow-up candidates (out of scope, recorded 2026-07-21)

- **Node-shell pooling through ReaderGate:** PooledBTree pools arrays but allocates
  LeafNode/InternalNode shells per split. The existing post-grace `ReclaimToPool` hook is the
  safe recycling point for the shells themselves (a bounded freelist per closed generic).
  Benchmark-gated; touches the reclamation path hardened here. **Attempted and rejected
  2026-07-21:** allocations dropped to 0 B/op on every BTreeChurn scenario, but batch-stamped
  churn time regressed +5.7% (reproduced) — recycled scattered shells lose the Gen0 locality of
  fresh allocations. Working patch preserved in the task-14 report; retry only with a
  locality-aware design. (Prompted by reviewing a
  skip-list multimap alternative — rejected for lock-free-reader UAF on pooled nodes, mixed
  CAS/plain-write value chains, remove livelock after marking, no range API, O(run) duplicate
  removal, ~10x per-entry memory. Its optimistic lazy-locking recipe is only relevant if the
  single-writer-per-cache model ever changes; the B+tree analogue would be B-link, not per-node
  locks.)
- **`UpdateOrRemoveResult.KeyHash`:** Task 3 surfaced the hash on `UpdateResult`/`TryRemove`
  only; `LastUpdatedIndex.Remove` still recomputes its group-key hash once (cost identical to
  pre-change).

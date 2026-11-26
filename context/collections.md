# Internal collections

> **Read when:** touching pooled sets/dicts, intersection primitives, or anything that rents from `ArrayPool` on a query/join hot path.

## PragueArrayPool&lt;T&gt; — the pool hook (leak testing)

Every pooled type rents via `PragueArrayPool<T>.Pool` (never `ArrayPool<T>.Shared` directly — a
compliance grep should find zero direct `Shared` uses in Core outside `PragueArrayPool.cs`). In
production the hook is a `static readonly` alias of `Shared` (same JIT shape, zero cost); tests
install a `TrackingArrayPool` provider process-wide (`Prague.Core.Tests/Infrastructure/`,
global `LeakTrackingSetup` fixture) that ledgers every Rent/Return, catches leaks AND
double-/foreign returns. `LeakAssert.Balanced(scenario)` runs a scenario twice (first pass warms
never-returned statics like `PooledSet<T>.Empty`), quiesces (GC + finalizers + `ReaderGate.TryDrain`
×2), and asserts zero new outstanding arrays. Leak tests live in `Prague.Core.Tests/Leaks/`.
When adding a Rent site, add/extend a leak test — exception paths included (user
`CompareTo`/`Equals`/`GetHashCode`/filters can throw mid-operation).

## ValueSet&lt;T&gt;

Pooled set; `Dispose()` returns the rented array. Surface: `RetainOnly`, `IntersectWith`, `UnionWith`, `RetainNonNullSlots`, `IntersectWith<TKey,TInto>(…)`.

**Slot addressing:** `AddOrFind(item, out slot)` reports the slot the item occupies (new or existing), `ValueAt(slot)` reads it back by ref, and `Enumerator.CurrentSlot` reports the slot of `Current` during a walk — the same numbering `AddOrFind` handed out. Slots are dense while only adding, survive growth out of inline storage, and are never renumbered by in-place removals (a removal leaves a hole the enumerator skips). Callers can therefore keep per-item side data in a parallel array indexed by slot and reach it again from an enumeration without a second hash — the JoinMany fan-out does this for its right → extra-lefts chains (see [`joins.md`](joins.md), "JoinMany fan-out"), delivering through `IJoinedSlotResultContainer.Add(foreignKey, slot, value)`. `IndexOf(item)` is the test-side stand-in for naming a slot without enumerating; production code reads `CurrentSlot`. Pinned by `Prague.Core.Tests/Collections/ValueSetEnumeratorSlotTests` (inline → heap growth, holes from removals).

**Pool-corruption caveat:** `Dispose()` has a defensive `catch(ArgumentException)` around `ArrayPool.Return`, so a double-return is *swallowed* — **but it corrupts the pool** (same buffer enters the bag twice; two callers both Rent it). Exactly-one-Dispose must be enforced by callers. Joins do this with the `handedOff` guard — see [`joins.md`](joins.md).

## ValueDictionary&lt;TKey,TValue&gt;

**`internal`** struct (was public; made internal by converting `SortResults` to explicit interface impls + deleting dead code — `Result<T>`, `Test`, `IChainedResolvers`). It's still forced into public *generic signatures* across resolver-chain infra, but the type itself is internal.
- `Intersect(HashSet<TKey>)` — compact-and-rebuild intersect (matching entries compacted via write pointer, metadata table rebuilt with `Array.Fill(-1)` + re-probe). Zero temp alloc.
- `Filter<TFilter>(TFilter)` where `TFilter : struct, IValueDictionaryFilter<TKey,TValue>` — compact-and-rebuild keyed on `filter.Keep(ref value)`; JIT-inlined per closed generic. Backs `RetainNonNullSlots`, and `RetainRows` / `PruneRows` through `RowKeyFilter` (a key predicate holding a copy of a `ValueSet` / `ValueDictionary` — the copy shares the storage and is only read).
- **Two `Dispose` overloads:** `Dispose()` returns metadata + keys only — the values array is the hand-off payload (result maps, `SortResults`). An owner that never hands its values on must call `Dispose(withValues: true)`, otherwise a pooled values array leaks on every call.

## PooledSet&lt;T&gt; / LeftKeySetView&lt;T&gt;

- `PooledSet<T>` — `internal sealed class`. Single writer, lock-free readers. All
  reader-visible state lives in a volatile-published `Tables` generation whose arrays
  stay ArrayPool-rented: escaping readers (the enumerators `GetValues` hands out) pin
  the generation via a refcount; scoped readers (`Contains`, `ValueSet.IntersectWith`)
  are covered by `ReaderGate`'s grace period; the last release hands the arrays to the
  gate, never straight to the pool — readers can never observe recycled memory.
  Version-guarded copy-out compiles only for multi-word `T` (`AtomicCopy` JIT-folds it
  away for `long`/`int`/`string` keys). `Dispose` publishes a shared never-retired
  sentinel generation before retiring the old one (publish-then-retire, like `Grow`),
  so reads on a disposed set are safe and empty; it is idempotent and safe with
  outstanding enumerators. The ref struct enumerator pins via the per-thread
  `ReaderGate` slot (no shared-line Interlocked); only the boxed enumerator refcounts
  its generation.
  **Reclaim clears `[0, LastIndex)`, not the array.** `Tables.ReclaimToPool` used to
  return `Slots` with `clearArray: true` for reference-containing keys: O(array length), under
  the gate's process-wide `_limboLock`, so one large string-keyed generation held up every other
  writer's `Retire` for the length of the wipe — even when the set had been emptied and
  every slot was already clear. A slot at or past `LastIndex` holds no reference: either it was never written,
  or `Remove` cleared it when it freed it (`_clearOnFree`) before `LastIndex` fell back to zero.
  So `Array.Clear(Slots, 0, LastIndex)` leaves no reference behind: an emptied bucket
  (`LastIndex` back at 0) clears nothing, and a grown generation skips the power-of-two tail. The
  invariant is load-bearing now — **no slot at or past `LastIndex` may hold a reference** — and
  it is local: `Tables` is the only renter of `HashSlot<T>[]`. What still clears under the lock
  is a reference-keyed generation retired by `Grow` (it is full, so all `Size` slots) and a
  non-empty set that is disposed (up to its `LastIndex`). Pinned by `Prague.Core.Tests/DataStructures/PooledSetReclaimClearTests`, which
  keep the array and read it after it went back to the pool.
  **`CapacitySlots` is a logical capacity.** It reads `Tables.Size` of the live generation, and
  the index aggregate (`GetCapacitySlots`, the `prague.kafka.cache.index.capacity` instrument) sums
  those. It is neither the array length — 293 table slots sit in 512-slot arrays — nor everything
  still rented: a retired generation counts for nothing while it waits in the gate or under a
  boxed enumerator's pin. `index.size{kind="values"} / index.capacity` is therefore the fill of
  the tables, which is what the metric is for; do not read bytes off it. The `values` series, not
  `keys`, and only where the capacity is above zero: `GetCapacitySlots` defaults to 0 for index
  kinds without pooled sets (`Unique`, `Range`), and the reporter publishes that 0. One index kind
  reads half: `CacheCollectionSymmetricKeyValueListIndex` (what the generator builds for a `Many`
  index on a collection property) writes every link into `Forward` and into `Reverse`, sums the capacity of both, and
  takes its counters from `Forward` alone. That is left as it is and documented. Counting only
  `Forward`'s slots would hide `Reverse`, the half with many keys and few values each — the slack
  the metric exists to show; doubling `values` would change what `index.size{kind="values"}`
  means for this index (links) in every dashboard that already reads it.
  The numerator was wrong until 2026-11: a `Many` index mutates a bucket in place and then
  compared the bucket's `Count` "before" and "after" — one object — so `values` rose by one when
  a bucket was created and never moved again, and `keys` went negative on an index whose key
  changes through `Update` (buckets made there were not counted, but were subtracted on removal).
  `CacheKeyValueListIndex` now counts inside the store callbacks of `Update`, `AddUnderKey` and
  `RemoveUnderKey`: `values` from what `PooledSet.Add` / `Remove` return, `keys` when a bucket is
  created or disposed. `Update` keeps callbacks of its own: one pair shared with the add and
  remove paths measured up to 4 % slower on updates (see `RESULTS.MD`, 2026-11-25). Pinned by `Prague.Core.Tests/Indexing/ListIndexCounterTests`.
  A bucket never shrinks: a `Many` bucket keeps the generation of its largest moment until it
  empties and is disposed. A shrink-on-remove was built and dropped (2026-11): on the caches it
  was meant for, tables were 35–59 % full — a table that doubles is a little under half full
  right after it grows — so it would never have fired, while it cost 6–7 % on `AddRemoveChurn`. What does leave slots idle is the
  default first generation of 59 on an index whose keys hold a value or two each; that is what
  `InitialCapacity` is for.
  **Duplicates are the one shape the staleness model admits.** A walk (enumerator or
  `CopyKeysTo`) visits slots in order; a key removed and re-added while it is in flight is
  re-inserted wherever the LIFO free list points (a higher freed slot, or `LastIndex`), so a walk
  that already yielded it yields it again. Consumers that collect into a set are immune; a flat
  buffer is not. The writer therefore keeps a plain `_epoch`, bumped before every structural
  mutation, and `CopyKeysTo` returns whether it was unchanged across the walk (acquire load
  before, full fence + load after — two loads and one fence per walk, never per key): `true` is
  a proof of one distinct key per slot, `false` means "may contain a repeat, dedupe if you need
  set semantics". The pipeline's single-bucket seed is the consumer that acts on it
  (`PipelineStep.SeedBucket` → `DedupeSeedFrom`, cold). Pinned by
  `Prague.Core.Tests/DataStructures/PooledSetCopyKeysChurnTests` — a sink that plays the writer
  from inside the walk reproduces the duplicate deterministically.
- `ReaderGate` (`src/Prague.Core/Collections/ReaderGate.cs`) — process-wide
  grace-period reclamation shared by `PooledSet` and `PooledBTree`: readers pin with
  padded per-thread slots (two stores + one local fence, no RMW); writers park retired
  memory in a limbo batch stamped with pinned slots' sequence numbers and reclaim once
  each has unpinned (store-buffer-litmus fences on both sides; slots recycle through a
  finalizer-backed free list).
- `LeftKeySetView<T>` (`src/Prague.Core/LeftKeySetView.cs`) — `public readonly struct`, single `internal PooledSet<T> Set` field, internal-only ctor, **zero public members**. Exists only so the JoinOne LeftSym resolver's generic signatures can appear publicly without CS0703 (the JoinMany resolvers carry plain `TLeftKey` pairs — see `joins.md`, "JoinMany fan-out"). Inside Core it's reinterpreted back via `Unsafe.As<LeftKeySetView<T>, PooledSet<T>>(ref view)` — zero-cost (identical single-reference layout). **Do not add public members** unless intentionally exposing set contents.

## IncrementalIntersecter&lt;TKey,TInto&gt;

Stackalloc bitmap mark-and-sweep (falls back to `ArrayPool` above `StackAllocThreshold`). Accumulates marks across lookups (UNION), sweeps unmarked slots on `Dispose` (INTERSECT against the union). Used for multi-value index intersection and per-branch OR filtering without a temp `ValueSet`.

## JoinedKeyPair&lt;TLeft,TKey&gt;

Pair type stored in the paired join core. `Equals`/`GetHashCode` consider **only `.Key`** — intersection works by key alone; multiple lefts sharing a key collapse to one pair. `.IntoTrait` provides the mark-and-sweep trait for `IncrementalIntersecter`.

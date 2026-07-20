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

**Pool-corruption caveat:** `Dispose()` has a defensive `catch(ArgumentException)` around `ArrayPool.Return`, so a double-return is *swallowed* — **but it corrupts the pool** (same buffer enters the bag twice; two callers both Rent it). Exactly-one-Dispose must be enforced by callers. Joins do this with the `handedOff` guard — see [`joins.md`](joins.md).

## ValueDictionary&lt;TKey,TValue&gt;

**`internal`** struct (was public; made internal by converting `SortResults` to explicit interface impls + deleting dead code — `Result<T>`, `Test`, `IChainedResolvers`). It's still forced into public *generic signatures* across resolver-chain infra, but the type itself is internal.
- `Intersect(HashSet<TKey>)` — compact-and-rebuild intersect (matching entries compacted via write pointer, metadata table rebuilt with `Array.Fill(-1)` + re-probe). Zero temp alloc.
- `Filter<TFilter>(TFilter)` where `TFilter : struct, IValueDictionaryFilter<TKey,TValue>` — compact-and-rebuild keyed on `filter.Keep(ref value)`; JIT-inlined per closed generic. Backs `RetainNonNullSlots`.

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
- `ReaderGate` (`src/Prague.Core/Collections/ReaderGate.cs`) — process-wide
  grace-period reclamation shared by `PooledSet` and `PooledBTree`: readers pin with
  padded per-thread slots (two stores + one local fence, no RMW); writers park retired
  memory in a limbo batch stamped with pinned slots' sequence numbers and reclaim once
  each has unpinned (store-buffer-litmus fences on both sides; slots recycle through a
  finalizer-backed free list).
- `LeftKeySetView<T>` (`src/Prague.Core/LeftKeySetView.cs`) — `public readonly struct`, single `internal PooledSet<T> Set` field, internal-only ctor, **zero public members**. Exists only so the LeftSym join resolver's generic signatures can appear publicly without CS0703. Inside Core it's reinterpreted back via `Unsafe.As<LeftKeySetView<T>, PooledSet<T>>(ref view)` — zero-cost (identical single-reference layout). **Do not add public members** unless intentionally exposing set contents.

## IncrementalIntersecter&lt;TKey,TInto&gt;

Stackalloc bitmap mark-and-sweep (falls back to `ArrayPool` above `StackAllocThreshold`). Accumulates marks across lookups (UNION), sweeps unmarked slots on `Dispose` (INTERSECT against the union). Used for multi-value index intersection and per-branch OR filtering without a temp `ValueSet`.

## JoinedKeyPair&lt;TLeft,TKey&gt;

Pair type stored in the paired join core. `Equals`/`GetHashCode` consider **only `.Key`** — intersection works by key alone; multiple lefts sharing a key collapse to one pair. `.IntoTrait` provides the mark-and-sweep trait for `IncrementalIntersecter`.

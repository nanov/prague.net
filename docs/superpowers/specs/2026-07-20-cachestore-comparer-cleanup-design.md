# ConcurrentCacheStore comparer cleanup — design

**Date:** 2026-07-20
**Status:** approved

## Problem

`ConcurrentCacheStore` inherited the BCL `ConcurrentDictionary` non-randomized string-hashing
scheme: the constructor wraps default/Ordinal/OrdinalIgnoreCase string comparers in
`NonRandomizedStringEqualityComparer` (DJB2), and bucket chains longer than 100 entries trigger
`forceRehashIfNonRandomized` → `GrowTable` swaps to the randomized comparer and rehashes.

The comparer-specialization work broke that chain without removing it:

- `GetHashCode(IEqualityComparer<TKey>?, TKey)` ignores the comparer and always hashes via
  `default(DefaultKeyComparer<TKey>)` — for strings that is randomized Marvin32. The stored DJB2
  comparer never hashes anything; the flood-rehash machinery is dead weight, and chain-length
  counting is paid on every ref-type-key add for nothing.
- Write paths compare via `NodeEqualsKey` (specialized, ordinal) but read paths still call
  `comparer!.Equals(...)` — a virtual call, and a **latent correctness trap**: a caller passing
  `StringComparer.OrdinalIgnoreCase` (or any custom comparer) gets case-sensitive hashes with
  case-insensitive read equality → silent lookup misses.
- No production call site passes a comparer; all four uses are `new()`.
- `ConcurrentSortedSet._comparer` is assigned from `HashCollectionsTools` and never read.

## Decision

Remove the vestigial comparer support entirely and finish the specialization (option A of the
brainstorm; options B "realize the intent" and C "minimal consistency patch" rejected — B
reintroduces virtual dispatch for an unused feature and a pointless DoS defense, C leaves the
dead machinery in place).

## Changes

- `ConcurrentCacheStore`: drop the comparer ctor overload and comparer threading; drop
  `Tables.Comparer`; drop chain-length counting, `forceRehashIfNonRandomized`, and the
  `GrowTable` comparer-swap (nodes keep their stored `Hashcode` on resize); collapse every
  `if (typeof(TKey).IsValueType && comparer is null) { … } else { … }` pair into one path that
  hashes and compares via `default(DefaultKeyComparer<TKey>)`. Retry loops keep only the
  `tables != _tables` re-read (a table swap can no longer change the hash).
- `NonRandomizedStringEqualityComparer` class: deleted. `StringTools.GetNonRandomizedHashCode`
  **stays** (moved to `Utils/StringTools.cs`) — `PooledBTree` uses it as the composite-ordering
  tiebreak hash. The unused nested `RandomizedStringEqualityComparer` helper is deleted.
- `HashCollectionsTools`: deleted. Test usages (`ImmutableHashSet.Create(...)` seeds) switch to
  `EqualityComparer<int>.Default`.
- `ConcurrentSortedSet`: dead `_comparer` field removed.

**Resulting invariant:** `ConcurrentCacheStore` hashes and compares exclusively via
`DefaultKeyComparer<TKey>` — the same struct as `ValueSet`/`PooledSet`/`ValueDictionary` — so
hash/equals are consistent across every path, and string reads devirtualize.

**Behavioral changes:** none observable for production callers. The broken custom-comparer ctor
becomes a compile error instead of a silent-wrong-results trap.

## Tests (new: `Prague.Core.Tests`, NUnit)

1. **Oracle differential** — seeded-random add/update/remove/lookup/clear sequences over string
   keys mirrored against `Dictionary<string,int>`; identical observable state throughout.
   An `int`-key variant pins the value-type path.
2. **Growth** — insert past several resize boundaries with string keys; count and full
   retrievability verified after each doubling (exercises `GrowTable` reusing stored hashcodes).
3. **Long chain** — a ref-type key with a constant `GetHashCode` forces >100 entries into one
   bucket; lookups/removes/updates stay correct with no flood-rehash escape hatch.
   (Deterministic string collisions are impossible under per-process Marvin32 seeding; a
   constant-hash ref key pins the same chain semantics.)
4. **Hash/equals consistency** — keys equal by `Equals` find each other regardless of which
   write/read overload inserted them.

## Risks

Low — deletion of unreachable/inert paths plus mechanical branch collapse; the existing
~1950-test suite backstops. `NonRandomizedStringEqualityComparer` is public but nothing outside
`Prague.Core` references it (verified by grep; internals-visible test projects included).

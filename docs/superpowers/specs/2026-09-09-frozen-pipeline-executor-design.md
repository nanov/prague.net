# Frozen queries stage 3: the pipeline executor

> **Status:** design, branch `poc/prepared-query`, written against HEAD `a0b1ef4`. §13 steps 1–3 and 5–7 are
> shipped (step 3: the small-probe seed §3.4, the free seed §3.3 for `Count` / classic `Sort` /
> `ReorderIndexNarrowers`, the bulk `PooledSet.CopyKeysTo` seed copy, `IndexStepsExecutor` retired —
> parent spec §8 "Stage 3", RESULTS.MD "stage 3, step 3"; step 6: the `SortBounded` feed §8 for simple
> plans and for `SortBounded` → outer-`JoinOne` joined plans, the joins unfused — parent spec §8 "step 6",
> RESULTS.MD "stage 3, step 6"; step 5: `JoinOne` fusion §7.1 — parent spec §8 "step 5"; step 7: the
> frozen bounded joined container §8 — parent spec §8 "step 7", RESULTS.MD "stage 3, step 7"); **step 4
> (composites, §5) is shipped** — `Or` as a seed (default `OrSeed`: the union's own order, §5.1's
> recommendation as decided on review; `OrSeed = false`: the eager store walk kept to the branch union,
> byte-identical) and as a probe, `If` /
> `IfElse` / `Match` as bind-time arm selection with branch filters — parent spec §8 "step 4",
> RESULTS.MD "stage 3, step 4". Step 8 (cleanup) is open. Stage 2 (`FrozenOptions`, `FusedFilter`, `FrozenHints`, `IndexStepsExecutor`) was
> uncommitted in the working tree while this was written; where the design touches it, the file is
> named and the dependency called out. Line numbers are HEAD's unless marked *(wt)* for the
> working tree.
> **Parent:** `2026-09-09-prepared-query-command-design.md` (§8 `BuildFrozen()` stage 1, the
> `PointLookupExecutor`, and the stage-2 notes).

## 0. Thesis

The eager builder is optimized for being *built at the call site*: every `UseIndex` mutates a struct
core through `in`, the first step seeds a rented `ValueSet<TKey>` (`CacheQueryBuilder.cs:24,127`),
every later step intersects into it and compacts (`ValueSet.cs:1091-1110`, `1663-1677`), and
`Execute` walks the survivors a second time through the store (`CacheQueryBuilder.cs:711-739`,
`ConcurrentCacheStore.cs:317-347`). That is the right shape when the plan is discovered one call at a
time. A frozen plan already knows every step, so it can run a different algorithm:

```
Seed (smallest / first source) ─► key ─► Probes (key-side) ─► store TryGet ─► value ─► Probes (value-side)
     ─► Predicates ─► Emit (container.Add) | JoinOne right lookup | Count
```

One pass, one store lookup per candidate, no intermediate set, every other narrower turned into an
O(1) probe on the key or the value. The stage-1 `PointLookupExecutor` (`PointLookupExecutor.cs:45-103`)
is the degenerate case of this pipeline with a one-key seed; the stage-2 `IndexStepsExecutor` *(wt)*
is a half-way point that still drives the eager core. This document specifies the full pipeline, the
index APIs it needs, the exact parity rules, and the order in which to ship it.

The numbers that motivate it (Apple M4 Pro; RESULTS.MD *(wt)* lines 439-451 and the throwaway
measurement in §2.3):

| Shape (100k rows) | Eager today | Pipeline, measured by hand | Why |
|---|---:|---:|---|
| `list(1k) ∩ range(60k)` | 407 µs | 7.5 µs | range becomes a value compare instead of a 60k tree walk |
| `Or(list 1k, list 1k)` first | 1,078 µs | 17 µs | eager seeds from *all 100k rows* under all store locks |
| `list(1k) ∩ list(100)` | 9.6 µs | 1.2 µs (order-preserving, §3.4) | walk the small bucket, sort survivors by the big bucket's slot |
| `list(1k) + Where` | 8.5 µs | ~6-7 µs | no `ValueSet` copy, no rehash chain |
| `range(1k)` | 13.4 µs | 9.8 µs | keys go to a flat buffer, not a hash set |
| `JoinOne` LeftSym, 1k lefts | 32 µs | ~13 µs | right lookup fused into the same pass |

## 1. Cost model of today's engine

Every path below starts in `CacheQueryBuilderCoreCombined<TKey,TValue>` (`CacheQueryBuilder.cs:13`),
whose state is `_first` (line 24), `Candidates : ValueSet<TKey>` (25), `_filter : Predicate<TValue>?`
(20). The prepared replay (`PreparedReplay.Into`, `PreparedQuery.cs:136-149`) reproduces this state
exactly, so "eager" and "replay" share one cost model.

### 1.1 Primitive costs (measured, §2.3)

| Primitive | Cost | Where |
|---|---:|---|
| `PooledSet` ref-struct enumeration | ~5.6-7 ns / slot | `PooledSet.cs:183-216` (`Volatile.Read` of `HashCode` per slot, gate pin at `528-536`) |
| `ValueSet.Add` (hash insert, no growth) | ~5 ns | `ValueSet.cs:662-726` |
| `ValueSet` growth chain 47 → 97 → … → 1597 for a 1k seed | 5 rentals + rehash copies | `ValueSet.cs:34,531-600` |
| store `TryGetValue` (lock-free chained buckets) | ~5.6 ns / distinct key | `ConcurrentCacheStore.cs:124-145` |
| `PooledSet.Contains` incl. `ReaderGate.Enter/Exit` | ~4 ns | `PooledSet.cs:483-491`, `ReaderGate.cs:108,128` |
| `ReaderGate.Enter` + `Exit` alone | ~4 ns | `ReaderGate.cs:108-140` |
| `PooledBTree` range walk | ~0.7 ns / element (60k in 42 µs) | `PooledBTree.cs:1464-1512` |
| `IncrementalIntersecter.IntersectWith(T)` (hash probe + bit set) | ~6 ns | `ValueSet.cs:1523-1528` |
| store all-rows walk (`GetValuesInit`, **acquires every stripe lock**) | ~5 µs / 1k rows | `ConcurrentCacheStore.cs:1125-1150` |
| `CacheKeySetIndex.Contains` (a `lock` per call) | ~10 ns | `InMemoryDataCache.cs:682-686` |

### 1.2 Per-shape work today

**Unique (`UniqueEq` + filters).** Stage 1 already routes this to `PointLookupExecutor`
(`FrozenQuery.cs:140-147`): two probes, 19-27 ns. Nothing to do; the pipeline planner must keep
that routing (§9).

**List + Where (`ListEq`, `Filter`).** `UseIndexInternal(listIndex, value)` (`CacheQueryBuilder.cs:184-202`)
creates `Candidates` at the inline size 47 (`ValueSet.cs:34,68-80`) and calls
`index.IntersectValues(value, ref Candidates, add: true)` (`Indexing.cs:727-737`) →
`Candidates.UnionWith(bucket)` (`ValueSet.cs:336-339`): one bucket enumeration plus one hash insert
per key, with the 47→…→1597 growth chain for a 1k bucket (the stage-2 capacity hint removes the
chain). `WhereInternal` stores the predicate (`679-682`; a second `Where` allocates a 96 B closure
through `Compose`, `689-690`). `Execute` (`711-739`) then `Init(Candidates.Count)`, walks the set
through `TryGetValues(container, keys, predicate)` (`ConcurrentCacheStore.cs:317-347`) — a second
hash lookup per key — and `Seal(actual)`. Two passes over the keys, one hash set built and torn down.

**List + list.** Second step: `IntersectValues(value, ref Candidates, add: false)` →
`Candidates.IntersectWith(bucket)` (`ValueSet.cs:401-410` → `1091-1110`): walks the **candidate
set**, probes the second bucket with `ContainsWithHashCode` (gate-pinned, `PooledSet.cs:494-502`),
and `Remove(item)` per miss. Cost O(|candidates|), so `list(1k) ∩ list(100)` costs 9.6 µs while the
reversed spelling costs 1.3 µs (RESULTS.MD *(wt)* 439-448). Survivors keep the seed's slot order
(`RemoveAt` leaves holes the enumerator skips, `ValueSet.cs:798-849`, `1720-1737`).

**List + range — the `ListRange` row (407 µs).** Second step is `UseIndexCore` (`470-649`), the
non-first branch: `stackalloc int[100]` bitmap (530), `ValueIntersect` aggregator (914-931) over an
`IncrementalIntersecter` sized to `Candidates._lastIndex` (1503-1516), then
`index.GetValuesBetween(gte, lt, ref va)` — a **full B+tree walk of the range window**
(`PooledBTree.cs:1464-1512`), and for every one of the **60,000** keys the window contains the
aggregator calls `IntersectWith(value)` (927 → `ValueSet.cs:1523-1528`): a hash probe into the 1k
candidate set plus a bit set, with the `IndexSkip` `CompareTo` wrapper on top (`767-783`). The work
is O(|range window|), not O(|candidates|): 60k × ~6.5 ns ≈ 390 µs, plus the seed. Divided by the
1,000 candidates the query is actually about, that is the ~400 ns per candidate the row shows. The
reversed spelling (`range(60k)` first) is worse still — 984 µs — because it inserts 60k keys into a
`ValueSet` and then walks 60k candidates probing the list bucket.

**Range alone (1k window, 13.4 µs).** First-step branch of `UseIndexCore`: `ValueAdd` (891-912)
does `Candidates.Add(value)` per element, so a 1k window pays the growth chain and 1k hash inserts
(~10 µs measured) before the 1k `TryGet` walk. The tree walk itself is 4.7 µs.

**Or.** `OrWith` (`64-108`): when the Or is the first narrowing, it **auto-seeds from every row in
the store** (`73-79`, `EnumerateAllValuesInit` → `GetValuesInit`, which takes all stripe locks) into
`Candidates`, then each branch runs in intersecter mode marking bits on a bitmap over that set
(bucket walks, `Indexing.cs:705-712`), the bitmaps are unioned (96) and the set compacted (101). For
100k rows that is 1.08-1.36 ms regardless of how selective the branches are — the `Or` row in
RESULTS.MD. Encounter order is the *store's hash-bucket order*, not either branch's.

**If / Match.** The condition or tag selector runs per replay (`Narrowers.If.cs:29`,
`Narrowers.Match.cs:147-148`); an inactive branch simply does not call the core, so `_first` stays
set and the next step seeds. `IfTaken` (list then unique) costs the whole 1k bucket copy (3.1 µs)
to return one row: `IntersectValue(value, ref Candidates, add:false)` → `ValueSet.IntersectWith(T)`
(`367-380`) clears the set and re-adds the single survivor.

**List + JoinOne (LeftSym, 32 µs).** Base walk as above, then `ExecuteJoins` (`ResolverChain.cs:197-203`)
→ `JoinOneLeftSymResolver.UnsafeExecuteWithAccessor` (`JoinOneResolver.cs:616-655`): builds a
`ValueSet<JoinedKeyPair<…>>` from every left key, wraps a paired core, and bulk-reads the right store
through `ExecutePaired`. Three passes over the lefts (base, pair build, right read) and a second
pooled set.

**SortBounded (list 1k, take 20, 16.2 µs).** `ExecuteCoreSimpleTop` (`1982-2005`) feeds
`TopKSimpleResultContainer` (`ResolverChain.cs:377-470`) from the same base walk; the seed copy and
the second `TryGet` pass are still paid.

**Count.** `Count()` (`692-707`) → `TryCountValues(keys, predicate)` (`ConcurrentCacheStore.cs:262-286`):
the seed copy is paid in full, then one store lookup per key without materializing values. `Count`
of `list + Where` measured 7.3 µs vs 13.9 µs for `Execute` in the same run — the copy is roughly half.

## 2. The pipeline model

### 2.1 Stages and the data between them

```
                 ┌──────────────┐ TKey    ┌────────────────┐ TKey  ┌──────────┐ ref TValue ┌──────────────────┐
  seed source ──►│ Seed         ├────────►│ Key-side probes├──────►│ TryGet   ├───────────►│ Value-side probes│
  (one index     │ (keys copied │         │ unique Equals  │       │ (store,  │            │ range/list/keyset│
   or all rows)  │  under ONE   │         │ bucket Contains│       │ lock-free│            │ selector compare │
                 │  gate pin)   │         │ last-updated ts│       │  1 hash) │            │ last-updated ts  │
                 └──────────────┘         └────────────────┘       └──────────┘            └────────┬─────────┘
                                                                                                    │ (key, value)
                                                     ┌──────────────────────────────────────────────▼─────────┐
                                                     │ Predicates (FilterStep[], adaptive order)              │
                                                     └──────────────────────────────────────────────┬─────────┘
                                                                     ┌──────────────┬───────────────┼───────────────┐
                                                                     ▼              ▼               ▼               ▼
                                                              container.Add   JoinOne right   Count++         TopK.Add
                                                              (Simple/TopK)   lookup + slot   (no emit)       (bounded)
```

What flows between stages is a `TKey` and then a `TValue` reference (a `ref readonly` into the
store node is not available — `TryGetValue` copies the reference out, which is what the eager walk
does too, `ConcurrentCacheStore.cs:131`). There is no intermediate `ValueSet`, no bitmap, no
compaction. The only buffer is the seed's key list (§2.2), sized once.

Stage costs per candidate (from §1.1): key-side probe 1-4 ns, `TryGet` ~5.6 ns, value-side probe
~1-2 ns (a field read and a compare; ~4 ns through the index's `Func` selector), predicate 1-2 ns
per `Where`, emit ~1 ns (`QueryResults.UnsafeAdd`, `QueryResults.cs:293-301`). A candidate rejected
by a key-side probe never touches the store.

### 2.2 The seed is copied out first, then processed

The seed source is enumerated **once, under one `ReaderGate` pin**, into a flat key buffer
(`SeedKeys<TKey>`: `stackalloc` for ≤ 128 keys when `sizeof(TKey) ≤ 8`, else `PragueArrayPool<TKey>`
rental, grown by doubling; returned in `finally`). Then the pin is released and the pipeline
processes the buffer. Two reasons, both load-bearing:

1. **No user code under a gate pin.** Predicates, `Clone()`, comparers and the index key selectors
   are user code. The eager builder never runs them while pinned either — `UnionWith` copies the
   bucket first (`ValueSet.cs:336-339`). A pin held across a slow predicate would stall
   reclamation process-wide (`ReaderGate.cs` header).
2. **Exact result sizing.** `SimpleResultContainer.Init(maxCount)` rents a fixed buffer and
   `UnsafeAdd` *drops* overflow rows and sets `Truncated` (`ResolverChain.cs:333`,
   `QueryResults.cs:293-310`). `PooledSet.Count` is a plain counter that an enumeration can exceed
   (`context/joins.md`, "Concurrency"), so sizing from the live count is not safe; sizing from the
   copied key count is exact, which is precisely what eager gets from `Candidates.Count`.

The copy costs ~1 ns/key (a store into a flat array) instead of eager's ~5 ns hash insert plus the
growth chain — and it is what makes `Truncated` unreachable on this path, as it is on eager's.

### 2.3 Throwaway measurement

A scratch console project outside the repo (`/private/tmp/.../scratchpad/pipe`, assembly named
`Prague.Benchmarks` to satisfy `InternalsVisibleTo`, referencing the Release `Prague.Core.dll`),
Stopwatch over 2k-20k iterations after warm-up, same data as `FrozenQueryBenchmarks` (100k rows,
`Group = Id % 100`, `Code = 1000 + Id`, `Flag = Id % 3 == 0`). Not BDN; ±20% between runs, ratios
within a run are what matter.

| Row | ns | Notes |
|---|---:|---|
| eager `list(1k) ∩ range(60k)` | 403,767 | reproduces the RESULTS row |
| pipeline: bucket walk → `TryGet` → `Code ∈ [lo,hi)` → emit | **7,526** | 54× |
| same, compare through the index's `Func<TKey,TValue,int>` selector | 11,561 | delegate ≈ +4 ns/row |
| eager `range(60k) ∩ list(1k)` | 984,492 | range-first spelling |
| eager `range(1k)` | 15,839 | |
| B+tree walk 1k, count-only aggregator | 4,743 | the tree walk is not the cost |
| pipeline: tree walk 1k → `TryGet` → emit | **9,805** | |
| eager `list(1k) + Where(Flag)` | 13,924 | (8.5 µs in the BDN table; this run was noisier) |
| pipeline: walk → `TryGet` → `Flag` → emit | **6,989** | |
| eager `Count(list(1k) + Where)` | 7,293 | |
| eager `list(1k) ∩ list(100)` / reversed | 12,944 / 1,360 | |
| pipeline fixed seed 1k, probe by `bucket(100).Contains` | 9,731 | |
| pipeline fixed seed 1k, probe by `TryGet` + selector compare | **7,240** | value-side beats `Contains` |
| pipeline free seed 100, probe `bucket(1k).Contains` / selector | 1,310 / **953** | |
| eager `list(1k) ∩ keyset(flag)` | 12,085 | |
| pipeline probe by `keyset.Contains` (lock per call) | 16,893 | slower — hence the value-side predicate probe (§4) |
| eager `Or(list g, list g2)` first | 1,364,223 | |
| pipeline: walk b1, emit; walk b2, skip if `b1.Contains`, emit | **16,865** | 80× |
| pipeline with a `ValueSet<int>` dedupe set instead | 16,946 | same |
| eager all rows + `Where` (100k) | 561,693 | the Or auto-seed floor |
| pipeline: walk → `TryGet` → `customers.TryGet(v.Group)` (PK-style JoinOne) | **12,918** | vs 32 µs eager LeftSym |
| 1k bucket enumeration alone / 1k distinct store `TryGet` alone | 5,656 / 5,635 | the floor of every list-seeded shape |
| `PooledSet.Contains` hit / miss; `ReaderGate` enter+exit | 4 / 4 / 4 | |

Two consequences shape the design: (a) the **floor** of a list-seeded shape is the bucket walk plus
one store lookup per key, ~11 ns/key with memory-level parallelism, so a 1k seed cannot go below
~6 µs whatever the executor does — the wins come from not doing the *other* work; (b) a value-side
compare (read a field of the value you already fetched) beats a second index probe, so every
narrower that has a key selector or predicate is probed on the value, not on its index (§4).

## 3. Seed selection

### 3.1 Sources and their cardinality signals

| Source | Signal | Cost | API |
|---|---|---:|---|
| `UniqueEq` | 0 / 1 | 1 hash | `CacheKeyValueIndex.TryGetValue` (`InMemoryDataCache.cs:28`, exists) |
| `UniqueIn` (n keys) | ≤ n | n hashes | same |
| `ListEq` | live bucket `Count` | 1 hash | `CacheKeyValueListIndex.TryGetCount` *(wt, stage 2; `PooledSet.Count` at `PooledSet.cs:325`)* |
| `ListIn` / `ListInProjected` | Σ bucket counts (upper bound; buckets may overlap only for collection-backed indexes) | n hashes | same |
| `KeySet` | `ApproximateCount` (`InMemoryDataCache.cs:673`) | 0 | exists |
| `Range` (fixed / arg / optional) | **estimate** from the B+tree, §3.2 | 2 descents ≈ 100 ns | **new** `CacheRangeIndex.EstimateCount` |
| `LastUpdatedAfter` / `Between` | same estimator on `LastUpdatedIndex._rangeIndex` (`775-777`) | 2 descents | **new** `LastUpdatedIndex.EstimateCount` |
| all rows (no active index step) | `_cache.Count` (`1304`, acquires all stripe locks — only used when nothing else can seed) | ~µs | exists |
| `Or` | Σ of branch seed signals | Σ | derived |
| `Filter` / `FilterArg` | never seeds | — | — |

### 3.2 Range cardinality estimate

`PooledBTree` has `Length` (`105`), `_firstLeaf` / `_lastLeaf` (`92-93`), leaf `Count` (`123`),
internal `KeyCount` + `Children` (`165-166`), `FindLeafForRange` (`463-476`) and `LeafLowerBound`
(`492-510`). It has no rank. Proposed:

```csharp
// PooledBTree<TIndex,TValue> — internal
/// Estimated number of entries with key in [from, to]; exact when both bounds land in one leaf.
/// Two lock-free descents (restart on a torn child like FindLeafForRange); no ReaderGate pin needed
/// because nothing is read past the leaf's acquire-read Count.
internal int EstimateCount(TIndex from, bool fromInclusive, TIndex to, bool toInclusive);
internal int EstimateCountFrom(TIndex from, bool inclusive);   // [from, +inf)
internal int EstimateCountTo(TIndex to, bool inclusive);       // (-inf, to]
```

Algorithm: descend for `from` and for `to`, recording at each internal node the child index `i_d`
and child count `c_d`; the fractional rank of a bound is
`r = Σ_d (i_d / Π_{k≤d} c_k) + pos / (count_leaf · Π_d c_d)` and the estimate is
`round(Length × (r_to − r_from))`, clamped to `[0, Length]`. When both descents end in the same
leaf return the exact `pos_to − pos_from`. Nodes are between half and fully occupied after splits,
so the estimate is within a small constant factor — enough to decide *which* step seeds; it is never
used to size a buffer. Exposed through `CacheRangeIndex`:

```csharp
// CacheRangeIndex<TKey,TValue,TIndexKey> — internal
internal int EstimateCount(in RangeValue<TIndexKey> from, in RangeValue<TIndexKey> to); // dispatches on the two RangeValueTypes
internal TIndexKey KeyOf(TKey key, TValue value) => _keySelector(key, value);           // the value-side probe (§4)
```

(`_keySelector` is private today, `InMemoryDataCache.cs:524`.) `LastUpdatedIndex` gets the same
`EstimateCount(long after, long untilInclusive)` forwarding to its `_rangeIndex`.

### 3.3 Decision rule

After binding (§5 — conditions and selectors evaluated, so the active step set is known):

1. Collect the active index steps at the top level of the plan (Or counts as one step; its signal is
   the sum of its branches' seeds).
2. **Fixed-seed mode (default):** seed = the *first* active index step. Every later step is a probe.
   Encounter order is that step's enumeration order, byte-identical to eager (§3.4).
3. **Free-seed mode:** seed = the step with the smallest signal; ties → the earliest. A unique step
   (signal 0/1) always wins; a signal of 0 short-circuits to the empty result with no walk at all.
4. Free-seed mode is automatic for **`Count`** (no order) and for **classic `Sort`** (the result is
   fully sorted afterwards; `StableSort` keeps comparer-equal rows in encounter order — `context/query.md`
   "Sorted paging" — so ties *would* differ; see §8 for why that is acceptable and how it is gated).
   It is **not** automatic for `SortBounded` (ties are by encounter ordinal, the plan's whole point)
   or for unsorted `Execute*`; those take it only with `FrozenOptions.ReorderIndexNarrowers = true`
   *(wt — the same opt-in stage 2 introduced for `IndexStepsExecutor`; semantics unchanged: same set,
   same `Count`, order follows the seeding source)*.

### 3.4 Order-preserving small-probe seed (the fixed-seed answer to `list ∩ list`)

Fixed seed does not have to mean "walk the first bucket". When the first step is a `PooledSet`-backed
step (list, key-set) and another active equality step (unique, list) has a smaller signal, walk the
**small** step, probe the **first** step's set for the slot each survivor occupies, and sort the
survivors by that slot before emitting:

```csharp
// PooledSet<T,TKeyComparer> — internal; ContainsCore (PooledSet.cs:505-525) already has the index
internal bool TryGetSlot(T item, out int slot);   // gate-pinned like Contains; slot < Tables.Size
```

Eager's encounter order for `list(A) ∩ list(B)` is A's slot order: `UnionWith(A)` fills a fresh set
densely in A's enumeration order, `IntersectWith(B)` removes by slot, the enumerator yields slot
order (`ValueSet.cs:1720-1737`). Sorting survivors by their slot in A reproduces exactly that
sequence, at O(|B| + k log k) instead of O(|A|). For `list(1k) ∩ list(100)` that is
100 × (5.6 walk + 4 probe) + sort(10) + 10 × `TryGet` ≈ 1.2 µs against 9.6 µs, **with the eager
order**. `IfTaken` (list then unique) degenerates to one probe. Rule: take this path when
`2 × smallest ≤ first` (survivors are bounded by the smallest, so the sort is cheap) — the `2×`
absorbs the probe and the extra pass. Not applicable when the first step is a range or last-updated
step (B+tree order has no stable slot) or when the first step is an `Or`.

## 4. Probe catalogue

For each `NarrowerKind` (`NarrowerDescriptor.cs:6-22`): the seed behaviour, the probe, the API it
needs, its cost, and the reader-safety argument. "Value-side" means the probe reads the value the
pipeline already fetched from the store; "key-side" means it consults the index. Eager's staleness
baseline, for comparison: `AddOrUpdate` writes the store first, then every index in registration
order (`InMemoryDataCache.cs:1437-1446`); `Remove` likewise (`1477-1482`). List and unique indexes
add the new key before removing the old (`Indexing.cs:300-334`, `InMemoryDataCache.cs:405-430`), so
an entity is transiently visible under two index keys and never invisible.

| Kind | As seed | As probe | API | Probe cost | Staleness vs eager |
|---|---|---|---|---:|---|
| `UniqueEq` | `TryGetValue(k)` → 0/1 keys | key-side: `TryGetValue(k, out e) && e.Equals(candidate)` | exists (`InMemoryDataCache.cs:28`) | 1 hash | identical: eager's non-first path is `IntersectValue(add:false)` → `ValueSet.IntersectWith(entityKey)` (`80-91`, `ValueSet.cs:367`), the same index read |
| `UniqueIn` (span) | for each key in span order `TryGetValue` (dedupe not needed: unique) ; **empty span = zero rows** (eager `139-182`: empty → `Clear()`) | key-side: `TryGetValue` per span key until `Equals` (n ≤ 8: linear; else: hash the candidate's entity keys once per execution into a stack set) | exists | n hashes worst case | identical |
| `ListEq` | walk bucket (`GetValuesUnsafe`, `Indexing.cs:522`) | **value-side** when the index has a scalar `KeySelector` (`Indexing.cs:274`, internal): `KeySelector(key, value).Equals(k)`; **key-side** `bucket.Contains(key)` for collection-backed indexes (`_collectionSelector`, `233`) or when the value is not yet fetched (probe before `TryGet` to save the lookup when selective) | exists; add `CacheKeyValueListIndex.HasKeySelector` and `TryGetBucket(k, out PooledSet)` | 1-2 ns / 4 ns | value-side reads the store's value; eager reads the bucket. Differ only in the window between a store write and its index write (§14.1) |
| `ListIn` / `ListInProjected` | union of buckets in span order with dedupe (`ValueSet<TKey>` seen-set; eager `Indexing.cs:660-700` dedupes by set insert) ; **empty span = step inactive** (eager `275-276`, `231-232` `return` before `_first=false`) | value-side: `set.Contains(KeySelector(key, value))` where `set` is the n keys hashed once per execution (stack `ValueSet` ≤ 47 inline); key-side `Contains` over each bucket for collection indexes | exists + the two above | ~5 ns | as `ListEq` |
| `Range` (fixed, arg, optional) | B+tree walk into the key buffer via an aggregator (`CacheRangeIndex.GetValuesBetween<TAgg>` etc., `598-621`, exist; `IndexSkip` semantics for exclusive bounds reproduced by choosing `RangeFromExclusive`/`RangeToExclusive`/`RangeCustom`, `PooledBTree.cs:1621,1670,1738`) ; both bounds `None` = inactive (`Narrowers.Range.cs:107-108`) | **value-side**: `k = index.KeyOf(key, value)`; `from.Type switch … CompareTo` | **new** `KeyOf`, `EstimateCount` (§3.2) | ~2-4 ns | value-side (§14.1). Eager's tree walk sees the index's key; the pipeline sees the store's |
| `KeySet` | copy `_keys` under `_lock` (eager `AddKeyTo`, `688-692`) | **value-side**: `index.Matches(key, value)` = the predicate (`_predicate`, `665`); never `Contains` (a `lock` per probe measured slower than eager) | **new** `internal bool Matches(TKey, TValue) => _predicate(key, value)`, `internal int Count => _keys.Count` | 1-2 ns | value-side. Note the eager key-set step is *also* the one stage 2 could not make adaptive (*(wt)* `FrozenQuery.cs:445-447`); the pipeline handles it uniformly |
| `LastUpdatedAfter` | `GetValuesGt(after)` walk into the buffer (`InMemoryDataCache.cs:907-910`) | key-side: `index.TryGetLastUpdated(key, out ts) && ts > after` (`869-877`, exists) | exists (+ `EstimateCount`) | 1 hash | identical source (the index's own store) ; eager walks the whole `(after, +inf)` window — O(rows updated since `after`) — the probe is O(1) |
| `LastUpdatedBetween` | `GetValuesBetween(after, until)` with the `IndexSkip(after)` exclusion (`426-459`) | key-side: `ts > after && ts <= until` | exists | 1 hash | identical |
| `Filter` / `FilterArg` | never | predicate stage: `FilterStep.Passes(value, in args)` (`PointLookupExecutor.cs:24-34`) — direct call, no `ArgPredicatePool` | exists | 1-2 ns | n/a |
| `Or` | union of branch seeds, branch order, cross-branch dedupe (§5.1) | OR of branch conjunctions (§5.1) | derived | Σ | order differs from eager when first (§5.1) |
| `If` / `IfElse` / `Match` | inactive branch contributes nothing; active branch's steps are spliced into the top-level step list at bind time (§5.2) | same | derived | 1 delegate call per execution | identical |

Every value-side probe has a key-side alternative with eager's exact staleness; §14.1 argues why the
value-side default is acceptable and how to fall back per step (`FrozenOptions.IndexSideProbes`,
off by default).

Reader safety in one paragraph: probes run on the caller thread while the single writer mutates.
Key-side probes are the same calls eager makes (`TryGetValue` on a `ConcurrentCacheStore`, gate-pinned
`Contains` on a `PooledSet`) and inherit their guarantees. Value-side probes touch only the value the
store handed out, which is immutable by contract (values are replaced, never mutated —
`context/…event-loop-research.md` §1 "Results are already materialized"). The seed walk is the one
place a generation is pinned, and it is a bounded copy (§2.2). No lock is taken (`CacheKeySetIndex._lock`
is avoided by design). No probe reads a leaf or bucket array past its acquire-read count.

## 5. Composite steps

### 5.1 `Or`

*Or is the first active step (seed).* Eager auto-seeds from all rows under all store locks and ends
in store hash order (§1.2). The pipeline seeds each branch by *its* first step, probes the branch's
remaining steps on those keys, and unions the surviving keys across branches with a dedupe set
(`ValueSet<TKey>`: 47 inline slots, pooled above); a branch whose every step is inactive (`q => q`,
or an `If` whose condition failed) contributes nothing — the eager no-op rule (`OrWith` flushes only
if a branch narrowed, `101-102`). If *all* branches are inactive the Or is inactive and the next
step seeds, as eager's `_first` stays set. Encounter order becomes *branch 1's keys, then branch 2's
new keys*, which is **not** eager's order — eager's is the store's bucket order, which the public
contract never specified and which changes whenever the store resizes. Recommendation: enable by
default (the eager order is an accident of the seed, not a promise; the win is 60-80×) behind
`FrozenOptions.OrSeed = true`, with the differential tests for Or-first comparing sets and `Count`
(§11.b). If the reviewer disagrees, the same code path is exactly one flag away from opt-in.

*Or is not the seed.* Probe = `branch1(key, value) || branch2(key, value)` where a branch is the AND
of its steps' probes in declaration order and an inactive branch is `false`. A branch's first step is
not special here (eager marks then prunes; the bitmap AND/OR is a pure set operation, so the
conjunction/disjunction of per-row probes is result-identical). Nested Ors recurse. Or probes are
index-only (`PreparedNarrowOnly` admits no `Where`), so every leaf is a §4 probe.

### 5.2 `If` / `IfElse` / `Match`

The typed chain evaluates conditions at replay; the pipeline evaluates them at **bind time**, before
seed selection: `IfStep.Bind(in args, ref frame)` calls `condition(args)` once and, when true,
appends its child steps' bindings to the frame's *active list*; `Match` selects the first arm whose
tag equals `selector(args)` under `EqualityComparer<TTag>.Default` (the `Narrowers.Match.cs:75`
rule), else the default, else nothing. Seed selection then runs over the active list only, so the
seed is decided per execution after the conditions — an inactive first `If` leaves the next step to
seed, exactly as eager's `_first`. Branch `Where`s (allowed in `PreparedConditionalBranch`) are
appended to the predicate stage in their declared position relative to top-level filters; order does
not affect results (predicates are pure) but does affect exception timing — see the adaptive-order
caveat stage 2 documented for `AdaptiveFilterOrdering` *(wt, `FrozenOptions.cs`)*.

### 5.3 Fused predicates with adaptive ordering

The predicate stage is a `FilterStep<TValue,TArgs>[]` (constant `Predicate<TValue>` or
`Func<TValue,TArgs,bool>` called directly — no per-thread box, because the pipeline is the one
applying the filter). Ordering reuses stage 2's `FusedFilter` sampler *(wt, `FusedFilter.cs`)*: every
256th execution counts calls/rejections per step, publishes a permutation through `Volatile.Write`,
unsampled executions read it once. Value-side *probes* are pure and cheap too, so they join the same
adaptive pool as the predicates — a rejecting range compare should run before an expensive string
`Where`. Key-side probes stay ahead of `TryGet` (they can save the store lookup); the sampler may
reorder among them as well. Reordering is order-preserving for rows because every probe and predicate
is a pure function of `(key, value, args)`; the DuckDB `AdaptiveFilter` argument.

## 6. Emit stage: exact parity with the existing containers

The pipeline **drives the existing containers instead of re-implementing them**. `SimpleResultContainer`
(`ResolverChain.cs:305-362`), `TopKSimpleResultContainer` (`377-470`) and `JoinedResultContaier`
(`120-303`) all implement `IResultContainerInitializer<TKey,TValue>` (`IDataCacheEntity.cs:439-443`):
`Init(maxCount)`, `Add(key, value)`, `Seal(actualCount)`. Eager's `Execute` is exactly
`Init(Candidates.Count); TryGet(container, keys, filter); Seal(actual)` (`CacheQueryBuilder.cs:726-729`).
The pipeline replaces the middle call:

```csharp
container.Init(seed.Count);                 // exact upper bound: the copied key count (§2.2)
var actual = 0;
foreach (var key in seed) {
    if (!KeyProbes(key)) continue;
    if (!_cache.TryGet(key, out var value)) continue;      // InMemoryDataCache.cs:1308
    if (!ValueProbes(key, value) || !Predicates(value, in args)) continue;
    container.Add(key, value);                              // clones on add when eager would
    actual++;
}
container.Seal(actual);
return container.BuildResults();
```

What falls out for free, step for step with eager:

- `TotalCount` = `actual` = **all** matches, even when `take` is small: eager materializes every
  match and slices afterwards (`SimpleResultContainer.BuildResults`, `340-356`;
  `SliceLeaveTotalCount`, `QueryResults.cs:249-256`). The pipeline does the same. (A page-bounded
  buffer that stores only `[0, skip+take)` and counts the rest is a correct later optimization for
  the *unsorted* container — `_cloneOnAdd` is already false when a slice is requested, `323-327` —
  but not v1.)
- `Execute` / `ExecuteCloned` / `ExecutePooled` / `ExecutePooledCloned`: the `pool` / `clone` flags
  go to the container constructor; clone-on-add vs clone-after-slice is its decision (`323-327`,
  `355`). Pooled buffers come from `PragueArrayPool<T>.Pool` (`QueryResults.cs:54`).
- Empty seed → return `QueryResults<T>.Empty` without `Init` (eager `724-725`). Seed non-empty but
  nothing survives → `Init` rented, `Seal(0)`, `BuildResults` returns `EmptyWithTotalCount(0)` =
  `Empty`, `Dispose` returns the buffer (`340-344`, `358-361`).
- `skip > total` → `EmptyWithTotalCount(total)` (`342-344`).
- `Truncated` is unreachable (exact `Init`).
- `Count`: no container; `foreach key … if (probes && TryGet && predicates) n++` — the store
  existence check is kept because eager's `TryCountValues` only counts keys present in the store
  (`ConcurrentCacheStore.cs:236-260`). When no value-side probe and no predicate is active, `TryGet`
  is still required for that reason (a stale index key must not count); it is the same 5.6 ns.
- Disposal: the seed buffer, the dedupe set and the container are released in nested `finally`s in
  the `ExecuteCoreJoinedTop` style (`CacheQueryBuilder.cs:1895-1916`); a throwing predicate or
  selector strands nothing (leak tests, §11.d).

## 7. Joins

### 7.1 Fusing `JoinOne` (outer and inner)

A `JoinOne` right lookup is a point read per left; fusing it means writing the right slot of the row
in the same pass, right after `container.Add(key, value)`. `JoinedResultContaier.Add` creates the row
(`ResolverChain.cs:187-192`, `GetValueRefOrAddDefault`); the row type exposes its slots through
`IJoinResult.TUnsafeGetValAt<TRightValue>(index)` (used by `Clone`, `JoinOneResolver.cs:179-181`).
The pipeline needs a per-left lookup from each resolver family:

```csharp
// implemented explicitly by the four JoinOne resolvers (JoinOneResolver.cs:144, 490, 818, 1077)
internal interface IFusableJoinOne<TLeftKey, TLeftValue, TRightValue> {
    /// True when the resolver has no filter callback (NoFilter<TBuilder>) — a filter is a builder
    /// lambda over the paired core and cannot be turned into a point probe.
    bool CanFuse { get; }
    /// One right lookup for one left: PK-to-PK → Selector.Select(leftKey) → right store;
    /// RightUnique → _rightIndex.TryGetValue(selector(leftKey)) → right store;
    /// LeftUnique → LeftIndex.Reverse.TryGetValue(leftKey) → …; LeftSym → LeftIndex._cacheReverse
    /// (Indexing.cs:229, the per-left reverse map) → optional RightIndex → right store.
    bool TryLookupRight(TLeftKey leftKey, TLeftValue leftValue, [MaybeNullWhen(false)] out TRightValue right);
}
```

Per left: 1-3 hashes (~6-15 ns) against today's pair-set build + paired bulk read (three passes).
Inner joins: a left without a right is **not emitted and not counted** — eager's `CountCoreJoined`
narrows candidates to matched lefts (`CacheQueryBuilder.cs:1819-1834`) and the outer walk never
writes those rows. The container's `ExecuteJoins` (`197-203`) must then **skip the fused resolvers**:
add a `fusedMask` (a bit per resolver position) to `ExecuteWithAccessorProcessor` so it processes only
unfused ones (typically none; a sorter still runs). Cloning on `ExecuteCloned` is the container's
`ResolveChainCloner` (`ResolverChain.cs:106-118`), unchanged.

Fusable in v1: all four families, identity or key-selector, outer or inner, **`NoFilter` only** — with one
exception found in implementation: an **inner left-symmetric** join is *not* fused by default. Its pair set
is keyed by the lookup key, so the fan-out creates the rows grouped by right (every left of a bucket
together) while a per-left lookup keeps the seed's order; the two agree as sets, never as sequences. Rather
than break stage 3's rule that the frozen sequence is eager's unless the caller opts out, such a chain
replays, and `FrozenOptions.FuseSymmetricInnerJoins` opts into the fused-and-reordered form the way
`ReorderIndexNarrowers` does for the seed. An *outer* left-symmetric join is unaffected — its rows already
exist and the fan-out only fills them.
Fallback to replay: any `JoinOneFilter` / `JoinOneFilterWithArg` (the filter is `Func<TBuilder,TBuilder>`
over a paired core — `JoinOneResolver.cs:32-60`), chained joins deeper than the T4 emits handle
today; a chain containing a `JoinMany` is admitted since step 8 (§7.2).

### 7.2 `JoinMany` — as implemented (step 8; v1 said "replay")

v1 deferred `JoinMany` on the argument that its fan-out is inherently two-pass: the rows are slots
in one shared buffer partitioned by `PrepareSharedBuffer` after every left's count is known, so the
resolver must record every (left, right) pair before it can size anything, dedupe rights across
lefts through `JoinManyFanOut`, and deliver by slot (`context/joins.md`, "JoinMany fan-out",
"Concurrency"). Step 8 separated the two things that argument conflates and did both:

1. **The chain is admitted regardless.** `FrozenPlanner.Joined` no longer gates on `manyCount == 0`;
   `JoinChainShape` counts a `JoinMany` (`IJoinResolver.IsMany`, JIT-folded) as a join of its own
   kind, never as "unfusable". The narrowing is the pipeline pass; what the fan-out costs is paid
   after it, over the rows the pass formed, exactly as it is paid over the eager base walk's rows.
   An **unfused** outer `JoinMany` is an ordinary unfused resolver of the post-pass walk
   (`ExecuteJoins` / `ExecuteJoinsBounded` → `UnsafeExecuteWithAccessor`); an unfused **inner** one
   runs in the fill walk (`FillFused(…, innerMany: true)`), in chain order with the fused inner
   fills, then drops the rows whose slot stayed empty (`IUnsafeValueAccessor.PruneEmptyManySlots`,
   the eager `RetainNonEmptyManySlots` rule without a candidate set). Such a chain stays on the
   classic flow (eager's `AllInnerNarrowable` gate) and its `Count` forms the rows and runs the inner
   fills (`CountThroughFill`), as eager's `CountCoreJoined` runs the whole inner phase.
2. **Without a filter callback the join fuses** (`CanFuse = TFilter.IsNoOp`, like a `JoinOne`),
   through `JoinManyFusedFill` (`QueryBuilders/JoinManyFusedFill.cs`): one pass over the rows —
   per left the family's own bucket read (`IManyBucketSource`: right-list → selector → right list
   index; left-symmetric → `Reverse` → selector → right list index; collection → the index half),
   per right one store lookup — appending the rights into **one append-only buffer in row order**.
   That is the answer to "the slot cannot grow": it never has to. Because lefts are processed
   sequentially, each slot is a contiguous run of the buffer whose length is known the moment its
   left is done; the buffer grows by doubling (pooled rent / copy / return) and the slots are given
   the final array afterwards, by row index, as `(offset, count)` runs (`QueryResults.SetFilled` +
   `AssignSharedBuffer`). No pair set, no `PrepareSharedBuffer`, no dictionary lookup per delivery,
   no `Init` per left. Exactness holds by construction — a slot holds exactly what was appended for
   it — so nothing is dropped or marked `Truncated` whatever the index writer does; the one thing
   the pair set also bought, "a right the bucket enumerator yields twice for one left is delivered
   once", is kept with a linear check over the slot's last 16 keys and a `ValueSet` beyond that.
   A pooled buffer is registered with the result's disposer exactly as the eager shared buffer is;
   a `Clone()` that throws mid-fill returns the rental in the fill's `finally`. The buffer's first
   rental is sized by the previous execution's total (an advisory `int` per chain position, owned
   by the executor). An inner fused `JoinMany` narrows a bounded page before the heap and a count
   by a bucket probe (`UnsafeNarrowFused`: the first right the store has decides), so it keeps the
   bounded flow where eager falls back to the classic core.

   Two build-time gates keep the fill off where it would do more work than eager: a **filter
   callback** (a builder lambda over the paired core — it needs the pair set) and a **classic `Sort`
   declared before the join** (the container sorts and crops before the ordinary walk fills only
   the page; an innermost `SortBounded` is fine, the bounded flow materializes the page first, and a
   sorter *after* the join needs every row filled anyway).

**Order.** Rows come out in the seed's order on every family — the eager inner phase creates them
in candidate order too, so no `JoinMany` family regroups the way the inner left-symmetric `JoinOne`
does. Inside a slot the fused fill keeps the bucket's order; the eager fan-out delivers a slot in
pair-set order, which differs only when rights are shared across lefts in overlapping buckets (the
collection shapes, a non-injective selector: eager interleaves by first-recording order). A slot is
an unordered set and an unsorted result's sequence is not a contract (owner's ruling: "order is not
important if not specified"), so no opt-in exists for the eager interleaving; sorted results with a
total comparer are byte-identical.

**Measured** (`RESULTS.MD`, "stage 3, step 8"): shape A with a `JoinMany` 19.1 → 6.7 µs (admission
alone) and lower with the fused fill; three lists + inner `JoinMany` 23.8 → 11.8 µs by admission;
the 1k-row fan-outs move only with the fused fill (right-list 97.9 → 64.8 µs, collection 116.5 →
57.3, left-symmetric 86.2 → 46.8). The pair-set fan-out stays for filtered joins and after a classic
`Sort`, and for the eager path, untouched.

### 7.3 Sort after join

The sorter is an ordinary resolver in the chain and runs inside `ExecuteJoins` /
`UnsafeSortResults(ref ValueDictionary…)` (`Resolvers.cs:56-61`); a comparer over joined fields
works unchanged because the rows are complete when it runs. `SortBounded` after a fused join follows
§8: the pipeline feeds `TopKJoinedBaseContainer` the way `ExecuteCoreJoinedTop` does
(`CacheQueryBuilder.cs:1862-1918`), then the fused lookups fill only the page rows in
`ExecuteJoinsBounded` — which is the *existing* bounded flow with the resolver's per-left lookup
replacing the pair-set build.

## 8. Sort

- **Classic `Sort`**: pipeline → `SimpleResultContainer<…, SortResolver>` → `BuildResults` calls
  `UnsafeSortResults` (`ResolverChain.cs:350-351` → `Resolvers.cs:47-53` → `StableSort`). Free seed
  is allowed here (§3.3). `StableSort` keeps comparer-equal rows in encounter order, so with a
  *non-total* comparer a free seed changes which of two equal rows comes first. `context/query.md`
  documents classic ties as "unspecified" for page boundaries and the `ClassicVsBoundedDifferentialTests`
  header asserts only "right count, real rows, no phantoms" for non-total classic sorts
  (`ClassicVsBoundedDifferentialTests.cs:5-13`). Free seed for `Sort` therefore stays inside the
  documented contract; the differential tests for sorted free-seed plans compare sorted sequences
  with a total comparer and multisets otherwise (§11.b).
- **`SortBounded`**: pipeline → `TopKSimpleResultContainer` (`Add` stamps `_seen++` as the ordinal,
  `415-426`) with the **fixed** seed, so ordinals are eager's. The `ExecuteCoreSimpleTop` gate
  (`take != int.MaxValue && TResolver.IsSorter && AllowsBounded && OrdersByLeftValues`,
  `1982-1993`) is reused verbatim; when it fails the classic container is used, as eager does. A
  *joined* `SortBounded` page takes the frozen-only `FrozenTopKJoinedContainer` (§13 step 7) instead of
  the eager `TopKJoinedBaseContainer` — identical behaviour, but the sorter arrives as a struct type
  parameter, so a comparison is the user comparer's `Compare` rather than one `__Canon`-shared hop per
  chain link.
- **Classic `Sort` with a finite page takes the bounded flow too** (step 8, under the owner's ruling
  that ties inside a sorted result are unspecified). The bounded container breaks ties by encounter
  ordinal, which *is* the stable sort's tie order for the same input sequence, so for one seed the two
  plans produce the same sequence; the classic `Sort` keeps its free seed on either container (a
  `SortBounded` keeps the fixed seed, so its ordinals stay eager's). Gate: the sorter is innermost and
  orders by the left value (`OrdersByLeftValues`); a sorter over the joined row still sorts in the
  classic container. A page of a 1k-row `Sort` then costs a heap of its size instead of the full sort
  (`Sort_JoinMany`, `RESULTS.MD` "stage 3, step 8"), and a `JoinMany` after such a `Sort` fuses (§7.2)
  because the page is materialized before the fill.

## 9. Fallback matrix and the planner rule

`FrozenPlanner.Simple` / `.Joined` (`FrozenQuery.cs:149-179`; *(wt)* `370-480` with stage 2) gain one
more executor. Selection order:

| Plan shape | Executor |
|---|---|
| simple, unsorted, `[UniqueEq, Filter*]` | `PointLookup` (stage 1, unchanged) |
| simple or single-sorter; every top-level step is a supported kind (§4) incl. `Or`/`If`/`IfElse`/`Match` whose leaves are supported; ≤ 16 index steps after flattening; every `TIndexKey` is a reference type or an unmanaged type ≤ 16 bytes | **`Pipeline`** |
| joined: as above and every resolver is a `JoinOne` with `CanFuse` (or the sorter) | **`Pipeline`** (fused joins) |
| joined: as above with any `JoinMany` (filtered or not, outer or inner) among fused `JoinOne`s (step 8, §7.2) | **`Pipeline`** — a `JoinMany` without a filter callback and no classic `Sort` before it fuses (the per-left fill); otherwise its fan-out runs after the pass |
| any filtered `JoinOne` outside the `SortBounded` → outer-joins shape, an inner left-symmetric `JoinOne` (unless opted in), nested/keyed join seams | `Replay` (joined) |
| a `TIndexKey` that is an unmanaged struct > 16 bytes (rare: big value tuples) | `Replay` |
| `LastUpdated*` against an adapter whose group key is not the entity key (`LastUpdatedIndexAdapter._groupKeySelector`, `InMemoryDataCache.cs:1073-1117`) | `Replay` in v1 — the probe needs the group key of the row; add `GroupKeyOf` later |
| anything the binder rejects at build (unknown descriptor kind) | `Replay` |

`FrozenOptions` *(wt)* gains `Pipeline = true`, `OrSeed = true` (§5.1), `IndexSideProbes = false`
(§14.1). `ReorderIndexNarrowers` keeps its meaning (free seed for unsorted `Execute` / `SortBounded`).
`AdaptiveIntersection`, `CapacityHints` and `IndexStepsExecutor` become irrelevant for pipeline
plans (the pipeline has no candidate set to size or intersect) and stay for the replay fallback.
`Explain()` prints `executor: Pipeline`, the seed rule, and per step `seed-capable / probe: value-side |
key-side` plus the live adaptive order.

## 10. Concurrency and lifetime

- **Immutable after build.** `PipelineExecutor` holds `IPipelineStep[]` (boxed once), the
  `FilterStep[]`, the resolver chain by value, and the `FusedFilter` ordering object (its only mutable
  state is the `Volatile`-published permutation and advisory counters, as in stage 2).
- **Per-execution state is a `ref struct` frame:**
  ```csharp
  [SkipLocalsInit]  // the frame is fully written before it is read; the seed buffer is a stackalloc
  internal ref struct PipelineFrame<TKey, TArgs> {
      internal ActiveSteps Active;                 // [InlineArray(16)] of (stepIndex, bindingIndex)
      internal StepBindings Bindings;              // [InlineArray(16)] StepBinding { long Bits0, Bits1; object? Ref; int Kind; }  ≈ 32 B each → 512 B
      internal SeedKeys<TKey> Seed;                // stackalloc TKey[128] when sizeof(TKey) ≤ 8, else PragueArrayPool<TKey>
      internal ValueSet<TKey, DefaultKeyComparer<TKey>> Dedupe;  // only created for multi-bucket / Or seeds; 47 inline slots
  }
  ```
  Resolved selector values (`Func<TArgs, TIndexKey>` results, range bounds, `Match` tags) are written
  once per execution into the step's `StepBinding` (`Unsafe.WriteUnaligned` for unmanaged keys,
  the `Ref` slot for reference keys); probes read them back with `Unsafe.ReadUnaligned`. Nothing is
  boxed per execution.
- **No `ArgPredicatePool`.** The pipeline applies filters itself, so `Func<TValue,TArgs,bool>` is
  called directly with the frame's `in args` (`FilterStep.Passes`, `PointLookupExecutor.cs:33`). No
  thread-static state on this path at all.
- **Re-entrancy.** A predicate that executes another frozen query on the same thread sees a fresh
  frame; the outer frame lives on the outer stack. No shared mutable state → trivially re-entrant
  and thread-safe for concurrent executions of one plan.
- **Gate discipline.** Exactly one `ReaderGate` pin per seed source, released before the first store
  lookup (§2.2). Key-side probes pin internally per call (`PooledSet.Contains`). No user code runs
  pinned. No lock is taken.
- **`Dispose` contract unchanged.** Pooled results are the container's `QueryResults`; the caller
  disposes them as today. Everything else the pipeline rents is returned inside the execution.

## 11. Semantics and test plan

Extend `tests/Prague.Core.Tests/Prepared/` (the 293-test suite: differential, allocation, leak,
concurrency, frozen), reusing `PreparedQueryDifferentialTests.AssertSame` (`:43`) and the shape
fixtures in `PreparedQueryNarrowerDifferentialTests` / `…OrDifferentialTests` / `…IfDifferentialTests`
/ `…MatchDifferentialTests` / `…OptionalRangeDifferentialTests` / `…SortDifferentialTests` /
`…JoinDifferentialTests`. New file `FrozenPipelineTests.cs`:

- **(a) Byte-identical parity, fixed seed.** For every shape in §1.2 plus multi-value `In` (empty span,
  one key, n keys, missing keys), `ListInProjected`, key-set, last-updated after/between, optional
  range (both/one/no bound), nested `Or` inside `If`, `Match` with duplicate tags and no default:
  eager == prepared == frozen(Pipeline) as *sequences*, across `Execute` / `ExecuteCloned` /
  `ExecutePooled` / `ExecutePooledCloned` × the five `(skip, take)` pages `FrozenQueryTests` uses,
  with clone-identity checks (`ReferenceEquals` false only on cloned variants), `TotalCount`,
  `Truncated == false`. Includes the small-probe seed (§3.4) on `list ∩ list`, `list ∩ unique`,
  `keyset ∩ list` — the row *sequence* must still be eager's.
- **(b) Set + `Count` parity, free seed.** `Count` on every shape (auto free seed); `Sort` with a
  total comparer (sequence equal) and a non-total comparer (multiset equal, per-page rows ⊆ full
  result, pages partition); `ReorderIndexNarrowers = true` on unsorted shapes (multiset equal);
  Or-first with `OrSeed` (multiset + `Count`), and Or-first with `OrSeed = false` (sequence equal,
  proving the flag routes back).
- **(c) Staleness under a concurrent writer.** Deterministic interleavings first, then a 2 s
  live-writer stress in the style of `JoinManyRightListIndexConcurrentMutationTests`: a writer moves
  rows across the range boundary, across list buckets, in and out of the key-set predicate, and
  removes/re-adds keys, while N threads execute the plan. Assertions: no exception, every returned
  row satisfies the *current or previous* value's predicates (both are legitimate under the
  contract), no duplicate keys, `Count == results.Count` when executed back-to-back with no writer.
  Plus the two directional cases of §14.1 pinned as deterministic tests with the writer paused
  between the store write and the index write (test hook: an `ICacheIndex` that blocks on a gate).
- **(d) Leaks.** `LeakAssert.Balanced` around each shape, including: a throwing arg selector at
  bind time, a throwing predicate mid-walk, a throwing `Clone()` (pooled + cloned), a throwing user
  comparer in `Sort` and `SortBounded`, a throwing `IFusableJoinOne` selector; seeds above 128 keys
  (pool path) and Or seeds above 47 keys (dedupe pool path).
- **(e) Allocation pins.** `PreparedQueryAllocationTests` style (`Measure` / `MeasureSettled`,
  `:63-80`): 0 B per `ExecutePooled` and `Count` on list+where, list∩list, list∩range, range,
  keyset, last-updated, Or (both modes), If/Match, fused JoinOne (outer, inner), Sort with a struct
  comparer, SortBounded page. Frozen ≤ prepared on every other shape.
- **(f) Executor selection via `Explain()`.** Every row of the §9 matrix asserts the executor name
  and, for `Pipeline`, the per-step probe side; `TIndexKey` > 16 B falls back; a filtered `JoinOne`
  falls back; `JoinMany` falls back.
- **(g) Generated layer.** `Prague.Generated.Tests/Prepared` gains one `BuildFrozen()` twin per
  generated `WithXxx` shape family (no codegen change: the generated extensions record the same
  narrowers).

## 12. Benchmark plan with targets

Rows in `FrozenQueryBenchmarks` (eager baseline / `Build()` / `BuildFrozen()`), default job
`--inProcess` — the stale-worktree problem stage 2 hit means the out-of-process toolchain builds a
copy of the tree that may not match the working files; in-process runs the bits under test. Re-run
any range row that lands 2-3× off its neighbours alone (the known in-process artifact,
parent spec §8 "Not observed as stable"). Keep the change only if every row meets its bar:

| Row | Eager (M4 Pro) | Predicted pipeline | Keep if |
|---|---:|---:|---|
| `Unique*` (4 rows) | 19-27 ns frozen | unchanged (PointLookup kept) | within noise of stage 1 |
| `ListWhere` (1k, `Flag`) | 8.5 µs | 5.5-7 µs | ≥ 1.3× (bar asked: 1.5×; the walk+TryGet floor is ~6 µs, so 1.5× needs the snapshot walk or the store to get faster — report honestly) |
| `ListTwoWheres`, `ListThreeWheres` | 9.5 / 9.9 µs | 6-7 µs | ≥ 1.4× |
| `ListList` (1k ∩ 100) | 9.56 µs | 1.2-1.5 µs (small-probe seed, order-preserving) | ≥ 2× |
| `ListListReversed` (100 ∩ 1k) | 1.32 µs | ~1.0 µs | ≥ 1.2× |
| `ListRange` (1k ∩ 60k window) | 407 µs | 8-12 µs | **≥ 5×** (predicted ~40×) |
| `RangeList` (new: 60k window first, fixed seed) | 984 µs | ~300 µs fixed / ~10 µs with `ReorderIndexNarrowers` | ≥ 2× fixed; ≥ 20× free |
| `Range` (1k window) | 13.4 µs | 9-10 µs | ≥ 1.3× |
| `OptionalRange` | 13.3 µs | 9-10 µs | ≥ 1.3× |
| `ListKeySet` (new) | 12.1 µs | 7-8 µs | ≥ 1.4× |
| `ListLastUpdated` (new: bucket ∩ updated-after covering 50% of rows) | tree walk of 50k | ~8 µs | ≥ 5× |
| `Or` (two 1k buckets, first) | 1,078 µs | 15-20 µs | **≥ 1.5×** asked; predicted ~60× |
| `OrAfterList` (new: list then Or of two uniques) | ~10 µs | ~7 µs | ≥ 1.3× |
| `IfSkipped` / `IfTaken` | 10.2 / 3.1 µs | ~6.5 µs / ~0.1 µs | ≥ 1.4× / ≥ 10× |
| `Match` | 8.5 µs | ~6.5 µs | ≥ 1.2× |
| `JoinOne` LeftSym 1k, fused | 32 µs | 12-15 µs | **≥ 1.5×** |
| `InnerJoinOne` fused (new) | ~32 µs | 12-15 µs | ≥ 1.5× |
| `JoinOneFiltered` (new, fallback) | — | replay | within noise of `Build()` |
| `JoinMany` (fallback) | 95 µs class | replay | within noise |
| `SortBounded` (1k, take 20) | 16.2 µs | 12-14 µs | ≥ 1.1× (not slower) |
| `Sort` classic (1k) | — | seed copy removed | not slower |
| `Count_ListWhere`, `Count_ListList` (new) | 7.3 µs / ~9 µs | ~6 µs / ~1 µs | ≥ 1.1× / ≥ 5× |
| `Build_FourNarrowers` frozen | 88 B once | + step objects, once | build-time only |

Allocation column: `-` (0 B) on every `Pipeline` row.

## 13. Implementation plan (PR-sized, each shippable with tests and rows)

1. **Index probe APIs and cardinality signals** (`Indexing.cs`, `InMemoryDataCache.cs`, `Collections/PooledBTree.cs`, `Collections/PooledSet.cs`):
   `PooledSet.TryGetSlot`; `CacheKeyValueListIndex.HasKeySelector`, `TryGetBucket`, (`TryGetCount` if
   stage 2 has not landed it); `CacheRangeIndex.KeyOf`, `EstimateCount`; `CacheKeySetIndex.Matches`,
   `Count`; `LastUpdatedIndex.EstimateCount`; `PooledBTree.EstimateCount*`. Unit tests for the
   estimator (uniform keys, duplicate runs, after deletes: within 2× of exact; exact within one
   leaf) and for `TryGetSlot` (slot equals the enumerator's index; disposed set → false). No
   behaviour change elsewhere.
2. **Pipeline for equality/range seeds with probes and predicates, unsorted, fixed seed**
   (`QueryBuilders/Prepared/Pipeline/`: `PipelineExecutor.cs`, `PipelineStep.cs` (`IPipelineStep`,
   `StepBinding`, `PipelineFrame`, `SeedKeys`), `Steps/UniqueSteps.cs`, `Steps/ListSteps.cs`,
   `Steps/RangeStep.cs`, `Steps/KeySetStep.cs`, `Steps/LastUpdatedSteps.cs`;
   `IPipelineStepSource<TKey,TValue,TArgs>` implemented explicitly by the narrowers in
   `Narrowers*.cs` the way `IPointLookupSource` is (`Narrowers.cs:11,30-31`); planner rule in
   `FrozenQuery.cs`; `Explain` output). Drives `SimpleResultContainer` (§6). Tests (a) for the
   non-composite shapes, (d), (e), (f). Rows: `ListWhere`, `ListList` (fixed walk only — expect
   ~1.3×), `ListRange`, `Range`, `OptionalRange`, `ListKeySet`, `ListLastUpdated`, `Count_*`.
3. **Seed selection** — *shipped*: small-probe seed (§3.4), free seed for `Count`, `FrozenOptions.ReorderIndexNarrowers`
   wiring, `Sort` (classic) via `SimpleResultContainer<…, SortResolver>` with free seed, the bulk
   `PooledSet.CopyKeysTo` seed copy. Tests (b) in `FrozenPipelineSeedTests`. Rows: `ListList` 5.3×,
   `ListListList` 5.2×, `Count_ListList` 8.4×, `Sort_ListList` 4.75× (parent spec §8 "step 3").
4. **Composites** — *shipped*: no per-kind `Steps/IfSteps.cs` / `MatchStep.cs` files — `If` / `IfElse` /
   `Match` became **nodes** (`Pipeline/PipelineTree.cs`: `SelectNode`, `IBranchSelector<TArgs>` —
   `IfSelector`, `MatchSelector<TTag>` — and `FilterNode` for a branch `Where`), bound once per
   execution by `PipelineCore.BindNodes` before seed selection, splicing the taken arm's leaf steps and
   filters into the same active list a top-level step would occupy — exactly the bind-time activation
   §5.2 called for, just carried by the tree walker rather than one step type per composite kind. `Or`
   is `Pipeline/Steps/OrStep.cs`: a step whose binding is bound in *branch mode* (`PipelineCore.BindOr`,
   mirroring the eager intersecter core per branch — an empty list `In` span *empties* the branch here
   where it is a no-op at the top level), carrying each branch's leaves and, packed into two repurposed
   binding slots, the active-branch mask and per-branch first-leaf indices. As a probe: the OR of each
   active branch's leaf conjunctions. As the seed (Or-first): **by default** a second store walk kept to
   the branch union reproduces the eager sequence byte for byte under `OrSeed = false`; the **default
   (`OrSeed`, decided on review) walks the union directly** — the path the design predicted as ~60×, and
   where the 39× lands — as do `Count` and a classic `Sort` either way. Planner
   (`Pipeline/PipelinePlanner.cs`, replacing `FrozenQuery.TryPipeline`): builds the flat step array and,
   only when a composite is present, the tree; rejects (replay) an `Or` nested beside a leaf in its own
   branch (only a *sole* nested `Or` flattens, inheriting no narrowing bit — the eager nested `OrWith`
   union has none to inherit), more than eight branches after flattening, and — the one shape a per-row
   probe cannot reproduce — a `Range` / `KeySet` / `LastUpdated*` step that is not an `Or` branch's
   *first* node: the eager branch core *marks* (unions) those where it *intersects* a preceding unique /
   list step, a set-level operation. Tests: `FrozenPipelineCompositeTests` (new file, superseding the (a)/
   (b) fixtures this step originally planned to extend in place), `PreparedGeneratedFrozenCompositeTests`
   (generated twins), `+1` pin group in `FrozenPipelineAllocationTests`. Rows: `Or` 39.4× default /
   1.38× `OrSeed = false`, `OrAfterList` 30.2×, `IfTaken` 28.0×, `IfSkipped` 1.15–1.31× (bar 1.4× **missed** — the
   composite bind path — see RESULTS), `Match` 1.30×. A first cut taxed composite-free plans (a per-row
   branch-filter test in the general `Walk`, a per-execution `Kind` interface call in `ChooseSeed`; shape
   B +3–6%); both were hoisted / gated out so a plan without a composite runs the pre-step-4 loop bodies
   byte for byte, and the composite path lost its `NodeKind` virtual get and, for `If` / `IfElse`, its
   `IBranchSelector` dispatch (parent spec §8 "step 4", RESULTS.MD "stage 3, step 4").
5. **`JoinOne` fusion** — *shipped*: `IFusableJoinOne` (`QueryBuilders/IFusableJoinOne.cs`) on the four
   resolvers with `IJoinFilter.IsNoOp` deciding `CanFuse`; `fusedMask` in `ExecuteWithAccessorProcessor`
   plus a **fill walk** (`JoinResults.tt`), so the fused resolvers are skipped in the ordinary walk and
   are the only ones run in the fill; `JoinedResultContaier.FillFused`. The fill is **per resolver, not
   per row** (`UnsafeFillFusedRows` writes this resolver's slot of every row and prunes an inner join's
   misses; `UnsafeNarrowFused` compacts a collected run to the lefts that have a right): the per-row
   chain-walk variant measured *slower than the unfused paired read* (`JoinOne` 42.8 µs against eager's
   31.3) because the chain dispatch over a generic `TLeft` is a per-call `__Canon` lookup, so it is paid
   once per resolver instead. `PipelineJoinedExecutor` gained the classic flow (fill → sorter → crop) and
   the inner narrowing of the bounded flow (a pooled `RowBuffer`, compacted per inner resolver, then the
   heap — the ordinals stay eager's); `FrozenPlanner.Joined` dropped its `isSorted` requirement. Found on
   the way: the four resolvers' inner paths left an earlier chained inner resolver's rows behind on an
   empty pair set (a row with a default `Left`); they now run the same `RetainNonNullSlots` post-walk the
   non-empty path runs. Rows: `JoinOne` 1.54×, `InnerJoinOne` 1.94×, `JoinOneChained` 1.68×, B-range
   1.51× (bars kept); shape A 2.37× (bar ≥ 2.5×) and shape B 1.37× (bar ≥ 1.5×) **missed** — the fills
   were ≈2.1 µs of B, not the 9.4 µs step 6 attributed to them; the residual is the eager joined bounded
   container's comparer path, shared with eager (parent spec §8 "step 5", RESULTS "stage 3, step 5").
   Inner **left-symmetric** joins are the one shape held back: their fan-out regroups the rows, so they
   replay by default and fuse under `FrozenOptions.FuseSymmetricInnerJoins` (§7.1). Tests:
   `FrozenPipelineJoinTests` (10), `PreparedGeneratedFrozenJoinTests` (4), pins in
   `FrozenPipelineAllocationTests` (+1) and the eager-only
   `Join/JoinOneChainedInnerEmptyPairSetCoreTests` (6) for the resolver fix.
6. **`SortBounded` feed** — *shipped*: pipeline → `TopKSimpleResultContainer` / `TopKJoinedBaseContainer` with the
   `ExecuteCoreSimpleTop` / `ExecuteCoreJoinedTop` gate (§8), fixed seed. `PipelineExecutor` split into the
   shared `PipelineCore` and two thin executors; the new `PipelineJoinedExecutor` drives the eager joined
   containers with the join resolvers unfused (`ExecuteJoinsBounded` over the page rows; `ExecuteJoins`
   on the classic fallback) for chains of one innermost bounded left-value sorter and outer joins only —
   inner joins and `JoinMany` replay until step 5. Tests: `FrozenPipelineSortBoundedTests` (11), pins in
   `FrozenPipelineAllocationTests` (+2), the production-shape fixture asserts `Pipeline`. Rows:
   `SortBounded` 1.06× (bar 1.1× missed by a hair — the floor, see RESULTS), `SortBounded_ListList` 3.1×,
   shape A 2.2× (bar ≥ 2× kept), shape B 1.27× / 1.24× (bar ≥ 1.5× missed: the two unfused `JoinOne` fills
   are 9.4 of its 26.8 µs — step 5's baseline; the narrowing + page alone is 1.44×). Found and fixed on the way: `JoinedResultContaier.BuildResults` handed its buffer off before the
   clone, so a throwing `Clone()` on a pooled cloned joined page stranded the values array (eager too).
7. **The frozen bounded joined container** — *shipped*: `IResolvers.WithSorter<TVisitor>` +
   `ISorterVisitor` (`ResolverChain.cs`, the sorter-only twin of `Execute<TExecutor>`: the same JIT-folded
   `TResolver.IsSorter` test per link, walked **once per execution**) hand the chain's sorter to
   `PipelineJoinedExecutor.BoundedFeeder` statically typed, and the bounded flow's heap runs in the new
   `FrozenTopKJoinedContainer<TKey,TValue,TSorter>` (`Pipeline/FrozenTopKJoinedContainer.cs`) whose
   `TopKSorterPairComparer` holds the sorter itself. Behaviourally the eager
   `TopKJoinedBaseContainer` line for line — same heap-vs-collect-all plan, same encounter ordinals, same
   `Seal` total, same `Drain` contract, same "the heap buffer never transfers ownership" rule — and the
   eager container is untouched (the only eager edit is the two additive `WithSorter` implementations on
   `Resolvers<…>`). *Why:* `IResolvers.CompareLeftValues<TLeft>` is a generic method over a reference
   `TLeft`, so each chain link is a constrained call into a `__Canon`-shared body that never inlines
   through to the user comparer — ~2-4 ns per link per comparison. Measured on the shapes' own bounded
   workload (`BoundedComparerProbeBenchmarks`, 100 candidates into a heap of 40 plus the drain): 3.86 µs
   through the sorter, 4.99 / 7.44 / 9.61 through a 1 / 2 / 3-link chain. Rows: shape A 3.50× (bar ≥ 2.5×
   kept), shape B 1.89× and B-range 2.10× (bar ≥ 1.5× kept); a joined bounded page now costs what the
   simple one costs (B 17.85 µs against 17.62 without its joins). Tests: `FrozenPipelineJoinTests` +1,
   `FrozenPipelineAllocationTests` +2 pins; every existing joined `SortBounded` suite now runs through the
   new container (parent spec §8 "step 7", RESULTS "stage 3, step 7").
8. **Cleanup** (first half done in step 3: `IndexStepsExecutor` and `AdaptiveIntersection` retired — every
   plan they served takes the pipeline): keep
   `FusedFilter` (ordering) and `FrozenHints` (replay fallback only). Update `context/query.md` and
   the parent spec's §8 with a stage-3 section and the final tables.

Existing types reused unchanged: `FilterStep`, `FrozenQuery<TArgs,TResult,TExecutor>`,
`IFrozenExecutor`, `PlanInfo` / `NarrowerDescriptor`, `SimpleResultContainer`,
`TopKSimpleResultContainer`, `JoinedResultContaier`, `QueryResults` (`EmptyWithTotalCount`,
`UnsafeAdd`, `SliceLeaveTotalCount`, `CloneInPlace`), `ValueSet` (dedupe), `PragueArrayPool`,
`ReaderGate`, `SortResolver`, `TopKSelect`, the four `JoinOne*Resolver`s.

## 14. Risks and open questions

### 14.1 Value-side probes vs index walks under a concurrent writer

`AddOrUpdate` writes the store, then each index (`InMemoryDataCache.cs:1437-1446`). Consider a row
whose range field moves from *inside* to *outside* the window, or the reverse, and a query that
observes the gap between the two writes:

| Direction | Eager (`range` step walks the tree) | Pipeline (value-side compare) |
|---|---|---|
| store = NEW (outside), index still OLD (inside) | tree walk yields the key → **row returned with a value that does not satisfy the range** | `KeyOf(value)` outside → **row not returned** |
| store = NEW (inside), index still OLD (outside) | tree walk skips it → **row missing although its current value qualifies** | inside → **row returned** |

The reverse ordering (index new, store old) cannot occur through the public write path — the store
is always written first (`Remove` too, `1477-1482`) — so those are the only two windows. In both the
pipeline returns rows that are consistent *with the values it returns*, which the eager path cannot
promise (it can hand back a value that contradicts the query). Both behaviours are inside the
documented contract ("a row written mid-query may be included or missed", `context/joins.md`,
"Concurrency"). Where they differ is *which* stale answer a caller gets; a caller who depended on
eager's exact staleness is depending on an artifact. Risk: a test that asserts index-side staleness
(none found in `Prague.Core.Tests`; the concurrent-mutation suites assert the contract, not the
window). Mitigation: `FrozenOptions.IndexSideProbes = true` switches every value-side probe to its
key-side twin (bucket `Contains`, tree membership is not O(1) — for `Range` the key-side twin is
"walk the window into a stack set when the estimate is small, else fall back to replay"), and tests
(c) pin both directions deterministically.

The same analysis holds for list (`KeySelector(value) == k` vs bucket membership) and key-set
(`Matches(value)` vs `_keys`) probes; for *unique* steps the pipeline uses the index (key-side) on
purpose, because that is also the seed mechanism and because a unique index transiently maps two
keys to one entity during an update (`405-430`) — a value-side compare would drop the row under the
old key where eager returns it; parity there is cheap to keep.

### 14.2 Encounter order

Fixed seed reproduces eager's order because (i) `ValueSet` slot order is insertion order with
holes (§3.4), (ii) the seed enumerations are the same calls eager makes, and (iii) the small-probe
seed sorts by slot. Places it could silently break: a *multi-bucket* seed whose buckets overlap
(collection-backed list index) — eager dedupes on insert, so the first occurrence wins; the pipeline's
dedupe set must be consulted in the same order (test (a) with a collection index). An `Or` seed is
knowingly different (§5.1). A range seed emits in B+tree order — within a run of equal index keys
"order is unspecified" (`PooledBTree.cs` header) but *deterministic for one walk*, and both engines
perform one walk, so sequences match; a concurrent shift can make two walks differ — as they can
between two eager executions today.

### 14.3 `TotalCount`, clone identity, `Truncated`

Covered by driving the eager containers (§6). The residual risk is a future page-bounded buffer
optimisation changing `TotalCount` semantics; test (a) pins `TotalCount` on every page including
`skip > total` and `take = 0`.

### 14.4 Cardinality estimate quality

A bad range estimate only picks a worse seed (free-seed mode) — never a wrong result. Worst case is a
skewed tree after heavy deletes where the estimate is off by more than 2× and a 60k walk is chosen
over a 1k bucket. Mitigation: prefer an *exact* equality signal over a range estimate unless the
estimate is smaller by a margin (`estimate × 2 < bucketCount`); log the decision in `Explain()`;
estimator tests after deletes.

### 14.5 Gate pin duration and the seed copy

The seed copy pins for the whole walk of one bucket or window (60k keys ≈ 42 µs on the tree). Eager
pins for the same walk (its `UnionWith` / aggregator loops). No regression, but a very large seed
under free-seed mode is a sign the plan should not run at all; `Explain()` should surface seeds
larger than 1<<20 as a warning the way `FrozenHints` caps its hint.

### 14.6 Open questions

- **Decided (step 4, on review): `OrSeed` defaults on**, as §5.1 recommended. The implementation
  first shipped it opt-in — the opt-out path (a second store walk kept to the branch union, the eager
  sequence byte for byte) clears the row's bar on its own at 1.38× — and the reviewer took the
  recommendation: the eager Or-first order is the store's hash order, which nobody can build on, and the
  default is 39×. `OrSeed = false` keeps the eager sequence for a caller who wants it.
- Should the pipeline expose an internal `IPipelineSink` so the Kafka layer or `Prague.Api` can
  consume rows without a `QueryResults` buffer (streaming)? Out of scope; the stage keeps the
  `QueryResults` contract.
- The `PooledSet` enumerator at ~5.6 ns/slot and the store's node-per-entry layout (~5.6 ns/lookup)
  are the floor of every list-seeded shape. A snapshot-span walk measured no faster. If the
  `ListWhere` 1.5× bar matters, the lever is the store (a struct-of-arrays layout), not the executor.
- `LastUpdated*` on group-keyed adapters needs `GroupKeyOf(key, value)` from the adapter to probe;
  v1 replays those plans. Worth adding in step 2 if the generated `UpdatedAfter` shapes are common.
- `TIndexKey` unmanaged structs > 16 B: raise the inline binding size or box once per plan into a
  per-execution *pooled* frame? Replay in v1; revisit if a real model needs it.

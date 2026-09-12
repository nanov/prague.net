# The frozen-query codegen ceiling — shape A hand-written at four levels

> **Status:** measurement and recommendation, no generator. Written 2026-09-12 against `poc/prepared-query` at
> `7ef34ab`, on branch `research/codegen-ceiling`. Task brief:
> `docs/superpowers/plans/2026-09-11-codegen-ceiling-task.md`. Tracker: #82.
> **Question:** of a frozen query's per-execution cost, how much could a source generator remove by emitting one
> specialized straight-line executor per call site — and is that worth a generator PR, and is a materialized
> join worth its writer-side invalidation?
> **Answer in one line:** a straight-line executor takes shape A from 4.49 µs to 1.70 µs (2.65× over frozen, 10× over eager) and its `Count` from 1.46 µs to 0.31 µs; three quarters of what remains is the page selection itself; the join is not measurable and a materialized join buys nothing here.

## 1. What was measured

Production shape A, `ListListListSortBoundedJoinOne`: three list-index narrowings (`_byGroup` 1k ∩ `_byBand`
333 ∩ `_byLane` 111 → 111 rows) → `SortBounded(new PqbByScoreTies())`, page `ExecutePooled(20, 20)` → outer
`JoinOne(_details)` by primary key (every fourth right row missing). Same fixture and arguments as
`FrozenQueryBenchmarks` (100k `PqbItem`, `Random(1234)` scores, `(13, 113, 413)`), rebuilt in
`benchmarks/Prague.Benchmarks/FrozenCodegenCeilingBenchmarks.cs` with the `_Eager` and `_Frozen` bodies copied in
as baseline and reference, plus the hand-written executors below. Every hand-written body executes pooled,
disposes, returns the frozen query's rows / `Count` / `TotalCount` (asserted once in `Setup`: multiset of left
keys, each row's right against its own left, both counts) and allocates 0 B per operation (asserted in `Setup`
after warming the pools, and by `[MemoryDiagnoser]`).

### The frozen executor today, per execution (what the hand-written code replaces)

`PipelineJoinedExecutor.Execute` → `PipelineCore.Open`: three `ListEqStep.Bind` virtual calls (each a
`Func<TArgs,int>` selector delegate, a `TryGetBucket`, a `StepBinding.Write`), `ChooseSmallest` (three
`Signal` virtual calls), `Seed` (the lane bucket's `CopyKeysTo` into the stack buffer). Then `ExecuteTop`: a
`JoinedResultContaier` over a `ValueDictionary`, `chain.WithSorter` (the chain walk to the sorter, once),
`FrozenTopKJoinedContainer.Init` (k = 40, 4k ≥ 111 → collect-all, one rent of 111 triples),
`PipelineCore.Walk` — per key one store `TryGet`, then `PassesValueProbes`: two `IPipelineStep.ProbeValue`
virtual calls, each `_index.KeySelector(key, value)` (a delegate call into the user's index lambda) compared
with `binding.Read<int>(0)` (a type-erased `Unsafe.ReadUnaligned` out of the 16-byte slot) — then
`topK.Add` (a triple append and `_seen++`). Then `Drain` (`TopKSelect.SelectPage`, introselect for ranks
[20, 40) through `TopKSorterPairComparer` → `SortResolver.CompareLeftValues` → `PqbByScoreTies.Compare`),
`MaterializeTopK` (20 `GetValueRefOrAddDefault` inserts), `FillFused` (the chain walk to the join, then
`JoinOneResolver.UnsafeFillFusedRows`: per page row one right-store `TryGet` through an accessor
`GetSlotAt`), `BuildResults` (`FromArray` over the dictionary's values array), and the disposes.

### The four levels

| Level | Method | Removed | Kept |
|---|---|---|---|
| 1 | `ShapeA_Level1` (`StraightLine`) | step objects, bindings, selector and `KeySelector` delegates, the probe list and its two virtual calls per row, the resolver chain and both chain walks, the joined container and its `ValueDictionary`, the accessor | three `TryGetBucket`, the smallest-signal choice (two compares), the seed `CopyKeysTo`, per key one store `TryGet` + two field compares, the collect-all / heap choice, one rent of 111 triples, introselect through a comparer calling `PqbByScoreTies.Compare` directly, 20 right-store `TryGet`s, one pooled result buffer |
| 2 | `ShapeA_Level2` (`PipelinePieces`) | only the per-row `KeySelector` delegate and the `StepBinding.Read` behind each value-side probe (the probe compares a field against the argument), and the bind-time selector delegates | everything else structurally: bind and probe through an interface over a step array and a probe list, the real `SortResolver` inside the real `FrozenTopKJoinedContainer`, `MaterializeTopK` into a `ValueDictionary`, a second fill loop over the page, `FromArray` |
| 3a | `ShapeA_Level3a` | = level 1 (the brief defines 3a as level 1 with the join as today's hash lookups); the same body measured twice, so the pair reads the run's noise floor | |
| 3b | `ShapeA_Level3b` | level 1 with the 20 right-store `TryGet`s replaced by 20 reads of a `PqbCustomer?[100k]` slot array built in `Setup` (a writer-maintained materialized join) | |
| Count 1 | `Count_Level1` (`CountStraightLine`) | as level 1: no container, no page | three `TryGetBucket`, the choice, `CopyKeysTo`, per key one `TryGet` + two field compares |

Plan decisions a generator cannot make at build (which bucket is smallest, heap-or-collect for the page)
stay as the same compares the runtime makes; a generator knows the indexes, the two field compares behind the
probes, the comparer type and the join family, and that is what the straight line fixes.

## 2. The numbers

Apple M4 Pro, .NET 9.0.19 (osx-arm64), Release, `--inProcess --anyCategories FrozenCodegenCeiling`, default job,
`[MemoryDiagnoser]`, one run, 2026-09-12 00:46–00:51. **1-minute load 4.05 before, 2.36 after.** Not a clean
window: another lane's build / `dotnet test` overlapped part of it (the machine ran other agents' test and
benchmark lanes back to back the whole hour, and a second full run at 00:57–01:03, load 4.08 → 4.82, had a full
`Prague.Core.Tests` run inside it). The user accepted this run; the second full run and the two `--quick` runs
agree with it row for row within 3%, and the conclusions below do not turn on any 3%.

| Row | Mean | Ratio to eager | Ratio to frozen | Bytes/op |
|---|---:|---:|---:|---:|
| `ShapeA_Eager` | 16,983 ns | 1.00 | 0.26 | 0 B |
| `ShapeA_Frozen` | 4,493 ns | **3.78×** | 1.00 | 0 B |
| `ShapeA_Level1` — whole plan, straight line | **1,697 ns** | **10.0×** | **2.65×** | 0 B |
| `ShapeA_Level2` — per-row pieces only | 3,713 ns | 4.57× | 1.21× | 0 B |
| `ShapeA_Level3a` — level 1, join as store hash (= level 1, second reading) | 1,689 ns | 10.1× | 2.66× | 0 B |
| `ShapeA_Level3b` — level 1, join as an array read | 1,668 ns | 10.2× | 2.69× | 0 B |
| `Count_Eager` | 10,726 ns | 1.00 | 0.14 | 0 B |
| `Count_Frozen` | 1,458 ns | 7.36× | 1.00 | 0 B |
| `Count_Level1` | **314 ns** | **34.2×** | **4.65×** | 0 B |
| `Probe_NarrowCollect` (attribution) | 443 ns | | | 0 B |
| `Probe_NarrowCollectSelect` (attribution) | 1,621 ns | | | 0 B |

Error / StdDev: every row under 2% except `Count_Level1` (±26 ns). The 3a/1 pair — the same body — reads 8 ns
apart: the run's noise floor. Second full run, for the record: eager 17,378 / frozen 4,530 / level 1 1,715 /
level 2 3,818 / 3a 1,724 / 3b 1,700 / count 10,918 → 1,482 → 364 / probes 467, 1,827 ns.

## 3. Where the remaining cost is

Two attribution probes cut level 1 after each stage (`Probe_NarrowCollect`: bind, seed, walk, collect, no
page; `Probe_NarrowCollectSelect`: plus the page selection, no join, no result). With `Count_Level1` (bind,
seed, walk, no buffer) they split level 1 into its parts; the split is stable across the quick runs and the
full run to within the noise floor the 3a/1 pair reads.

| Part of level 1 | Cost | How it was read |
|---|---:|---|
| Bind (3 `TryGetBucket`), seed choice, `CopyKeysTo` of 111 keys, 111 store `TryGet`s and 222 field compares | 314 ns | `Count_Level1` |
| The buffer rent and 111 triple appends | 129 ns | `Probe_NarrowCollect` − `Count_Level1` |
| The page selection: introselect for ranks [20, 40) over 111 triples, then the page sort — every step a `PqbByScoreTies.Compare` on two 24-byte triples passed by value | 1,178 ns | `Probe_NarrowCollectSelect` − `Probe_NarrowCollect` |
| The join (20 right-store hashes) and the pooled result buffer | 76 ns | `ShapeA_Level1` − `Probe_NarrowCollectSelect` |

**The page selection is the floor.** It is the one primitive level 1 shares with the runtime
(`TopKSelect.SelectPage`, the eager container's own plan choice: k = 40 against 111 rows collects everything
and selects in place), and it is about three quarters of what a straight line still costs. The comparer is
already devirtualized and inlined in level 1; what is left is the algorithm's compares and swaps over a key with
eight distinct values (`Score & 7`), which is the shape's own choice of comparer. A generator cannot remove
this; a different selection (a counting pass when the comparer key is known to be small, or a smaller `k` plan
threshold) could, and would help the runtime just as much.

**The join is not there.** 3b − 3a is −21 ns, inside the 3a/1 noise floor (8 ns between two readings of the same body). Twenty
`TryGet`s on a 75k-entry store against twenty array reads is not a measurable difference on this page size.
`ShapeA_Level1 − Probe_NarrowCollectSelect` (the join plus the result buffer) reads 76 ns, the same verdict.

**What the frozen executor pays over level 1** (4,493 → 1,697 ns, 2,796 ns):

- Level 2 says how much of it is the two per-row costs a small generator change would target — the
  `KeySelector` delegate and the `StepBinding.Read` behind each value-side probe: 780 ns, about 28% of the gap
  (4,493 → 3,713 ns). Real, but well under a third of the gap.
- The rest, 2,016 ns (3,713 → 1,697 ns), is structure that level 2 keeps: two interface-dispatched probes per
  row over a probe list (222 virtual calls), the container's `Add` through the sorter type parameter
  (`SortResolver.CompareLeftValues<TLeft>` is a generic method over a reference-type `TLeft`, so its
  instantiation is the shared-canonical one — a runtime-generic lookup per compare and no inlining down to
  `PqbByScoreTies.Compare`, which is where level 1's direct comparer differs), the `ValueDictionary`
  materialization of the page (20 hashed inserts), the second fill loop over it and the `FromArray` hand-off.
  The chain walks (`WithSorter`, `FillFused`) and the bind-time selector delegates are per execution, not per
  row, and are a small part of this. The comparer hop is the largest suspect but was not isolated; it would
  take one more probe (level 2 with a direct comparer) to split it from the virtual probes.

**Count.** 1,458 → 314 ns: the counting pass has no container and no page, so the two virtual probes
with their delegate and binding read per row are nearly all of the frozen count, and the straight line removes
them. What is left is the seed copy and 111 store hashes — the same as level 1's first row.

## 4. Recommendations

### (i) A generator PR: justified at level 1, not at level 2

**Justified.** Level 1 takes shape A from 4.49 µs to 1.70 µs (2.65× over frozen, 10× over eager)
and its `Count` from 1.46 µs to 0.31 µs (4.65× over frozen), at 0 B and with the same rows. A
generator that emits the straight line pays that per execution of every production call site; nothing else on
the branch's list buys a further 2.6× on the shape that matters most.

**Not at level 2.** Removing only the delegate and the type-erased read (the "small generator change":
generated `IPipelineStep`s that compare a field against the argument) buys 1.21× — real, and cheaper to
build, but it leaves the interface dispatch, the comparer hop and the joined container in place, and three
quarters of the reachable gain on the table. If a generator is built at all it should emit the whole executor.

**What it would emit**, per call site, in the user's partial class:

- A sealed executor with a field per index and right store handed over at build (the plan's runtime objects
  are still instances — the generator knows their *types* and the selector *bodies*, not the fields'
  identities), one `Execute(in TArgs, skip, take)` and one `Count(in TArgs)`.
- Bind: one `TryGetBucket` per list step reading the argument field directly; the seed choice as the
  compares the runtime makes (smallest exact signal, ties to the declared order); the seed copy through
  `CopyKeysTo` into the stack buffer.
- The walk: one loop per possible seed (three here), the other steps as field compares inlined from the index
  key selectors' bodies, one store `TryGet` per key, filters inlined as the user's predicate bodies.
- The page: the eager plan choice (heap or collect) over `TopKSelect` with a comparer struct that calls the
  user comparer directly.
- The join: one right lookup per page row into the result buffer for the fusable `JoinOne` families; a
  `JoinMany` keeps its fill. Anything the generator cannot see (a non-static lambda, a filter callback on a
  join, a composite it does not handle) falls back to `BuildFrozen()` at the same call site — the generator
  must never be a correctness surface, only a fast path.
- The differential tests are the safety net: every generated executor is checked against the frozen
  query's rows and counts, as `Setup` does here.

**Which surface.** Two ways to trigger it:

1. **A `.Compile()` terminal on the existing fluent chain** (the prepared-query recorder). The generator
   walks the chain syntactically with the semantic model: every `UseIndex(_byX, static a => a.field)`,
   `Where(static v => …)`, `SortBounded(new C())`, `JoinOne(_y)` is a method call it can name, whose lambda
   bodies it can lift verbatim and whose index and store *types* it reads from the fields' declared types.
   Attractive because nothing new is learned by users and every call site is already in this shape. Costly
   because the generator has to accept exactly the syntactic forms it recognizes and bail out on everything
   else (a lambda that captures, a builder passed through a helper, a conditional chain), which makes the fast
   path silently shape-dependent — the tax pattern this branch has hit twice, now at build time.
2. **A declarative surface on the partial class** — an attribute or a small DSL on a partial method, say
   `[CompiledQuery] partial FrozenQuery<Args, Row> ByGroupBandLane();` with the shape declared in attribute
   arguments (indexes by member name, sort by comparer type, joins by right-cache member), or a one-line
   query string. The generator parses a closed grammar instead of pattern-matching C#, the accepted set is
   the grammar, and unsupported shapes are compile errors rather than quiet fallbacks. Users write the
   query once more in a second notation, and the lambdas the fluent chain lifts for free (`Where` bodies)
   have to be expressed as member references or predicate method names.

Recommendation: **start with the declarative surface** and keep `.Compile()` on the fluent chain as a later
convenience once the emitted executor is stable. The executor is the same either way (this file is it); the
declarative front end is the smaller and safer generator, it gives compile errors where the fluent one would
give a silent fallback, and its narrow grammar is exactly the set of shapes that measured worth compiling —
list steps, `SortBounded` over the left value, fusable `JoinOne`s, static predicates. Widen it when a shape
outside it is measured.

### (ii) A materialized join: not worth its writer-side invalidation

3b − 3a is −21 ns on a 20-row page, inside the noise floor. The join on the frozen path is already
one hash per page row into a store the writer already maintains; replacing it with a writer-maintained
right-slot array trades a 25-thousandth of a microsecond per row for a second structure the writer must keep
consistent on every add, update and remove on either side, plus its memory. On this shape there is no case
for it. What *would* pay in the join's area is shape B's two chained `JoinOne`s through the resolver chain,
and that is a reshaping question (the chain hop, above), not a materialization one — measure B at level 1
before deciding anything there.

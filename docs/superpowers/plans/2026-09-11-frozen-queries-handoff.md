# Frozen queries — session handoff

> **Branch:** `poc/prepared-query`, HEAD after the ordering relaxation, working tree clean.
> **Tracking issue:** [#83 follow-up index](https://github.com/nanov/prague.net/issues/83) and the
> main tracker **#82**.
> **Written:** 2026-09-11, revised at the end of the session that shipped §3.1 (the ordering relaxation).
> Supersedes the earlier revision of this file; the sections it changed are marked.

Read this first, then the two design docs it points at.

## 1. Where the work stands

`BuildFrozen()` builds a long-lived plan and binds it to one of three executors — `PointLookup`,
`Pipeline` or `Replay`. Stage 3 replaced the replay for nearly every shape with a single-pass pipeline:
seed one index step, probe the rest per candidate, fetch once, filter, feed the eager result container.
**Stage 3 steps 1–8 and the ordering relaxation are all shipped.**

| Step | What it did | Commit |
|---|---|---|
| 1–2 | Index probe APIs; the fixed-seed single-pass pipeline | `1c6c723` |
| 3 | Seed selection, bulk `PooledSet.CopyKeysTo`; retired `IndexStepsExecutor` | `b0c9d39` |
| 6 | `SortBounded` feed — the pass drives the eager top-k containers | `2c85b3c` |
| 5 | `JoinOne` fusion — one point lookup per left, no pair set | `d49db44` |
| 7 | Frozen bounded joined container — the sorter compares as a struct type parameter | `d76d87b` |
| 4 | Composites — `Or` as a step, `If`/`Match` as bind-time tree nodes | `6c69d8e` |
| 8 | `JoinMany` admission + one-pass fused fill; general `Count` fast path | `a91644d` |
| **§3.1** | **The ordering relaxation — the free seed by default, one `PreserveEagerOrder` opt-out** | **`f889c0c`** |

Also on the branch: the lock-vs-lock-free experiment (`a1b3b4e`, lock-free stays) and two eager bug
fixes — `StableSort` boxing a struct comparer (`a0b1ef4`) and the chained-inner-join empty-pair-set
defect (in `d49db44`).

### The ordering contract (shipped — this is now behaviour, not a plan)

**"Order is not important if not specified."**

- An **unsorted** frozen result has **no row-order guarantee**.
- **Ties** inside a sorted result are **unspecified** too.
- `Sort` / `SortBounded` with a **total** comparer is **byte-identical** to eager.
- Always unchanged: same rows, same `Count` / `TotalCount` / `Truncated`, pages are slices of the same
  whole, clone timing, pooling, 0 B, leak safety.

`FrozenOptions` is **6 flags**: `FuseFilters`, `AdaptiveFilterOrdering`, `CapacityHints`, `Pipeline`,
`IndexSideProbes`, `PreserveEagerOrder`. The last is the single opt-out that restores the eager sequence
everywhere at once — the fixed seed, the eager `Or` store walk and the left-symmetric inner-join replay.
`ReorderIndexNarrowers`, `FuseSymmetricInnerJoins` and `OrSeed` are gone.

Retired with the fixed-order machinery: `SeedMode.SmallProbe`, `PipelineCore.SortBySlot`,
`IPipelineStep.SlotAddressable` / `TryGetSlot` and the step overrides, the key-set index's `TryGetSlot`,
`PooledSet.TryGetSlot` (and `PooledSetTryGetSlotTests`), `PipelineLimits.SlotStackInts`. **Under the
opt-out a multi-step plan now walks the first declared step outright** — slower than the small probe
was (`ListList` 7.63 µs against the old 1.72 µs), correct, and off the default path.

### Headline numbers (eager → frozen, 0 B per execution everywhere)

Full sweep of all 54 categories, one per run, 2026-09-11 17:10–17:52 — `RESULTS.MD`, "the ordering
relaxation". The two **production shapes**:

| Shape | Eager | Frozen | Ratio | Was |
|---|---:|---:|---:|---:|
| A: 3 list indexes → `SortBounded(page)` → `JoinOne` | 17.09 µs | **4.56** | **3.7×** | 3.4× |
| A `Count` | 11.00 µs | **1.42** | 7.7× | 7.5× |
| B: newer-than-T → 2 lists → `SortBounded` → 2 `JoinOne`s | 33.90 µs | **18.51** | 1.82× | 1.9× |
| B, range-on-timestamp variant | 36.98 µs | **20.78** | 1.79× | 2.1× |
| A with a `JoinMany` instead | 18.96 µs | **5.15** | **3.7×** | 3.4× |

Others: `ListList` 9.26 → **1.29 µs** (7.2×, was 1.72), `ListListList` 11.18 → **1.78** (6.3×, was
2.25), `SortBounded_ListList` 11.33 → **3.42** (3.3×), `ListRange` 398 → 11.2 (35×), `ListLastUpdated`
216 → 9.66 (22×), `Or` 976 → 26.9 µs (36×), `OrAfterList` 7.13 µs → 249 ns, `IfTaken` 3.03 µs → 123 ns,
`Sort_JoinMany` 118 → 17.2 (6.9×), `Count_ListList` 8.69 → 0.969 (9.0×), point lookups 132 → 18–25 ns.

**What the relaxation itself bought:** the `SortBounded` shapes (A and its `JoinMany` twin,
`SortBounded_ListList`) and the multi-list narrowings declared worst-first (`ListList`, `ListListList`).
Everything else — shape B, every `Count_*` row, every join row, the single-step plans — is flat, which is
the check that the change did not leak into plans it had no business touching.

**Unresolved, do not read as a result:** `ListWhere` now reads 1.47× (was ~1.10×) and `IfSkipped` 1.15×
(was 1.29×). Neither plan changed. This sweep ran at a 1-minute load of 2.7–5 against the 4–6 the earlier
numbers were taken at; both are single-run readings on rows with almost no walk to amortise their fixed
cost. They were deliberately **not** re-measured. See §4.

### Test and build state

- `Prague.Core.Tests`: **1544 passed, 1 skipped, 0 failed** — net9.0 and net10.0.
- `Prague.Generated.Tests`: **1224** — both frameworks.
- `dotnet build Prague.sln -c Release`: 0 warnings, 0 errors.
- `Prague.Kafka.IntegrationTests`: **not run** — they need Docker (Testcontainers), which was not up.
- `Prague.DI.Tests`: not run this session.

### How the test suites are organised after the relaxation

This matters before touching them:

- **Sequence assertions run through the opt-out.** Each affected fixture has a local
  `EagerOrder`/`PinnedSeed` = `new FrozenOptions { PreserveEagerOrder = true }`, and the byte-identical
  differential helpers (`AssertSame`, `AssertSameJoined`, `AssertPipeline`, `AssertSequence`) are fed
  frozen queries built with it. These remain the project's strongest safety net — do not weaken them.
- **The default path has its own assertions**: `AssertSameRows` / `AssertSameJoinedRows` (multiset +
  counts) and `AssertSameJoinedCounts` (counts only — a *page* is not comparable to eager's once a tie
  has crossed a page boundary; only the whole result is). New in
  `PreparedQueryProductionShapeDifferentialTests`: `A_DefaultSeed_…` and `B_DefaultSeed_…` pin the whole
  result's multiset, the **sorted key sequence** (so only comparer-equal rows may have moved) and pages
  partitioning the frozen query's own whole.
- **Some byte-identity tests still pass on the default path by coincidence** — the fixtures insert rows
  in Id order, so a bucket walk and a smaller bucket's walk often yield the same ascending sequence.
  That is a free canary, not a contract. If one of them starts failing after an unrelated change, check
  whether the sequence was ever owed before "fixing" it.
- **Staleness tests pin the seed** (`FrozenPipelineTests`, `PinnedSeed`): they are about which side
  judges a row, so the step that walks must not be chosen by live signals.

## 2. What to do next, in order

### 2.1 `Match` — make `Default` mandatory via the type system

Asked for two sessions ago and still open. Today an unmatched tag with no `Default` is a silent no-op
(the query simply does not narrow). Guard it with type-state so `Match` only accepts a closed arm chain,
the way `Case`-after-`Default` is already a compile error. Self-contained; codegen + `Narrowers.Match.cs`
+ `PreparedQueryBuilderMatchExtensions.cs`.

### 2.2 The acceptance harness — the original goal

> "work until we have a highly optimized frozen queries protocol, **one writer many readers**"

Still nothing built beyond the lock-vs-lock-free experiment. Wanted: a concurrent harness running
production shapes A and B with **8 readers plus a churning writer**, reporting reader p50/p99,
throughput, writer stall, eager vs frozen. From `a1b3b4e`, the metric to improve is **reader p99 under a
max-rate writer (827 µs)**; suspects named there are bucket retirement and store lock striping. Expect to
need `perf/` baselines updated too.

Note the relaxation helps here in principle — a free seed walks fewer keys, so a reader holds its gate
pin over a shorter span — but nothing has measured that.

### 2.3 Resolve the two unresolved rows

`ListWhere` and `IfSkipped` (above). One clean run of both categories on a settled box decides whether
the earlier sub-bar readings were load artefacts. Cheap; do it before anyone quotes either number.

### 2.4 Parked / deferred

- **Struct filter type parameter on the eager core** (`CacheQueryBuilderCoreCombined<TKey,TValue,TFilter>`)
  to replace the predicate pool and the eager `&&` closure. The way past the delegate call that floors
  `ListWhere` and `ListKeySet`. Invasive (codegen reassignment, `Or`/paired cores) — own PR.
- **TimeRange index** (issue #83) — "everything since T" as a first-class index.
- Fragment memoization, materialized key sets, `ExecuteMany(ReadOnlySpan<TArgs>)` — see #82.
- Shapes that still replay: a nested `JoinMany`; a filtered `JoinOne` outside the step-6 shape; a
  `Range`/`KeySet`/`LastUpdated*` step that is not first in an `Or` branch; a nested `Or` beside a leaf
  in its branch; filter-only plans; `Pipeline = false`; **and, under `PreserveEagerOrder`, any chain with
  an inner left-symmetric `JoinOne`**.

## 3. Operational notes that will save hours

- **Benchmarks.** Always `--inProcess` and `-f net9.0` (the project multi-targets; `dotnet run` refuses
  without it). One category per run, never two jobs at once. Extract with
  `awk '/^\| Method/{p=1} p{print} /^$/{if(p)exit}'`. A full 54-category sweep takes **~1h45m** at ~2 min
  a category; the script used this session is worth keeping (loop over `--anyCategories <cat>`, append).
- **This machine's load floor is not fixed.** The previous session recorded 4–6 and never below 3; this
  session ran at 2.7–5. That difference is large enough to move the 5–10 µs rows by more than the deltas
  being claimed, which is exactly what happened to `ListWhere` and `IfSkipped`. Record the load with the
  numbers.
- **Watch for the tax pattern.** Twice a feature quietly slowed plans that did not use it. Anything added
  must be decided at build, not per row. Only re-measuring shapes A/B caught it both times — the full
  sweep now does that job properly.
- **The editor language server reports stale errors** that a real compile does not reproduce. Trust
  `dotnet build`.
- **T4:** `~/.dotnet/tools/t4 JoinResults.tt -o <dir>/<name>.cs` from `src/Prague.Core` writes
  `<name>.generated.cs`. It does **not** run on build — regenerate manually and diff. Untouched this
  session.
- **Docker** must be running for `Prague.Kafka.IntegrationTests`; 55 tests fail instantly without it and
  the failure looks alarming but is only the socket.

## 4. Key documents

| File | What it holds |
|---|---|
| `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` | The pipeline design — **read the supersession banner first**: everything it says about preserving the eager encounter order is history |
| `docs/superpowers/specs/2026-09-09-prepared-query-command-design.md` | The prepared-query model and §8, a write-up per shipped step |
| `benchmarks/Prague.Benchmarks/RESULTS.MD` | Every measured table, each with a "Reading it" section; the relaxation's sweep is the last one |
| `context/query.md` | The runtime description a reader should trust — the ordering contract lives under `BuildFrozen()` |
| `context/joins.md` | Join resolvers; inner LeftSym fusing is described there |
| `README.md` → "`BuildFrozen()` — the same query, planned once" | The public-facing contract and the measured table |
| Issue #82 | A comment per step with its tables and decisions |

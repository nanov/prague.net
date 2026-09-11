# Frozen queries — session handoff

> **Branch:** `poc/prepared-query`, 19 commits ahead of `main`, HEAD `a91644d`, working tree clean.
> **Tracking issue:** [#83 follow-up index](https://github.com/nanov/prague.net/issues/83) and the
> main tracker **#82** (every step below has a comment there with its measured tables).
> **Written:** 2026-09-11, at the end of the session that shipped stage 3 steps 1–8.

Read this first, then the two design docs it points at. Everything here is verified on a settled
machine unless marked otherwise.

## 1. Where the work stands

`BuildFrozen()` builds a long-lived plan and binds it to one of three executors — `PointLookup`,
`Pipeline` or `Replay`. Stage 3 replaced the replay for nearly every shape with a single-pass
pipeline: seed one index step, probe the rest per candidate, fetch once, filter, feed the eager
result container. **All planned steps are shipped.**

| Step | What it did | Commit |
|---|---|---|
| 1–2 | Index probe APIs; the fixed-seed single-pass pipeline | `1c6c723` |
| 3 | Seed selection (small-probe, free seed for `Count`/classic `Sort`), bulk `PooledSet.CopyKeysTo`; retired `IndexStepsExecutor` | `b0c9d39` |
| 6 | `SortBounded` feed — the pass drives the eager top-k containers, joined `SortBounded` leaves replay | `2c85b3c` |
| 5 | `JoinOne` fusion — one point lookup per left, no pair set | `d49db44` |
| 7 | Frozen bounded joined container — the sorter compares as a struct type parameter | `d76d87b` |
| 4 | Composites — `Or` as a step, `If`/`Match` as bind-time tree nodes | `6c69d8e` |
| 8 | `JoinMany` admission + one-pass fused fill; general `Count` fast path | `a91644d` |

Also on the branch: the lock-vs-lock-free experiment (`a1b3b4e`, lock-free stays) and two eager bug
fixes that were previously unpinned — `StableSort` boxing a struct comparer (`a0b1ef4`) and the
chained-inner-join empty-pair-set defect (in `d49db44`).

### Headline numbers (eager → frozen, 0 B per execution everywhere)

The two **production shapes** are the ones that matter most:

| Shape | Eager | Frozen | Ratio |
|---|---:|---:|---:|
| A: 3 list indexes → `SortBounded(page)` → `JoinOne` | 16.96 µs | **4.93** | 3.4× |
| A `Count` | 10.87 µs | **1.45** | 7.5× |
| B: newer-than-T → 2 lists → `SortBounded` → 2 `JoinOne`s | 33.8 µs | **17.95** | 1.9× |
| B, range-on-timestamp variant | 36.7 µs | **17.55** | 2.1× |
| A with a `JoinMany` instead | 19.0 µs | **5.63** | 3.4× |

Others worth knowing: `ListRange` 401 → 12.5 µs (32×), `ListLastUpdated` 219 → 10.8 (20×),
`Count_ListList` 8.64 → 0.954 (9.1×), `Sort_JoinMany` 125.8 → 17.2 (7.3×), `OrAfterList` 7.16 µs →
237 ns, `IfTaken` 3.08 µs → 108 ns, point lookups 131 → 18–26 ns.

Two rows sit below their bars, both documented rather than hidden: `IfSkipped` 1.29× (bar 1.4×) and
`ListWhere` ~1.10× (bar 1.5×). Both are per-execution fixed cost on plans with almost no walk to
amortise it; the decompositions are in `RESULTS.MD`.

### Test and build state

- `Prague.Core.Tests` Debug, both target frameworks: **1546 passed, 1 skipped, 0 failed**.
- `Prague.Core.Tests` `~Prepared` Release: **440**.
- `Prague.Generated.Tests`: **1224**.
- `dotnet build Prague.sln -c Release`: 0 warnings, 0 errors.
- `JoinResults.generated.cs` verified byte-identical to a fresh `t4` run.

## 2. The one thing to understand before changing anything

**The ordering contract, decided by the repo owner this session: "order is not important if not
specified."**

- An **unsorted** frozen result has **no row-order guarantee**.
- `Sort` / `SortBounded` with a **total** comparer **must stay byte-identical** to eager.
- **Ties** inside a sorted result are **unspecified** too (the comparer does not define them).
- Always unchanged: same rows, same `Count` / `TotalCount` / `Truncated`, pages are slices of the
  same whole, clone timing, pooling, 0 B, leak safety.

This was decided *after* steps 3–7 were built, so **the code still contains machinery that exists
only to preserve an order nobody is entitled to**. Retiring it is the next task (§3.1).

Until then the defaults are conservative: `ReorderIndexNarrowers` and `FuseSymmetricInnerJoins` are
opt-in, and the small-probe seed sorts survivors back into eager's order. The one exception already
shipped is `OrSeed`, which **defaults on** — an Or-first query's eager order is the store's hash
enumeration order, which moves whenever the store resizes, so there is no stable order to preserve
and reproducing it costs a full store walk (757–794 µs against 26.6–28.0).

## 3. What to do next, in order

### 3.1 The ordering relaxation (highest value, fully specified, not started)

A full brief was written and an agent launched twice; both died to model rate limits before doing
any work. The tree is untouched. Deliverables:

- **A.** Free seed becomes the default for unsorted `Execute*`. Measured on `ListList`: 1.65 µs
  fixed vs **1.22–1.27 µs** free.
- **B.** Retire the small-probe slot sort (design §3.4, `PipelineCore.SortBySlot`). It exists only
  to restore eager's encounter order. `PooledSet.TryGetSlot` / `IPipelineStep.SlotAddressable` may
  become dead — retire them if so.
- **C.** `SortBounded` seeds free (its tie-break is the encounter ordinal, and ties are now free).
  Verify a **total** comparer still gives byte-identical pages.
- **D.** Fuse inner left-symmetric `JoinOne` by default; `FuseSymmetricInnerJoins` becomes
  unnecessary.
- **E.** One opt-out flag (`PreserveEagerOrder`) replacing `ReorderIndexNarrowers` and
  `FuseSymmetricInnerJoins`, which genuinely restores byte-identical order everywhere.
- **F.** Document the contract in `context/query.md`, the README and the `FrozenOptions` comments.

**Test strategy matters here.** The byte-identical differential tests are this project's strongest
safety net — they caught the step-5 eager defect and the step-4 composite tax. Do **not** weaken them
into multiset checks wholesale. Keep them byte-identical but run them **through the
`PreserveEagerOrder` opt-out**, and add default-path assertions pinning the multiset, the counts and
page-partitioning. Sorted-with-a-total-comparer keeps byte-identity on the default path.

Re-measure **every** benchmark row afterwards: this changes the default plan for almost all of them.

### 3.2 `Match` — make `Default` mandatory via the type system

Asked for earlier and still open. Today an unmatched tag with no `Default` is a silent no-op (the
query simply does not narrow). Guard it with type-state so `Match` only accepts a closed arm chain,
the way `Case`-after-`Default` is already a compile error.

### 3.3 Cleanup

Retire whatever the relaxation makes dead, re-check the `FrozenOptions` surface reads coherently
(it is now 8 flags: `FuseFilters`, `AdaptiveFilterOrdering`, `CapacityHints`,
`ReorderIndexNarrowers`, `Pipeline`, `FuseSymmetricInnerJoins`, `OrSeed`, `IndexSideProbes`), and
make sure `context/` and the specs describe the shipped state rather than the plan.

### 3.4 The acceptance harness — the original goal

> "work until we have a highly optimized frozen queries protocol, **one writer many readers**"

Nothing has been built for this yet beyond the lock-vs-lock-free experiment. Wanted: a concurrent
harness running production shapes A and B with **8 readers plus a churning writer**, reporting reader
p50/p99, throughput, writer stall, eager vs frozen. From `a1b3b4e`, the metric to improve is **reader
p99 under a max-rate writer (827 µs)**; suspects named there are bucket retirement and store lock
striping. Expect to need `perf/` baselines updated too.

### 3.5 Parked / deferred

- **Struct filter type parameter on the eager core** (`CacheQueryBuilderCoreCombined<TKey,TValue,TFilter>`)
  to replace the predicate pool and the eager `&&` closure. This is the way past the delegate call
  that floors `ListWhere` and `ListKeySet`. Invasive (codegen reassignment, `Or`/paired cores) — own PR.
- **TimeRange index** (issue #83) — "everything since T" as a first-class index with the same
  guarantees as the B+tree.
- Fragment memoization, materialized key sets, `ExecuteMany(ReadOnlySpan<TArgs>)` — see #82.
- Shapes that still replay: a nested `JoinMany`; a filtered `JoinOne` outside the step-6 shape; a
  `Range`/`KeySet`/`LastUpdated*` step that is not first in an `Or` branch; a nested `Or` beside a
  leaf in its branch; filter-only plans; `Pipeline = false`.

## 4. Operational notes that will save hours

- **Benchmarks.** Always `--inProcess`, **one category per run**, never two jobs at once (a stale
  worktree under `.claude/worktrees/` duplicates the project name otherwise). Extract with
  `awk '/^\| Method/{p=1} p{print} /^$/{if(p)exit}'`.
- **This machine idles at load 4–6 and never drops below 3.** Do not wait for a quiet box. Subagent
  numbers ran **5–15% high** versus a settled machine throughout this session; always re-measure the
  production shapes yourself before believing a regression. Rows in the 6–10 µs range swing ~10% run
  to run — report a range, not the best reading.
- **Watch for the tax pattern.** Twice a feature quietly slowed plans that did not use it (step 4's
  per-row branch-filter test and per-execution `Or` check; step 8's `Count` fetching unused values).
  Neither showed up in the new rows — only re-measuring shapes A/B caught them. Anything added must
  be decided at build, not per row.
- **The editor language server reports stale errors** that a real compile does not reproduce. Trust
  `dotnet build`.
- **T4:** `~/.dotnet/tools/t4 JoinResults.tt -o <dir>/<name>.cs` from `src/Prague.Core` writes
  `<name>.generated.cs`. It does **not** run on build (`TextTemplatingFileGenerator` is IDE-time), so
  regenerate manually and diff against the tracked file.
- **Subagent rate limits** ended four tasks mid-flight this session. Work survives in the tree; check
  `git status` and `dotnet build` before assuming anything is lost, and resume rather than restart.

## 5. Key documents

| File | What it holds |
|---|---|
| `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` | The pipeline design — seed rules §3, composites §5, joins §7, sort §8, fallback matrix §9, bars §12, the step plan §13, risks §14 |
| `docs/superpowers/specs/2026-09-09-prepared-query-command-design.md` | The prepared-query model and §8, which carries a write-up per shipped step |
| `benchmarks/Prague.Benchmarks/RESULTS.MD` | Every measured table, each with a "Reading it" section |
| `context/query.md`, `context/joins.md` | The runtime description a reader should trust |
| Issue #82 | A comment per step with its tables and decisions |

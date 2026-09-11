# Task brief — the frozen-query codegen ceiling

> **For:** a separate session or agent in an isolated worktree off `poc/prepared-query`.
> **Tracker:** #82. **Written:** 2026-09-11, from the plan that closes the frozen-queries branch.
> **Deliverable:** a number and a recommendation, not a generator.

## Goal

Measure how much of a frozen query's **per-execution** cost a source generator could remove if it emitted
one specialized, straight-line executor per `Prepare()…BuildFrozen()` call site. Build-time cost is paid once
per plan and is not a target. The result decides two things for the branch: whether a generator PR is
justified and at which level, and whether joins on the frozen path should be reshaped or materialized.

## Read first

- `CLAUDE.md` (commands, invariants, the benchmark rules).
- `context/query.md` § Prepared queries — the runtime description to trust, including the ordering contract.
- `context/joins.md` — resolver families and the fused `JoinOne` loop.
- `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` — read the supersession banner first.
- `benchmarks/Prague.Benchmarks/RESULTS.MD` — the last section, "the ordering relaxation", for shape A's row.
- `src/Prague.Core/QueryBuilders/Prepared/Pipeline/PipelineExecutor.cs`, `PipelineJoinedExecutor.cs`,
  `PipelineStep.cs` (`StepBinding`), `Steps/*.cs`, `src/Prague.Core/QueryBuilders/JoinOneResolver.cs:207-224`
  (the fused fill loop) — this is the code your hand-written executor replaces.
- `benchmarks/Prague.Benchmarks/FrozenQueryBenchmarks.cs:1319-1340` — shape A's eager / prepared / frozen
  triple, and `:365-368` where the frozen plan is built. Fixture: `_items` (100k `PqbItem`), list indexes
  `_byGroup` / `_byBand` / `_byLane`, comparer `PqbByScoreTies`, right store `_details` (`PqbCustomer`, keyed by
  the item id — the PK-to-PK `JoinOneResolver` family, one store hash per row), args
  `(int group, int band, int lane)` = `_threeListArgs`, page `ExecutePooled(20, 20)`.

## What to build

One new file, `benchmarks/Prague.Benchmarks/FrozenCodegenCeilingBenchmarks.cs`, on the **same fixture and
arguments** as shape A (`ListListListSortBoundedJoinOne`: three list-index narrowings → `SortBounded(page)`
→ `JoinOne`), with the existing `_Eager` and `_Frozen` bodies copied in as the category's baseline and
reference. Then hand-write, once, exactly the code a generator would emit, at these levels — one benchmark
method each, same category:

1. **Whole-plan straight line.** Seed the smallest of the three buckets, probe the other two `PooledSet`s
   inline, feed the bounded top-k container, then the `JoinOne` inline via the right store's `TryGet`.
   No `IPipelineStep`, no `StepBinding`, no delegate: the arguments are read as fields of the args struct.
2. **Per-row pieces only.** Keep the pipeline structure (seed / probe / feed as today) but replace the two
   costs a small generator change would target: the delegate calls into user lambdas and the
   type-erased `StepBinding` reads. This measures what a cheap generator buys.
3. **The join, two ways.** Level 1 with the join as today's hash lookups (3a), and level 1 with the join
   as an **array read** over a precomputed right-slot array built in `Setup` (3b — simulating a
   writer-maintained materialized join). The gap 3b − 3a is the whole case for materialized joins;
   3a − frozen is the case for reshaping the join without new writer-side state.
4. **Shape A `Count`** at level 1 (`Count_ListListListSortBoundedJoinOne` is the reference row).

Every method executes pooled and disposes, allocates **0 B per operation**, and returns the same rows,
`Count` and `TotalCount` as the frozen query — assert that once in `Setup` (build both, execute both, compare
the multiset of keys and the counts; an unsorted tie may reorder, a sorted total comparer may not).

## Rules

- `dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --quick --target FrozenCodegenCeiling`
  steers the work (short job, ~2 min, prints one line per method with mean / ratio / bytes).
- Final numbers come from **one full single-category run**:
  `dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories FrozenCodegenCeiling`.
  Record the 1-minute load (`uptime`) next to the table. Nothing else running.
- Do **not** run the full 54-category sweep. Do **not** touch `src/`. Do **not** hand-edit `*.generated.cs`.
- House style: `code-style` and `high-performance-net` skills apply to the benchmark code too.

## Deliverable

`docs/superpowers/specs/2026-09-1x-frozen-codegen-ceiling.md` with:

- The table: eager / frozen / level 1 / level 2 / level 3a / level 3b / `Count` level 1 — mean, ratio to
  eager, ratio to frozen, bytes per op, plus the load.
- Per level: what it removed and what remained, with the remaining cost attributed (store hashes, top-k
  compares, bucket walk).
- The recommendation: (i) is a generator PR justified, at which level, and roughly what it would emit;
  (ii) is a materialized join worth its writer-side invalidation, given 3b − 3a.

Post the table and the two recommendations as a comment on #82. Leave the benchmark file in the worktree
branch; whether it merges is decided with the recommendation.

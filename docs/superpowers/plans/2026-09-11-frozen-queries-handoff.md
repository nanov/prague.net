# Frozen queries — session handoff

> **Branch:** `poc/prepared-query`. **Tracker:** #82 (main), follow-ups #84–#93, #95.
> **Written:** 2026-09-11 after the ordering relaxation; **revised 2026-09-12** at the end of the closing
> session (the grilling plan is `~/.claude/plans/ethereal-beaming-prism.md` on the author's machine; the
> decisions it recorded are all on #82). Read this first, then `context/query.md` and `context/joins.md`.

## 1. Where the work stands

`BuildFrozen()` is complete and reviewed. The branch is the integration branch; every lane ran in a
worktree off it and merged back; the PR to `main` is the final step of this session. Full suite green at
every checkpoint (Core 1,598, Generated 1,224, Kafka 73 + 55 integration under Docker, both TFMs), build
0 warnings, `perf/` latency metrics within tolerance or improved.

### Shipped 2026-09-11 (the pipeline; see the earlier revision's table, commits `1c6c723` → `f889c0c`)

Stage 3 steps 1–8 and the ordering relaxation. The contract: **order is not part of the result unless
sorted**; ties unspecified; `Sort`/`SortBounded` with a total comparer byte-identical to eager;
`PreserveEagerOrder` is the single opt-out. Six `FrozenOptions` flags.

### Shipped 2026-09-12 (the closing session), in merge order

| Commit | What | Measured |
|---|---|---|
| `7ef34ab` | `--quick` benchmark switch; `LangVersion` 14.0; SDK 10 in publish/release workflows; codegen-ceiling brief | 1:43 wall, ratios match full runs |
| `fa60800` | `Match` requires a `Default` arm (closed-chain type-state), `Default()` | build-time only |
| `a8c6166` | arg `Where` predicate takes `TArgs` by `in` (`ArgFilter`); selectors stay `Func` | `ListTwoArgWheres` 13.59 → 9.79 µs (1.25× → 0.90× of eager) |
| `0cc5a4f` | `readonly Resolvers.Resolver`; `StepBinding.Read`'s `readonly` defended | no per-execution chain copy |
| `f3a8eea` | the codegen ceiling: shape A hand-written at four levels | 4.49 → 1.70 µs reachable; `Count` 1.46 → 0.31; materialized join = noise → #93 |
| `769c410` | `where TArgs : struct` everywhere (176 clauses + codegen) | — |
| `e79d0f0`, `1a29961` | guard-form `Match`; `If`/`IfElse` as sugar over it; `IfNarrower`/`IfSelector`/bind-nothing path gone | `IfSkipped` 9.00 → 8.47 µs; `IfTaken` bimodal (103/124 ns) unchanged |
| `2ccc76a`, `ad38e86` | `FrozenFkJoinBenchmarks`: the generated `JoinWith…` path under `BuildFrozen()` | 1.4–2.0× over eager on all seven shapes |
| `fc31ad9` | **bug:** `_manyHints` sized 8, generated fill indexes to 15 → OOB past seven joins; T4-emitted constant + arity guard | — |
| `fda6e9c` | filtered `JoinOne` fuses when the callback is a value predicate (build-time probe) | `Fk_ManyToOne_Filtered` 40.9 → 27.8 µs; `JoinOneFiltered` (degenerate fixture) parity |
| `f3a7764` … `d3ed565` | the two-axis review: 778 dead codegen lines, empty `Prague.DI.Tests` out of the filter, `IFusableJoinOne` retired, docs drift, the T4 command in CLAUDE.md fixed, `FusedFilter.Reorder` guard order, `perf/` alloc artefact explained | A/B flat |
| `9de3656` … `f56a3a5` | docs-site page for prepared/frozen; `sweep.sh`; RESULTS.MD closing section | — |
| `553f8af`, `e09a90a`, `9c6fbb7`, `285004d` | the reshaping lanes' **negative results** written up (below) | — |

### The negative results, and what they establish

The ceiling attributed shape A's 2.8 µs gap to four runtime costs. Each was built and measured against a
same-session baseline with untouched control rows:

- **Comparer hop (R1a):** built, no change on A/B. A constrained call on a struct receiver is resolved at
  JIT time; A/B take the collect-all plan, not the heap plan the probe exercised.
- **Page materialization (R3):** built (a second T4 accessor family), no change. The whole
  `ValueDictionary` path is < 100 ns of A. Branch `r3-page-materialize-killed`.
- **Struct key selectors (R2):** built, **+8–41% regression**. Dynamic PGO already devirtualizes and
  inlines the monomorphic probe call site, delegate included; per-index step types made it polymorphic.
  Branch `r2-struct-selectors-killed`.
- **Four join-loop items:** killed with evidence (RESULTS.MD "Killed").
- **Page selection (direct-comparer probe, R1b/R4a):** _[filled in when the probe lane reports]_

Conclusion: on shapes A and B the frozen pipeline is at the practical floor of its type-erased design.
The remaining 2.6× is the shared call site and the type-erased steps, removable only by a generated
straight-line executor per plan — #93, deferred until further notice.

## 2. What to do next

1. **Open the PR** `poc/prepared-query` → `main`, history kept (this session's last step).
2. **#83 — TimeRange index**, the next branch. Groundwork is on the plan file and summarised on #83;
   the design question to settle first is whether a bucket ring is needed at all or the win is the bulk
   `CopyKeysTo` seed vs the tree walk — measure the seed before choosing the structure. Probe side needs
   zero change. The `perf/` baseline models carry no last-updated index, so the ingestion bar needs a
   dedicated case.
3. **#93 — compiled queries** when told. The two negative results above strengthen its level-1
   recommendation and rule out a level-2 generator.
4. Follow-ups filed: #84 harness, #86 struct filter type parameter, #87 replay shapes, #88 Stage 4,
   #89 `ExecuteMany`, #90 query-string API, #91 index+store hash, #92 codegen FK gaps, #95 dead public
   surface. #85 closed by measurement.

## 3. Operational notes that will save hours

- **Worktree agents branch from `main` by default.** Every lane must `git checkout -B <branch> <tip>`
  first; every brief this session said so, and every lane needed it.
- **A `pgrep -f` guard matches its own shell** if the pattern text appears in the command line. Use the
  bracket form: `pgrep -f 'testhos[t]|bin/Release/net[0-9.]*/Prague.Benchmark[s]'`.
- **T4:** `~/.dotnet/tools/t4 JoinResults.tt -o JoinResults` from `src/Prague.Core` — the basename, no
  extension (the template appends `.generated.cs`; the old `-o <Name>.generated.cs` wrote a stray
  `.generated.generated.cs`, left the real file untouched and broke the build).
- **Same-session baselines.** Recorded tables drift with load (`JoinOne` 19.9 recorded vs 21.2–21.6 on the
  day); always re-take the rows you compare in the same session, and include untouched control rows so
  the drift is visible.
- **`IfTaken_Frozen` is bimodal** on this box (~103 or ~124 ns per process); `perf/` alloc readings of
  1–2 B/op are pool re-warm, not allocations (`main` trips them too).
- **Dynamic PGO is part of the baseline.** A monomorphic interface call site is already devirtualized and
  inlined; "removing a delegate" by specialising types can make things slower (R2).
- **`--quick`** steers (~2 min); a keep-or-kill decision still takes one full single-category run.
  `sweep.sh` runs any category list one at a time with the load recorded; the full 62 is ~2 h and was
  deliberately not run at the close — every change was measured at its own step.
- Write agent log files inside the worktree, not `/tmp` (one lane lost its output there).
- **Docker** must be up for `Prague.Kafka.IntegrationTests`; `Prague.DI.Tests` holds no tests and is out
  of the filter — fill it or delete it.

## 4. Key documents

| File | What it holds |
|---|---|
| `context/query.md`, `context/joins.md`, `context/generated.md` | the runtime description to trust |
| `www/docs/articles/core-concepts/prepared-queries.md`, `README.md` § Prepared / `BuildFrozen()` | the public contract |
| `benchmarks/Prague.Benchmarks/RESULTS.MD` | every measured table; the last five sections are this session |
| `docs/superpowers/specs/2026-09-12-frozen-codegen-ceiling.md` | the ceiling, with the R2 correction on #93 |
| `docs/superpowers/plans/2026-09-11-codegen-ceiling-task.md` | how the ceiling was briefed (template for future measured spikes) |
| Issue #82 | the decision log, one comment per merge |

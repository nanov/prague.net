# PQL — a query DSL over frozen queries: research handoff

> **Branch:** `research/dsl`, fast-forwarded to `poc/prepared-query` @ `fa60800`, working tree clean.
> **Written:** 2026-09-12, at the end of the exploration session.
> **Deliverable is a DESIGN DOC. Do not write a lexer.**

## 0. The goal, stated precisely

Decide whether Prague should grow a textual query DSL that a source generator compiles into the
existing frozen-query API — and if so, what it looks like. The output of this work is
`docs/superpowers/specs/2026-09-12-pql-dsl-research.md` (the `*-research.md` suffix, as in
`2026-09-09-single-threaded-event-loop-research.md`, is the house spelling for a design-only spec).

**No implementation.** An earlier plan for this work included "prototype the front-end in
`src/Prague.Codegen/`"; that was cut. The research question is whether the language is worth
learning, and that is answered on paper — by writing the grammar and the refusal matrix and looking
at them — not by building a parser. If the design survives review, implementation is a separate,
later decision.

## 1. Why a DSL at all

`BuildFrozen()` plans once and binds `PointLookup` or `Pipeline`; the payoff is measured and large
(point lookups 132 ns -> 18-25 ns, production shape A 17.09 -> 4.56 us, `Or` 976 -> 26.9 us, **0 B per
execution everywhere** — `benchmarks/Prague.Benchmarks/RESULTS.MD`).

The cost is authoring. From `tests/Prague.Core.Tests/Prepared/PreparedQueryProductionShapeDifferentialTests.cs`:

```csharp
_items.Prepare<int, PqItem, (int group, int band, int lane)>()
  .UseIndex(_byGroup, static a => a.group)
  .UseIndex(_byBand,  static a => a.band)
  .UseIndex(_byLane,  static a => a.lane)
  .SortBounded(new ByIdMod5())
  .JoinOne(_details)
  .BuildFrozen();
```

Three type parameters, an arg tuple threaded through three `static a =>` selectors, a comparer
passed by instance, and an executor choice implicit in the chain's shape. All of it is bookkeeping
the generator already has the information to do.

## 2. Decisions already locked (do not relitigate)

| Decision | Choice | Why |
|---|---|---|
| When it runs | **Compile-time only** | Runtime parsing forfeits the monomorphization the frozen path exists for |
| Syntax family | **Pipeline stages, SQL keywords** (KQL-shaped, SQL vocabulary) | Familiarity lives in the keywords (`where`/`and`/`join`/`sort by`/`take`), not clause order — and it avoids promising `GROUP BY`, subqueries, arbitrary `JOIN ... ON`, none of which the index engine can serve |
| Scope | **Whatever `BuildFrozen()` binds** | `PointLookup` + `Pipeline`. Anything falling back to `Replay` is refused — the value proposition and the scope boundary become the same line |
| Lowering | **Through the generated `With*` surface**, not raw `UseIndex` | The C# compiler then validates the plan for us — see §4 |

Rejected along the way, with reasons worth keeping:

- **A SQL subset.** sqlc works precisely because it *never* subsets SQL — the text goes verbatim to a
  real engine and the generator only does typing. Prague has no SQL engine, so a SQL surface must be
  a subset, and every gap becomes an arbitrary-feeling compile error. The killer case: `Sort` vs
  `SortBounded` has **no SQL spelling**. SQL has one `ORDER BY ... LIMIT`; Prague has two plans with
  different tie behaviour, and `context/query.md` records that inferring between them was tried and
  **reverted**. A SQL surface forces hint comments plus non-SQL join verbs — neither familiar nor honest.
- **The querystring `QueryParser`** (`src/Prague.Core/QueryStringParser.cs`) is unrelated. Not a
  precedent, not a thing to replace. Ignore it.

## 3. The semantic ceiling is already written down

`src/Prague.Core/QueryBuilders/Prepared/NarrowerDescriptor.cs`:

```csharp
public enum NarrowerKind {
  UniqueEq, UniqueIn, ListEq, ListIn, ListInProjected, Range, KeySet,
  LastUpdatedAfter, LastUpdatedBetween, Filter, FilterArg, Or, If, IfElse, Match,
}
```

Fifteen members, plus the resolver verbs (`JoinOne`/`JoinMany` +/- `Inner`, `Sort`/`SortBounded`) and
the five terminals (`Execute`, `ExecuteCloned`, `ExecutePooled`, `ExecutePooledCloned`, `Count`).
**The grammar covers exactly this and nothing else.**

The invariant that defines the language, from `2026-09-09-prepared-query-command-design.md`, which
rejected an `Eval` escape hatch:

> "a callback that narrows the replayed core directly cannot be described, so `BuildFrozen()` would
> lose the plan."

Everything expressible must be describable as a `NarrowerDescriptor`. **No escape hatch** — one would
silently drop queries to `Replay`, which is exactly where the DSL's value disappears. This constraint
is the design's best feature, not its limitation.

Hard limits to respect (`src/Prague.Core/QueryBuilders/Prepared/Pipeline/PipelineStep.cs`):
`MaxSteps = 16` after `Or` flattening; `MaxOrBranches = 8` flattened while authored `Or` is exactly
two branches (nest to widen); no `Where` inside an `Or` branch (`If`/`Match` arms do admit it);
`Range`/`KeySet`/`LastUpdated*` must be first inside an `Or` branch; filter-only plans have no seed
and always replay.

## 4. The load-bearing design choice: lower through `With*`

The generated prepared surface is type-state-checked, so emitting C# through it makes these
**compile errors in the emitted code** rather than rules a DSL binder must re-implement:

- `Where` inside an `Or` branch — branches are `PreparedNarrowOnly<TCache>`
- narrowing after a sort — the result is `SortedQuery<PreparedQueryDiscriminator<TCache>>`
- `Case` after `Default` — `Case` constrains `TArms : IOpenMatchArms<...>`
- an index from a different cache — `TDiscriminator : ICacheCarrier<{cacheClassName}>`

**A live example landed mid-session.** `fa60800` made `Match`'s `Default` arm mandatory via
`IClosedMatchArms<...>`; a `Match` with no `Default` is now CS0315. Before that commit it was a silent
no-op and the design had to carry a rule about it. Now the design carries nothing — the type system
does it. That is the whole argument for this lowering choice, demonstrated.

Known costs, which the design doc must state plainly: the DSL's name resolution has to mirror the
`With*` naming rules forever (`IndexName` overrides the property name, the `...Index` suffix is
stripped, has-not-value indexes emit `Without{Prop}`), and collection-backed `Many` indexes — which
bind against `{index}.Forward` by element type — stay out of reach.

## 5. What the design doc must contain

1. **Grammar** in EBNF.
2. **`NarrowerKind` -> syntax mapping table**, all 15 rows. If a row has no good spelling, say so.
3. **The refusal matrix** — every shape that would fall back to `Replay`, and the diagnostic text for
   each. Reserve a fresh `CACHE060+` block; put the descriptors in one central table, unlike the ~45
   existing inline ones (`CACHE022` already collides across two different messages).
4. **Arg binding model** — `@param` names into the single `TArgs` tuple, in declaration order.
5. **Hosting.** Recommended: attribute + partial method, the `[GeneratedRegex]`/`[LibraryImport]`
   shape — the method name supplies what sqlc's `-- name:` supplies, the return type supplies
   `:one`/`:many`. Chosen over sqlc-style `.pql` files because diagnostics then land via an ordinary
   Roslyn `Location`; a separate file needs `Location.Create(path, TextSpan, LinePositionSpan)`, a
   pattern that appears nowhere in this repo. `.pql` stays open later as a second input provider.
6. **Worked lowering for both production shapes**, DSL text -> emitted C#, side by side. Shape A:
   three list lanes -> `SortBounded` -> `JoinOne`. Shape B: last-updated window -> two list lanes ->
   `SortBounded` -> two chained `JoinOne`s. Both bind `Pipeline`.
7. **Points that must stay explicit in the syntax**, with the reason attached:
   - `sort by` vs `sort bounded by` — never inferred (`context/query.md`: tried, reverted, changes
     which rows come back on comparer ties).
   - optional predicates lower to `If` (bind-time arm selection, free at execution) rather than the
     implicit null/empty no-op the `With*` overloads carry — implicit "null means skip" can silently
     return the whole cache.
   - emitted lambdas are always `static` (`context/joins.md`: this is what makes capture zero-alloc).
   - pooled terminals must be `Dispose()`d, so the DSL has to make the pooled/allocating choice visible.

## 6. The question this answers

Whether a language whose ceiling is exactly `NarrowerKind` is expressive enough to be worth learning.
**If the refusal matrix ends up longer than the grammar, the answer is no — and writing that down is
a successful outcome, not a failed one.**

## 7. Operational notes

- `research/dsl` had zero unique commits and was fast-forwarded, not rebased. To undo, point the
  branch back at `d781863` with a hard reset.
- `poc/prepared-query` is checked out in the main worktree and **is actively moving** — a peer session
  is committing to it. It cannot be checked out here; fast-forward `research/dsl` again to pick up new
  work.
- Never bare `git stash` — the stash stack is shared across worktrees and concurrent sessions.

## 8. Key documents

| Read | For |
|---|---|
| `docs/superpowers/plans/2026-09-11-frozen-queries-handoff.md` | Where the frozen work stands |
| `docs/superpowers/specs/2026-09-09-prepared-query-command-design.md` | The command alphabet; the `Eval` rejection in §7 |
| `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` | How `Pipeline` binds and fuses |
| `context/query.md` | The `Sort`/`SortBounded` reversion; the ordering contract |
| `context/joins.md` | Join families, `static` lambdas, leak-safety |
| `src/Prague.Core/QueryBuilders/Prepared/NarrowerDescriptor.cs` | The compile target |

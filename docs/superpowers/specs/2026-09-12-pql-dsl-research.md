# PQL — a textual query DSL compiled into frozen queries: design research

> **Status:** Research. Design only — no lexer, no parser, no generator code was written, and no file
> under `src/` was touched. Every code claim below carries a `file:line` checked on `fa60800`.
> **Question:** Prague's prepared/frozen API is fast and type-safe but verbose. Is a textual query
> language whose semantic ceiling is exactly `NarrowerKind` expressive enough to be worth learning?
> **Method:** write the grammar, write the refusal matrix, replay the existing prepared-query corpus
> through both, and compare the two artefacts. Per the handoff's exit condition, a refusal matrix
> longer than the grammar is a "no", and recording that would have been a successful outcome.
> **Verification:** static only. `dotnet build` and `dotnet test` were deliberately not run.

---

## 0. Corrections to the handoff

The governing brief is `docs/superpowers/plans/2026-09-12-pql-dsl-handoff.md`. Its locked decisions
(§2) stand unchanged. Six of its factual claims do not, and the design changes as a result. They are
listed first because several of them move load-bearing parts of the document.

| # | Handoff said | Actually | Consequence for the design |
|---|---|---|---|
| 1 | §4: one `With*` naming rule (`IndexName` overrides, `...Index` stripped, has-not-value emits `Without{Prop}`) | **Seven families, three conventions, one dead branch.** Property-attached `[DataCacheIndex]` emits `With{Property.Name}` and *never* honours a custom name; only the class-level `DataCache<T>` path honours `IndexName` (`CacheGenerator.cs:2442`); only the value / no-value index families strip the `...Index` suffix (`:7250-7252`, `:7256-7258`); `Without{Prop}` ignores a custom name outright | §2's name-resolution rules and §9's cost statement are written against seven families, not one |
| 2 | §4: collection-backed `Many` indexes "stay out of reach" | **They are in reach.** The generator emits `With{Prop}` bound to `{IndexName}.Forward` over the *element* type — eager `CacheGenerator.cs:6496-6499`, prepared `:7206-7209` | They need a type rule (parameter type ≠ property type), not an exclusion. Not a refusal row |
| 3 | §7: optional predicates must lower to `If` because the `With*` overloads carry an implicit null/empty no-op | **The reason is backwards.** The no-op exists only on the *eager* emitter (`:6459`, `:6461`, `:6463`, `:6472`, `:6482`). The prepared emitter's body is an unconditional `=> builder.UseIndex(...)` (`EmitForward`, `:7166`) and the target parameter is `where TIndexKey : notnull` (`PreparedQueryBuilderExtensions.cs:97`) | The rule survives; the reason inverts. `If` is not a guard against a dangerous implicit skip — it is the *only* way to express optionality, because there is no null to pass. Range is the documented exception (`Narrowers.Range.cs:113`, `:127-130`) |
| 4 | §7: `sort by` vs `sort bounded by` stays explicit because the two plans order comparer-equal rows differently | **Stale.** `context/query.md:25`: "Ties no longer decide it: both plans break ties by encounter order, so the choice is purely about cost." `context/query.md:18` still carries the old reason — the repo's own doc is internally inconsistent. The tried-and-reverted thing (`:26`) was the *large-prefix fallback*, not inference between the two | The rule survives; the reason is restated as cost plus honouring the caller's declared intent |
| 5 | §3: the alphabet is 15 kinds + 6 resolver verbs + 5 terminals | **The resolver alphabet is 10, not 6.** The corpus exercises `JoinOne`, `InnerJoinOne`, `JoinMany`, `InnerJoinMany`, `JoinManyCollection`, `InnerJoinManyCollection`, `JoinManyCollectionForward`, `InnerJoinManyCollectionForward`, `Sort`, `SortBounded` — the four collection verbs at `FrozenPipelineJoinManyTests.cs:575`, `:576`, `:306`, `:308` | A grammar scoped to six verbs would under-serve the corpus by four. §1's `join-verb` production covers eight join verbs |
| 6 | §6: the production-shape fixture can be lowered as written | **It cannot.** `tests/Prague.Core.Tests/Prepared/PreparedQueryProductionShapeDifferentialTests.cs` contains **zero** `[DataCacheIndex]` attributes; `PqItem` is an unattributed POCO on a raw `InMemoryDataCache<int, PqItem>` with indexes built imperatively in `[OneTimeSetUp]`. A raw cache has no generated `With*` surface at all | §6 states the scope rule and restates shapes A and B as attributed POCOs, quoting the real chains alongside. Presenting a lowering of the literal test chains would be a fiction |

Two further facts the handoff did not have, both discovered while checking that the worked lowerings
would compile, both load-bearing:

- **A join-side filter cannot read execution arguments.** The FK join filter callback receives an
  *eager* paired builder (`CacheGenerator.cs:7333-7339`), and the filter+arg flavour's `TArg` is a
  build-time value captured into the resolver (`:7345-7351`), not the query's `TArgs`. `@param` inside
  a join filter must be a diagnostic (§3, `CACHE076`).
- **`sort` before a join is only available for some join directions.** The `Sort → JoinWith` parallel
  overloads are emitted outer-only and no-filter-only (`CacheGenerator.cs:6641-6643`), and only for the
  reverse FK families — reverse collection `:6713-6724`, reverse one-to-many `:6761-6772`, reverse
  one-to-one `:6836-6849`, forward collection `:6911`. The **forward many-to-one** family (`:6965-6978`)
  emits the plain and `Inner` flavours and **no sorted parallel**. This is exactly the restated shape B
  (§6.3), and it is why that restatement does not lower as written.

---

## 1. Grammar

Pipeline stages with SQL vocabulary, as locked in handoff §2 — KQL shape, SQL keywords. The `|`
separator is the stage delimiter; `and` joins predicates inside one `where`.

```ebnf
(* ── shell ─────────────────────────────────────────────────────────────── *)
 1  query         = source , { "|" , stage } , { "|" , resolver } , "|" , terminal ;
 2  source        = "from" , cache-name ;

(* ── stages: everything that records a NarrowerDescriptor ──────────────── *)
 3  stage         = narrow | filter | or-stage | if-stage | match-stage ;
 4  narrow        = "where" , predicate , { "and" , predicate } ;
 5  predicate     = eq-pred | in-pred | projected-in | range-pred | updated-pred | flag-pred ;
 6  eq-pred       = index-name , "==" , scalar ;
 7  in-pred       = index-name , "in" , set ;
 8  projected-in  = index-name , "in" , set , "by" , member-path ;   (* reserved; refused in v1 *)
 9  range-pred    = index-name , rel-op , scalar
                  | index-name , "between" , bound , "and" , bound ;
10  rel-op        = "<" | "<=" | ">" | ">=" ;
11  bound         = "*" | scalar | "[" , scalar , "]" ;   (* "*" = open side, [x] = inclusive *)
12  updated-pred  = "updated" , "after" , scalar
                  | "updated" , "between" , scalar , "and" , scalar ;
13  flag-pred     = [ "not" ] , "has" , index-name ;
14  filter        = "filter" , row-lambda ;
15  or-stage      = "or" , "{" , or-branch , "," , or-branch , "}" ;
16  or-branch     = ( narrow | or-stage ) , { "|" , ( narrow | or-stage ) } ;
17  if-stage      = "if" , arg-expr , "{" , arm , "}" , [ "else" , "{" , arm , "}" ] ;
18  match-stage   = "match" , arg-expr , "{" , { case-arm } , default-arm , "}" ;
19  case-arm      = "case" , literal , "->" , "{" , arm , "}" ;
20  default-arm   = "default" , [ "->" , "{" , arm , "}" ] ;
21  arm           = stage , { "|" , stage } ;

(* ── resolvers ─────────────────────────────────────────────────────────── *)
22  resolver      = sort-clause | join-clause ;
23  sort-clause   = "sort" , [ "bounded" ] , "by" , sort-name ;
24  join-clause   = [ "inner" ] , join-verb , join-target , [ "{" , join-filter , "}" ] ;
25  join-verb     = "join" | "join many" | "join many collection"
                  | "join many collection forward" ;
26  join-target   = entity-name | index-name , "=>" , cache-name ;
27  join-filter   = narrow , { "|" , narrow } ;

(* ── terminal ──────────────────────────────────────────────────────────── *)
28  terminal      = "select" , [ "pooled" ] , [ "cloned" ] | "count" ;

(* ── leaves ────────────────────────────────────────────────────────────── *)
29  scalar        = literal | param ;
30  set           = "[" , literal , { "," , literal } , "]" | param ;
31  param         = "@" , identifier ;
32  arg-expr      = (* C# boolean expression over the host method's parameters *) ;
33  row-lambda    = identifier , "=>" ,
                    (* C# boolean expression over the row and the parameters *) ;
34  literal       = number | string-literal | "true" | "false" | enum-member ;
35  member-path   = identifier , { "." , identifier } ;

    index-name = cache-name = entity-name = sort-name = identifier ;
```

**35 productions, 45 non-blank lines.** Production 8 is reserved and refused in v1 (§2, row
`ListInProjected`); the effective grammar is 34.

Notes on the shape, each with the constraint that forced it:

- **`and` is intra-`where` only.** Stage separation is `|`. That keeps the reading order identical to
  the emitted chain order, which matters: `PipelinePlanner` cares about position, not just content
  (`PipelinePlanner.cs:110-111` — a marking narrower must be leftmost inside an `Or` branch).
- **`or` takes exactly two branches** (production 15) because the authored form is exactly two
  (`PreparedQueryBuilderOrExtensions.cs:25`); nesting widens it, and flattening is the planner's job,
  capped at `MaxOrBranches = 8` (`Pipeline/PipelineStep.cs:19`).
- **`default` is mandatory in `match`** (production 18 requires `default-arm`), mirroring `fa60800`:
  `Match` constrains `TArms : struct, IClosedMatchArms<...>` (`PreparedQueryBuilderMatchExtensions.cs:139`,
  `:160`, `:181`) and `DefaultArm` is the sole implementer, so an open chain is CS0315. The parameterless
  `default` (production 20's optional body) is the `Default()` overload at `:108`.
- **No escape hatch.** There is no production that admits an opaque narrowing callback. That is the
  invariant from `2026-09-09-prepared-query-command-design.md` — anything not describable as a
  `NarrowerDescriptor` would silently drop the query to `Replay`, which is exactly where the value
  disappears. `row-lambda` (production 33) is not an exception: it lowers to `Where`, which *is* a
  describable narrower (`NarrowerKind.Filter` / `FilterArg`).

---

## 2. `NarrowerKind` → syntax, all fifteen rows

`src/Prague.Core/QueryBuilders/Prepared/NarrowerDescriptor.cs:6-22` is the ceiling:

```csharp
public enum NarrowerKind {
  UniqueEq, UniqueIn, ListEq, ListIn, ListInProjected, Range, KeySet,
  LastUpdatedAfter, LastUpdatedBetween, Filter, FilterArg, Or, If, IfElse, Match,
}
```

| # | Kind | PQL spelling | Lowers to | Notes |
|---|---|---|---|---|
| 1 | `UniqueEq` | `where Id == @id` | `WithId(static a => a.id)` → `UniqueIndexEq(Arg)` (`PreparedQueryBuilderExtensions.cs:83-115`) | The only kind `PointLookup` will seed on (`FrozenQuery.cs:388-395`) |
| 2 | `UniqueIn` | `where Id in @ids` / `where Id in [1,2,3]` | `WithId(...)` → `UniqueIndexIn(Arg)`; prepared overloads take `ReadOnlyMemory<T>`, `T[]`, `Func<TArgs,ReadOnlyMemory<T>>` (`CacheGenerator.cs:7182-7189`) | Rare — 2 corpus sites |
| 3 | `ListEq` | `where Group == @g` | `WithGroup(static a => a.g)` → `ListIndexEq(Arg)` (`:117-149`) | **Same surface spelling as `UniqueEq`.** The index declaration decides which kind is recorded; the author never chooses |
| 4 | `ListIn` | `where Group in @gs` | `WithGroup(...)` → `ListIndexIn(Arg)` | As above |
| 5 | `ListInProjected` | *reserved: `where Tags in [...] by Id`* — **refused in v1** | raw `UseIndex(listIndex, ReadOnlyMemory<TOther>, Func<TOther,TIndexKey>)` (`PreparedQueryBuilderExtensions.cs:298-314`) | **No good spelling.** Two independent reasons: (a) no `With*` twin exists — list indexes get only eq/eq-arg/in/in-arg (`CacheGenerator.cs:7175-7192`), no key-selector overload; (b) **no arg-selector twin at all** — `_values` is bound at build time (`Narrowers.cs:214-216`), only the per-element projection is a delegate, so `in @param` is *not expressible* (`CACHE075`). What remains spellable is a compile-time-literal foreign set, which no corpus site uses: all 12 sites bind runtime-held collections |
| 6 | `Range` | `where Ts > @t`, `where Ts between @a and [@b]`, `where Ts between * and @b` | `WithTs(static (rb,a) => rb.Gt(a.t))`; optional-bounds forms → `RangeOptional(Ref)ArgNarrower`, picked by `prop.Type.IsValueType` (`CacheGenerator.cs:7224`) | `*` is the open side; `[x]` is inclusive. The only kind whose no-op is real: an unbounded range records a step and skips at execution (`Narrowers.Range.cs:113`, `:127-130`) |
| 7 | `KeySet` | `where has Featured` / `where not has CompletedAt` | `WithFeatured()` / `WithoutCompletedAt()` (`CacheGenerator.cs:7245-7260`, via `EmitKeySetMethod` `:7194-7195`) | Attribute-declared key sets get a parameterless `With*`/`Without*`. A hand-held `CacheKeySetIndex` has no declared name to reference and is therefore outside the grammar, not refused by it |
| 8 | `LastUpdatedAfter` | `where updated after @since` | `UpdatedAfter(static a => a.since)` (`CacheGenerator.cs:7269-7285`) | Only the *key* global index gets this sugar — the generator emits `_keyGlobalIndex` only when a `[DataCacheGlobalLastUpdateIndex<T>]` property's type matches the key type (`CacheGenerator.cs:925-936`) |
| 9 | `LastUpdatedBetween` | `where updated between @from and @to` | `UpdatedAfter(static a => a.from, static a => a.to)` (`:7282-7284`) | Prepared-only: the eager emitter has no window form (`:6601-6629`) |
| 10 | `Filter` | `filter r => r.Flag` | `Where(static v => v.Flag)` (`PreparedQueryBuilderExtensions.cs:151-165`) | See the resolution rule below |
| 11 | `FilterArg` | `filter r => r.Score > @min` | `Where(static (v, in a) => v.Score > a.min)` (`:167-193`) | See the resolution rule below |
| 12 | `Or` | `or { where A == @a, where B == @b }` | `Or(b => ..., b => ...)` (`PreparedQueryBuilderOrExtensions.cs:25` / `:50` / `:75`) | Branches are `PreparedNarrowOnly<TCache>`: no `filter` inside (`CACHE066`) |
| 13 | `If` | `if @min > 0 { where Score > @min }` | `If(static a => a.min > 0, b => ...)` (`PreparedQueryBuilderIfExtensions.cs:32`) | Arms are `PreparedConditionalBranch<TCache>`, which *does* admit `filter` |
| 14 | `IfElse` | `if ... { ... } else { ... }` | `IfElse(cond, then, otherwise)` (`:52`) | Rarest kind in the corpus: 3 sites |
| 15 | `Match` | `match @pick { case Department -> {...} default }` | `Match(static a => a.pick, m => m.Case(...).Default())` (`PreparedQueryBuilderMatchExtensions.cs:124`) | Tag selector is `Func<TArgs,TTag> where TTag : notnull` (`:130`), run once per execution |

### The `Filter` / `FilterArg` split is invisible in the surface syntax — how it resolves

There is one keyword (`filter`) and two kinds. The split is not about what the predicate *tests*, it
is about *when it reads arguments*: `Filter` holds a `Predicate<TValue>` frozen at build time,
`FilterArg` holds an `ArgFilter<TValue,TArgs>` — `bool (TValue value, in TArgs args)` — that
reads `TArgs` at execution. C# resolves the two by lambda arity
(`PreparedQueryBuilderExtensions.cs:167-193`: "Overload resolution … by lambda arity").

**Resolution rule for PQL: purely syntactic, decided by scanning the lambda body for `@`.** Zero
`@param` references → emit a one-parameter `static v => …` lambda → `Filter`. One or more → emit
`static (v, in a) => …` with each `@x` rewritten as `a.x` → `FilterArg`. No type information, no
semantic analysis, no ambiguity — and the choice is observable in the source text, which matters
because `FilterArg` costs a pooled predicate box per execution (`ArgPredicate.cs:77-148`) and `Filter`
costs nothing.

### The same spelling for `Unique` and `List`

Rows 1/3 and 2/4 share a spelling. That is deliberate and it is the only place the DSL hides a
semantic distinction. It is defensible because the distinction is a property of the *schema*, not of
the query: an author cannot choose which kind is recorded, the `[DataCacheIndex]` declaration does.
The generated method is the same name in both cases (`CacheGenerator.cs:7204`, `:7210-7211`), so the
emitted C# is identical too; only the narrower struct it binds differs.

---

## 3. The refusal matrix

Handoff §5 asked for one central table with a fresh `CACHE060+` block. Both requirements are
justified by the existing state of the generator's diagnostics.

**The hygiene argument.** All diagnostics live inline in `src/Prague.Codegen/CacheGenerator.cs`: 40
distinct IDs across 44 descriptor sites. The absence of one table has produced four defects:

| Defect | Sites |
|---|---|
| `CACHE022` used for two different messages | `:864` ("Invalid HasValue index type") and `:2147` ("Key property cannot be indexed as Many") |
| `CACHE021` and `CACHE022` carry an **identical title and message** — two IDs for one diagnostic | `:460` vs `:2147` |
| `CACHE046` declared twice | `:599` (TFk mismatch), `:615` (TPk mismatch) |
| `CACHE051` declared twice with identical title and message | `:496`, `:2101` |

Four collisions in 44 sites is a 9% defect rate, and every one of them is a defect that a single
table of descriptors makes structurally impossible. **`CACHE060`–`CACHE099` are confirmed entirely
unused** (highest ID in use: `CACHE051`), so the block is free.

**The matrix.** Twenty-five rows. The "source" column cites the corpus shape (§8.2) or the code fact
that forces the refusal.

| ID | PQL refuses | Source | Diagnostic text |
|---|---|---|---|
| `CACHE060` | a query with no indexed predicate anywhere | S13 `FrozenQueryTests.cs:160`; S60 `FrozenPipelineJoinTests.cs:688`; S75 `FrozenPipelineJoinManyTests.cs:584`; S79 `FrozenPipelineSortBoundedTests.cs:317`; S83 `FrozenQueryTests.cs:180` | "This query has no indexed predicate, so it would scan the whole cache and fall back to the replay executor. Add a `where` over an indexed property." |
| `CACHE061` | a query whose only stages are `filter` | S14 `FrozenQueryTests.cs:161`, `FrozenPipelineTests.cs:725`; S46 `FrozenPipelineCompositeTests.cs:713-714` | "A filter-only query has no seed: the pipeline executor needs at least one index to start from. Add an indexed `where`, or use the eager API." |
| `CACHE062` | a nested `or` sitting beside a leaf inside the same branch | S28 `FrozenPipelineCompositeTests.cs:445`, `:715` | "A nested `or` cannot share a branch with another predicate. Move it into a branch of its own." |
| `CACHE063` | more than 8 `or` branches after flattening | S29 `FrozenPipelineCompositeTests.cs:471`; `PipelineStep.cs:19` | "This `or` flattens to {n} branches; the pipeline supports at most 8 (`MaxOrBranches`)." |
| `CACHE064` | a `between` / `updated` / `has` predicate that is not leftmost in an `or` branch | S31 `FrozenPipelineCompositeTests.cs:323-325`; S41 `:326`; S101 `PreparedGeneratedFrozenBranchTests.cs:43-46`; `PipelinePlanner.cs:110-111` | "Inside an `or` branch, a range, key-set or last-updated predicate must come first. Move `{pred}` to the front of its branch." |
| `CACHE065` | a plan exceeding 16 pipeline steps | `PipelineStep.cs:13` (`MaxSteps = 16`); no corpus witness | "This query flattens to {n} steps; the pipeline supports at most 16 (`MaxSteps`)." |
| `CACHE066` | `filter` inside an `or` branch | `PipelinePlanner.cs:72-76`; `PreparedQueryBuilderOrExtensions.cs:5-22` | "`filter` is not allowed inside an `or` branch. Use `if` or `match`, whose arms do admit it, or lift the filter out of the `or`." |
| `CACHE067` | `inner join` on a forward many-to-one FK when the host asked for eager ordering | S57 `FrozenPipelineJoinTests.cs:684-685`, `FrozenPipelineSortBoundedTests.cs:328`, `FrozenPipelineJoinManyTests.cs:579` | "An inner forward many-to-one join regroups its rows, so under `PreserveEagerOrder` it can only replay. Drop `inner` or drop the ordering guarantee." |
| `CACHE068` | a `{ … }` filter on a one-row join | S58 `FrozenPipelineJoinTests.cs:689-690`; S74 `FrozenPipelineJoinManyTests.cs:583` | "A filter on a one-row join cannot fuse into the pipeline pass. Remove it, or filter the joined rows after the query." |
| `CACHE069` | two joins where the second cannot fuse | S59 `FrozenPipelineJoinTests.cs:691`, `:693` | "The second join in this chain cannot fuse into the pipeline pass, so the whole query would replay." |
| `CACHE070` | any query whose host opted out of the pipeline | S86 `FrozenPipelineSortBoundedTests.cs:316`, `:323`, `FrozenPipelineJoinManyTests.cs:585`, `FrozenPipelineTests.cs:731` | "`Pipeline = false` on this host disables the executor PQL compiles to. PQL queries require the pipeline." |
| `CACHE071` | `between` over an unmanaged key wider than 16 bytes | S87 `FrozenPipelineTests.cs:733` | "Range narrowing over `{T}` ({n} bytes) has no pipeline step; only unmanaged keys up to 16 bytes are supported." |
| `CACHE072` | `between` over a struct key containing references | S88 `FrozenPipelineTests.cs:734` | "Range narrowing over `{T}` has no pipeline step: the key type contains references." |
| `CACHE073` | `between` when the host enabled index-side probes | S89 `FrozenPipelineTests.cs:736`, `:501`, `FrozenPipelineCompositeTests.cs:716` | "A range step and `IndexSideProbes` are mutually exclusive on this plan." |
| `CACHE074` | a key-side range window walk | S90 `FrozenPipelineTests.cs:462` | "The key-side range window walk has no pipeline step." |
| `CACHE075` | `in @param` over a projected list index | Finding K; `Narrowers.cs:214-216`; `PreparedQueryBuilderExtensions.cs:298-314` | "A projected `in` binds its value set at build time; it cannot read a parameter. (Projected `in` is reserved and not available in v1.)" |
| `CACHE076` | `@param` inside a join filter | `CacheGenerator.cs:7333-7351`; `PreparedQueryBuilderExtensions.cs` joined `Build()` remarks | "A join-side filter is bound when the query is built and cannot read execution arguments. Move `{param}` to a `where` on the left side." |
| `CACHE077` | `sort by` naming something that is not a declared sort | S99 (§8.2 bucket (d)-2); `DataCacheSortAttribute.cs`; `CacheGenerator.cs:1676-1679` | "`{name}` is not a declared sort on `{Entity}`. Add `[DataCacheSort(\"{name}\", \"Prop:1\", …)]` to the model." |
| `CACHE078` | a sort key that reads the joined row | S82 `FrozenPipelineSortBoundedTests.cs:330`, `FrozenPipelineJoinManyTests.cs:499`, `:507` | "Sorts are declared on the entity and order the left side only; `{name}` would have to order joined rows. Not supported in v1." |
| `CACHE079` | a join on a derived key expression | S68 `FrozenPipelineJoinManyTests.cs:301`, `:304` | "Joins follow declared foreign keys; an expression key has no generated surface. Declare the relationship with `[DataCacheForeignKey<T>]`." |
| `CACHE080` | any stage after a `sort` | `PreparedQueryBuilderSortExtensions.cs:13-15` | "Nothing may narrow or filter after a `sort`. Move `{stage}` before it." |
| `CACHE081` | `case` after `default`, or `match` with no `default` | `PreparedQueryBuilderMatchExtensions.cs:73`, `:96`, `:116`, `:139`, `:160`, `:181` | "Every `match` ends in exactly one `default`, and no `case` may follow it." |
| `CACHE082` | an index name not declared on this cache | `CacheGenerator.cs:7146-7150` (`ICacheCarrier<{Cache}>`) | "`{name}` is not an indexed property of `{Entity}`. Indexed properties: {list}." |
| `CACHE083` | a host method over a cache that is not `[DataCache]`-generated | Finding C; §6.1 | "PQL compiles against generated caches. `{type}` has no `[DataCache]` model, so it has no generated query surface." |
| `CACHE084` | `sort` before a forward many-to-one, nested, or selector-form `join`, or before any `inner join` or filtered join | `CacheGenerator.cs:6641-6643`; sorted parallels emitted at `:6714`, `:6762`, `:6837`, `:6907` only; absent at `:6789`, `:6965`, `:7037`, `:7087`; `PreparedGeneratedFrozenJoinTests.cs:123-124` | "A `sort` before this join has no generated overload (the sorted join family is outer-only, no-filter-only, and covers only the reverse-FK and collection families). Move the `sort` after the join." |

**Twenty-five rows.** Not all twenty-five are equal, and the comparison in §10 depends on the
distinction:

| Class | Rows | Can a well-formed query written by a person trigger it? |
|---|---|---|
| **Query-shape refusals** — the plan is legal PQL but would replay | `CACHE060`–`CACHE064`, `CACHE066`, `CACHE068`, `CACHE069`, `CACHE075`–`CACHE079`, `CACHE084` | Yes, 14 rows |
| **Host/option refusals** — nothing about the query text is wrong | `CACHE065`, `CACHE067`, `CACHE070`–`CACHE074`, `CACHE083` | Only by changing a `FrozenOptions` flag, a key type, or the host, 8 rows |
| **Type-state backstops** — the emitted C# would also reject them | `CACHE080`, `CACHE081`, `CACHE082` | Yes, but the compiler catches them anyway; PQL diagnoses them first only to keep the error on the query text rather than in generated code, 3 rows |

---

## 4. The argument-binding model

`@param` names bind to the host method's parameters. **If there is one parameter, `TArgs` is its type.
If there are several, `TArgs` is a `ValueTuple` of them in declaration order**, and `@x` lowers to
`a.x` against the tuple's element names.

```
public partial QueryResults<Listing> Page(long dept, int minOrder)
  →  TArgs = (long dept, int minOrder)
     @dept → a.dept        @minOrder → a.minOrder
```

Facts that shape this:

- **`TArgs` is completely unconstrained.** `grep -rn "where TArgs" src/` returns zero hits. The
  `ValueTuple` convention is a convention, not a requirement — `Prepare<int>()` is used directly
  (`PreparedGeneratedFrozenJoinTests.cs:71`). The single-parameter case therefore needs no tuple and
  the selector is `static a => a` (`PreparedGeneratedKeySetLastUpdatedTests.cs:109`).
- **Zero parameters is `NoArgs`.** `src/Prague.Core/QueryBuilders/Prepared/NoArgs.cs:4`
  (`public readonly struct NoArgs;`), reached through the parameterless `Prepare()`
  (`PreparedQueryBuilderExtensions.cs:22`).
- **Threading is by `in` throughout.** `INarrower.Apply<TCore>(ref TCore core, in TArgs args)`
  (`Prepared/INarrower.cs:16`), `INarrowerChain.Replay<TCore>(ref TCore, in TArgs)` (`:34`). No boxing,
  no copy, regardless of how wide the tuple gets.
- **Selectors run once per execution, not once per row.** They are stored as `Func<TArgs,T>` fields in
  closed-generic narrower structs (`Narrowers.cs:38`, `:88`, `:138`, `:184`) and invoked in `Apply`.
  The same is true of the `match` tag selector (`PreparedQueryBuilderMatchExtensions.cs:37-39`).
- **`filter` with a parameter is allocation-free, but not free.** `FilterArg` goes through
  `ArgPredicate` (`Prepared/ArgPredicate.cs:12`, delegates created once in the constructor `:25-30`)
  rented from a thread-static, stack-disciplined `ArgPredicatePool<TValue,TArgs>` (`:77-148`). The pool
  is `Reset` after `Execute`/`Count`, not after replay (`:65-76`). This is why §2's syntactic
  `Filter`/`FilterArg` rule is worth having: the cost is visible in the query text.
- **Last-updated selectors take unix milliseconds and nothing else.**
  `PreparedQueryBuilderLastUpdatedExtensions.cs:9-10`: "Parameterized forms take unix-ms selectors only;
  convert other time types inside the selector." Signatures at `:121`, `:137`, `:253`, `:269` are all
  `Func<TArgs,long>`. **PQL must emit the conversion inside the lambda** — a `DateTimeOffset` parameter
  bound to `where updated after @since` lowers to
  `UpdatedAfter(static a => a.since.ToUnixTimeMilliseconds())`, not to a `DateTimeOffset` overload.
  (The bound overloads do take `DateTime`/`DateTimeOffset` — `CacheGenerator.cs:7271` loops over three
  types — but those are build-time constants, not parameters.)
- **A join-side filter cannot read a parameter.** `@param` inside a `join … { … }` block is
  `CACHE076`. The callback receives an eager paired builder
  (`CacheQueryBuilder.cs:944 PairedCacheQueryBuilderCoreCombined<TLeft,TKey,TValue>`), and the
  filter+arg flavour's `TArg` is captured into the resolver at build time
  (`CacheGenerator.cs:7345-7351`). The joined `Build()` remarks say it outright: "The right-side join
  filters … are bound at build time: a filter cannot read `TArgs`."
- **Every emitted lambda is `static`.** Not an optimization — it is the thing that makes the capture
  zero-allocation (`context/joins.md`). PQL never emits a capturing lambda, which is trivially
  satisfiable because every value a PQL lambda can reference is either a literal or a member of `a`.

---

## 5. Hosting: attribute plus partial method

**Recommendation: the `[GeneratedRegex]` / `[LibraryImport]` shape.** The query text is a string
argument to an attribute on a `partial` method; the generator writes the body.

```csharp
public sealed partial class ListingQueries(ProductListingCache listings) {
    [Pql("""
         from listings
         | where IsPublished == true and DepartmentId == @dept
         | sort bounded by ByDateAsc
         | select pooled
         """)]
    public partial QueryResults<ProductListing> PublishedInDepartment(long dept);
}
```

emits

```csharp
partial class ListingQueries {
    private FrozenQuery<long, ProductListing>? _pql_PublishedInDepartment;

    public partial QueryResults<ProductListing> PublishedInDepartment(long dept)
        => (_pql_PublishedInDepartment ??= listings.Prepare<long>()
                .WithIsPublished(true)
                .WithDepartmentId(static a => a)
                .SortBounded(ProductListingCache.ByDateAscComparer)
                .BuildFrozen())
           .ExecutePooled(dept);
}
```

What each piece of the C# declaration supplies:

| sqlc's | PQL's | Why |
|---|---|---|
| `-- name: GetAuthor` | the method name | It is already there and already unique in its scope |
| `:one` / `:many` | the return type | `QueryResults<T>` is `:many`; `int` is `count`; `T?` is `:one`, emitted as `Execute(args, 0, 1)` plus an index — sugar, not a sixth terminal (all five terminals return `QueryResults<TResult>`, `FrozenQuery.cs:63-74`) |
| the parameter list, inferred from `$1` | the method parameters | Declared, typed, named, and ordered by the author — §4 |

**Why not `.pql` files.** Diagnostics. An attribute argument is an ordinary C# syntax node, so every
row of §3's matrix lands on a plain Roslyn `Location` with no extra machinery, including offsets into
the string literal. A separate file needs `Location.Create(path, TextSpan, LinePositionSpan)` and an
`AdditionalFiles` pipeline — a pattern that appears **nowhere** in this repository today. For a
research-stage feature whose entire value proposition rests on the quality of 25 refusal messages,
paying for a second diagnostic mechanism before the first query compiles is the wrong order. `.pql`
files stay open as a *second input provider* later; nothing in the design forecloses them.

Three consequences worth stating:

- **The cache is an instance, so the frozen query cannot be `static`.** `BuildFrozen()` binds to a
  cache instance. The generated field is therefore an instance field with `??=` initialization. The
  race is benign (two threads may each build a plan; one wins, the other is garbage), and a frozen
  query is immutable and thread-safe once built (`FrozenQuery.cs:9-16`).
- **Pooled terminals must be disposed, and the syntax says so.** `select pooled` returns a pooled
  `QueryResults<T>` the caller must `Dispose()`. Because that obligation is visible in the query text
  and in the method's return type, it can be enforced the usual way. `select` (allocating) carries no
  obligation. This is §7's fourth point.
- **`FrozenOptions` are a host concern, not query text.** They belong on the attribute
  (`[Pql(..., PreserveEagerOrder = true)]`), which is also what makes `CACHE067` and `CACHE070`
  diagnosable at the right place.

---

## 6. Worked lowerings: production shapes A and B

### 6.1 The scope rule, and why the shapes had to be restated

**PQL applies only to `[DataCache]`-generated caches.** A raw `InMemoryDataCache<TKey,TValue>` has no
generated `With*` surface, and handoff §2 locked the lowering onto that surface.

The two canonical production shapes live in
`tests/Prague.Core.Tests/Prepared/PreparedQueryProductionShapeDifferentialTests.cs`, and that file
contains **zero `[DataCacheIndex]` attributes**. `PqItem` and `PqRecord` are unattributed POCOs on raw
caches; every index is built imperatively in `[OneTimeSetUp]`, e.g.
`_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group)`.

**The restatement below is therefore necessary, and it is a restatement, not a lowering.** The real
chains are quoted verbatim beside the PQL so a reader can see exactly what was changed and judge
whether the change is cosmetic. Presenting a "lowering" of the literal test chains would be a fiction:
the methods it emits do not exist for those caches.

### 6.2 Shape A — three list lanes → bounded sort → outer one-to-one join

**The real chain** (`PreparedQueryProductionShapeDifferentialTests.cs:130-138`, asserted
`Executor == "Pipeline"` at `:138` with the message "SortBounded before an outer JoinOne: the joined
pipeline (step 6)"):

```csharp
var frozen = _items.Prepare<int, PqItem, (int group, int band, int lane)>()
    .UseIndex(_byGroup, static a => a.group)
    .UseIndex(_byBand,  static a => a.band)
    .UseIndex(_byLane,  static a => a.lane)
    .SortBounded(new ByIdMod5())
    .JoinOne(_details)
    .BuildFrozen(EagerOrder);
```

**The restated model.** Two changes beyond adding attributes, both forced:

1. `ByIdMod5` compares `((x?.Id ?? 0) % 5)` (`:38-40`) — a *computed* key. `[DataCacheSort]` accepts
   only `"Prop:±1"` specs (`src/Prague.Attributes/DataCacheSortAttribute.cs`), so the computed key must
   be materialized as a stored property.
2. `JoinOne(_details)` is PK-to-PK identity ("Outer join PK-to-PK with no filter",
   `JoinQueryBuilders.generated.cs:30-35`). The attribute vocabulary has no PK-to-PK identity form, so
   the relationship is declared as an ordinary reverse one-to-one FK — the `Author`/`AuthorProfile`
   pattern (`tests/Prague.Generated.Tests/Join/ForeignKeyJoinModels.cs:33-44`).

```csharp
[DataCache]
[DataCacheSort("ByIdMod5", "IdMod5:1")]
public partial class Item {
    [DataCacheKey] public int Id { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)] public int Group { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)] public int Band { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)] public int Lane { get; set; }
    public int IdMod5 { get; set; }   // materialised: [DataCacheSort] cannot compute Id % 5
}

[DataCache]
public partial class Detail {
    [DataCacheKey] public int Id { get; set; }
    [DataCacheForeignKey<Item>(DataCacheJoinType.OneToOne)] public int ItemId { get; set; }
    public string Region { get; set; } = "";
}
```

**PQL**

```
from items
| where Group == @group and Band == @band and Lane == @lane
| sort bounded by ByIdMod5
| join Detail
| select
```

**Emitted C#**

```csharp
items.Prepare<(int group, int band, int lane)>()
    .WithGroup(static a => a.group)
    .WithBand(static a => a.band)
    .WithLane(static a => a.lane)
    .SortBounded(ItemCache.ByIdMod5Comparer)
    .JoinWithDetail()
    .BuildFrozen()
```

Each line checked against the emitter it comes from: `WithGroup`/`WithBand`/`WithLane` from
`CacheGenerator.cs:7204` + `:7210-7211` (the `Func<TArgs,T>` overload at `:7178`);
`ItemCache.ByIdMod5Comparer` from `:1676-1679`
(`public static readonly IComparer<{documentType}> {Name}Comparer`); `SortBounded` from
`PreparedQueryBuilderSortExtensions.cs:67`; `JoinWithDetail()` after a sort from the reverse
one-to-one sorted parallel at `CacheGenerator.cs:6836-6849`. The identical shape on real attributed
models is asserted `Pipeline` at `PreparedGeneratedFrozenJoinTests.cs:128-129`
(`WithId(...).SortBounded(cmp).JoinWithAuthorProfile()` → "executor: Pipeline", "sort: bounded",
"fused: 1").

**Verdict on shape A: expressible, 5 lines of PQL for 7 lines of chain**, and the three type
parameters, the tuple threading and the comparer instance all disappear.

### 6.3 Shape B — last-updated window → two list lanes → bounded sort → two chained joins

**The real chain** (`:180-189`, asserted `Executor == "Pipeline"` at `:188` and
`Narrowers[0].Kind == NarrowerKind.LastUpdatedAfter` at `:189`):

```csharp
var frozen = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>()
    .UseIndex(_recordsUpdated, static a => a.t)
    .UseIndex(_byKeyA, static a => a.keyA)
    .UseIndex(_byKeyB, static a => a.keyB)
    .SortBounded(new ByScoreTies())
    .JoinOne(_recByCustomer, _customers)
    .JoinOne(_recByProduct,  _products)
    .BuildFrozen(EagerOrder);
```

**The restated model.** Three forced changes:

1. `ByScoreTies` compares `(Score & 7)` (`:82-84`) — computed again; materialized as `ScoreBucket`.
2. `_recordsUpdated` is a raw `LastUpdatedIndex<int>` registered with
   `_records.CacheLastUpdatedIndex(_recordsUpdated, static (id, _) => id)`. The generated `UpdatedAfter`
   sugar exists only for the *key* global index — the generator emits `_keyGlobalIndex` only when a
   `[DataCacheGlobalLastUpdateIndex<T>]` property's type equals the key type
   (`CacheGenerator.cs:925-936`), and the raw `LastUpdatedIndex<TKey>` forms have no `With*` twin at
   all (`PreparedQueryBuilderLastUpdatedExtensions.cs:147-276`). Declaring the attribute on `Id` is the
   exact equivalent: group key = the cache key = `static (id, _) => id`.
3. `_recByCustomer` / `_recByProduct` are `CacheSymmetricKeyValueListIndex<int, PqRecord, int>`. That is
   precisely what a `ManyToOne` FK auto-emits — "Auto-emits `CacheSymmetricKeyValueListIndex<TKey,
   TValue, TOtherKey>` on the FK property" (`src/Prague.Attributes/DataCacheJoinType.cs`, `ManyToOne`
   doc) — so the two joins are forward many-to-one FKs.

```csharp
public partial class RecordLastUpdatedIndex : IDataCacheGlobalLastUpdateIndex<int> { }

[DataCache]
[DataCacheSort("ByScore", "ScoreBucket:1")]
public partial class Record {
    [DataCacheKey]
    [DataCacheGlobalLastUpdateIndex<RecordLastUpdatedIndex>]
    public int Id { get; set; }

    [DataCacheIndex(DataCacheIndexType.Many)] public int KeyA { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)] public int KeyB { get; set; }

    [DataCacheForeignKey<Customer>(DataCacheJoinType.ManyToOne)] public int CustomerId { get; set; }
    [DataCacheForeignKey<Product>(DataCacheJoinType.ManyToOne)]  public int ProductId  { get; set; }

    public int ScoreBucket { get; set; }   // materialised: [DataCacheSort] cannot compute Score & 7
}
```

**PQL as the shape is written — and it does not compile.**

```
from records
| where updated after @t and KeyA == @keyA and KeyB == @keyB
| sort bounded by ByScore
| join Customer
| join Product
| select
```

This is `CACHE084`. The `Sort → JoinWith` parallel overloads are emitted **outer-only, no-filter-only**
(`CacheGenerator.cs:6641-6643`: *"only the outer JoinWith family gets a Sorted variant — InnerJoinWith
is intentionally skipped"*), and only for **four of the eight** FK families. Exactly four sites build a
`SortedQuery<TInner>`-wrapped return type:

| FK family | Verb | Sorted parallel |
|---|---|---|
| reverse collection (M:N) | `JoinManyCollection` | **yes** — `:6714` |
| reverse one-to-many | `JoinMany` | **yes** — `:6762` |
| reverse one-to-one | `JoinOne` | **yes** — `:6837` |
| forward collection | `JoinManyCollectionForward` | **yes** — `:6907` |
| nested | `JoinMany` (continuation) | **no** — `:6789`, `:6794` |
| **forward many-to-one** | `JoinOne` | **no** — `:6965`, `:6972` |
| selector-form one-to-one on PK | `JoinOne` | **no** — `:7037`, `:7044` |
| selector-form general | `JoinOne` | **no** — `:7087`, `:7094` |

So the split is **not** forward-vs-reverse — forward collection has a sorted parallel. It is that the
four families which build an explicit `joinReturnType*Sorted` get one, and the four routed only through
`EmitFkJoinWithOverloads` do not. The **forward many-to-one** family — the one shape B needs — is in the
second group, which `PreparedGeneratedFrozenJoinTests.cs:123-124` states in so many words: "The forward
and the inner `JoinWith` bind on `ICacheCarrier`, which a `SortedQuery` discriminator does not carry
(eager too)."

The raw chain gets away with it because `JoinOne(index, cache)` is hand-written and binds on the
prepared discriminator directly; the generated forward FK sugar has no such overload. **This is a
codegen asymmetry that PQL inherits and cannot paper over**, and it is a candidate fix in the
generator (filed, not fixed here — see §9).

**PQL that does compile**, with the sort moved after the joins:

```
from records
| where updated after @t and KeyA == @keyA and KeyB == @keyB
| join Customer
| join Product
| sort bounded by ByScore
| select
```

**Emitted C#**

```csharp
records.Prepare<(long t, int keyA, int keyB)>()
    .UpdatedAfter(static a => a.t)
    .WithKeyA(static a => a.keyA)
    .WithKeyB(static a => a.keyB)
    .JoinWithCustomer()
    .JoinWithProduct()
    .SortBounded(RecordCache.ByScoreComparer)
    .BuildFrozen()
```

`UpdatedAfter(Func<TArgs,long>)` from `CacheGenerator.cs:7279-7281`; `WithKeyA`/`WithKeyB` from
`:7204`+`:7210-7211`; `JoinWithCustomer`/`JoinWithProduct` from the forward many-to-one family at
`:6965-6972`; `SortBounded` after a join chain from `PreparedQueryBuilderSortExtensions.cs:86-104`
(`where TResult : struct, IJoinResult<TValue>` — satisfied by the join chain, not by an unjoined query).

**Verdict on shape B: expressible, but only in a reordered form, and the reorder is not free** — a
bounded sort placed after a fan-out is a different plan from one placed before it. Shape B is the
single most valuable case in the corpus (it is one of the two shapes `CLAUDE.md` says to re-measure
after any executor change) and PQL cannot express it in its production ordering on the attributed
surface. That is the sharpest cost in this document, and §10 weighs it.

---

## 7. What stays explicit in the syntax, with corrected reasons

**1. `sort by` vs `sort bounded by` — never inferred.** *Corrected reason.* The handoff justified this
by tie behaviour; that justification is stale. `context/query.md:25`: "**Ties no longer decide it: both
plans break ties by encounter order, so the choice is purely about cost.**" The tried-and-reverted
experiment (`:26`) was a *large-prefix fallback*, not inference between the two plans, and the
conclusion recorded there is the real reason: "**The caller chose this plan; honouring the choice beats
second-guessing it.**" So the rule survives on two grounds — the two plans have genuinely different
cost profiles that only the author knows the workload for, and the API's stated position is that a
declared intent is honoured rather than re-derived. (`context/query.md:18` still carries the old
tie-based wording; that line is stale and should be corrected independently of this work.)

**2. Optional predicates lower to `if`, never to an implicit skip.** *Corrected reason.* The handoff
said `If` guards against the `With*` overloads' implicit null/empty no-op silently returning the whole
cache. On the **prepared** path that no-op does not exist. The eager emitter skips on null/empty
(`CacheGenerator.cs:6459`, `:6461`, `:6463`, `:6472`, `:6482`); the prepared emitter's body is an
unconditional `=> builder.UseIndex(...)` (`EmitForward`, `:7166`), and the hand-written target is

```csharp
// PreparedQueryBuilderExtensions.cs:90-98
    CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
    TIndexKey value)
    …
    where TIndexKey : notnull
    => Link(in builder, new UniqueIndexEq<TKey, TValue, TIndexKey, TArgs>(index, value));
```

The parameter is non-nullable and constrained `notnull`; `Link` is called unconditionally; at replay it
is unconditional too (`Narrowers.cs:23-25`). **The rule survives and the reason inverts: `if` is not a
guard, it is the only way to express optionality, because there is no null to pass.**

The one exception is the range-optional family, and only it: `Narrowers.Range.cs:113` documents "an
unbounded range is a recorded no-op", `:127-130` is `if (!_range.IsUnbounded) core.UseIndexInternal(...)`,
and `RangeOptionalArgNarrower.Apply` (`:167-174`) evaluates both selectors then returns early when both
are null. Even there the step is still recorded and still described as `NarrowerKind.Range` — the skip
is a per-execution no-op, not an absent link. That is why production 11's `*` bound exists: optional
range bounds are the one optionality the API models directly, so PQL spells them directly rather than
routing them through `if`.

**3. Emitted lambdas are always `static`.** `context/joins.md` records that this is what makes the
capture zero-allocation. PQL satisfies it structurally: every value a PQL lambda can name is a literal
or a member of `a`, so a capturing lambda is unreachable from the grammar.

**4. The pooled/allocating choice is visible in the syntax.** `select pooled` vs `select`. Pooled
results must be `Dispose()`d — join resolvers enforce exactly-one-`Dispose` via the `handedOff` guard
(`context/joins.md`) — and a syntax that hid the choice would hide the obligation. Four of the five
terminals are reachable as `select` × `pooled?` × `cloned?` (production 28); `count` is the fifth.

---

## 8. Coverage evidence

The user's bar was that the language must express the real use cases. Three independent
demonstrations follow: closure over the alphabet, replay of the whole existing corpus, and the two
production shapes in both languages (§6).

### 8.1 Alphabet closure — 15 kinds × 10 verbs × 5 terminals

Every cell is either mapped to a production or explicitly refused. A blank cell would be a finding;
there are none.

**Kinds (15):** §2's table, all fifteen rows, one spelling each except `ListInProjected` (reserved,
refused, `CACHE075`, with the reason stated twice over). Rarity worth recording, because it bears on
how much grammar each kind earns: `IfElse` has **3** corpus sites (`FrozenQueryTests.cs:228`,
`FrozenPipelineCompositeTests.cs:510`, `PreparedQueryIfDifferentialTests.cs:262`); `ListInProjected`
has 12, **all on raw caches**, with no generated twin in existence; `LastUpdatedBetween` and `UniqueIn`
have 2 each.

**Resolver verbs (10, not the handoff's 6):**

| Verb | Corpus sites | PQL |
|---|---|---|
| `JoinOne` | 352 | `join {Entity}` |
| `SortBounded` | 329 | `sort bounded by {Name}` |
| `InnerJoinOne` | 164 | `inner join {Entity}` |
| `Sort` | 147 | `sort by {Name}` |
| `JoinMany` | 116 | `join many {Entity}` |
| `InnerJoinMany` | 60 | `inner join many {Entity}` |
| `JoinManyCollectionForward` | 8 | `join many collection forward {Entity}` |
| `InnerJoinManyCollectionForward` | 6 | `inner join many collection forward {Entity}` |
| `InnerJoinManyCollection` | 6 | `inner join many collection {Entity}` |
| `JoinManyCollection` | 4 | `join many collection {Entity}` |

Plus the generated FK sugar these lower through: `JoinWithBook` 19, `JoinWithAuthorProfile` 11,
`InnerJoinWithBook` 11, `JoinWithM2OAuthor` 8, `InnerJoinWithAuthorProfile` 8, `InnerJoinWithM2OAuthor`
6, `JoinWithCfkTag` 4, `JoinWithCfkBook` 3, `InnerJoinWithCfkTag` 3, `InnerJoinWithCfkBook` 3. All four
collection verbs are exercised (`FrozenPipelineJoinManyTests.cs:575`, `:576`, `:306`, `:308`).

**Terminals (5):** `Execute` → `select`; `ExecuteCloned` → `select cloned`; `ExecutePooled` →
`select pooled`; `ExecutePooledCloned` → `select pooled cloned`; `Count` → `count`
(`FrozenQuery.cs:63-75`). All five appear in the corpus; two blocks exercise all five in one place
(`PreparedQueryOrDifferentialTests.cs:146-153`, `PreparedQuerySortDifferentialTests.cs:85-90`).

**Executors:** all three asserted, unevenly — 211 `"Pipeline"`, 59 `"Replay"`, 13 `"PointLookup"`
across 283 direct `Plan.Executor` assertions. **A coverage gap in Prague's own tests, reported here
because it was found here and is not this document's to fix:** all 13 `PointLookup` assertions are on
raw caches (`FrozenQueryTests.cs:142-148`, `:193`, `:547`; `FrozenQueryStage2Tests.cs:85`;
`FrozenPipelineTests.cs:716-717`; `PreparedQueryAllocationTests.cs:265`). `PointLookup` is asserted
**nowhere** in `tests/Prague.Generated.Tests/Prepared/`, and the one generated-surface shape that would
bind it (`WithId(v)`) is `Build()`-only. The single executor a PQL point query would most often compile
to has zero end-to-end assertions on the attributed path.

### 8.2 The corpus replay — 101 canonical shapes

**Corpus boundary.** `.Prepare<` / `Prepare()` / `BuildFrozen(` appear in exactly two test directories:
`tests/Prague.Core.Tests/Prepared/` (20 files) and `tests/Prague.Generated.Tests/Prepared/` (9 files,
one of which has no hits). The only other repo-wide occurrences are the two benchmark files, whose
shapes are structural duplicates and were excluded. ~350 raw call sites collapse to **101 canonical
chain structures** — bound vs arg-selector spellings, `array`/`ReadOnlyMemory`/`Func` forms of `in`,
`long`/`DateTime`/`DateTimeOffset` overloads, which concrete index object of a given kind, paging, and
choice of terminal all collapse as identical.

**The dual accounting, reported loudly because it changes the answer.** Every fixture in
`tests/Prague.Core.Tests/Prepared/` is a raw `InMemoryDataCache<int, PqXxx>` over one of 14
unattributed POCOs (`PqBomb`, `PqBombLine`, `PqBombRight`, `PqCustomer`, `PqDoc`, `PqInvoice`, `PqItem`,
`PqLine`, `PqNote`, `PqOrder`, `PqProduct`, `PqRecord`, `PqShipment`, `PqTag`); the `[DataCache]` count
in that directory is **zero**. On the letter of §6.1's scope rule, 85 of 101 shapes are bucket (c) —
84.2% unreachable — and bucket (d) goes vacuous, which is a meaningless result about the *fixtures*
rather than about the *language*. So each shape carries two verdicts: **literal** (strict scope rule)
and **transposed** (what it would be if the same chain ran on an attributed cache).

**The transposed basis is the main tally, because that is the basis on which the design lives or dies.**

| Bucket | Transposed | Literal |
|---|---|---|
| **(a)** expressible | **74** (73.3%) | 12 |
| **(b)** refused — falls to `Replay` by design | **21** (20.8%) | 2 |
| **(c)** unreachable — no generated `With*`/`JoinWith` | **4** (4.0%) | 85 (84.2%) |
| **(d)** no spelling | **2** (2.0%) | 2 |

The 21 **(b)** shapes are the raw material for §3's matrix; every one of them is `Replay`-asserted in
the corpus, not inferred (S101 is the single inferred one). The 4 **(c)** shapes are `ListInProjected`
alone (S6), last-updated via a caller-supplied global-index object (S12), `KeySet` → `ListInProjected`
(S23), and a join on a derived key (S68 — `static id => id - id % 2`, flagged as the least confident
call in the whole exercise: `JOIN ... ON lines.order_id = orders.id - orders.id % 2` is perfectly
ordinary SQL, so if the generator ever emits a keyed join overload this becomes (a); it is not (d)
either way).

**Bucket (d) is two shapes, and the verdict rests on them.**

- **(d)-1, S82 — a sort keyed on the joined row.** Sites: `FrozenPipelineSortBoundedTests.cs:330`
  (`ByLeftCode`, declared `:102`, an `IComparer<JoinResult<PqItem, PqCustomer?>>`) and
  `FrozenPipelineJoinManyTests.cs:499`, `:507` (`ByLineCountThenIdDesc` over
  `JoinResult<PqOrder, QueryResults<PqLine>>`, ordering by `Right.Count`). All three are asserted
  **`Pipeline`** (`FrozenPipelineSortBoundedTests.cs:330`, `FrozenPipelineJoinManyTests.cs:682`) — the
  planner accepts them; they are squarely in scope and PQL still cannot say them. **The obstacle is
  structural.** `[DataCacheSort]` is an attribute on the entity and the generator emits exactly one
  thing from it — `public static readonly IComparer<{documentType}> {Name}Comparer`
  (`CacheGenerator.cs:1679`) — whose type argument is the **left** entity. There is no attribute and no
  generated symbol of any kind whose type is `IComparer<JoinResult<L,R>>`. "Join the customers, then
  order by the customer's code", or worse "order by how many lines each order joined to", has no
  declarable name to reference. Inventing syntax (`sort by joined.Code`, `sort by count(lines)`) would
  make PQL *synthesize* a comparer over an anonymous generated join-row type at compile time —
  materially more work than mapping a name onto an existing field. The mid-chain sort path in codegen
  (`:8017`, `:8120`) sorts by `IComparer<TDocument>` *before* chaining the join, confirming the surface
  only ever orders the left side. **This is the one genuine expressiveness gap.** It is `CACHE078`.
- **(d)-2, S99 — an ad-hoc `IComparer<T>` on a cache that is attributed.** Sites:
  `PreparedGeneratedDifferentialTests.cs:19-24` (used `:139`, `:147`),
  `PreparedGeneratedFrozenJoinTests.cs:119-121` (used `:128`), `PreparedGeneratedJoinTests.cs:23-25`
  (used `:121`, `:125`, `:128`). These run on real `[DataCache]` models with a full generated surface,
  bind `Pipeline`, and the only thing PQL cannot say is the ordering — because the ordering lives in a
  `Compare` method **body** rather than in a **declaration**. One attribute fixes it:
  `[DataCacheSort("ByFeaturedOrderThenId", "FeaturedOrder:1", "Id:1")]`, after which
  `sort by ByFeaturedOrderThenId` lowers to the emitted field. **This is a missing declaration, not a
  missing grammar**, and the corpus proves the mechanism works end to end: `ProductListing` declares two
  sorts (`tests/Prague.Generated.Tests/Fixtures/Entities/ProductListing.cs:10-11`) and six prepared call
  sites already consume the generated fields by name (`PreparedGeneratedFrozenCompositeTests.cs:176`,
  `:182`, `:183`, `:184`; `PreparedGeneratedFrozenMatchOptionalRangeTests.cs:114`;
  `PreparedGeneratedFrozenBranchTests.cs:120`). S98 and S99 are the *same shape*; the only difference is
  whether somebody wrote the attribute. It is `CACHE077`. **Caveat, stated because it bounds the fix:**
  `[DataCacheSort]` expresses multi-key *property* ordering and nothing else, so a comparer with
  genuinely non-lexicographic logic (a computed rank, a null-ordering rule) would not be declarable
  either. §6 hit this twice — `ByIdMod5` and `ByScoreTies` both compute their keys — and both were
  resolved by materializing the key as a stored property. The corpus contains no comparer that
  materializing cannot reach.

**Candidates considered and rejected from (d).** A two-row (d) is only credible if the reader can see
what was thrown out:

| Rejected candidate | Where | Why it is not (d) |
|---|---|---|
| Join filter callbacks — `JoinMany(_lines, _lineByOrder, q => q.Where(l => l.Id % 2 == 0))` | `FrozenPipelineJoinManyTests.cs:302`, `Pipeline` asserted `:572` | A predicate on the joined side is native SQL/KQL vocabulary, and the generated surface already lowers exactly this: `JoinWithCfkTag(q => q.WithName("fantasy"))` (`tests/Prague.Generated.Tests/Join/JoinCollectionFkTests.cs:92`) is nested narrowing through generated `With*`. Calling it (d) would, for consistency, make every top-level `Where(v => v.Flag)` a (d) too, which makes the exercise vacuous |
| `if`/`match` condition lambdas — `static a => a.min > 0` | throughout | They read only from `TArgs`: ordinary boolean parameter expressions (`if @min > 0`), not opaque predicates over `TValue` |
| `FrozenOptions` flags — `Pipeline=false`, `PreserveEagerOrder`, `IndexSideProbes`, `AdaptiveFilterOrdering`, `FuseFilters`, `CapacityHints` | S86–S90, S57 | Build-time knobs, not query text; a compiler takes them as options or attributes the way a query hint is taken. They *cause* `Replay`, which is what (b) is for. Classified (b), and they are exactly the 8 host/option rows of §3 |
| `Plan`/`Explain()` assertions as the observable | `PreparedQueryMatchDifferentialTests.cs:510-544`, `PreparedQueryOptionalRangeDifferentialTests.cs:229-245` | Not query shapes — tests of the planner's own output. The chains themselves are S47 and S8, both (a) |
| `ListInProjected`, caller-supplied global-index object | S6, S12, S23 | Moved to (c): no `With*` twin exists, so there is nothing to lower through. Unreachability, not a naming failure |
| Derived join keys | S68 | Moved to (c). The least confident call in the table |

### 8.3 The production shapes

§6. Shape A lowers cleanly. Shape B lowers only in a reordered form, because of the sorted-join
asymmetry recorded as `CACHE084`.

---

## 9. Known costs, stated plainly

**1. PQL's name resolution mirrors seven `With*` families forever — one of which is dead code, and two
of which disagree with the rest about whether a custom index name counts.**

| Family | Emitted name | Eager | Prepared | Custom `IndexName` honoured? |
|---|---|---|---|---|
| `[DataCacheIndex]` Unique/Many/Range, property-attached | `With{Property.Name}` | `:6493` | `:7204` | **No — dead code (see below)** |
| `[DataCacheIndex]` via class-level `DataCache<T>` | `With{IndexName ?? Property.Name}` | `:2442` | same | Yes |
| FK indexes | `With{Property.Name}` | `:6530` | `:7238` | No |
| has-value | `With{Property.Name}` | `:6540` | `:7245` | No |
| has-not-value | `Without{Property.Name}` | `:6553` | `:7247` | **No** |
| value index | `With{IndexName minus trailing "Index"}` | `:6566-6568` | `:7250-7252` | Yes |
| no-value index | `With{IndexName minus trailing "Index"}` | `:6580` | `:7256-7258` | Yes |
| key | `WithKey` and/or `With{keyPropertyName}` | `:6593-6598` | `:7263-7266` | n/a |
| global last-update | `UpdatedAfter` | `:6601` | `:7269-7285` | n/a |

Three conventions (`With{Prop}`, `With{IndexName}`, `With{IndexName}` minus a suffix) and one prefix
exception (`Without`). The `...Index` suffix stripping applies **only** to the value / no-value
families. `Without{Prop}` ignores a custom index name even though the *field* honours it — proof:
`[DataCacheHasNotValueIndex(IndexName="UncompletedIndex")]` on `CompletedAt` yields the field
`UncompletedIndex` (`tests/Prague.Generated.Tests/Indexing/HasNotValueIndexTests.cs:92`) and the method
`WithoutCompletedAt` (declaration `:45`, call `:346`). This is permanent coupling: every generator
change to a naming rule is a breaking change to PQL's name resolution.

**2. Report-only generator bug (Finding A1) — NOT fixed here, file separately.**
`ExtractIndexedProperties` (`CacheGenerator.cs:8785`) reads
`GetNamedArgument<string>(x.IndexAttribute!, "Name")` at `:8792`, and `GetNamedArgument<T>`
(`:8739-8742`) matches on `a.Key == name`. But `DataCacheIndexAttribute` declares **`IndexName`**
(`src/Prague.Attributes/DataCacheIndexAttribute.cs:128`) — there is no `Name` property. So
`CustomIndexName` is **always null** on the property-attached path, and both the index field name
(`:8793`, `(customIndexName ?? x.Property.Name) + "Index"`) and the `With*` method silently fall back to
the property name. The same capability *is* live on the external `DataCache<T>` path (`:2442` passes
`ip.IndexName`). Row 1 of the table above reads "No — dead code" for that reason. **This was found
while verifying the naming rules and is reported, not fixed; fixing it is out of scope for this
document and would be a behaviour change to the generated surface.**

**3. The lowering story is mixed, not uniform.** Handoff §4 implies everything lowers through `With*`.
It does not. **Leaf predicates** do: unique/many/collection-many/FK equality, `In` and arg-selector
forms (`:7175-7192`, dispatch `:7198-7242`); range including optional bounds (`:7211-7233`); key-set
has-value/has-not-value/value/no-value (`:7245-7260`); key (`:7263-7266`); global last-update
(`:7269-7285`). **Combinators, clauses and terminals do not** — PQL emits the raw call for `Where`
(`PreparedQueryBuilderExtensions.cs:151`, `:167`), `Or`
(`PreparedQueryBuilderOrExtensions.cs:25`/`:50`/`:75`), `If`/`IfElse`
(`PreparedQueryBuilderIfExtensions.cs:32`/`:52`/`:80`/`:100`/`:128`/`:148`), `Match`/`Case`/`Default`
(`PreparedQueryBuilderMatchExtensions.cs:124`/`:145`/`:166`, arms `:60`/`:84`/`:108`),
`Sort`/`SortBounded` (`PreparedQueryBuilderSortExtensions.cs:23`/`:42`/`:67`/`:86`), the explicit join
verbs, raw non-global `LastUpdatedIndex<TKey>` narrowing
(`PreparedQueryBuilderLastUpdatedExtensions.cs:147-276`), `ListInProjected` (`:298`), a hand-held key-set
index (`:317`), and every `Build`/`BuildFrozen` terminal.

There is a free win inside that cost: the prepared `With*` constraint is
`TDiscriminator : struct, IIndexNarrower, ICacheCarrier<{Cache}>` (`CacheGenerator.cs:7146-7150`), which
`PreparedNarrowOnly` and `PreparedConditionalBranch` both satisfy — so a leaf `With*` binds **inside**
`or`/`if`/`match` bodies with no extra work. The composites are spelled three times per placement
(`PreparedQueryBuilderOrExtensions.cs:18-20` explains why: "C# does not infer a type argument from a
constraint, so the receiver's discriminator is spelled out per placement") but overload resolution
picks; PQL emits one spelling and lets the compiler sort it out.

**4. The `[DataCache]`-only scope rule** (§6.1, `CACHE083`). Raw `InMemoryDataCache` queries are outside
the language — including, as written, both canonical production shapes.

**5. The sorted-join asymmetry** (§6.3, `CACHE084`). Four of the eight FK families emit a sorted
parallel and four do not; `sort` before a forward many-to-one, nested or selector-form join has no
generated overload, and neither does `sort` before any `inner join` or any filtered join
(`CacheGenerator.cs:6641-6643`; sorted parallels at `:6714`, `:6762`, `:6837`, `:6907` only). **Also a candidate generator fix, filed not fixed.** If it were fixed,
shape B would lower in its production ordering and `CACHE084` would leave the matrix.

**6. Prepared/eager congruence gaps PQL must not paper over.** The prepared surface is not the eager
surface. Eager has `ReadOnlySpan<T>` and `List<T>` binding forms and `out long max` last-updated forms
that prepared does not (`CacheGenerator.cs:7122-7127` names these three, correctly but incompletely).
Prepared has, and eager does not: the entire `RangeOptional*` family, `DateTime` and window forms of
`UpdatedAfter`, `ReadOnlyMemory<T>`, and arg-selectors everywhere. **PQL must not offer span literals,
`List<T>` bindings, or any `out max` form** — there is nothing to lower them to.

---

## 10. Verdict

Handoff §6 set the exit condition: *"If the refusal matrix ends up longer than the grammar, the answer
is no — and writing that down is a successful outcome, not a failed one."*

**The counts.**

| | Size |
|---|---|
| Grammar (§1) | **35 productions** (34 effective — production 8 is reserved and refused), 45 non-blank lines |
| Refusal matrix (§3) | **25 rows** |

The grammar is longer, by 35 to 25 — but the margin is thin enough that it should not be leaned on,
and the raw comparison flatters neither side (a production and a diagnostic are not the same unit).
Two disaggregations make the comparison mean something:

- **8 of the 25 rows are host/option refusals** — `Pipeline = false`, `PreserveEagerOrder`,
  `IndexSideProbes`, a key type too wide for a range step, a non-`[DataCache]` host. No well-formed
  query triggers them; they fire when someone changes a flag or a model. **3 more are type-state
  backstops** the emitted C# would reject anyway (`CACHE080`, `CACHE081`, `CACHE082`); PQL diagnoses
  them only to keep the error on the query text. **That leaves 14 rows a person can hit by writing a
  query against 34 effective productions.** Roughly two productions of language earned per rule to
  learn. That is a real ratio, not a comfortable one.
- **20 of the 25 rows encode facts the author faces today anyway.** Every (b) shape is
  `Replay`-asserted in the existing corpus: today those authors write a chain, get a silently slower
  executor, and discover it by reading `Explain()`. PQL converts a silent performance cliff into a
  compile error with a named cause. That is the largest single argument in this document, and it does
  not depend on the grammar being short.

**The coverage question, answered.** On the transposed basis, 74 of 101 canonical corpus shapes are
expressible (73.3%), 21 are refused by design and would be `Replay` today (20.8%), 4 are unreachable for
want of a generated surface (4.0%), and **2 have no spelling (2.0%)** — of which only one, S82's sort
keyed on the joined row, is a genuine expressiveness gap. The other, S99, is a missing
`[DataCacheSort]` declaration on a model that already demonstrates the mechanism working six times over.

**The verdict: positive, and conditional.** A KQL-shaped pipeline grammar over the `NarrowerKind`
alphabet covers this corpus, on two conditions that must be accepted up front rather than discovered
later:

1. **PQL requires named, declared sorts.** Ad-hoc `IComparer<T>` instances do not survive the
   translation, and `[DataCacheSort]`'s `"Prop:±1"` vocabulary means a computed sort key must be
   materialized as a stored property. §6 hit this twice in two shapes.
2. **Post-join ordering keyed on right-side data is out of scope in v1.** It is the one genuine gap,
   it is structural, and closing it would mean synthesizing comparers over generated join-row types.

Two things weigh against, and neither is fatal but both are real. **Shape B — one of the two production
shapes `CLAUDE.md` names as the regression canary — does not lower in its production ordering** on the
attributed surface, because the generator emits no sorted parallel for forward many-to-one joins
(§6.3). And **the scope rule excludes raw caches entirely**, which today is 84% of the prepared corpus
by shape count; that number is about the fixtures rather than about production code, but nobody has
measured which is which.

**Recommendation: proceed to a design review, not to an implementation.** The specific things that
review should decide first, because each would change the shape of what gets built:

- Whether the sorted-join asymmetry (§9 cost 5) is fixed in the generator *before* PQL is built. If it
  is, shape B lowers as written and `CACHE084` leaves the matrix.
- Whether `[DataCacheSort]` grows enough vocabulary to absorb the (d)-2 comparers without forcing
  materialized key properties.
- Whether the `[DataCache]`-only scope rule is acceptable against real production usage, which this
  research measured only through test fixtures.

Had the matrix come out longer, the recommendation would have been to stop. It did not, but it came
close enough that the three questions above are prerequisites rather than follow-ups.

---

## Appendix: verification status

Everything above is **static verification on `fa60800`**. `dotnet build` and `dotnet test` were
deliberately not run — the suite is not part of this deliverable and building writes output into the
worktree. Every `file:line` was read; the prepared-builder signatures in §6's lowerings were read in
full so that the emitted C# would compile as written. No file under `src/`, `tests/` or `benchmarks/`
was created or modified by this work. The generator bug in §9 cost 2 and the sorted-join asymmetry in
§9 cost 5 are reported for separate filing and were **not** fixed.

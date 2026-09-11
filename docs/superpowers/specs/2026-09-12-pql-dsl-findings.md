# PQL — research findings and syntax samples

> **Status:** evidence appendix. Design-only; nothing here was implemented.
> **Tree:** `research/dsl` @ `fa60800`. Every `file:line` below was checked against that checkout.
> **Companion:** `2026-09-12-pql-dsl-research.md` is the design document proper (grammar, refusal
> matrix, arg model, hosting, worked lowerings, verdict). This file is the raw material it rests on,
> plus a set of syntax samples for getting a feel for the language.
> **Brief:** `docs/superpowers/plans/2026-09-12-pql-dsl-handoff.md`.

Two caveats that apply to everything below:

- **All verification is static.** No build, no test run. Where a claim rests on an assertion that
  already exists in the test source, that is said explicitly — those are the strongest claims here.
  Where it rests on reading the generator or planner, it is reading, not execution.
- **The samples in Part 2 are sketches, not a locked grammar.** They exist so a reader can judge
  whether the language is worth learning. The normative grammar is in the research doc.

---

# Part 1 — Findings

## F1. Where the handoff is wrong

Six corrections. The handoff was written from `git show` against `7ef34ab` before the worktree had
the sources; the branch has since moved to `fa60800`. Five of the six are not staleness — they were
wrong when written.

| # | Handoff says | Actually | Consequence |
|---|---|---|---|
| 1 | §4: one `With*` naming rule | Seven families, three conventions, one dead branch | Coupling cost is higher than §4 priced it — F2 |
| 2 | §4: collection-backed `Many` indexes "stay out of reach" | They are emitted, bound to `{index}.Forward`, taking the **element** type | Needs a type rule, not an exclusion — F2 |
| 3 | §7: optional predicates → `If` because `With*` carries an implicit null-skip that "can silently return the whole cache" | That no-op exists **only on the eager emitter**. The prepared parameter is `notnull` and links unconditionally | Rule survives, **reason inverts** — F7 |
| 4 | §7: `sort by` vs `sort bounded by` stays explicit because inference "changes which rows come back on comparer ties" | `context/query.md:25` — "Ties no longer decide it … the choice is purely about cost" | Rule survives, reason restated — F14 |
| 5 | §3: resolver verbs are 6 | The corpus exercises **10** | A 6-verb grammar under-serves the corpus by four — F15 |
| 6 | §5/§6: implies the production-shape fixture is attribute-declared | It has **zero** `[DataCacheIndex]` attributes | Forces a scope rule and a restatement — F5 |

## F2. `With*` naming — the corrected table

Two sibling emitters, not one. Both exist; the prepared one was the thing in doubt.

- **Eager** — `GenerateCombinedBuilderExtensions`, `src/Prague.Codegen/CacheGenerator.cs:6418` →
  `{Cache}QueryExtensions` (`:6447`).
- **Prepared** — `GeneratePreparedBuilderExtensions`, `:7130` → `{Cache}PreparedQueryExtensions`
  (`:7170`). Every emitted body is `=> builder.UseIndex(...)` (`EmitForward`, `:7166`).

So `With*` **is** codegen'd sugar over `UseIndex`, on both sides. Seven families:

| Family | Emitted name | Eager | Prepared | Custom `IndexName` honoured? |
|---|---|---|---|---|
| `[DataCacheIndex]` Unique/Many/Range, property-attached | `With{Property.Name}` | `:6493` | `:7204` | **No — dead code (F3)** |
| `[DataCacheIndex]` via class-level `DataCache<T>` (external) | `With{IndexName ?? Property.Name}` | `:2442` | same | Yes |
| FK indexes | `With{Property.Name}` | `:6530` | `:7238` | no |
| has-value | `With{Property.Name}` | `:6540` | `:7245` | no |
| has-not-value | `Without{Property.Name}` | `:6553` | `:7247` | **No** |
| value index | `With{IndexName minus trailing "Index"}` | `:6566-6568` | `:7250-7252` | yes |
| no-value index | `With{IndexName minus trailing "Index"}` | `:6580` | `:7256-7258` | yes |
| key | `WithKey` and/or `With{keyPropertyName}` | `:6593-6598` | `:7263-7266` | n/a |
| global last-update | `UpdatedAfter` | `:6601` | `:7269-7285` | n/a |

Three sub-findings:

1. **Suffix stripping applies only to the value / no-value families**, not to the ordinary index
   families. §4 generalised it.
2. **`Without{Prop}` ignores a custom index name.** Proven by an existing test, not inferred:
   `[DataCacheHasNotValueIndex(IndexName = "UncompletedIndex")]` on `CompletedAt`
   (`tests/Prague.Generated.Tests/Indexing/HasNotValueIndexTests.cs:45`) produces a **field** named
   `UncompletedIndex` (asserted `:92`) but a **method** named `WithoutCompletedAt` (called `:346`,
   in a test literally named `WithoutCompletedAt_CustomNamedIndex_ShouldWork`, `:335`). Field
   honours it; method does not.
3. **Collection-backed `Many` indexes are reachable**, contra §4. Eager `:6496-6499`, prepared
   `:7206-7209` — `With{Prop}` bound to `{IndexName}.Forward`, taking the **element** type. What
   they need is a type rule (parameter type ≠ property type), which PQL's resolver must encode.

**Net:** the locked "lower through `With*`" decision survives, but PQL's name resolver must mirror
a seven-branch table containing one dead branch and two families that disagree about whether
`IndexName` matters — permanently, because the DSL's spelling becomes an API contract. That is a
real cost and the design doc states it rather than repeating §4's one-liner.

## F3. The dead `IndexName` lookup — a latent generator bug (REPORT ONLY)

`ExtractIndexedProperties` (`CacheGenerator.cs:8785`) reads:

```csharp
:8792  var customIndexName = GetNamedArgument<string>(x.IndexAttribute!, "Name");
:8793  var indexName = (customIndexName ?? x.Property.Name) + "Index";
```

`GetNamedArgument` (`:8739`) matches on `a.Key == name`. But `DataCacheIndexAttribute` declares
`IndexName` (`src/Prague.Attributes/DataCacheIndexAttribute.cs:128`) — there is **no `Name`
property** (the full set is `PropertyName` `:123`, `IndexName` `:128`, `IndexType` `:133`,
`Symmetric` `:141`, `InitialCapacity` `:149`). So `customIndexName` is always `null` on the
property-attached path, and both the index field name and the `With*` method silently fall back to
the property name. The same capability **is** live on the external `DataCache<T>` path, which
passes `ip.IndexName` (`:2442`).

This is a Prague bug, not a PQL problem. **It is not fixed in this work** — it is reported here and
should be filed as its own issue. PQL must be designed against observed behaviour (property name
wins), and if the bug is ever fixed the DSL's name resolution changes with it, which is itself an
argument for pinning the naming contract in a spec.

## F4. Shapes A and B still bind `Pipeline` on `fa60800` — confirmed, from source assertions

Stronger than "verified": the shapes do not merely bind `Pipeline`, they **already assert it**.
All in `tests/Prague.Core.Tests/Prepared/PreparedQueryProductionShapeDifferentialTests.cs`:

- **Shape A** `:130-138` — three list lanes → `SortBounded(ByIdMod5)` → `JoinOne(_details)` →
  `BuildFrozen(EagerOrder)`. `:138` asserts `Executor == "Pipeline"`, with the message *"SortBounded
  before an outer JoinOne: the joined pipeline (step 6)"*.
- **Shape B** `:180-189` — `UpdatedAfter` lane + two list lanes → `SortBounded(ByScoreTies)` → two
  chained `JoinOne`s. `:188` asserts `"Pipeline"`; `:189` asserts
  `Narrowers[0].Kind == NarrowerKind.LastUpdatedAfter`.
- **Shape B range twin** `:207-215` — first lane swapped for
  `.UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t))`. `:215` asserts `"Pipeline"`.
- Further assertions `:267`, `:308-309`. Structural analogues named "shape A" / "shape B" at
  `FrozenPipelineSortBoundedTests.cs:320,322`.

Planner path traced rather than assumed: `options.Pipeline` defaults true (`FrozenOptions.cs:54`);
`PipelineJoinedExecutor.Accepts` (`:104-111`) passes both (A: `Joins=1`/`Fused=1`; B: `Joins=2`/
`Fused=2`, both **outer**, so `PreserveEagerOrder`'s regrouping veto at `:499` — inner-left-symmetric
only — does not fire); `PipelinePlanner.TryPlan` passes at 3 steps each, well under `MaxSteps`.
B's `LastUpdatedAfter` first lane is vetoed by `Marks()` (`PipelinePlanner.cs:110-111`) **only
inside an `Or` branch**, and B has no `Or`.

`PlanInfo.Executor` is a public `string` (`NarrowerDescriptor.cs:136`), fed from `TExecutor.Name`
at `FrozenQuery.cs:53`.

## F5. The production-shape fixture is attribute-free — this forces a scope rule

`PreparedQueryProductionShapeDifferentialTests.cs` contains **zero `[DataCacheIndex]` attributes**.
`PqItem` is a plain `internal sealed class` (`PreparedQueryDifferentialTests.cs:12`) on a raw
`InMemoryDataCache<int, PqItem>`, with every index built imperatively in `[OneTimeSetUp]`:

```csharp
_items.CacheKeyValueListIndex<int>(static (_, v) => v.Group)
```

A raw cache has **no generated `With*` surface at all**. This collides head-on with the locked
lowering decision, and the resolution is not to relitigate it but to state the consequence:

> **SCOPE RULE.** PQL applies only to `[DataCache]`-generated caches. Raw `InMemoryDataCache`
> queries — including the two canonical production shapes *exactly as the tests write them* — are
> outside the language.

Therefore the worked lowerings must **restate shapes A and B as attributed `[DataCache]` POCOs**,
show the emitted C# against that restatement, and say plainly that the restatement was necessary.
Presenting a lowering of the literal test chains would be a fiction.

## F6. Prepared vs eager `With*` are not congruent

Unique / Many / collection-Many / FK — **eager emits 3 overloads** (`EmitWithMethods`, `:6451-6485`):

| # | Signature | Behaviour |
|---|---|---|
| 1 | `{T}? value` (`:6455`) | **skips when null** (`:6459`, `:6461`, else `return builder;` `:6463`) |
| 2 | `ReadOnlySpan<{T}> values` (`:6469`) | skips when empty (`:6472`) |
| 3 | `List<{T}>? values` (`:6479`) | skips when null/empty (`:6482`), forwards via `CollectionsMarshal.AsSpan` |

**Prepared emits 5** (`:7175-7192`), a different set:

| # | Signature | Narrower |
|---|---|---|
| 1 | `{T} value` (`:7176`) | `UniqueIndexEq` / `ListIndexEq` |
| 2 | `Func<TArgs,{T}> selector` (`:7178`) | `…EqArg` |
| 3 | `ReadOnlyMemory<{T}> values` (`:7182`) | `…In` |
| 4 | `{T}[] values` (`:7185`) | `…In` |
| 5 | `Func<TArgs,ReadOnlyMemory<{T}>> selector` (`:7188`) | `…InArg` |

Other families diverge too:

- **Range** — eager 2 (`:6502-6520`); prepared **4** (`:7211-7233`), the extra two being the
  optional-bounds forms `RangeOptionalArgNarrower` / `RangeOptionalRefArgNarrower` (picked at
  `:7224` by `prop.Type.IsValueType`) and `RangeOptionalNarrower` (`:7229-7232`). **Both
  optional-bounds groups are prepared-only — no eager twin.**
- **`UpdatedAfter`** — eager 4 (`:6601-6629`, including two `out long max` forms); prepared **8**
  (`:7269-7285`): a loop at `:7271` over `{long, DateTime, DateTimeOffset}` × `{after,
  after+untilInclusive}` = 6, plus `Func<TArgs,long>` (`:7279-7281`) and two-selector
  (`:7282-7284`). **No `out long max` twin**; eager has no `DateTime` and no window form.

The generator's own doc comment (`:7122-7127`) names three eager-only gaps (`ReadOnlySpan`,
`List<T>`, `out long max`) and is **correct but incomplete** — it omits that `RangeOptional*` is
prepared-only, that prepared `UpdatedAfter` adds `DateTime` and window forms, that the single-value
nullability differs (`{T}?` vs `{T}`), and that arg-selectors exist on the prepared side only.

**Consequence for PQL:** it must not offer span literals, `List<T>` bindings, or any `out max`
last-updated form. None of the three has a prepared twin to lower to.

## F7. The null/empty no-op asymmetry — the handoff's reason is backwards

Eager `With*` silently no-ops on null/empty (`:6459`, `:6461`, `:6463`, `:6472`, `:6482`).
Prepared `With*` **never** does. The emitted body is an unconditional `=> builder.UseIndex(...)`
(`:7166`), and the hand-written target it forwards to is
`src/Prague.Core/QueryBuilders/Prepared/PreparedQueryBuilderExtensions.cs:83-98`:

```csharp
:90      CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
:91      TIndexKey value)
:97  where TIndexKey : notnull
:98  => Link(in builder, new UniqueIndexEq<TKey, TValue, TIndexKey, TArgs>(index, value));
```

Non-nullable parameter, constrained `notnull`, `Link` called unconditionally. Same shape at `:100-115`
(`UniqueIndexEqArg`), `:117-132` (`ListIndexEq`), `:134-149` (`ListIndexEqArg`). At replay it is
likewise unconditional (`Narrowers.cs:23-25`).

**One exception, and only one** — the range-optional family. `Narrowers.Range.cs:113` documents
"an unbounded range is a recorded no-op"; `:127-130`:

```csharp
public void Apply<TCore>(ref TCore core, in TArgs args) {
    if (!_range.IsUnbounded) core.UseIndexInternal(_index, OptionalRange<TIndexKey>.PassThrough, _range);
}
```

with the arg twin at `:167-174` (evaluates both selectors, then `if (!from.HasValue && !to.HasValue) return;`)
and the reference-type twin at `:191`/`:210`. Even there **the step is still recorded** and still
described as `NarrowerKind.Range` — the skip is a per-execution no-op, not an absent link.

**So:** handoff §7 says optional predicates must lower to `If` *because* `With*` carries a dangerous
implicit skip. On the prepared path that skip does not exist. **The rule survives; the reason
inverts.** `If` is not there to avoid an implicit skip — it is the *only* way to express optionality,
because the parameter is `notnull` and there is no null to pass. Ranges are the documented exception
and get the `RangeOptional*` family instead.

## F8. What has no `With*` twin — the real lowering boundary

**Has a `With*` twin** (PQL lowers through the generated surface): unique/many/collection-many/FK
equality + membership + arg-selector (`:7175-7192`, dispatch `:7198-7242`); `Range` including
optional bounds (`:7211-7233`); key-set has-value / has-not-value / value / no-value (`:7245-7260`);
key / `WithKey` (`:7263-7266`); global last-update `UpdatedAfter`, 8 forms (`:7269-7285`).

**Has no `With*` twin** — PQL must emit the raw call:

| Construct | Raw entry point |
|---|---|
| `ListInProjected` | `PreparedQueryBuilderExtensions.cs:302` |
| key-set via an explicitly-held `CacheKeySetIndex` | `:320` |
| `Where` (constant / parameterized) | `:155` / `:175` |
| `Or` | `PreparedQueryBuilderOrExtensions.cs:25`, `:50`, `:75` |
| `If` / `IfElse` | `PreparedQueryBuilderIfExtensions.cs:32`,`:52`,`:80`,`:100`,`:128`,`:148` |
| `Match` / `Case` / `Default` | `PreparedQueryBuilderMatchExtensions.cs:124`,`:145`,`:166`; arms `:60`,`:84`,`:108` |
| `Sort` / `SortBounded` | `PreparedQueryBuilderSortExtensions.cs:23`,`:42`,`:67`,`:86` |
| explicit `JoinOne`/`JoinMany`/`JoinManyCollection`/`…Forward` + `Inner*` | hand-written |
| raw (non-global) `LastUpdatedIndex<TKey>` | `PreparedQueryBuilderLastUpdatedExtensions.cs:150-276` (8 overloads) |
| `Build` / `BuildFrozen` | `PreparedQueryBuilderExtensions.cs:338`,`:365`,`:385`,`:395`; sorted twins `PreparedQueryBuilderSortExtensions.cs:113`,`:129`,`:143`,`:153` |

> **PQL's leaf predicates lower through `With*`. PQL's combinators, clauses and terminals do not.**
> It is a **mixed lowering**, not the uniform one §4 implies, and the design doc must say so.

One free win: the prepared `With*` constraint (`:7145`) is
`TDiscriminator : struct, IIndexNarrower, ICacheCarrier<{Cache}>`, which admits `PreparedNarrowOnly`
and `PreparedConditionalBranch` — so a leaf `With*` binds **inside** `Or`/`If`/`Match` bodies with no
extra work. Composites are spelled three times per placement (`PreparedQueryBuilderOrExtensions.cs:19-20`
explains why: *"C# does not infer a type argument from a constraint"*), but overload resolution picks;
PQL emits one spelling and lets the compiler sort it out.

## F9. FK joins bind unchanged — and join filters cannot read execution args

The join constraint (`CacheGenerator.cs:6667`, verbatim) is load-bearing precisely for what it
*omits*:

```csharp
where TDiscriminator : struct, IBaseJoinable, ICacheCarrier<{cacheClassName}>
where TExecutor : struct, ICandidatesExecutor<{keyTypeName}, {documentTypeName}>
where TResolverChain : struct, IResolvers
```

No `ICandidatesFilterer`. `PreparedNarrowers` implements `ICandidatesExecutor` but **deliberately
not** `ICandidatesFilterer` (`PreparedQueryBuilderSortExtensions.cs:7-9`: *"The eager overloads are
constrained on `ICandidatesFilterer`, which the recorder deliberately lacks, so these rebind them"*).
That single omission is what makes eager `UseIndex`/`Where`/`With*` **fail** to bind on a prepared
builder while the FK join extensions **still** bind. Confirmed empirically at
`tests/Prague.Generated.Tests/Prepared/PreparedGeneratedFrozenJoinTests.cs:71-72`, `:87-91` and
`PreparedGeneratedJoinTests.cs:87-88`.

Names: `JoinWith{Entity}` (`:1611`, `:1656`, `:6943`, `:7005`), `InnerJoinWith{Entity}`
(`:6728`, `:6777`, `:6974`, `:7046`, `:7096`). Three filter flavours, `EmitFkJoinWithOverloads`
`:7299-7352`: (a) no filter `:7321-7327`; (b) `Func<nonExec,nonExec> filter` `:7333-7339`;
(c) `Func<nonExec,TArg,nonExec> filter, TArg arg` `:7345-7351`.

> **LOAD-BEARING.** The filter callback receives an **eager** paired builder
> (`CacheQueryBuilder.cs:944`), and flavour (c)'s `TArg` is the **build-time** arg captured into the
> resolver — not the query's `TArgs`. **A join-side filter in a prepared query is fixed at build
> time and cannot read the execution arguments.** `@param` inside a join filter must be a
> diagnostic, not a silent capture.

FK-driven and explicit joins are two different things PQL must keep distinct: `JoinWith{Other}` is
attribute-driven, takes no index argument, and covers nine FK directions (`:6707`/`:6726` reverse
collection M:N, `:6755`/`:6775` reverse one-to-many, `:6789`/`:6794` nested, `:6830`/`:6850` reverse
OneToOne, `:6900`/`:6919` forward collection, `:6965`/`:6972` forward ManyToOne, `:7037`/`:7044` and
`:7087`/`:7094` selector forms). The explicit `JoinOne(index, cache)` / `JoinMany(index, cache)`
forms are hand-written and take an index plus a target cache.

## F10. Arg binding

- **`TArgs` is completely unconstrained** — `grep -rn "where TArgs" src/` returns **zero** hits. Any
  type works; the ValueTuple convention is convention only. The corpus proves it: `_authors.Prepare<int>()`
  (`PreparedGeneratedFrozenJoinTests.cs:128`) uses a bare `int`. Empty case is
  `Prepared/NoArgs.cs:4` (`public readonly struct NoArgs;`), used by `Prepare()`
  (`PreparedQueryBuilderExtensions.cs:22`).
- **Threading is by `in` reference throughout** — `INarrower.Apply<TCore>(ref TCore core, in TArgs args)`
  (`Prepared/INarrower.cs:16`), `INarrowerChain.Replay<TCore>(ref TCore, in TArgs)` (`:34`). No boxing,
  no copy.
- **Selectors are invoked once per execution, not per row.** Stored as `Func<TArgs,T>` fields in
  closed-generic narrower structs (`Narrowers.cs:38`, `:88`, `:138`, `:184`).
- **Parameterized `Where` is allocation-free** via `Prepared/ArgPredicate.cs` — delegates created
  once in the ctor (`:25-30`), `Bind` `:38-42`, `BindFused` `:45-49`, `Clear` `:53-58`; pool
  `ArgPredicatePool<TValue,TArgs>` `:77`, `InitialCapacity = 4` `:78`, `[ThreadStatic]` `:80-81`.
  Class doc `:65-76`: stack discipline gives re-entrancy safety, thread-static gives thread safety,
  `Reset` after `Execute`/`Count` but **not** after replay.
- **Last-updated arg selectors take unix-ms only** —
  `PreparedQueryBuilderLastUpdatedExtensions.cs:9-10`: *"Parameterized forms take unix-ms selectors
  only; convert other time types inside the selector."* Signatures `Func<TArgs,long>` at `:121`,
  `:137`, `:253`, `:269`. **PQL must emit the conversion inside the lambda** — a `DateTime` parameter
  is a PQL-level convenience, not a runtime one.
- **`Match` tag selector** is `Func<TArgs,TTag> where TTag : notnull`
  (`PreparedQueryBuilderMatchExtensions.cs:130`, `:138`), run once per execution (`:37-39`).
- **`skip` / `take` are execution-time, not build-time** — every terminal is
  `Execute(in TArgs args, int skip = 0, int take = int.MaxValue)` (`FrozenQuery.cs:63`, `:66`, `:69`,
  `:72`). They are not part of `TArgs` and PQL must not fold them into it.

## F11. Type-state machinery PQL inherits for free

Discriminators: `PreparedQueryDiscriminator<TCache>` (top level), `PreparedNarrowOnly<TCache>`
(inside an `Or` branch), `PreparedConditionalBranch<TCache>` (inside `If`/`IfElse`/`Match` arms),
`SortedQuery<PreparedQueryDiscriminator<TCache>>` (after a sort). All constrained
`struct, IIndexNarrower` at every prepared call site.

This means the emitted C# gets these checked by the C# compiler rather than by a DSL binder:

| Rule | Mechanism |
|---|---|
| no `Where` inside an `Or` branch | branches are `PreparedNarrowOnly<TCache>` |
| no narrowing or filtering after a sort | result is `SortedQuery<…>`, which *"admits only joins and `Build()`"* — `PreparedQueryBuilderSortExtensions.cs:13-15` |
| no `Case` after `Default` | `Case` constrains `TArms : IOpenMatchArms<…>` (`PreparedQueryBuilderMatchExtensions.cs:73`, `:96`, `:116`) |
| **a `Match` must have a `Default`** | `Match` requires `IClosedMatchArms<…>` (`:139`, `:160`, `:181`); `DefaultArm` is the sole implementer, so an unclosed chain does not bind — **CS0315** |
| no index from a different cache | `TDiscriminator : ICacheCarrier<{cacheClassName}>` |

The `Match` row landed **mid-research**, in `fa60800` itself. Before that commit an absent `Default`
was a silent no-op and the design had to carry a rule about it; now the design carries nothing. That
is the whole argument for the lowering choice, demonstrated rather than asserted.

Chain plumbing: `INarrower<TKey,TValue,TArgs>` (`Prepared/INarrower.cs:13`), `Apply` `:16`,
`Describe` `:22`; `INarrowerChain` `:31`, `Replay` `:34`, `ReplayIndexOnly` `:43`, `Describe` `:46`.
`EmptyNarrowers` + `NarrowerLink<TPrev,TNarrower,…>`, every link a closed generic so the JIT
specializes `Replay`.

`Sort` on a simple query requires `Resolvers<BaseResolver<TKey,TValue>>` incoming (`:29`) —
sort-before-join and sort-after-join are two distinct overloads (`:23` vs `:42`).

## F12. Diagnostic IDs — `CACHE060+` is free, and the existing block is a mess

All in `src/Prague.Codegen/CacheGenerator.cs`. **40 distinct IDs**, 44 descriptor sites, 46
`DiagnosticDescriptor` occurrences in `src/` (the other two are `DataCacheFromRefactoring.cs:22`
`DATACACHE001` and `:23` `DATACACHE002`).

In use: `CACHE001` `:419`, `002` `:479`, `004` `:393`, `005` `:3522`, `006` `:3286`, `007` `:3604`,
`008` `:700`, `009` `:770`, `010` `:1917`, `011` `:1937`, `012` `:2007`, `013` `:2034`, `014` `:2113`,
`015` `:2132`, `016` `:2165`, `017` `:2200`, `018` `:2219`, `019` `:1982`, `020` `:2247`, `021` `:460`,
`022` `:864` **and** `:2147`, `023` `:884`, `030` `:256`, `031` `:275`, `032` `:810`, `033` `:826`,
`034` `:843`, `035` `:297`, `040` `:641`, `041` `:658`, `042` `:672`, `043` `:8897`, `044` `:8916`,
`045` `:580`, `046` `:599` **and** `:615`, `047` `:559`, `048` `:537`, `049` `:754`, `050` `:739`,
`051` `:496` **and** `:2101`.

**Highest in use: `CACHE051`. Free: `CACHE003`, `CACHE024-029`, `CACHE036-039`, `CACHE052-CACHE099`.
`CACHE060-CACHE099` is confirmed entirely unused**, so §5's assumption holds.

Collisions are worse than the handoff knew:

- `CACHE022` carries **two different messages** (`:864`, `:2147`).
- `CACHE046` is emitted from two sites (`:599`, `:615`).
- `CACHE051` fires from two paths (`:496`, `:2101`) with an identical message.
- **`CACHE021` (`:460`) and `CACHE022` (`:2147`) share an identical title *and* message** — two IDs
  for one diagnostic.

That is the argument for PQL putting its descriptors in **one central table** rather than ~45 inline
ones.

## F13. `ListInProjected` and `KeySet` — the two hardest to spell

**`ListIndexInProjected`** — `Narrowers.cs:210-232`. Doc `:206-209`: *"List (1:N) index membership
over a bound set of foreign values, each projected to an index key at replay by `keySelector`."*
Fields `:214-216` = the index, a `ReadOnlyMemory<TOtherValue> _values`, and a
`Func<TOtherValue,TIndexKey> _keySelector`. `Apply` `:225-226`. `Describe` → `ListInProjected` `:228`.
Pipeline step `:230-231`. Builder entry `:302`.

> **There is no arg-selector twin.** The value set is **bound at build time** and cannot come from
> `TArgs`; only the per-element projection is a delegate. So a PQL `in @param` over a projected list
> index is **not expressible**. It also has **no `With*` twin at all** — list indexes get only
> eq / eq-arg / in(`ReadOnlyMemory`) / in(array) / in-arg at `:7175-7192`, with no key-selector
> overload.

**`KeySetNarrower`** — `Narrowers.cs:234-250`. Doc `:234`: *"the index itself is the whole
description, nothing to bind."* Single field `:238`, `Apply` `:243-244`, `Describe` `:246`,
step `:248-249`. Builder entry `:320`. Codegen sugar `EmitKeySetMethod` `:7194-7195`, used by the
four attribute-declared families at `:7245-7260` — so attribute-declared key sets **do** get a
parameterless `With*`/`Without*`; only a hand-held `CacheKeySetIndex` needs the raw call.

`NarrowerKind` consumers worth knowing: `FrozenQuery.cs:553-554`; `Pipeline/Steps/ListSteps.cs:96`
(`Kind => _keySelector is null ? NarrowerKind.ListIn : NarrowerKind.ListInProjected`);
`PipelinePlanner.cs:111` (the "marking" set); `PipelineExecutor.cs:285`; `Steps/KeySetStep.cs:21`.

## F14. Limits, and the stale `Sort`/`SortBounded` reason

- `MaxSteps = 16` (`Pipeline/PipelineStep.cs:13`), `MaxOrBranches = 8` (`:19`).
- `PipelinePlanner.TryPlan` `:42-59`; the `_steps.Count is 0` check `:48`; filter-only path `:72-76`.
- `Marks()` `:110-111`: `kind is Range or KeySet or LastUpdatedAfter or LastUpdatedBetween` — these
  must be **first** inside an `Or` branch.
- `FrozenPlanner.IsPointLookup` `FrozenQuery.cs:388`. `FrozenOptions.Pipeline` defaults true (`:54`).
- `NarrowerKind` — 15 members, `NarrowerDescriptor.cs:6-22`.

**Stale reason.** `context/query.md:25` now reads: *"Ties no longer decide it: both plans break ties
by encounter order, so the choice is purely about cost."* But `context/query.md:18` **still carries
the old tie-based reason** — the repo's own doc is internally inconsistent. What was *"tried and
reverted"* (`:26`) is the **large-prefix fallback**, not `Sort`/`SortBounded` inference. The rule
(never infer) survives; the reason must be restated as **cost plus honouring the caller's declared
intent**.

`context/query.md:58` confirms both production shapes *"take the joined pipeline since step 6, with
their joins fused since step 5."*

There is **no checked-in source-generator output** showing real emitted `With*` signatures — the only
`*.generated.cs` in `src/` are `Prague.Core/JoinResults.generated.cs` and
`JoinQueryBuilders.generated.cs`, both T4 output. Everything in F2/F6 is read from the emitter.

## F15. The corpus replay — 101 canonical shapes

**Boundary.** `.Prepare<` / `Prepare()` / `BuildFrozen(` appear in exactly two test directories:
`tests/Prague.Core.Tests/Prepared/` (20 files) and `tests/Prague.Generated.Tests/Prepared/` (9 files,
one of which — `PreparedParity.cs` — has zero hits). The only other repo-wide occurrences are
`benchmarks/Prague.Benchmarks/{FrozenQuery,PreparedQuery}Benchmarks.cs` and `RESULTS.MD`; those
shapes are structural duplicates and were excluded.

**Ground truth.** The corpus carries **283 direct `Plan.Executor` assertions** — 211 `"Pipeline"`,
59 `"Replay"`, 13 `"PointLookup"`. Two blocks do most of the work:
`FrozenPipelineSortBoundedTests.cs:313-333` and `FrozenPipelineJoinManyTests.cs:570-586`. Shapes
without an assertion are marked *inferred*.

**Collapsing.** A shape is a distinct chain **structure**. Collapsed as identical: bound vs
arg-selector spellings; array vs `ReadOnlyMemory` vs `Func<TArgs,ReadOnlyMemory>` for `In`;
`long`/`DateTime`/`DateTimeOffset` overloads; which concrete index object of a given kind;
skip/take paging; choice of terminal. ~350 raw call-site rows → **101 canonical shapes**.

### The dual accounting — stated loudly, because it changes the answer

Every fixture in `tests/Prague.Core.Tests/Prepared/` is a raw `InMemoryDataCache<int, PqXxx>` over an
unattributed POCO; the `[DataCache]` count in that directory is **zero**. The 14 POCOs: `PqBomb`,
`PqBombLine`, `PqBombRight`, `PqCustomer`, `PqDoc`, `PqInvoice`, `PqItem`, `PqLine`, `PqNote`,
`PqOrder`, `PqProduct`, `PqRecord`, `PqShipment`, `PqTag`.

On the **letter** of the F5 scope rule that makes 85 of 101 shapes (84.2%) bucket (c) and leaves
bucket (d) **vacuous** — a meaningless result. So each shape carries two verdicts:

- **LITERAL** — strict scope rule.
- **TRANSPOSED** — what the same chain would be on an attributed `[DataCache]` cache.

**The main tally uses the transposed basis**, because that is the basis on which the design lives or
dies. The literal number is reported alongside, not hidden.

### Tally

| Bucket | Meaning | Count | Share |
|---|---|---|---|
| **(a) EXPRESSIBLE** | PQL has a spelling | **74** | **73.3%** |
| **(b) REFUSED** | falls to `Replay` by design — out of scope by the locked scope rule | **21** | **20.8%** |
| **(c) UNREACHABLE** | no generated `With*`/`JoinWith` surface to lower through | **4** | **4.0%** |
| **(d) NO SPELLING** | in scope, but the grammar has no syntax for it | **2** | **2.0%** |

Literal basis for contrast: (c) = 85 (84.2%); the remaining 16 split 12 (a) / 2 (b) / 2 (d).

### The 21 refused shapes — raw material for the refusal matrix

Abbreviations: files under `tests/Prague.Core.Tests/Prepared/` unless prefixed `GEN:` =
`tests/Prague.Generated.Tests/Prepared/`. `FQT`=`FrozenQueryTests.cs`,
`FQS2`=`FrozenQueryStage2Tests.cs`, `FPT`=`FrozenPipelineTests.cs`, `FPS`=`FrozenPipelineSeedTests.cs`,
`FPC`=`FrozenPipelineCompositeTests.cs`, `FPJ`=`FrozenPipelineJoinTests.cs`,
`FPJM`=`FrozenPipelineJoinManyTests.cs`, `FPSB`=`FrozenPipelineSortBoundedTests.cs`.

| ID | Shape | Sites | Evidence |
|---|---|---|---|
| S13 | no narrowing at all | FQT:160 | Replay **asserted** |
| S14 | `Filter` only, no seed | FQT:161; FPT:725 | Replay **asserted** |
| S28 | `Or`, nested `Or` beside a leaf in its branch | FPC:445,715 | Replay **asserted** |
| S29 | `Or`, 9 branches after flattening (> `MaxOrBranches`) | FPC:471 | Replay **asserted** |
| S31 | `Or` branch, marking narrower not first | FPC:323,324,325 | Replay **asserted** |
| S41 | `If` arm with a marking narrower, inside an `Or` | FPC:326 | Replay **asserted** |
| S46 | `If` with only a `Where`, no index anywhere | FPC:713,714 | Replay **asserted** |
| S57 | index → `InnerJoinOne` under `PreserveEagerOrder` | FPJ:684,685; FPSB:328; FPJM:579 | Replay **asserted** |
| S58 | index → `JoinOne` **with** a filter callback | FPJ:689,690 | Replay **asserted** |
| S59 | `JoinOne` → `JoinOne`, one unfusable | FPJ:691,693 | Replay **asserted** |
| S60 | `JoinOne`, no index seed | FPJ:688; FPSB:333 | Replay **asserted** |
| S74 | index → filtered `JoinOne` → `JoinMany` | FPJM:583 | Replay **asserted** |
| S75 | `JoinMany`, no seed | FPJM:584 | Replay **asserted** |
| S79 | `Sort`/`SortBounded`, no index seed | FPSB:317,318; FPS:520 | Replay **asserted** |
| S83 | `JoinOne` → `Sort`, no seed | FQT:180; FPSB:333 | Replay **asserted** |
| S86 | anything under `FrozenOptions { Pipeline = false }` | FPSB:316,323; FPJM:585; FPT:731 | Replay **asserted** |
| S87 | `Range` over an unmanaged key > 16 bytes | FPT:733 | Replay **asserted** |
| S88 | `Range` over a struct key containing references | FPT:734 | Replay **asserted** |
| S89 | `Range` step under `IndexSideProbes` | FPT:736,501; FPC:716 | Replay **asserted** |
| S90 | key-side range twin (window walk) | FPT:462 | Replay **asserted** |
| S101 | `Or` branch, `Range` not first | GEN:Branch:43-46 | Replay *inferred* |

Also stage-dependent: S15 (`ListEq` → `ListEq`) and S20 (`ListEq` before `UniqueEq`) are `Pipeline`
normally but `Replay` under Stage 2 — asserted FQS2:338, FQT:166.

### The 4 unreachable shapes

| ID | Shape | Sites | Why |
|---|---|---|---|
| S6 | `ListInProjected` alone | Narr:124; FQT:218 | no `With*` twin; list indexes get no key-selector overload (F13) |
| S12 | `LastUpdated*` via a caller-supplied global-index object | Narr:265,273,284,285 | only the global index gets `UpdatedAfter` sugar |
| S23 | `KeySet` → `ListInProjected` | Narr:127 | as S6 |
| S68 | index → `JoinMany` with a **key-selector lambda** | FPJM:301,304 | `JoinWith{T}` derives its key from the FK attribute and takes no key selector; Pipeline **asserted** FPJM:274 |

S68 is flagged as the **least confident call** in the whole table. `JOIN … ON lines.order_id =
orders.id - orders.id % 2` is perfectly ordinary SQL, so if the generator ever emits a keyed join
overload this becomes (a). It is not (d) either way.

### The 2 no-spelling shapes — what the verdict rests on

**(d)-1 — S82: sort keyed on the joined row, not the left entity.**

Sites: `FPSB:330` (`ByLeftCode`, declared `:102`, an `IComparer<JoinResult<PqItem, PqCustomer?>>`);
`FPJM:499` and `:507` (`ByLineCountThenIdDesc` over
`IComparer<JoinResult<PqOrder, QueryResults<PqLine>>>`, ordering by `Right.Count`). **All asserted
`Pipeline`** (FPSB:330, FPJM:682) — squarely in scope; the planner accepts them.

The obstacle is structural. `[DataCacheSort]` is an attribute on the entity and the generator emits
exactly one thing from it:

```csharp
CacheGenerator.cs:1679   public static readonly IComparer<{documentType}> {Name}Comparer
```

The document type is the **left** entity. There is no attribute and no generated symbol of any kind
whose type is `IComparer<JoinResult<L,R>>`. So *"join the customers, then order by the customer's
code"* — or worse, FPJM:499's *"order by how many lines each order joined to"* — has **no declarable
name to reference**. Inventing syntax for it (`sort by joined.Code`, `sort by count(lines)`) means
PQL must **synthesise a comparer over a generated join-row type at compile time** — materially larger
than mapping `sort by X` onto an existing named field. The mid-chain sort path in codegen
(`:8017`, `:8120`) explicitly sorts by `IComparer<TDocument>` **before** chaining the join, confirming
the surface only ever orders the left side.

> **This is the one genuine expressiveness gap.**

**(d)-2 — S99: an ad-hoc `IComparer<T>` on a cache that *is* attributed.**

Sites: `PreparedGeneratedDifferentialTests.cs:19-24` (`ByFeaturedOrderThenId`, used `:139`, `:147`);
`PreparedGeneratedFrozenJoinTests.cs:119-121` (`AuthorByIdDesc`, used `:128`);
`PreparedGeneratedJoinTests.cs:23-25` (used `:121`, `:125`, `:128`).

These run on real `[DataCache]` models with a full generated `With*` surface, bind `Pipeline`, and
the **only** thing PQL cannot say is the ordering — because the ordering lives in a `Compare` method
**body** rather than in a **declaration**. The fix is one attribute on the model:

```csharp
[DataCacheSort("ByFeaturedOrderThenId", "FeaturedOrder:1", "Id:1")]
```

after which `sort by ByFeaturedOrderThenId` lowers directly to the emitted `…Comparer` field.

> **This is a missing declaration, not a missing grammar.**

The corpus proves the mechanism works end to end: `ProductListing` declares two sorts
(`tests/Prague.Generated.Tests/Fixtures/Entities/ProductListing.cs:10-11`) and four prepared call
sites already consume the generated field **by name** (GEN:FrozenComposite:176,182,183,184;
GEN:MatchOptionalRange:114; GEN:Branch:120). S98 and S99 are the **same shape**; the only difference
is whether somebody wrote the attribute.

*Caveat, recorded honestly:* `[DataCacheSort]` takes `"Prop:±1"` specs, so it expresses multi-key
**property** ordering and nothing else. A comparer with genuinely non-lexicographic logic (a computed
rank, a null-ordering rule) would not be declarable either. The corpus contains no such comparer —
the closest is `ByFlag` (`FPSB:97`), which ties on purpose to test sort stability and is a plain
single-key ascending order.

### Candidates considered and rejected from (d)

A 2-row bucket is only credible if the reader can see what was thrown out.

| Candidate | Verdict | Reason |
|---|---|---|
| Join filter callbacks — `JoinMany(_lines, _lineByOrder, q => q.Where(l => l.Id % 2 == 0))` (FPJM:302, Pipeline asserted :572) | **not (d)** | A predicate on the joined side is native SQL/KQL vocabulary, and the generated surface already lowers exactly this — `JoinWithCfkTag(q => q.WithName("fantasy"))` at `tests/Prague.Generated.Tests/Join/JoinCollectionFkTests.cs:92` is nested narrowing through generated `With*`. Calling it (d) would, for consistency, make every top-level `Where(v => v.Flag)` a (d) too, which makes the exercise vacuous. |
| `If`/`Match` condition lambdas — `static a => a.min > 0` | **not (d)** | They read only from `TArgs`, i.e. ordinary boolean parameter expressions (`if (@min > 0)`), not opaque predicates over `TValue`. |
| `FrozenOptions` flags — `Pipeline=false`, `PreserveEagerOrder`, `IndexSideProbes`, `AdaptiveFilterOrdering`, `FuseFilters`, `CapacityHints` (S86-S90, S57) | **→ (b)** | Build-time knobs, not query text; a compiler takes them as options or attributes the way a query hint is taken. They *cause* `Replay`, which is what (b) is for. |
| Plan / `Explain()` assertions as the observable (Match:510-544, OptR:229-245) | **not a shape** | Tests of the planner's own output. The chains themselves are S47 and S8, both (a). |
| `ListInProjected`, caller-supplied global index (S6, S12, S23) | **→ (c)** | No `With*` twin exists, so there is nothing to lower through. Unreachability, not a naming failure. |
| Derived join keys (S68) | **→ (c)** | Least confident call in the table; see above. |

### Alphabet coverage — what appears nowhere

- **`NarrowerKind`: all 15 members appear. No gaps.** Rarest witnesses: `IfElse` — 3 sites only
  (FQT:228, FPC:510, If:262); `ListInProjected` — 12 sites, **all on raw caches**, no generated twin
  in existence; `LastUpdatedBetween` (Narr:230, If:422); `UniqueIn` (Narr:61, If:381).
- **Resolver verbs: the alphabet is 10, not 6.** Corpus counts — `JoinOne` 352, `SortBounded` 329,
  `InnerJoinOne` 164, `Sort` 147, `JoinMany` 116, `InnerJoinMany` 60, `JoinManyCollectionForward` 8,
  `InnerJoinManyCollectionForward` 6, `InnerJoinManyCollection` 6, `JoinManyCollection` 4. Plus
  generated FK sugar: `JoinWithBook` 19, `JoinWithAuthorProfile` 11, `InnerJoinWithBook` 11,
  `JoinWithM2OAuthor` 8, `InnerJoinWithAuthorProfile` 8, `InnerJoinWithM2OAuthor` 6,
  `JoinWithCfkTag` 4, `JoinWithCfkBook` 3, `InnerJoinWithCfkTag` 3, `InnerJoinWithCfkBook` 3.
  **Nothing is absent** — all four collection-join verbs are exercised (FPJM:575, :576, :306, :308).
  **A grammar scoped to the handoff's six verbs would under-serve the corpus by four.**
- **Terminals: all five appear.** Two shapes exercise all five in one block (Or:146-153, Sort:85-90).
- **Executors: all three asserted, unevenly.** `PointLookup` is asserted at 13 sites, **every one on
  a raw cache** (FQT:142-148, :193, :547; FQS2:85; FPT:716-717;
  `PreparedQueryAllocationTests.cs:265`). It is asserted **nowhere** in
  `tests/Prague.Generated.Tests/Prepared/`, and the one generated-surface shape that would bind it
  (S91, `WithId(v)`) is `Build()`-only.

> **That last point is a real coverage gap in Prague's own tests, not just in this audit:** the
> single executor a PQL point query would most often compile to has **zero** end-to-end assertions
> on the attributed path. Worth an issue independently of whether PQL ever ships.

### Corpus verdict

Bucket (d) is **2 shapes of 101 (2.0%)**, and only **one** of the two (S82) is a genuine
expressiveness gap rather than a missing declaration. By **call-site weight** (d) looks much bigger —
most of the 476 `Sort`/`SortBounded` sites pass ad-hoc comparers — but that is the same single cause
repeated, and it is a cause PQL can remove by requiring a declared sort name.

> A KQL-shaped pipeline grammar over the `NarrowerKind` alphabet **covers this corpus, on one
> condition** — that PQL requires **named, declared sorts**, and accepts that **post-join ordering
> keyed on right-side data is out of scope in v1**.

---

## F16. The sorted-join asymmetry — four FK families of eight

Found while the design doc was being written, and it is the sharpest single cost in the whole design.

`Sort`/`SortBounded` produces a `SortedQuery<TInner>`-wrapped discriminator, and a `JoinWith*` can only
follow it if the generator emitted a **parallel overload** accepting that wrapper. The generator's own
comment says what the parallel covers (`CacheGenerator.cs:6640-6643`):

> *"Parallel string-set: same shape but with `TDiscriminator` → `SortedQuery<TInner>`. Used to emit
> Sort→JoinWith overloads after each regular reverse-JoinWith. (Cache is fetched via `disc.Inner.Cache`;
> only the outer JoinWith family gets a Sorted variant — `InnerJoinWith` is intentionally skipped.)"*

Exactly **four** sites build a `joinReturnType*Sorted`, out of eight FK families:

| FK family | Verb | Sorted parallel? |
|---|---|---|
| reverse collection (M:N) | `JoinManyCollection` | **yes** — `:6714` |
| reverse one-to-many | `JoinMany` | **yes** — `:6762` |
| reverse one-to-one | `JoinOne` | **yes** — `:6837` |
| forward collection | `JoinManyCollectionForward` | **yes** — `:6907` |
| nested | `JoinMany` (continuation) | **no** — `:6789`, `:6794` |
| **forward many-to-one** | `JoinOne` | **no** — `:6965`, `:6972` |
| selector-form one-to-one on PK | `JoinOne` | **no** — `:7037`, `:7044` |
| selector-form general | `JoinOne` | **no** — `:7087`, `:7094` |

**The split is not forward-vs-reverse** — forward collection has one. It is that the four families
building an explicit `joinReturnType*Sorted` get a parallel, and the four routed only through
`EmitFkJoinWithOverloads` (`:6789`, `:6965`, `:7037`, `:7087`) do not. `InnerJoinWith*` never gets one
in any family, and neither do the filtered flavours — the parallel is bound to
`NoFilter<{nonExecBuilder}>` at all four sites.

`PreparedGeneratedFrozenJoinTests.cs:123-124` states it directly:

> *"The forward and the inner `JoinWith` bind on `ICacheCarrier`, which a `SortedQuery` discriminator
> does not carry (eager too); the reverse outer one-to-one join after a `SortBounded` is the generated
> bounded shape."*

**Why it bites.** Production **shape B** is `UpdatedAfter` + two lanes → `SortBounded` → two chained
`JoinOne`s. The raw chain gets away with it because hand-written `JoinOne(index, cache)` binds on the
prepared discriminator directly. Restated on an attributed cache, those become **forward many-to-one**
`JoinWith*` calls — the family with no sorted parallel. **Shape B does not lower in its production
clause order**; PQL must move the sort after the joins, which is a different plan.

This is a codegen asymmetry PQL inherits and cannot paper over. It became `CACHE084` in the research
doc's refusal matrix. **A candidate generator fix, filed not fixed** — if it is fixed, shape B lowers as
written and the row leaves the matrix.

---

# Part 2 — PQL syntax samples

**These are sketches for judgement, not a locked grammar.** The normative grammar, the full
`NarrowerKind` table and the refusal matrix live in `2026-09-12-pql-dsl-research.md`.

Every sample below is written against a **real, attributed fixture** —
`tests/Prague.Generated.Tests/Fixtures/Entities/ProductListing.cs` — so the generated names are
observable rather than invented:

```csharp
[DataCache]
[DataCacheSort("ByDateAsc",  "ReleaseDate:1",  "Id:1")]
[DataCacheSort("ByDateDesc", "ReleaseDate:-1", "Id:-1")]
public partial class ProductListing {
    [DataCacheKey]                                 public string CompositeId { get; set; }
    [DataCacheIndex(DataCacheIndexType.Unique)]    public long Id { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)]      public SalesChannel ChannelId { get; set; }
    [DataCacheIndex(DataCacheIndexType.Range)]     public DateTime ReleaseDate { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)]      public long DepartmentId { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)]      public long CategoryId { get; set; }
    [DataCacheIndex(DataCacheIndexType.Many)]      public long BrandId { get; set; }
    // … IsFeatured / IsPublished / HasDiscount value indexes, FeaturedOrder range
}
```

## The hosting shape

Attribute + partial method, the `[GeneratedRegex]` / `[LibraryImport]` shape. Because every
`[DataCache]` user type is already `partial` and the cache class is generated, the natural home is a
user-written partial **of the cache class** — then `this` is the cache and the frozen query is a
lazily-initialised instance field, built once.

```csharp
public partial class ProductListingCache {
    [Pql("""
         from ProductListing
         | where ChannelId == @channel and DepartmentId == @department
         | sort by ByDateDesc
         """)]
    public partial QueryResults<ProductListing> ByDepartment(SalesChannel channel, long department);
}
```

The method name supplies what sqlc's `-- name:` supplies. The return type supplies `:one` / `:many`
and the pooled/allocating choice. Diagnostics land on an ordinary Roslyn `Location` — the reason this
beats a separate `.pql` file, which would need `Location.Create(path, TextSpan, LinePositionSpan)`, a
pattern appearing nowhere in this repo.

**`skip` / `take` are execution-time (F10)**, so they are method parameters, not part of `@args`:

```csharp
public partial QueryResults<ProductListing> ByDepartment(SalesChannel channel, long department,
                                                         int skip = 0, int take = int.MaxValue);
```

## Sample 1 — point lookup

```
from ProductListing
| where Id == @id
```
```csharp
// emitted
_cache.Prepare<long>()
      .WithId(static a => a)
      .BuildFrozen();
// → Executor: PointLookup
```

The whole `Prepare<int, ProductListing, long>()` / `UseIndex(_idIndex, …)` ceremony collapses to one
line, and `TArgs` is a bare `long` — legal because `TArgs` is unconstrained (F10), and witnessed in
the corpus at `PreparedGeneratedFrozenJoinTests.cs:128` (`_authors.Prepare<int>()`).

## Sample 2 — several lanes, with a declared sort

```
from ProductListing
| where ChannelId == @channel
    and DepartmentId == @department
    and BrandId in @brands
| sort by ByDateDesc
```
```csharp
_cache.Prepare<(SalesChannel channel, long department, ReadOnlyMemory<long> brands)>()
      .WithChannelId(static a => a.channel)
      .WithDepartmentId(static a => a.department)
      .WithBrandId(static a => a.brands)
      .Sort(ProductListingCache.ByDateDescComparer)
      .BuildFrozen();
// → Pipeline
```

`in @brands` picks the `Func<TArgs,ReadOnlyMemory<T>>` overload (`CacheGenerator.cs:7188` →
`ListIndexInArg`). `ByDateDesc` resolves to the generated `…Comparer` field
(`CacheGenerator.cs:1679`), which is exactly the "**named, declared sorts**" condition from F15.

## Sample 3 — `sort bounded by` is a different keyword, never inferred

```
| sort bounded by ByDateDesc
```
```csharp
.SortBounded(ProductListingCache.ByDateDescComparer)
```

Two keywords because the choice is **cost plus honouring declared intent** — *not* tie behaviour
(F14; the handoff's reason is stale and `context/query.md` contradicts itself on it). PQL must never
pick for you.

## Sample 4 — range, including the optional-bounds form

```
from ProductListing
| where ReleaseDate >= @from and ReleaseDate < @to
```
```csharp
.WithReleaseDate(static (rb, a) => rb.Gte(a.from).Lt(a.to))   // RangeArgNarrower
```

Optional bounds — **either end may be absent at execution**, which the prepared surface supports
natively and the eager surface does not (F6):

```
| where ReleaseDate between @from? and @to?
```
```csharp
.WithReleaseDate(static a => a.from, static a => a.to)        // RangeOptionalArgNarrower
// both-null is a recorded no-op — Narrowers.Range.cs:167-174
```

This is the **one** place optionality does not need `If` (F7).

## Sample 5 — optional predicates lower to `If`

Everywhere else, an optional predicate **must** become `If`, because the prepared parameter is
`notnull` and there is simply no null to pass (F7 — the handoff's stated reason is backwards):

```
from ProductListing
| where IsPublished
| if @department is set { where DepartmentId == @department }
| if @featuredOnly { where IsFeatured }
```
```csharp
_cache.Prepare<(long? department, bool featuredOnly)>()
      .WithIsPublished(true)
      .If(static a => a.department.HasValue,
          b => b.WithDepartmentId(static a => a.department!.Value))
      .If(static a => a.featuredOnly, b => b.WithIsFeatured(true))
      .BuildFrozen();
// → Pipeline; arm selection happens at execution, costs nothing
```

## Sample 6 — `or`

```
from ProductListing
| where ChannelId == @channel
| where DepartmentId == @a or DepartmentId == @b
```
```csharp
.WithChannelId(static a => a.channel)
.Or(b => b.WithDepartmentId(static a => a.a),
    b => b.WithDepartmentId(static a => a.b))
```

Three type-state rules come free from the emitted C# (F11): **no `where` filter inside an `or`
branch** (branches are `PreparedNarrowOnly<TCache>`), a marking narrower must be **first** in a
branch, and more than 8 flattened branches is refused (`MaxOrBranches`, F14). PQL diagnoses the last
two itself; the first the C# compiler catches even if PQL misses it.

## Sample 7 — `match`, with a mandatory `default`

```
from ProductListing
| where ChannelId == @channel
| match @pick {
    case Department -> where DepartmentId == @id
    case Brand      -> where BrandId == @id and IsPublished
    case Featured   -> where FeaturedOrder >= @min
    default         -> where HasDiscount
  }
```
```csharp
_cache.Prepare<(Pick pick, long id, int min)>()
      .WithChannelId(static a => a.channel)
      .Match(static a => a.pick, m => m
          .Case(Pick.Department, b => b.WithDepartmentId(static a => a.id))
          .Case(Pick.Brand,      b => b.WithBrandId(static a => a.id).Where(static p => p.IsPublished))
          .Case(Pick.Featured,   b => b.WithFeaturedOrder(static a => (int?)a.min, static a => null))
          .Default(b => b.WithHasDiscount(true)))
      .BuildFrozen();
```

(Modelled on the real chain at `PreparedGeneratedFrozenCompositeTests.cs:143-150`, which asserts
`Pipeline`.) **Omitting `default` need not be a PQL diagnostic at all** — the emitted C# would fail
to compile with CS0315, because `Match` requires `IClosedMatchArms<…>` (F11). That is the lowering
choice paying for itself, and it landed in `fa60800` *during* this research.

## Sample 8 — joins, with the arg restriction visible

```
from Author
| where Id == @id
| sort bounded by ByIdDesc
| join one AuthorProfile
```
```csharp
_authors.Prepare<int>()
        .WithId(static a => a)
        .SortBounded(AuthorCache.ByIdDescComparer)
        .JoinWithAuthorProfile()
        .BuildFrozen();
// → Pipeline, sort: bounded, fused: 1
```

(The real chain, minus the ad-hoc comparer, is `PreparedGeneratedFrozenJoinTests.cs:128`.)

This one compiles **because `AuthorProfile` is a reverse one-to-one FK**, one of the four families with
a sorted parallel (F16). The same text with a *forward many-to-one* join after the sort has no generated
overload and is refused — that is `CACHE084`, and it is why production shape B has to be reordered.

A filter on the joined side is allowed — but **it cannot read `@params`** (F9):

```
| join many Tags { where Name == "fantasy" }      -- OK, build-time constant
| join many Tags { where Name == @tag }           -- PQL0xx: a join-side filter cannot read
                                                  --         execution arguments
```

because the callback receives an **eager** paired builder (`CacheQueryBuilder.cs:944`) and the
filter's own `TArg` is captured at build time, not execution time. This is the kind of rule that
*must* be a PQL diagnostic, since the C# would compile and silently freeze the wrong value.

## Sample 9 — the terminals

```
| count                              → Count(args)
                                     → declared `int`
(default)                            → Execute(args, skip, take)
                                     → declared `QueryResults<T>`
| pooled                             → ExecutePooled(…)          — caller must Dispose
| cloned                             → ExecuteCloned(…)
| pooled cloned                      → ExecutePooledCloned(…)
```

All five are `FrozenQuery<TArgs,TResult>` members (`FrozenQuery.cs:63`, `:66`, `:69`, `:72`, `:75`).
The pooled/allocating choice **has to be visible in the syntax** because pooled results must be
`Dispose()`d — a DSL that hid it would be handing out leaks. An `| one` sugar is possible (lower to
`take 1` plus an indexer) but it is sugar, not a sixth terminal.

## Sample 10 — what refusal looks like

The scope rule is *"whatever `BuildFrozen()` binds"*, so anything that would fall to `Replay` is a
compile error with a reason, not a slow query:

```
from ProductListing
| where ProductName startswith "z"
```
```
PQL0xx: this query has no index seed, so it would replay the whole cache.
        `where ProductName startswith "z"` is a filter; add at least one indexed
        predicate, or use Query() directly if a full scan is intended.
```

Same treatment for: more than 8 `or` branches; a marking narrower (`range` / key-set / last-updated)
that is not first in its `or` branch; a `where` filter inside an `or` branch; a join with no seed;
a sort with no seed. All 21 shapes in F15's table get a row in the central `CACHE060+` table.

---

## What is NOT decided here

- The normative grammar, the 15-row `NarrowerKind` table, the full refusal matrix with final
  diagnostic IDs and text, and **the verdict** — all in `2026-09-12-pql-dsl-research.md`.
- Whether PQL ships at all. This is research; implementation is a separate, later decision.
- The dead `IndexName` lookup (F3) is **reported, not fixed**, and should be filed as its own issue.
- The `PointLookup` test-coverage gap (F15) is likewise a separate issue.

# Prague.Codegen — source generation & T4

> **Read when:** changing the source generator, T4 templates, attributes, or anything that emits `*.generated.cs` / `XxxCache` partials.

Two generation mechanisms. **Never hand-edit `*.generated.cs`** — change the generator or the template and rebuild. After touching `Prague.Codegen` / T4, run `Prague.Generated.Tests`.

## Roslyn source generator (`CacheGenerator.cs`)

Driven by `[DataCache]` POCOs. For each it emits a `partial XxxCache` with: index storage + `Cache.AddXxxIndex(...)` calls, the fluent `Query()` API, the `Cache` property, FK join-convenience methods, and the query-string API (`TryApplyParam`, `ApplyFilter`, `StringQueryInternal`). Every `[DataCache]` type must be `partial`.

## Attributes → codegen (public surface, `src/Prague.Attributes/`)

| Attribute | Effect |
|-----------|--------|
| `[DataCache]` | Marks a POCO as a cache item; emits its `XxxCache`. |
| `[DataCacheKey]` | Primary key property. |
| `[DataCacheIndex(type, Symmetric=)]` | Secondary index — impl + semantics in [`indexes.md`](indexes.md). |
| `[DataCacheForeignKey<T>(DataCacheJoinType)]` | FK → emits `JoinWith{T}` / `InnerJoinWith{T}` (`OneToOne` / `OneToMany`). |
| `[DataCacheValueIndex]` / `[DataCacheNoValueIndex]` / `[DataCacheHasValueIndex]` / `[DataCacheHasNotValueIndex]` | Key-set indices — see [`indexes.md`](indexes.md). |
| `[DataCacheGlobalLastUpdateIndex]` | Group key for a global LastUpdated index. |
| `[DataCacheSort]` | Custom sort comparer. |
| `[DataCacheIgnoreEquality]` | Excludes a property from equality / hash (drives conditional-update change detection). |
| `[DataCacheTopic]` | Generates a cache topic constant. |
| `[DataCacheFrom]` / `[DataCacheFromTimestamp]` | Copy properties from a source type. |
| `[DataCacheHeader]` | Maps a property to a Kafka header (header SerDe in [`kafka-serde.md`](kafka-serde.md)). |
| `[DataCacheJsonSerializables]` | Marks a partial `JsonSerializerContext` for JSON registrations. |

## FK join-convenience methods

`[DataCacheForeignKey<T>]` emits `.JoinWith{T}()` / `.InnerJoinWith{T}()` for all five FK shapes (reverse Many, reverse OneToOne, forward ManyToOne raw, selector OneToOne-on-PK, selector ManyToOne/OneToOne-non-PK), outer + inner, at every chain level. Three overloads each: no-filter, filter lambda, filter + user-state `TArg` — forwarding to the lower-level join extensions and their `NoFilter`/`JoinFilter`/`JoinFilterWithArg` strategy structs ([`joins.md`](joins.md)). `Sort → JoinWith` stays no-filter-only.

Selector-form FK (`DataCacheForeignKey<T, TSelector>`): the auto-index is keyed by the FK property's **raw type** (selector applied at *join* time, not index-build), so `With{FkProperty}(rawType)` is usable as a plain standalone filter.

## Prepared queries (generated surface)

Next to the eager `Query()` the generator emits, per `[DataCache]` wrapper, the build-once / execute-on-demand twin (`GenerateQueryMethods` + `GeneratePreparedBuilderExtensions` in `CacheGenerator.cs`; engine in [`query.md`](query.md) → "Prepared queries"):

- **Entry points** on the wrapper: `Prepare()` (`TArgs = NoArgs`) and `Prepare<TArgs>()`, both `Cache.Prepare<XxxCache, TKey, TValue, TArgs>(this)` — the Core carrier overload. The discriminator is `PreparedQueryDiscriminator<XxxCache>`: it carries the **wrapper**, not the raw `InMemoryDataCache`, while the recorder (`PreparedNarrowers<TKey,TValue,TArgs,TChain>`) carries the raw cache.
- **Extension class** `XxxCachePreparedQueryExtensions`, receiver `in CacheQueryBuilderCombined<TDiscriminator, PreparedNarrowers<TKey,TValue,TArgs,TChain>, TKey, TValue, TResolverChain, TResult>`. Each overload forwards to the hand-written prepared `UseIndex` over the same wrapper index field the eager `WithXxx` reads, so the return type grows the chain by one `NarrowerLink<TChain, {Narrower}, …>`. Emitted per index kind:
  - Unique / Many / FK / collection-Many (`.Forward`) / key (`WithKey`, `With{KeyProp}` over `Cache.KeyIndex`): `WithXxx(T value)`, `WithXxx(Func<TArgs,T>)`, `WithXxx(ReadOnlyMemory<T>)`, `WithXxx(T[])`, `WithXxx(Func<TArgs, ReadOnlyMemory<T>>)` → `UniqueIndexEq/EqArg/In/InArg` or `ListIndexEq/EqArg/In/InArg`. The eager `T? value` (null = skip), `ReadOnlySpan<T>` and `List<T>` forms have no twin: a recorder cannot skip a link at build time by value, store a span, or borrow a list.
  - Range: `WithXxx(Func<RangeQueryBuilder<T>, TRb>)` and `WithXxx(Func<RangeQueryBuilder<T>, TArgs, TRb>)` → `RangeNarrower` / `RangeArgNarrower`; optional bounds `WithXxx(Func<TArgs,T?> from, Func<TArgs,T?> to, bool fromInclusive = true, bool toInclusive = true)` → `RangeOptionalArgNarrower` (value-type key) or `RangeOptionalRefArgNarrower` (reference-type key; the generator picks by `IsValueType` because the emitted return type names the narrower) and `WithXxx(T? from, T? to, …)` → `RangeOptionalNarrower`. A `null` bound is the open side; both `null` records a no-op step.
  - Key-set (`[DataCacheHasValueIndex]` → `WithXxx()`, `[DataCacheHasNotValueIndex]` → `WithoutXxx()`, value / no-value indexes): parameterless → `KeySetNarrower`.
  - Global last-updated on the key: `UpdatedAfter(long | DateTime | DateTimeOffset)`, `UpdatedAfter(after, untilInclusive)` per time type, `UpdatedAfter(Func<TArgs,long>)`, `UpdatedAfter(Func<TArgs,long>, Func<TArgs,long>)` → `GlobalLastUpdatedAfter/Between/AfterArg/BetweenArg`. The eager `out long max` forms are execution-time outputs and are not emitted.
- **Carrier scoping rule** (same as eager): every prepared overload is constrained `TDiscriminator : struct, IIndexNarrower, ICacheCarrier<XxxCache>`, so it binds only on this wrapper's builders — a `WithXxx` of cache A on cache B's prepared builder is CS0315 — and reaches the index through `GetDiscriminator(ref …).Cache.XxxIndex`.
- **Branch rule**: `Or` / `If` / `IfElse` / `Match` branch and arm builders are discriminated by `PreparedNarrowOnly<XxxCache>` / `PreparedConditionalBranch<XxxCache>` carrying the **same** wrapper, so every generated `WithXxx` binds inside a branch or arm (bound and parameterized). `Match` / `If` / `Or` themselves are hand-written generic extensions that already bind on generated builders — nothing is emitted for them. `Where` binds in `If` branches and `Match` arms only; joins, sort and `Build()` never bind inside a branch (CS0315, exactly as eager `Or`).
- **FK `JoinWith{T}` / `InnerJoinWith{T}` need no twin.** The eager emission binds on `TExecutor : ICandidatesExecutor` (the recorder implements it) and `TDiscriminator : IBaseJoinable, ICacheCarrier<XxxCache>` (the top-level prepared discriminator implements both), including the `Sort → JoinWith` family over `SortedQuery<TInner>`. `Prepare().WithXxx(..).JoinWith{T}().Build()` and `Prepare().SortBounded(cmp).JoinWith{T}().Build()` compile as-is.
- Query-string API (`TryApplyParam` / `StringQueryInternal`) is eager-only.

Tests: `tests/Prague.Generated.Tests/Prepared/` (differential vs eager per overload family, joins, branches, `Match` + optional range, allocation).

## T4 templates (`Prague.Core/*.tt`)

`JoinQueryBuilders.tt` and `JoinResults.tt` emit the **combinatorial** join builders and result accessors. Regenerate via the template, never edit the output. Load-bearing emitted members: per-accessor `RetainNonNullSlots<TKey,TRightValue>(ref candidates)` + `GetKeys` (chained-inner correctness); `Right{N}NonNullFilter` structs. Phase 1B region emits `CacheQueryBuilderCombinedJoinNewLevel{N}Extensions` (chained joins) for PK-to-PK / RightUnique / LeftUnique families.

## Tests

`tests/Prague.Generated.Tests` — NUnit, exercises the **generated** layer using `[DataCache]` fixtures under `Fixtures/` (Entities, Enums). `Join/` mirrors `Prague.Core.Tests/Join/` but through codegen.

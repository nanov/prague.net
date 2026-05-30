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

## T4 templates (`Prague.Core/*.tt`)

`JoinQueryBuilders.tt` and `JoinResults.tt` emit the **combinatorial** join builders and result accessors. Regenerate via the template, never edit the output. Load-bearing emitted members: per-accessor `RetainNonNullSlots<TKey,TRightValue>(ref candidates)` + `GetKeys` (chained-inner correctness); `Right{N}NonNullFilter` structs. Phase 1B region emits `CacheQueryBuilderCombinedJoinNewLevel{N}Extensions` (chained joins) for PK-to-PK / RightUnique / LeftUnique families.

## Tests

`tests/Prague.Generated.Tests` — NUnit, exercises the **generated** layer using `[DataCache]` fixtures under `Fixtures/` (Entities, Enums). `Join/` mirrors `Prague.Core.Tests/Join/` but through codegen.

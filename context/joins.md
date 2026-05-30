# Joins

> **Read when:** working on `JoinWith`/`JoinOne`/`JoinMany`, resolver families, the paired core, inner/chained joins, or join leak-safety.

Compile-time-safe joins over the query builder. Spans Core (resolvers, paired core) and Generated (T4 builders, FK convenience). Builds on [`query.md`](query.md) (builder/discriminators), [`indexes.md`](indexes.md) (which index drives which family), [`collections.md`](collections.md) (`ValueSet`/`JoinedKeyPair`/`PooledSet`). Naming note: legacy `JoinOneNew`/`JoinMany…New` lost the `New` suffix when the originals were retired — git history & some specs still say `…New`.

## Public surface

- **FK convenience (preferred):** `.JoinWith{T}()` / `.InnerJoinWith{T}()` — emitted from `[DataCacheForeignKey<T>]` (see `generated.md`). Three overloads each: no-filter, filter, filter + `TArg`.
- **Lower-level:** `.JoinOne(...)`, `.InnerJoinOne(...)` (1:1 / outer + inner), `.JoinMany(...)`, `.InnerJoinMany(...)` (1:N). **Pass the cache wrapper directly — no `.Query()` at the call site.** Extension parameter is the `IDataCache<…>` interface (so C# can infer `TRightValue`), cast to the concrete cache inside.
- Pooled join results: zero-allocation, caller `Dispose()`s. `result.Left`, `result.Right1` (1:1), `result.Right2` (1:N list), etc.

## Strategy structs (zero-cost, JIT-devirtualized per closed generic)

**Filter** — `IJoinOneFilter<TBuilder>.Apply(q)`:
- `NoFilter<TBuilder>` — identity, `Apply` fully elided.
- `JoinOneFilter<TBuilder>` — wraps `Func<TBuilder,TBuilder>`.
- `JoinOneFilterWithArg<TBuilder,TArg>` — wraps func + arg; **declare the lambda `static` for zero-alloc capture.**

**Key selector** — `IKeySelector<TIn,TOut>` (static-abstract `IsIdentity`, JIT-folded):
- `IdentitySelector<T>` — identity path uses `Unsafe.As<TIn,TOut>` reinterpret; `Select` elided. Default everywhere.
- `KeySelector<TIn,TOut>` — wraps `Func<TIn,TOut>`. Enables cross-key-type joins (e.g. `int → long`) with **no new resolver types**.
- `KeySelectorWithArg<TIn,TArg,TOut>` — func + arg; `static` lambda for zero-alloc.

Filter callbacks receive a `NonExecutableQuery<TRightCache>` discriminator (`WithXxx`/`UseIndex`/`Or` callable; `Execute*` hidden). `AsNonExecutable()` (internal extension) swaps `ExecutableQuery<TCache>` → `NonExecutableQuery<TCache>` preserving other generics — call `Cache.Query().AsNonExecutable()` in any resolver needing a non-exec builder.

## The four JoinOne resolver families

| Family | Trigger | Resolver | Notes |
|--------|---------|----------|-------|
| **PK-to-PK** | `TLeftKey == TRightKey` | `JoinOneResolver` | No index step; stays **unpaired**. Optional key selector for cross-key joins. |
| **Right-unique-index** (FK-on-right) | `[DataCacheIndex(Unique)]` on right FK → `CacheUniqueIndex<TRightKey,TRightValue,TLeftKey>` | `JoinOneRightUniqueIndexResolver` | e.g. `Book → BookInfo` where `BookInfo.BookId` is unique. |
| **Left-unique-index** (FK-on-left, 1:1) | `[DataCacheIndex(Unique, Symmetric=true)]` on left → `CacheSymmetricUniqueIndex` (`.Reverse` supports `IntersectValues`) | `JoinOneLeftUniqueIndexResolver` | Bijective, no fan-out. e.g. `Author.BookId`. |
| **Left-symmetric-index** | `[DataCacheIndex(Many, Symmetric=true)]` on left → `CacheSymmetricKeyValueListIndex` | `JoinOneLeftSymResolver` | Index-driven; fans out (many lefts share one index value). |

Each family has identity + key-selector overloads × {no-filter, filter, filter+arg}; identity overloads pass `IdentitySelector` (zero cost).

## Paired core (execution engine)

- `PairedCacheQueryBuilderCoreCombined<TLeft,TKey,TValue>` stores candidates as `ValueSet<JoinedKeyPair<TLeft,TKey>>` and slots in as `TExecutor` inside `CacheQueryBuilderCombined`. `JoinedKeyPair.Equals/GetHashCode` consider only `.Key` — intersection is by key alone.
- `ExecutePaired<TContainer>(ref container)` calls `_dataCache.TryGet<TLeft,TContainer>(ref container, ref _candidates, _filter)` — native paired bulk-read, no projection-to-unpaired, disposes candidates in finally.
- `UseIndex` on the paired core is intersect-only (pairs added once at promotion). Strategies per index type avoid temp `ValueSet`: `IncrementalIntersecter` (KeyValue), direct `IntersectWith` against the index's `PooledSet` (KeyValueList), `IntersectPairedViaTemp` only for Range/LastUpdated B-tree walks.
- **Filter executor-agnosticism:** `Where`/`UseIndex`/`WithXxx`/`Or` constrain on the **discriminator** (`IBaseFilterable`, `ICacheCarrier<TCache>`), never the executor type — so filter callbacks are unchanged whether a resolver runs paired or unpaired.
- **LeftSym borrow-the-set:** `TLeft = LeftKeySetView<TLeftKey>` wrapping a *borrowed* reference to the index's internal `PooledSet<TLeftKey>` (no copy, no side-map). `OuterFanOutContainer` / `InnerFanOutContainer` reinterpret it via `Unsafe.As` and iterate; inner additionally filters via `_candidates.Contains(lk)`.

## Inner joins — unified post-walk

`InnerJoinOne` at level 0 constructs the resolver with `isInner: true`. `UnsafeExecuteIndexedInner` flow (PK-to-PK, RightUnique, LeftUnique — **LeftSym deferred**):
1. seed `ValueSet<JoinedKeyPair>` from candidates (`PrepareIndexedInner` = `_ = leftQuery.GetCandidates<TLeftKey>()` to trigger auto-populate-from-leftCache — note: direct `.Candidates.Count` access *bypasses* auto-populate, which was the historic PK-to-PK bug);
2. `Filter.Apply` (configures predicate, narrows candidates via `UseIndex`);
3. `ExecutePaired(ref container)` — `container.Add` for matches only;
4. `accessor.RetainNonNullSlots<TLeftKey,TRightValue>(ref candidates)` — drops `_results` entries whose Right_N slot is null/default (miss or predicate-reject) and narrows candidates. A slot is non-null **IFF** this resolver wrote it via `container.Add`; the `is null` check covers ref types and `Nullable<T>` uniformly.

This replaced the old miss-callback infra (`IInnerJoinContainer`, `UnsafeInnerResolverContainer`, etc., ~550 lines removed): 1 pair walk instead of 2, sequential filter walk instead of per-miss hash-removes.

## Chained joins

`.JoinWith{A}().JoinWith{B}()` (and `JoinOne` equivalents) — T4 Phase 1B emits chained levels for **PK-to-PK / RightUnique / LeftUnique** identity families (LeftSym chained deferred). Correctness uses the same post-walk `RetainNonNullSlots`.

## Leak-safety (`handedOff` guard) — all 4 families

Every `ExecuteReverse` / `UnsafeExecuteIndexedInner` wraps its `ValueSet<JoinedKeyPair<…>>` in `try { … } finally { if (!handedOff && pairs.IsInitlized) pairs.Dispose(); }`. **`handedOff = true` is set immediately before `ExecutePaired`, *after* `Filter.Apply`** — this position is load-bearing: `Filter.Apply` (user lambda) sits between paired-core construction and `ExecutePaired` and can throw; flipping earlier silently leaks the rented array. Enforces exactly-one-Dispose (a double `ArrayPool.Return` is swallowed by `ValueSet.Dispose` but corrupts the pool).

## Key files & tests

- `src/Prague.Core/QueryBuilders/JoinOneResolver.cs`, `…/CacheQueryBuilder.JoinOne.Extensions.cs`, `…/JoinManyResolver.cs`, `…/CacheQueryBuilder.JoinMany.Extensions.cs`, `CacheQueryBuilder.cs` (paired core + `AsNonExecutable`).
- T4: `JoinQueryBuilders.tt`, `JoinResults.tt`.
- Tests: `tests/Prague.Core.Tests/Join/` (raw POCO, no codegen — `JoinOneCoreTests`, `…RightUniqueIndexCoreTests`, `…LeftUniqueIndexCoreTests`, `…SymIndexCoreTests`, `…KeySelectorCoreTests`, `…ChainedCoreTests`); `Prague.Generated.Tests.Join` (through codegen).

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
- **JoinOne LeftSym borrow-the-set (`JoinOneLeftSymResolver` only):** `TLeft = LeftKeySetView<TLeftKey>` wrapping a *borrowed* reference to the index's internal `PooledSet<TLeftKey>` (no copy, no side-map). `OuterFanOutContainer` / `InnerFanOutContainer` reinterpret it via `Unsafe.As` and iterate; inner additionally filters via `_candidates.Contains(lk)`. The JoinMany resolvers do NOT do this — they carry plain `TLeftKey` pairs and chain them behind one pair per right in the fan-out (next section).

## JoinMany fan-out

All three JoinMany resolvers (`JoinManyRightListIndexResolver`, `JoinManyLeftSymResolver`, `JoinManyCollectionResolver`) run the paired core with `TLeft = TLeftKey`, so the filter builder type is `PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>` for every JoinMany shape (hand-written extensions, T4 chained levels, codegen `JoinWith` alike). Because `JoinedKeyPair` identity is the right key alone, one pair set cannot hold `(L1, r)` and `(L2, r)`; lefts sharing rights — a LeftSym lookup group, a non-injective key selector folding several groups onto one right bucket, overlapping M:N collection buckets — are handled by the **fan-out** in every JoinMany resolver, RightList included (before it, a non-injective selector or overlapping collection buckets silently starved the second left):

- `JoinManyFanOut<TLeftKey,TRightKey>` (`src/Prague.Core/QueryBuilders/JoinManyFanOut.cs`, internal `ref struct`, used by ALL THREE JoinMany resolvers) — ONE `ValueSet<JoinedKeyPair<…>>` holding one pair per distinct right (identity is the right key; the JoinedKey is the first left that recorded it) plus pooled right → extra-lefts chains keyed by the pair's slot: `ValueSet.AddOrFind` reports the slot, slots are dense while recording, survive growth and are never renumbered by the in-place removals a filter performs. The chains are lazy — rented by the first right a second left records (`HasChains`), so the FK shape writes nothing but the pair. `RecordBucket` is the per-pair hot loop and is split in two: while no chain exists it is one `AddOrFind` per right and nothing else (no chain bookkeeping, no per-right counter — the count is the pair/node delta), and it switches to the chain-maintaining loop only once a right is actually shared. That split is what put the unshared shape back at pre-fan-out parity (#72: a few extra instructions per pair were a 5% regression at 10 000 pairs). The bucket's stored hash is deliberately not reused for the pair — the enumerator's (value, hash) copy is not atomic against a concurrent remove + slot reuse — so the walk hashes each right once. A right the bucket enumerator yields twice for the same left (removed and re-added under the walk) is ignored because that right's latest recorder (the pair's first left or its chain head, `ValueSet.ValueAt`) is the current left.
- **Exactness invariant:** each left's right bucket (`GetValuesUnsafe` — never null, `Empty` sentinel on a miss) is enumerated exactly once; every distinct right recorded for a left becomes one chain node, and `container.Init(left, pairsRecorded)` runs only when `pairsRecorded > 0`. A slot thus reserves exactly the Adds the delivery can make to it, whatever the index writer does concurrently.
- **Execution order:** record every left → `PrepareSharedBuffer()` → `RegisterPooledBuffer` (BEFORE any user code runs) → one paired core over the pair set → `_filter.Apply` once → `MarkPairsHandedOff` → `ExecutePairedJoined` with a `JoinManyFanOut.Delivery`: the store walks the pair set and calls `Add(firstLeft, slot, value)` per surviving right (`TryGetValuesJoined`, container contract `IJoinedSlotResultContainer`; the slot is `ValueSet.Enumerator.CurrentSlot`), and the delivery adds the value to the first left and to every left in `_heads[slot]`'s chain — no second lookup, the right is never hashed again. One store lookup and one predicate evaluation per distinct right, not per pair. When no right is shared at all (`SingleLeftPerRight`: pairs recorded == distinct rights) the resolver skips the `Delivery` and runs the plain `ExecutePaired` — the store adds to the pair's JoinedKey directly — so the unshared shape costs one lookup per pair like a single-set execute (measured: this closes the whole gap to the former rounds engine on 32768 × 8 unshared; the wrapper alone cost 12%).
- **Hand-off discipline:** the core receives a copy of the set at construction; `MarkPairsHandedOff` runs after `_filter.Apply` and before the execute, so a throw inside the user lambda leaves the set to `JoinManyFanOut.Dispose` while a set that reached the core is disposed there exactly once. The hand-off only flips an ownership flag — the fan-out's ~1 KB inline set is not zeroed, and `DistinctRights` keeps reading its `Count`. The delivery holds the chain arrays and the target container by value (a ref field cannot refer to a ref struct); the resolver copies the container back after the execute.
- **Consequences for users:** the join filter lambda runs **once per query** and its `Where` predicate once per distinct right; rights inside a slot come out in pair-set order — the order the distinct rights were first recorded — and a right's chain delivers to its lefts in reverse recording order (results are unordered sets anyway). A right removed and re-added under the walk is delivered once. Per-query cost is O(pairs) recording plus O(distinct rights) lookups: a right shared by thousands of lefts costs one lookup and one chain walk.

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

The joined pipeline itself (`ExecuteCoreJoined` / `CountCoreJoined` / `ExecuteCoreJoinedTop` / `ExecuteCoreJoinedKeyed`) releases a candidate set the indexed-inner phase seeded or auto-populated when a resolver throws before base execution (`ReleaseUnconsumedCandidates`, no-op after a legitimate consume because base execution disposes candidates by ref).

## Concurrency — a query never fails

Caches are written while queries read them (a live Kafka consumer on one side, request handlers on the other); there is no query-time lock and no snapshot isolation, by design. The contract that follows:

- **A query never fails because of a concurrent write.** No exception, no torn buffer.
- **A query makes no consistency promise.** It sees a roughly-snapshot view: a row written mid-query may be included or missed, and different legs of one join may disagree. Stale or short results are correct behaviour; the next query sees the write.

The sharp edge is slot sizing. A `JoinMany` slot is a **fixed partition of one shared buffer** (`Init` reserves, `PrepareSharedBuffer` partitions, `Add` fills) — it cannot grow. A resolver that reserves from a *live* index count and then delivers rows from a *separate* read of the same index can be handed more rows than it reserved: `PooledSet.Count` is a plain counter, while `Add` publishes the slot before bumping it and the enumerator walks a `LastIndex` snapshot, so an enumeration can legitimately yield more than a preceding `Count`.

Two mechanisms hold the contract:

1. **Size from the recorded pairs, not the live count.** Every JoinMany resolver records its `(left, right)` pairs first — through `JoinManyFanOut`, see "JoinMany fan-out" above — and calls `Init` with the number recorded for that left, so the reservation and the delivery come from one enumeration. Filter narrowing and rows removed mid-query can only *reduce* the delivered rows, so the recorded count is an exact upper bound. Nothing reads a live index count to size a slot any more, and nothing consults a live `LeftKeySetView` at delivery time.
2. **`QueryResults.UnsafeAdd` drops rather than throws.** A full slot drops the row, sets `QueryResults{T}.Truncated`, and increments `QueryResultsDiagnostics.DroppedRows`. This is the floor under every resolver, including ones added later. In DEBUG the drop *throws* (`QueryResultsDiagnostics.ThrowOnSlotOverflowInDebug`) so a sizing defect fails a test; the throw is `[Conditional("DEBUG")]` and cannot reach release builds.

`DroppedRows` is expected to be `0`. A non-zero value means a resolver is under-sizing slots — a drop leaves no other trace.

All three JoinMany resolvers size from recorded pairs now. `JoinManyLeftSymResolver` and `JoinManyCollectionResolver` used to reserve from a live index count and then fan a right out over a live `LeftKeySetView` at `Add` time, so one recorded pair could become N `Add` calls with N read later; the fan-out records the extra lefts as it walks instead, which is what makes the count exact for them too. Mechanism 2 stays as the floor under any resolver added later.

## Key files & tests

- `src/Prague.Core/QueryBuilders/JoinOneResolver.cs`, `…/CacheQueryBuilder.JoinOne.Extensions.cs`, `…/JoinManyResolver.cs`, `…/JoinManyCollectionResolver.cs`, `…/JoinManyFanOut.cs`, `…/CacheQueryBuilder.JoinMany.Extensions.cs`, `…/CacheQueryBuilder.JoinManyCollection*.Extensions.cs`, `CacheQueryBuilder.cs` (paired core + `AsNonExecutable`).
- T4: `JoinQueryBuilders.tt`, `JoinResults.tt`.
- Tests: `tests/Prague.Core.Tests/Join/` (raw POCO, no codegen — `JoinOneCoreTests`, `…RightUniqueIndexCoreTests`, `…LeftUniqueIndexCoreTests`, `…SymIndexCoreTests`, `…KeySelectorCoreTests`, `…ChainedCoreTests`); `Prague.Generated.Tests.Join` (through codegen). JoinMany fan-out: `JoinManyFanOutCoreTests` (engine unit tests, shared-right correctness, filter-once-per-query), `JoinManyNonInjectiveSelectorCoreTests` (every resolver delivers a shared bucket to every left), `JoinManyLeftSymCollectionConcurrentMutationTests` / `JoinManyRightListIndexConcurrentMutationTests` (deterministic writer interleavings + 2 s live-writer stress), `Leaks/JoinManyFanOutLeakTests` and `Leaks/QueryJoinLeakTests` (pool balance incl. filter/predicate throwing mid-walk), `Collections/ValueSetEnumeratorSlotTests` (the slot the store walk reports via `Enumerator.CurrentSlot` equals the one `AddOrFind` handed out, across growth and removals).

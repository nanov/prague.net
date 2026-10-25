# CountJoin / InnerCountJoin — implementation plan

**Goal.** A join resolver whose per-left slot is an `int`: the number of rights that `JoinMany` would
have delivered for that left. Available for every shape `JoinMany` supports (right-list, left-symmetric,
collection reverse / forward; identity / selector; no-filter / filter / filter+arg), with an `Inner…`
variant that drops lefts whose count is `0`. Needed in Core (hand-written level 0 + T4 chained levels),
admitted by the frozen/prepared pipeline, and emitted by the Roslyn codegen as FK sugar
(`CountJoinWith{T}` / `InnerCountJoinWith{T}`). The nested continuation form
(`JoinMany(b => b.JoinXxx(...))`) is **out of scope** — say so in the docs.

**Performance claim.** Unfiltered: one index-bucket read per left, write `bucket.Count` into the slot.
No pair set, no fan-out, no store lookups, no per-left `QueryResults` buffer, no shared buffer, no
clone. The count is the index bucket's snapshot (same staleness window as the eager fan-out; the
concurrency contract promises no consistency, only "never fails"). Filtered: the fan-out + one paired
execute (same as JoinMany) but the container just increments an int per surviving right — still no
buffer / clone / shared partition.

## Rules (from CLAUDE.md — not optional)

- Never hand-edit `*.generated.cs`. Edit the `.tt`, regenerate from `src/Prague.Core`:
  `~/.dotnet/tools/t4 JoinResults.tt -o JoinResults` and `~/.dotnet/tools/t4 JoinQueryBuilders.tt -o JoinQueryBuilders`
  (basename only for `-o`; the template appends `.generated.cs`). Then `git diff --stat` to confirm the
  real generated files changed and no stray `*.generated.generated.cs` exists.
- `dotnet build Prague.sln` must be clean — warnings are errors. Trust the build, not the LSP.
- Apply the `code-style` skill (tabs, K&R braces, usings inside file-scoped namespace, `var`,
  `_camelCase` fields, no braces for single-line statements) and `high-performance-net` (no LINQ /
  closures / allocations on hot paths, `[MethodImpl(AggressiveInlining)]` on per-row calls,
  `NoInlining` on cold paths, static-abstract strategy structs).
- All tests NUnit. After touching Codegen/T4, run `Prague.Generated.Tests`.
- Anything on a query path is decided at build time, not per row. Re-run `Fk_OneToManyReverse` A/B after
  the resolver changes to confirm JoinMany did not regress.

## Step 1 — Core: shared bucket sources + shared paired-execute helper

Files: `src/Prague.Core/QueryBuilders/JoinManyFusedFill.cs`, `JoinManyResolver.cs`,
`JoinManyCollectionResolver.cs`, new `JoinManyBuckets.cs`, new `JoinManyPairedExecute.cs` (names are
suggestions).

1. Make `IManyBucketSource<TLeftKey, TRightKey>` **public** (it will appear in public type arguments).
2. Lift the three private `Buckets` structs into public top-level structs (the JoinMany resolvers then use
   them instead of their private copies and their private `TryGetBucket` helpers — one definition each):
   - `JoinManyRightListBuckets<TLeftKey, TRightKey, TRightValue, TIndexKey, TSelector>` — fields
     `CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey>` + `TSelector`; `TryGetBucket` = selector
     (identity via `Unsafe.As`) → `GetValuesUnsafe` → `bucket is { Count: > 0 }`.
   - `JoinManyLeftSymBuckets<TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightKey, TRightValue, TSelector>` —
     `leftIndex.Reverse.TryGetValue(leftKey, out lookupKey)` → selector → `rightIndex.GetValuesUnsafe`.
   - `JoinManyCollectionBuckets<TLeftKey, TRightKey, TOwnerValue>` — `rightsIndex.GetValuesUnsafe(leftKey)`.
   Constructors `internal`; types `public readonly struct` (or `struct` holding the selector). Keep
   `[MethodImpl(AggressiveInlining)]` on `TryGetBucket`.
3. Extract the identical ~35-line `ExecutePairs` (3 copies, `JoinManyResolver.cs:192`, `:~533`,
   `JoinManyCollectionResolver.cs:154`) into one internal static helper, e.g.
   ```csharp
   internal static class JoinManyPairedExecute {
     internal static void Run<TLeftKey, TRightCache, TRightKey, TRightValue, TFilter, TContainer>(
       TRightCache rightCache, ref TFilter filter, ref JoinManyFanOut<TLeftKey, TRightKey> fanOut, ref TContainer container)
       where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct …
   ```
   Body unchanged (early return on `DistinctRights == 0`, build paired core over `fanOut.Pairs`, wrap in
   the non-executable builder, `filter.Apply`, `MarkPairsHandedOff`, `ExecutePaired` vs `Delivery` +
   `ExecutePairedJoined`, `container = delivery.Inner`). Keep the doc comment once. All three JoinMany
   resolvers call it.

## Step 2 — Core: `JoinCountResolver`

New `src/Prague.Core/QueryBuilders/JoinCountResolver.cs`:

```csharp
public struct JoinCountResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TBuckets, TFilter>
  : IJoinResolver<TLeftKey, TLeftValue, int>
  where TBuckets : struct, IManyBucketSource<TLeftKey, TRightKey>
  where TFilter : struct, IJoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
  … (same constraints as the JoinMany resolvers)
```
Fields: `TRightCache _rightCache`, `TBuckets _buckets`, `TFilter _filter`, `bool _isInner`. Internal ctor
`(TRightCache rightCache, TBuckets buckets, TFilter filter, bool isInner = false)`.

Contract:
- `IsSorter => false`, `Inner => _isInner`, `static bool IJoinResolver.IsMany => true` (the frozen
  pipeline then admits it unchanged: fused in the fill walk when unfiltered, otherwise own execute after
  the pass, inner pruned by `UnsafePruneEmptyManySlots`).
- `CanFuse => TFilter.IsNoOp`.
- `Clone<TFullResult>` and `CloneValue(ref int)` are no-ops; no `SlotCloner` registration.
- `UnsafeFillFusedRows`: `keys = accessor.GetKeys<TLeftKey>()`; for `i` in rows:
  `accessor.GetSlotAt<int>(i) = _buckets.TryGetBucket(keys[i], out var b) ? b.Count : 0`; count
  empties; return `false` unless inner and empties > 0, then `accessor.PruneZeroCountSlots()` and
  return `true`. Do **not** touch `sizeHint`, the disposer, or the store.
- `UnsafeNarrowFused<TKey,TValue>(keys, values)`: keep keys whose bucket is non-empty (same
  compaction loop shape as `JoinManyFusedFill.Narrow`, without the store probe).
- `UnsafePruneEmptyManySlots` → `accessor.PruneZeroCountSlots()`.
- `UnsafeExecuteWithAccessor` (outer unfused: filtered eager, or filtered/sorted frozen): if
  `TFilter.IsNoOp`, do the same O(1)-per-left write as the fused fill but through
  `GetValueRef<TLeftKey,int>(key)` (or `GetSlotAt`) over `accessor.GetKeys<TLeftKey>()`. Otherwise:
  `var fanOut = new JoinManyFanOut<TLeftKey,TRightKey>(keys.Length)`, per key with a non-empty bucket
  `fanOut.RecordBucket(key, bucket)`, then a counting container
  ```csharp
  private ref struct CountContainer<TAccessor> : IJoinedResultContainer<TLeftKey, TRightValue> {
    TAccessor _accessor; int _total;
    public int TotalCount => _total;
    public int Add(TLeftKey left, TRightValue _) { ref var slot = ref _accessor.GetValueRef<TLeftKey,int>(left); if (Unsafe.IsNullRef(ref slot)) return 0; slot++; _total++; return 1; }
  }
  ```
  and `JoinManyPairedExecute.Run(_rightCache, ref _filter, ref fanOut, ref container)` inside
  `try/finally { fanOut.Dispose(); }`. Check the `IJoinedResultContainer` return-value convention in
  `QueryBuilderToolbox.cs` / `JoinManyFanOut.Delivery.Add` and mirror it.
- `PrepareIndexedInner`: `_ = leftQuery.GetCandidates<TLeftKey>();`
- `UnsafeExecuteIndexedInner` (eager inner): `ref var candidates = ref leftQuery.GetCandidates<TLeftKey>()`;
  return if uninitialised/empty. If `TFilter.IsNoOp`: for each candidate with non-empty bucket
  `accessor.GetValueRefOrAddDefault<TLeftKey,int>(key, out _) = bucket.Count` (no fan-out at all);
  if nothing was written → `candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty); return;` else
  `accessor.RetainNonZeroCountSlots<TLeftKey>(ref candidates)`. Filtered: materialise the slot with
  `GetValueRefOrAddDefault` (value stays 0), `RecordBucket`, then if `fanOut.DistinctRights == 0` →
  intersect-with-empty, else run the paired execute with the counting container and finish with
  `RetainNonZeroCountSlots`. `fanOut.Dispose()` in `finally`.
- `SupportsNarrowOnly` stays false (JoinMany does not implement it either).

Add `IUnsafeValueAccessor` members (in `QueryBuilderToolbox.cs`):
```csharp
internal void RetainNonZeroCountSlots<TKey>(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) where TKey : IEquatable<TKey>;
internal void PruneZeroCountSlots();
```
and in `JoinResults.tt` (per accessor, next to the `NonEmptyMany` pair, lines ~290-345): `PruneZeroCountSlots()`,
`RetainNonZeroCountSlots<TKey1>(ref candidates)`, and a `{prop}NonZeroCountFilter<TKey, TJoinResult>`
whose `Keep` is `value.UnsafeGet{prop}<int>() != 0`. Regenerate `JoinResults.generated.cs`.

## Step 3 — Core: `CountJoin` / `InnerCountJoin` extension methods

Mirror the JoinMany naming: `CountJoin` / `InnerCountJoin` (right-list and left-sym families; the
left-sym ones take the same `(leftIndex, rightCache, rightIndex[, selector][, filter[, arg]])`
parameters as the corresponding `JoinMany` overloads), `CountJoinCollection` /
`InnerCountJoinCollection`, `CountJoinCollectionForward` / `InnerCountJoinCollectionForward`. Each
overload has the identical parameter list to its `JoinMany` twin (see
`CacheQueryBuilder.JoinMany.Extensions.cs` — 30 shapes M1-3, MS1-3, MSA1-3, MI1-3, LS1-3, LSI1-3,
LSS1-3, LSSI1-3, LSSA1-3, LSSAI1-3 — and `…JoinManyCollection.Extensions.cs` /
`…JoinManyCollectionForward.Extensions.cs`), returns
`CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, Resolvers<TResolverChain, JoinCountResolver<…>>, JoinResult<TLeftValue[, T1..Tn], int>>`
and calls `Unsafe.AsRef(in builder).AddResolver<…>(new Resolvers<…>(builder._resolverChain, resolver), false)`
— **`isMany: false`**: `_manyCount` counts pooled child buffers and a count has none.

Preferred: generate all levels (0..MaxJoins-1) from **one new T4 loop** in `JoinQueryBuilders.tt`,
placed after the main `for (int level = 1; …)` loop closes (line ~4047, just before
`#endregion` / "Generated IResolverChain Interfaces"), inside its own `#region Generated CountJoin
Extension Methods`. Level 0 uses `TValue` as the builder's current result type and
`JoinResult<TValue, int>`; level ≥ 1 uses `JoinResult<TValue, T1..Tn>` → `JoinResult<TValue, T1..Tn, int>`.
Copy the variable set the chained JoinMany block uses (lines ~2770-2960: `nonExecBuilderM`,
`joinFilterM`, `joinFilterArgM`, `methodTypeParamsM`, the LeftSym/selector variants, and the collection
block at ~3728-4045) and swap the resolver type for `JoinCountResolver<TKey, TValue, TRightCache,
TRightKey, TRightValue, JoinMany{Family}Buckets<…>, {filter}>`. Check whether level-0 generic method
constraints clash with the hand-written level-0 JoinMany extension class; put the count methods in
their own static class (e.g. `CacheQueryBuilder_CountJoin_Extensions`). Doc-comment each method.
Regenerate `JoinQueryBuilders.generated.cs`, then `dotnet build Prague.sln`.

## Step 4 — Codegen: `CountJoinWith{T}` / `InnerCountJoinWith{T}`

`src/Prague.Codegen/CacheGenerator.cs`, inside the per-level Combined JoinWith loop. Next to each of
the three JoinMany emission blocks (reverse one-to-many ~6956-6990, collection reverse ~6907-6946,
collection forward ~7099-7130), add two `EmitFkJoinWithOverloads` calls:
- method names `$"Count{methodName.Substring(4)}"` → `CountJoinWith{T}` and `InnerCountJoinWith{T}`
  (methodName is `JoinWith{T}`; the inner twin already uses `$"InnerJoin{methodName.Substring(4)}"`);
- resultType `JoinResult<doc[, T1..Tlevel], int>`;
- `resolverFor = f => $"JoinCountResolver<{key}, {doc}, {cacheFqn}, {otherKey}, {otherType}, {bucketsType}, {f}>"` with
  `bucketsType` = `JoinManyRightListBuckets<{key}, {otherKey}, {otherType}, {key}, IdentitySelector<{key}>>`
  (reverse), `JoinManyCollectionBuckets<{key}, {otherKey}, {otherType}>` (collection reverse,
  TOwnerValue = otherType), `JoinManyCollectionBuckets<{key}, {fwdKey}, {doc}>` (forward, TOwnerValue = doc);
- delegateName `"CountJoin"` / `"InnerCountJoin"` / `"CountJoinCollection"` / `"InnerCountJoinCollection"` /
  `"CountJoinCollectionForward"` / `"InnerCountJoinCollectionForward"`, same `baseDelegateArgs`.
- Also emit the sorted twin via `EmitFkSortedJoinWithOverload` for the outer no-filter shape, as the
  JoinMany blocks do.
Check `EmitFkJoinWithOverloads` (line ~7547) does not hardcode `QueryResults<…>` anywhere. Build, then
`dotnet test tests/Prague.Generated.Tests`.

## Step 5 — Tests (NUnit)

- `tests/Prague.Core.Tests/Join/CountJoinCoreTests.cs`: right-list (`MnAuthor`/`MnBook` fixtures as in
  `JoinManyRightListIndexCoreTests`), left-sym (`MlsAuthor`/`MlsBook`), collection reverse + forward
  (`MnTag`/`MnTaggedBook`). For each: outer counts incl. zeros, inner drops zeros, filter and filter+arg
  counts only surviving rights, selector shape, shared right across lefts counted for each, and a
  differential assertion `CountJoin == JoinMany(...).Right.Count` per left for every shape. Pooled
  `ExecutePooled()` + `Dispose()` path and `Execute()` path both.
- `tests/Prague.Core.Tests/Prepared/FrozenPipelineCountJoinTests.cs` (pattern:
  `FrozenPipelineJoinManyTests.cs`): eager == prepared `.Build()` == `.BuildFrozen()` for outer/inner,
  unfiltered (expect `Plan.Executor == "Pipeline"`, fused) and filtered; `Count()`; chained after a
  JoinOne and before a Sort.
- `tests/Prague.Generated.Tests/Join/CountJoinTests.cs` (`JmnAuthor`/`JmnBook`, registry-built):
  `CountJoinWithJmnBook()`, `InnerCountJoinWithJmnBook()`, filter overload, and a frozen twin
  (`Prepare<int>().WithId(static id => id).CountJoinWithJmnBook().BuildFrozen()`, `Explain()` contains
  "executor: Pipeline"). Add a collection FK case if a collection fixture exists in Generated.Tests.
- Run `dotnet build Prague.sln` then `dotnet test Prague.Tests.slnf` (Kafka integration tests need Docker;
  socket failures there are the only acceptable failures — say so in the report).

## Step 6 — Benchmark + docs

- `benchmarks/Prague.Benchmarks/FrozenFkJoinBenchmarks.cs`: add categories `Fk_OneToManyReverse_Count`
  (eager `CountJoinWithFfkLine` baseline vs frozen) and `Fk_Collection_Count`; run
  `dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories Fk_OneToManyReverse Fk_OneToManyReverse_Count`
  (one category per run per CLAUDE.md is fine too), note machine load (`uptime`) next to the numbers,
  append the table to `benchmarks/Prague.Benchmarks/RESULTS.MD`.
- Docs: a `#region count-join` sample in `samples/Prague.Samples/Concepts/Joins.cs` (a `[Test]`, e.g.
  customers → `CountJoinWithOrder()` printing `row.Right` orders), referenced from
  `www/docs/articles/core-concepts/joins.md` after the reverse-join paragraph ("`CountJoinWith{X}` yields
  the count as `int` without materialising the rights; `InnerCountJoinWith{X}` drops zero counts; the
  nested continuation form has no count variant"). Update `context/joins.md` (resolver family list +
  fused-fill note) and the cardinality table in `context/generated.md` (~lines 32-41).

## Report back

List: files changed/created, the build result, test totals (and the Docker-only failures), benchmark
numbers with machine load, and anything left out with the reason.

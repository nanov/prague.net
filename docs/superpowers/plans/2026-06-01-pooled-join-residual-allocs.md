# Follow-up: eliminate the remaining pooled-join per-query allocations

> Status: **Item 1 investigated → declined; Item 2 open.** Spun out of the `QueryResultsDisposer`
> → struct change (which removed the last *disposer* allocation). These are two **separate,
> pre-existing** per-query allocations on pooled join paths, discovered while verifying that work.
>
> **Resolution (2026-06-01) — Item 1 declined.** Confirmed the `n`-scaling allocation is the `pairs`
> `ValueSet` growth (forcing a large fixed capacity flattens the per-query curve to ~0). But:
> (a) the payoff is small — it caps at ~432 B/query only under pathological skew and is already
> **0 B/op on realistic shapes** (the heavy-join benchmark shows 0 allocated, 0 Gen0 pooled);
> (b) a correct fix is fiddlier than "size to the match count" — sizing `pairs` to the *exact*
> match count did **not** stop the growth (a hash set resizes on load factor, so it needs ~1.4×
> headroom above the count), and the cheap O(1) count (`CacheKeyValueListIndex.ApproximateCount`)
> is exact only for **unfiltered** joins (an over-count once a `Where` narrows the left set).
> Net: small benefit, real fiddliness → left as-is. Kept here as an investigation record.
> **Item 2 (`Where` closure) remains an open, independent follow-up.**

## Context

After making `QueryResultsDisposer` an inline struct, the pooled `JoinMany` path is allocation-free
on the benchmark's uniform shape (0 B/op), but a deterministic micro-benchmark surfaced two residual
per-query allocations that are **not** the disposer:

1. **`pairs` `ValueSet` growth churn** — scales with `log₂(matched-right-count)`.
2. **`Where(Predicate<TValue>)` closure** — one closure+delegate per query when the predicate captures.

Neither is per-row; both are pre-existing and orthogonal to the disposer change.

---

## Item 1 — Pre-size the `pairs` ValueSet in JoinMany (the ~log₂(N)·72 B churn)

### Evidence

Pooled `JoinMany` (single parent, varying child count), per-query allocation:

| children (pairs) | 16 | 64 | 256 | 1024 | 2000 | 4096 |
|------------------|----|----|-----|------|------|------|
| alloc/query (B)  | 0  | 72 | 216 | 288  | 360  | 432  |

≈ +72 B per doubling tier → it's the **incremental growth** of the `pairs` set, not per-row work.

### Root cause

`ExecuteReverse` (`src/Prague.Core/QueryBuilders/JoinManyResolver.cs:165`) creates:

```csharp
var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, …>(leftKeys.Length);
```

It is sized to the number of **left keys**, but then filled with the **total number of right
matches** (`Σ bucket.Count` over left keys). With fan-out (e.g. one parent, 2000 children) it grows
`leftKeys.Length → … → ~2729` through ~log₂(N) `SetCapacity` steps (`ValueSet.cs:477-541`). Each step
rents new buckets/slotMetas/values arrays from `ArrayPool<T>.Shared` and returns the old ones; the
shared pool's per-core/TLS slots can't always hand the exact-size array back the next iteration (other
`int[]`/array structures in the same query compete for the same buckets), so ~one ~72 B re-rent misses
per growth tier. Pure ArrayPool churn — proportional to the number of doublings.

`ValueSet` itself is fully pooled and correct; the defect is **sizing**.

### Fix

Size `pairs` to the total expected pair count up front so it `Initialize`s once at the right tier and
never grows. The per-left `bucket.Count` is already read in the build loop.

- **Outer** (`ExecuteReverse`, `JoinManyResolver.cs:154-214`): add a cheap pre-pass over `leftKeys`
  summing `_rightIndex.GetValuesUnsafe(indexKey)?.Count ?? 0`, then `new ValueSet<…>(totalPairs)`.
  The extra `GetValuesUnsafe` per left key is a hash lookup (cheap); avoid materializing a temp list.
- **Inner** (`UnsafeExecuteIndexedInner`, `JoinManyResolver.cs:255-322`, the `pairs` at `:266`): same —
  pre-sum bucket counts over `candidates` before constructing `pairs`.
- **LeftSym resolver** (`JoinManyResolver.cs` LeftSym family, the `InnerKeyedContainer` paths): verify
  whether its keyed set has the same under-sizing and apply the same pre-sum if so.
- Leave **JoinOne** paired families as-is: 1:1 matches are `≤ candidates.Count`, so the existing
  `candidates.Count` sizing already avoids growth (no fan-out).

### Verification

- Reuse the `JoinPooledBufferReturnCoreTests` allocation harness; add a sweep asserting pooled
  `JoinMany` per-query allocation is **~0 and flat across child counts** (16…4096), not the
  log₂-shaped curve above.
- All `tests/Prague.Core.Tests/Join` + `tests/Prague.Generated.Tests` stay green.
- Re-run `JoinRuntimeBenchmarks` heavy-join (Allocated/op should stay 0 and the skewed-shape micro
  case should now also be 0).

---

## Item 2 — Zero-alloc `Where` overload (capturing-predicate closure)

### Root cause

`Where` takes a delegate:

```csharp
// CacheQueryBuilder.cs:2192-2204
public static …Where<…>(this in …builder, Predicate<TValue> predicate) …
```

A capturing lambda — the common `q.Where(p => p.X == localVar)`, or any lambda that captures `this`/a
field — allocates a **display-class closure + delegate per query call**. Only a fully non-capturing
predicate (`static p => p.Id == 3`) is cached by Roslyn as a singleton (0 alloc). So every filtered
query that closes over a parameter pays one closure allocation.

### Fix (mirror the existing join-filter strategy pattern)

The join filters already solve this with `…WithArg<TArg>` + a **`static` lambda + explicit arg** (see
`IJoinOneFilter` / `JoinOneFilterWithArg` in `context/joins.md`, and the `high-performance-net` skill's
"func + arg, declare the lambda `static`" rule). Add the analogous zero-capture `Where` overloads:

- `Where<TArg>(TArg arg, Func<TValue, TArg, bool> predicate)` — caller passes a `static` lambda + the
  captured state as `arg`; no closure allocates. Thread `arg` into `WhereInternal` (`_leftQuery`).
- Optionally a static-abstract struct-predicate overload
  `Where<TPredicate>(TPredicate predicate) where TPredicate : struct, IValuePredicate<TValue>` for
  fully devirtualized, zero-alloc filtering on the hottest paths.
- Keep the existing `Predicate<TValue>` overload for ergonomics; document that capturing predicates
  allocate and point to the `…WithArg` overload for hot paths.

Find `WhereInternal` on the left-query executor and add the arg-threaded variant alongside it.

### Verification

- Add an allocation test: `q.Where(arg, static (p, a) => p.X == a).ExecutePooled()` allocates **0 B/op**
  after warmup, vs the capturing-lambda overload which allocates one closure/query.
- Existing query/filter tests stay green.

---

## Notes

- Both items are independent of (and safe to land after) the `QueryResultsDisposer` struct change.
- Item 1 is the higher-value, lower-risk win (pure internal sizing change, no public API).
- Item 2 adds public API surface — confirm the overload shape/naming against the join-filter
  convention before implementing.

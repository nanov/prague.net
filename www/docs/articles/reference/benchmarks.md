---
title: Benchmarks
description: The latest measured numbers for every Prague benchmark family - indexed lookups, intersections, OR queries, sorted paging, prepared and frozen queries, joins, concurrent reads under a writer - with the log section each table comes from and how to reproduce them.
---

# Benchmarks

This page is the curated view of [`benchmarks/Prague.Benchmarks/RESULTS.MD`](https://github.com/nanov/prague.net/blob/main/benchmarks/Prague.Benchmarks/RESULTS.MD), the chronological log of every BenchmarkDotNet table the project has recorded. Each section below explains what one benchmark family measures, then shows the **latest** rows for it, copied from the most recent section of the log that measured that shape. Every table names the log section it came from (the log's `#` headers), so the full table, its error bars and the reading behind it are one search away. Nothing here is rounded or re-derived; where the log has no row for a scenario, the page says so.

## How these numbers were measured

- **Machine:** Apple M4 Pro (12 cores), macOS, .NET 9 (`osx-arm64`), Release. Absolute numbers vary by hardware; the ratios travel better than the means.
- **Harness:** BenchmarkDotNet with the in-process toolchain (`--inProcess`, `InProcessNoEmit`), `[MemoryDiagnoser]`, the default job (15+ iterations) unless a table says `--job short` (3 iterations, read as order-of-magnitude).
- **One category per run, nothing else running.** Rows in the 5–20 µs range move more between runs than many of the deltas being claimed, so the log records the 1-minute machine load next to its tables and treats a ~5% between-run spread as noise.
- **What a row is.** Unless stated otherwise the fixture is the raw `InMemoryDataCache` (no codegen) with 100k rows, list buckets of 1k rows, range windows of 1k codes, and every body executes pooled (`ExecutePooled()`) and disposes. `Eager` is the fluent builder as a caller writes it, `Build()` the prepared replay, `BuildFrozen()` the plan-once executor; `Allocated` is per execution.
- **`1 B` cells** in the log are BenchmarkDotNet amortising a one-off pool refill over many operations, not a per-execution allocation; the deterministic 0 B pins live in the test suite (`PreparedQueryAllocationTests`, `FrozenPipelineAllocationTests`, `TopKAllocationCoreTests`).
- The regression tripwire that guards these paths per PR is a separate, smaller suite: see [`perf/README.md`](https://github.com/nanov/prague.net/blob/main/perf/README.md).

## The headline numbers, traced

The README and the docs index quote three figures. Here is where each one lives in the log, and where it does not.

| Claim | Log row | Section of `RESULTS.MD` |
|---|---|---|
| **18 ns** frozen unique-key point lookup | `UniqueArg` `BuildFrozen()` **17.99 ns**; `Unique (bound int args)` **17.94 ns** (first logged as `UniqueArg_Frozen` 18.84 ns, default job) | "Frozen queries — the ordering relaxation", table "Every row in the sweep"; "Frozen queries — `BuildFrozen()` stage 1" |
| **<100 ns** indexed lookup | No row of the log measures a bare index probe plus fetch. The nearest rows: the eager unique-key query `Unique_Eager` at **130.97 ns**, which includes renting and disposing the pooled result container; a store lookup at **133.3 ns per 100 keys** and a `PooledSet` probe at **414.2 ns per 100 keys** (≈1.3 ns and ≈4 ns per key) | "Every row in the sweep"; "Lock vs lock-free", S1 table (`Store_ConcurrentCacheStore`, `Probe_PooledSet`, SeedSize 100) |
| **11M+ reads/sec** concurrent pooled join | `HeavyJoinPooledBenchmarks`, `ExecutePooled()` row: **11,100,645 Reads/s** (run 2) and 11,741,457 (run 1); 10 reader threads + 2 writer threads, pooled 3-level join, reads = result rows returned ÷ wall-clock seconds, 9.19 KB allocated for the whole 3 s window versus 7.2 GB on the allocating path. The README used to say 15.9M+; that figure was never logged and did not reproduce on a non-idle machine (1-minute load 2.7–4.1 at start) | "HeavyJoinPooledBenchmarks — first logged run (2026-10-23)" |

## Indexed lookups

A unique-index equality (`WithId(x)`-style) is the shortest query the cache answers: one index probe, one store fetch, the filters, and a one-slot result. The eager builder pays its general machinery on top (a candidate set rented and returned, a composed filter closure); `BuildFrozen()` binds the `PointLookup` executor for this shape and skips all of it, which is why the row is the one most people will feel first.

From "Frozen queries — the ordering relaxation", table "Every row in the sweep" (`FrozenQueryBenchmarks`, 54 categories in one sweep, load 2.7–5):

| Shape | Eager | `Build()` | `BuildFrozen()` | Speed-up |
|---|---:|---:|---:|---:|
| `Unique` (bound key) | 130.97 ns | 126.61 ns | **25.15 ns** | 5.21× |
| `Unique` (bound `int` args) | 130.97 ns | — | **17.94 ns** | 7.30× |
| `UniqueArg` (key as argument) | 131.60 ns | 129.17 ns | **17.99 ns** | 7.32× |
| `UniqueWhere` | 148.96 ns | 139.09 ns | **19.07 ns** | 7.81× |
| `UniqueArgWhere` | 163.79 ns | 135.77 ns | **20.49 ns** | 7.99× |

Every frozen row is 0 B. On the eager side only `UniqueArgWhere` allocates (88 B, the closure a parameterised `Where` needs — "stage 1" table, default job); the prepared twin binds the argument into a pooled predicate box and allocates nothing.

## Intersections

Two or more indexed narrowings intersect their candidate sets; the eager planner walks them in declaration order on a stackalloc bitmap and short-circuits on the first empty lane. The frozen pipeline instead seeds from the smallest signal and probes the other steps on the fetched value, so the walk is the size of the narrowest bucket rather than the first one declared — the rows where the first index is the large one (`ListList`, `ListListList`, `ListRange`, `ListLastUpdated`) are where that shows.

From "Every row in the sweep" (same section as above):

| Shape | Eager | `Build()` | `BuildFrozen()` | Speed-up |
|---|---:|---:|---:|---:|
| `ListWhere` (1k bucket + filter) | 8.615 µs | 8.578 µs | **5.835 µs** | 1.48× |
| `ListList` (1k ∩ 100, largest first) | 9.264 µs | 9.109 µs | **1.288 µs** | 7.19× |
| `ListListReversed` (100 ∩ 1k) | 1.312 µs | 1.321 µs | **1.018 µs** | 1.29× |
| `ListListList` (1k ∩ 333 ∩ 111) | 11.179 µs | 11.100 µs | **1.775 µs** | 6.30× |
| `ListRange` (1k bucket ∩ range window) | 397.57 µs | 396.34 µs | **11.22 µs** | 35.43× |
| `ListKeySet` (1k ∩ key-set of a third of the rows) | 12.017 µs | 11.995 µs | **9.037 µs** | 1.33× |
| `ListLastUpdated` (1k ∩ updated-after, half the rows) | 216.375 µs | 216.127 µs | **9.657 µs** | 22.41× |
| `Range` (1k-code window) | 12.822 µs | 12.651 µs | **6.572 µs** | 1.95× |
| `OptionalRange` (both bounds present) | 12.784 µs | 12.761 µs | **6.628 µs** | 1.93× |

`Count()` twins from the same sweep: `Count_ListList` 8,687.9 / 8,527.7 / **969.1 ns** (8.96×), `Count_ListRange` 394.080 / 394.455 / **8.596 µs** (45.84×), `Count_ListWhere` 6.672 / 6.623 / **5.521 µs** (1.21×).

Filter fusion and ordering on one bucket, same sweep: `ListTwoWheres` 8.080 / 9.649 / **7.003 µs** (1.15×) and `ListThreeWheresSelectiveLast` 9.822 / 11.557 / **7.242 µs** (1.36× — the adaptive filter order moves the 99%-rejecting predicate to the front). `ListTwoArgWheres` was re-measured later, after the arg predicate started taking `TArgs` by `in` ("Frozen queries — closing the branch", table "`TArgs` by `in`", load 3.56): 10.858 µs eager (256 B) / 13.098 µs `Build()` (96 B) / **9.788 µs** frozen (0 B).

## OR queries and composites

`Or` unions the candidates of its branches; `If` / `Match` select an arm per execution. Eagerly, an `Or` that is the query's first narrowing seeds from the whole store (a 100k-key bitmap union); frozen, the union itself is the seed, so the walk is two buckets instead of the store — the opt-out that keeps the eager sequence (`PreserveEagerOrder`) shows the price of that order.

From "Every row in the sweep", plus the later `If` re-measure from "Frozen queries — closing the branch", table "Guard-form `Match`" (`after (1a29961)` column):

| Shape | Eager | `Build()` | `BuildFrozen()` | Speed-up |
|---|---:|---:|---:|---:|
| `Or` (two 1k buckets, `Or` first) | 976.18 µs | 982.91 µs | **26.95 µs** | 36.22× |
| `Or` under `PreserveEagerOrder` | 976.18 µs | — | 757.43 µs | 1.29× |
| `OrAfterList` (1k bucket, then an `Or` of two unique keys) | 7,134.5 ns | 7,195.8 ns | **249.0 ns** | 28.65× |
| `Match` (three parameterised arms, third selected) | 8.431 µs | 8.469 µs | **7.104 µs** | 1.19× |
| `Count_Or` | 950.47 µs | — | **15.37 µs** | 61.84× |
| `Count_Match` | 6.701 µs | — | **5.870 µs** | 1.14× |
| `IfTaken` (1k bucket, then a taken `If(unique)`) | 3,036.5 ns | — | **123.5 ns** | — |
| `IfSkipped` (1k bucket, `If` skipped) | 10.158 µs | — | **8.465 µs** | 0.83 ratio |

The eager `Or` rows allocate 128 B (the auto-seeded `Candidates` set); every frozen row is 0 B ("stage 3, step 4" section). `IfTaken_Frozen` is bimodal on this machine (~103 or ~124 ns per process); the sweep read it at 123.4 ns.

## Sorted paging

`Sort(cmp)` materialises every match and sorts it; `SortBounded(cmp)` runs a top-k selection sized to the page, so its cost scales with `skip + take` instead of the match count. The crossover matters: a small page from a large match set wins big, a page that is most of the result loses to the full sort.

From "Sorted paging — `Sort` vs `SortBounded`" (`TopKExecuteBenchmarks`, eager path, `--job short`, N = 100,000 matches; `Sorted_ExecutePooled` is the bounded plan, `Sorted_ClassicCore_Pooled` the classic one):

| Shape | `SortBounded` mean | Allocated | `Sort` (classic) mean | Allocated |
|---|---:|---:|---:|---:|
| page of 20 | **940.1 µs** | - | 8,066.5 µs | 64 B |
| page of 200 | **1,079.9 µs** | - | 7,711.9 µs | 64 B |
| page of 20 at the end (`skip` deep) | **2,192.4 µs** | - | 7,552.9 µs | 64 B |
| half the result | 10,281.4 µs | - | **7,766.1 µs** | 64 B |
| the whole result | 16,241.0 µs | - | **7,555.5 µs** | 64 B |
| joined (`InnerJoinOne`), page of 20 | **4,847.4 µs** | - | 24,403.8 µs | 24 B |
| joined, whole result | 28,942.7 µs | - | **25,207.3 µs** | 24 B |

Per-query allocation, pinned deterministically on a 1,000-row cache (same section):

| Query | B/op |
|---|---:|
| unsorted `ExecutePooled()` | 0 |
| `SortBounded(...).ExecutePooled(0, 10)` — struct or class comparer | **0** |
| `Sort(...).ExecutePooled()` | 88 |
| unsorted joined `ExecutePooled()` | 144 |
| `SortBounded(...).InnerJoinOne(...).ExecutePooled(0, 10)` | **144** |
| `Sort(...).InnerJoinOne(...).ExecutePooled()` | 168 |

The frozen executor's sorted rows were last measured when the eager top-k containers started calling the user comparer directly ("Frozen queries — closing the branch", table "R1b for the eager path", `after` columns, loads 1.9–11.8; 0 B on every row):

| Shape | Eager | `Build()` | `BuildFrozen()` |
|---|---:|---:|---:|
| `SortBounded` (1k bucket, heap plan, page 0..20) | 13.60 µs | 13.39 µs | **12.12 µs** |
| `SortBounded_ListList` (1k ∩ 100, page 0..20) | 10.85 µs | 10.79 µs | **2.838 µs** |
| `SortBounded_JoinMany` (page 0..20) | 16.47 µs | 16.54 µs | **14.64 µs** |
| `Sort_JoinMany` (classic `Sort`, page 0..20) | 120.8 µs | 121.7 µs | **14.38 µs** |

`Sort_ListList` (1k ∩ 100 under a classic `Sort`) is 10.365 / 10.181 / **2.105 µs** (4.92×) and `Count_SortBounded` 5.015 / 4.892 / **2.116 µs** (2.37×) in "Every row in the sweep". A frozen classic `Sort` with a finite page takes the bounded plan (the 7.3× on `Sort_JoinMany`), because ties inside a sorted result are unspecified — see [Sorted Paging](../core-concepts/sorted-paging.md) and [Prepared and Frozen Queries](../core-concepts/prepared-queries.md).

## Prepared vs frozen vs eager

`Prepare()…Build()` replays the eager plan with the arguments bound, so its execution cost *is* the eager cost and its only saving is the closure a parameterised `Where` would allocate; `BuildFrozen()` plans once and binds one of three executors (`PointLookup`, `Pipeline`, `Replay`). The two production shapes the project gates every change on are **A** — three list indexes (1k ∩ 333 ∩ 111) → `SortBounded` page 20..40 → `JoinOne` — and **B** — 10k records newer than T (~15%) → two list keys → `SortBounded` page 20..40 → two chained `JoinOne`s (with a range-on-timestamp variant).

Eager vs `Build()`, from "Prepared queries — eager builder vs `PreparedQuery<TArgs, TResult>`" (`PreparedQueryBenchmarks`, the three widest rows re-run with the default job):

| Shape | Eager | `Build()` | Ratio | Allocated eager → prepared |
|---|---:|---:|---:|---:|
| `IfSkipped` | 10.213 µs | 10.129 µs | 0.99 | 1 B → 1 B |
| `ListArgWhere` | 8.990 µs | 9.163 µs | 1.02 | 89 B → 1 B |
| `Or` | 1,116.326 µs | 1,045.573 µs | 0.94 | 3136 B → 3136 B |

Building is `Build_FourNarrowers` **32.94 ns / 88 B**, once (same section, `--job short`).

The production shapes, latest from "Frozen queries — closing the branch", table "R1b for the eager path" (`after` columns; the frozen rows did not move in that change, the eager side got faster):

| Shape | Eager | `Build()` | `BuildFrozen()` |
|---|---:|---:|---:|
| A: `ListListListSortBoundedJoinOne` | 14.16 µs | 13.86 µs | **3.917 µs** |
| B: `TimeWindowListListSortBoundedJoinTwo` | 28.36 µs | 28.61 µs | **17.88 µs** |
| B-range: `TimeRangeListListSortBoundedJoinTwo` | 31.56 µs | 31.33 µs | **20.53 µs** |

The log reads that as shape A 3.6× and shape B 1.6×. Their `Count()` twins, from "Every row in the sweep": A `Count` 10.999 µs → **1.421 µs** (7.69×), B `Count` 21.18 µs → **12.63 µs** (1.67×), `Count_TimeRangeListListSortBoundedJoinTwo` 24.65 / 24.35 / **16.54 µs** (1.49×). Every frozen row is 0 B per pooled execution. The gap that remains on shape A is quantified in the "codegen ceiling" rows further down.

## Joins

A `JoinOne` fills one right slot per left row; a `JoinMany` fills a slot of rights per left (right-list, left-symmetric with shared rights, or a collection-backed M:N). Eagerly a join builds a pair set and does a paired bulk read; the frozen pipeline fuses `JoinOne` into the pass as one point lookup per row and fills `JoinMany` per left in row order, and an inner join becomes a bucket probe that rejects a left with one lookup.

Hand-written joins on the raw cache, from "Every row in the sweep" (the filtered row from the later "Filtered `JoinOne` fuses" table, load 2.80):

| Shape | Eager | `Build()` | `BuildFrozen()` | Speed-up |
|---|---:|---:|---:|---:|
| `JoinOne` (1k list → outer left-symmetric) | 31.04 µs | 29.53 µs | **19.90 µs** | 1.56× |
| `InnerJoinOne` (PK-to-PK, every fourth right missing) | 33.86 µs | 33.61 µs | **17.55 µs** | 1.93× |
| `JoinOneChained` (two outer joins) | 44.33 µs | 44.81 µs | **26.24 µs** | 1.69× |
| `JoinOneFiltered` (right-side filter, one shared right) | 21.34 µs | 21.25 µs | 21.64 µs | 1.01 ratio |
| `JoinMany` (list 1k → ~1.5k lines) | 98.35 µs | 95.76 µs | **65.07 µs** | 1.51× |
| `InnerJoinMany` | 103.68 µs | 102.99 µs | **59.82 µs** | 1.73× |
| `JoinMany_LeftSym` (1k orders × 3 shared notes) | 85.37 µs | 82.62 µs | **47.20 µs** | 1.81× |
| `InnerJoinMany_LeftSym` | 104.16 µs | 103.95 µs | **47.55 µs** | 2.19× |
| `JoinMany_Collection` (1k docs → tags, M:N) | 115.73 µs | 116.88 µs | **57.32 µs** | 2.02× |
| `InnerJoinMany_Collection` | 122.13 µs | 122.16 µs | **58.11 µs** | 2.10× |
| `Count_JoinMany` | 5.145 µs | 4.973 µs | **2.969 µs** | 1.73× |
| `Count_InnerJoinMany` | 93.49 µs | 93.46 µs | **24.86 µs** | 3.76× |

The generated `JoinWith{T}` path (`[DataCache]` + `[DataCacheForeignKey]` models, 100k customers / orders, 10k docs, ~1k-row buckets), from "Frozen queries — closing the branch" — the first-measurement table, the `Fk_SortBounded_ManyToOne` table (#92) and the "Filtered `JoinOne` fuses" table; every row 0 B on both sides:

| Shape | Eager | `BuildFrozen()` | Speed-up |
|---|---:|---:|---:|
| `Fk_ManyToOne` (forward, left-symmetric) | 51.43 µs | **25.47 µs** | 2.02× |
| `Fk_InnerManyToOne` | 52.36 µs | **31.15 µs** | 1.68× |
| `Fk_OneToOneReverse` (right-unique) | 30.47 µs | **22.12 µs** | 1.38× |
| `Fk_OneToManyReverse` (right-list `JoinMany`) | 79.35 µs | **56.95 µs** | 1.39× |
| `Fk_Collection` (M:N) | 96.55 µs | **53.70 µs** | 1.80× |
| `Fk_SortBounded_OneToOneReverse` (page 20..40) | 30.95 µs | **19.23 µs** | 1.61× |
| `Fk_SortBounded_ManyToOne` (page 20..40) | 29.27 µs | **14.39 µs** | 2.03× |
| `Fk_ManyToOne_Filtered` (filter keeps half) | 41.67 µs | **27.84 µs** | 1.50× |

`Fk_SortBounded_OneToOneReverse` was later re-measured at **16.53 µs** frozen (1.86× eager) once the bounded joined container carried the comparer directly (same section, table "R1b — the comparer carried into the bounded container").

**Allocating vs pooled.** `JoinRuntimeBenchmarks` (`HeavyJoin_Execute` vs `HeavyJoin_ExecutePooled`), `NestedJoinBenchmarks` and `JoinCountBenchmarks` have no table in the log. The pooled-vs-allocating evidence the log does carry is the per-query B/op table under [Sorted paging](#sorted-paging) (unsorted joined `ExecutePooled()` 144 B, the rest of the pooled rows 0 B) and the concurrent table in the next section. The oldest join table in the log, `# v0.0.9` "JoinCacheBenchmark (GameCount = 500; MarketsPerGame = 20)", is the allocating era before pooling (`JoinCacheBenchmarks`, whose models have since been renamed from games/markets to products/offers): `ReadAllGames` 2.486 µs / 7.92 KB, `ReadGamesWithInfo` 11.929 µs / 7.95 KB, `ReadLiveGamesWithMarkets` 127.173 µs / 73.08 KB, `ReadGamesWithMarkets` 304.801 µs / 175.98 KB, `SingleGameLookupWithJoins` 317.110 µs / 2021.09 KB. Treat those as history, not as the current path.

## Concurrent reads with a writer

Readers are lock-free (`PooledSet` under a `ReaderGate` pin, `ConcurrentCacheStore`); one writer mutates while N reader threads run a pooled 3-level join in a tight loop. The pooled path keeps the GC out of the picture, which is what lets throughput hold under churn.

From `# v0.0.9`, table "Optimimizations Rounds 3 - Final" (`ConcurrentReadWriteBenchmark`: 11 reader threads, 3,000 ms; `Reads` = result rows returned; the class now defaults to 10 readers and 6,000 ms):

| Method | Reads | Writes | Reads/s | Gen0 | Allocated |
|---|---:|---:|---:|---:|---:|
| `ConcurrentReadsWithWriter_Pooled` | 29591857 | 1535 | **9711801** | - | **90.2 KB** |
| `ConcurrentReadsWithWriter_NotPooled` | 24763813 | 1545 | 8132615 | 607000.0000 | 4841851.1 KB |

The 11M+ reads/sec headline comes from `HeavyJoinPooledBenchmarks` (pooled 3-level join under 2 writers), logged on 2026-10-23 on a non-idle machine; see [The headline numbers, traced](#the-headline-numbers-traced).

Whether locks could beat the lock-free primitives was measured and answered in "Lock vs lock-free (pipeline primitives)" (`LockVsLockFreeConcurrencyBenchmarks`, wall-clock harness, 8 reader threads; the composed pass = copy a 1k seed → probe 1k keys against a 300-bucket → fetch the hits):

| Scenario | Lock-free | Locked (batch, `HashSet`) |
|---|---:|---:|
| S2 — 8 readers, no writer: passes/s, p99 | **669k/s, p99 12.9 µs** | 318k/s, p99 208 µs |
| S3 — 8 readers + 1 writer at max rate: reads/s, reader p99 | **69k/s, p99 827 µs** | 40k/s, p99 3.7 ms |
| S3 — writer ops/s | **2.77M/s** | 6.7k/s |

Locks win only with a single reader (0.67–0.81 of the lock-free time); with eight readers they serialise the pass, and with a writer present they starve it by ~400×. The lock-free model stays.

## Kafka consume path

`KafkaLiveDispatchBenchmarks` compares the per-message managed allocation of the consumer's live dispatch — the original typed-channel path against the `RawConsumer` + zero-copy + ring-buffer-worker path — and `KafkaProduceSerializeBenchmarks` the typed vs scratch-buffer serialize on the producer side. **Neither has a table in the log yet.** The only ingestion-side table recorded is `# v0.0.9` "RingBuffer ArrayPool", from a benchmark class that is no longer in the project (the ring buffer the consumer's live worker grew out of, against `ArrayPool` rentals):

| KeySize | RingBuffer, single thread | ArrayPool, single thread | RingBuffer, SPSC via `Channel` | ArrayPool, SPSC via `Channel` |
|---:|---:|---:|---:|---:|
| 16 | **2.648 µs** | 4.770 µs | 131.704 µs (2216 B) | 222.862 µs (2303 B) |
| 64 | **2.902 µs** | 4.989 µs | 138.005 µs (2216 B) | 241.630 µs (2393 B) |
| 256 | **5.012 µs** | 6.438 µs | 141.648 µs (2216 B) | 229.947 µs (2227 B) |

The managed ingest tail (MessagePack bytes in native memory → zero-copy deserialize → enrich → ring-buffer worker → `AddOrUpdate`) is exercised per PR by the `full-sim` config of the perf tripwire rather than by a BenchmarkDotNet table; see [`perf/README.md`](https://github.com/nanov/prague.net/blob/main/perf/README.md).

## Diagnostic probes

These classes exist to attribute a cost, not to advertise one; their rows explain the numbers above.

**Seed selection by declaration order** — `TimeRangeSeedOrderBenchmarks`, the same three narrowings (a time window and two list keys) declared time-first or list-first, from "Frozen queries — closing the branch", "#97 — the symmetric seed chooser":

| Window | time-first, before #97 | time-first, after | list-first |
|---|---:|---:|---:|
| 1 s | 9,298.6 ns | **140.6 ns** | 141.3 ns |
| 1 min | 10,385.9 ns | **1,380.3 ns** | 1,400.8 ns |
| production (~15%) | 14,628.4 ns | 15,068.9 ns | 15,055.0 ns |
| whole range | 24,947.1 ns | 26,141.9 ns | 26,177.5 ns |

Declaration order no longer decides the seed.

**The comparer hop in bounded pages** — `BoundedComparerProbeBenchmarks`, 100 candidates into a heap of K = 40 then the drain (~600 comparisons), from "stage 3, step 7": `ValueComparer_Sorter` 3.906 µs, `PairComparer_Sorter` 3.861 µs, then one resolver-chain link 4.992 µs, two 7.435 µs, three 9.612 µs — ≈2–4 ns per link per comparison, which is what the frozen and (later) the eager bounded containers removed by carrying the user comparer as a struct type parameter. The direct-comparer arm added later reads 1.913 µs against the sorter arm's 3.784 µs ("closing the branch", R0).

**The codegen ceiling for shape A** — `FrozenCodegenCeilingBenchmarks`, the real pipeline against a hand-written straight line of the same plan, from "closing the branch", "R1b — the comparer carried into the bounded container" (load 5.61 → 1.82): `ShapeA_Frozen` 4,557.7 ns, `ShapeA_Level2` (real container, sorter comparer) 3,735.3 ns, `ShapeA_Level2_DirectComparer` 2,376.7 ns, `ShapeA_Level1` (straight line) 1,672.0 ns. The frozen pipeline is at the practical floor of its type-erased design; the rest is a generator-emitted executor per call site.

**Not yet in the log.** `BitHelperScanBenchmarks`, `BTreeChurnBenchmarks`, `CacheIndexMaintenanceBenchmarks`, `CompositeKeyIndexAllocationBenchmarks`, `CompoundIndexBenchmarks`, `ConcurrentBenchmarks`, `ConcurrentDictionaryComparisonBenchmarks`, `ConcurrentSortedListBenchmarks`, `MemoryAllocationBenchmarks`, `PooledBTreeVsSkipListBenchmarks`, `PooledSetBenchmarks`, `ProfileBenchmark`, `TimeRangeSeedProbeBenchmarks`, `TimeRangeWriteBenchmarks` and `ValueDictionaryComparerBenchmarks` have no recorded table; `SortedArraySetBenchmarks` has only the `# v0.0.9` table (`SortedArraySet` vs `ImmutableHashSet` at 10 / 100 / 1,000 elements).

## Reproduce

One category per run, in-process, nothing else on the machine, and write down the load average next to the numbers:

```bash
dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories <category>
```

Category names are the `[BenchmarkCategory]` strings in `benchmarks/Prague.Benchmarks/*.cs` (for example `Unique`, `ListList`, `Or`, `SortBounded_ListList`, `JoinOne`, `Fk_ManyToOne`). A `--quick [--target <category>]` mode runs a short in-process job over shapes A and B plus the target and prints one line per method; it steers work, but a keep-or-kill decision still takes one full single-category run. Append new tables to `RESULTS.MD` under a header that names what changed and the load the machine carried.

# The TimeRange index — the seed-walk spike

> **Tracker:** [#83](https://github.com/nanov/prague.net/issues/83). **Branch:** `spike/83-timerange` off `poc/prepared-query` (`4421db2`).
> **Deliverable:** a number and a recommendation, not the index.
> **Machine:** Apple M4 Pro, 12 cores, 24 GB, macOS (Darwin 25.6.0), .NET 9, `--inProcess`, one category per run. The 1-minute load is recorded above each table.

## What was asked

#83 proposes replacing the B+tree behind `LastUpdatedIndex<TKey>` with a ring of coarse time buckets, so
that "everything since T" is answered as a union of whole key sets instead of a descent plus a leaf-chain
walk. The groundwork comment on the issue had already established that the **probe** side needs no change
(one `TryGetLastUpdated` and two compares, O(1)) and that the whole candidate win is on the **seed**. What
was unknown, and what this spike measures, is whether the seed is where the time goes at all.

## What was built

Nothing in `src/`. Three artefacts, all in the benchmark and test projects:

- `benchmarks/Prague.Benchmarks/TimeRangeSeedProbeBenchmarks.cs` — category `TimeRangeSeedProbe`. The
  current seed path in isolation against a hand-built bucket ring, on two fixtures × four windows.
- `benchmarks/Prague.Benchmarks/TimeRangeWriteBenchmarks.cs` — category `TimeRangeWrite`. The ingestion
  bar: `LastUpdatedIndex` against `SpikeRingLastUpdatedIndex`, which is the same index with the
  `CacheRangeIndex` swapped for a ring and nothing else changed.
- `benchmarks/Prague.Benchmarks/TimeRangeSeedOrderBenchmarks.cs` — category `TimeRangeSeedOrder`. The
  end-to-end cost of the finding in §3.
- `tests/Prague.Core.Tests/Prepared/TimeRangeSeedChoiceProbeTests.cs` — the `Explain()` seed decisions,
  pinned.

### The fixtures

| | Shape A (`ListLastUpdated`) | Shape B (`TimeWindowListListSortBoundedJoinTwo`) |
|---|---|---|
| rows | 100 000 | 10 000 |
| timestamps | dense ascending, 1 ms apart | bijective shuffle over one hour |
| span | 100 s | 3 600 s |
| density | 1 key/ms | 1 key / 360 ms |
| other narrowers | one list bucket, 1 000 rows | two list buckets, 3 334 and 2 000 rows |
| production window | newest half — 50 000 keys | newest ~15 % — 1 500 keys |
| ring, fine | 1 s → 100 buckets of 1 000 | 1 min → 60 buckets of ~167 |
| ring, coarse | 1 min → 2 buckets | 1 h → 1 bucket of 10 000 |

Both fixtures are the ones the frozen rows already use, rebuilt standalone so the seed can be called
without a query around it. `1 h` is omitted as a window: on shape A the whole span is 100 s and on shape B
it is exactly one hour, so on both fixtures `1 h` and `whole range` are the same query. `All` is `T = 0`,
which is also the "T before the horizon" case — the ring answers it from every bucket with no boundary
filter, which is the cheap half of what a fold set would do.

### How the ring was built

A flat array of lazily created `PooledSet<int, DefaultKeyComparer<int>>`, indexed by
`(ts - base) / granularity`; a span with no key allocates no bucket. The seed is the union of every bucket
newer than `T` through the bulk `PooledSet.CopyKeysTo` — one generation read, one acquire of `LastIndex`,
plain loads over the slot array — plus an exact `TryGetLastUpdated` filter on the one bucket that contains
`T`, plus a `ValueSet` dedupe.

Three things about that construction are load-bearing for reading the tables:

1. **The dedupe is not optional.** A writer's Remove-from-old-bucket and Add-to-new-bucket are not atomic
   as a pair, so a reader walking the union can see the same key in two buckets. There is no cheaper
   escape: filtering *every* bucket by "the key's authoritative timestamp maps back to this bucket" does
   not help, because the reader's two timestamp reads happen at different instants — a key can pass the
   test in the old bucket (store not yet updated) and again in the new one. A hash set over the union is
   the only correct answer, and the tables measure it both cold and pre-sized from the buckets' own
   exact counts.
2. **Each bucket copy takes its own `ReaderGate` pin.** A real ring would hoist one pin over the whole
   union. That overhead is bounded by the bucket count and lands only on the wide windows, where the ring
   is already losing — it flatters nothing in the ring's favour.
3. **No lock is taken on the read path.** If a ring adopted `CacheKeySetIndex`'s concurrency model — the
   only one `PooledSet` supports without narrowing the writer contract — every bucket copy would take one
   uncontended `Monitor` round-trip on top. See §4.

## 1. The seed, measured

`dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories TimeRangeSeedProbe`
Load before the run `23:26 up 1 day, 9:12, 2 users, load averages: 2.55 3.21 3.43`; after
`23:46 up 1 day, 9:32, 2 users, load averages: 2.98 2.82 2.86`. `TreeSeed` is the baseline of each
(Shape, Window) group — a ratio above 1.00 means **slower than the seed that ships today**.

| Method                 | Shape | Window     | Mean           | Error         | StdDev        | Ratio  | RatioSD | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |---------------:|--------------:|--------------:|-------:|--------:|----------:|------------:|
| TreeSeed               | A     | Second     |   1,331.141 ns |     9.3259 ns |     8.7235 ns |   1.00 |    0.01 |         - |          NA |
| TreeWalk               | A     | Second     |     761.918 ns |     2.2469 ns |     1.9919 ns |   0.57 |    0.00 |         - |          NA |
| RingFineCopy           | A     | Second     |   1,366.897 ns |     7.4032 ns |     6.9249 ns |   1.03 |    0.01 |         - |          NA |
| RingFineFilter         | A     | Second     |   2,634.670 ns |    12.6194 ns |    11.8042 ns |   1.98 |    0.02 |         - |          NA |
| RingFineDedupe         | A     | Second     |   7,098.933 ns |    22.4089 ns |    20.9613 ns |   5.33 |    0.04 |         - |          NA |
| RingFineDedupePresized | A     | Second     |   6,069.421 ns |    20.6126 ns |    19.2810 ns |   4.56 |    0.03 |         - |          NA |
| RingCoarseCopy         | A     | Second     |  29,554.544 ns |    91.8696 ns |    85.9349 ns |  22.20 |    0.15 |         - |          NA |
| RingCoarseDedupe       | A     | Second     |  92,343.049 ns |   331.1052 ns |   276.4878 ns |  69.37 |    0.49 |       1 B |          NA |
| TreeSeed               | A     | Minute     |  80,192.140 ns |   170.4173 ns |   159.4084 ns |   1.00 |    0.00 |       1 B |        1.00 |
| TreeWalk               | A     | Minute     |  51,062.195 ns |   168.0201 ns |   157.1661 ns |   0.64 |    0.00 |         - |        0.00 |
| RingFineCopy           | A     | Minute     |  43,625.269 ns |   308.9624 ns |   289.0036 ns |   0.54 |    0.00 |         - |        0.00 |
| RingFineFilter         | A     | Minute     |  45,726.933 ns |   241.2163 ns |   225.6339 ns |   0.57 |    0.00 |         - |        0.00 |
| RingFineDedupe         | A     | Minute     | 349,344.324 ns |   942.4382 ns |   881.5572 ns |   4.36 |    0.01 |       3 B |        3.00 |
| RingFineDedupePresized | A     | Minute     | 336,731.975 ns |   778.1740 ns |   727.9044 ns |   4.20 |    0.01 |       3 B |        3.00 |
| RingCoarseCopy         | A     | Minute     |  72,522.661 ns |    97.0236 ns |    90.7560 ns |   0.90 |    0.00 |       1 B |        1.00 |
| RingCoarseDedupe       | A     | Minute     | 519,715.369 ns |   729.4294 ns |   646.6201 ns |   6.48 |    0.01 |       6 B |        6.00 |
| TreeSeed               | A     | Production |  64,989.344 ns |   265.7305 ns |   235.5631 ns |   1.00 |    0.00 |       1 B |        1.00 |
| TreeWalk               | A     | Production |  42,729.541 ns |   200.2096 ns |   167.1841 ns |   0.66 |    0.00 |         - |        0.00 |
| RingFineCopy           | A     | Production |  36,307.138 ns |   375.9984 ns |   351.7091 ns |   0.56 |    0.01 |         - |        0.00 |
| RingFineFilter         | A     | Production |  38,469.514 ns |   298.2508 ns |   264.3915 ns |   0.59 |    0.00 |         - |        0.00 |
| RingFineDedupe         | A     | Production | 306,709.765 ns | 1,550.1554 ns | 1,374.1724 ns |   4.72 |    0.03 |       3 B |        3.00 |
| RingFineDedupePresized | A     | Production | 209,387.326 ns |   612.8390 ns |   543.2658 ns |   3.22 |    0.01 |       1 B |        1.00 |
| RingCoarseCopy         | A     | Production |  72,223.726 ns |   136.4156 ns |   120.9289 ns |   1.11 |    0.00 |       1 B |        1.00 |
| RingCoarseDedupe       | A     | Production | 482,373.840 ns | 2,812.9427 ns | 2,631.2283 ns |   7.42 |    0.05 |       3 B |        3.00 |
| TreeSeed               | A     | All        | 139,758.552 ns |   326.5174 ns |   289.4491 ns |   1.00 |    0.00 |       1 B |        1.00 |
| TreeWalk               | A     | All        |  82,853.628 ns |   277.5965 ns |   259.6639 ns |   0.59 |    0.00 |       1 B |        1.00 |
| RingFineCopy           | A     | All        |  73,387.369 ns |   247.3735 ns |   219.2902 ns |   0.53 |    0.00 |       1 B |        1.00 |
| RingFineFilter         | A     | All        |  73,413.121 ns |   375.4838 ns |   351.2278 ns |   0.53 |    0.00 |       1 B |        1.00 |
| RingFineDedupe         | A     | All        | 618,756.481 ns | 1,703.4796 ns | 1,510.0903 ns |   4.43 |    0.01 |       6 B |        6.00 |
| RingFineDedupePresized | A     | All        | 403,300.113 ns | 2,038.8170 ns | 1,907.1107 ns |   2.89 |    0.01 |       3 B |        3.00 |
| RingCoarseCopy         | A     | All        |  72,362.443 ns |    72.6414 ns |    60.6589 ns |   0.52 |    0.00 |       1 B |        1.00 |
| RingCoarseDedupe       | A     | All        | 619,427.363 ns | 2,157.2587 ns | 2,017.9012 ns |   4.43 |    0.02 |       6 B |        6.00 |
| TreeSeed               | B     | Second     |      27.032 ns |     0.1835 ns |     0.1716 ns |   1.00 |    0.01 |         - |          NA |
| TreeWalk               | B     | Second     |       8.587 ns |     0.0793 ns |     0.0662 ns |   0.32 |    0.00 |         - |          NA |
| RingFineCopy           | B     | Second     |     121.470 ns |     0.4234 ns |     0.3961 ns |   4.49 |    0.03 |         - |          NA |
| RingFineFilter         | B     | Second     |     260.242 ns |     0.8699 ns |     0.8137 ns |   9.63 |    0.07 |         - |          NA |
| RingFineDedupe         | B     | Second     |     283.388 ns |     4.3454 ns |     4.0647 ns |  10.48 |    0.16 |         - |          NA |
| RingFineDedupePresized | B     | Second     |     316.752 ns |     1.1930 ns |     1.1159 ns |  11.72 |    0.08 |         - |          NA |
| RingCoarseCopy         | B     | Second     |   7,385.172 ns |    23.4411 ns |    21.9268 ns | 273.21 |    1.85 |         - |          NA |
| RingCoarseDedupe       | B     | Second     |  21,590.800 ns |    80.5725 ns |    75.3675 ns | 798.74 |    5.59 |         - |          NA |
| TreeSeed               | B     | Minute     |     225.637 ns |     0.6427 ns |     0.6012 ns |   1.00 |    0.00 |         - |          NA |
| TreeWalk               | B     | Minute     |     121.457 ns |     0.3849 ns |     0.3601 ns |   0.54 |    0.00 |         - |          NA |
| RingFineCopy           | B     | Minute     |     121.432 ns |     0.3468 ns |     0.2896 ns |   0.54 |    0.00 |         - |          NA |
| RingFineFilter         | B     | Minute     |     301.282 ns |     1.5078 ns |     1.3366 ns |   1.34 |    0.01 |         - |          NA |
| RingFineDedupe         | B     | Minute     |     972.904 ns |     6.0947 ns |     5.7010 ns |   4.31 |    0.03 |         - |          NA |
| RingFineDedupePresized | B     | Minute     |     742.024 ns |     2.8565 ns |     2.6720 ns |   3.29 |    0.01 |         - |          NA |
| RingCoarseCopy         | B     | Minute     |   7,389.528 ns |    18.1640 ns |    16.9907 ns |  32.75 |    0.11 |         - |          NA |
| RingCoarseDedupe       | B     | Minute     |  23,128.648 ns |   103.8871 ns |    92.0932 ns | 102.50 |    0.47 |         - |          NA |
| TreeSeed               | B     | Production |   2,069.013 ns |     7.8083 ns |     7.3039 ns |   1.00 |    0.00 |         - |          NA |
| TreeWalk               | B     | Production |   1,180.289 ns |     9.8149 ns |     8.7007 ns |   0.57 |    0.00 |         - |          NA |
| RingFineCopy           | B     | Production |   1,126.002 ns |     8.3394 ns |     7.8007 ns |   0.54 |    0.00 |         - |          NA |
| RingFineFilter         | B     | Production |   1,272.407 ns |    11.8366 ns |     9.8841 ns |   0.61 |    0.01 |         - |          NA |
| RingFineDedupe         | B     | Production |   8,397.064 ns |    36.9562 ns |    34.5688 ns |   4.06 |    0.02 |         - |          NA |
| RingFineDedupePresized | B     | Production |   6,042.828 ns |    24.7128 ns |    23.1164 ns |   2.92 |    0.01 |         - |          NA |
| RingCoarseCopy         | B     | Production |   7,476.702 ns |    25.9180 ns |    21.6427 ns |   3.61 |    0.02 |         - |          NA |
| RingCoarseDedupe       | B     | Production |  29,885.196 ns |    87.3136 ns |    77.4013 ns |  14.44 |    0.06 |         - |          NA |
| TreeSeed               | B     | All        |  14,810.067 ns |    63.1645 ns |    59.0842 ns |   1.00 |    0.01 |         - |          NA |
| TreeWalk               | B     | All        |   8,303.349 ns |    27.0602 ns |    23.9881 ns |   0.56 |    0.00 |         - |          NA |
| RingFineCopy           | B     | All        |   8,347.714 ns |    10.1389 ns |     7.9158 ns |   0.56 |    0.00 |         - |          NA |
| RingFineFilter         | B     | All        |   8,363.987 ns |    18.0194 ns |    15.0470 ns |   0.56 |    0.00 |         - |          NA |
| RingFineDedupe         | B     | All        |  60,322.739 ns |   177.7638 ns |   166.2804 ns |   4.07 |    0.02 |         - |          NA |
| RingFineDedupePresized | B     | All        |  52,278.703 ns |   168.4759 ns |   157.5925 ns |   3.53 |    0.02 |         - |          NA |
| RingCoarseCopy         | B     | All        |   7,590.184 ns |    64.9830 ns |    60.7852 ns |   0.51 |    0.00 |         - |          NA |
| RingCoarseDedupe       | B     | All        |  60,818.034 ns |   269.5224 ns |   238.9246 ns |   4.11 |    0.02 |         - |          NA |

### The cost split, per key

Taken off shape A, whose 100 000-key `All` window divides cleanly, and off the 1 000-key boundary bucket
of its `Second` window.

| stage | ns/key | how it was derived |
|---|---:|---|
| B+tree descent + leaf-chain walk, nothing written | 0.83 | `TreeWalk` A/All ÷ 100 000 |
| the `SeedKeys` sink on top of it | 0.57 | (`TreeSeed` − `TreeWalk`) A/All ÷ 100 000 |
| **what the seed costs today** | **1.40** | `TreeSeed` A/All ÷ 100 000 |
| ring: `PooledSet` slot scan **including** the same sink | 0.73 | `RingFineCopy` A/All ÷ 100 000 |
| ring: exact boundary filter, per key of the boundary bucket | 1.27 | (`RingFineFilter` − `RingFineCopy`) A/Second ÷ 1 000 |
| ring: `ValueSet` dedupe, pre-sized from the bucket counts | 3.30 | (`RingFineDedupePresized` − `RingFineFilter`) A/All ÷ 100 000 |
| ring: `ValueSet` dedupe, grown from 47 slots | 5.45 | (`RingFineDedupe` − `RingFineFilter`) A/All ÷ 100 000 |

**The bulk copy does win the part it was supposed to win.** `CopyKeysTo` over a `PooledSet`'s slot array
costs 0.73 ns/key *with* the sink, against 1.40 ns/key for the tree walk *with* the same sink — a clean
1.9×, and it beats even the tree's bare walk (0.83) because a flat slot array is a better prefetch target
than a leaf chain. That 1.9× is the ring's ceiling, and it is reached only by an answer that is wrong
(no boundary filter, no dedupe).

**Then the dedupe eats it and four more copies of it.** At 3.30 ns/key pre-sized, the dedupe alone is
2.4× the entire cost of today's seed. There is no version of the union that skips it (§How the ring was
built, 1), so the honest ring is 2.9–3.5× *slower* than the tree on the wide windows and 4.6–11.7× slower
on the narrow ones.

**Granularity has to track the window, and can't.** The ring is only ahead while the boundary bucket is
not much bigger than the window itself. Shape B's `Second` window is 3 keys; a 1-minute bucket holds 167,
so even the unfiltered copy loses 4.5×, and a 1-hour bucket loses 273×. Shape A's `Second` window sits in
a 1-second ring and only breaks even on copy (1.03) — and the issue's own motivation is that "T is usually
recent", i.e. the windows that matter are the ones where this is worst.

## 2. Ingestion

`dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories TimeRangeWrite`
Load before `23:47 up 1 day, 9:33, 2 users, load averages: 4.07 3.10 2.96`; after
`23:47 up 1 day, 9:33, 2 users, load averages: 3.66 3.10 2.97`. 100 000 keys per operation; `Churn` is
100 000 Remove + Add pairs, so 200 000 index mutations.

| Method              | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0      | Allocated | Alloc Ratio |
|-------------------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|----------:|------------:|
| TreeAppendAscending |  6.875 ms | 0.1324 ms | 0.3068 ms |  6.864 ms |  1.00 |    0.06 | 1000.0000 |  14.67 MB |        1.00 |
| RingAppendAscending |  7.965 ms | 0.1395 ms | 0.1305 ms |  7.943 ms |  1.16 |    0.05 | 1000.0000 |  15.88 MB |        1.08 |
| TreeChurn           |  9.469 ms | 0.1886 ms | 0.2386 ms |  9.468 ms |  1.38 |    0.07 |         - |   4.78 MB |        0.33 |
| RingChurn           | 10.097 ms | 0.2016 ms | 0.5817 ms |  9.859 ms |  1.47 |    0.11 | 1000.0000 |   8.54 MB |        0.58 |
| TreeBackwards       | 12.866 ms | 0.2209 ms | 0.2455 ms | 12.803 ms |  1.88 |    0.09 | 1000.0000 |   14.6 MB |        0.99 |
| RingBackwards       |  8.019 ms | 0.1572 ms | 0.2835 ms |  7.962 ms |  1.17 |    0.06 | 1000.0000 |  15.88 MB |        1.08 |

Per mutation: ascending append 68.8 ns (tree) against 79.7 ns (ring), **+15.9 %**; churn 47.3 against
50.5 ns, **+6.6 %**; descending insert 128.7 against 80.2 ns, **−37.7 %**.

The ring is faster exactly where the tree gives up its fast path and slower everywhere else — including
the ascending append, which is the Kafka feed's own shape and the bar the issue sets. A 16 % ingestion
regression on the dominant write path is not "within noise". The ring also allocates more in every
shape, because a bucket is a whole hash set where a leaf is shared.

## 3. The finding the spike did not go looking for

Before measuring a replacement for the seed it was worth checking whether the last-updated step is the
seed at all. `tests/Prague.Core.Tests/Prepared/TimeRangeSeedChoiceProbeTests.cs` prints the `Explain()`
decision for both shapes at all four windows:

```
A            second     rows=   10 estimate=   451 listBucket=1000              | last seed: step 1 LastUpdatedAfter (signal 451)
A            minute     rows=  600 estimate= 46001 listBucket=1000              | last seed: step 0 ListEq (signal 1000)
A            production rows=  500 estimate= 38238 listBucket=1000              | last seed: step 0 ListEq (signal 1000)
A            all        rows= 1000 estimate=100000 listBucket=1000              | last seed: step 0 ListEq (signal 1000)
B time-first second     rows=    0 estimate=     2 keyA=3334 keyB=2000          | last seed: step 2 ListEq (signal 2000)
B time-first minute     rows=   12 estimate=   166 keyA=3334 keyB=2000          | last seed: step 2 ListEq (signal 2000)
B time-first production rows=   99 estimate=  1231 keyA=3334 keyB=2000          | last seed: step 2 ListEq (signal 2000)
B time-first all        rows=  667 estimate= 10000 keyA=3334 keyB=2000          | last seed: step 2 ListEq (signal 2000)
B list-first second     rows=    0 estimate=     2 keyA=3334 keyB=2000          | last seed: step 2 LastUpdatedAfter
B list-first minute     rows=   12 estimate=   166 keyA=3334 keyB=2000          | last seed: step 2 LastUpdatedAfter
B list-first production rows=   99 estimate=  1231 keyA=3334 keyB=2000          | last seed: step 1 ListEq (signal 2000)
B list-first all        rows=  667 estimate= 10000 keyA=3334 keyB=2000          | last seed: step 1 ListEq (signal 2000)
```

Shape B's production query cannot seed on its time window at any width — not even with an estimate of 2
against a list bucket of 2 000. The same three steps, declared list-first, seed on it at both narrow
windows. Nothing about the index changed.

The cause is `PipelineExecutor.cs:377-391`:

```csharp
if (step.ExactSignal) {
  if (!exact || value < signal) { seed = s; signal = value; exact = true; }
} else if (exact ? 2L * value < signal : value < signal) { seed = s; signal = value; }
```

`!exact` makes the **first** exact signal displace an inexact incumbent unconditionally, however much
better that incumbent was; only once an exact signal is in hand does the 2× rule let an estimate win the
seed back. So a plan that declares its range or last-updated step before its first list throws that
step's estimate away. Shape A declares the list first and the rule works; shape B declares the time
window first and it does not.

What that costs, end to end, on shape B's own fixture and arguments:

`dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories TimeRangeSeedOrder`
Load before `23:47 up 1 day, 9:33, 2 users, load averages: 3.56 3.10 2.97`; after
`23:50 up 1 day, 9:36, 2 users, load averages: 2.18 2.73 2.84`.

| Method    | Window     | Mean        | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------- |----------- |------------:|----------:|----------:|------:|--------:|----------:|------------:|
| TimeFirst | Second     |  9,352.4 ns |  66.77 ns |  62.46 ns |  1.00 |    0.01 |         - |          NA |
| ListFirst | Second     |    137.2 ns |   1.24 ns |   1.16 ns |  0.01 |    0.00 |         - |          NA |
| TimeFirst | Minute     | 10,343.8 ns | 132.69 ns | 124.11 ns |  1.00 |    0.02 |         - |          NA |
| ListFirst | Minute     |  1,410.2 ns |   3.68 ns |   2.87 ns |  0.14 |    0.00 |         - |          NA |
| TimeFirst | Production | 14,668.0 ns | 100.28 ns |  93.80 ns |  1.00 |    0.01 |         - |          NA |
| ListFirst | Production | 14,952.3 ns | 149.53 ns | 132.56 ns |  1.02 |    0.01 |         - |          NA |
| TimeFirst | All        | 24,974.4 ns | 152.69 ns | 135.36 ns |  1.00 |    0.01 |         - |          NA |
| ListFirst | All        | 25,343.7 ns | 200.47 ns | 177.71 ns |  1.01 |    0.01 |         - |          NA |

**68× on the one-second window and 7.3× on the one-minute window, from moving two `UseIndex` calls.** On
the wide windows the two orders agree to within 2 %, which is the control: the order only matters when
the estimate deserved to win.

## 4. The open questions, answered

**Granularity and horizon defaults.** The ring is ahead only while the boundary bucket is no coarser than
the window. Shape B's 1-second window against a 1-minute bucket already loses 4.5× on the copy alone;
against a 1-hour bucket, 273×. Since #83's premise is that T is usually recent, the granularity would
have to be about 1 s — 86 400 buckets for a 24 h horizon — which is precisely the configuration the
memory answer below prices out of reach.

**T before the horizon: fold set or cold tree.** The `All` rows are that case. A fold set copies at
0.72 ns/key (`RingCoarseCopy` A/All, 72.4 µs for 100 000 keys — one big `PooledSet`, which is what a fold
set is), but it still enters the same union and so still pays the 3.30 ns/key dedupe: 4.0 ns/key against
the tree's 1.40. A cold **tree** for the tail costs exactly what today costs, by construction. So the
hybrid's cold half can only match the status quo, never beat it, and its warm half is the part that
loses. There is no configuration in which the fold set is the reason to build this.

**Memory per bucket.** Laziness works for the empty case — an unoccupied span is one null reference, 8 B.
An *occupied* bucket is a whole `PooledSet`: `DefaultInitialCapacity = 59` (`PooledSet.cs:314`) rounded to
a prime and rented from `ArrayPool`, so ≈ 64 slots of `HashSlot<int>` plus a 64-entry bucket array plus a
64-entry free list — on the order of 1.5 KB of rented arrays, plus the `PooledSet` and `Tables` objects,
for a bucket that may hold one key. At the ~1 s granularity the first answer requires, a continuously fed
24 h horizon is ~86 400 live buckets: roughly 130 MB of rented arrays behind a 691 KB ring array, to hold
an index that is a few MB of B+tree today. `PooledSet` renting its first generation eagerly (the
groundwork's note) is what makes the per-bucket floor this high. These are derived from the constant, not
measured.

**Can the ring be exact (`ExactSignal = true`)?** Yes, but only by running the boundary filter inside
`Signal()`, at 1.27 ns/key over the whole boundary bucket — paying most of the seed's work *before* the
chooser has decided whether to seed there, on every execution of every plan containing a time step,
including all the ones where another step wins. That is the "anything added to a query path is decided at
build time, not per row" invariant, broken. The cheap alternative — report the sum of the touched
buckets' exact `Count`s — is an **upper** bound, and the numbers in §3 show an upper bound is the wrong
direction: shape A's `Second` window seeds on time today *because* the B+tree estimate under-reports
(451 against a true 1 000); a ring would have reported 2 000 and lost the seed it deserved. A bucketed
index would need its bound tightened, not merely made cheaper.

**Concurrency, had it been built.** `PooledSet` is strictly single-writer, so a ring takes either
`CacheKeySetIndex`'s model — which locks on the **read** path too (`InMemoryDataCache.cs:700-719`) and so
adds one uncontended `Monitor` round-trip per bucket touched: at 1 s granularity that is one per second of
window, ~15–20 ns each, so ~0.8 µs on shape A's production window and ~2 µs on its whole-range window, on
top of numbers that already lose — or a narrowing of the writer contract, which is a regression against
the multi-writer `ConcurrentCacheStore` pair that ships today. Neither is attractive, and neither needed
deciding once the seed numbers came in.

## 5. Recommendation

**(c) Do not build it. The tree walk is not the cost.**

Against #83's own keep/kill bar — *≥ 5× on recent windows, ingestion within noise*:

| bar | measured | verdict |
|---|---|---|
| ≥ 5× on recent windows | shape A `Second`: **4.56× slower**; shape B `Second`: **11.72× slower** | fails, and in the wrong direction |
| best case anywhere | 1.9× faster, but only for a union with no boundary filter and no dedupe — a wrong answer | fails |
| best correct case anywhere | shape A `Production`: **3.22× slower**; shape B `All`: **3.53× slower** | fails |
| ingestion within noise | **+15.9 %** on ascending append, +6.6 % on churn | fails |

The one thing the ring genuinely does better — bulk `CopyKeysTo` over a flat slot array instead of a
B+tree leaf chain, 0.73 against 1.40 ns/key — is worth 1.9× and is wiped out several times over by the
dedupe the structure forces (3.30 ns/key) and the boundary filter it needs to hide its own granularity
(1.27 ns/key over the boundary bucket). Ingestion then pays 16 % for the privilege. Every number in §1
and §2 says the B+tree walk is already close to the floor for this access pattern, and that the sink and
the set-membership bookkeeping — not the traversal — are where the time goes.

**What to do instead, and it is worth more than the ring would have been.** The narrow-window win #83 is
chasing is real, it is 68× on shape B, and it is available today with no new index: fix the seed
chooser's asymmetry at `PipelineExecutor.cs:377-391` so that an arriving exact signal is compared against
an inexact incumbent under the same 2× rule that already governs the reverse case, instead of displacing
it unconditionally. That is one arm of one branch, no new per-execution state, no per-row cost, and it
makes seed selection independent of the order the caller happened to declare the narrowings in — which is
the surprise a caller is least equipped to predict. It should be its own issue, and it should be measured
against the same `TimeRangeSeedOrder` row.

Second, and smaller: the B+tree estimate under-reports by 20–55 % on these shapes (451 for 1 000;
38 238 for 50 000; 1 231 for ~1 500). That asymmetry currently helps — it is why shape A seeds on time at
one second — but it is luck, not design. If the chooser is touched, the estimate's bias is worth
characterising at the same time.

**What to do with the spike's code.** `TimeRangeSeedProbeBenchmarks` and `TimeRangeWriteBenchmarks`
answered their question and have no ongoing job; keeping them costs two categories in the sweep and pins
a hand-built structure that nothing ships. `TimeRangeSeedOrderBenchmarks` and
`TimeRangeSeedChoiceProbeTests` should survive into whatever issue takes the chooser fix — the first is
its keep/kill row, the second pins the behaviour it changes.

## 6. What this spike did not do

- The ring was never run under a concurrent writer, and rotation past the horizon is never exercised —
  every fixture fits inside one horizon. Both were out of scope once the quiescent numbers lost.
- The ring's read path takes no lock, which flatters it relative to any implementation built on
  `CacheKeySetIndex`'s model (§4). The write path does take one, on both sides.
- Each bucket copy takes its own `ReaderGate` pin where a real ring would hoist one over the union;
  this flatters the *tree*, bounded by the bucket count, and only on the wide windows.
- Memory per bucket is derived from `PooledSet`'s initial-capacity constant, not measured.
- `Explain()`'s decision line caches the signal from the last execution at which the decision *changed*,
  so a run that keeps the same seed step across two different arguments prints the earlier signal
  (visible in the `B list-first minute` row above, which printed `signal 2` while the estimate was 166).
  The seed step is right; only the printed number is stale. Worth a look if anyone relies on that line.

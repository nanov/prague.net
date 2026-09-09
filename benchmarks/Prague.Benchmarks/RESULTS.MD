# v0.0.9

## Before PooledSet -> ValueSet

BenchmarkDotNet v0.15.8, macOS Sequoia 15.3.1 (24D70) [Darwin 24.3.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host] : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessNoEmitToolchain IterationCount=3 WarmupCount=1

| Method                              | ReaderThreads | DurationMs |    Mean | Rank | Reads | Writes | Duration | Reads/s |        Gen0 |        Gen1 |  Allocated |
|-------------------------------------|---------------|------------|--------:|-----:|------:|-------:|---------:|--------:|------------:|------------:|-----------:|
| ConcurrentReadsWithWriter_Pooled    | 11            | 3000       | 3.006 s |    1 | 19365 |   1704 |  3002 ms |    6451 |  13000.0000 |   6000.0000 |  108.96 MB |
| ConcurrentReadsWithWriter_NotPooled | 11            | 3000       | 3.009 s |    1 | 40059 |   1626 |  3007 ms |   13322 | 566000.0000 | 283000.0000 | 4290.99 MB |

## After PooledSet -> ValueSet

BenchmarkDotNet v0.15.8, macOS Sequoia 15.3.1 (24D70) [Darwin 24.3.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host] : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessNoEmitToolchain IterationCount=3 WarmupCount=1

| Method                              | ReaderThreads | DurationMs |    Mean | Rank | Reads | Writes | Duration | Reads/s |        Gen0 |        Gen1 |  Allocated |
|-------------------------------------|---------------|------------|--------:|-----:|------:|-------:|---------:|--------:|------------:|------------:|-----------:|
| ConcurrentReadsWithWriter_Pooled    | 11            | 3000       | 3.003 s |    1 | 20543 |   1698 |  3003 ms |    6848 |  13000.0000 |   6000.0000 |   98.28 MB |
| ConcurrentReadsWithWriter_NotPooled | 11            | 3000       | 3.008 s |    1 | 43507 |   1644 |  3009 ms |   14459 | 615000.0000 | 307000.0000 | 4648.05 MB |

## Optimizations round 1

| Method                              | ReaderThreads | DurationMs |    Mean | Rank | Reads | Writes | Duration | Reads/s |        Gen0 |        Gen1 |  Allocated |
|-------------------------------------|---------------|------------|--------:|-----:|------:|-------:|---------:|--------:|------------:|------------:|-----------:|
| ConcurrentReadsWithWriter_Pooled    | 11            | 3000       | 3.005 s |    1 | 20235 |   1698 |  3008 ms |    6727 |  10000.0000 |   5000.0000 |   85.58 MB |
| ConcurrentReadsWithWriter_NotPooled | 11            | 3000       | 3.007 s |    1 | 40318 |   1626 |  3006 ms |   13413 | 570000.0000 | 285000.0000 | 4307.35 MB |

## Optimizations Round 2

| Method                              | ReaderThreads | DurationMs |    Mean | Rank | Reads | Writes | Duration | Reads/s |        Gen0 |        Gen1 |      Gen2 |  Allocated |
|-------------------------------------|---------------|------------|--------:|-----:|------:|-------:|---------:|--------:|------------:|------------:|----------:|-----------:|
| ConcurrentReadsWithWriter_Pooled    | 11            | 3000       | 3.006 s |    1 | 21012 |   1692 |  3004 ms |    6995 |  11000.0000 |   5000.0000 |         - |   91.84 MB |
| ConcurrentReadsWithWriter_NotPooled | 11            | 3000       | 3.006 s |    1 | 38532 |   1650 |  3010 ms |   12801 | 546000.0000 | 273000.0000 | 2000.0000 | 4116.59 MB |

# Optimimizations Rounds 3 - Final

| Method                              | ReaderThreads | DurationMs |    Mean | Rank |    Reads | Writes | Duration | Reads/s |        Gen0 |        Gen1 |      Gen2 |    Allocated |
|-------------------------------------|---------------|------------|--------:|-----:|---------:|-------:|---------:|--------:|------------:|------------:|----------:|-------------:|
| ConcurrentReadsWithWriter_Pooled    | 11            | 3000       | 3.055 s |    1 | 29591857 |   1535 |  3047 ms | 9711801 |           - |           - |         - |      90.2 KB |
| ConcurrentReadsWithWriter_NotPooled | 11            | 3000       | 3.045 s |    1 | 24763813 |   1545 |  3045 ms | 8132615 | 607000.0000 | 302000.0000 | 2000.0000 | 4841851.1 KB |

## JoinCacheBenchmark (GameCount = 500; MarketsPerGame = 20; TotalMarkets = GameCount * MarketsPerGame; )

BenchmarkDotNet v0.15.8, macOS Sequoia 15.3.1 (24D70) [Darwin 24.3.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]     : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a
DefaultJob : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a

| Method                      | ReaderThreads |           Mean |         Error |         StdDev |     Ratio |  RatioSD | Rank |       Gen0 |       Gen1 |     Gen2 |    Allocated | Alloc Ratio |
|-----------------------------|---------------|---------------:|--------------:|---------------:|----------:|---------:|-----:|-----------:|-----------:|---------:|-------------:|------------:|
| ReadAllGames                | 16            |       2.486 us |     0.0444 us |      0.0562 us |      1.00 |     0.03 |    1 |     0.9689 |     0.0038 |        - |      7.92 KB |        1.00 |
| ReadGamesWithInfo           | 16            |      11.929 us |     0.0624 us |      0.0553 us |      4.80 |     0.11 |    2 |     0.9613 |          - |        - |      7.95 KB |        1.00 |
| ReadLiveGamesWithMarkets    | 16            |     127.173 us |     0.7243 us |      0.6775 us |     51.17 |     1.13 |    3 |     8.7891 |     0.7324 |        - |     73.08 KB |        9.22 |
| ReadGamesWithMarkets        | 16            |     304.801 us |     1.4629 us |      1.1422 us |    122.64 |     2.67 |    4 |    21.4844 |     3.9063 |        - |    175.98 KB |       22.21 |
| ReadGamesWithInfoAndMarkets | 16            |     307.475 us |     2.1123 us |      1.6492 us |    123.72 |     2.73 |    4 |    21.9727 |     3.9063 |        - |       180 KB |       22.72 |
| SingleGameLookupWithJoins   | 16            |     317.110 us |     5.7514 us |     12.6245 us |    127.59 |     5.73 |    4 |   246.5820 |    16.6016 |        - |   2021.09 KB |      255.13 |
| ConcurrentReadsWithWriter   | 16            | 144,539.594 us | 2,855.4169 us |  8,100.3432 us | 58,157.62 | 3,474.38 |    5 | 37000.0000 | 18750.0000 | 500.0000 | 288016.38 KB |   36,357.10 |
| ConcurrentReadsWithJoins    | 16            | 156,067.842 us | 5,508.1719 us | 16,067.5972 us | 62,796.18 | 6,574.03 |    5 | 37250.0000 | 19000.0000 | 750.0000 | 288026.84 KB |   36,358.42 |


## RingBuffer ArrayPool

// * Summary *

BenchmarkDotNet v0.15.8, macOS Tahoe 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]     : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a
DefaultJob : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a


| Method                        | KeySize | Mean       | Error     | StdDev     | Ratio | RatioSD | Rank | Gen0   | Allocated | Alloc Ratio |
|------------------------------ |-------- |-----------:|----------:|-----------:|------:|--------:|-----:|-------:|----------:|------------:|
| 'RingBuffer single-thread'    | 16      |   2.648 us | 0.0027 us |  0.0021 us |  0.56 |    0.01 |    1 |      - |         - |          NA |
| 'ArrayPool single-thread'     | 16      |   4.770 us | 0.0504 us |  0.0447 us |  1.00 |    0.01 |    2 |      - |         - |          NA |
| 'RingBuffer SPSC via Channel' | 16      | 131.704 us | 2.6277 us |  7.3250 us | 27.61 |    1.55 |    3 | 0.2441 |    2216 B |          NA |
| 'ArrayPool SPSC via Channel'  | 16      | 222.862 us | 4.6507 us | 13.0410 us | 46.73 |    2.75 |    4 | 0.2441 |    2303 B |          NA |
|                               |         |            |           |            |       |         |      |        |           |             |
| 'RingBuffer single-thread'    | 64      |   2.902 us | 0.0161 us |  0.0135 us |  0.58 |    0.00 |    1 |      - |         - |          NA |
| 'ArrayPool single-thread'     | 64      |   4.989 us | 0.0210 us |  0.0175 us |  1.00 |    0.00 |    2 |      - |         - |          NA |
| 'RingBuffer SPSC via Channel' | 64      | 138.005 us | 2.6566 us |  2.4850 us | 27.66 |    0.49 |    3 | 0.2441 |    2216 B |          NA |
| 'ArrayPool SPSC via Channel'  | 64      | 241.630 us | 4.7764 us | 13.0752 us | 48.43 |    2.61 |    4 | 0.2441 |    2393 B |          NA |
|                               |         |            |           |            |       |         |      |        |           |             |
| 'RingBuffer single-thread'    | 256     |   5.012 us | 0.0189 us |  0.0177 us |  0.78 |    0.01 |    1 |      - |         - |          NA |
| 'ArrayPool single-thread'     | 256     |   6.438 us | 0.0499 us |  0.0467 us |  1.00 |    0.01 |    2 |      - |         - |          NA |
| 'RingBuffer SPSC via Channel' | 256     | 141.648 us | 2.5220 us |  2.3591 us | 22.00 |    0.39 |    3 | 0.2441 |    2216 B |          NA |
| 'ArrayPool SPSC via Channel'  | 256     | 229.947 us | 4.5901 us |  4.9113 us | 35.72 |    0.78 |    4 | 0.2441 |    2227 B |          NA |




// * Summary *

BenchmarkDotNet v0.15.8, macOS Tahoe 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]     : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a
DefaultJob : .NET 9.0.11 (9.0.11, 9.0.1125.51716), Arm64 RyuJIT armv8.0-a


| Method                          | Size | Mean           | Error       | StdDev      | Ratio | RatioSD | Rank | Gen0    | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----- |---------------:|------------:|------------:|------:|--------:|-----:|--------:|-------:|----------:|------------:|
| SortedArraySet_Iterate          | 10   |       2.913 ns |   0.0819 ns |   0.1251 ns | 0.006 |    0.00 |    1 |       - |      - |         - |        0.00 |
| ImmutableHashSet_Contains       | 10   |      15.415 ns |   0.2479 ns |   0.2319 ns | 0.032 |    0.00 |    2 |       - |      - |         - |        0.00 |
| ImmutableHashSet_AddDuplicate   | 10   |      18.866 ns |   0.1810 ns |   0.1604 ns | 0.039 |    0.00 |    3 |       - |      - |         - |        0.00 |
| SortedArraySet_AddDuplicate     | 10   |      42.236 ns |   0.4766 ns |   0.4225 ns | 0.088 |    0.00 |    4 |       - |      - |         - |        0.00 |
| SortedArraySet_Contains         | 10   |      48.572 ns |   0.8561 ns |   0.8008 ns | 0.101 |    0.00 |    5 |       - |      - |         - |        0.00 |
| SortedArraySet_AddOneByOne      | 10   |      85.695 ns |   1.6134 ns |   1.5092 ns | 0.179 |    0.00 |    6 |  0.0257 |      - |     216 B |        0.10 |
| ImmutableHashSet_Iterate        | 10   |     120.476 ns |   0.5665 ns |   0.5022 ns | 0.251 |    0.00 |    7 |       - |      - |         - |        0.00 |
| SortedArraySet_RemoveOneByOne   | 10   |     164.352 ns |   1.6701 ns |   1.5622 ns | 0.343 |    0.01 |    8 |  0.0114 |      - |      96 B |        0.04 |
| ImmutableHashSet_RemoveOneByOne | 10   |     287.406 ns |   2.6304 ns |   2.3318 ns | 0.599 |    0.01 |    9 |  0.1779 |      - |    1488 B |        0.65 |
| ImmutableHashSet_AddOneByOne    | 10   |     479.991 ns |   7.4954 ns |   8.6317 ns | 1.000 |    0.02 |   10 |  0.2708 |      - |    2272 B |        1.00 |
|                                 |      |                |             |             |       |         |      |         |        |           |             |
| SortedArraySet_Iterate          | 100  |      46.394 ns |   0.4771 ns |   0.4229 ns | 0.005 |    0.00 |    1 |       - |      - |         - |        0.00 |
| ImmutableHashSet_Contains       | 100  |     331.858 ns |   6.1089 ns |   7.0350 ns | 0.038 |    0.00 |    2 |       - |      - |         - |        0.00 |
| ImmutableHashSet_AddDuplicate   | 100  |     421.835 ns |   8.1013 ns |   9.3294 ns | 0.048 |    0.00 |    3 |       - |      - |         - |        0.00 |
| SortedArraySet_AddDuplicate     | 100  |     895.772 ns |   4.0885 ns |   3.8243 ns | 0.102 |    0.00 |    4 |       - |      - |         - |        0.00 |
| SortedArraySet_Contains         | 100  |     905.198 ns |   2.7544 ns |   2.4417 ns | 0.103 |    0.00 |    4 |       - |      - |         - |        0.00 |
| ImmutableHashSet_Iterate        | 100  |   1,232.048 ns |   4.0126 ns |   3.7534 ns | 0.140 |    0.00 |    5 |       - |      - |         - |        0.00 |
| SortedArraySet_AddOneByOne      | 100  |   1,624.610 ns |  11.6073 ns |   9.6926 ns | 0.185 |    0.00 |    6 |  0.1411 |      - |    1184 B |        0.03 |
| SortedArraySet_RemoveOneByOne   | 100  |   3,067.355 ns |  17.6230 ns |  16.4846 ns | 0.349 |    0.00 |    7 |  0.0534 |      - |     456 B |        0.01 |
| ImmutableHashSet_RemoveOneByOne | 100  |   6,474.842 ns |  28.7141 ns |  26.8592 ns | 0.736 |    0.00 |    8 |  3.7155 | 0.0229 |   31120 B |        0.77 |
| ImmutableHashSet_AddOneByOne    | 100  |   8,796.627 ns |  51.9852 ns |  48.6270 ns | 1.000 |    0.01 |    9 |  4.8370 | 0.0458 |   40584 B |        1.00 |
|                                 |      |                |             |             |       |         |      |         |        |           |             |
| SortedArraySet_Iterate          | 1000 |     512.802 ns |   1.4665 ns |   1.3000 ns | 0.004 |    0.00 |    1 |       - |      - |         - |       0.000 |
| ImmutableHashSet_Contains       | 1000 |   6,606.469 ns |  70.9393 ns |  66.3567 ns | 0.047 |    0.00 |    2 |       - |      - |         - |       0.000 |
| ImmutableHashSet_AddDuplicate   | 1000 |   7,410.626 ns |  45.7221 ns |  42.7684 ns | 0.052 |    0.00 |    3 |       - |      - |         - |       0.000 |
| ImmutableHashSet_Iterate        | 1000 |  13,487.104 ns |  62.5895 ns |  55.4840 ns | 0.095 |    0.00 |    4 |       - |      - |         - |       0.000 |
| SortedArraySet_AddDuplicate     | 1000 |  13,704.674 ns |  51.0595 ns |  47.7611 ns | 0.097 |    0.00 |    4 |       - |      - |         - |       0.000 |
| SortedArraySet_Contains         | 1000 |  13,709.137 ns | 133.9130 ns | 125.2623 ns | 0.097 |    0.00 |    4 |       - |      - |         - |       0.000 |
| SortedArraySet_AddOneByOne      | 1000 |  29,819.597 ns | 284.1037 ns | 265.7508 ns | 0.211 |    0.00 |    5 |  0.9766 |      - |    8424 B |       0.014 |
| SortedArraySet_RemoveOneByOne   | 1000 |  62,816.993 ns | 436.0578 ns | 364.1280 ns | 0.444 |    0.00 |    6 |  0.3662 |      - |    4056 B |       0.007 |
| ImmutableHashSet_RemoveOneByOne | 1000 | 116,827.809 ns | 887.5783 ns | 830.2413 ns | 0.826 |    0.01 |    7 | 59.9365 | 3.5400 |  501824 B |       0.842 |
| ImmutableHashSet_AddOneByOne    | 1000 | 141,414.297 ns | 717.2825 ns | 670.9465 ns | 1.000 |    0.01 |    8 | 71.0449 | 6.3477 |  595848 B |       1.000 |

# Sorted paging — `Sort` vs `SortBounded`

`SortBounded(cmp)` opts a sorted query into the bounded top-K plan for the paged `Execute*`
terminals; `Sort(cmp)` is the classic materialize-and-full-sort. The choice is the caller's, because
the plans order comparer-equal rows differently — see
`www/docs/articles/core-concepts/sorted-paging.md`.

Apple M4 Pro, .NET 9.0.19 (osx-arm64), `--job short` (3 iterations), `[MemoryDiagnoser]`.
**Both plans measured in one process**, so every pair below is directly comparable. `*_ClassicCore*`
rows call the classic core directly; the others go through the public terminal.

Read the means as order-of-magnitude: ShortRun on a laptop gives error bars of 5-50% of the mean on
the larger rows. The `Allocated` column is deterministic and is the point of the table.

## N = 10,000

| Shape | Method | Mean | Allocated |
|---|---|---:|---:|
| page of 20 | `Sorted_ExecutePooled` | **97.6 µs** | **-** |
| | `Sorted_ClassicCore_Pooled` | 465.7 µs | 64 B |
| page of 200 | `Sorted_ExecutePooled` | **193.9 µs** | **-** |
| | `Sorted_ClassicCore_Pooled` | 449.2 µs | 128 B |
| page of 20 at the end | `Sorted_DeepPage_ExecutePooled` | **232.4 µs** | **-** |
| | `Sorted_DeepPage_ClassicCore` | 494.3 µs | 64 B |
| half the result | `Sorted_HalfPage_ExecutePooled` | 813.2 µs | - |
| | `Sorted_HalfPage_ClassicCore` | **506.2 µs** | 64 B |
| the whole result | `Sorted_FullPage_ExecutePooled` | 1,175.0 µs | 768 B |
| | `Sorted_FullPage_ClassicCore` | **440.9 µs** | 64 B |
| joined, page of 20 | `SortedInnerJoin_ExecutePooled` | **433.3 µs** | **-** |
| | `SortedInnerJoin_ClassicCore_Pooled` | 1,924.1 µs | 24 B |
| joined, page of 200 | `SortedInnerJoin_ExecutePooled` | **624.0 µs** | **-** |
| | `SortedInnerJoin_ClassicCore_Pooled` | 1,952.2 µs | 24 B |
| joined, whole result | `SortedInnerJoin_FullPage_ExecutePooled` | 2,251.1 µs | 3,849 B |
| | `SortedInnerJoin_FullPage_ClassicCore` | **1,960.2 µs** | 24 B |

## N = 100,000

| Shape | Method | Mean | Allocated |
|---|---|---:|---:|
| page of 20 | `Sorted_ExecutePooled` | **940.1 µs** | **-** |
| | `Sorted_ClassicCore_Pooled` | 8,066.5 µs | 64 B |
| page of 200 | `Sorted_ExecutePooled` | **1,079.9 µs** | **-** |
| | `Sorted_ClassicCore_Pooled` | 7,711.9 µs | 64 B |
| page of 20 at the end | `Sorted_DeepPage_ExecutePooled` | **2,192.4 µs** | **-** |
| | `Sorted_DeepPage_ClassicCore` | 7,552.9 µs | 64 B |
| half the result | `Sorted_HalfPage_ExecutePooled` | 10,281.4 µs | - |
| | `Sorted_HalfPage_ClassicCore` | **7,766.1 µs** | 64 B |
| the whole result | `Sorted_FullPage_ExecutePooled` | 16,241.0 µs | - |
| | `Sorted_FullPage_ClassicCore` | **7,555.5 µs** | 64 B |
| joined, page of 20 | `SortedInnerJoin_ExecutePooled` | **4,847.4 µs** | **-** |
| | `SortedInnerJoin_ClassicCore_Pooled` | 24,403.8 µs | 24 B |
| joined, whole result | `SortedInnerJoin_FullPage_ExecutePooled` | 28,942.7 µs | - |
| | `SortedInnerJoin_FullPage_ClassicCore` | **25,207.3 µs** | 24 B |

## Reading it

- **Paging wins, 2.3-8.6×**, and the win grows with N — which is the point: the classic plan's cost
  scales with the result, the bounded plan's with the page.
- **`skip` is free.** A page of 20 taken from the end wins 2.1-3.4×; introselect over N still beats a
  full sort of N.
- **A large contiguous prefix loses**, 1.3× at half the result and 2.0-2.7× at all of it: a near-full
  page leaves no sort to skip, only the `(value, ordinal)` tuple overhead at twice the bytes of a
  bare reference. Deliberately not guarded against — see `context/query.md`.
- **Paged shapes allocate nothing.** The comparer reaches the selection code as a struct type
  parameter (no `Comparison<T>` delegate, no boxed `IComparer<T>`). The two non-zero bounded cells are
  full-page collect-all renting its O(N) buffer, which is the shape you should not use it for.

Per-query allocation, measured separately with `GC.GetAllocatedBytesForCurrentThread` over 20 ops
after warmup on a 1,000-row cache (deterministic, unlike BenchmarkDotNet's per-op figure) and pinned
by `tests/Prague.Core.Tests/Join/TopKAllocationCoreTests.cs`:

| query | B/op |
|---|---:|
| unsorted `ExecutePooled()` | 0 |
| `SortBounded(...).ExecutePooled(0, 10)` — struct or class comparer | **0** |
| `Sort(...).ExecutePooled()` | 88 |
| unsorted joined `ExecutePooled()` | 144 |
| `SortBounded(...).InnerJoinOne(...).ExecutePooled(0, 10)` | **144** — nothing over the unsorted join |
| `Sort(...).InnerJoinOne(...).ExecutePooled()` | 168 |

> **Superseded.** An earlier revision of this file carried a Before/After pair from a Ryzen 9 9955HX3D
> box claiming 16-29× and `Allocated: -` on every bounded row. Neither reproduces: the two runs used
> different machines and the ratio was inflated at both ends, and the bounded path did allocate 64-88 B
> per query at that commit (a `Comparison<T>` delegate plus a boxed struct comparer, both since
> removed). The tables above replace it.

# Prepared queries — eager builder vs `PreparedQuery<TArgs, TResult>`

`PreparedQueryBenchmarks`: raw `InMemoryDataCache` (no codegen), 100k rows, list buckets of 1k
(`Group = Id % 100`), range windows of 1k codes. Every category pairs the eager spelling a caller
writes (baseline) with the prepared twin built once in `GlobalSetup` and executed with the same
arguments; every body executes pooled and disposes. `Build_FourNarrowers` is the one-time build cost.

Apple M4 Pro, .NET 9.0.19 (osx-arm64), `--job short --inProcess` (3 iterations), `[MemoryDiagnoser]`.
Read the ratios as within-noise unless the error bars say otherwise; `Allocated` is the point.

| Method                | Categories   | Mean            | Error         | StdDev        | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |------------- |----------------:|--------------:|--------------:|------:|--------:|----------:|------------:|
| Build_FourNarrowers   | Build        |        32.94 ns |      31.37 ns |      1.719 ns |     ? |       ? |      88 B |           ? |
| IfSkipped_Eager       | IfSkipped    |    10,173.01 ns |     504.17 ns |    27.635 ns |  1.00 |    0.00 |       1 B |        1.00 |
| IfSkipped_Prepared    | IfSkipped    |    10,536.87 ns |  10,499.93 ns |   575.536 ns |  1.04 |    0.05 |       1 B |        1.00 |
| IfTaken_Eager         | IfTaken      |     3,091.83 ns |     366.72 ns |    20.101 ns |  1.00 |    0.01 |         - |          NA |
| IfTaken_Prepared      | IfTaken      |     3,192.32 ns |   1,000.29 ns |    54.829 ns |  1.03 |    0.02 |         - |          NA |
| JoinOne_Eager         | JoinOne      |    32,196.05 ns |   1,484.48 ns |    81.369 ns |  1.00 |    0.00 |       5 B |        1.00 |
| JoinOne_Prepared      | JoinOne      |    31,679.71 ns |  14,137.91 ns |   774.946 ns |  0.98 |    0.02 |       5 B |        1.00 |
| ListArgWhere_Eager    | ListArgWhere |     9,096.26 ns |     129.28 ns |     7.086 ns |  1.00 |    0.00 |      89 B |        1.00 |
| ListArgWhere_Prepared | ListArgWhere |     9,912.71 ns |   6,166.75 ns |   338.021 ns |  1.09 |    0.03 |       1 B |        0.01 |
| ListWhere_Eager       | ListWhere    |     8,590.23 ns |   1,952.37 ns |   107.016 ns |  1.00 |    0.02 |       1 B |        1.00 |
| ListWhere_Prepared    | ListWhere    |     8,857.73 ns |   5,340.77 ns |   292.746 ns |  1.03 |    0.03 |       1 B |        1.00 |
| Or_Eager              | Or           | 1,077,910.81 ns | 161,451.33 ns | 8,849.692 ns |  1.00 |    0.01 |    3136 B |        1.00 |
| Or_Prepared           | Or           | 1,044,669.83 ns | 709,850.41 ns | 38,909.296 ns |  0.97 |    0.03 |         - |        0.00 |
| Range_Eager           | Range        |    13,394.55 ns |   1,939.65 ns |   106.319 ns |  1.00 |    0.01 |       1 B |        1.00 |
| Range_Prepared        | Range        |    13,539.05 ns |   3,739.13 ns |   204.954 ns |  1.01 |    0.01 |       1 B |        1.00 |
| SortBounded_Eager     | SortBounded  |    16,179.03 ns |   7,035.60 ns |   385.645 ns |  1.00 |    0.03 |       1 B |        1.00 |
| SortBounded_Prepared  | SortBounded  |    16,396.41 ns |   8,022.25 ns |   439.727 ns |  1.01 |    0.03 |       1 B |        1.00 |
| Unique_Eager          | Unique       |       134.09 ns |      20.63 ns |     1.131 ns |  1.00 |    0.01 |         - |          NA |
| Unique_Prepared       | Unique       |       132.97 ns |      78.84 ns |     4.321 ns |  0.99 |    0.03 |         - |          NA |

The three rows whose short-run error bars were widest, re-run with the default job (15+ iterations):

| Method                | Mean         | Error      | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |-------------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| IfSkipped_Eager       |    10.213 us |  0.1448 us |  0.1487 us |  1.00 |    0.02 |       1 B |        1.00 |
| IfSkipped_Prepared    |    10.129 us |  0.1254 us |  0.1173 us |  0.99 |    0.02 |       1 B |        1.00 |
| ListArgWhere_Eager    |     8.990 us |  0.0804 us |  0.0713 us |  1.00 |    0.01 |      89 B |        1.00 |
| ListArgWhere_Prepared |     9.163 us |  0.0740 us |  0.0692 us |  1.02 |    0.01 |       1 B |        0.01 |
| Or_Eager              | 1,116.326 us | 21.7728 us | 43.9820 us |  1.00 |    0.05 |    3136 B |        1.00 |
| Or_Prepared           | 1,045.573 us | 20.7983 us | 19.4548 us |  0.94 |    0.04 |    3136 B |        1.00 |

## Reading it

- **Execution cost is the eager cost.** Every ratio lands within its error bars of 1.00 once the job
  is long enough; the replay does the eager work plus one cached-delegate call per parameterized
  narrower.
- **The only allocation delta is the closure the eager spelling needs.** `Where(v => v.Id >= a.min)`
  with `a` a per-call argument costs the eager builder 89 B (display class + delegate); the prepared
  side binds the argument into a per-thread pooled predicate box and allocates nothing. Bound
  `Where`, indexes, ranges, `If`, joins and `SortBounded` pages allocate the same on both sides.
- **`Build()` is 88 B, once.** That is the `PreparedQuery<TArgs,TResult>` object holding the
  four-narrower struct chain by value.
- The `1 B` / `5 B` cells are BenchmarkDotNet's per-op amortization of a one-off pool refill; the
  deterministic per-call pins live in `tests/Prague.Core.Tests/Prepared/PreparedQueryAllocationTests.cs`.
  The `3136 B` on the `Or` rows is the eager `Or` core's own cost at this candidate size (a 100k-key
  bitmap union) and is identical on both sides in the longer run.


# Frozen queries — `BuildFrozen()` stage 1: the point-lookup fast path

`FrozenQueryBenchmarks`: same data and spelling rules as `PreparedQueryBenchmarks` (raw
`InMemoryDataCache`, 100k rows, list buckets of 1k, range windows of 1k codes; every body executes
pooled and disposes). Each category has three rows: the eager builder (baseline), `Build()` (replay)
and `BuildFrozen()`. The first four categories are the shapes the frozen planner binds to the
`PointLookup` executor — a unique-index equality, bound or parameterized, followed only by filters. The
last three (`ListWhere`, `Range`, `JoinOne`) are deliberately ineligible: their `_Frozen` rows must sit
on the `_Prepared` rows, because the fallback *is* the replay.

Apple M4 Pro, .NET 9.0.19 (osx-arm64), `--job short --inProcess` (3 iterations), `[MemoryDiagnoser]`.

| Method                  | Categories     | Mean         | Error         | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |--------------- |-------------:|--------------:|-----------:|------:|--------:|----------:|------------:|
| JoinOne_Eager           | JoinOne        | 32,221.65 ns | 13,662.724 ns | 748.900 ns |  1.00 |    0.03 |       5 B |        1.00 |
| JoinOne_Prepared        | JoinOne        | 32,689.87 ns |  8,519.926 ns | 467.006 ns |  1.01 |    0.02 |       5 B |        1.00 |
| JoinOne_Frozen          | JoinOne        | 32,258.69 ns |  8,732.989 ns | 478.685 ns |  1.00 |    0.02 |       5 B |        1.00 |
| ListWhere_Eager         | ListWhere      |  8,543.20 ns |  2,685.538 ns | 147.203 ns |  1.00 |    0.02 |       1 B |        1.00 |
| ListWhere_Prepared      | ListWhere      |  8,421.19 ns |    733.979 ns |  40.232 ns |  0.99 |    0.02 |       1 B |        1.00 |
| ListWhere_Frozen        | ListWhere      |  8,486.40 ns |    755.602 ns |  41.417 ns |  0.99 |    0.02 |       1 B |        1.00 |
| Range_Eager             | Range          | 13,492.41 ns |    230.372 ns |  12.627 ns |  1.00 |    0.00 |       1 B |        1.00 |
| Range_Prepared          | Range          | 13,423.35 ns |  2,245.412 ns | 123.079 ns |  0.99 |    0.01 |       1 B |        1.00 |
| Range_Frozen            | Range          | 13,399.89 ns |    439.864 ns |  24.110 ns |  0.99 |    0.00 |       1 B |        1.00 |
| Unique_Eager            | Unique         |    135.24 ns |     24.882 ns |   1.364 ns |  1.00 |    0.01 |         - |          NA |
| Unique_Prepared         | Unique         |    131.09 ns |     11.165 ns |   0.612 ns |  0.97 |    0.01 |         - |          NA |
| Unique_Frozen           | Unique         |     26.96 ns |      0.396 ns |   0.022 ns |  0.20 |    0.00 |         - |          NA |
| UniqueArg_Eager         | UniqueArg      |    134.19 ns |     20.892 ns |   1.145 ns |  1.00 |    0.01 |         - |          NA |
| UniqueArg_Prepared      | UniqueArg      |    132.21 ns |     10.160 ns |   0.557 ns |  0.99 |    0.01 |         - |          NA |
| UniqueArg_Frozen        | UniqueArg      |     18.81 ns |      2.544 ns |   0.139 ns |  0.14 |    0.00 |         - |          NA |
| UniqueArgWhere_Eager    | UniqueArgWhere |    158.79 ns |     18.986 ns |   1.041 ns |  1.00 |    0.01 |      88 B |        1.00 |
| UniqueArgWhere_Prepared | UniqueArgWhere |    138.19 ns |      4.343 ns |   0.238 ns |  0.87 |    0.01 |         - |        0.00 |
| UniqueArgWhere_Frozen   | UniqueArgWhere |     19.29 ns |      2.642 ns |   0.145 ns |  0.12 |    0.00 |         - |        0.00 |
| UniqueWhere_Eager       | UniqueWhere    |    149.62 ns |      8.429 ns |   0.462 ns |  1.00 |    0.00 |         - |          NA |
| UniqueWhere_Prepared    | UniqueWhere    |    134.72 ns |      9.769 ns |   0.535 ns |  0.90 |    0.00 |         - |          NA |
| UniqueWhere_Frozen      | UniqueWhere    |     24.09 ns |      0.490 ns |   0.027 ns |  0.16 |    0.00 |         - |          NA |

The four eligible categories re-run with the default job (15+ iterations) for tight error bars:

| Method                  | Categories     | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------------ |--------------- |----------:|---------:|---------:|------:|----------:|------------:|
| Unique_Eager            | Unique         | 134.25 ns | 1.232 ns | 1.153 ns |  1.00 |         - |          NA |
| Unique_Prepared         | Unique         | 130.83 ns | 1.598 ns | 1.495 ns |  0.97 |         - |          NA |
| Unique_Frozen           | Unique         |  26.54 ns | 0.205 ns | 0.192 ns |  0.20 |         - |          NA |
| UniqueArg_Eager         | UniqueArg      | 132.29 ns | 0.410 ns | 0.320 ns |  1.00 |         - |          NA |
| UniqueArg_Prepared      | UniqueArg      | 130.02 ns | 1.159 ns | 1.084 ns |  0.98 |         - |          NA |
| UniqueArg_Frozen        | UniqueArg      |  18.84 ns | 0.104 ns | 0.092 ns |  0.14 |         - |          NA |
| UniqueArgWhere_Eager    | UniqueArgWhere | 156.11 ns | 1.572 ns | 1.313 ns |  1.00 |      88 B |        1.00 |
| UniqueArgWhere_Prepared | UniqueArgWhere | 143.58 ns | 0.753 ns | 0.629 ns |  0.92 |         - |        0.00 |
| UniqueArgWhere_Frozen   | UniqueArgWhere |  19.08 ns | 0.084 ns | 0.075 ns |  0.12 |         - |        0.00 |
| UniqueWhere_Eager       | UniqueWhere    | 149.43 ns | 1.116 ns | 1.044 ns |  1.00 |         - |          NA |
| UniqueWhere_Prepared    | UniqueWhere    | 134.41 ns | 0.888 ns | 0.831 ns |  0.90 |         - |          NA |
| UniqueWhere_Frozen      | UniqueWhere    |  24.43 ns | 0.166 ns | 0.147 ns |  0.16 |         - |          NA |

## Reading it

- **The point lookup is 5–8× faster than eager and allocates nothing.** 19–27 ns against 132–156 ns.
  What the executor skips is the eager core itself: no `ValueSet` rented and returned, no candidate
  insert, no set walk through `TryGetValues`, no `Compose`d filter closure, no per-thread predicate box
  for the parameterized `Where`. What remains is what a lookup fundamentally costs: the index probe,
  the store probe, the filters, and a one-slot `QueryResults` rented from the same pool the eager
  container rents from (so `Dispose` returns it the same way). A miss returns the shared `Empty` and
  never touches the pool.
- **The result is the eager result.** `TotalCount`, `Truncated`, `skip` / `take` slicing (including
  `skip == 1`, `take == 0` and `skip > 1` over a single row), pooled vs allocated buffer source and
  clone timing (clone-on-add without a slice, clone-in-place after one) are reproduced step for step
  from `SimpleResultContainer`; `tests/Prague.Core.Tests/Prepared/FrozenQueryTests.cs` pins every
  combination against both the eager builder and `Build()`.
- **No regression on the fallback.** `ListWhere`, `Range` and `JoinOne` frozen rows are within their
  error bars of the prepared rows: the replay executor calls the same `PreparedReplay` routine the
  `PreparedQuery` classes call.
- The bound (`NoArgs`) unique row is a consistent ~8 ns slower than the parameterized one (26.5 vs
  18.8 ns) in both jobs. Inlining the `NoArgs` forwarders did not move it (27.8 ns), so it is not the
  extension hop; left for stage 2.
- Building a frozen query allocates the descriptor list and the executor object once; nothing on the
  execute path reads the descriptors.


# `Match` and optional-bounds range — eager vs `Build()` vs `BuildFrozen()`

`FrozenQueryBenchmarks` / `PreparedQueryBenchmarks`, same data and rules as above (100k rows, list
buckets of 1k, range windows of 1k codes, every body executes pooled and disposes). `Match` is three
parameterized arms (`UseIndex(unique)`, `UseIndex(list)`, `UseIndex(list).Where`) with the third selected,
against the eager `switch` a caller writes; `OptionalRange` is `UseIndex(range, from:, to:, toInclusive: false)`
with both bounds present, against the eager four-arm `if` over the type-state builder. Neither shape is
point-lookup eligible, so the `_Frozen` rows are the replay executor and must sit on the `_Prepared` rows.

Apple M4 Pro, .NET 9, `--job short --inProcess` (3 iterations), `[MemoryDiagnoser]`.

| Method                 | Categories    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |-------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Match_Eager            | Match         |  8.340 us | 0.9136 us | 0.0501 us |  1.00 |    0.01 |         - |          NA |
| Match_Prepared         | Match         |  8.481 us | 1.9180 us | 0.1051 us |  1.02 |    0.01 |         - |          NA |
| Match_Frozen           | Match         |  8.484 us | 2.7098 us | 0.1485 us |  1.02 |    0.02 |         - |          NA |
| OptionalRange_Eager    | OptionalRange | 13.291 us | 0.5024 us | 0.0275 us |  1.00 |    0.00 |         - |          NA |
| OptionalRange_Prepared | OptionalRange | 13.318 us | 0.0514 us | 0.0028 us |  1.00 |    0.00 |         - |          NA |
| OptionalRange_Frozen   | OptionalRange | 13.355 us | 2.4157 us | 0.1324 us |  1.00 |    0.01 |       1 B |          NA |

`PreparedQueryBenchmarks` (eager vs `Build()` only):

| Method                 | Categories    | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------------------- |-------------- |----------:|----------:|----------:|------:|----------:|------------:|
| Match_Eager            | Match         |  8.406 us | 0.7177 us | 0.0393 us |  1.00 |       1 B |        1.00 |
| Match_Prepared         | Match         |  8.436 us | 1.5786 us | 0.0865 us |  1.00 |         - |        0.00 |
| OptionalRange_Eager    | OptionalRange | 13.291 us | 1.1032 us | 0.0605 us |  1.00 |         - |          NA |
| OptionalRange_Prepared | OptionalRange | 13.366 us | 1.2045 us | 0.0660 us |  1.01 |         - |          NA |

## Reading it

- **Dispatch is free.** `Match` costs the selected arm's eager work plus one tag-selector call and a
  chain of `EqualityComparer<TTag>.Default.Equals` compares the JIT folds per closed generic; ratios
  sit at 1.00–1.02 inside the short-job error bars, and nothing is allocated on either side.
- **Optional bounds are the eager range.** The prepared step hands the core the same two `RangeValue`s
  the eager `Gte(..).Lt(..)` chain produces, through a cached pass-through delegate; 13.3 us both ways.
- The `1 B` cells are BenchmarkDotNet's per-op amortization of a one-off pool refill (as in the earlier
  tables); the deterministic pins — 0 B pooled in Release for both bounds, one bound, no bound, and every
  `Match` arm — are `PreparedQueryAllocationTests.OptionalRange_*` / `Match_*`.


# Frozen queries — `BuildFrozen()` stage 2: fused filters, capacity hints, adaptive intersection, smallest-bucket seeding

`FrozenQueryBenchmarks`, same data and rules as stage 1 (100k rows; Group buckets of 1k, Tier buckets of
100; every body executes pooled and disposes). New categories 10–15; every kept optimization has its
control row next to the eager (baseline) / `Build()` / `BuildFrozen()` rows: `_FrozenFixedOrder`
(fusion without adaptive ordering), `_FrozenNoHints`, `_FrozenEagerIntersection` (hints only, per-step
intersection), `_FrozenReorder` (opt-in `ReorderIndexNarrowers`). Design and per-optimization reading:
`docs/superpowers/specs/2026-09-09-prepared-query-command-design.md` §8 "Stage 2".

Apple M4 Pro, .NET 9.0 (osx-arm64), `--inProcess`, default job (15+ iterations), `[MemoryDiagnoser]`.

| Method                                        | Categories                   | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|---------------------------------------------- |----------------------------- |----------:|----------:|----------:|------:|----------:|------------:|
| ListWhere_Eager                               | ListWhere                    |  8.385 us | 0.1577 us | 0.1398 us |  1.00 |       1 B |        1.00 |
| ListWhere_Prepared                            | ListWhere                    |  8.476 us | 0.1248 us | 0.1226 us |  1.01 |       1 B |        1.00 |
| ListWhere_Frozen                              | ListWhere                    |  7.116 us | 0.1414 us | 0.2243 us |  0.85 |         - |        0.00 |
| ListWhere_FrozenNoHints                       | ListWhere                    |  8.463 us | 0.1533 us | 0.1434 us |  1.01 |       1 B |        1.00 |
| ListTwoWheres_Eager                           | ListTwoWheres                |  9.520 us | 0.1015 us | 0.0900 us |  1.00 |      97 B |        1.00 |
| ListTwoWheres_Prepared                        | ListTwoWheres                |  9.528 us | 0.0939 us | 0.0784 us |  1.00 |      97 B |        1.00 |
| ListTwoWheres_Frozen                          | ListTwoWheres                |  8.546 us | 0.1144 us | 0.1014 us |  0.90 |       1 B |        0.01 |
| ListTwoWheres_FrozenFixedOrder                | ListTwoWheres                |  8.344 us | 0.0777 us | 0.0607 us |  0.88 |       1 B |        0.01 |
| ListThreeWheresSelectiveLast_Eager            | ListThreeWheresSelectiveLast | 10.380 us | 0.1955 us | 0.3425 us |  1.00 |     193 B |        1.00 |
| ListThreeWheresSelectiveLast_Prepared         | ListThreeWheresSelectiveLast | 11.482 us | 0.1915 us | 0.1698 us |  1.11 |     193 B |        1.00 |
| ListThreeWheresSelectiveLast_Frozen           | ListThreeWheresSelectiveLast |  7.039 us | 0.1339 us | 0.1375 us |  0.68 |         - |        0.00 |
| ListThreeWheresSelectiveLast_FrozenFixedOrder | ListThreeWheresSelectiveLast | 10.149 us | 0.1936 us | 0.1811 us |  0.98 |         - |        0.00 |
| ListTwoArgWheres_Eager                        | ListTwoArgWheres             | 12.242 us | 0.0773 us | 0.0603 us |  1.00 |     257 B |       1.000 |
| ListTwoArgWheres_Prepared                     | ListTwoArgWheres             | 13.491 us | 0.0839 us | 0.0655 us |  1.10 |      97 B |       0.377 |
| ListTwoArgWheres_Frozen                       | ListTwoArgWheres             | 11.628 us | 0.0705 us | 0.0550 us |  0.95 |       1 B |       0.004 |
| ListList_Eager                                | ListList                     |  9.415 us | 0.1869 us | 0.3174 us |  1.00 |       1 B |        1.00 |
| ListList_Prepared                             | ListList                     |  9.318 us | 0.1812 us | 0.2356 us |  0.99 |       1 B |        1.00 |
| ListList_Frozen                               | ListList                     |  4.332 us | 0.0440 us | 0.0367 us |  0.46 |         - |        0.00 |
| ListList_FrozenEagerIntersection              | ListList                     |  8.080 us | 0.1449 us | 0.1284 us |  0.86 |         - |        0.00 |
| ListList_FrozenReorder                        | ListList                     |  1.260 us | 0.0250 us | 0.0221 us |  0.13 |         - |        0.00 |
| ListListReversed_Eager                        | ListListReversed             |  1.357 us | 0.0264 us | 0.0334 us |  1.00 |         - |          NA |
| ListListReversed_Prepared                     | ListListReversed             |  1.328 us | 0.0256 us | 0.0251 us |  0.98 |         - |          NA |
| ListListReversed_Frozen                       | ListListReversed             |  1.265 us | 0.0213 us | 0.0189 us |  0.93 |         - |          NA |
| ListListReversed_FrozenEagerIntersection      | ListListReversed             |  1.246 us | 0.0201 us | 0.0168 us |  0.92 |         - |          NA |
| ListListReversed_FrozenReorder                | ListListReversed             |  1.239 us | 0.0178 us | 0.0139 us |  0.91 |         - |          NA |
| ListRange_Eager                               | ListRange                    | 400.9 us  |  3.26 us  |  3.05 us  |  1.00 |      24 B |        1.00 |
| ListRange_Prepared                            | ListRange                    | 406.0 us  |  6.32 us  |  5.91 us  |  1.01 |      24 B |        1.00 |
| ListRange_Frozen                              | ListRange                    | 401.6 us  |  3.86 us  |  3.42 us  |  1.00 |      24 B |        1.00 |

Stage-1 categories and the optimization-5 probes, `--job short --inProcess`:

| Method                    | Categories     | Mean         | Error         | StdDev       | Ratio | Allocated | Alloc Ratio |
|-------------------------- |--------------- |-------------:|--------------:|-------------:|------:|----------:|------------:|
| JoinOne_Eager             | JoinOne        | 34,463.68 ns | 62,763.319 ns | 3,440.269 ns |  1.01 |       5 B |        1.00 |
| JoinOne_Prepared          | JoinOne        | 33,422.91 ns | 19,621.113 ns | 1,075.499 ns |  0.98 |       5 B |        1.00 |
| JoinOne_Frozen            | JoinOne        | 31,511.10 ns |  4,031.990 ns |   221.007 ns |  0.92 |       5 B |        1.00 |
| Range_Eager               | Range          | 13,592.98 ns |  9,723.970 ns |   533.004 ns |  1.00 |       1 B |        1.00 |
| Range_Prepared            | Range          | 13,489.37 ns |  6,095.481 ns |   334.114 ns |  0.99 |       1 B |        1.00 |
| Range_Frozen              | Range          | 13,249.91 ns |    481.576 ns |    26.397 ns |  0.98 |       1 B |        1.00 |
| Unique_Eager              | Unique         |    140.21 ns |    187.600 ns |    10.283 ns |  1.00 |         - |          NA |
| Unique_Prepared           | Unique         |    131.24 ns |      7.430 ns |     0.407 ns |  0.94 |         - |          NA |
| Unique_Frozen             | Unique         |     27.18 ns |     24.522 ns |     1.344 ns |  0.19 |         - |          NA |
| Unique_FrozenDirectCall   | Unique         |     23.70 ns |      0.802 ns |     0.044 ns |  0.17 |         - |          NA |
| Unique_FrozenBoundIntArgs | Unique         |     19.47 ns |     18.353 ns |     1.006 ns |  0.14 |         - |          NA |
| UniqueArg_Eager           | UniqueArg      |    139.37 ns |     25.739 ns |     1.411 ns |  1.00 |         - |          NA |
| UniqueArg_Prepared        | UniqueArg      |    134.12 ns |     49.593 ns |     2.718 ns |  0.96 |         - |          NA |
| UniqueArg_Frozen          | UniqueArg      |     25.51 ns |      9.231 ns |     0.506 ns |  0.18 |         - |          NA |
| UniqueArgWhere_Eager      | UniqueArgWhere |    186.02 ns |    220.661 ns |    12.095 ns |  1.00 |      88 B |        1.00 |
| UniqueArgWhere_Prepared   | UniqueArgWhere |    141.82 ns |      6.664 ns |     0.365 ns |  0.76 |         - |        0.00 |
| UniqueArgWhere_Frozen     | UniqueArgWhere |     19.88 ns |     24.129 ns |     1.323 ns |  0.11 |         - |        0.00 |
| UniqueWhere_Eager         | UniqueWhere    |    157.95 ns |    185.968 ns |    10.194 ns |  1.00 |         - |          NA |
| UniqueWhere_Prepared      | UniqueWhere    |    142.02 ns |     22.997 ns |     1.261 ns |  0.90 |         - |          NA |
| UniqueWhere_Frozen        | UniqueWhere    |     24.76 ns |      0.584 ns |     0.032 ns |  0.16 |         - |          NA |

`Match` and `OptionalRange` re-measured within their error bars of their earlier table (0.98–1.02 of
`Build()`); `ListRange`, a range-seeded plan no stage-2 optimization applies to, binds the stage-1 replay
executor and sits on `Build()` in the default job (401.6 vs 406.0 / 400.9 µs).

## Reading it

- **Fused filters (optimization 1).** Two constant `Where`s: the eager `&&` closure (97 B) is gone and
  the row is 10% faster (0.90); with the 1.5× promotion margin the declared order is kept here (the
  second predicate rejects 90% against the first's 67% but costs about what the swap would save), and the
  `_FrozenFixedOrder` control at 0.88 shows the price of sampling itself — one execution in 256 counts
  and (for its first 4096 calls) times every predicate call, ~2% here. Three `Where`s with the 99%
  rejector declared last: the adaptive order moves it to the front and the row drops from 10.38 to
  7.04 µs (0.68), against 10.15 µs for the same fused plan with the order fixed — that difference *is*
  the ordering. Two parameterized `Where`s: one pooled box instead of two plus a closure, 5 delegate
  hops per row become 3; 0 B against 257 B eager / 97 B `Build()`, 5% under eager and 14% under `Build()`.
- **Capacity hints (2).** `ListWhere_Frozen` 7.12 µs against 8.46 µs with hints off (0.85 vs 1.01): the
  1k bucket rents its arrays once at 1,103 slots instead of rehashing five times. The same hint is the
  `ListList_FrozenEagerIntersection` row's 14%. `JoinOne_Frozen` (1k bucket, no filter) sits at 0.92
  inside a wide short-job error bar.
- **Adaptive intersection (3).** `list(1k) ∩ list(100)`: 4.33 µs against 8.08 µs for the per-step
  intersection (0.46 vs 0.86) — the 100-row bucket is walked and probed into the 1k set instead of the
  1k set being walked and probed into the bucket, then one compaction. `list(100) ∩ list(1k)`: the
  bucket (1k) is larger than the seeded set (100), so the executor takes the eager way — 1.27 µs, parity
  with `_FrozenEagerIntersection` (1.25) and 7% under eager. The literal "compact once" variant measured
  first (always the bitmap way) was 2× slower on this reversed shape, which is why the kept form is
  size-directed.
- **Smallest-bucket seeding (4, opt-in).** Declared large-then-small: 1.26 µs, 7.5× faster than eager
  and 3.4× faster than the default frozen plan; declared small-then-large: 1.24 µs, parity — the seed is
  already the smallest bucket.
- **The bound point lookup (5).** Direct virtual call 23.7 ns vs 27.2 ns through the `NoArgs`
  extension; the same bound value with `int` args 19.5 ns. The `NoArgs` instantiation, not the bound
  value, carries the ~5 ns; left as is.
- **Regression gate.** A first version of the hints added a capacity field and an `InitCandidates()`
  helper to the eager core and the optimized frozen replay carried its hint / fused-filter parameters
  on every plan; a default-job run of `ListRange` — a plan with no optimization active — then measured
  the frozen row at 416 µs against 398 / 397 (1.05, ~6σ; the stage-1 tree measured 399 / 398 / 399).
  The core change was reverted (the hint now pre-creates `core.Candidates` before the replay) and plans
  without an optimization bind the stage-1 replay executor; the row is back at 1.00 (table above).
  Separately, long `--inProcess` runs intermittently measured one range row (`Range_Prepared`,
  `OptionalRange_Prepared` or `ListRange_Frozen`) at 2–3× with ~100 B/op — a different row each run,
  never reproducible when the category ran alone, and prepared rows are stage-1 code the change did not
  touch — so those runs were discarded and per-category runs are reported.
- The `1 B` cells are BenchmarkDotNet's amortization of a one-off pool refill; the deterministic pins —
  0 B pooled in Release for list + two constant `Where`s, list + two arg `Where`s, list ∩ list on the
  default, adaptive-intersection and reorder plans — are `PreparedQueryAllocationTests.Frozen_*`.



# Frozen queries — `BuildFrozen()` stage 3, steps 1–2: the pipeline executor

`FrozenQueryBenchmarks`, same data and rules as stages 1–2 (100k rows; Group buckets of 1k, Tier buckets of
100; every body executes pooled and disposes). New categories: `ListKeySet` (1k bucket ∩ key-set of a third
of the rows), `ListLastUpdated` (1k bucket ∩ updated-after covering half the rows), the `Count_*` twins of
`ListWhere` / `ListList` / `ListRange`, and the two production shapes — `ListListListSortBoundedJoinOne`
(1k ∩ 333 ∩ 111 → 111 rows, `SortBounded` page 20..40 with a tying struct comparer, `JoinOne` 1:1 by PK)
and `TimeWindowListListSortBoundedJoinTwo` / `TimeRangeListListSortBoundedJoinTwo` (10k records, newer
than T selecting ~15%, two skewed list keys, `SortBounded` page 20..40, two chained `JoinOne`s 1:1 by FK;
the last-updated index vs a range index on the timestamp), each with a `Count_` twin. Simple unsorted
list / range / key-set / last-updated plans bind the `Pipeline` executor; `ListList_FrozenIndexSteps` is the
stage-2 adaptive intersection (`Pipeline = false`) for reference; the production shapes replay in this
step (their rows are the baseline for the design's steps 3 / 5 / 6). Design and reading:
`docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md`, parent spec §8 "Stage 3".

Apple M4 Pro, .NET 9.0 (osx-arm64), `--inProcess`, default job, one category per run, `[MemoryDiagnoser]`.

**Pipeline plans**

| Method                  | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListWhere_Eager         | 9.792 us | 0.1955 us | 0.3044 us |  1.00 |    0.04 |       1 B |        1.00 |
| ListWhere_Prepared      | 8.645 us | 0.0802 us | 0.0711 us |  0.88 |    0.03 |       1 B |        1.00 |
| ListWhere_Frozen        | 7.020 us | 0.0833 us | 0.0779 us |  0.72 |    0.02 |         - |        0.00 |
| ListWhere_FrozenNoHints | 9.234 us | 0.0707 us | 0.0661 us |  0.94 |    0.03 |         - |        0.00 |

| Method                           | Mean     | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|--------------------------------- |---------:|----------:|----------:|------:|----------:|------------:|
| ListList_Eager                   | 9.627 us | 0.0812 us | 0.0720 us |  1.00 |       1 B |        1.00 |
| ListList_Prepared                | 9.584 us | 0.1137 us | 0.1064 us |  1.00 |       1 B |        1.00 |
| ListList_Frozen                  | 8.913 us | 0.0538 us | 0.0504 us |  0.93 |         - |        0.00 |
| ListList_FrozenEagerIntersection | 8.456 us | 0.0895 us | 0.0837 us |  0.88 |         - |        0.00 |
| ListList_FrozenReorder           | 1.285 us | 0.0105 us | 0.0082 us |  0.13 |         - |        0.00 |
| ListList_FrozenIndexSteps        | 4.375 us | 0.0456 us | 0.0426 us |  0.45 |         - |        0.00 |

| Method                                   | Mean     | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------------------------------------- |---------:|----------:|----------:|------:|----------:|------------:|
| ListListReversed_Eager                   | 1.328 us | 0.0117 us | 0.0110 us |  1.00 |         - |          NA |
| ListListReversed_Prepared                | 1.314 us | 0.0060 us | 0.0053 us |  0.99 |         - |          NA |
| ListListReversed_Frozen                  | 1.032 us | 0.0090 us | 0.0080 us |  0.78 |         - |          NA |
| ListListReversed_FrozenEagerIntersection | 1.253 us | 0.0151 us | 0.0141 us |  0.94 |         - |          NA |
| ListListReversed_FrozenReorder           | 1.250 us | 0.0085 us | 0.0079 us |  0.94 |         - |          NA |
| ListListReversed_FrozenIndexSteps        | 1.255 us | 0.0108 us | 0.0101 us |  0.94 |         - |          NA |

| Method             | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------- |----------:|---------:|---------:|------:|----------:|------------:|
| ListRange_Eager    | 406.70 us | 3.524 us | 3.124 us |  1.00 |      24 B |        1.00 |
| ListRange_Prepared | 407.42 us | 4.924 us | 4.606 us |  1.00 |      24 B |        1.00 |
| ListRange_Frozen   |  12.09 us | 0.077 us | 0.068 us |  0.03 |         - |        0.00 |

| Method         | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|----------:|------:|----------:|------------:|
| Range_Eager    | 12.940 us | 0.1114 us | 0.1042 us |  1.00 |       1 B |        1.00 |
| Range_Prepared | 12.957 us | 0.0438 us | 0.0366 us |  1.00 |       1 B |        1.00 |
| Range_Frozen   |  8.822 us | 0.1351 us | 0.1388 us |  0.68 |         - |        0.00 |

| Method                 | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|------:|----------:|------------:|
| OptionalRange_Eager    | 12.961 us | 0.0803 us | 0.0751 us |  1.00 |       1 B |        1.00 |
| OptionalRange_Prepared | 13.209 us | 0.1364 us | 0.1276 us |  1.02 |       1 B |        1.00 |
| OptionalRange_Frozen   |  8.781 us | 0.0615 us | 0.0514 us |  0.68 |         - |        0.00 |

| Method              | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|-------------------- |----------:|----------:|----------:|------:|----------:|------------:|
| ListKeySet_Eager    | 12.307 us | 0.1164 us | 0.1088 us |  1.00 |       1 B |        1.00 |
| ListKeySet_Prepared | 12.146 us | 0.1479 us | 0.1384 us |  0.99 |       1 B |        1.00 |
| ListKeySet_Frozen   |  9.902 us | 0.1078 us | 0.1008 us |  0.80 |         - |        0.00 |

| Method                   | Mean       | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |-----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListLastUpdated_Eager    | 224.332 us | 3.9234 us | 4.5182 us |  1.00 |    0.03 |      11 B |        1.00 |
| ListLastUpdated_Prepared | 219.435 us | 2.1722 us | 1.9256 us |  0.98 |    0.02 |      11 B |        1.00 |
| ListLastUpdated_Frozen   |   9.861 us | 0.1347 us | 0.1260 us |  0.04 |    0.00 |         - |        0.00 |

| Method                   | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_ListWhere_Eager    | 6.915 us | 0.0922 us | 0.0818 us |  1.00 |    0.02 |         - |          NA |
| Count_ListWhere_Prepared | 6.756 us | 0.0527 us | 0.0493 us |  0.98 |    0.01 |         - |          NA |
| Count_ListWhere_Frozen   | 5.768 us | 0.0503 us | 0.0471 us |  0.83 |    0.01 |         - |          NA |

| Method                  | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_ListList_Eager    | 8.931 us | 0.1623 us | 0.1355 us |  1.00 |    0.02 |       1 B |        1.00 |
| Count_ListList_Prepared | 8.742 us | 0.1422 us | 0.1397 us |  0.98 |    0.02 |       1 B |        1.00 |
| Count_ListList_Frozen   | 8.410 us | 0.0767 us | 0.0680 us |  0.94 |    0.02 |         - |        0.00 |

| Method                   | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------------- |----------:|---------:|---------:|------:|----------:|------------:|
| Count_ListRange_Eager    | 434.01 us | 3.095 us | 2.895 us |  1.00 |      20 B |        1.00 |
| Count_ListRange_Prepared | 433.50 us | 3.821 us | 3.575 us |  1.00 |      20 B |        1.00 |
| Count_ListRange_Frozen   |  10.14 us | 0.047 us | 0.042 us |  0.02 |         - |        0.00 |

**Unchanged executors (point lookup, stage-2 fused filters, replay fallbacks)**

| Method                    | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|-------------------------- |----------:|---------:|---------:|------:|----------:|------------:|
| Unique_Eager              | 131.91 ns | 0.452 ns | 0.378 ns |  1.00 |         - |          NA |
| Unique_Prepared           | 129.96 ns | 1.396 ns | 1.238 ns |  0.99 |         - |          NA |
| Unique_Frozen             |  27.19 ns | 0.295 ns | 0.276 ns |  0.21 |         - |          NA |
| Unique_FrozenDirectCall   |  23.36 ns | 0.233 ns | 0.218 ns |  0.18 |         - |          NA |
| Unique_FrozenBoundIntArgs |  18.94 ns | 0.268 ns | 0.251 ns |  0.14 |         - |          NA |

| Method             | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------- |----------:|---------:|---------:|------:|----------:|------------:|
| UniqueArg_Eager    | 131.52 ns | 0.559 ns | 0.495 ns |  1.00 |         - |          NA |
| UniqueArg_Prepared | 128.53 ns | 0.646 ns | 0.540 ns |  0.98 |         - |          NA |
| UniqueArg_Frozen   |  18.78 ns | 0.113 ns | 0.095 ns |  0.14 |         - |          NA |

| Method               | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------------------- |----------:|---------:|---------:|------:|----------:|------------:|
| UniqueWhere_Eager    | 149.72 ns | 0.780 ns | 0.652 ns |  1.00 |         - |          NA |
| UniqueWhere_Prepared | 132.06 ns | 0.404 ns | 0.315 ns |  0.88 |         - |          NA |
| UniqueWhere_Frozen   |  19.13 ns | 0.101 ns | 0.094 ns |  0.13 |         - |          NA |

| Method                  | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------ |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| UniqueArgWhere_Eager    | 165.50 ns | 0.912 ns | 0.854 ns |  1.00 | 0.0105 |      88 B |        1.00 |
| UniqueArgWhere_Prepared | 137.53 ns | 0.726 ns | 0.644 ns |  0.83 |      - |         - |        0.00 |
| UniqueArgWhere_Frozen   |  19.30 ns | 0.300 ns | 0.281 ns |  0.12 |      - |         - |        0.00 |

| Method                         | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListTwoWheres_Eager            | 8.144 us | 0.0508 us | 0.0450 us |  1.00 |    0.01 |      97 B |        1.00 |
| ListTwoWheres_Prepared         | 9.663 us | 0.0784 us | 0.0733 us |  1.19 |    0.01 |      97 B |        1.00 |
| ListTwoWheres_Frozen           | 7.393 us | 0.1285 us | 0.1202 us |  0.91 |    0.02 |         - |        0.00 |
| ListTwoWheres_FrozenFixedOrder | 9.112 us | 0.0862 us | 0.0806 us |  1.12 |    0.01 |         - |        0.00 |

| Method                                        | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| ListThreeWheresSelectiveLast_Eager            |  9.865 us | 0.0405 us | 0.0338 us |  1.00 | 0.0153 |     193 B |        1.00 |
| ListThreeWheresSelectiveLast_Prepared         | 11.678 us | 0.0900 us | 0.0798 us |  1.18 | 0.0153 |     193 B |        1.00 |
| ListThreeWheresSelectiveLast_Frozen           |  7.771 us | 0.0389 us | 0.0345 us |  0.79 |      - |         - |        0.00 |
| ListThreeWheresSelectiveLast_FrozenFixedOrder | 11.521 us | 0.0989 us | 0.0826 us |  1.17 |      - |         - |        0.00 |

| Method                    | Mean     | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|---------:|---------:|------:|-------:|----------:|------------:|
| ListTwoArgWheres_Eager    | 10.90 us | 0.068 us | 0.063 us |  1.00 | 0.0305 |     257 B |        1.00 |
| ListTwoArgWheres_Prepared | 13.44 us | 0.129 us | 0.107 us |  1.23 |      - |      97 B |        0.38 |
| ListTwoArgWheres_Frozen   | 12.58 us | 0.133 us | 0.124 us |  1.15 |      - |         - |        0.00 |

| Method           | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|----------------- |---------:|---------:|---------:|------:|----------:|------------:|
| JoinOne_Eager    | 31.34 us | 0.387 us | 0.343 us |  1.00 |       6 B |        1.00 |
| JoinOne_Prepared | 32.24 us | 0.333 us | 0.278 us |  1.03 |       6 B |        1.00 |
| JoinOne_Frozen   | 30.61 us | 0.363 us | 0.339 us |  0.98 |       5 B |        0.83 |

| Method         | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Match_Eager    | 8.513 us | 0.1032 us | 0.0965 us |  1.00 |    0.02 |       1 B |        1.00 |
| Match_Prepared | 8.458 us | 0.0707 us | 0.0627 us |  0.99 |    0.01 |       1 B |        1.00 |
| Match_Frozen   | 8.442 us | 0.0346 us | 0.0307 us |  0.99 |    0.01 |       1 B |        1.00 |

**Production shapes (replay in this step — the baseline for steps 3 / 5 / 6)**

| Method                                  | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|---------------------------------------- |---------:|---------:|---------:|------:|----------:|------------:|
| ListListListSortBoundedJoinOne_Eager    | 17.70 us | 0.180 us | 0.168 us |  1.00 |       1 B |        1.00 |
| ListListListSortBoundedJoinOne_Prepared | 17.36 us | 0.083 us | 0.069 us |  0.98 |       1 B |        1.00 |
| ListListListSortBoundedJoinOne_Frozen   | 16.11 us | 0.122 us | 0.114 us |  0.91 |       1 B |        1.00 |

| Method                                        | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_ListListListSortBoundedJoinOne_Eager    | 10.996 us | 0.1385 us | 0.1296 us |  1.00 |    0.02 |       1 B |        1.00 |
| Count_ListListListSortBoundedJoinOne_Prepared | 10.825 us | 0.1086 us | 0.1015 us |  0.98 |    0.01 |       1 B |        1.00 |
| Count_ListListListSortBoundedJoinOne_Frozen   |  9.687 us | 0.0968 us | 0.0905 us |  0.88 |    0.01 |         - |        0.00 |

| Method                                        | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|---------------------------------------------- |---------:|---------:|---------:|------:|----------:|------------:|
| TimeWindowListListSortBoundedJoinTwo_Eager    | 34.56 us | 0.303 us | 0.268 us |  1.00 |       6 B |        1.00 |
| TimeWindowListListSortBoundedJoinTwo_Prepared | 34.39 us | 0.333 us | 0.312 us |  1.00 |       6 B |        1.00 |
| TimeWindowListListSortBoundedJoinTwo_Frozen   | 34.04 us | 0.132 us | 0.110 us |  0.98 |       6 B |        1.00 |

| Method                                              | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|---------------------------------------------------- |---------:|---------:|---------:|------:|----------:|------------:|
| Count_TimeWindowListListSortBoundedJoinTwo_Eager    | 21.64 us | 0.177 us | 0.166 us |  1.00 |       3 B |        1.00 |
| Count_TimeWindowListListSortBoundedJoinTwo_Prepared | 21.48 us | 0.175 us | 0.155 us |  0.99 |       3 B |        1.00 |
| Count_TimeWindowListListSortBoundedJoinTwo_Frozen   | 21.57 us | 0.182 us | 0.161 us |  1.00 |       3 B |        1.00 |

| Method                                       | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------------------------------------------- |---------:|---------:|---------:|------:|----------:|------------:|
| TimeRangeListListSortBoundedJoinTwo_Eager    | 37.33 us | 0.091 us | 0.076 us |  1.00 |       6 B |        1.00 |
| TimeRangeListListSortBoundedJoinTwo_Prepared | 37.17 us | 0.516 us | 0.482 us |  1.00 |       6 B |        1.00 |
| TimeRangeListListSortBoundedJoinTwo_Frozen   | 37.00 us | 0.292 us | 0.259 us |  0.99 |       6 B |        1.00 |

| Method                                             | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------------------------------------------------- |---------:|---------:|---------:|------:|----------:|------------:|
| Count_TimeRangeListListSortBoundedJoinTwo_Eager    | 24.95 us | 0.102 us | 0.080 us |  1.00 |       3 B |        1.00 |
| Count_TimeRangeListListSortBoundedJoinTwo_Prepared | 24.86 us | 0.178 us | 0.167 us |  1.00 |       3 B |        1.00 |
| Count_TimeRangeListListSortBoundedJoinTwo_Frozen   | 24.87 us | 0.202 us | 0.179 us |  1.00 |       3 B |        1.00 |

`Count_TimeRangeListListSortBoundedJoinTwo` in the long sequential run measured prepared / frozen at 48.2 / 48.4 µs
(1.92, 6 B) against 25.1 eager while its Execute twin sat at 1.00 — the known in-process artifact
(parent spec §8 "Not observed as stable"); re-run alone (table above) it is 24.86 / 24.87 vs 24.95 (1.00, 3 B).

## Reading it

- **Keep / kill bars (design §12).** `ListRange` 406.7 → 12.09 µs (**33.6×**, bar ≥ 5×, kept; the
  predicted ~40× minus the value-side selector call the design measured at +4 ns/row) and its `Count`
  twin 434 → 10.1 µs (43×). `ListLastUpdated` 224 → 9.86 µs (**22.7×**, bar ≥ 5×): eager walks the
  50k-key window of the last-updated tree, the pipeline reads each candidate's own timestamp.
  `ListWhere` 9.79 → 7.02 µs (1.39×, bar ≥ 1.3×; the walk + `TryGet` floor the design put at ~6 µs).
  `Range` 12.94 → 8.82 µs and `OptionalRange` 12.96 → 8.78 µs (1.47×, bar ≥ 1.3×): the window's keys
  go to a flat buffer instead of a hash set. `ListKeySet` 12.31 → 9.90 µs (1.24×, bar ≥ 1.4× — **missed**:
  the value-side predicate is a delegate call per candidate, and the seed walk + `TryGet` floor is the
  same ~6 µs; the key-set copy under its lock is what the eager path also pays). `ListListReversed`
  1.33 → 1.03 µs (1.29×, bar ≥ 1.2×). `ListList` 9.63 → 8.91 µs (1.08×; the design's expectation for the
  fixed walk was ~1.3× — the 1k walk, 1k `TryGet`s and 1k selector calls are all still paid; the
  order-preserving small-probe seed of step 3 is what takes this row to ~1.2 µs, and the stage-2
  adaptive intersection stays reachable at 4.38 µs with `Pipeline = false`). `Count_ListWhere` 6.92 →
  5.77 µs (1.20×, bar ≥ 1.1×); `Count_ListList` 8.93 → 8.41 µs (1.06×, bar ≥ 5× — step 3's free seed).
  `Unique*` 18.8–27.2 ns and the stage-2 rows within noise of their stage-2 tables; `JoinOne` / `Match`
  replay at 0.98–0.99.
- **Allocation.** `-` on every pipeline row, execute and `Count`; the deterministic pins are
  `FrozenPipelineAllocationTests` (0 B pooled and 0 B `Count` in Release on list + where, list ∩ list,
  list ∩ range, range, optional range, list ∩ key-set, list ∩ last-updated, unique / list `In`, three
  fused filters).
- **Production shapes.** Replay in this step, as designed: `ListListListSortBoundedJoinOne` 17.7 µs eager /
  17.4 prepared / 16.1 frozen (the fused-filter-free replay with a capacity hint; the three-way
  intersection dominates), `Count` 11.0 / 10.8 / 9.7 µs. `TimeWindowListListSortBoundedJoinTwo` 34.6 /
  34.4 / 34.0 µs, `Count` 21.6 / 21.5 / 21.6 µs; the range-on-timestamp twin 37.3 / 37.2 / 37.0 µs and
  25.1 / 24.9 / 24.9 µs. Where the pipeline would seed once step 3 lands: the time window is ~1,500 of
  10k rows by the estimator (`LastUpdatedIndex.EstimateCount` / `CacheRangeIndex.EstimateCount`), key
  A's hot bucket ~3,333 and key B's ~2,000, so the time step is the smallest signal and keeps seeding
  as declared — then the two list probes are value-side field compares (≈ 3 ns per candidate against
  the two bucket intersections eager runs today), the SortBounded feed (step 6) takes the survivors and
  the fused JoinOnes (step 5) fill only the page rows.


## Lock vs lock-free (pipeline primitives) — `LockVsLockFreeBenchmarks`, `LockVsLockFreeConcurrencyBenchmarks`

Question: would plain collections behind one lock per batch (`HashSet`/`List` bucket copied out under `lock`,
`Dictionary` store read under `lock`) beat the lock-free machinery (`PooledSet` under a `ReaderGate` pin,
`ConcurrentCacheStore`) for the frozen pipeline's seed → probe → `TryGet` pass? Apple M4 Pro, .NET 9, Release,
`--inProcess`. Full tables: `BenchmarkDotNet.Artifacts/results/Prague.Benchmarks.LockVsLockFree*-report-github.md`.

**S1 — single reader (BDN, seed 1k, probe bucket 300):**

| Primitive | Lock-free | Locked (batch) | Locked (per call) |
|---|---:|---:|---:|
| Seed copy-out (1k) | 754 ns (`PooledSet` enumerate) | 759 ns `HashSet` / **37 ns `List`/array** | — |
| Probe ×1k | 4,179 ns | 2,663 ns (0.64) | 4,536 ns (1.09) |
| Composed pass | 5,443 ns | 4,420 ns `HashSet` (0.81) / 3,624 ns `List` (0.67) | — |

**S2 — 8 readers, no writer** (aggregate passes/s, p50/p99 per pass):

| Primitive | Lock-free | Locked (batch) |
|---|---:|---:|
| Seed copy `PooledSet` vs `HashSet`+`Lock` / `SpinLock` / `List`+`Lock` | 6.09M/s, p99 1.4 µs | 0.88M/s p99 178 µs / 1.29M/s p99 1.0 µs / 8.10M/s p99 6.3 µs |
| Probe | 903k/s, p99 9.8 µs | 300k/s, p99 601 µs |
| Store lookup | 1.84M/s, p99 7.3 µs | 152k/s, p99 1.5 ms |
| Composed | **669k/s, p99 12.9 µs** | 318k/s p99 208 µs (`HashSet`) / 47k/s p99 3.3 ms (`List`) |

**S3 — 8 readers + 1 writer at max rate** (reads/s, reader p99, writer ops/s):

| Primitive | Lock-free | Locked (batch) |
|---|---:|---:|
| Seed copy | 1.26M/s, p99 7.3 µs, **writer 11.0M/s** | `List`: 871k/s, p99 85 µs, writer 148k/s; `HashSet`: 121k/s, p99 1.7 ms, writer 43k/s |
| Probe | 95k/s, p99 342 µs, writer 3.06M/s | 47k/s, p99 78 µs, **writer 4.3k/s** |
| Store lookup | 255k/s, p99 50 µs, writer 7.65M/s | 29k/s, p99 5.2 ms, writer 19k/s |
| Composed | **69k/s, p99 827 µs, writer 2.77M/s** | 40k/s, p99 3.7 ms, **writer 6.7k/s** |

**Reading.** Locks win only with a single reader (composed 0.67–0.81). With 8 readers the lock serializes the
pass: throughput halves and p99 grows ~16×; with a writer present the writer is excluded on every reader batch
and collapses by ~400× (2.77M → 6.7k ops/s). Against the bars (must win S1 AND lose S3 by < 10% reads /
< 20% writer) the locked model fails S3 on both counts. **Decision: the lock-free model stays for
one-writer / many-readers; no locks enter the pipeline.**

Two things the experiment did teach: (1) a dense array copy is ~20× cheaper than `PooledSet` enumeration
for the seed (37 vs 754 ns per 1k) — a bulk `CopyKeysTo(Span)` on `PooledSet` that reads `Tables` once and
walks the slot array without per-slot volatile loads is a pipeline step-3 item; (2) reader p99 under a
max-rate writer (827 µs on the composed pass, and `ConcurrentCacheStore` reads swinging 45k–325k/s) is the
number the "one writer, many readers" acceptance run has to improve — bucket retirement and store lock
striping are the suspects.

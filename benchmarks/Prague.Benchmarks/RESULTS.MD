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


# Frozen queries — `BuildFrozen()` stage 3, step 3: seed selection

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §3.3 / §3.4 / §8, §13 step 3.
Apple M4 Pro, .NET 9.0 (osx-arm64), Release, `--inProcess`, default job, `[MemoryDiagnoser]`, one
category per run (the sweep script runs the categories back to back in separate processes). Same
100k-row store as the stage-3 tables above: group buckets 1k rows, tier buckets 100, band 333, lane 111.
Rows: `ListListList` (1k ∩ 333 ∩ 111 — production shape A's narrowing) and `Sort_ListList` (1k ∩ 100
under a classic `Sort`) are new; `ListList_FrozenIndexSteps` / `_FrozenEagerIntersection` are gone with
the stage-2 executor they measured.

| Method                 | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListList_Eager         | 9.086 us | 0.1809 us | 0.2084 us |  1.00 |    0.03 |       1 B |        1.00 |
| ListList_Prepared      | 9.036 us | 0.1569 us | 0.1744 us |  1.00 |    0.03 |       1 B |        1.00 |
| ListList_Frozen        | 1.721 us | 0.0153 us | 0.0143 us |  0.19 |    0.00 |         - |        0.00 |
| ListList_FrozenReorder | 1.349 us | 0.0270 us | 0.0277 us |  0.15 |    0.00 |         - |        0.00 |


| Method                         | Mean       | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------------------- |-----------:|---------:|---------:|------:|----------:|------------:|
| ListListReversed_Eager         | 1,312.9 ns |  9.22 ns |  8.63 ns |  1.00 |         - |          NA |
| ListListReversed_Prepared      | 1,295.7 ns | 15.56 ns | 14.55 ns |  0.99 |         - |          NA |
| ListListReversed_Frozen        |   993.5 ns |  4.55 ns |  3.80 ns |  0.76 |         - |          NA |
| ListListReversed_FrozenReorder | 1,003.1 ns |  8.64 ns |  8.08 ns |  0.76 |         - |          NA |


| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListListList_Eager    | 11.700 us | 0.2169 us | 0.2227 us |  1.00 |    0.03 |       1 B |        1.00 |
| ListListList_Prepared | 11.575 us | 0.1727 us | 0.1531 us |  0.99 |    0.02 |       1 B |        1.00 |
| ListListList_Frozen   |  2.249 us | 0.0229 us | 0.0215 us |  0.19 |    0.00 |         - |        0.00 |


| Method                  | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_ListList_Eager    | 8.898 us | 0.1502 us | 0.1405 us |  1.00 |    0.02 |       1 B |        1.00 |
| Count_ListList_Prepared | 8.749 us | 0.1228 us | 0.1089 us |  0.98 |    0.02 |       1 B |        1.00 |
| Count_ListList_Frozen   | 1.064 us | 0.0133 us | 0.0125 us |  0.12 |    0.00 |         - |        0.00 |


| Method                 | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Sort_ListList_Eager    | 10.509 us | 0.1660 us | 0.1553 us |  1.00 |    0.02 |       1 B |        1.00 |
| Sort_ListList_Prepared | 10.385 us | 0.1303 us | 0.1219 us |  0.99 |    0.02 |       1 B |        1.00 |
| Sort_ListList_Frozen   |  2.212 us | 0.0146 us | 0.0122 us |  0.21 |    0.00 |         - |        0.00 |


| Method                  | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListWhere_Eager         | 8.474 us | 0.0611 us | 0.0542 us |  1.00 |    0.01 |       1 B |        1.00 |
| ListWhere_Prepared      | 8.443 us | 0.0842 us | 0.0787 us |  1.00 |    0.01 |       1 B |        1.00 |
| ListWhere_Frozen        | 7.721 us | 0.1536 us | 0.3679 us |  0.91 |    0.04 |         - |        0.00 |
| ListWhere_FrozenNoHints | 9.656 us | 0.1919 us | 0.2987 us |  1.14 |    0.04 |         - |        0.00 |


| Method                   | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_ListWhere_Eager    | 6.637 us | 0.0816 us | 0.0763 us |  1.00 |    0.02 |         - |          NA |
| Count_ListWhere_Prepared | 6.577 us | 0.0346 us | 0.0289 us |  0.99 |    0.01 |         - |          NA |
| Count_ListWhere_Frozen   | 6.773 us | 0.1330 us | 0.1949 us |  1.02 |    0.03 |         - |          NA |


| Method              | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| ListKeySet_Eager    | 11.95 us | 0.067 us | 0.060 us |  1.00 |    0.01 |       1 B |        1.00 |
| ListKeySet_Prepared | 12.00 us | 0.160 us | 0.149 us |  1.00 |    0.01 |       1 B |        1.00 |
| ListKeySet_Frozen   | 10.37 us | 0.202 us | 0.207 us |  0.87 |    0.02 |         - |        0.00 |

`ListRange` 401 → 12.5 µs, `Count_ListRange` 424 → 11.1, `ListLastUpdated` 219 → 10.8, `Range` 12.7 → 8.8,
`OptionalRange` 12.8 → 9.1, `Unique*` 18–26 ns, the `ListTwo/ThreeWheres` rows, `JoinOne` / `Match` and the
six production-shape rows re-measured within noise of the step-2 tables (the sweep's full tables are in
`BenchmarkDotNet.Artifacts/results/`); the production shapes still replay (steps 5 / 6).

## Reading it

- **Bars (design §12, step 3).** `ListList` 9.09 → 1.72 µs (**5.3×**, bar ≥ 2×): the fixed seed now walks
  the 100-row tier bucket, locates each survivor in the 1k group bucket with `PooledSet.TryGetSlot` and
  sorts the survivors by slot — the eager sequence, byte for byte, at the small bucket's cost.
  `ListList_FrozenReorder` (free seed) 1.35 µs: the slot sort is the ~0.4 µs between the two.
  `ListListReversed` 1.31 → 0.99 µs (1.32×): already the small bucket first; the bulk seed copy and the
  value-side tier probe are the gain. `ListListList` 11.70 → 2.25 µs (**5.2×**, bar ≥ 3×): the lane bucket
  (111, declared last) is walked, slot-sorted into the group bucket, the band probed on the value.
  `Count_ListList` 8.90 → 1.06 µs (**8.4×**, bar ≥ 5×): `Count` seeds free — the smallest exact signal —
  and never sorts. `Sort_ListList` 10.51 → 2.21 µs (**4.75×**, bar ≥ 2×): a classic `Sort` seeds free and
  the eager container's `StableSort` runs on the 100 survivors; a total comparer gives the eager
  sequence, ties keep eager's rows per tie group in a different order inside it (`context/query.md`
  "Sorted paging" leaves classic ties unspecified).
- **Missed: `ListWhere`** 8.47 → 7.72 µs (1.10×, bar ≥ 1.5×) and **`ListKeySet`** 11.95 → 10.37 µs
  (1.15×, re-measure of the step-2 1.24×). Both are one 1k bucket walked and every candidate fetched:
  the floor is 1k × (`TryGet` ≈ 2.5 ns + one delegate call ≈ 2 ns + the container's `Add`), which no seed
  rule touches. The bulk copy (`PooledSet.CopyKeysTo`, 546 vs 761 ns per 1k bucket in isolation, 0.72×)
  is worth ~0.3 µs here, measured with `DOTNET_TieredPGO=0` (bulk 8.93 / enumerator 9.25 µs; `Count`
  7.76 / 8.07). With PGO on, the `ListWhere_Frozen` row wanders in a 7.1–8.6 µs band and needed 68
  measured iterations against the usual 15: dynamic PGO guards the predicate delegate call in the
  pipeline's shared `Run` with the first lambda it profiles, so one `FrozenQuery` instance gets a direct
  inlined `Flag` read and another the indirect call — `ListWhere_Frozen` and `ListWhere_FrozenNoHints`
  are the *same* plan (hints only apply to replays) and measure 7.7 vs 9.7 µs; with PGO off both are
  8.9 µs. A struct filter type parameter (deferred, parent spec "Planned") is the way past the delegate.
- **`ListTwoArgWheres`** 10.88 eager / 13.59 frozen (1.25 of eager; step 2 measured 1.15): the
  `(int group, int min, int max)` tuple is copied into each arg predicate per candidate through the fused
  filter's `in TArgs` path, on top of the two delegate calls. Not a seed-selection matter; carried to the
  filter/probe ordering work (design step 4's fused predicates over composites) — the eager `&&` closure
  captures the tuple once.
- **Executor retirement.** Every plan the stage-2 `IndexStepsExecutor` served (two or more equality steps,
  adaptive intersection; `ReorderIndexNarrowers`) takes the pipeline: `ListList` frozen is 1.72 µs against
  the stage-2 executor's 4.38 µs (adaptive intersection) and its `Reorder` 1.29 µs against the pipeline's
  free seed 1.35 µs (within noise). `AdaptiveIntersection` is removed from `FrozenOptions`;
  `ReorderIndexNarrowers` keeps its name and meaning (opt-in, changes encounter order).
- **Seeds under a writer.** The small-probe walk judges the small step by its index and the survivors
  by the first step's set; both are inside the documented staleness window (design §14.1) and are
  exercised by `FrozenPipelineSeedTests` (8 readers × a churning writer, no exception, no duplicate,
  the value-side probe on the small step's survivors). A free `Count` can miss a row whose index write
  has not landed where the fixed `Execute` returns it through another index — never the reverse
  (`FrozenPipelineTests.Staleness_RangeProbe_BothDirections…` pins both).


# Frozen queries — `BuildFrozen()` stage 3, step 6: the `SortBounded` feed

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §8, §7.3, §13 step 6.
Apple M4 Pro, .NET 9.0 (osx-arm64), Release, `--inProcess`, default job, `[MemoryDiagnoser]`, one
category per run (several categories chained back to back in one process where noted). Same 100k-row
item store as the stage-3 tables above (group buckets 1k rows, tier 100, band 333, lane 111; `Score`
random) and the 10k-record store of production shape B (timestamps over one hour, hot list keys A / B,
2k customers, 500 products). New rows: `SortBounded` (1k bucket, page 0..20), `SortBounded_ListList`
(1k ∩ 100, page 0..20), `Count_SortBounded`, `TimeWindowListListSortBounded` (shape B without its joins),
`TimeWindowListListSortBoundedJoinTwo_FrozenIndexSide` (shape B with the list steps probed on their
buckets). The six production-shape rows leave the replay: `_Frozen` is the joined pipeline.

| Method                        | Categories           | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|------------------------------ |--------------------- |----------:|----------:|----------:|------:|----------:|------------:|
| SortBounded_Eager             | SortBounded          | 15.852 us | 0.1207 us | 0.1070 us |  1.00 |         - |          NA |
| SortBounded_Prepared          | SortBounded          | 15.791 us | 0.1002 us | 0.0889 us |  0.99 |         - |          NA |
| SortBounded_Frozen            | SortBounded          | 14.961 us | 0.2634 us | 0.2464 us |  0.94 |         - |          NA |
|                               |                      |           |           |           |       |           |             |
| SortBounded_ListList_Eager    | SortBounded_ListList | 11.485 us | 0.1430 us | 0.1338 us |  1.00 |         - |          NA |
| SortBounded_ListList_Prepared | SortBounded_ListList | 11.584 us | 0.1166 us | 0.1091 us |  1.01 |         - |          NA |
| SortBounded_ListList_Frozen   | SortBounded_ListList |  3.655 us | 0.0304 us | 0.0284 us |  0.32 |         - |          NA |

(`SortBounded_Prepared` from the first sweep of the category; `_Eager` / `_Frozen` re-measured on the final
binary after the two inlining changes below — 15.90 / 17.22 before them.)

| Method                     | Categories        | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------------- |------------------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Count_SortBounded_Eager    | Count_SortBounded |  5.088 us | 0.0409 us | 0.0382 us |  1.00 |    0.01 |         - |          NA |
| Count_SortBounded_Prepared | Count_SortBounded |  4.954 us | 0.0583 us | 0.0545 us |  0.97 |    0.01 |         - |          NA |
| Count_SortBounded_Frozen   | Count_SortBounded |  5.388 us | 0.1069 us | 0.1726 us |  1.06 |    0.03 |         - |          NA |


| Method                                        | Categories                           | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------------------------- |------------------------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ListListListSortBoundedJoinOne_Eager          | ListListListSortBoundedJoinOne       | 17.049 us | 0.2015 us | 0.1884 us |  1.00 |    0.02 |         - |          NA |
| ListListListSortBoundedJoinOne_Prepared       | ListListListSortBoundedJoinOne       | 16.844 us | 0.2692 us | 0.2518 us |  0.99 |    0.02 |         - |          NA |
| ListListListSortBoundedJoinOne_Frozen         | ListListListSortBoundedJoinOne       |  7.714 us | 0.0700 us | 0.0585 us |  0.45 |    0.01 |         - |          NA |
|                                               |                                      |           |           |           |       |         |           |             |
| Count_ListListListSortBoundedJoinOne_Eager    | Count_ListListListSortBoundedJoinOne | 10.938 us | 0.1201 us | 0.1124 us |  1.00 |    0.01 |         - |          NA |
| Count_ListListListSortBoundedJoinOne_Prepared | Count_ListListListSortBoundedJoinOne | 10.643 us | 0.0859 us | 0.0804 us |  0.97 |    0.01 |         - |          NA |
| Count_ListListListSortBoundedJoinOne_Frozen   | Count_ListListListSortBoundedJoinOne |  1.412 us | 0.0130 us | 0.0115 us |  0.13 |    0.00 |         - |          NA |


| Method                                               | Categories                                 | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------------------------------------- |------------------------------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| TimeWindowListListSortBoundedJoinTwo_Eager           | TimeWindowListListSortBoundedJoinTwo       | 34.043 us | 0.1515 us | 0.1343 us |  1.00 |    0.01 |         - |          NA |
| TimeWindowListListSortBoundedJoinTwo_Prepared        | TimeWindowListListSortBoundedJoinTwo       | 33.469 us | 0.3420 us | 0.3199 us |  0.98 |    0.01 |         - |          NA |
| TimeWindowListListSortBoundedJoinTwo_Frozen          | TimeWindowListListSortBoundedJoinTwo       | 26.816 us | 0.1093 us | 0.0913 us |  0.79 |    0.00 |         - |          NA |
| TimeWindowListListSortBoundedJoinTwo_FrozenIndexSide | TimeWindowListListSortBoundedJoinTwo       | 29.074 us | 0.2156 us | 0.1911 us |  0.85 |    0.01 |         - |          NA |
|                                                      |                                            |           |           |           |       |         |           |             |
| TimeWindowListListSortBounded_Eager                  | TimeWindowListListSortBounded              | 25.058 us | 0.0774 us | 0.0604 us |  1.00 |    0.00 |         - |          NA |
| TimeWindowListListSortBounded_Frozen                 | TimeWindowListListSortBounded              | 17.438 us | 0.0662 us | 0.0553 us |  0.70 |    0.00 |         - |          NA |
|                                                      |                                            |           |           |           |       |         |           |             |
| Count_TimeWindowListListSortBoundedJoinTwo_Eager     | Count_TimeWindowListListSortBoundedJoinTwo | 21.70 us  | 0.224 us  | 0.209 us  |  1.00 |    0.01 |         - |          NA |
| Count_TimeWindowListListSortBoundedJoinTwo_Prepared  | Count_TimeWindowListListSortBoundedJoinTwo | 21.33 us  | 0.152 us  | 0.135 us  |  0.98 |    0.01 |         - |          NA |
| Count_TimeWindowListListSortBoundedJoinTwo_Frozen    | Count_TimeWindowListListSortBoundedJoinTwo | 13.99 us  | 0.054 us  | 0.042 us  |  0.64 |    0.01 |         - |          NA |
|                                                      |                                            |           |           |           |       |         |           |             |
| TimeRangeListListSortBoundedJoinTwo_Eager | TimeRangeListListSortBoundedJoinTwo | 37.77 us | 0.480 us | 0.449 us | 1.00 | 0.02 | - | NA |
| TimeRangeListListSortBoundedJoinTwo_Prepared | TimeRangeListListSortBoundedJoinTwo | 37.25 us | 0.477 us | 0.446 us | 0.99 | 0.02 | - | NA |
| TimeRangeListListSortBoundedJoinTwo_Frozen | TimeRangeListListSortBoundedJoinTwo | 27.93 us | 0.385 us | 0.360 us | 0.74 | 0.01 | - | NA |
|                                                      |                                            |           |           |           |       |         |           |             |
| Count_TimeRangeListListSortBoundedJoinTwo_Eager | Count_TimeRangeListListSortBoundedJoinTwo | 25.09 us | 0.180 us | 0.159 us | 1.00 | 0.01 | - | NA |
| Count_TimeRangeListListSortBoundedJoinTwo_Prepared | Count_TimeRangeListListSortBoundedJoinTwo | 25.09 us | 0.427 us | 0.400 us | 1.00 | 0.02 | - | NA |
| Count_TimeRangeListListSortBoundedJoinTwo_Frozen | Count_TimeRangeListListSortBoundedJoinTwo | 16.76 us | 0.212 us | 0.198 us | 0.67 | 0.01 | - | NA |

(The `TimeRange…` / `Count_TimeRange…` rows above are re-measured on the final tree, after the store-lookup inlining hint: the B-range twin's frozen row moved 29.59 → 27.93 µs (1.35× eager), its `Count` stayed at 16.76 (1.50×); the `Count_TimeRange…_Prepared` 1.95 outlier of the first run — the known in-process artifact, parent spec §8 "Not observed as stable" — is 1.00 on the re-run.)

Re-measured on the final binary, same run: `ListList` 9.39 / 9.37 / **1.70** (`_FrozenReorder` 1.26),
`ListListList` 11.48 / 11.46 / **2.18**, `Sort_ListList` 10.34 / 10.28 / **2.14**, `ListWhere` 8.49 / — /
7.42 (0.87), `Count_ListWhere` 6.69 / 6.70 / 6.08 (0.91; step 3: 6.77), `ListRange` 430 / 430 / 11.3 µs
(step 3: 12.5), `Range` 13.38 / 13.32 / **6.85** (0.51; step 3: 8.8 — the inlined per-key lookup is worth
~2 µs on a 1k tree-seeded walk), `UniqueArg` 133 / 130 / 21.5 ns (unchanged) — nothing regressed; every
pipeline row 0 B.

## Reading it

- **Bars (design §12, step 6).** `SortBounded_ListList` 11.49 → 3.66 µs (**3.1×**): the small-probe seed
  walks the 100-row tier bucket, the heap of 20 sees 100 rows; the page is eager's byte for byte because
  the fixed seed hands the top-k container eager's encounter ordinals. **Shape A**
  (`ListListListSortBoundedJoinOne`) 17.05 → 7.71 µs (**2.2×**, bar ≥ 2× kept): the narrowing is the
  step-3 `ListListList` walk (2.18 µs), the page is 20 of 111 rows, and the `JoinOne` fill runs
  unchanged over those 20 rows (`ExecuteJoinsBounded`, one paired read) — the remaining ~5 µs is that
  fill, step 5's target. Its `Count` 10.94 → 1.41 µs (7.7×): an outer join counts every left, so this is
  the free-seeded three-way intersection with no container.
- **Missed: `SortBounded`** (1k bucket, page 0..20) 15.85 → 14.96 µs (**1.06×**, bar ≥ 1.1×). Both
  engines pay 1k × (one store lookup + one `Score` read on a cold `PqbItem` + one heap compare) ≈ 13–14 µs;
  the pipeline removes only eager's candidate-set build (~1 µs on a 1k bucket, the same gap `ListWhere`
  shows). Two inlining changes were needed to get even there: the probe-free pass (`PipelineCore.WalkPlain`,
  one active step, direct filters) is a loop as small as the eager store walk so the container's `Add` →
  `TopKSelect.Push` → comparer chain inlines (17.22 → 16.27 µs), and `ConcurrentCacheStore.TryGetValue`
  / `InMemoryDataCache.TryGet` carry `AggressiveInlining` so the per-key lookup is the loop body as it is
  in the store's own `TryGetValues` / `TryCountValues` (16.27 → 14.96; `Count_SortBounded_Frozen` 7.52 →
  5.39, now parity with eager's count, which never dereferences a value). An `AggressiveInlining` on
  `SortResolver.CompareLeftValues` alone measured nothing and was not kept.
- **Missed: shape B** (`TimeWindowListListSortBoundedJoinTwo`) 34.04 → 26.82 µs (**1.27×**, bar ≥ 1.5×); the
  range-on-timestamp twin 37.77 → 27.93 (1.35×). The `Count`s make their bar (21.70 → 13.99, 1.55×; 25.09 →
  16.76, 1.50×). Decomposition from the new no-join row: the narrowing + page alone is 25.06 → 17.44 µs
  (1.44×), so the two `JoinOne` fills are **9.4 µs of the frozen row** (9.0 of the eager one): two pair-set
  builds and two paired bulk reads for 20 rows — the fusion of design step 5 (one right lookup per page
  row, ~0.3 µs for 40 lookups) is what closes this bar, not the seed. The narrowing's own 17.4 µs is the
  floor of the shape: the time window (~1.5k keys) is already the smallest source, so the fixed seed is the
  free seed's choice too, and every one of its keys costs a lookup plus two selector compares to reject
  the ~93 % that fail the list keys. The key-side alternative was measured and refuted:
  `_FrozenIndexSide` (the two list steps probed on their buckets before the lookup) is **slower**, 29.07 µs —
  two gate-pinned `Contains` per key cost more than the lookup plus two field compares, the design's §2.3
  finding; the value-side default stands.
- **Executor change.** `PipelineExecutor` is now `PipelineCore` (bind, seed, walk, count, release — held
  by value) plus two thin executors: the simple one applies the eager `ExecuteCoreSimpleTop` gate per call
  (`take != int.MaxValue`, non-negative page, a bounded left-value sorter — the resolver half decided
  once at build) and drives `TopKSimpleResultContainer` or the classic container; the new
  `PipelineJoinedExecutor` drives `TopKJoinedBaseContainer` → `MaterializeTopK` → `ExecuteJoinsBounded`
  (the eager `ExecuteCoreJoinedTop` with the base walk replaced), or the classic joined container with
  `ExecuteJoins` when the gate fails. Planner: joined + `Pipeline` + `SortBounded` innermost ordering by
  the left value + no inner join + no `JoinMany` + pipeline-capable steps → `Pipeline`. `Explain()` prints
  `sort: bounded (…)` / `sort: classic (…)` and `joins: n (outer, …)`.
- **Found on the way.** `JoinedResultContaier.BuildResults` set `_handedOff` before `CloneElements`, so a
  throwing `Clone()` on a pooled, cloned joined page stranded the values array — on the eager path too.
  It now hands off after the clone (the simple container's order); pinned for eager and frozen in
  `FrozenPipelineSortBoundedTests`.


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

### Full tables and per-primitive reading

The complete BDN and harness tables behind the summary above, and how each primitive was modelled.

Model: 100k store rows (`PqbItem`), seed bucket of 1k / 300 / 100 int keys (`i * 100`, spanning the
store), a 300-key probe bucket hitting 30% of the seed; every reader owns its key buffers (no
allocation per pass). Composed pass = copy the seed → probe every key against the 300-bucket → fetch
the hits from the store. Every locked *batched* form is `lock { return Core(...) }` with the loop in a
`NoInlining` core — a loop written inside the lock's `try` region pays the JIT's EH-write-thru spills
on its locals (first cut: `Store_Dictionary_LockBatch` 12.4 µs vs 9.6 µs *per call*; split: 4.58 µs).
Same split as `PooledSet.Contains` around its gate pin, so both sides get the same codegen.

Apple M4 Pro (12 cores), .NET 9.0.19 (osx-arm64). S1: `--inProcess`, default job, `[MemoryDiagnoser]`
(0 B on every row). S2 / S3: wall-clock harness hosted in BDN (`InProcessNoEmit`, 1 warmup + 5
measured runs of 2,000 ms each, run WITHOUT `--inProcess` — the CLI flag stacks a second job on the
class's own): 8 reader threads, `Writers = 1` adds one thread doing `Remove` + re-`Add` of a seed key
and a probe key plus a store `AddOrUpdate` per iteration at max rate (five mutating calls = five
writer ops; the lock-free side keeps its single-writer contract, the locked writer takes the lock per
call). Reads/s = aggregate passes per second; p50 / p99 = per-pass latency merged over the readers
(first 524k passes per reader sampled); each cell is the median of the 5 runs with `[min..max]`.

#### S1 — single reader, no writer (BDN)

| Method                       | Categories | SeedSize | Mean         | Error      | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------------- |----------- |--------- |-------------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| Composed_LockFree            | Composed   | 100      |   519.494 ns |  1.6652 ns |  1.4762 ns |  1.00 |    0.00 |         - |          NA |
| Composed_HashSet_LockBatch   | Composed   | 100      |   429.165 ns |  1.5716 ns |  1.3932 ns |  0.83 |    0.00 |         - |          NA |
| Composed_List_LockBatch      | Composed   | 100      |   363.597 ns |  1.3734 ns |  1.2847 ns |  0.70 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Composed_LockFree            | Composed   | 300      | 1,608.989 ns |  9.8813 ns |  9.2430 ns |  1.00 |    0.01 |         - |          NA |
| Composed_HashSet_LockBatch   | Composed   | 300      | 1,276.111 ns |  2.0400 ns |  1.5927 ns |  0.79 |    0.00 |         - |          NA |
| Composed_List_LockBatch      | Composed   | 300      | 1,100.654 ns |  7.7577 ns |  7.2565 ns |  0.68 |    0.01 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Composed_LockFree            | Composed   | 1000     | 5,442.531 ns | 46.0061 ns | 43.0342 ns |  1.00 |    0.01 |         - |          NA |
| Composed_HashSet_LockBatch   | Composed   | 1000     | 4,419.575 ns | 36.9446 ns | 32.7504 ns |  0.81 |    0.01 |         - |          NA |
| Composed_List_LockBatch      | Composed   | 1000     | 3,623.517 ns | 30.3089 ns | 28.3510 ns |  0.67 |    0.01 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Probe_PooledSet              | Probe      | 100      |   414.240 ns |  0.8446 ns |  0.7053 ns |  1.00 |    0.00 |         - |          NA |
| Probe_HashSet_LockPerCall    | Probe      | 100      |   444.891 ns |  2.8321 ns |  2.6491 ns |  1.07 |    0.01 |         - |          NA |
| Probe_HashSet_LockBatch      | Probe      | 100      |   260.025 ns |  1.1393 ns |  1.0100 ns |  0.63 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Probe_PooledSet              | Probe      | 300      | 1,247.270 ns |  3.3853 ns |  2.6430 ns |  1.00 |    0.00 |         - |          NA |
| Probe_HashSet_LockPerCall    | Probe      | 300      | 1,315.270 ns |  5.4913 ns |  4.2873 ns |  1.05 |    0.00 |         - |          NA |
| Probe_HashSet_LockBatch      | Probe      | 300      |   795.711 ns |  3.6612 ns |  3.4246 ns |  0.64 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Probe_PooledSet              | Probe      | 1000     | 4,179.188 ns | 30.3245 ns | 28.3656 ns |  1.00 |    0.01 |         - |          NA |
| Probe_HashSet_LockPerCall    | Probe      | 1000     | 4,536.305 ns | 18.7961 ns | 17.5819 ns |  1.09 |    0.01 |         - |          NA |
| Probe_HashSet_LockBatch      | Probe      | 1000     | 2,663.319 ns | 19.6730 ns | 18.4021 ns |  0.64 |    0.01 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| SeedCopy_PooledSet           | SeedCopy   | 100      |    69.528 ns |  0.6186 ns |  0.5786 ns |  1.00 |    0.01 |         - |          NA |
| SeedCopy_HashSet_Lock        | SeedCopy   | 100      |    81.043 ns |  0.4837 ns |  0.4524 ns |  1.17 |    0.01 |         - |          NA |
| SeedCopy_HashSet_SpinLock    | SeedCopy   | 100      |    79.639 ns |  0.6688 ns |  0.6256 ns |  1.15 |    0.01 |         - |          NA |
| SeedCopy_List_Lock           | SeedCopy   | 100      |     7.168 ns |  0.0815 ns |  0.0681 ns |  0.10 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| SeedCopy_PooledSet           | SeedCopy   | 300      |   223.238 ns |  0.6792 ns |  0.5672 ns |  1.00 |    0.00 |         - |          NA |
| SeedCopy_HashSet_Lock        | SeedCopy   | 300      |   233.244 ns |  1.7077 ns |  1.5973 ns |  1.04 |    0.01 |         - |          NA |
| SeedCopy_HashSet_SpinLock    | SeedCopy   | 300      |   231.598 ns |  1.4043 ns |  1.3136 ns |  1.04 |    0.01 |         - |          NA |
| SeedCopy_List_Lock           | SeedCopy   | 300      |    13.534 ns |  0.0627 ns |  0.0587 ns |  0.06 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| SeedCopy_PooledSet           | SeedCopy   | 1000     |   754.503 ns |  4.8688 ns |  4.3160 ns |  1.00 |    0.01 |         - |          NA |
| SeedCopy_HashSet_Lock        | SeedCopy   | 1000     |   758.956 ns |  6.5745 ns |  6.1498 ns |  1.01 |    0.01 |         - |          NA |
| SeedCopy_HashSet_SpinLock    | SeedCopy   | 1000     |   758.528 ns |  5.9451 ns |  5.5611 ns |  1.01 |    0.01 |         - |          NA |
| SeedCopy_List_Lock           | SeedCopy   | 1000     |    37.506 ns |  0.2930 ns |  0.2446 ns |  0.05 |    0.00 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Store_ConcurrentCacheStore   | Store      | 100      |   133.328 ns |  2.2225 ns |  2.0789 ns |  1.00 |    0.02 |         - |          NA |
| Store_Dictionary_LockPerCall | Store      | 100      |   505.231 ns |  2.4500 ns |  2.1719 ns |  3.79 |    0.06 |         - |          NA |
| Store_Dictionary_LockBatch   | Store      | 100      |   261.328 ns |  0.6011 ns |  0.5020 ns |  1.96 |    0.03 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Store_ConcurrentCacheStore   | Store      | 300      |   523.219 ns | 10.1621 ns |  9.5056 ns |  1.00 |    0.02 |         - |          NA |
| Store_Dictionary_LockPerCall | Store      | 300      | 1,583.777 ns | 10.2015 ns |  9.0434 ns |  3.03 |    0.06 |         - |          NA |
| Store_Dictionary_LockBatch   | Store      | 300      |   950.991 ns |  7.7147 ns |  7.2163 ns |  1.82 |    0.03 |         - |          NA |
|                              |            |          |              |            |            |       |         |           |             |
| Store_ConcurrentCacheStore   | Store      | 1000     | 2,455.723 ns | 27.2515 ns | 25.4911 ns |  1.00 |    0.01 |         - |          NA |
| Store_Dictionary_LockPerCall | Store      | 1000     | 9,162.455 ns | 64.6387 ns | 57.3005 ns |  3.73 |    0.04 |         - |          NA |
| Store_Dictionary_LockBatch   | Store      | 1000     | 4,584.636 ns | 87.2166 ns | 89.5650 ns |  1.87 |    0.04 |         - |          NA |

#### S2 (`Writers = 0`) — 8 readers, no writer; S3 (`Writers = 1`) — 8 readers + 1 writer at max rate

| Method | Writers | Reads/s | p50 | p99 | Writer ops/s |
|------------------------------|---------|---------|-----|-----|--------------|
| SeedCopy_PooledSet | 0 | 6,092,391 [5,885,951..6,455,593] | 1.17 us [1.17 us..1.29 us] | 1.42 us [1.29 us..1.46 us] | 0 [0..0] |
| SeedCopy_HashSet_Lock | 0 | 882,238 [881,110..889,056] | 0.96 us [0.96 us..0.96 us] | 178.17 us [159.75 us..203.38 us] | 0 [0..0] |
| SeedCopy_HashSet_SpinLock | 0 | 1,288,630 [1,268,922..1,292,324] | 0.75 us [0.75 us..0.75 us] | 0.96 us [0.96 us..0.96 us] | 0 [0..0] |
| SeedCopy_List_Lock | 0 | 8,099,825 [7,947,192..8,170,468] | 0.12 us [0.08 us..0.12 us] | 6.25 us [6.04 us..6.42 us] | 0 [0..0] |
| Probe_PooledSet | 0 | 902,556 [840,658..921,374] | 8.33 us [8.29 us..9.54 us] | 9.83 us [9.71 us..14.67 us] | 0 [0..0] |
| Probe_HashSet_LockPerCall | 0 | 12,098 [9,475..17,184] | 672.33 us [137.54 us..886.54 us] | 1833.83 us [1772.00 us..1906.92 us] | 0 [0..0] |
| Probe_HashSet_LockBatch | 0 | 299,653 [299,498..301,995] | 3.33 us [3.29 us..3.33 us] | 601.04 us [372.71 us..699.71 us] | 0 [0..0] |
| Store_ConcurrentCacheStore | 0 | 1,841,011 [1,627,147..1,867,773] | 4.12 us [4.08 us..4.62 us] | 7.25 us [4.83 us..7.92 us] | 0 [0..0] |
| Store_Dictionary_LockPerCall | 0 | 13,156 [12,336..17,996] | 597.12 us [427.21 us..628.83 us] | 1309.12 us [1228.71 us..1348.33 us] | 0 [0..0] |
| Store_Dictionary_LockBatch | 0 | 151,796 [150,169..154,013] | 6.33 us [6.25 us..6.46 us] | 1540.33 us [1492.54 us..1609.12 us] | 0 [0..0] |
| Composed_LockFree | 0 | 669,023 [622,508..678,609] | 12.25 us [12.21 us..12.25 us] | 12.88 us [12.71 us..17.38 us] | 0 [0..0] |
| Composed_HashSet_LockBatch | 0 | 317,697 [48,816..346,065] | 6.04 us [5.71 us..35.75 us] | 207.62 us [88.21 us..3091.17 us] | 0 [0..0] |
| Composed_List_LockBatch | 0 | 46,657 [36,557..55,888] | 36.04 us [33.79 us..36.17 us] | 3330.50 us [1688.50 us..4984.04 us] | 0 [0..0] |
|------------------------------|---------|---------|-----|-----|--------------|
| SeedCopy_PooledSet | 1 | 1,264,292 [1,139,774..1,477,700] | 5.12 us [5.12 us..5.17 us] | 7.33 us [7.08 us..7.54 us] | 10,982,151 [10,147,975..11,725,320] |
| SeedCopy_HashSet_Lock | 1 | 120,656 [118,962..125,433] | 5.17 us [5.12 us..5.33 us] | 1717.75 us [1572.62 us..1957.25 us] | 43,433 [38,446..51,315] |
| SeedCopy_HashSet_SpinLock | 1 | 205,632 [197,665..222,045] | 4.83 us [4.25 us..4.83 us] | 6.04 us [5.83 us..9.00 us] | 71,585 [1,172..135,172] |
| SeedCopy_List_Lock | 1 | 870,858 [795,508..1,037,587] | 0.83 us [0.54 us..1.08 us] | 84.88 us [47.50 us..358.96 us] | 147,570 [118,270..184,540] |
| Probe_PooledSet | 1 | 95,371 [91,700..97,866] | 42.58 us [39.62 us..43.88 us] | 341.83 us [335.54 us..402.88 us] | 3,060,856 [2,606,672..3,205,649] |
| Probe_HashSet_LockPerCall | 1 | 13,798 [12,403..15,791] | 93.67 us [54.50 us..128.42 us] | 4720.42 us [4478.21 us..6066.25 us] | 329,019 [294,906..376,324] |
| Probe_HashSet_LockBatch | 1 | 47,360 [44,827..48,478] | 17.38 us [17.33 us..17.46 us] | 77.83 us [59.08 us..116.00 us] | 4,304 [2,679..13,802] |
| Store_ConcurrentCacheStore | 1 | 255,066 [44,973..324,571] | 17.75 us [16.71 us..18.17 us] | 49.58 us [47.58 us..1190.92 us] | 7,647,243 [1,510,087..9,620,842] |
| Store_Dictionary_LockPerCall | 1 | 4,721 [2,083..5,854] | 518.96 us [82.71 us..3520.38 us] | 10880.83 us [9692.08 us..15008.96 us] | 189,466 [138,490..211,762] |
| Store_Dictionary_LockBatch | 1 | 28,579 [25,503..29,745] | 32.00 us [31.04 us..33.21 us] | 5178.04 us [2680.54 us..7246.00 us] | 19,249 [12,544..27,467] |
| Composed_LockFree | 1 | 69,312 [67,119..80,958] | 46.58 us [45.75 us..47.42 us] | 827.00 us [665.46 us..900.33 us] | 2,766,817 [2,508,367..3,081,300] |
| Composed_HashSet_LockBatch | 1 | 39,980 [32,949..43,420] | 36.33 us [35.58 us..44.38 us] | 3669.21 us [3256.04 us..3778.46 us] | 6,679 [5,787..10,059] |
| Composed_List_LockBatch | 1 | 28,607 [27,160..33,874] | 42.00 us [38.54 us..46.71 us] | 2725.17 us [1956.25 us..3535.12 us] | 10,391 [8,846..14,231] |

#### Reading it

- **Seed copy-out.** Single-threaded the pooled walk and the `HashSet` walk under a lock are the same
  (0.755 vs 0.759 µs for 1k, 0.75 ns/slot); the lock costs nothing uncontended and `SpinLock` is the
  same. The plain array (`List<int>`, a 4 KB `CopyTo`) is **20×** faster (37.5 ns) — that is a data
  layout win, not a lock win. Eight readers: the pooled walk scales to 6.09M passes/s (p99 1.42 µs);
  the locked `HashSet` copy serializes at 0.88M/s with a 178 µs p99 (a 1 µs critical section still
  convoys eight threads through one `Lock`); `SpinLock` 1.29M/s. The locked array copy reaches
  8.1M/s (1.33× the pooled walk) but its p99 is 6.25 µs (4.4× worse). Add the writer: pooled
  1.26M/s at 11.0M writer ops/s; locked `HashSet` 0.12M/s (−90%) at 43k writer ops/s (−99.6%);
  locked array 0.87M/s (−31%) at 148k writer ops/s (−98.7%, and that writer pays `List.Remove`'s
  O(n) scan). The writer stall is the cost of readers holding the lock, exactly as feared.
- **Probe (1k `Contains` against 300).** Per-call locking loses even single-threaded (1.09×): a
  `Lock` enter/exit pair is dearer than a gate pin. Batched `HashSet.Contains` under one lock wins S1
  at 0.64× (2.66 vs 4.18 µs — 2.7 vs 4.2 ns/key; roughly 1.5 ns of the gap is the per-call
  `ReaderGate.Enter/Exit`, the rest `HashSet<int>`'s tighter int lookup). At 8 readers the pooled
  probe does 903k/s (p99 9.8 µs); the batched lock 300k/s (**3× fewer**, p99 601 µs); per-call 12k/s
  (75× fewer). With the writer: pooled 95k/s at 3.06M writer ops/s; batched lock 47k/s (−50%) at
  **4.3k writer ops/s (−99.9%)** — a 2.7 µs critical section held by eight readers in rotation starves
  a per-call writer completely.
- **Store lookup (1k `TryGetValue`).** `ConcurrentCacheStore` wins every scenario: 2.46 µs vs 4.58
  batched (1.87×) / 9.16 per-call (3.7×) single-threaded — `Dictionary<int,T>`'s lookup is simply
  slower than the store's volatile bucket walk here, the lock is not the difference. 8 readers: 1.84M/s
  vs 152k/s (12×) / 13k/s (140×); with the writer 255k/s at 7.6M writer ops/s vs 28.6k/s at 19k writer
  ops/s.
- **Composed pass (the number that matters).** S1: lock-free 5.44 µs = 0.75 copy + 4.18 probe + 0.52
  store(300); locked `HashSet` 4.42 µs (**0.81×**) = 0.76 + 2.66 + 0.95; locked array 3.62 µs
  (**0.67×**) = 0.04 + 2.66 + 0.95 — the whole single-threaded win is the batched probe (−1.5 µs) and
  the array copy (−0.7 µs), minus the `Dictionary` penalty (+0.4 µs). S2: lock-free 669k passes/s
  (p99 12.9 µs); locked `HashSet` 318k/s (**−53%**, p99 208 µs, run spread 49k..346k); locked array
  47k/s (**−93%**, p50 36 µs, p99 3.3 ms — the near-free copy delivers all eight readers to the probe
  lock in lockstep and the convoy is worse). S3: lock-free 69.3k/s at 2.77M writer ops/s; locked
  `HashSet` 40.0k/s (**−42%** reads) at 6.7k writer ops/s (**−99.8%**); locked array 28.6k/s (**−59%**)
  at 10.4k writer ops/s (**−99.6%**).
- **Lock-free under churn.** The writer at max rate costs the lock-free side a lot too (composed 669k →
  69k passes/s; seed copy 6.09M → 1.26M): eight readers walking the same 12 KB slot array that a
  writer rewrites ~10M times a second is cache-line ping-pong, not gate cost (`Remove` + `Add` never
  retires a generation). It is an adversarial rate — a 100 Hz change stream is five orders of
  magnitude slower — and the lock-free side degrades gracefully (p50 47 µs, no starvation) where the
  locked side starves the writer.

**Verdict.** The plain-collection-plus-lock pipeline wins S1 (0.81× / 0.67×) but fails both S3 bars by a
wide margin (reads −42% / −59% against a ≤ 10% loss bar; writer −99.8% / −99.6% against a ≤ 20% bar) and
loses S2 outright (−53% / −93%). Not worth building. What the measurement does say is worth taking:
(1) a **batched, gate-pinned probe** on `PooledSet` (`ContainsMany(ReadOnlySpan<T>, Span<bool>)` —
one `ReaderGate.Enter/Exit` per batch instead of per key) would recover most of the 1.5 µs / 1k the
locked probe wins single-threaded, with no lock and no contract change; (2) the 20× faster array
copy-out argues for a **dense key array maintained by the writer alongside the hash slots** (swap-remove,
published through the same `Tables` generation) so the seed copy becomes a `CopyTo` under the existing
pin — that is a layout change inside the current reclamation model, not a locking change.


# Frozen queries — `BuildFrozen()` stage 3, step 5: `JoinOne` fusion

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §7.1 (fusing `JoinOne`),
§7.3 (sort after join), §9 (the planner rule), §13 step 5. Apple M4 Pro, .NET 9.0 (osx-arm64), Release,
`--inProcess`, default job, `[MemoryDiagnoser]`, one category per run. Same stores as the stage-3 tables
above; the `JoinOne` rows walk one 1k customer bucket of the 100k-row order store. New rows:
`InnerJoinOne` (inner PK-to-PK, every fourth right missing), `JoinOneChained` (outer left-symmetric →
outer PK-to-PK), `JoinOneFiltered` (a right-side filter callback — the replay fallback). Four families ×
outer/inner × identity/selector, chained, with or without one `Sort` / `SortBounded`, now leave the replay.

| Method                        | Categories      | Mean      | Ratio | Allocated |
|------------------------------ |---------------- |----------:|------:|----------:|
| JoinOne_Eager                 | JoinOne         | 31.67 us  |  1.00 |         - |
| JoinOne_Prepared              | JoinOne         | 30.57 us  |  0.97 |         - |
| JoinOne_Frozen                | JoinOne         | **20.57 us** | 0.65 |      - |
|                               |                 |           |       |           |
| InnerJoinOne_Eager            | InnerJoinOne    | 34.34 us  |  1.00 |         - |
| InnerJoinOne_Prepared         | InnerJoinOne    | 34.05 us  |  0.99 |         - |
| InnerJoinOne_Frozen           | InnerJoinOne    | **17.68 us** | 0.51 |      - |
|                               |                 |           |       |           |
| JoinOneChained_Eager          | JoinOneChained  | 45.12 us  |  1.00 |         - |
| JoinOneChained_Prepared       | JoinOneChained  | 44.78 us  |  0.99 |         - |
| JoinOneChained_Frozen         | JoinOneChained  | **26.83 us** | 0.59 |      - |
|                               |                 |           |       |           |
| JoinOneFiltered_Eager         | JoinOneFiltered | 21.29 us  |  1.00 |         - |
| JoinOneFiltered_Prepared      | JoinOneFiltered | 21.18 us  |  0.99 |         - |
| JoinOneFiltered_Frozen        | JoinOneFiltered | 19.77 us  |  0.93 |         - |

The production shapes (the joined bounded flow: the fused lookups fill the page rows only):

| Method                                               | Mean      | Ratio | Step 6 | Allocated |
|----------------------------------------------------- |----------:|------:|-------:|----------:|
| ListListListSortBoundedJoinOne_Eager (shape A)       | 17.23 us  |  1.00 | 17.05  |         - |
| ListListListSortBoundedJoinOne_Prepared              | 16.99 us  |  0.99 | 16.84  |         - |
| ListListListSortBoundedJoinOne_Frozen                | **7.27 us** | 0.42 | 7.71 |         - |
| Count_ListListListSortBoundedJoinOne_Eager           | 10.87 us  |  1.00 | 10.94  |         - |
| Count_ListListListSortBoundedJoinOne_Frozen          | **1.50 us** | 0.14 | 1.41 |         - |
|                                                      |           |       |        |           |
| TimeWindowListListSortBoundedJoinTwo_Eager (shape B) | 33.93 us  |  1.00 | 34.04  |         - |
| TimeWindowListListSortBoundedJoinTwo_Prepared        | 33.69 us  |  0.99 | 33.47  |         - |
| TimeWindowListListSortBoundedJoinTwo_Frozen          | **24.73 us** | 0.73 | 26.82 |      - |
| TimeWindowListListSortBoundedJoinTwo_FrozenIndexSide | 25.69 us  |  0.76 | 29.07  |         - |
| TimeWindowListListSortBounded_Eager (B, no joins)    | 24.96 us  |  1.00 | 25.06  |         - |
| TimeWindowListListSortBounded_Frozen                 | 17.63 us  |  0.71 | 17.44  |         - |
| Count_TimeWindowListListSortBoundedJoinTwo_Eager     | 21.69 us  |  1.00 | 21.70  |         - |
| Count_TimeWindowListListSortBoundedJoinTwo_Frozen    | **12.60 us** | 0.58 | 13.99 |      - |
|                                                      |           |       |        |           |
| TimeRangeListListSortBoundedJoinTwo_Eager (B-range)  | 36.97 us  |  1.00 | 37.77  |         - |
| TimeRangeListListSortBoundedJoinTwo_Prepared         | 36.78 us  |  1.00 | 37.25  |         - |
| TimeRangeListListSortBoundedJoinTwo_Frozen           | **24.49 us** | 0.66 | 27.93 |      - |

## Reading it

- **Bars met.** `JoinOne` (1k list → outer left-symmetric join) 31.67 → 20.57 µs (**1.54×**, bar ≥ 1.5×):
  the plan used to replay whole; now the pass narrows and each fused resolver writes its right slot with
  one point lookup per row instead of a pair-set build plus a paired bulk read. `InnerJoinOne` 34.34 →
  17.68 (**1.94×**, bar ≥ 1.5×) — an inner join is where fusion pays most, because the eager path builds
  the pair set, walks it, then prunes and re-narrows the candidate set, while the fused pass rejects a
  left with one lookup. `JoinOneChained` (two fused joins) 45.12 → 26.83 (**1.68×**). B-range 36.97 →
  24.49 (**1.51×**, bar ≥ 1.5× — step 6 missed it at 1.35×). `Count` of shape B 21.69 → 12.60 (1.72×),
  of shape A 10.87 → 1.50 (7.2×). `JoinOneFiltered` replays as designed and sits on `Build()` (0.93 — the
  replay's fused filter and capacity hint, not the pipeline). Every pipeline row 0 B.
- **Missed: shape B** `TimeWindowListListSortBoundedJoinTwo` 33.93 → 24.73 (**1.37×**, bar ≥ 1.5×) and
  **shape A** `ListListListSortBoundedJoinOne` 17.23 → 7.27 (**2.37×**, bar ≥ 2.5×). Both bars were set
  from step 6's decomposition, which read the whole join-vs-no-join delta (9.4 µs on B) as "the two
  unfused fills". Measured on this tree that attribution was wrong. B decomposes as: `Count` (the same
  narrowing, no container, no joins) **12.60**; plus the *simple* bounded container and its page (the
  no-join row) **17.63** (+5.03); plus the *joined* bounded container and both fused fills **24.73**
  (+7.10). The 40 page lookups (20 rows × 2 joins) are ≈0.3 µs of that +7.10 — removing the fills moved
  the row 26.82 → 24.73, so they cost ≈2.1 µs, not 9.4. The residual ≈6.6 µs is the **eager joined
  bounded container**, which step 5 drives unchanged and eager pays identically:
  `TopKJoinedBaseContainer`'s heap of `(key, left, ordinal)` triples compared through `TopKPairComparer`
  → `IResolvers.CompareLeftValues<TLeft>`, a *generic* method reached through the 4-link nested chain
  type (each link a constrained call over `__Canon` for a reference `TLeft`), plus `MaterializeTopK` into
  a `ValueDictionary` and `BuildResults`. The simple bounded container does the same work for the same
  ~100 matched rows in 5.03 µs with `TopKValueComparer`'s direct, fully devirtualized comparer. Shape A
  tells the same story from the other end: its fill was 0.45 µs of step 6's 7.71.
- **What that means for the bars.** The lever left on A and B is not the join — it is the joined bounded
  container's comparer path, shared by both engines, so fixing it lifts eager and frozen together and
  moves the *ratio* only a little (removing 5 µs from both sides of B gives 28.9 / 19.7 = 1.47×). A
  frozen-only bounded joined container (keys carried beside a `(left, ordinal)` heap with the sorter's
  comparer reached directly, as the simple container does) is the change that would clear the bar; it is
  a new container rather than "drive the eager one", so it belongs to step 7 / cleanup, not here.
- **One shape does not fuse by default, and it costs nothing here.** An *inner left-symmetric* join's eager
  fan-out emits its rows grouped by right key, which a per-left lookup cannot reproduce, so that chain
  replays (byte-identical) unless the caller sets `FrozenOptions.FuseSymmetricInnerJoins` — the same opt-in
  shape as `ReorderIndexNarrowers`. No row above uses one: `InnerJoinOne` is PK-to-PK and measures 17.73 µs
  (1.96×) with the rule in place against 17.68 µs (1.94×) without it, i.e. noise; `JoinOne`,
  `JoinOneChained` and shapes A / B are outer joins, which fuse either way.
- **Key-side probing still loses.** `_FrozenIndexSide` 25.69 vs 24.73 — the design's §2.3 finding holds
  after fusion too; the value-side default stands.
- **No regressions.** The narrowing-only rows are unchanged within noise (B without joins 17.63 vs 17.44
  at step 6; shape A's `Count` 1.50 vs 1.41), and the shapes that already took the pipeline are untouched.


# Frozen queries — `BuildFrozen()` stage 3, step 7: the frozen bounded joined container

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §8 (`SortBounded`), §7.3
(sort after join), §13 step 7. Apple M4 Pro, .NET 9.0 (osx-arm64), Release, `--inProcess`, default job,
`[MemoryDiagnoser]`, one category per run. Same stores and rows as step 5. The only change under test:
a joined `SortBounded` page now selects its top-k in `FrozenTopKJoinedContainer`, which the resolver
chain hands its sorter as a **struct type parameter** (`IResolvers.WithSorter`), instead of in the eager
`TopKJoinedBaseContainer`, which compares through `IResolvers.CompareLeftValues` — one constrained,
`__Canon`-shared hop per chain link per comparison. Everything else (the pass, the fused fills, the
page materialization, the eager containers) is untouched.

## The diagnosis, measured — `BoundedComparerProbeBenchmarks`

The same bounded workload the production shapes run (100 candidates pushed into a heap of K = 40, then
the ascending drain — ~600 comparisons), driven through each comparer in isolation. `Link` is a
placeholder `IJoinResolver` standing in for a `JoinOne`; only the chain's shape matters here.

| Method               | Mean     | Ratio | Δ vs sorter | Allocated |
|--------------------- |---------:|------:|------------:|----------:|
| ValueComparer_Sorter | 3.906 us |  1.00 |           — |         - |
| PairComparer_Sorter  | 3.861 us |  0.99 |    −0.05 us |         - |
| PairComparer_Chain1  | 4.992 us |  1.28 |    +1.13 us |         - |
| PairComparer_Chain2  | 7.435 us |  1.90 |    +3.57 us |         - |
| PairComparer_Chain3  | 9.612 us |  2.46 |    +5.75 us |         - |

## The production shapes

| Method                                               | Mean         | Ratio | Step 5 | Allocated |
|----------------------------------------------------- |-------------:|------:|-------:|----------:|
| ListListListSortBoundedJoinOne_Eager (shape A)       | 17.06 us     |  1.00 | 17.23  |         - |
| ListListListSortBoundedJoinOne_Prepared              | 16.85 us     |  0.99 | 16.99  |         - |
| ListListListSortBoundedJoinOne_Frozen                | **4.88 us**  |  0.29 | 7.27   |         - |
| Count_ListListListSortBoundedJoinOne_Eager           | 10.74 us     |  1.00 | 10.87  |         - |
| Count_ListListListSortBoundedJoinOne_Frozen          | **1.42 us**  |  0.13 | 1.50   |         - |
|                                                      |              |       |        |           |
| TimeWindowListListSortBoundedJoinTwo_Eager (shape B) | 33.81 us     |  1.00 | 33.93  |         - |
| TimeWindowListListSortBoundedJoinTwo_Prepared        | 33.64 us     |  0.99 | 33.69  |         - |
| TimeWindowListListSortBoundedJoinTwo_Frozen          | **17.85 us** |  0.53 | 24.73  |         - |
| TimeWindowListListSortBoundedJoinTwo_FrozenIndexSide | 20.03 us     |  0.59 | 25.69  |         - |
| TimeWindowListListSortBounded_Eager (B, no joins)    | 25.23 us     |  1.00 | 24.96  |         - |
| TimeWindowListListSortBounded_Frozen                 | 17.62 us     |  0.70 | 17.63  |         - |
| Count_TimeWindowListListSortBoundedJoinTwo_Eager     | 21.66 us     |  1.00 | 21.69  |         - |
| Count_TimeWindowListListSortBoundedJoinTwo_Frozen    | **12.99 us** |  0.60 | 12.60  |         - |
|                                                      |              |       |        |           |
| TimeRangeListListSortBoundedJoinTwo_Eager (B-range)  | 37.10 us     |  1.00 | 36.97  |         - |
| TimeRangeListListSortBoundedJoinTwo_Prepared         | 37.07 us     |  1.00 | 36.78  |         - |
| TimeRangeListListSortBoundedJoinTwo_Frozen           | **17.65 us** |  0.48 | 24.49  |         - |
| Count_TimeRangeListListSortBoundedJoinTwo_Eager      | 24.50 us     |  1.00 | —      |         - |
| Count_TimeRangeListListSortBoundedJoinTwo_Frozen     | **17.13 us** |  0.70 | 16.76¹ |         - |

¹ step 6's number; step 5 did not re-measure the B-range `Count`.

## The rows that must not move

| Method                        | Mean         | Ratio | Step 5 | Allocated |
|------------------------------ |-------------:|------:|-------:|----------:|
| JoinOne_Frozen                | 19.97 us     |  0.65 | 20.57  |         - |
| InnerJoinOne_Frozen           | 17.69 us     |  0.52 | 17.68  |         - |
| JoinOneChained_Frozen         | 26.79 us     |  0.60 | 26.83  |         - |
| JoinOneFiltered_Frozen        | 19.76 us     |  0.92 | 19.77  |         - |
| SortBounded_Frozen            | 14.74 us     |  0.91 | 14.96² |         - |
| SortBounded_ListList_Frozen   | 3.85 us      |  0.33 | 3.66²  |         - |
| ListListList_Frozen           | 2.21 us      |  0.19 | 2.18²  |         - |

² step 6's number (step 5 did not re-measure the simple rows).

## Reading it

- **The step-5 attribution was right, and the probe pins it exactly.** Step 5 decomposed shape B as
  `Count` 12.60 → the *simple* bounded page 17.63 (+5.03) → the *joined* bounded page with both fused
  fills 24.73 (+7.10), and attributed ≈6.6 µs of that to the joined container's comparer path.
  Reproduced on this tree before any change: 12.60 → 17.67 → 23.98 (+6.31). The probe says the joined
  container's 3-link chain costs **+5.75 µs** over a sorter-typed comparer on exactly this workload, and
  `17.67 + 5.75 = 23.42` against the 23.98 measured — the remaining ≈0.6 µs is `MaterializeTopK`, the 40
  page lookups and the joined `BuildResults`. So the comparer hop is not *part* of the gap, it is
  essentially all of it; `MaterializeTopK` is noise by comparison, which corrects step 5's "plus
  `MaterializeTopK`" wording. Shape A's 2-link chain costs +3.57 µs on 100 rows, and shape A moved
  7.28 → 4.88 (−2.40 on its 111 rows) — the same effect at the same scale.
- **The chain hop is per link, and it is linear.** 3.86 (sorter) → 4.99 (1 link) → 7.44 (2) → 9.61 (3):
  ≈1.1 µs for the first link and ≈2.3 µs for each further one, over ~600 comparisons, i.e. ~2-4 ns per
  link per comparison. `CompareLeftValues<TLeft>` is a generic method over a reference `TLeft`, so every
  link is a constrained call into a `__Canon`-shared body that needs its generic context and cannot be
  inlined through; the sorter-typed comparer collapses the whole hop into the user comparer's `Compare`.
  Carrying the key in the heap triple costs nothing (`PairComparer_Sorter` 3.86 vs `ValueComparer_Sorter`
  3.91) — the tuple width was never the problem.
- **Bars.** Shape A `ListListListSortBoundedJoinOne` 17.06 → 4.88 (**3.50×**, bar ≥ 2.5× — step 5 missed
  it at 2.37×). Shape B `TimeWindowListListSortBoundedJoinTwo` 33.81 → 17.85 (**1.89×**, bar ≥ 1.5× —
  step 5 missed it at 1.37×). B-range 37.10 → 17.65 (**2.10×**, bar ≥ 1.5×). Every pipeline row 0 B.
- **The joined page now costs what the simple page costs.** Shape B with two joins is 17.85 µs against
  17.62 for the same narrowing and page *without* joins: the joined bounded container is no longer more
  expensive than the simple one, and the 0.23 µs left is the two fused fills over 20 page rows plus the
  page materialization. That is the floor this shape has — the remaining 17.6 µs is the narrowing
  (`Count` 12.99) plus the top-k over ~100 rows, exactly what the simple row pays.
- **B-range gains more than B.** 24.49 → 17.65 (−6.84) against B's −6.13, and its frozen time is now
  *below* B's despite a slower eager, because the range seed pays no last-updated tree walk on the free
  count path.
- **Key-side probing still loses**, by less: `_FrozenIndexSide` 20.03 vs 17.85 (step 5: 25.69 vs 24.73).
  The value-side default stands (design §2.3, §14.1).
- **No regressions.** The unsorted / classic joined rows (`JoinOne`, `InnerJoinOne`, `JoinOneChained`,
  `JoinOneFiltered`) take the classic flow, which step 7 does not touch, and land within noise of step 5;
  the simple containers (`SortBounded`, `SortBounded_ListList`, `ListListList`) are untouched. Both
  `Count` rows of shape B moved up ~0.4 µs (12.60 → 12.99, 16.76 → 17.13) — the count path has no
  container and no chain hop, so this is run-to-run drift, and their eager baselines moved with them.
  One `InnerJoinOne` run landed at 18.61 µs and re-measured at 17.69; single in-process runs on this
  machine carry ~5% between-run spread, which is why every claim above rests on a gap far larger than it.


# Frozen queries — `BuildFrozen()` stage 3, step 4: composites as pipeline steps

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §5, §13 step 4. Apple
M4 Pro, .NET 9.0 (osx-arm64), Release, `--inProcess`, default job, one category per run. Same 100k-row
store and 1k list buckets as every other stage-3 row. `Or` is the query's first narrowing over two
1k buckets (`Group = Id % 100`); `OrAfterList` is a 1k bucket then an `Or` of two unique lookups;
`IfTaken` / `IfSkipped` is a 1k-bucket list step then a taken/skipped `If(unique)`; `Match` is the
existing three-arm parameterized shape (`byCode` / `byGroup` / `byGroup + Where`).

## New rows

| Method | Mean | Ratio | Bar | Allocated |
|---|---:|---:|---|---:|
| `Or_Eager` | 1,046.93 / 1,027.32 µs | 1.00 | — | 128 B |
| `Or_Prepared` | 1,046.54 / 1,014.86 µs | 1.00 / 0.99 | — | 10–11 B |
| `Or_Frozen` (default — `OrSeed` on: the union itself, branch order) | **26.58 / 28.04 µs** | 0.03 (**39.4× / 36.6×**) | ≥ 1.3× **kept** | 0 B |
| `Or_FrozenEagerOrder` (`OrSeed = false`: store walk kept to the union, the eager sequence) | 756.52 / 794.39 µs | 0.72 / 0.77 (1.38× / 1.29×) | the opt-out, not the gated row; its own two readings straddle 1.3× (±22 µs StdDev on a ~1 ms op) | 5–6 B (sub-byte per op: a BDN artefact of a 1 ms body, not a per-execution allocation — the pins in `FrozenPipelineAllocationTests` read 0 B for the same shape) |
| `Count_Or_Eager` | 976.96 µs | 1.00 | — | 128 B |
| `Count_Or_Frozen` (always seeds the union) | **21.94 µs** | 0.02 (**44.5×**) | — | 0 B |
| `OrAfterList_Eager` | 7,158.8 ns | 1.00 | — | 128 B |
| `OrAfterList_Prepared` | 7,203.3 ns | 1.01 | — | 0 B |
| `OrAfterList_Frozen` (the Or's union is the small-probe seed) | **237.3 ns** | 0.03 (**30.2×**) | ≥ 1.3× **kept** | 0 B |
| `IfTaken_Eager` | 3,083.2 ns | 1.00 | — | 0 B |
| `IfTaken_Prepared` | 3,061.7 ns | 0.99 | — | 0 B |
| `IfTaken_Frozen` (the taken unique arm is the small-probe seed) | **112.7 ns** | 0.04 (**27.4×**) | ≥ 10× **kept** | 0 B |
| `IfSkipped_Eager` | 10.167 µs | 1.00 | — | 0 B |
| `IfSkipped_Prepared` | 10.074 µs | 0.99 | — | 0 B |
| `IfSkipped_Frozen` | 7.968 µs | 0.78 (**1.28×**) | ≥ 1.4× **missed** | 0 B |
| `Match_Eager` | 8.626 µs | 1.00 | — | 0 B |
| `Match_Prepared` | 8.533 µs | 0.99 | — | 0 B |
| `Match_Frozen` | **6.58–7.12 µs** (5 runs; median ≈ 6.9) | 0.79 (**1.19–1.30×**, median ≈ 1.25×) | ≥ 1.2× **kept**, but inside the row's own spread | 0 B |
| `Count_Match_Eager` | 6.689 µs | 1.00 | — | 0 B |
| `Count_Match_Frozen` | 6.304 µs | 0.94 (1.06×) | none set; reported for completeness | 0 B |

## Re-measured rows (regression check — `PipelineCore.Bind` / `ChooseSeed` / `Walk` all changed for step 4)

Two passes: the first cut, then after the composite-free tax was removed (see "Reading it"). The
machine carried a 1-minute load of 3.6–4.8 from the user's desktop during the second pass (a quiet
machine reads ≈1.5); the ~10% run-to-run scatter that shows on the 6–9 µs rows (`ListWhere_Frozen`
alone read 5.59 and 6.26 µs in two runs) is that load, not the code.

| Method | First cut | After the fix | Step 3/5/6/7 number | Verdict |
|---|---:|---:|---:|---|
| Shape A `ListListListSortBoundedJoinOne_Frozen` | 5.031 µs (3.40×) | **4.917 µs (3.44×)** | 4.88–4.94 | unchanged |
| `Count_` shape A | 1.466 µs | 1.433 µs (7.42×) | 1.42 | unchanged |
| Shape B `TimeWindowListListSortBoundedJoinTwo_Frozen` | 18.69 µs (1.82×) | **17.64 µs (1.95×)** | 17.85–17.98 | tax removed — back on the step-7 number |
| Shape B without its joins | 17.25 µs | 17.58 µs | 17.62 | unchanged |
| `Count_` shape B | 12.86 µs | 12.68 µs (1.70×) | 12.99 | unchanged |
| Shape B `_FrozenIndexSide` | 22.82 µs | 21.99 µs | 20.03 | value-side default stands |
| Shape B-range `TimeRangeListListSortBoundedJoinTwo_Frozen` | 21.00 µs (1.77×) | 20.11 / 20.74 µs (1.84×) | **20.33 µs on the pre-step-4 tree, same machine, same session** | not a step-4 effect — see below; bar ≥ 1.5× kept |
| `Count_` shape B-range | 17.20 µs | 16.79 / 17.34 µs | 17.44 (pre-step-4, same machine) | unchanged |
| `ListList_Frozen` / `_FrozenReorder` | 1.682 / 1.251 µs | 1.644 / 1.218 µs (5.80×) | 5.3× | unchanged |
| `ListListList_Frozen` | 2.178 µs | 2.153 µs (5.46×) | 5.2× | unchanged |
| `Sort_ListList_Frozen` | 2.194 µs (4.67×) | — | 4.75× | within noise |
| `SortBounded_Frozen` / `_ListList_Frozen` | 14.441 / 3.701 µs | 14.788 / 3.736 µs (1.08× / 3.10×) | 1.06× / 3.1× | unchanged |
| `JoinOne_Frozen` | 19.96 µs (1.55×) | 20.77 µs (1.50×) | 1.53–1.54× | within noise (a first-cut run read 1.42×, two re-runs 1.55× / 1.50×) |
| `InnerJoinOne_Frozen` / `JoinOneChained_Frozen` | 18.02 / 27.01 µs (1.89× / 1.70×) | — | 1.91× / 1.66× | within noise |
| `Count_ListList_Frozen` / `Count_ListWhere_Frozen` | 922.8 ns / 5.670 µs | — | 8.4× / 1.1× bar | within noise |
| `IfTaken_Frozen` | 112.7 ns (27.4×) | **108.7 ns (28.0×)** | — | the devirtualized `IfSelector` shows as −4 ns |
| `IfSkipped_Frozen` | 7.968 µs (1.28×) | 7.754 / 8.750 / 8.578 µs (1.31× / 1.15× / 1.17×) | — | bar ≥ 1.4× **missed** in every run, under load 3.6–4.8 — residual below |
| `Match_Frozen` | 6.635 µs (1.30×) | 7.534 µs (1.13×, load 4.2) | 6.58 / 6.74 / 6.96 / 7.12 µs (1.19–1.27×, four settled runs) | resolved: the row swings ~10% run to run, so no fix regressed it and 1.30× was the optimistic end. The table above records the range; the bar holds on the median but not on every run |

## Reading it

- **`Or` at 39.4× by default; the eager sequence is one flag away at 1.38×.** `OrSeed` is on by
  default (the reviewer's call, taking the design's §5.1 recommendation): an Or-first seeds the union of
  its branches, in branch order — the design predicted ~60× for exactly this. The eager Or-first order
  is the store's hash order, an accident of the bucket layout that changes on resize and that no caller
  can rely on; same rows, same `Count`. `OrSeed = false` (`Or_FrozenEagerOrder`) reproduces the eager
  sequence byte for byte: the branch union is built once into a dedupe set and a full 100k-row store
  walk copies out only the rows it admits — one hash probe per store row instead of one `ValueSet`
  insert into a growing bitmap-backed set, which is where its 1.38× comes from. Only an `Or` that is
  the *first* narrowing is affected either way; after another step the `Or` is a probe and keeps that
  step's order.
- **`OrAfterList` and `IfTaken` land far above their bars because the union / the taken arm becomes
  the pipeline's seed — the walk shrinks from 1k rows to 2 and 1 respectively.** `IfTaken`'s 112.7 ns
  is two hash lookups (the unique step's own `TryGetValue`, then the slot-sort against the list bucket)
  plus the container write; `OrAfterList`'s 237.3 ns is the same shape with the Or's two-key union
  standing in for the unique step.
- **`IfSkipped` misses its 1.4× bar (1.15–1.31× across four runs), and what is left is the composite
  bind path on a plan with almost no walk to amortize it.** `IfSkipped`'s plan (list step, `If` skipped)
  walks the same 1k bucket with zero filters that `ListWhere` (list step, one direct `Where`) walks with
  one filter call per row, yet reads 7.75–8.75 µs against `ListWhere_Frozen`'s 5.59–6.26. The difference
  is per execution, not per row: a plan with a `PipelineTree` binds through `BindNodes` (a loop over the
  top nodes, a switch per node, `_plan.RecordArm`, and the `If` condition delegate) where a flat plan's
  `Bind` is a straight-line loop over the step array. Two of its costs were removed in the fix pass —
  `NodeKind` is a field (no virtual get per node) and the `If` selector is called directly through a
  sealed typed reference (no `IBranchSelector` dispatch) — and `IfTaken` shows the effect as −4 ns; on
  `IfSkipped` it is inside the ~10% run-to-run scatter this machine put on every 6–9 µs row during the
  pass, so it cannot be read off these runs. What cannot be removed without a different shape is the
  tree walk itself: one node loop, one switch, one `Func<TArgs,bool>` call (which copies the 12-byte
  argument tuple by value — the delegate's signature, shared with the eager `If`). `IfTaken` clears its
  bar regardless because the small-probe seed saves ~3 µs of walk in the other direction. Not lowered,
  not waved off: reported as the residual.
- **`Match` and `Count_Match` both improve, modestly.** `Match`'s three arms are three different plan
  shapes (unique, list, list+where) behind one `IBranchSelector`; the 1.30× is the same kind of bind
  overhead discount as `IfSkipped`'s regression, partly offset here because the selected arm (`byGroup`
  + `Where`, mode 2 in the benchmark's fixed args) still walks a 1k bucket, so the walk dominates.
  `Count_Match` at 1.06× is expected and not gated by a bar: `Count` was already the cheap path for
  this shape (no container, no clone, no page math) on both eager and frozen, so there is little left
  to save.
- **The composite-free tax, and its removal.** The first cut of this step measured shape B at +3–6%
  (a reviewer's quiet-machine reading: 17.98 → 18.46 / 19.01 µs) although the shape carries no
  composite. Two per-row / per-execution costs had leaked onto every plan: the general `Walk` loop
  tested `branchFilters.Length > 0` once **per surviving row** (always false without a taken arm's
  `Where`), and `ChooseSeed` called `steps[seed].Kind` (an interface get) once **per execution** to
  ask whether the seed was an `Or` (always false without one). Both are gone — the branch-filter test
  is hoisted into the once-per-execution dispatcher so a plan without branch filters runs the
  pre-step-4 `Walk` / `WalkPlain` bodies byte for byte, and the `Or` test is gated on a `_hasOr` flag
  read once at build. The composite path itself lost two more: `PipelineNode.NodeKind` is a field, not
  a virtual property, and a `SelectNode` over the sealed `IfSelector<TArgs>` calls it through a typed
  field (a direct, inlinable call) instead of through `IBranchSelector<TArgs>`. The re-measured rows
  below are after those fixes.
- **Shape B-range's 20 µs is this machine's number for that row, not a step-4 effect.** Measured
  like for like — the step-4 tree stashed, the pre-step-4 tree rebuilt and run on the same machine in
  the same session — `TimeRangeListListSortBoundedJoinTwo_Frozen` reads **20.33 µs before step 4**
  against 20.11 / 20.74 after it; its `Count_` twin 17.44 before against 16.79 / 17.34 after. The
  17.65 µs in the step-7 table was another day's run of a range-seeded row, the class this file
  already flags as not stable in-process ("Not observed as stable" in the parent spec); the row's bar
  (≥ 1.5×) holds either way (1.84×).
- **Every new pipeline row is 0 B.** `Or_Eager` / `Count_Or_Eager` allocate 128 B (the eager auto-seed's
  `Candidates` set) and `Or_Prepared` 11 B (a small transient); every `_Frozen` row, composite or not,
  is `-`.

Tests: `tests/Prague.Core.Tests/Prepared/FrozenPipelineCompositeTests.cs` (a: `Or` first/after/small-probe/
no-op/nested/branch-with-two-narrowers/marking-steps-replay, `If`/`IfElse`/nested/`Or`-in-`If`/`If`-in-
`Or`, `Match` every arm/default/unmatched/duplicate-tag/nested/in-`Or`, all × sorted/bounded/joined; b:
`Count` and `OrSeed` set parity; d: throwing condition/tag-selector/arg-selector/branch-predicate/
comparer/clone under `LeakAssert.Balanced`; e: `Explain()` shape/arm/branch output and the fallback
matrix; f: eight readers against a churning writer), `+1` pin group in `FrozenPipelineAllocationTests`
(`Composites_...`: Or default, `OrSeed`, `Or`-after-list small-probe, `If` taken/skipped, `Match` arm
with a branch filter, `Match(Or)` under `SortBounded` + fused join — all 0 B), and
`tests/Prague.Generated.Tests/Prepared/PreparedGeneratedFrozenCompositeTests.cs` (the generated `WithXxx`
twins of the same shapes, `Or`/`If`/`IfElse`/`Match`, nested, sort-bounded-joined). `Prague.Core.Tests/
Prepared` 429 in Release (net9.0), 429 + 1106 in Debug on net9.0 (1 pre-existing skip), `Prague.Generated.
Tests` 1223 green. No codegen or T4 change.


# Frozen queries — `BuildFrozen()` stage 3, step 8: `JoinMany` — admission, the fused per-left fill, and `Sort` on the bounded flow

Design: `docs/superpowers/specs/2026-09-09-frozen-pipeline-executor-design.md` §7.2 (rewritten: "as
implemented"), §8, §9. Apple M4 Pro, .NET 9.0 (osx-arm64), Release, `--inProcess`, default job,
`[MemoryDiagnoser]`, one category (or two small ones) per run; the machine idled at a 1-minute load of
4–6 throughout, so the 5–20 µs rows carry the usual ~5% between-run spread and every claim below rests on a
gap far larger than it. New right sides on the existing stores: `_lines` by item (two per item, none for
every fourth; 150k rows), three `_notes` per customer shared by the customer's 1k orders (the fan-out's
shared-right chain path), and a tag collection on 10k documents (three of 500 tags each, a quarter
untagged; M:N). Three stages were measured on the same tree, in this order.

## 1. Baseline — before step 8 (every `JoinMany` chain replayed; `_Frozen` = the hinted replay)

| Method | Eager | `Build()` | `BuildFrozen()` (replay) | Allocated |
|---|---:|---:|---:|---:|
| `JoinMany` (right-list, list 1k → ~1.5k lines) | 98.24 µs | 99.40 | 97.29 | 0 B |
| `InnerJoinMany` (a quarter of the lefts dropped) | 103.4 | 107.0 | 108.2 | 0 B |
| `JoinMany_LeftSym` (1k orders × 3 shared notes) | 86.19 | 85.38 | 84.08 | 0 B |
| `InnerJoinMany_LeftSym` | 103.9 | 103.4 | 102.7 | 0 B |
| `JoinMany_Collection` (1k docs → their tags, M:N) | 116.50 | 115.55 | 115.71 | 0 B |
| `InnerJoinMany_Collection` | 124.8 | 125.1 | 122.8 | 0 B |
| `Count_JoinMany` | 4.901 | 4.946 | 3.421 | 0 B |
| `Count_InnerJoinMany` | 96.87 | 98.54 | 99.91 | 0 B |
| `Sort_JoinMany` (page 0..20) | 123.18 | 120.55 | 121.32 | 0 B |
| `SortBounded_JoinMany` (page 0..20) | 29.32 (StdDev 3.4) | 29.29 | 26.22 | 0 B |
| `ListListListSortBoundedJoinMany` (shape A with a `JoinMany`, page 20..40) | 19.16 | 19.18 | 17.87 | 0 B |
| `ListListListInnerJoinMany` (three lists → 111 rows → inner lines) | 23.84 | 24.24 | 22.76 | 0 B |
| `Count_ListListListInnerJoinMany` | 23.54 | 24.27 | 22.76 | 0 B |

(The `1 B` / `2 B` BDN reads on some 100 µs rows are the diagnoser's rounding on a rare GC-side
allocation, not a per-execution one — the `FrozenPipelineAllocationTests` pins read 0 B for the same shapes.)

## 2. Admission only — the narrowing is the pass, the eager fan-out runs after it

| Method | Eager | `Build()` | `BuildFrozen()` | Ratio |
|---|---:|---:|---:|---:|
| `JoinMany` / `_LeftSym` / `_Collection` | 97.90 / 83.46 / 116.99 | 97.12 / 84.09 / 115.23 | 96.90 / 85.75 / 114.55 | 0.99 / 1.03 / 0.98 |
| `InnerJoinMany` / `_LeftSym` / `_Collection` | 104.07 / 104.57 / 125.41 | 102.98 / 106.98 / 125.30 | 98.70 / 87.01 / 118.91 | 0.95 / 0.83 / 0.95 |
| `Count_JoinMany` / `Count_InnerJoinMany` | 4.921 / 97.81 | 4.813 / 98.71 | 4.772 / 96.18 | 0.97 / 0.98 |
| `Sort_JoinMany` / `SortBounded_JoinMany` | 121.83 / 26.14 | 120.44 / 26.25 | 121.70 / **17.40** | 1.00 / **0.67** |
| `ListListListSortBoundedJoinMany` | 19.09 | 19.05 | **6.71** | **0.35** |
| `ListListListInnerJoinMany` / `Count_` | 23.76 / 23.76 | 23.92 / 23.13 | **11.77 / 11.27** | **0.50 / 0.47** |

The split the brief predicted: where the narrowing is the cost (three lists, a bounded page) admission
alone is 2–3×; where the 1k-row fan-out is the cost (~90 µs of the 97), the pass's 2 µs is invisible.

## 3. The fused per-left fill (`JoinManyFusedFill`) — and, after it, classic `Sort` on the bounded flow

| Method | Eager | `Build()` | `BuildFrozen()` | Ratio | Speed-up | Allocated |
|---|---:|---:|---:|---:|---:|---:|
| `JoinMany` | 99.60 | 97.97 | **64.78** | 0.65 | **1.53×** | 0 B |
| `JoinMany_LeftSym` | 83.90 | 84.44 | **46.77** | 0.56 | **1.79×** | 0 B |
| `JoinMany_Collection` | 118.60 | 116.44 | **57.34** | 0.48 | **2.07×** | 0 B |
| `InnerJoinMany` | 106.43 | 101.43 | **64.45** | 0.61 | **1.65×** | 0 B |
| `InnerJoinMany_LeftSym` | 103.26 | 102.51 | **48.08** | 0.47 | **2.15×** | 0 B |
| `InnerJoinMany_Collection` | 124.43 | 127.71 | **58.05** | 0.47 | **2.14×** | 0 B |
| `Count_InnerJoinMany` (a bucket probe per left) | 98.18 | 98.95 | **25.93** | 0.26 | **3.79×** | 0 B |
| `Count_JoinMany` (outer: the pipeline's own count) | 4.967 | 4.954 | 5.372 | 1.08 | 0.92× (see below) | 0 B |
| `ListListListSortBoundedJoinMany` | 19.02 | 18.94 | **5.630** | 0.30 | **3.38×** | 0 B |
| `ListListListInnerJoinMany` | 24.10 | 24.04 | **7.198** | 0.30 | **3.35×** | 0 B |
| `Count_ListListListInnerJoinMany` | 23.95 | 23.92 | **2.767** | 0.12 | **8.66×** | 0 B |
| `SortBounded_JoinMany` (page 0..20; two runs) | 27.34 / 27.20 | 27.34 / 27.41 | **18.13 / 17.15** | 0.66 / 0.63 | **1.51–1.59×** | 0 B |
| `Sort_JoinMany` (page 0..20) before the `Sort` change | 119.77 | 120.89 | 118.32 | 0.99 | — | 0 B |
| `Sort_JoinMany` (page 0..20) with a classic `Sort` on the bounded flow | 125.85 | 126.23 | **17.19** | 0.14 | **7.3×** | 0 B |
| `Sort_ListList` (unbounded page: the classic container, unchanged) | 10.69 | 10.66 | 2.213 | 0.21 | (4.8×, as before) | 0 B |

## The rows that must not move (re-measured on this tree, after everything above)

| Method | Eager | `BuildFrozen()` | Ratio | Steps 4/7 | Verdict |
|---|---:|---:|---:|---:|---|
| Shape A `ListListListSortBoundedJoinOne_Frozen` / `Count_` | 17.29 / 10.89 | 5.051 / 1.456 | 0.29 / 0.13 | 4.88–5.03 / 1.42–1.47 | unchanged |
| Shape B `TimeWindowListListSortBoundedJoinTwo_Frozen` / `_FrozenIndexSide` / `Count_` | 34.01 / — / 21.64 | 18.27 / 22.44 / 12.33 | 0.54 / 0.66 / 0.57 | 17.64–18.69 / 20.03–22.82 / 12.68–12.99 | unchanged |
| Shape B-range `TimeRangeListListSortBoundedJoinTwo_Frozen` / `Count_` | 36.73 / 24.66 | 20.51 / 16.62 | 0.56 / 0.67 | 20.11–20.74 / 16.79–17.34 (this machine) | unchanged |
| `ListList_Frozen` / `_FrozenReorder` / `ListListList_Frozen` | 9.31 / — / 11.48 | 1.770 / 1.321 / 2.151 | 0.19 / 0.14 / 0.19 | 1.64–1.68 / 1.22–1.25 / 2.15–2.18 | unchanged (ListList +5%, inside the spread) |
| `SortBounded_Frozen` / `SortBounded_ListList_Frozen` | 16.41 / 11.70 | 14.75 / 3.779 | 0.90 / 0.32 | 14.4–14.8 / 3.70–3.85 | unchanged (a first run read 15.46 / 4.16 with the eager rows up 3–4% alongside — drift) |
| `JoinOne_Frozen` / `InnerJoinOne_Frozen` | 30.82 / 35.36 | 21.25 then 20.31 (re-run) / 18.26 | 0.69 → 0.66 / 0.52 | 19.96–20.77 / 17.69–18.61 | unchanged (`JoinOne`'s first run read 2–6% up, the re-run 20.31 — its documented spread is 1.42–1.55×) |

## Reading it

- **The two-pass argument was half right.** §7.2 said the fan-out must record every pair before it
  can size a slot, because a slot is a fixed partition of one shared buffer. That is true of the *eager*
  delivery, which walks the pair set by right; it is not true of a delivery that walks the *lefts* in row
  order — then each slot is a contiguous run of one append-only buffer, its length known the moment its
  left is done, and the slots can be handed the (possibly relocated) buffer afterwards by row index.
  `JoinManyFusedFill` does exactly that: per left the family's own bucket read, per right one store
  lookup, one append. What disappears is the pair-set `AddOrFind` per right, the `PrepareSharedBuffer`
  walk over every row, and the `ValueDictionary` lookup per `Init` and per delivered right — on the 1k ×
  1.5k right-list shape roughly 3.2k dictionary lookups and 1.5k set inserts out of ~7k hash operations,
  which is the 97.9 → 64.8 µs. The shared-right families gain more (left-symmetric 1.79×, collection
  2.07×) because the eager fan-out's chain bookkeeping goes too.
- **What stays exact.** Nothing can be dropped or `Truncated` (a slot holds exactly what was appended
  for it), the pooled buffer is registered with the disposer as before, a throwing `Clone()` returns the
  rental, and "a right the enumerator yields twice for one left is delivered once" is kept by a 16-key
  window and a set beyond it. The only observable difference is slot *order* when rights are shared
  across lefts in overlapping buckets (collection, a non-injective selector): eager interleaves by
  first-recording order, the fill keeps bucket order — same set, and a slot is an unordered set.
- **Inner `JoinMany` is now a bucket probe where it counts.** `Count_InnerJoinMany` 98 → 26 µs and
  `Count_ListListListInnerJoinMany` 24 → 2.8 µs: eager's `CountCoreJoined` runs the whole inner fan-out
  to learn which lefts have a right; the fused resolver's `UnsafeNarrowFused` stops at the first right
  the store has. The same probe narrows a bounded page before the heap, so `SortBounded → InnerJoinMany`
  keeps the bounded flow where eager falls back to the classic core (a total comparer makes the pages
  identical; the tests pin that).
- **Where the fill is deliberately off.** A filter callback (a builder lambda over the paired core —
  it needs the pair set) and a sorter over the joined row declared before the join (the container sorts
  and crops first; filling every row would do more work than eager). Both keep the eager two-pass fan-out,
  admitted after the pass. `Explain()` prints `joins: n (fused: f, unfused: u, many: m, …)`.
- **`Sort` with a page: 7.3× from a gate.** With ties inside a sorted result unspecified (the owner's
  ruling), a classic `Sort` over the left value can take the bounded top-k plan for a finite page: its
  encounter-ordinal tie-break is the stable sort's tie order for the same input, so nothing observable
  changes for a total comparer and a tie group stays a valid order otherwise. `Sort_JoinMany` page 0..20
  went 125.8 → 17.2 µs — the full stable sort of 1k rows was the whole cost — and a `JoinMany` after such
  a `Sort` now fuses over the page. `Sort` keeps its free seed; `SortBounded` keeps its fixed seed and its
  byte-identical pages with tie comparers.
- **The probe-free count no longer copies values.** `Count_JoinMany` (an outer `JoinMany` over one 1k
  bucket, no filter) first read 5.37 µs against the 3.42 the hinted *replay* gave it before admission —
  an outer join never changes a count, so this was the pipeline's own counting walk paying an
  `InMemoryDataCache.TryGet` call per seed key, value copied out and unread, against the store's
  key-only `TryCountValues` the eager `Count` runs. `PipelineCore.Count` now counts a plan whose pass
  needs no value — no key-side or value-side probe active, no filter (top-level, fused, or in a taken
  arm), decided once per execution from the frame's activation lists — with a span twin of exactly that
  `TryCountValues` body: the same tables walk, the same membership (a key the store holds), so a key an
  index bucket holds but the store does not — or no longer — is rejected by both, and nothing counted
  changes. `Count_JoinMany` reads **3.10 µs** (0.63 of eager, under the replay's 3.42); the other
  `Count_*` rows are in the table below — the ones with a probe or a filter walk exactly as before.
- **No tax elsewhere.** Everything added is decided at build (`IsMany`, `CanFuse`, the sorter's place —
  all JIT-folded or read once); the only per-execution additions are two fields on the fill processor and
  an `int[]` hint the executor owns. Shapes A / B / B-range, `ListList`, `ListListList`, `SortBounded`,
  `InnerJoinOne`, `JoinOne` re-measured on their step-4/7 numbers (`JoinOne` read 21.25 once and 20.31 on
  the re-run, inside the 1.42–1.55× spread step 4 recorded for that row).

## Every `Count_*` row after the presence count

| Method | Eager | `Build()` | `BuildFrozen()` | Ratio | Path |
|---|---:|---:|---:|---:|---|
| `Count_JoinMany` | 4.957 | 4.858 | **3.100** | 0.63 | presence count (was 5.37 / replay 3.42) |
| `Count_InnerJoinMany` | 98.14 | 98.75 | 25.27 | 0.26 | bucket probe (unchanged) |
| `Count_ListListListInnerJoinMany` | 23.16 | 23.79 | 2.792 | 0.12 | bucket probe (unchanged) |
COUNT_ROWS_PLACEHOLDER

# Single-hash index maintenance — benchmark results

## Baseline (before, commit d210cab)

> Run command (Task 8 re-run MUST match, incl. `-f net9.0`, for an apples-to-apples comparison):
> `dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --filter '*CacheIndexMaintenance*'`

BenchmarkDotNet v0.15.8, macOS Tahoe 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 9.0.17 (9.0.17, 9.0.1726.26416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 9.0.17 (9.0.17, 9.0.1726.26416), Arm64 RyuJIT armv8.0-a

| Method              | N      | Mean      | Error    | StdDev   | Gen0      | Gen1      | Gen2     | Allocated  |
|-------------------- |------- |----------:|---------:|---------:|----------:|----------:|---------:|-----------:|
| AddAll_Composite    | 100000 |  55.30 ms | 1.092 ms | 1.381 ms | 3200.0000 | 1800.0000 | 400.0000 | 26137449 B |
| AddAll_String       | 100000 |  87.58 ms | 1.523 ms | 1.350 ms | 2666.6667 | 1333.3333 | 166.6667 | 23654561 B |
| UpdateAll_Composite | 100000 |  66.76 ms | 0.505 ms | 0.473 ms |         - |         - |        - |   157500 B |
| UpdateAll_String    | 100000 | 166.81 ms | 2.684 ms | 2.511 ms |         - |         - |        - |      112 B |

### RemoveAll baseline

RemoveAll uses `[IterationSetup]` to repopulate a fresh cache each iteration, which forces
`InvocationCount=1` (per-op timing, no Gen0/1/2 columns; `Median` reported instead).

| Method              | N      | Mean     | Error    | StdDev   | Median   | Allocated |
|-------------------- |------- |---------:|---------:|---------:|---------:|----------:|
| RemoveAll_Composite | 100000 | 31.82 ms | 0.985 ms | 2.889 ms | 30.82 ms |         - |
| RemoveAll_String    | 100000 | 61.05 ms | 1.164 ms | 1.196 ms | 61.04 ms |         - |

## After (commit 6ee91c9)

> Same command, same machine (Apple M4 Pro, .NET SDK 10.0.301, .NET 9.0.17 host — header identical
> to baseline), BenchmarkDotNet v0.15.8. An initial run overlapped heavy ambient load (load avg 5–7,
> bimodal UpdateAll_String 115↔250 ms) and was discarded; the run below was taken on a quiet machine
> (1-min load 2.65) with every CI margin ≤ 2.0% of mean.

| Method              | N      | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2     | Allocated  |
|-------------------- |------- |---------:|---------:|---------:|----------:|----------:|---------:|-----------:|
| AddAll_Composite    | 100000 | 48.54 ms | 0.911 ms | 0.895 ms | 3200.0000 | 1800.0000 | 400.0000 | 26187569 B |
| AddAll_String       | 100000 | 62.83 ms | 0.618 ms | 0.578 ms | 3000.0000 | 1666.6667 | 333.3333 | 24800230 B |
| UpdateAll_Composite | 100000 | 53.37 ms | 1.026 ms | 0.960 ms |         - |         - |        - |   180000 B |
| UpdateAll_String    | 100000 | 84.35 ms | 1.131 ms | 1.003 ms |         - |         - |        - |          - |

### RemoveAll after

| Method              | N      | Mean     | Error    | StdDev   | Median   | Allocated |
|-------------------- |------- |---------:|---------:|---------:|---------:|----------:|
| RemoveAll_Composite | 100000 | 29.52 ms | 0.573 ms | 0.941 ms | 29.47 ms |         - |
| RemoveAll_String    | 100000 | 48.89 ms | 0.978 ms | 1.907 ms | 48.66 ms |         - |

## Delta

| Scenario            | Baseline  | After    | Δ Mean  |
|---------------------|----------:|---------:|--------:|
| AddAll_Composite    |  55.30 ms | 48.54 ms | −12.2%  |
| AddAll_String       |  87.58 ms | 62.83 ms | −28.3%  |
| UpdateAll_Composite |  66.76 ms | 53.37 ms | −20.1%  |
| UpdateAll_String    | 166.81 ms | 84.35 ms | −49.4%  |
| RemoveAll_Composite |  31.82 ms | 29.52 ms |  −7.2% (median −4.4%) |
| RemoveAll_String    |  61.05 ms | 48.89 ms | −19.9% (median −20.3%) |

Allocation compare: AddAll_Composite 26,137,449 → 26,187,569 B (+0.2%, back to baseline from +6.9%
under always-on storage); AddAll_String 23,654,561 → 24,800,230 B (+4.8% — stored string hashes,
down from +6.1%); UpdateAll_Composite 157,500 → 180,000 B (+14.3% — not the gated hash arrays,
which are never rented for value-typed values; the residual matches the +8 B nullable-array field
Task 9 added per node object, ≈2.8k node allocations per pass); UpdateAll_String 112 → 0 B;
both RemoveAll scenarios remain allocation-free.

Tripwire (>2% above baseline mean): no scenario is above baseline at all — pass. RemoveAll_Composite,
the offender under always-on storage (+2.3–6%), is now −7.2%.

Measurement history: an intermediate measurement at 766e181 (hashes stored for every value type)
kept large string wins (−26/−44/−15.5%) but tripped the tripwire on RemoveAll_Composite (+2.3–6%,
composite allocations +6.9–14.3%) — composites pay the wider node copies while recomputing their
tiebreak hash cheaply. 6ee91c9 gates storage to ref-typed values (JIT-folded per closed generic),
folding value-typed instantiations back to the recompute codegen and keeping the string wins.

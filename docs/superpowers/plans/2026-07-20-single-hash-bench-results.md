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

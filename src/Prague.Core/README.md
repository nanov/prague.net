# Prague.Core

> Part of **[Prague](https://github.com/nanov/prague.net)** — a high-performance, compile-time-safe
> in-memory event cache for .NET, fed by event-sourced Kafka streams.

The runtime engine: `InMemoryDataCache`, index storage and plan selection, candidate intersection
(stackalloc bitmap, short-circuit on first empty set), the fluent query builder with allocating
(`Execute()`) and pooled (`ExecutePooled()`) results, and compile-time joins with pooled,
zero-allocation result sets. Built on span-based, stack-allocated, SIMD-friendly hot paths.

Usually consumed transitively via the `Prague` facade package.

## Install
`dotnet add package Prague.Core`

See the [project README](https://github.com/nanov/prague.net) for the full guide.

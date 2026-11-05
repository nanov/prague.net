# Prague

> A high-performance, compile-time-safe in-memory event cache for .NET, fed by event-sourced Kafka
> streams. **Start here** — this is the top-level facade package.

Prague turns plain `[DataCache]` POCOs into a zero-reflection, compile-time-safe fluent query API via
a Roslyn source generator. Indexes (unique / many / range, plus key-set and symmetric variants) are
intersected automatically with an optimal plan selected at runtime; compile-time 1:1 and 1:N joins
return pooled, zero-allocation result sets. Span-based, stack-allocated, SIMD-friendly hot paths give
~9.7M reads/sec and sub-100ns indexed lookups.

Referencing this package pulls in the runtime (`Prague.Core`), attributes (`Prague.Attributes`), the
source generator (`Prague.Codegen`) and the Kafka integration (`Prague.Kafka`). It also ships agent
skills for coding agents (`skills/prague-modeling`, `-queries`, `-kafka`, `-handlers`, `-testing`); install them with
`dotnet-package-skills install`. See https://nanov.github.io/prague.net/docs/articles/how-to/use-with-ai-agents.html.

## Install
`dotnet add package Prague`

See the [project README](https://github.com/nanov/prague.net) for the full guide.

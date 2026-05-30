# Prague.Codegen

> Part of **[Prague](https://github.com/nanov/prague.net)** — a high-performance, compile-time-safe
> in-memory event cache for .NET, fed by event-sourced Kafka streams.

The Roslyn source generator. It reads `[DataCache]`-annotated `partial` types (see
`Prague.Attributes`) and emits the per-type cache partial, index storage, fluent query builders, and
FK-driven `JoinWith{T}` convenience methods — zero reflection, all resolved at compile time.

Ships as an analyzer (no runtime assembly, no dependencies). Reference it as a development
dependency; it produces no `lib/` output of its own.

## Install
`dotnet add package Prague.Codegen`

See the [project README](https://github.com/nanov/prague.net) for the full guide.

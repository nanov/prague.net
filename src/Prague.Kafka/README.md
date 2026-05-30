# Prague.Kafka

> Part of **[Prague](https://github.com/nanov/prague.net)** — a high-performance, compile-time-safe
> in-memory event cache for .NET, fed by event-sourced Kafka streams.

Kafka integration on the `Nanov.Confluent.Kafka` fork: a raw, span-based zero-copy consume/produce
path, SerDe, message filters, change detection / conditional updates, a ring-buffer background
worker, health checks, and OpenTelemetry instrumentation. Hydrates Prague caches directly from
event-sourced topics.

## Install
`dotnet add package Prague.Kafka`

See the [project README](https://github.com/nanov/prague.net) for the full guide.

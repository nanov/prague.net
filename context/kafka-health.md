# Kafka health checks (`Prague.Kafka.Health`)

> **Read when:** working on liveness/readiness, `KafkaCachesHealthEvaluator`, or broker-state predicates.

Split liveness/readiness `IHealthCheck` impls backed by a pure `KafkaCachesHealthEvaluator` (no DI, testable in isolation). State lives on existing `KafkaCachesConsumerStatistics` / `KafkaDataCacheStatistics` as plain fields (no `Volatile`/`Interlocked`, single-writer — read via the `*Unsafe` accessors, see [`kafka.md`](kafka.md)). Healthy path is zero-alloc — allocate only on failure.

Register via `AddPragueKafkaLiveness` / `AddPragueKafkaReadiness` on `IHealthChecksBuilder`. Options via `KafkaCachesHealthOptions` (options pattern).

## Gotcha: MinBrokersUp needs statistics

`MinBrokersUp` (default 1) requires `StatisticsEnabled = true` (now the default in `KafkaCachesGlobalOptions`) — librdkafka emits no broker-state info otherwise, leaving `BrokerUpCountUnsafe` at 0 and readiness permanently **Degraded**. Set `MinBrokersUp = 0` to disable that predicate when running without librdkafka statistics.

Integration tests do not override `PollLoopHeartbeatTimeout` — the default 3 s suffices because `StartAndLoadAsync` fully completes (poll loop already heartbeating) before any health assertions run.

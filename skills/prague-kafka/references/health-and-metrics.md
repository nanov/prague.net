# Health checks and metrics

`AddPragueKafkaLiveness` and `AddPragueKafkaReadiness` (namespace `Prague.Kafka.Health`) and `AddPragueKafkaInstrumentation` (namespace `Prague.Kafka.OpenTelemetry`) all read the same statistics, so an alert on a `health` gauge and the probe agree.

## Failure reasons

`KafkaCachesHealthFailure`:

| Check | Reason | Means |
|---|---|---|
| liveness (Unhealthy) | `FatalLatched` | the consume loop stopped on a fatal Kafka error or an unexpected exception |
| | `PollLoopStalled` | the consume thread was silent for `PollLoopHeartbeatTimeout` (3 s); a slow filter or deserializer does it |
| | `HandlerLoopFaulted` | a cache's live worker (the after-handler loop) faulted |
| | `HandlerProcessingTimeout` | one message spent longer than `HandlerProcessingTimeout` (5 s) in after-handlers |
| readiness (Degraded) | `InitialLoadIncomplete` | still loading |
| | `NoPartitionAssigned` | fewer partitions assigned than the topic has |
| | `BrokersDown` | fewer than `MinBrokersUp` (1) brokers up |
| | `PartitionsLost` | librdkafka reported the assignment lost (for example a session timeout), until partitions are assigned again |

Readiness is Unhealthy, not Degraded, when liveness fails too.

## Options

`services.Configure<KafkaCachesHealthOptions>(o => ...)` sets `PollLoopHeartbeatTimeout`, `HandlerProcessingTimeout` and `MinBrokersUp`. A liveness failure restarts the pod and reloads every topic, so fix slow code instead of raising a timeout.

`MinBrokersUp` and `broker.rtt.p99` need librdkafka statistics, which are on by default. With them off, set `MinBrokersUp = 0` or readiness never passes.

## Instruments

Meter `Prague.Kafka`. `AddPragueKafkaInstrumentation("acme")` prefixes every name with `acme.`.

| Instrument | Tags |
|---|---|
| `prague.kafka.consumer.partitions.assigned`, `prague.kafka.consumer.broker.rtt.p99`, `prague.kafka.consumer.health` | `consumer` |
| `prague.kafka.cache.size`, `prague.kafka.cache.messages.received`, `prague.kafka.cache.health` | `consumer`, `cache` |
| `prague.kafka.cache.index.size` | `consumer`, `cache`, `index`, `kind` (`keys` / `values`) |
| `prague.kafka.cache.index.capacity` | `consumer`, `cache`, `index` |

The `health` gauges are 1 or 0, from the same predicates as the checks.

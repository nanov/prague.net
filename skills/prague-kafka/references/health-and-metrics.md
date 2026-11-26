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

`index.capacity` is the hash-table slots of an index's pooled sets (a logical capacity, not bytes); `index.size{kind="values"} / index.capacity` is their fill, where `index.capacity > 0` (`Unique` and `Range` indexes report 0). For a `Many` index on a collection property the ratio is half the real fill: capacity covers both directions, `values` counts a link once. A table that doubles when full is a little under half full right after it grows, so a `Many` index whose ratio stays well under one half has one of three causes: many keys with few values each against the default first size of 59 (give it a small `InitialCapacity`); an `InitialCapacity` larger than its buckets need; or a past peak — a bucket keeps the capacity of its largest moment until it empties (1000 values then 10 still hold 1327 slots), and no hint changes that.

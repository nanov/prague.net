# Prague Kafka — Observability Dashboards

Dashboards exposing the OpenTelemetry metrics emitted by
`MeterProviderBuilder.AddPragueKafkaInstrumentation()`. Both files cover the
same panels — pick the one matching your observability stack.

| File | Stack |
|---|---|
| `cx/prague-kafka.json` | Coralogix |
| `grafana/prague-kafka.json` | Grafana + Prometheus |

## Metric assumptions

Both dashboards assume the default (empty) instrumentation prefix —
`AddPragueKafkaInstrumentation()` without arguments — and the default
OpenTelemetry → Prometheus naming translation: dots → underscores, counters
get the `_total` suffix, units in curly-brace annotations (e.g.
`{partition}`) are stripped.

| Prague meter name | Prometheus name |
|---|---|
| `prague.kafka.consumer.partitions.assigned` | `prague_kafka_consumer_partitions_assigned` |
| `prague.kafka.consumer.broker.rtt.p99` | `prague_kafka_consumer_broker_rtt_p99` |
| `prague.kafka.consumer.health` | `prague_kafka_consumer_health` |
| `prague.kafka.cache.size` | `prague_kafka_cache_size` |
| `prague.kafka.cache.messages.received` | `prague_kafka_cache_messages_received_total` |
| `prague.kafka.cache.health` | `prague_kafka_cache_health` |
| `prague.kafka.cache.index.size` | `prague_kafka_cache_index_size` |

If you override the prefix or your exporter is configured to append units
(e.g. `_milliseconds` for the RTT gauge), edit the queries accordingly —
search-and-replace `prague_kafka_` with your prefix.

## Variables

Both dashboards expose four cascading multi-select filters, in order:

1. **`k8s_cluster_name`** — populated from `prague_kafka_consumer_health` label values.
2. **`k8s_pod_name`** — populated from `prague_kafka_consumer_health` filtered by the selected cluster (Grafana variant) or unfiltered (Coralogix variant — Coralogix uses query-time filtering only).
3. **`consumer`** — populated from `prague_kafka_consumer_health` filtered by cluster + pod.
4. **`cache`** — populated from `prague_kafka_cache_health` filtered by cluster + pod + consumer.

Every panel query intersects all four filters (`k8s_cluster_name=~"…",
k8s_pod_name=~"…", consumer=~"…", cache=~"…"`), so an empty selection or
"All" works as expected on every level.

The Grafana version additionally exposes a data-source picker via
`${DS_PROMETHEUS}`.

The `k8s_*` labels are the standard OTel Kubernetes resource attributes —
they appear automatically when the OTel SDK is configured with
`ResourceBuilder.AddDetector(new K8sResourceDetector())` or when the
OpenTelemetry Collector's `k8sattributes` processor is in the pipeline.

## Panels

Identical layout across stacks:

1. **Overview row** (4 stat tiles) — healthy consumers, healthy caches, total
   cache rows, messages received per second.
2. **Health row** — per-consumer and per-cache health timeseries (1 = healthy,
   0 = unhealthy).
3. **Throughput row** — per-cache message ingest rate (`rate(... [1m])`) and
   live cache row count.
4. **Broker row** — broker RTT p99 (max-of-p99 across brokers) and assigned
   partition count, both per consumer.
5. **Index row** — per-index key count and per-index value count, by
   `(consumer, cache, index)`.

## Importing

### Coralogix

```sh
cx dashboards create --from-file cx/prague-kafka.json
```

Or via the UI: Dashboards → New → Import → upload `cx/prague-kafka.json`.

### Grafana

UI: Dashboards → New → Import → upload `grafana/prague-kafka.json`, then pick
your Prometheus data source when prompted for `${DS_PROMETHEUS}`.

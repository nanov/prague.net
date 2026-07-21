# Prague performance baseline

A regression tripwire, not a benchmark suite. Each run produces a small set of
stable metrics that `compare.py` diffs against a committed, machine-specific
baseline; any metric that moves outside its tolerance band fails the run
(exit 1). CI runs it per-PR; locally you run it before/after a hot-path change.

## Configs

Three configs, each measuring a progressively larger slice of the stack:

- **core-only** — BenchmarkDotNet (`perf/Prague.Baseline.Bdn`). Per-op latency and
  allocation of the cache primitives in isolation: ingest `AddOrUpdate` and each
  query shape. No Kafka, no threading. Deterministic.
- **full-sim** — harness (`perf/Prague.Baseline.Harness`, `--config full-sim`).
  In-process replay of the **managed** Kafka ingest tail with **no broker and no
  librdkafka**: pre-encoded MessagePack value bytes pinned in native memory →
  `CacheSerde.DeserializeFromSpan` (zero-copy) → `Enricher.Enrich` →
  `AsyncValueBufferedWorker` ring → worker-thread `cache.AddOrUpdate`. It does
  **not** exercise the `RawConsumer` topic/poll loop or any librdkafka path —
  that boundary is deliberate, so full-sim stays deterministic and Docker-free.
- **full-real** — harness (`--config full-real`). Same harness against a real
  Kafka broker via Testcontainers. **Opt-in**: needs Docker, is excluded from
  `all` and from CI, and must be invoked explicitly.

## How to run

```bash
perf/run.sh core|sim|real|all [--bless] [--machine <class>]
```

- `core` — BDN core-only     `sim` — full-sim harness
- `real` — full-real harness (Docker)     `all` — core + sim (real is **not** included; run it explicitly)

The machine class is required; supply it via `--machine <class>` or the
`PRAGUE_PERF_MACHINE` env var (the flag wins). `PRAGUE_PERF_COMMIT` is set
automatically from `git rev-parse --short HEAD`. On a runner where the CPU name
is not auto-detected, set `PRAGUE_PERF_CPU` to label `env.cpu` in the output.

Examples:

```bash
perf/run.sh all --machine apple-m4pro-darwin        # core + sim, compare vs baseline
perf/run.sh core --machine apple-m4pro-darwin        # just the BDN core config
PRAGUE_PERF_MACHINE=linux-x64-ci perf/run.sh all     # machine class via env var
perf/run.sh real --machine apple-m4pro-darwin        # full-real (requires Docker)
```

Each config writes `perf/out/<config>.json`; `compare.py` then diffs those
against `perf/baseline/<machine-class>.json` and prints a per-metric table.

## Metrics

Metric ids are stable and config-scoped:

| id | unit | meaning |
|---|---|---|
| `ingest.throughput` | ent/s | entities applied per second (higher is better) |
| `ingest.alloc` | bytes | bytes allocated per ingested entity |
| `query.<type>.p50` / `.p99` / `.p999` | ns | query latency percentiles |
| `query.<type>.alloc` | bytes | bytes allocated per query |

Query `<type>` is one of `uniqueLookup`, `rangeScan`, `joinOne`, `joinMany`,
`multiJoin`.

Which config emits which:

- **core-only** — `ingest.throughput`, `ingest.alloc`, and for every query type
  its `.p50` and `.alloc`.
- **full-sim / full-real** — `ingest.throughput` plus `query.multiJoin.p50`,
  `.p99`, `.p999` (the tail of the heaviest query shape under a live ingest ring).

Note on `query.<type>.p50` across configs (same id, different statistic): for
**core-only** (BDN) the `.p50` value carries **BenchmarkDotNet's Mean** as a stable
per-op proxy — BDN does not emit a true percentile. The harness
`query.multiJoin.p50` / `.p99` / `.p999` are **true HdrHistogram percentiles**.

## Follow-ups

- **Concurrent read-under-write Phase B + `read.throughput` — implemented** (config
  `concurrent`). `ScenarioSpec.ReaderThreads` readers run the pooled `multiJoin` in a loop
  while 1 writer applies `ProductInfo`/`Offer` updates at `ScenarioSpec.WriterUpdatesPerSecond`
  over `ScenarioSpec.SteadyStateSeconds`, emitting `read.throughput` (result-rows/s) plus
  contended `query.multiJoin.p50/.p99/.p999` (per-reader histograms merged after join). Runs
  via `run.sh concurrent`; included in `run.sh all`. The single-threaded `QueryMixLatencyTest`
  is retained as the isolated (uncontended) latency signal.

## Tolerance bands

From `perf/thresholds.json`. A metric regresses when it moves against its
"better" direction by more than its band. Longest-prefix wins, but a matching
suffix (`.p999`, `.alloc`) takes precedence over any prefix.

| match | band |
|---|---|
| `ingest.throughput`, `read.throughput` (prefix) | ±10% |
| `query.` (prefix) | ±8% |
| `.p999` (suffix) | ±15% |
| `.alloc` (suffix, and `ingest.alloc`) | ±2% |
| everything else (`default`) | ±8% |

The wider `.p999` band absorbs tail jitter; the tight `.alloc` band catches new
allocations on paths that should stay allocation-stable.

## Baselines and re-blessing

Blessed baselines live in `perf/baseline/<machine-class>.json` (e.g.
`perf/baseline/apple-m4pro-darwin.json`). `perf/BASELINE.md` is a generated,
human-readable rollup of every committed baseline — never hand-edit it.

Run with `--bless` to record the current run as the new baseline: it rewrites
`perf/baseline/<machine-class>.json` and regenerates `perf/BASELINE.md`, then
exits 0 without comparing. Bless **intentionally** — only when a metric change
is expected and reviewed (an accepted perf trade-off, a new machine class, or a
deliberate improvement you want to lock in). Never bless to silence a
regression you have not explained.

```bash
perf/run.sh all --machine apple-m4pro-darwin --bless   # re-record core + sim
```

## Machine-class convention

Baselines are per machine class because absolute numbers are not portable across
hardware. The class is the baseline filename stem. When not set explicitly the
harness derives `<cpu-slug>-<os>-<arch>` (e.g. `apple-m4pro-darwin`), but CI and
shared runners pin an explicit, stable class via `PRAGUE_PERF_MACHINE` so the
committed baseline is matched deterministically (CI uses `linux-x64-ci`).

## CI

`.github/workflows/perf-baseline.yml` runs `core + sim` on `linux-x64-ci` per
PR and fails on any regression; it never blesses. The `linux-x64-ci` baseline
must be seeded once by a maintainer running the tripwire on that runner class
with `--bless` and committing `perf/baseline/linux-x64-ci.json`. Until then
`compare.py` reports every metric as `NEW` and the job passes.

# Prague performance baseline — design

**Date:** 2026-07-21
**Status:** approved

## Problem

Prague has ~20 exploratory BenchmarkDotNet micro-benchmarks in `benchmarks/Prague.Benchmarks`
(collections, joins, indexes, ring-buffer, concurrent read/write) plus two Kafka benches, and a
hand-pasted `RESULTS.MD`. What it lacks is a **canonical, repeatable baseline** — a fixed scenario
you rerun to answer one question: *"did I just make Prague slower?"*

There is no blessed set of headline numbers, no automated regression check, and the Kafka dispatch
bench (`KafkaLiveDispatchBenchmarks`) only stands in for librdkafka with a hand-built
`ConsumeResult` graph — it never runs the pipeline end-to-end into the cache.

The reference repo (`nanov.highperformance`) already has this muscle: a purpose-built
`ThroughputTestSession`/`LatencySessionContext` harness, layered `perf-local/L0-baseline … L3`
captures, and a `memory.md` decision log recording perf deltas per optimization layer. This design
ports that discipline to Prague.

## Goal

A **regression tripwire**: one canonical scenario, run in three configs, producing blessed headline
numbers (throughput, latency percentiles, alloc) that are auto-diffed against committed baselines
and fail past a tolerance band.

## Decisions (user-approved)

1. **Role:** regression tripwire — one canonical scenario per config, stable blessed headline
   numbers, rerun to catch regressions. (Not a general comparative A/B harness.)
2. **Three configs, differing only in how ingest events arrive:**
   - `core-only` — direct `AddOrUpdate`, **no Kafka assemblies referenced**.
   - `full-sim` — pre-encoded MessagePack payloads fed through the real managed ingest tail:
     `CacheSerde.DeserializeFromSpan` (`SpanMessagePackDeserializer`, zero-copy off a pinned native
     span) → real `Enricher.Enrich` → real `AsyncValueBufferedWorker` ring-buffer → `cache.AddOrUpdate`
     + indexing. Skips only librdkafka and the broker-bound topic-resolution loop (the `RawConsumer`
     types live in the external `Nanov.Confluent.Kafka` package). No broker, no Docker,
     deterministic. **CI tripwire.**
   - `full-real` — same payloads produced to a Testcontainers broker, consumed through the real
     Prague consumer. **Opt-in** (local / nightly), noisier, needs Docker.
3. **Hybrid engine:** BenchmarkDotNet drives `core-only` (per-op latency + alloc — where BDN
   excels and existing investment lives); a custom harness (ported from the reference rig) drives
   the full sustained pipeline (`full-sim` + `full-real`) throughput + latency percentiles.
4. **Two-phase scenario** on the established Product→Info→Offer 500×20 dataset: Phase A ingest
   (throughput + p99), Phase B steady-state read/join (latency + alloc), reported as distinct
   headline numbers.
5. **Tripwire = both** machine-readable committed baseline JSON (auto-diff, non-zero exit past
   tolerance) **and** a generated human-readable rollup (`BASELINE.md`).
6. **Packaging = dedicated `perf/` tree** (approach B), isolated from the exploratory
   `Prague.Benchmarks` sandbox, sharing one scenario library so all three configs exercise the
   identical dataset.

## Project structure

```
perf/
  Prague.Baseline.Scenario/     # shared class-lib: the ONE canonical scenario
    BaselineModels.cs           #   Product → Info(1:1) → Offer(1:N), [DataCache] POCOs
    DatasetFactory.cs           #   deterministic gen (fixed seed 42), 500×20
    ScenarioSpec.cs             #   counts, phase params, thread counts (single source of truth)
    ScenarioSteps.cs            #   phase bodies: BuildCaches / Ingest / ReadJoinLoop
    Payloads.cs                 #   pre-encoded MessagePack bytes for the sim/real Kafka path
  Prague.Baseline.Bdn/          # core-only tripwire (BenchmarkDotNet)
  Prague.Baseline.Harness/      # full pipeline tripwire (custom rig) — sim + real
  baseline/
    apple-m4pro-darwin.json     # blessed numbers per machine class
    <ci-runner-class>.json      # blessed numbers for the CI runner class
  thresholds.json               # per-metric tolerance bands
  BASELINE.md                   # generated human-readable rollup (regenerated on --bless)
  compare.py                    # diff current run vs blessed baseline; non-zero exit past tolerance
  run.sh                        # one entrypoint: run [core|sim|real|all] [--bless] [--machine <c>]
```

- Added to `Prague.sln`; **excluded** from `Prague.Publish.slnf` (not shippable).
- `Prague.Core` already exposes internals to `*.Benchmarks` via `InternalsVisibleTo`; extend the
  same grant to `Prague.Baseline.Scenario` / `.Bdn` / `.Harness` as needed for hot-path access.
- `benchmarks/Prague.Benchmarks` stays untouched as the exploratory sandbox.
- Compare script is Python (`compare.py`) — no extra .NET project, easy JSON diffing; `run.sh`
  orchestrates build + run + compare. (A PowerShell `run.ps1` twin is optional, not required.)

## The canonical scenario (shared, identical across all 3 configs)

Single dataset, single seed → `core-only` / `full-sim` / `full-real` numbers are directly
comparable. The **only** difference between configs is how Phase A entities arrive.

**Dataset** (`DatasetFactory`, fixed `Random(42)`):
- 500 `BaselineProduct` (root), with a `Range` index and representative Unique/Many index fields.
- 500 `BaselineProductInfo` (1:1 FK → Product).
- 10,000 `BaselineOffer` (1:N FK → Product, 20/product).
Shape reuses the established `JoinCacheBenchmarks` domain so the numbers connect to prior work.

**Phase A — Ingest (throughput):** apply all 11,000 entities into an empty cache until fully
indexed and queryable.
- Metrics: `ingest.throughput` (entities/s, higher-better); `ingest.latency.p50` /
  `ingest.latency.p99` (ns/entity, lower-better); `ingest.alloc` (bytes/entity, lower-better).

**Phase B — Steady-state read/join (latency):** N reader threads run a fixed query mix against the
loaded cache while 1 writer applies updates at a set rate.
- Query mix (from existing benches): indexed unique lookup, range scan, `JoinOne` (1:1),
  `JoinMany` (1:N), pooled multi-join.
- Metrics per query type: `query.<type>.p50` / `.p99` / `.p999` (ns, lower-better);
  `query.<type>.alloc` (bytes/op); `read.throughput` (reads/s, higher-better).

**Config-specific ingest source:**
- `core-only`: direct `AddOrUpdate`.
- `full-sim`: `Payloads` pre-encodes each entity to MessagePack once; Phase A feeds those bytes
  through the real `RawConsumer`/`SpanMessagePackDeserializer`/ring-buffer worker → cache.
- `full-real`: same `Payloads` bytes produced to a Testcontainers broker, consumed through the
  real Prague consumer. Phase B is byte-for-byte identical to the other configs.

## Engines & measurement

- **`Prague.Baseline.Bdn` (core-only only):** BDN classes, `[MemoryDiagnoser]`, a pinned `Job`
  (fixed warmup + iteration counts; toolchain choice — `InProcessNoEmitToolchain` vs a fixed
  external toolchain — settled during implementation for stability), fixed seed. One class for
  Phase-A ingest per-op + alloc, one for Phase-B query-mix latency + alloc. BDN's JSON export is
  normalized to the common schema by `compare.py`.
- **`Prague.Baseline.Harness` (full-sim + full-real):** ported/adapted from the reference
  `ThroughputTestSession` + `LatencySessionContext` (warmup, optional thread affinity,
  `ComputerSpecifications` capture, percentile histograms). Phase A = sustained throughput
  session; Phase B = latency session. Emits the common schema natively.

**Common result schema** (both engines):
```json
{ "machineClass": "apple-m4pro-darwin", "config": "full-sim",
  "commit": "<git-sha>", "timestampUtc": "2026-07-21T...Z",
  "env": { "cpu": "...", "os": "...", "dotnet": "...", "coreCount": 12 },
  "metrics": [
    { "id": "ingest.throughput", "unit": "ent/s", "value": 1234567, "higherIsBetter": true },
    { "id": "query.joinMany.p99", "unit": "ns", "value": 8421, "higherIsBetter": false }
  ] }
```

## Tripwire mechanism

- `baseline/<machine-class>.json` holds blessed `metrics[]` per config for that machine class.
  Machine class = normalized `CPU + OS` string (e.g. `apple-m4pro-darwin`); local M4 baseline and
  CI-runner baseline are separate committed files.
- `compare.py` loads the current run + the matching blessed baseline, joins on `metric.id`,
  computes `%Δ` honoring `higherIsBetter`, prints a table, and **exits non-zero if any metric
  regresses beyond its tolerance**.
- **Tolerance** (`thresholds.json`), per-metric with sane defaults; Apple-Silicon/macOS is noisy
  (no pinning, thermal decay — per the reference `memory.md`), so bands are deliberately generous:
  - throughput / read metrics: **±10%**
  - latency percentiles: **±8%** (p999 looser — **±15%** — tail is noisiest)
  - alloc: **±2%** (allocation is deterministic; a real change should show clearly)
- **Noise control:** each config runs K times (default 3), median per metric before compare.
- **Re-bless** is explicit: `run.sh <config> --bless` reruns, rewrites the machine-class JSON, and
  regenerates `BASELINE.md`. Never automatic.
- `BASELINE.md`: readable table of current blessed numbers + env + commit per config/machine class.
  Replaces the ad-hoc `RESULTS.MD` habit *for the tripwire* (RESULTS.MD stays as sandbox notes).

## Execution & CI

- `perf/run.sh core|sim|real|all [--bless] [--machine <class>]`: builds Release, runs the
  requested config(s) K times, writes `perf/out/<config>.json` (median), invokes `compare.py`
  against the committed baseline for the resolved machine class.
- **CI** (`.github/workflows`): a `perf` job runs `core` + `sim` only — deterministic, no Docker —
  on a fixed runner class, compares against that class's committed baseline, fails on regression.
- `full-real` is **opt-in**: `workflow_dispatch` / nightly, since it needs a broker container and
  is noisier. Not gating on PRs.

## Testing / verification

This deliverable is measurement tooling; "it works" is proven by exercising it, not unit tests:

1. Build all three `perf/` projects in Release.
2. Run `core` and `sim` end-to-end locally on the M4 Pro, produce `out/*.json`; `--bless` to seed
   `baseline/apple-m4pro-darwin.json`; re-run and confirm `compare.py` reports ~0% deltas, exit 0.
3. **Tripwire proof:** inject a deliberate slowdown (e.g. `Thread.Sleep`/extra work in a query
   path), confirm `compare.py` reports the regression and exits non-zero.
4. `full-real` smoke: one run against a Testcontainers broker to confirm the path works (blessed
   only if desired).

Unit tests are added only where a pure helper has non-trivial logic worth guarding (e.g. the
percentile computation or the schema normalizer), following the repo's NUnit conventions.

## Non-goals

- Not a comparative A/B micro-benchmark harness (that's the existing `Prague.Benchmarks` sandbox).
- Not replacing or migrating the existing exploratory benchmarks.
- Not a continuous perf-tracking dashboard / historical DB — just blessed baselines + auto-diff.
- No cross-machine number comparison — baselines are per-machine-class by construction.

## Open items settled during implementation

- BDN toolchain choice for stability (`InProcessNoEmitToolchain` vs fixed external).
- Exact Phase-B reader/writer thread counts and writer update rate (start from
  `JoinCacheBenchmarks`' 16 readers / 500ms writer, tune for a stable signal).
- CI runner machine-class string + seeding its committed baseline (first green CI run blesses it).

## Follow-ups

- **Concurrent Phase B (read-under-write) — implemented** (config `concurrent`).
  `ScenarioSpec.ReaderThreads` reader threads run the pooled `multiJoin` query mix while
  1 writer applies `ProductInfo`/`Offer` updates at `ScenarioSpec.WriterUpdatesPerSecond`
  over `ScenarioSpec.SteadyStateSeconds`, emitting `read.throughput` (result-rows/s) plus
  contended `query.multiJoin.p50/.p99/.p999` (per-reader `LongHistogram`s merged after
  join). `ConcurrentReadThroughputTest` + `ConcurrentQueryLatencyTest` live in the harness;
  `run.sh concurrent` runs it and `run.sh all` includes it. The single-threaded
  `QueryMixLatencyTest` is retained as the isolated (uncontended) latency signal.

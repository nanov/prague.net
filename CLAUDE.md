# CLAUDE.md

Guidance for Claude Code working in this repo. Keep this file compact — depth lives in [`context/`](context/) (map at the bottom).

## Overview

**Prague** — a high-performance, compile-time-safe in-memory event cache for .NET, fed by event-sourced Kafka streams. (Port of an internal B2Tech library; namespaces renamed to `Prague`.)

- Source-generator-driven: zero-reflection, compile-time-checked fluent query API.
- Index types: `Unique` (1:1), `Many` (1:N), `Range` (sorted), plus key-set / symmetric variants — automatic intersection + plan selection.
- Compile-time joins (1:1 and 1:N) with pooled, zero-allocation result sets; FK-attribute-driven `JoinWith{T}`.
- Two query flavors: eager `Query()…Execute()` and prepared `Prepare()…Build()` / `BuildFrozen()` (plan once, execute on demand, 0 B per execution). An unsorted frozen result has **no row-order guarantee** — see `context/query.md`.
- Kafka integration on the `Nanov.Confluent.Kafka` fork — raw, span-based zero-copy consume/produce path.

## Commands

```bash
dotnet build Prague.sln                 # full solution (warnings are errors)
dotnet build Prague.Publish.slnf        # publishable (NuGet) packages only
dotnet test  Prague.Tests.slnf          # all tests
dotnet test  tests/Prague.Core.Tests    # one project
dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories <cat>
```

- Every project multi-targets `net9.0;net10.0` (`Prague.Codegen` is `netstandard2.0`), so `dotnet run` needs `-f`.
- Solution filters split scope: `*.Publish.slnf` (shippable) vs `*.Tests.slnf` (test-only). A project in neither ships nor tests — check when adding one.
- `Prague.Kafka.IntegrationTests` need Docker (Testcontainers). Without it ~55 tests fail instantly on the socket; that is the only cause.
- Benchmarks: `--inProcess`, one category per run, nothing else running. Record machine load next to the numbers — 5–10 µs rows move more than the deltas being claimed. Measured tables live in `benchmarks/Prague.Benchmarks/RESULTS.MD`.
- Trust `dotnet build`; the editor language server reports stale errors a real compile does not.
- Central version and shared compiler settings (`Nullable`, `ImplicitUsings`, `LangVersion=latest`, `TreatWarningsAsErrors`) live in `Directory.Build.props`.

## Architecture

```
src/
  Prague.Attributes/   Public attributes ([DataCache], [DataCacheKey], [DataCacheIndex],
                       [DataCacheForeignKey], …)            → context/generated.md
  Prague.Codegen/      Roslyn source generator              → context/generated.md
  Prague.Core/         Runtime: InMemoryDataCache, indexing, query execution,
                       prepared/frozen queries, joins, T4 templates → context/core.md, query.md, joins.md
  Prague/              Top-level package facade
  Prague.Kafka/        Raw zero-copy consumer/producer, SerDe,
                       filters, health, OTel, background worker → context/kafka.md
  Prague.Api/ Prague.Api.UI/  HTTP inspection surface + UI
tests/        NUnit projects per layer (Core, DI, Generated, Kafka, Kafka.IntegrationTests); Tests.Models = fixtures lib
benchmarks/   Prague.Benchmarks (BenchmarkDotNet) + RESULTS.MD
www/ docs/    Public docs site + superpowers specs/plans
perf/         Regression tripwire: Prague.Baseline.{Bdn,Harness,Scenario}, compare.py diffs
              against perf/baseline/<machine-class>.json → perf/README.md
```

**Data flow:** `[DataCache]` POCO → codegen emits `XxxCache` partial + index storage + fluent `Query()` / `Prepare()` → at runtime `InMemoryDataCache` resolves the optimal index plan, intersects on a stackalloc bitmap (short-circuits on first empty set), returns allocating (`Execute()`) or pooled (`ExecutePooled()`, caller-`Dispose()`d) results. `BuildFrozen()` plans once at build time and binds one of three executors — `PointLookup`, `Pipeline`, `Replay`.

## Code conventions

House style is two skills — apply both, they are not optional:
- `code-style` — formatting, naming, structure for every `*.cs` file.
- `high-performance-net` — hot-path rules for anything in `Core`, `Kafka` SerDe/IO, or `benchmarks`.

`AllowUnsafeBlocks` is on in `Core`, `Kafka`, `Codegen`, `Benchmarks`. `Core` exposes internals via `InternalsVisibleTo` to the test and benchmark projects, `Prague.Kafka`, and `perf/Prague.Baseline.*`. **All test projects use NUnit** (`[TestFixture]`/`[Test]`/`[TestCase]`/`Assert.That`).

## Key invariants (don't break these)

- **Never hand-edit `*.generated.cs`** — change the source generator or the `*.tt` T4 template. T4 does **not** run on build: from `src/Prague.Core`, `~/.dotnet/tools/t4 <Name>.tt -o <dir>/<Name>.generated.cs`, then diff. After touching `Prague.Codegen`/T4, run `Prague.Generated.Tests`.
- **Every internal MessagePack call passes `PragueMessagePack.Options`** — never `MessagePackSerializer.DefaultOptions`. Compliance grep in `context/kafka-serde.md`.
- **Pooled results must be `Dispose()`d.** Join resolvers enforce exactly-one-Dispose via the `handedOff` guard — see `context/joins.md`.
- **Frozen-vs-eager differential tests are the safety net.** Sequence assertions build the frozen side with `PreserveEagerOrder = true`; multiset assertions cover the default path. Keep both; some byte-identity tests pass on the default path only because fixtures insert in Id order.
- **Anything added to a query path is decided at build time, not per row.** Twice a feature taxed plans that never used it; re-measure production shapes A/B after any executor change.
- Every `[DataCache]` user type is `partial`.
- Never commit `*.DotSettings.user` (gitignored per-user IDE state). If one reappears tracked: `git rm --cached <file>`.

## Context map — read when you need depth

Topic-first; each file opens with a one-line "Read when". Layer files (`core`/`generated`/`kafka`) are thin overviews that point into the topics.

| Working on… | Read |
|-------------|------|
| Runtime orientation (`InMemoryDataCache`, `IDataCache`) | [`context/core.md`](context/core.md) |
| Indexes — types, impls, symmetric/key-set, probe APIs | [`context/indexes.md`](context/indexes.md) |
| Query builder, candidate intersection, OR clause, pooled results | [`context/query.md`](context/query.md) |
| Prepared / frozen queries — `Prepare()`, `BuildFrozen()`, executors, ordering contract | [`context/query.md`](context/query.md) § Prepared queries, [`context/generated.md`](context/generated.md) § Prepared queries |
| Frozen work in flight — shipped steps, next tasks, operational notes | [`docs/superpowers/plans/2026-09-11-frozen-queries-handoff.md`](docs/superpowers/plans/2026-09-11-frozen-queries-handoff.md) |
| Joins — `JoinWith`/`JoinOne`/`JoinMany`, resolver families, paired core, inner/chained, leak-safety | [`context/joins.md`](context/joins.md) |
| Internal collections (`ValueSet`/`ValueDictionary`/`PooledSet`/`IncrementalIntersecter`) | [`context/collections.md`](context/collections.md) |
| Source generator, T4, attributes, FK `JoinWith` emission | [`context/generated.md`](context/generated.md) |
| Kafka orientation (lifecycle, raw zero-copy path, ring-buffer worker, OTel, `*Unsafe`) | [`context/kafka.md`](context/kafka.md) |
| Kafka message filters (`FilterDecision`, treatAsDelete) | [`context/kafka-filters.md`](context/kafka-filters.md) |
| Kafka header SerDe + `PragueMessagePack` isolation | [`context/kafka-serde.md`](context/kafka-serde.md) |
| Kafka health checks | [`context/kafka-health.md`](context/kafka-health.md) |
| Perf regression baseline — configs, running, updating baselines | [`perf/README.md`](perf/README.md) |
| Feature design history | `docs/superpowers/specs/` and `docs/superpowers/plans/` |

## Agent skills

- **Issue tracker:** GitHub Issues on `nanov/prague.net` via `gh`. See [`docs/agents/issue-tracker.md`](docs/agents/issue-tracker.md).
- **Triage labels:** five canonical roles, label string equal to role name. See [`docs/agents/triage-labels.md`](docs/agents/triage-labels.md).
- **Domain docs:** conventions for `CONTEXT.md` + `docs/adr/` in [`docs/agents/domain.md`](docs/agents/domain.md). Neither exists yet — proceed without them.

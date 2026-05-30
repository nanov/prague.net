# CLAUDE.md

Guidance for Claude Code working in this repo. Keep this file compact — deep detail lives in [`context/`](context/) (see the map at the bottom).

## Overview

**Prague** — a high-performance, compile-time-safe in-memory event cache for .NET 9, fed by event-sourced Kafka streams. (Port of an internal B2Tech library; namespaces renamed to `Prague`.)

- Source-generator-driven: zero-reflection, compile-time-safe fluent query API.
- Index types: `Unique` (1:1), `Many` (1:N), `Range` (sorted), plus key-set / symmetric variants — automatic intersection + plan selection.
- Compile-time joins (1:1 and 1:N) with pooled, zero-allocation result sets; FK-attribute-driven `JoinWith{T}` convenience methods.
- Kafka integration on the `Nanov.Confluent.Kafka` fork — raw, span-based zero-copy consume/produce path, change detection, conditional updates.
- Span-based, stack-allocated, SIMD-friendly hot paths (~9.7M reads/sec, <100ns indexed lookup, zero-alloc pooled queries).

## Commands

```bash
dotnet build Prague.sln                 # full solution
dotnet build Prague.Publish.slnf        # publishable (NuGet) packages only
dotnet test  Prague.Tests.slnf          # all tests
dotnet test  tests/Prague.Core.Tests    # a single test project
dotnet run -c Release --project benchmarks/Prague.Benchmarks   # BenchmarkDotNet
```

Solution filters split scope: `*.Publish.slnf` (shippable) vs `*.Tests.slnf` (test-only). Un-listed projects won't ship — check before adding one. Central version + `Nullable`/`ImplicitUsings`/`LangVersion=latest` in `Directory.Build.props`.

## Architecture

```
src/
  Prague.Attributes/   Public attributes ([DataCache], [DataCacheKey], [DataCacheIndex],
                       [DataCacheForeignKey], …)            → context/generated.md
  Prague.Codegen/      Roslyn source generator              → context/generated.md
  Prague.Core/         Runtime: InMemoryDataCache, indexing,
                       query execution, joins, T4 templates → context/core.md, context/joins.md
  Prague/              Top-level package facade
  Prague.Kafka/        Raw zero-copy consumer/producer, SerDe,
                       filters, health, OTel, background worker → context/kafka.md
  Prague.Api/ Prague.Api.UI/  HTTP inspection surface + UI
tests/        NUnit projects per layer (Core, DI, Generated, Kafka, Kafka.IntegrationTests); Tests.Models = fixtures lib
benchmarks/   Prague.Benchmarks (BenchmarkDotNet)
www/ docs/    Public docs site + superpowers specs/plans
```

**Data flow:** `[DataCache]` POCO → codegen emits `XxxCache` partial + index storage + fluent `Query()` → at runtime `InMemoryDataCache` resolves the optimal index plan, intersects on a stackalloc bitmap (short-circuits on first empty set), returns allocating (`Execute()`) or pooled (`ExecutePooled()`, caller-`Dispose()`d) results.

## Code conventions

House style is enforced by two skills — **read and apply them, they are not optional**:
- `code-style` — formatting/naming/structure (tabs width 2, file-scoped namespaces with usings **inside**, K&R braces, `var` everywhere, expression-bodied where it fits, `_camelCase` fields, no `this.`).
- `high-performance-net` — hot-path rules for anything in `Core`, `Kafka` SerDe/IO, or `benchmarks` (Span/stackalloc/ArrayPool/ref struct/zero-alloc, no LINQ/boxing/per-iteration allocs).

Other facts: .NET 9, `Nullable=enable`, `AllowUnsafeBlocks` on hot-path projects. `Core` exposes internals via `InternalsVisibleTo` to `*.Tests`/`*.Generated.Tests`/`*.Benchmarks`/`*.IntegrationTests`. **All test projects use NUnit** (`[TestFixture]`/`[Test]`/`[TestCase]`/`Assert.That`).

## Key invariants (don't break these)

- **Never hand-edit `*.generated.cs`** — change the source generator or the `*.tt` T4 template and rebuild. After touching `Prague.Codegen`/T4, run `Prague.Generated.Tests`.
- **Every internal MessagePack call passes `PragueMessagePack.Options`** — never `MessagePackSerializer.DefaultOptions`. Compliance grep in `context/kafka.md`.
- **Pooled results must be `Dispose()`d.** Join resolvers enforce exactly-one-Dispose via the `handedOff` guard — see `context/joins.md`.
- Every `[DataCache]` user type is `partial`.
- Never commit `*.DotSettings.user` (per-user IDE state, gitignored). If one reappears tracked: `git rm --cached <file>`.

## Context map — read when you need depth

Topic-first; each file opens with a one-line "Read when". Layer files (`core`/`generated`/`kafka`) are thin overviews that point into the topics.

| Working on… | Read |
|-------------|------|
| Runtime orientation (`InMemoryDataCache`, `IDataCache`) | [`context/core.md`](context/core.md) |
| Indexes — types, impls, symmetric/key-set, query/join usage | [`context/indexes.md`](context/indexes.md) |
| Query builder, candidate intersection, OR clause, pooled results | [`context/query.md`](context/query.md) |
| Joins — `JoinWith`/`JoinOne`/`JoinMany`, resolver families, paired core, inner/chained, leak-safety | [`context/joins.md`](context/joins.md) |
| Internal collections (`ValueSet`/`ValueDictionary`/`PooledSet`/`IncrementalIntersecter`) | [`context/collections.md`](context/collections.md) |
| Source generator, T4, attributes, FK `JoinWith` emission | [`context/generated.md`](context/generated.md) |
| Kafka orientation (lifecycle, raw zero-copy path, ring-buffer worker, OTel, `*Unsafe`) | [`context/kafka.md`](context/kafka.md) |
| Kafka message filters (`FilterDecision`, treatAsDelete) | [`context/kafka-filters.md`](context/kafka-filters.md) |
| Kafka header SerDe + `PragueMessagePack` isolation | [`context/kafka-serde.md`](context/kafka-serde.md) |
| Kafka health checks | [`context/kafka-health.md`](context/kafka-health.md) |
| Feature design history | `docs/superpowers/specs/` and `docs/superpowers/plans/` |

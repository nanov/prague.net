---
title: Installation
---

# Installation

## Requirements

- **.NET 9 SDK** — Prague uses static-abstract interface members, generic-math, and source-generator features that landed in C# 12+.
- **AllowUnsafeBlocks=true** — required on consumer projects that link against `Prague.Core` directly; the package metadata propagates this transitively for typical setups.

## Packages

| Package | Purpose |
| --- | --- |
| `Prague` | Top-level facade — references `Core`, `Attributes`, and `Codegen`. Add this to consumer projects. |
| `Prague.Core` | Runtime: `InMemoryDataCache`, indices, query builder, joins. |
| `Prague.Attributes` | The user-facing attribute set (`[DataCache]`, `[DataCacheKey]`, `[DataCacheIndex]`, etc.). |
| `Prague.Codegen` | Roslyn analyzer + source generator. Referenced as an analyzer so its types do not appear in your output. |
| `Prague.Kafka` | Kafka consumer/producer wiring, filters, change detection, OpenTelemetry. Optional. |
| `Prague.Kafka.TestAdaptor` | In-memory Kafka substitute for unit and integration tests. Optional. |

## Install

```bash
dotnet add package Prague
dotnet add package Prague.Kafka          # if you wire to a real broker
dotnet add package Prague.Kafka.TestAdaptor --version=*-* # tests only
```

## Project hygiene

Prague generates a `partial` companion class per `[DataCache]` POCO. The generator writes into the user project under the source generator output directory; nothing is committed. Ensure your `.csproj` does not exclude generated sources:

```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Source-generated files are visible under `Dependencies → Analyzers → Prague.Codegen` in your IDE. Do not edit them; re-run the build to regenerate.

## Next

- [Quick Start](quick-start.md) — define a cache, register it, run a query.

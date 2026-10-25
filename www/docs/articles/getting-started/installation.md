---
title: Installation
description: Prague NuGet packages, supported target frameworks and the project settings a consumer needs before the source generator runs.
---

# Installation

## Requirements

- **.NET 9 or .NET 10 SDK.** Every Prague package multi-targets `net9.0` and `net10.0`. The library itself is compiled as C# 14; set `LangVersion` to `latest` in consumer projects so the generated code (static-abstract interface members, `static` lambdas, `in` parameters) compiles.
- No `AllowUnsafeBlocks` is needed in consumer projects; the unsafe code lives inside `Prague.Core` and `Prague.Kafka`.

## Packages

| Package | Purpose |
| --- | --- |
| `Prague` | Top-level facade — references `Prague.Core`, `Prague.Attributes`, `Prague.Codegen` and `Prague.Kafka`. Add this to consumer projects that talk to a broker. |
| `Prague.Core` | Runtime: `InMemoryDataCache`, indices, query engine, prepared/frozen queries, joins, `QueryResults<T>`, `DataCacheRegistry`. |
| `Prague.Attributes` | The user-facing attribute set (`[DataCache]`, `[DataCacheKey]`, `[DataCacheIndex]`, `[DataCacheForeignKey<T>]`, …). The attributes live in namespace `Prague.Core`. |
| `Prague.Codegen` | Roslyn source generator (`netstandard2.0`). Referenced as an analyzer so its types do not appear in your output. |
| `Prague.Kafka` | Kafka consumer/producer on the `Nanov.Kafka` fork, SerDe, filters, change detection, health checks, OpenTelemetry. Pulled in by `Prague`; reference `Prague.Core` + `Prague.Attributes` + `Prague.Codegen` directly to stay broker-free. |
| `Prague.Api` / `Prague.Api.UI` | `MapPragueEndpoints()` HTTP inspection surface over live caches, and a MudBlazor UI for it. Optional. |

The version shown in the sidebar is the latest stable release on NuGet. Prerelease builds carry a `-pre.N` suffix and need `--prerelease` on `dotnet add package`.

## Install

```bash
dotnet add package Prague                # Core + Attributes + Codegen + Kafka
dotnet add package Prague.Api            # optional: HTTP inspection endpoints
dotnet add package Prague.Api.UI         # optional: Blazor UI over Prague.Api
```

Without a broker, reference the three building blocks instead. `Prague.Codegen` ships as an analyzer package, so it runs at compile time and nothing of it lands in your output assembly:

```xml
<ItemGroup>
  <PackageReference Include="Prague.Core" Version="x.y.z"/>
  <PackageReference Include="Prague.Attributes" Version="x.y.z"/>
  <PackageReference Include="Prague.Codegen" Version="x.y.z"/>
</ItemGroup>
```

A single `using Prague.Core;` brings in the attributes, the cache types and the query extensions.

## Project hygiene

Prague generates a `partial` companion class per `[DataCache]` POCO (the `XxxCache` wrapper) plus `CacheEquals` / `Clone()` partials on the POCO itself. Generated files live under `obj/`; nothing is committed and nothing should be edited by hand.

```xml
<PropertyGroup>
  <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Source-generated files are visible under `Dependencies → Analyzers → Prague.Codegen` in your IDE. Trust `dotnet build` over the editor's language server, which can report stale errors on generated code.

## Next

- [Quick Start](quick-start.md) — define a cache, register it, run a query.

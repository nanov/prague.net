# Prague Performance Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a regression-tripwire performance baseline for Prague — one canonical Product→Info→Offer scenario run in three configs (`core-only`, `full-sim`, `full-real`), producing blessed headline numbers auto-diffed against committed baselines.

**Architecture:** A dedicated `perf/` tree with a shared scenario library (models + deterministic dataset + pre-encoded payloads), a BenchmarkDotNet project driving `core-only` per-op latency/alloc, and a custom throughput/latency harness (ported from `nanov.highperformance/perf-tests`) driving the `full-sim` and `full-real` sustained pipeline. Both engines emit one common JSON result schema; a Python `compare.py` diffs a run against a committed per-machine-class baseline and exits non-zero past tolerance; `BASELINE.md` is the human-readable rollup.

**Tech Stack:** .NET 9 + .NET 10 (multi-target), C# `LangVersion=latest`, BenchmarkDotNet 0.15.8, HdrHistogram (NuGet), Testcontainers.Kafka 4.11.0 (full-real only), MessagePack 3.x via `PragueMessagePack.Options`, Python 3 (stdlib only) for `compare.py`, Bash for `run.sh`.

## Global Constraints

- **TFMs:** `net9.0;net10.0` on every new .NET project (copy from `benchmarks/Prague.Benchmarks/Prague.Benchmarks.csproj`).
- **Root MSBuild:** `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`. Every new csproj MUST carry `<NoWarn>$(NoWarn);CS8618;CS8602;CS8600;CS8604;CS8625;CS8601;CS8603;CS1061</NoWarn>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (query builders use `Unsafe.AsRef`; sim uses `NativeMemory`).
- **Internals access is name-gated.** `src/Prague.Core/Prague.Core.csproj` and `src/Prague.Kafka/Prague.Kafka.csproj` list `InternalsVisibleTo` targets. The perf projects that touch internals (`Prague.Baseline.Bdn`, `Prague.Baseline.Harness`, `Prague.Baseline.Scenario`) MUST be added to both.
- **Source generator reference:** any project defining `[DataCache]` POCOs MUST reference `src/Prague.Codegen/Prague.Codegen.csproj` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, else the `{ClassName}Cache` classes are never emitted.
- **Generated cache naming:** class `Foo` → generated `FooCache` with `.Cache`, `.AddOrUpdate(...)`, `.Query()`, per-FK `{FkProp}Index`, and `JoinWith{RelatedEntity}()`.
- **MessagePack parity:** every serialize/deserialize MUST pass `PragueMessagePack.Options` (via `CacheSerde<T>`), never `MessagePackSerializer.DefaultOptions`.
- **House style:** tabs width 2, file-scoped namespaces with usings inside, K&R braces, `var`, `_camelCase` private fields, no `this.`. Read the `code-style` and `high-performance-net` skills before writing hot-path code.
- **Not shippable:** every perf project sets `<IsPackable>false</IsPackable>` and is excluded from `Prague.Publish.slnf`.
- **`full-sim` boundary (do not over-claim):** it exercises the real managed ingest tail — `CacheSerde.DeserializeFromSpan` (`SpanMessagePackDeserializer`, zero-copy off a pinned native span) → real `Enricher.Enrich` → real `AsyncValueBufferedWorker` ring-buffer → `cache.AddOrUpdate` + indexing. It does NOT run librdkafka or the broker-bound `RawConsumer` topic loop (those types are in the external `Nanov.Confluent.Kafka` package).
- **Result schema id vocabulary (stable — never rename once blessed):** `ingest.throughput`, `ingest.latency.p50`, `ingest.latency.p99`, `ingest.alloc`, `read.throughput`, and per query type `query.<type>.p50|p99|p999|alloc` where `<type>` ∈ `{uniqueLookup, rangeScan, joinOne, joinMany, multiJoin}`.

---

## File Structure

```
perf/
  Prague.Baseline.Scenario/                  # shared class-lib
    Prague.Baseline.Scenario.csproj
    BaselineModels.cs                         # BaselineProduct / BaselineProductInfo / BaselineOffer
    ScenarioSpec.cs                           # counts, seed, thread/rate constants (single source of truth)
    DatasetFactory.cs                         # deterministic dataset (seed 42)
    Payloads.cs                               # pre-encoded MessagePack key+value bytes per entity
    ResultSchema.cs                           # BaselineResult / Metric records + JSON write/merge
    EnvCapture.cs                             # machine-class string + env block
  Prague.Baseline.Bdn/                        # core-only tripwire (BenchmarkDotNet)
    Prague.Baseline.Bdn.csproj
    Program.cs
    CoreIngestBenchmarks.cs                   # Phase A: ingest per-op + alloc
    CoreQueryBenchmarks.cs                    # Phase B: query-mix latency + alloc
    BdnResultExport.cs                        # normalize BDN summary -> ResultSchema JSON
  Prague.Baseline.Harness/                    # full pipeline tripwire (ported rig)
    Prague.Baseline.Harness.csproj
    Program.cs
    ProgramOptions.cs
    IThroughputTest.cs  ILatencyTest.cs
    ThroughputSessionContext.cs  ThroughputTestSessionResult.cs  ThroughputTestSession.cs
    LatencySessionContext.cs     LatencyTestSessionResult.cs     LatencyTestSession.cs
    PerfTestType.cs  PerfTestTypeSelector.cs
    Support/StopwatchUtil.cs  Support/ComputerSpecifications.cs  Support/PerfTestUtil.cs
    Tests/CoreIngestThroughputTest.cs         # config core-only ingest (harness form, for parity)
    Tests/SimIngestThroughputTest.cs          # config full-sim ingest
    Tests/RealIngestThroughputTest.cs         # config full-real ingest (Testcontainers)
    Tests/QueryMixLatencyTest.cs              # Phase B latency (shared across configs)
    Sim/SimIngestPipeline.cs                  # pinned-span -> deserialize -> enrich -> worker -> cache
    Sim/CacheApplyWorker.cs                   # AsyncValueBufferedWorker<Slot> that applies AddOrUpdate
    Real/RealIngestPipeline.cs                # Testcontainers broker + Prague consumer wiring
  baseline/
    apple-m4pro-darwin.json                   # blessed numbers (M4 dev host)
    linux-x64-ci.json                         # blessed numbers (CI runner) — seeded by first green run
  thresholds.json                            # per-metric tolerance bands
  compare.py                                 # diff current run vs blessed baseline; regenerate BASELINE.md
  run.sh                                     # entrypoint: run [core|sim|real|all] [--bless] [--machine <c>]
  BASELINE.md                                # generated rollup
```

Solution wiring: add the three `.csproj` to `Prague.sln`; exclude all three from `Prague.Publish.slnf`.

---

## Phase 0 — Foundation

### Task 0.1: Scenario project + internals wiring

**Files:**
- Create: `perf/Prague.Baseline.Scenario/Prague.Baseline.Scenario.csproj`
- Modify: `src/Prague.Core/Prague.Core.csproj` (InternalsVisibleTo block)
- Modify: `src/Prague.Kafka/Prague.Kafka.csproj` (InternalsVisibleTo block)
- Modify: `Prague.sln`, `Prague.Publish.slnf`

**Interfaces:**
- Produces: a compilable class-lib `Prague.Baseline.Scenario` with codegen wired and internals access to Core + Kafka.

- [ ] **Step 1: Write the csproj**

`perf/Prague.Baseline.Scenario/Prague.Baseline.Scenario.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);CS8618;CS8602;CS8600;CS8604;CS8625;CS8601;CS8603;CS1061</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Prague.Core\Prague.Core.csproj" />
    <ProjectReference Include="..\..\src\Prague.Kafka\Prague.Kafka.csproj" />
    <ProjectReference Include="..\..\src\Prague.Attributes\Prague.Attributes.csproj" />
    <ProjectReference Include="..\..\src\Prague.Codegen\Prague.Codegen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add InternalsVisibleTo to Core and Kafka**

In `src/Prague.Core/Prague.Core.csproj`, inside the existing `<ItemGroup>` that holds the `InternalsVisibleTo` entries (around lines 14-18), add:
```xml
    <InternalsVisibleTo Include="Prague.Baseline.Scenario" />
    <InternalsVisibleTo Include="Prague.Baseline.Bdn" />
    <InternalsVisibleTo Include="Prague.Baseline.Harness" />
```
Add the identical three lines to the matching `InternalsVisibleTo` group in `src/Prague.Kafka/Prague.Kafka.csproj` (around lines 14-17).

- [ ] **Step 3: Add project to solution + exclude from publish filter**

Run:
```bash
cd /Users/dimitarnanov/work/private/github/nanov/prague.net
dotnet sln Prague.sln add perf/Prague.Baseline.Scenario/Prague.Baseline.Scenario.csproj
```
Then confirm `Prague.Publish.slnf` does NOT list the new project (it enumerates shippable projects explicitly; leaving it out is the exclusion).

- [ ] **Step 4: Verify it builds**

Run: `dotnet build perf/Prague.Baseline.Scenario/Prague.Baseline.Scenario.csproj -c Release`
Expected: build succeeds (empty project, generator wired).

- [ ] **Step 5: Commit**

```bash
git add perf/Prague.Baseline.Scenario src/Prague.Core/Prague.Core.csproj src/Prague.Kafka/Prague.Kafka.csproj Prague.sln
git commit -m "perf: scaffold Prague.Baseline.Scenario + internals access"
```

---

### Task 0.2: Baseline entity models

**Files:**
- Create: `perf/Prague.Baseline.Scenario/BaselineModels.cs`

**Interfaces:**
- Produces: `BaselineProduct`, `BaselineProductInfo`, `BaselineOffer` ([DataCache] partial POCOs). Generated caches: `BaselineProductCache`, `BaselineProductInfoCache`, `BaselineOfferCache`, each with `.Cache`, `.AddOrUpdate(entity)`, `.AddOrUpdate(entity, long ts)`, `.Query()`, `.JoinWithBaselineProductInfo()`, and `BaselineOfferCache.ProductIdIndex`.

- [ ] **Step 1: Write the models**

`perf/Prague.Baseline.Scenario/BaselineModels.cs` — superset shape usable by all three configs (DataCache + MessagePack wire keys + IDataCacheItem + topic). Explicit `[Key(n)]` indices are required for wire stability across sim/real:
```csharp
namespace Prague.Baseline.Scenario;

using Core;
using MessagePack;
using Prague.Attributes;

[DataCache]
[DataCacheTopic("baseline-products")]
[MessagePackObject]
public partial class BaselineProduct : IDataCacheItem<int, BaselineProduct> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheIndex(DataCacheIndexType.Range)] public int Range { get; set; }
	[Key(2)] public string Category { get; set; } = string.Empty;
	[Key(3)] public string Status { get; set; } = string.Empty;
	[Key(4)] public bool IsPublished { get; set; }
	[Key(5)] public int PrimaryValue { get; set; }
	[Key(6)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}

[DataCache]
[DataCacheTopic("baseline-product-infos")]
[MessagePackObject]
public partial class BaselineProductInfo : IDataCacheItem<int, BaselineProductInfo> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheForeignKey<BaselineProduct>(DataCacheJoinType.OneToOne)] public int ProductId { get; set; }
	[Key(2)] public string Warehouse { get; set; } = string.Empty;
	[Key(3)] public int StockCount { get; set; }
	[Key(4)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}

[DataCache]
[DataCacheTopic("baseline-offers")]
[MessagePackObject]
public partial class BaselineOffer : IDataCacheItem<int, BaselineOffer> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheForeignKey<BaselineProduct>(DataCacheJoinType.OneToMany)] public int ProductId { get; set; }
	[Key(2)] public bool IsActive { get; set; }
	[Key(3)] public decimal BasePrice { get; set; }
	[Key(4)] public int DisplayOrder { get; set; }
	[Key(5)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}
```

> If the generator rejects any attribute combination (e.g. `[DataCacheTopic]` requires the Kafka package, or `IDataCacheItem` requires extra members), reconcile against `tests/Prague.Kafka.IntegrationTests/Entities/Product.cs` (the known-good superset template) and `benchmarks/Prague.Benchmarks/KafkaLiveDispatchBenchmarks.cs` `LiveDispatchEntity` — copy the exact member set those use. Do not invent members.

- [ ] **Step 2: Verify generation compiles**

Run: `dotnet build perf/Prague.Baseline.Scenario -c Release`
Expected: success; generated `BaselineProductCache` etc. exist (compile error on a later task referencing them would prove absence).

- [ ] **Step 3: Commit**

```bash
git add perf/Prague.Baseline.Scenario/BaselineModels.cs
git commit -m "perf: baseline Product/Info/Offer entities"
```

---

### Task 0.3: Scenario spec + deterministic dataset

**Files:**
- Create: `perf/Prague.Baseline.Scenario/ScenarioSpec.cs`
- Create: `perf/Prague.Baseline.Scenario/DatasetFactory.cs`
- Test: `tests/Prague.Core.Tests` (add `BaselineDatasetTests.cs`) — determinism guard

**Interfaces:**
- Produces:
  - `ScenarioSpec` static: `const int ProductCount = 500;`, `const int OffersPerProduct = 20;`, `const int TotalOffers = 10_000;`, `const int TotalEntities = 11_000; // 500 products + 500 infos + 10000 offers`, `const int Seed = 42;`, `const int ReaderThreads = 16;`, `const int WriterUpdatesPerSecond = 2;`, `const int SteadyStateSeconds = 5;`.
  - `DatasetFactory.Build() -> Dataset` where `readonly record struct Dataset(BaselineProduct[] Products, BaselineProductInfo[] Infos, BaselineOffer[] Offers)`.

- [ ] **Step 1: Write the failing determinism test**

`tests/Prague.Core.Tests/BaselineDatasetTests.cs`:
```csharp
namespace Prague.Core.Tests;

using NUnit.Framework;
using Prague.Baseline.Scenario;

[TestFixture]
public class BaselineDatasetTests {
	[Test]
	public void Build_IsDeterministic_AndHasExpectedCounts() {
		var a = DatasetFactory.Build();
		var b = DatasetFactory.Build();

		Assert.That(a.Products.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		Assert.That(a.Infos.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		Assert.That(a.Offers.Length, Is.EqualTo(ScenarioSpec.TotalOffers));

		// Determinism: same seed -> identical field values.
		Assert.That(a.Products[0].Range, Is.EqualTo(b.Products[0].Range));
		Assert.That(a.Offers[^1].BasePrice, Is.EqualTo(b.Offers[^1].BasePrice));
	}
}
```
Add a ProjectReference from `tests/Prague.Core.Tests/Prague.Core.Tests.csproj` to `perf/Prague.Baseline.Scenario/Prague.Baseline.Scenario.csproj`.

- [ ] **Step 2: Run test, verify it fails to compile/pass**

Run: `dotnet test tests/Prague.Core.Tests --filter BaselineDatasetTests`
Expected: FAIL — `ScenarioSpec`/`DatasetFactory` do not exist.

- [ ] **Step 3: Write ScenarioSpec**

`perf/Prague.Baseline.Scenario/ScenarioSpec.cs`:
```csharp
namespace Prague.Baseline.Scenario;

public static class ScenarioSpec {
	public const int ProductCount = 500;
	public const int OffersPerProduct = 20;
	public const int TotalOffers = ProductCount * OffersPerProduct;
	public const int TotalEntities = ProductCount + ProductCount + TotalOffers;
	public const int Seed = 42;
	public const int ReaderThreads = 16;
	public const int WriterUpdatesPerSecond = 2;
	public const int SteadyStateSeconds = 5;
	public const int RingCapacity = 4096;
}
```

- [ ] **Step 4: Write DatasetFactory**

`perf/Prague.Baseline.Scenario/DatasetFactory.cs`:
```csharp
namespace Prague.Baseline.Scenario;

public readonly record struct Dataset(
	BaselineProduct[] Products,
	BaselineProductInfo[] Infos,
	BaselineOffer[] Offers);

public static class DatasetFactory {
	public static Dataset Build() {
		var rng = new Random(ScenarioSpec.Seed);
		var products = new BaselineProduct[ScenarioSpec.ProductCount];
		var infos = new BaselineProductInfo[ScenarioSpec.ProductCount];
		var offers = new BaselineOffer[ScenarioSpec.TotalOffers];

		var offerId = 1;
		for (var i = 0; i < ScenarioSpec.ProductCount; i++) {
			var id = i + 1;
			products[i] = new BaselineProduct {
				Id = id,
				Range = rng.Next(0, ScenarioSpec.ProductCount),
				Category = "Category_" + (id % 10),
				Status = (id % 3) switch { 0 => "Active", 1 => "Scheduled", _ => "Archived" },
				IsPublished = id % 2 == 0,
				PrimaryValue = rng.Next(0, 5),
				LastUpdated = id,
			};
			infos[i] = new BaselineProductInfo {
				Id = id,
				ProductId = id,
				Warehouse = "WH_" + (id % 7),
				StockCount = rng.Next(0, 1000),
				LastUpdated = id,
			};
			for (var k = 0; k < ScenarioSpec.OffersPerProduct; k++) {
				offers[offerId - 1] = new BaselineOffer {
					Id = offerId,
					ProductId = id,
					IsActive = offerId % 4 != 0,
					BasePrice = offerId % 1000,
					DisplayOrder = k,
					LastUpdated = offerId,
				};
				offerId++;
			}
		}
		return new Dataset(products, infos, offers);
	}
}
```

- [ ] **Step 5: Run test, verify pass**

Run: `dotnet test tests/Prague.Core.Tests --filter BaselineDatasetTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add perf/Prague.Baseline.Scenario/ScenarioSpec.cs perf/Prague.Baseline.Scenario/DatasetFactory.cs tests/Prague.Core.Tests
git commit -m "perf: scenario spec + deterministic dataset factory"
```

---

### Task 0.4: Result schema + env capture

**Files:**
- Create: `perf/Prague.Baseline.Scenario/ResultSchema.cs`
- Create: `perf/Prague.Baseline.Scenario/EnvCapture.cs`
- Test: `tests/Prague.Core.Tests/BaselineResultSchemaTests.cs`

**Interfaces:**
- Produces:
  - `sealed record Metric(string Id, string Unit, double Value, bool HigherIsBetter)`.
  - `sealed record BaselineResult(string MachineClass, string Config, string Commit, string TimestampUtc, EnvBlock Env, IReadOnlyList<Metric> Metrics)`.
  - `sealed record EnvBlock(string Cpu, string Os, string Dotnet, int CoreCount)`.
  - `static class ResultWriter { string ToJson(BaselineResult r); void Write(string path, BaselineResult r); }` — `System.Text.Json`, indented, camelCase.
  - `static class EnvCapture { EnvBlock Current(); string MachineClass(); }` where `MachineClass()` returns a normalized `cpu-os` slug (e.g. `apple-m4pro-darwin`, `linux-x64-ci`).

- [ ] **Step 1: Write the failing test**

`tests/Prague.Core.Tests/BaselineResultSchemaTests.cs`:
```csharp
namespace Prague.Core.Tests;

using System.Text.Json;
using NUnit.Framework;
using Prague.Baseline.Scenario;

[TestFixture]
public class BaselineResultSchemaTests {
	[Test]
	public void ToJson_RoundTrips_MetricsAndCamelCases() {
		var result = new BaselineResult(
			"apple-m4pro-darwin", "core-only", "abc123", "2026-07-21T00:00:00Z",
			new EnvBlock("Apple M4 Pro", "Darwin", ".NET 9.0", 12),
			new[] { new Metric("ingest.throughput", "ent/s", 1000.0, true) });

		var json = ResultWriter.ToJson(result);
		Assert.That(json, Does.Contain("\"machineClass\""));
		Assert.That(json, Does.Contain("\"ingest.throughput\""));

		using var doc = JsonDocument.Parse(json);
		Assert.That(doc.RootElement.GetProperty("metrics").GetArrayLength(), Is.EqualTo(1));
		Assert.That(doc.RootElement.GetProperty("metrics")[0].GetProperty("higherIsBetter").GetBoolean(), Is.True);
	}
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Prague.Core.Tests --filter BaselineResultSchemaTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write ResultSchema.cs**

```csharp
namespace Prague.Baseline.Scenario;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record Metric(string Id, string Unit, double Value, bool HigherIsBetter);

public sealed record EnvBlock(string Cpu, string Os, string Dotnet, int CoreCount);

public sealed record BaselineResult(
	string MachineClass, string Config, string Commit, string TimestampUtc,
	EnvBlock Env, IReadOnlyList<Metric> Metrics);

public static class ResultWriter {
	private static readonly JsonSerializerOptions Options = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
	};

	public static string ToJson(BaselineResult result) => JsonSerializer.Serialize(result, Options);

	public static void Write(string path, BaselineResult result) {
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, ToJson(result));
	}
}
```
> Note: `Metric.Id` serializes as `"id"` under camelCase, and its value string (e.g. `"ingest.throughput"`) is what the schema-vocabulary refers to — the property NAME is `id`, the VALUE carries the dotted metric id. The test asserts the value string appears in JSON; that holds.

- [ ] **Step 4: Write EnvCapture.cs**

```csharp
namespace Prague.Baseline.Scenario;

using System.Runtime.InteropServices;

public static class EnvCapture {
	public static EnvBlock Current() => new(
		Cpu: CpuName(),
		Os: RuntimeInformation.OSDescription,
		Dotnet: RuntimeInformation.FrameworkDescription,
		CoreCount: Environment.ProcessorCount);

	public static string MachineClass() {
		var env = Environment.GetEnvironmentVariable("PRAGUE_PERF_MACHINE");
		if (!string.IsNullOrWhiteSpace(env)) return env;
		var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin"
			: RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
			: "windows";
		var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		return Slug(CpuName()) + "-" + os + "-" + arch;
	}

	private static string CpuName() {
		// Best-effort; overridden by PRAGUE_PERF_MACHINE in CI.
		return Environment.GetEnvironmentVariable("PRAGUE_PERF_CPU") ?? "cpu";
	}

	private static string Slug(string s) {
		Span<char> buf = stackalloc char[s.Length];
		var n = 0;
		foreach (var c in s) {
			if (char.IsLetterOrDigit(c)) buf[n++] = char.ToLowerInvariant(c);
			else if (n > 0 && buf[n - 1] != '-') buf[n++] = '-';
		}
		return new string(buf[..n]).Trim('-');
	}
}
```
> Machine class is authoritative from `PRAGUE_PERF_MACHINE` (set by `run.sh --machine` and CI). CPU auto-detection is intentionally minimal — the env override is the real source of truth, keeping the number stable and machine-tagged.

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test tests/Prague.Core.Tests --filter BaselineResultSchemaTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add perf/Prague.Baseline.Scenario/ResultSchema.cs perf/Prague.Baseline.Scenario/EnvCapture.cs tests/Prague.Core.Tests
git commit -m "perf: result schema + env/machine-class capture"
```

---

### Task 0.5: Pre-encoded payloads

**Files:**
- Create: `perf/Prague.Baseline.Scenario/Payloads.cs`
- Test: `tests/Prague.Kafka.Tests/BaselinePayloadTests.cs` (Kafka.Tests already has internals access to `CacheSerde`)

**Interfaces:**
- Produces: `sealed record EncodedEntity(byte[] Key, byte[] Value)`; `static class Payloads { EncodedSet Encode(Dataset d); }` with `readonly record struct EncodedSet(EncodedEntity[] Products, EncodedEntity[] Infos, EncodedEntity[] Offers)`. Uses `CacheSerde<T>.Serialize` (== `MessagePackSerializer.Serialize(value, PragueMessagePack.Options)`).

- [ ] **Step 1: Write the failing test**

`tests/Prague.Kafka.Tests/BaselinePayloadTests.cs`:
```csharp
namespace Prague.Kafka.Tests;

using NUnit.Framework;
using Prague.Baseline.Scenario;
using Prague.Kafka.SerDe;

[TestFixture]
public class BaselinePayloadTests {
	[Test]
	public void Encode_ValueBytes_RoundTripDeserialize() {
		var data = DatasetFactory.Build();
		var encoded = Payloads.Encode(data);

		Assert.That(encoded.Products.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		var back = CacheSerde<BaselineProduct>.DeserializeFromSpan(encoded.Products[0].Value);
		Assert.That(back.Id, Is.EqualTo(data.Products[0].Id));
		Assert.That(back.Range, Is.EqualTo(data.Products[0].Range));
	}
}
```
Add ProjectReference from `tests/Prague.Kafka.Tests` to `perf/Prague.Baseline.Scenario`.

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter BaselinePayloadTests`
Expected: FAIL — `Payloads` missing.

- [ ] **Step 3: Write Payloads.cs**

```csharp
namespace Prague.Baseline.Scenario;

using Prague.Kafka.SerDe;

public sealed record EncodedEntity(byte[] Key, byte[] Value);

public readonly record struct EncodedSet(
	EncodedEntity[] Products, EncodedEntity[] Infos, EncodedEntity[] Offers);

public static class Payloads {
	public static EncodedSet Encode(Dataset d) => new(
		Map(d.Products, static p => p.Id),
		Map(d.Infos, static i => i.Id),
		Map(d.Offers, static o => o.Id));

	private static EncodedEntity[] Map<T>(T[] items, Func<T, int> key) {
		var result = new EncodedEntity[items.Length];
		for (var i = 0; i < items.Length; i++) {
			result[i] = new EncodedEntity(
				CacheSerde<int>.Serialize(key(items[i])),
				CacheSerde<T>.Serialize(items[i]));
		}
		return result;
	}
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test tests/Prague.Kafka.Tests --filter BaselinePayloadTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add perf/Prague.Baseline.Scenario/Payloads.cs tests/Prague.Kafka.Tests
git commit -m "perf: pre-encoded MessagePack payloads for sim/real ingest"
```

---

## Phase 1 — Core-only tripwire (BenchmarkDotNet)

### Task 1.1: BDN project skeleton

**Files:**
- Create: `perf/Prague.Baseline.Bdn/Prague.Baseline.Bdn.csproj`
- Create: `perf/Prague.Baseline.Bdn/Program.cs`
- Modify: `Prague.sln`

**Interfaces:**
- Produces: runnable BDN switcher exe referencing scenario + core.

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);CS8618;CS8602;CS8600;CS8604;CS8625;CS8601;CS8603;CS1061</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.15.8" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Prague.Baseline.Scenario\Prague.Baseline.Scenario.csproj" />
    <ProjectReference Include="..\..\src\Prague.Core\Prague.Core.csproj" />
    <ProjectReference Include="..\..\src\Prague.Attributes\Prague.Attributes.csproj" />
    <ProjectReference Include="..\..\src\Prague.Codegen\Prague.Codegen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```
> Note: `Prague.Baseline.Scenario` already pulls in the generated caches; the Codegen analyzer ref here is only needed if this project itself defines `[DataCache]` types. It does not, but keeping the analyzer ref is harmless and future-proof. If it causes duplicate-generation errors, drop the Codegen ProjectReference from this csproj.

- [ ] **Step 2: Program.cs**

```csharp
namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Running;

internal static class Program {
	private static void Main(string[] args) =>
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

- [ ] **Step 3: Add to solution, build**

```bash
dotnet sln Prague.sln add perf/Prague.Baseline.Bdn/Prague.Baseline.Bdn.csproj
dotnet build perf/Prague.Baseline.Bdn -c Release
```
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Bdn Prague.sln
git commit -m "perf: scaffold Prague.Baseline.Bdn"
```

---

### Task 1.2: Core ingest + query benchmarks

**Files:**
- Create: `perf/Prague.Baseline.Bdn/CoreIngestBenchmarks.cs`
- Create: `perf/Prague.Baseline.Bdn/CoreQueryBenchmarks.cs`

**Interfaces:**
- Consumes: `DatasetFactory.Build()`, generated `BaselineProductCache`/`BaselineProductInfoCache`/`BaselineOfferCache`, `DataCacheRegistryBuilder`.
- Produces: BDN classes emitting per-op mean (ns) + alloc for: ingest-all, uniqueLookup, rangeScan, joinOne, joinMany, multiJoin.

- [ ] **Step 1: Write CoreIngestBenchmarks**

```csharp
namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Configs;
using Prague.Baseline.Scenario;
using Prague.Core;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class CoreIngestBenchmarks {
	private Dataset _data;

	private sealed class Config : ManualConfig {
		public Config() => AddJob(Job.Default
			.WithToolchain(InProcessNoEmitToolchain.Instance)
			.WithWarmupCount(1).WithIterationCount(5));
	}

	[GlobalSetup]
	public void Setup() => _data = DatasetFactory.Build();

	// One op = load the whole dataset into fresh caches (throughput = TotalEntities / mean).
	[Benchmark]
	public int IngestAll() {
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>()
			.Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>()
			.Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();

		for (var i = 0; i < _data.Products.Length; i++) products.AddOrUpdate(_data.Products[i]);
		for (var i = 0; i < _data.Infos.Length; i++) infos.AddOrUpdate(_data.Infos[i]);
		for (var i = 0; i < _data.Offers.Length; i++) offers.AddOrUpdate(_data.Offers[i]);
		return ScenarioSpec.TotalEntities;
	}
}
```

- [ ] **Step 2: Write CoreQueryBenchmarks**

```csharp
namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Configs;
using Prague.Baseline.Scenario;
using Prague.Core;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class CoreQueryBenchmarks {
	private BaselineProductCache _products = null!;
	private BaselineProductInfoCache _infos = null!;
	private BaselineOfferCache _offers = null!;
	private int _cursor;

	private sealed class Config : ManualConfig {
		public Config() => AddJob(Job.Default
			.WithToolchain(InProcessNoEmitToolchain.Instance)
			.WithWarmupCount(1).WithIterationCount(5));
	}

	[GlobalSetup]
	public void Setup() {
		var data = DatasetFactory.Build();
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>()
			.Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>()
			.Build();
		_products = registry.GetCache<BaselineProductCache>();
		_infos = registry.GetCache<BaselineProductInfoCache>();
		_offers = registry.GetCache<BaselineOfferCache>();
		foreach (var p in data.Products) _products.AddOrUpdate(p);
		foreach (var i in data.Infos) _infos.AddOrUpdate(i);
		foreach (var o in data.Offers) _offers.AddOrUpdate(o);
	}

	private int NextId() {
		_cursor = (_cursor + 1) % ScenarioSpec.ProductCount;
		return _cursor + 1;
	}

	[Benchmark] public int UniqueLookup() {
		_products.Cache.TryGet(NextId(), out var _);
		return 1;
	}

	[Benchmark] public int RangeScan() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId())).ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int JoinOne() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinWithBaselineProductInfo().ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int JoinMany() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinMany(_offers.Cache, _offers.ProductIdIndex).ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int MultiJoin() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinWithBaselineProductInfo()
			.JoinMany(_offers.Cache, _offers.ProductIdIndex)
			.ExecutePooled();
		return r.Count;
	}
}
```
> The exact query-chain forms (`WithRange`, `JoinWithBaselineProductInfo`, `.JoinMany(cache.Cache, cache.ProductIdIndex)`, `ExecutePooled()`/`.Count`/`.Dispose()`) are copied from the live `benchmarks/Prague.Benchmarks/ConcurrentReadWriteBenchmark.cs:195-264`. If `.JoinMany(...)` does not chain directly after `JoinWithBaselineProductInfo()`, use the `.Builder.JoinMany(...)` accessor form shown at that file's line 261. Do not guess other overloads.

- [ ] **Step 3: Smoke-run one benchmark**

Run: `dotnet run -c Release --project perf/Prague.Baseline.Bdn --framework net9.0 -- --filter *CoreQueryBenchmarks.UniqueLookup*`
Expected: BDN completes, prints a summary table with Mean + Allocated columns.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Bdn/CoreIngestBenchmarks.cs perf/Prague.Baseline.Bdn/CoreQueryBenchmarks.cs
git commit -m "perf: core-only ingest + query BDN benchmarks"
```

---

### Task 1.3: BDN → common-schema exporter

**Files:**
- Create: `perf/Prague.Baseline.Bdn/BdnResultExport.cs`
- Modify: `perf/Prague.Baseline.Bdn/Program.cs`

**Interfaces:**
- Consumes: BDN's built-in JSON export (`--exporters json` writes `BenchmarkDotNet.Artifacts/results/*-report-brief.json`) OR the in-proc `Summary`.
- Produces: `perf/out/core-only.json` in the `BaselineResult` schema. Metric ids: `ingest.throughput` (from `IngestAll` mean → `TotalEntities / meanSeconds`), `ingest.alloc` (from `IngestAll` allocated / `TotalEntities`), and per query `query.<type>.p50`/`.alloc` (BDN gives mean+alloc; map mean→p50 proxy, and skip p99/p999 for core-only since BDN reports mean/stddev not percentiles — those percentiles live in the harness configs only).

- [ ] **Step 1: Decide the export path (in-proc summary walk)**

BDN's `BenchmarkSwitcher.Run` returns `IEnumerable<Summary>`. Walk it after the run and emit the schema. Rewrite `Program.cs`:
```csharp
namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Running;

internal static class Program {
	private static void Main(string[] args) {
		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		BdnResultExport.Emit(summaries, "perf/out/core-only.json");
	}
}
```

- [ ] **Step 2: Write BdnResultExport**

```csharp
namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Reports;
using Perfolizer.Metrology;
using Prague.Baseline.Scenario;

internal static class BdnResultExport {
	public static void Emit(IEnumerable<Summary> summaries, string outPath) {
		var metrics = new List<Metric>();
		foreach (var summary in summaries) {
			foreach (var report in summary.Reports) {
				if (!report.Success) continue;
				var name = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
				var meanNs = report.ResultStatistics!.Mean; // nanoseconds
				var allocBytes = report.Metrics.TryGetValue("Allocated Memory", out var m) ? m.Value : 0;

				if (name == "IngestAll") {
					var meanSeconds = meanNs / 1_000_000_000.0;
					metrics.Add(new Metric("ingest.throughput", "ent/s",
						ScenarioSpec.TotalEntities / meanSeconds, true));
					metrics.Add(new Metric("ingest.alloc", "bytes",
						allocBytes / (double)ScenarioSpec.TotalEntities, false));
				}
				else {
					var type = MapQueryType(name); // UniqueLookup -> uniqueLookup, etc.
					metrics.Add(new Metric($"query.{type}.p50", "ns", meanNs, false));
					metrics.Add(new Metric($"query.{type}.alloc", "bytes", allocBytes, false));
				}
			}
		}
		var result = new BaselineResult(
			EnvCapture.MachineClass(), "core-only",
			Environment.GetEnvironmentVariable("PRAGUE_PERF_COMMIT") ?? "local",
			DateTime.UtcNow.ToString("O"), EnvCapture.Current(), metrics);
		ResultWriter.Write(outPath, result);
		Console.WriteLine($"[baseline] wrote {outPath} ({metrics.Count} metrics)");
	}

	private static string MapQueryType(string method) => method switch {
		"UniqueLookup" => "uniqueLookup",
		"RangeScan" => "rangeScan",
		"JoinOne" => "joinOne",
		"JoinMany" => "joinMany",
		"MultiJoin" => "multiJoin",
		_ => method,
	};
}
```
> The exact API for pulling allocated bytes (`report.Metrics["Allocated Memory"]`) and mean (`report.ResultStatistics.Mean`) is a BDN 0.15.8 surface. If the metric key differs, run once with `--exporters json`, open `BenchmarkDotNet.Artifacts/results/*-report-brief.json`, and read the exact `Metrics[].Descriptor.Id`/`Value` keys — then match them here. Do NOT invent keys; verify against the emitted JSON.

- [ ] **Step 3: Run the full core suite, confirm JSON**

Run: `dotnet run -c Release --project perf/Prague.Baseline.Bdn --framework net9.0 -- --filter *Core*`
Expected: run completes; `perf/out/core-only.json` exists with `ingest.throughput`, `ingest.alloc`, and `query.*.p50`/`query.*.alloc` metrics.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Bdn/BdnResultExport.cs perf/Prague.Baseline.Bdn/Program.cs
git commit -m "perf: export core-only BDN results to common schema"
```

---

## Phase 2 — Full pipeline harness (port + full-sim)

### Task 2.1: Port the scenario-agnostic harness infrastructure

**Files (create, ported verbatim from the reference repo unless noted):**
- `perf/Prague.Baseline.Harness/Prague.Baseline.Harness.csproj` (new — see below)
- Port from `/Users/dimitarnanov/private/github/nanov/nanov.highperformance/perf-tests/Nanov.HighPerformance.Threading.Workers.PerfTests/`:
  - `IThroughputTest.cs`, `ILatencyTest.cs`
  - `ThroughputSessionContext.cs`, `ThroughputTestSessionResult.cs`, `ThroughputTestSession.cs`
  - `LatencySessionContext.cs`, `LatencyTestSessionResult.cs`, `LatencyTestSession.cs`
  - `PerfTestType.cs`, `PerfTestTypeSelector.cs`, `ProgramOptions.cs`, `Program.cs`
  - `Support/StopwatchUtil.cs`, `Support/ComputerSpecifications.cs`, `Support/PerfTestUtil.cs`

**Port transforms (apply to every ported file):**
1. Change namespace `Nanov.HighPerformance.Threading.Workers.PerfTests[.Support]` → `Prague.Baseline.Harness[.Support]`.
2. Change `internal sealed`/`internal` on `ThroughputTestSessionResult`, `LatencyTestSessionResult` → `public sealed` (results are read by `Program`/emit code in this same assembly; keep `public` for clarity).
3. Delete Workers-specific bits: `SetBatchData`/`BatchPercent`/`AverageBatchSize` on `ThroughputSessionContext` and the batch columns on `ThroughputTestSessionResult` (drop the batch ctor params; keep the `(long totalOperationsInRun, TimeSpan duration, int gen0, int gen1, int gen2)` ctor and the `(Exception)` ctor). Drop `PerfTestUtil.AccumulatedAddition`.
4. Keep verbatim: the `OpsPerSecond => TotalOperationsInRun / Duration.TotalSeconds` formula; `LatencyTestSessionResult.P(double) => Histogram?.GetValueAtPercentile(percentile) ?? 0`; `LatencySessionContext.Histogram = new LongHistogram(10_000_000_000L, 4)`; `StopwatchUtil` tick→ns conversion.
5. In `Program.cs`: keep the `PerfTestTypeSelector` discovery + `RunTestForType` session dispatch; the emit hook is added in Task 2.4.

- [ ] **Step 1: Write the csproj**

`perf/Prague.Baseline.Harness/Prague.Baseline.Harness.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <TieredCompilation>false</TieredCompilation>
    <TieredPGO>false</TieredPGO>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <NoWarn>$(NoWarn);CS8618;CS8602;CS8600;CS8604;CS8625;CS8601;CS8603;CS1061</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HdrHistogram" Version="2.5.0" />
    <PackageReference Include="Testcontainers.Kafka" Version="4.11.0" />
    <PackageReference Include="Confluent.Kafka" Version="2.11.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Prague.Baseline.Scenario\Prague.Baseline.Scenario.csproj" />
    <ProjectReference Include="..\..\src\Prague.Core\Prague.Core.csproj" />
    <ProjectReference Include="..\..\src\Prague.Kafka\Prague.Kafka.csproj" />
    <ProjectReference Include="..\..\src\Prague.Attributes\Prague.Attributes.csproj" />
    <ProjectReference Include="..\..\src\Prague.Codegen\Prague.Codegen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```
> Verify exact package versions against the repo's `Directory.Build.props` floats and `tests/Prague.Kafka.IntegrationTests/Prague.Kafka.IntegrationTests.csproj` (Testcontainers.Kafka 4.11.0 confirmed there). For `HdrHistogram`, use the latest 2.x on nuget.org; if `2.5.0` is not the current version, run `dotnet add perf/Prague.Baseline.Harness package HdrHistogram` and accept the resolved version. `Confluent.Kafka` / `Microsoft.Extensions.Hosting` versions should match what `src/Prague.Kafka` transitively resolves — check with `dotnet list src/Prague.Kafka/Prague.Kafka.csproj package`.

- [ ] **Step 2: Copy the infra files with the transforms above**

Read each source file in full and write the Prague equivalent applying transforms 1–5. Example — `Support/StopwatchUtil.cs` ported verbatim except namespace:
```csharp
namespace Prague.Baseline.Harness.Support;

using System.Diagnostics;

public static class StopwatchUtil {
	private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
	public static long GetTimestamp() => Stopwatch.GetTimestamp();
	public static long ToNanoseconds(long ticks) => (long)(ticks * NanosecondsPerTick);
	public static long GetTimestampFromNanoseconds(long ns) => (long)(ns / NanosecondsPerTick);
}
```
(Read the source for the exact remaining methods; port them 1:1.)

- [ ] **Step 3: Build (tests not yet present — Program will list zero tests)**

Run: `dotnet sln Prague.sln add perf/Prague.Baseline.Harness/Prague.Baseline.Harness.csproj && dotnet build perf/Prague.Baseline.Harness -c Release`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Harness Prague.sln
git commit -m "perf: port throughput/latency harness infrastructure"
```

---

### Task 2.2: full-sim ingest pipeline

**Files:**
- Create: `perf/Prague.Baseline.Harness/Sim/CacheApplyWorker.cs`
- Create: `perf/Prague.Baseline.Harness/Sim/SimIngestPipeline.cs`

**Interfaces:**
- Consumes: `EncodedSet` from `Payloads.Encode`, generated caches, `CacheSerde<T>.DeserializeFromSpan`, `{Entity}.GetEnricher()`, `AsyncValueBufferedWorker<T>`, `default(RawHeaders)`, `Confluent.Kafka.Timestamp`.
- Produces: `SimIngestPipeline.IngestAll(EncodedSet, caches...) -> long` returning entities applied; and a per-entity latency variant `IngestSampled(..., LongHistogram)`.

- [ ] **Step 1: Write CacheApplyWorker (generic, applies AddOrUpdate)**

Model exactly on `benchmarks/Prague.Benchmarks/KafkaLiveDispatchBenchmarks.cs` `NoOpWorker` (lines 127-135) but apply to a cache:
```csharp
namespace Prague.Baseline.Harness.Sim;

using Prague.Core;
using Prague.Kafka.Internal;

internal struct Slot<T> where T : class { public T Value; public long TimestampMs; }

internal sealed class CacheApplyWorker<TCache, TKey, TValue> : AsyncValueBufferedWorker<Slot<TValue>>
	where TCache : class, IDataCache<TCache, TKey, TValue>
	where TValue : class {
	private readonly TCache _cache;
	public CacheApplyWorker(TCache cache, int capacity) : base(capacity, "BaselineSimWorker") => _cache = cache;

	protected override ValueTask ProcessAsync(ref ConsumeScope<Slot<TValue>> scope, CancellationToken ct) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		_cache.Cache.AddOrUpdate(/* key */ default!, value, ts); // see note
		return default;
	}
}
```
> The exact `AddOrUpdate` overload: `IDataCache<TKey,TValue>` exposes `AddOrUpdate(TValue, long timestamp)` (keys off the entity's `[DataCacheKey]`), so prefer calling the generated cache's `AddOrUpdate(TValue, long)` — change the field type to the concrete generated cache and call `_cache.AddOrUpdate(value, ts)` (the generated method reads the key from the entity, per `src/Prague.Codegen/CacheGenerator.cs:7259+`). Drop the `default!` key form. If a generic constraint fight ensues, make three concrete non-generic workers (`ProductApplyWorker`, `InfoApplyWorker`, `OfferApplyWorker`) — simpler and monomorphic; that is the recommended fallback.

- [ ] **Step 2: Write SimIngestPipeline**

Structure ingest as three typed sub-phases (products, infos, offers), each: pin the encoded value bytes in native memory (mirrors librdkafka), deserialize zero-copy, enrich, publish to that type's worker; drain; sum ops. Model the pin+deserialize+enrich+publish exactly on `KafkaLiveDispatchBenchmarks.RawWorkerPath()`:
```csharp
namespace Prague.Baseline.Harness.Sim;

using System.Runtime.InteropServices;
using Confluent.Kafka;
using Prague.Baseline.Scenario;
using Prague.Kafka.Internal;
using Prague.Kafka.SerDe;

internal static class SimIngestPipeline {
	public static unsafe long IngestAll(
		EncodedSet enc,
		BaselineProductCache products, BaselineProductInfoCache infos, BaselineOfferCache offers) {
		long ops = 0;
		ops += Drive(enc.Products, products, static (c, v, ts) => c.AddOrUpdate(v, ts),
			static s => CacheSerde<BaselineProduct>.DeserializeFromSpan(s),
			static (v, ts) => BaselineProduct.GetEnricher().Enrich(v, default(RawHeaders), ts));
		// ...same for infos, offers (see note)...
		return ops;
	}
}
```
> This sketch uses delegates for brevity, but on the measured path delegates allocate/deopt — write THREE explicit typed loops instead (no lambdas), each mirroring `RawWorkerPath()`: `var span = new ReadOnlySpan<byte>(nativePtr, len); var v = CacheSerde<T>.DeserializeFromSpan(span); {T}.GetEnricher().Enrich(v, default(RawHeaders), new Timestamp(baseMs + i, TimestampType.CreateTime)); using var scope = worker.Publish(); if (scope.IsOpen) { ref var slot = ref scope.Event(); slot.Value = v; slot.TimestampMs = v enrichment ts; }`. Cache the `GetEnricher()` result in a local before the loop. Pin each payload once via `NativeMemory.Alloc`/`CopyTo` before the loop (or pin the whole encoded array). Free native memory in a `finally`. After publishing all items for a type, call `worker.TryComplete(null); worker.Completion.GetAwaiter().GetResult();` to drain before timing stop. Total ops = `ScenarioSpec.TotalEntities`.

- [ ] **Step 3: Build**

Run: `dotnet build perf/Prague.Baseline.Harness -c Release`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Harness/Sim
git commit -m "perf: full-sim ingest pipeline (deserialize->enrich->worker->cache)"
```

---

### Task 2.3: Ingest throughput + query latency tests (core-only + full-sim)

**Files:**
- Create: `perf/Prague.Baseline.Harness/Tests/CoreIngestThroughputTest.cs`
- Create: `perf/Prague.Baseline.Harness/Tests/SimIngestThroughputTest.cs`
- Create: `perf/Prague.Baseline.Harness/Tests/QueryMixLatencyTest.cs`

**Interfaces:**
- Consumes: `IThroughputTest`/`ILatencyTest` (ported), `ThroughputSessionContext`/`LatencySessionContext`, `SimIngestPipeline`, `DatasetFactory`, `Payloads`, `StopwatchUtil`.
- Produces: concrete tests discoverable by `PerfTestTypeSelector`. Each throughput test's `Run(ctx)` returns total ops; the latency test records per-query ns into `ctx.Histogram`.

- [ ] **Step 1: SimIngestThroughputTest**

```csharp
namespace Prague.Baseline.Harness.Tests;

using Prague.Baseline.Harness.Sim;
using Prague.Baseline.Scenario;
using Prague.Core;

public sealed class SimIngestThroughputTest : IThroughputTest {
	public int RequiredProcessorCount => 2;

	public long Run(ThroughputSessionContext ctx) {
		var enc = Payloads.Encode(DatasetFactory.Build());
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>().Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>().Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();

		ctx.Start();
		var ops = SimIngestPipeline.IngestAll(enc, products, infos, offers);
		ctx.Stop();
		return ops;
	}
}
```

- [ ] **Step 2: CoreIngestThroughputTest** — identical shape but calls `products.AddOrUpdate(...)` directly in three loops (no Kafka), returning `ScenarioSpec.TotalEntities`. (Copy the body from `CoreIngestBenchmarks.IngestAll`, wrapped in `ctx.Start()/ctx.Stop()`.)

- [ ] **Step 3: QueryMixLatencyTest**

```csharp
namespace Prague.Baseline.Harness.Tests;

using HdrHistogram;
using Prague.Baseline.Scenario;
using Prague.Baseline.Harness.Support;
using Prague.Core;

public sealed class QueryMixLatencyTest : ILatencyTest {
	public int RequiredProcessorCount => 1;

	public void Run(LatencySessionContext ctx) {
		var data = DatasetFactory.Build();
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>().Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>().Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();
		foreach (var p in data.Products) products.AddOrUpdate(p);
		foreach (var i in data.Infos) infos.AddOrUpdate(i);
		foreach (var o in data.Offers) offers.AddOrUpdate(o);

		const int iterations = 200_000;
		ctx.Start();
		for (var i = 0; i < iterations; i++) {
			var id = (i % ScenarioSpec.ProductCount) + 1;
			var t0 = StopwatchUtil.GetTimestamp();
			using var r = products.Query().WithRange(q => q.Gte(id))
				.JoinWithBaselineProductInfo()
				.JoinMany(offers.Cache, offers.ProductIdIndex)
				.ExecutePooled();
			_ = r.Count;
			var ns = StopwatchUtil.ToNanoseconds(StopwatchUtil.GetTimestamp() - t0);
			ctx.Histogram.RecordValue(ns);
		}
		ctx.Stop();
	}
}
```
> This records the `multiJoin` query type (the heaviest, most regression-sensitive). Add sibling latency tests per query type only if the compare surface needs per-type p99/p999 for the harness configs (the schema vocabulary allows `query.<type>.p50|p99|p999`). For the first cut, `multiJoin` is the tracked harness latency metric; core-only BDN already covers the lighter query types' p50+alloc.

- [ ] **Step 4: Build + discovery smoke**

Run: `dotnet run -c Release --project perf/Prague.Baseline.Harness --framework net9.0 -- --target SimIngestThroughputTest --runs 2`
Expected: harness runs the sim ingest twice, prints per-run `Mops`/`ops/s` lines.

- [ ] **Step 5: Commit**

```bash
git add perf/Prague.Baseline.Harness/Tests
git commit -m "perf: core-only + full-sim ingest throughput + query latency tests"
```

---

### Task 2.4: Harness → common-schema emit + config selection

**Files:**
- Modify: `perf/Prague.Baseline.Harness/ProgramOptions.cs` (add `--config` + `--out`)
- Modify: `perf/Prague.Baseline.Harness/Program.cs` (emit results)
- Create: `perf/Prague.Baseline.Harness/HarnessResultExport.cs`

**Interfaces:**
- Consumes: `ThroughputTestSessionResult` (median `OpsPerSecond`), `LatencyTestSessionResult` (`P(50)`, `P(99)`, `P(99.9)`), `ResultWriter`.
- Produces: `perf/out/<config>.json` in the `BaselineResult` schema, with `config` ∈ `{full-sim, full-real, core-only}` (harness form).

- [ ] **Step 1: Add options**

In `ProgramOptions.cs` add `public string Config { get; set; } = "full-sim";` and `public string OutPath { get; set; } = "perf/out";`, parsed from `--config <name>` and `--out <dir>` in `TryParse`. Add `--config` to help text.

- [ ] **Step 2: Write HarnessResultExport**

```csharp
namespace Prague.Baseline.Harness;

using Prague.Baseline.Scenario;

internal static class HarnessResultExport {
	public static void Emit(string config, string outDir,
		double ingestOpsPerSecMedian, long p50Ns, long p99Ns, long p999Ns) {
		var metrics = new List<Metric> {
			new("ingest.throughput", "ent/s", ingestOpsPerSecMedian, true),
			new("query.multiJoin.p50", "ns", p50Ns, false),
			new("query.multiJoin.p99", "ns", p99Ns, false),
			new("query.multiJoin.p999", "ns", p999Ns, false),
		};
		var result = new BaselineResult(
			EnvCapture.MachineClass(), config,
			Environment.GetEnvironmentVariable("PRAGUE_PERF_COMMIT") ?? "local",
			DateTime.UtcNow.ToString("O"), EnvCapture.Current(), metrics);
		ResultWriter.Write(Path.Combine(outDir, config + ".json"), result);
		Console.WriteLine($"[baseline] wrote {config}.json");
	}
}
```

- [ ] **Step 3: Wire emit into the session runners**

In `Program.RunTestForType`, after a `ThroughputTestSession`/`LatencyTestSession` completes, collect the per-run results, take the MEDIAN `OpsPerSecond` across runs (throughput) and read `P(50)/P(99)/P(99.9)` from the last/median latency result, then call `HarnessResultExport.Emit(options.Config, options.OutPath, medianOps, p50, p99, p999)`. Selection of WHICH tests run for a `--config` is by naming convention: `--config full-sim` runs `SimIngestThroughputTest` + `QueryMixLatencyTest`; `--config core-only` runs `CoreIngestThroughputTest` + `QueryMixLatencyTest`; `--config full-real` runs `RealIngestThroughputTest` + `QueryMixLatencyTest` (Task 3). Map config→test-name set in `Program`.

- [ ] **Step 4: Run full-sim end-to-end, confirm JSON**

Run: `dotnet run -c Release --project perf/Prague.Baseline.Harness --framework net9.0 -- --config full-sim --runs 3`
Expected: `perf/out/full-sim.json` with `ingest.throughput` + `query.multiJoin.p50/p99/p999`.

- [ ] **Step 5: Commit**

```bash
git add perf/Prague.Baseline.Harness/ProgramOptions.cs perf/Prague.Baseline.Harness/Program.cs perf/Prague.Baseline.Harness/HarnessResultExport.cs
git commit -m "perf: harness config selection + common-schema emit"
```

---

## Phase 3 — full-real (Testcontainers, opt-in)

### Task 3.1: Testcontainers broker ingest test

**Files:**
- Create: `perf/Prague.Baseline.Harness/Real/RealIngestPipeline.cs`
- Create: `perf/Prague.Baseline.Harness/Tests/RealIngestThroughputTest.cs`

**Interfaces:**
- Consumes: `Testcontainers.Kafka` (`KafkaBuilder`), `Confluent.Kafka` (`AdminClientBuilder`, `ProducerBuilder<byte[],byte[]>`), Prague DI (`AddKafkaCaches`), `Payloads`, `IHostedService`, `KafkaCachesLoader`.
- Produces: `RealIngestThroughputTest : IThroughputTest` that starts a broker, produces all payloads, wires the Prague consumer, waits until the cache is fully loaded, and times the load.

- [ ] **Step 1: Write RealIngestPipeline**

Model exactly on `tests/Prague.Kafka.IntegrationTests/Fixtures/DualKafkaClusterFixture.cs` + `SelfConsumeTests.cs`:
```csharp
namespace Prague.Baseline.Harness.Real;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prague.Baseline.Scenario;
using Prague.Kafka;
using Testcontainers.Kafka;

internal sealed class RealIngestPipeline : IAsyncDisposable {
	private KafkaContainer _broker = null!;
	private ServiceProvider _sp = null!;

	public async Task<string> StartBrokerAndProduceAsync(EncodedSet enc) {
		_broker = new KafkaBuilder().WithImage("confluentinc/cp-kafka:7.5.0").Build();
		await _broker.StartAsync();
		var bootstrap = _broker.GetBootstrapAddress();

		await CreateTopicAsync(bootstrap, "baseline-products");
		await CreateTopicAsync(bootstrap, "baseline-product-infos");
		await CreateTopicAsync(bootstrap, "baseline-offers");

		using var producer = new ProducerBuilder<byte[], byte[]>(
			new ProducerConfig { BootstrapServers = bootstrap, Acks = Acks.All }).Build();
		Produce(producer, "baseline-products", enc.Products);
		Produce(producer, "baseline-product-infos", enc.Infos);
		Produce(producer, "baseline-offers", enc.Offers);
		producer.Flush(TimeSpan.FromSeconds(30));
		return bootstrap;
	}

	private static void Produce(IProducer<byte[], byte[]> p, string topic, EncodedEntity[] items) {
		foreach (var e in items)
			p.Produce(topic, new Message<byte[], byte[]> { Key = e.Key, Value = e.Value, Headers = new Headers() });
	}

	private static async Task CreateTopicAsync(string bootstrap, string name) {
		using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
		try {
			await admin.CreateTopicsAsync(new[] {
				new TopicSpecification { Name = name, NumPartitions = 1, ReplicationFactor = 1 } });
		} catch (CreateTopicsException) { /* already exists */ }
	}

	public async Task<(BaselineProductCache, BaselineProductInfoCache, BaselineOfferCache)>
		StartConsumerAsync(string bootstrap, CancellationToken ct) {
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
			["KafkaConfig:BootstrapServers"] = bootstrap,
		}).Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IConfiguration>(config);
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<BaselineProductCache, int, BaselineProduct>("baseline-products");
			b.AddCache<BaselineProductInfoCache, int, BaselineProductInfo>("baseline-product-infos");
			b.AddCache<BaselineOfferCache, int, BaselineOffer>("baseline-offers");
		});
		_sp = services.BuildServiceProvider();
		await _sp.GetRequiredService<IHostedService>().StartAsync(ct);
		await _sp.GetRequiredService<KafkaCachesLoader>().StartAsync(ct);
		return (_sp.GetRequiredService<BaselineProductCache>(),
			_sp.GetRequiredService<BaselineProductInfoCache>(),
			_sp.GetRequiredService<BaselineOfferCache>());
	}

	public async ValueTask DisposeAsync() {
		if (_sp is not null) await _sp.DisposeAsync();
		if (_broker is not null) await _broker.DisposeAsync();
	}
}
```
> The exact `AddCache<TCache,TKey,TValue>(topic)` signature + constraints, `KafkaCachesLoader.StartAsync`, and the in-memory `KafkaConfig:BootstrapServers` config key are confirmed in `src/Prague.Kafka/DependencyInjection.cs` and `tests/Prague.Kafka.IntegrationTests/SelfConsumeTests.cs`. Do NOT add the `X-Producer-Id` header — a plain `ProducerBuilder` publish omits it, so the Prague consumer will ingest (it self-filters only its own instance id).

- [ ] **Step 2: Write RealIngestThroughputTest**

`Run(ctx)`: build dataset+payloads, `StartBrokerAndProduceAsync`, then `ctx.Start()`, `StartConsumerAsync`, `PerfTestUtil.SpinUntil(() => products.Query().Count() == 500 && infos...==500 && offers...==10000)` (poll the cache counts until fully loaded), `ctx.Stop()`, dispose. Return `ScenarioSpec.TotalEntities`. Run everything via `.GetAwaiter().GetResult()` since `IThroughputTest.Run` is synchronous. `RequiredProcessorCount => 2`.

- [ ] **Step 3: Build**

Run: `dotnet build perf/Prague.Baseline.Harness -c Release`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add perf/Prague.Baseline.Harness/Real perf/Prague.Baseline.Harness/Tests/RealIngestThroughputTest.cs
git commit -m "perf: full-real Testcontainers broker ingest test"
```

---

### Task 3.2: full-real smoke (requires Docker)

- [ ] **Step 1: Run once against a live broker**

Run: `dotnet run -c Release --project perf/Prague.Baseline.Harness --framework net9.0 -- --config full-real --runs 1`
Expected: broker starts, payloads produced, consumer loads 11,000 entities, `perf/out/full-real.json` written. (Skip if Docker unavailable — this config is opt-in.)

- [ ] **Step 2: Commit any fixups**

```bash
git commit -am "perf: full-real smoke fixups" --allow-empty
```

---

## Phase 4 — Compare tooling + baselines

### Task 4.1: thresholds + compare.py

**Files:**
- Create: `perf/thresholds.json`
- Create: `perf/compare.py`

**Interfaces:**
- Consumes: `perf/out/<config>.json` (current), `perf/baseline/<machine-class>.json` (blessed), `perf/thresholds.json`.
- Produces: table on stdout with `%Δ` per metric; exit code 1 if any metric regresses beyond tolerance; `--bless` mode rewrites the baseline; regenerates `perf/BASELINE.md`.

- [ ] **Step 1: thresholds.json**

```json
{
  "default": 0.08,
  "byPrefix": {
    "ingest.throughput": 0.10,
    "read.throughput": 0.10,
    "query.": 0.08,
    "ingest.alloc": 0.02,
    "query.*.alloc": 0.02
  },
  "bySuffix": {
    ".p999": 0.15,
    ".alloc": 0.02
  }
}
```
> `bySuffix` wins over `byPrefix` wins over `default` (most-specific match). `.alloc` → ±2%, `.p999` → ±15%, throughput → ±10%, other latency → ±8%.

- [ ] **Step 2: compare.py**

```python
#!/usr/bin/env python3
import json, sys, argparse, datetime, glob, os

def tolerance(mid, thr):
    for suf, t in thr.get("bySuffix", {}).items():
        if mid.endswith(suf): return t
    best = None
    for pre, t in thr.get("byPrefix", {}).items():
        if mid.startswith(pre.rstrip("*")): best = t
    return best if best is not None else thr.get("default", 0.08)

def load(path):
    with open(path) as f: return json.load(f)

def regressed(mid, cur, base, higher_is_better, tol):
    if base == 0: return False, 0.0
    delta = (cur - base) / base
    signed = delta if higher_is_better else -delta   # positive = improvement
    return (signed < -tol), delta

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--machine", required=True)
    ap.add_argument("--configs", nargs="+", required=True)
    ap.add_argument("--out-dir", default="perf/out")
    ap.add_argument("--baseline-dir", default="perf/baseline")
    ap.add_argument("--thresholds", default="perf/thresholds.json")
    ap.add_argument("--bless", action="store_true")
    args = ap.parse_args()

    thr = load(args.thresholds)
    base_path = os.path.join(args.baseline_dir, args.machine + ".json")
    baseline = load(base_path) if os.path.exists(base_path) else {"configs": {}}
    baseline.setdefault("configs", {})

    any_regression = False
    for config in args.configs:
        cur = load(os.path.join(args.out_dir, config + ".json"))
        cur_metrics = {m["id"]: m for m in cur["metrics"]}
        if args.bless:
            baseline["configs"][config] = {
                "env": cur["env"], "commit": cur["commit"], "timestampUtc": cur["timestampUtc"],
                "metrics": cur["metrics"],
            }
            print(f"[bless] {config}: {len(cur['metrics'])} metrics")
            continue
        base_metrics = {m["id"]: m for m in baseline["configs"].get(config, {}).get("metrics", [])}
        print(f"\n== {config} ({args.machine}) ==")
        print(f"{'metric':32} {'baseline':>14} {'current':>14} {'delta':>9} {'tol':>6}  status")
        for mid, m in sorted(cur_metrics.items()):
            b = base_metrics.get(mid)
            if b is None:
                print(f"{mid:32} {'-':>14} {m['value']:>14.2f} {'NEW':>9}")
                continue
            tol = tolerance(mid, thr)
            reg, delta = regressed(mid, m["value"], b["value"], m["higherIsBetter"], tol)
            status = "REGRESSED" if reg else "ok"
            if reg: any_regression = True
            print(f"{mid:32} {b['value']:>14.2f} {m['value']:>14.2f} {delta*100:>8.1f}% {tol*100:>5.0f}% {status}")

    if args.bless:
        with open(base_path, "w") as f: json.dump(baseline, f, indent=2)
        write_rollup(args.baseline_dir)
        print(f"[bless] wrote {base_path}")
        return 0
    return 1 if any_regression else 0

def write_rollup(baseline_dir):
    lines = ["# Prague performance baseline\n",
             f"_Generated {datetime.datetime.utcnow().isoformat()}Z_\n"]
    for path in sorted(glob.glob(os.path.join(baseline_dir, "*.json"))):
        data = load(path)
        machine = os.path.splitext(os.path.basename(path))[0]
        lines.append(f"\n## {machine}\n")
        for config, block in sorted(data.get("configs", {}).items()):
            env = block.get("env", {})
            lines.append(f"\n### {config}  \n`{env.get('cpu','?')}` · `{env.get('os','?')}` · "
                         f"`{env.get('dotnet','?')}` · commit `{block.get('commit','?')}`\n")
            lines.append("| metric | value | unit |\n|---|---:|---|\n")
            for m in block.get("metrics", []):
                lines.append(f"| {m['id']} | {m['value']:.2f} | {m['unit']} |\n")
    with open("perf/BASELINE.md", "w") as f: f.writelines(lines)

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Unit-test the tolerance logic**

Run:
```bash
python3 - <<'PY'
import importlib.util, sys
spec = importlib.util.spec_from_file_location("cmp", "perf/compare.py")
cmp = importlib.util.module_from_spec(spec); spec.loader.exec_module(cmp)
thr = {"default":0.08,"byPrefix":{"ingest.throughput":0.10},"bySuffix":{".p999":0.15,".alloc":0.02}}
assert cmp.tolerance("ingest.throughput", thr) == 0.10
assert cmp.tolerance("query.multiJoin.p999", thr) == 0.15
assert cmp.tolerance("query.joinMany.alloc", thr) == 0.02
assert cmp.tolerance("query.multiJoin.p99", thr) == 0.08
# throughput drop beyond tol regresses; improvement does not
assert cmp.regressed("ingest.throughput", 90, 100, True, 0.05)[0] is True
assert cmp.regressed("ingest.throughput", 110, 100, True, 0.05)[0] is False
# latency rise beyond tol regresses
assert cmp.regressed("query.x.p99", 110, 100, False, 0.05)[0] is True
print("compare.py logic OK")
PY
```
Expected: `compare.py logic OK`.

- [ ] **Step 4: Commit**

```bash
git add perf/thresholds.json perf/compare.py
git commit -m "perf: thresholds + compare.py auto-diff + rollup generator"
```

---

### Task 4.2: run.sh entrypoint

**Files:**
- Create: `perf/run.sh` (chmod +x)

**Interfaces:**
- `perf/run.sh core|sim|real|all [--bless] [--machine <class>]` — builds Release, runs the config(s), then invokes `compare.py`.

- [ ] **Step 1: Write run.sh**

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."          # repo root
TARGET="${1:-all}"; shift || true
BLESS=""; MACHINE="${PRAGUE_PERF_MACHINE:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    --bless) BLESS="--bless" ;;
    --machine) MACHINE="$2"; shift ;;
  esac; shift
done
export PRAGUE_PERF_MACHINE="${MACHINE:?set --machine or PRAGUE_PERF_MACHINE}"
export PRAGUE_PERF_COMMIT="$(git rev-parse --short HEAD)"
TFM=net9.0
CONFIGS=()

run_core() { dotnet run -c Release --project perf/Prague.Baseline.Bdn --framework $TFM -- --filter '*Core*'; CONFIGS+=(core-only); }
run_sim()  { dotnet run -c Release --project perf/Prague.Baseline.Harness --framework $TFM -- --config full-sim --runs 3; CONFIGS+=(full-sim); }
run_real() { dotnet run -c Release --project perf/Prague.Baseline.Harness --framework $TFM -- --config full-real --runs 3; CONFIGS+=(full-real); }

case "$TARGET" in
  core) run_core ;;
  sim)  run_sim ;;
  real) run_real ;;
  all)  run_core; run_sim ;;   # 'all' excludes real (opt-in); use 'real' explicitly
  *) echo "usage: run.sh core|sim|real|all [--bless] [--machine <class>]"; exit 2 ;;
esac

python3 perf/compare.py --machine "$PRAGUE_PERF_MACHINE" --configs "${CONFIGS[@]}" $BLESS
```

- [ ] **Step 2: chmod + syntax check**

```bash
chmod +x perf/run.sh && bash -n perf/run.sh && echo "run.sh OK"
```
Expected: `run.sh OK`.

- [ ] **Step 3: Commit**

```bash
git add perf/run.sh
git commit -m "perf: run.sh entrypoint (build+run+compare)"
```

---

### Task 4.3: Seed the local baseline

**Files:**
- Create: `perf/baseline/apple-m4pro-darwin.json` (generated via `--bless`)
- Create: `perf/BASELINE.md` (generated)

- [ ] **Step 1: Run core+sim and bless on the M4 host**

```bash
PRAGUE_PERF_CPU="Apple M4 Pro" perf/run.sh all --bless --machine apple-m4pro-darwin
```
Expected: `perf/out/core-only.json` + `perf/out/full-sim.json` produced; `perf/baseline/apple-m4pro-darwin.json` written with both configs; `perf/BASELINE.md` regenerated.

- [ ] **Step 2: Re-run without bless → confirm ~0% deltas, exit 0**

```bash
PRAGUE_PERF_CPU="Apple M4 Pro" perf/run.sh all --machine apple-m4pro-darwin; echo "exit=$?"
```
Expected: table shows all metrics within tolerance, `exit=0`.

- [ ] **Step 3: Add out/ to gitignore, commit baseline + rollup**

Append `perf/out/` to the repo `.gitignore`. Then:
```bash
git add .gitignore perf/baseline/apple-m4pro-darwin.json perf/BASELINE.md
git commit -m "perf: seed apple-m4pro-darwin baseline + rollup"
```

---

## Phase 5 — CI + tripwire proof

### Task 5.1: CI workflow (core + sim)

**Files:**
- Create: `.github/workflows/perf-baseline.yml`

**Interfaces:**
- Runs `core` + `sim` on a fixed runner, compares against the committed CI-class baseline, fails on regression.

- [ ] **Step 1: Write the workflow**

```yaml
name: perf-baseline
on:
  pull_request:
  workflow_dispatch:
jobs:
  tripwire:
    runs-on: ubuntu-latest
    env:
      PRAGUE_PERF_MACHINE: linux-x64-ci
      PRAGUE_PERF_CPU: github-ubuntu
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            9.0.x
            10.0.x
      - name: Build
        run: dotnet build perf/Prague.Baseline.Bdn perf/Prague.Baseline.Harness -c Release
      - name: Run core + sim tripwire
        run: perf/run.sh all --machine linux-x64-ci
```
> On the FIRST run there is no `perf/baseline/linux-x64-ci.json`; `compare.py` reports every metric as `NEW` and exits 0 (no regression). Seed it by running once with `--bless` on the runner class (a maintainer runs the `workflow_dispatch` variant that appends `--bless`, or commits a baseline captured from a representative run). Document this in `perf/BASELINE.md`.

- [ ] **Step 2: Validate YAML locally**

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/perf-baseline.yml')); print('yaml OK')"
```
Expected: `yaml OK`. (If PyYAML absent, `pip install pyyaml` or skip — GitHub validates on push.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/perf-baseline.yml
git commit -m "perf: CI tripwire (core + sim) on linux-x64-ci"
```

---

### Task 5.2: Prove the tripwire fires

**Files:** (temporary edit, reverted)

- [ ] **Step 1: Inject a deliberate slowdown**

Temporarily add to `QueryMixLatencyTest.Run`, inside the loop after `_ = r.Count;`:
```csharp
System.Threading.Thread.SpinWait(2000); // TEMP regression probe
```

- [ ] **Step 2: Re-run sim and confirm non-zero exit**

```bash
PRAGUE_PERF_CPU="Apple M4 Pro" perf/run.sh sim --machine apple-m4pro-darwin; echo "exit=$?"
```
Expected: `query.multiJoin.*` shows a large positive delta, status `REGRESSED`, `exit=1`.

- [ ] **Step 3: Revert the probe**

Remove the `SpinWait` line. Re-run:
```bash
PRAGUE_PERF_CPU="Apple M4 Pro" perf/run.sh sim --machine apple-m4pro-darwin; echo "exit=$?"
```
Expected: `exit=0`.

- [ ] **Step 4: Commit (probe already reverted — no code change to commit; verifies behavior only)**

No commit needed; this task is a verification gate.

---

## Phase 6 — Docs + final verification

### Task 6.1: perf/README + CLAUDE.md context pointer

**Files:**
- Create: `perf/README.md`
- Modify: `CLAUDE.md` (add a one-line pointer under the context map)

- [ ] **Step 1: Write perf/README.md** — how to run each config, what each metric means, how/when to re-bless, the machine-class convention, and the full-sim boundary note.

- [ ] **Step 2: Add pointer to CLAUDE.md**

Under the "Architecture" tree or context map, add:
```
perf/         Regression-tripwire baseline: core-only (BDN) + full-sim/full-real (harness),
              compare.py auto-diff vs perf/baseline/<machine-class>.json. See perf/README.md.
```

- [ ] **Step 3: Commit**

```bash
git add perf/README.md CLAUDE.md
git commit -m "perf: document baseline usage"
```

---

### Task 6.2: Full-suite regression check

- [ ] **Step 1: Ensure the whole solution still builds**

Run: `dotnet build Prague.sln -c Release`
Expected: success (new projects + modified csprojs compile).

- [ ] **Step 2: Run the affected unit tests**

Run: `dotnet test tests/Prague.Core.Tests --filter "BaselineDatasetTests|BaselineResultSchemaTests" && dotnet test tests/Prague.Kafka.Tests --filter BaselinePayloadTests`
Expected: all PASS.

- [ ] **Step 3: Final end-to-end tripwire**

Run: `PRAGUE_PERF_CPU="Apple M4 Pro" perf/run.sh all --machine apple-m4pro-darwin; echo "exit=$?"`
Expected: `exit=0`, table within tolerance.

- [ ] **Step 4: Commit any final fixups**

```bash
git commit -am "perf: final baseline verification" --allow-empty
```

---

## Self-Review

**Spec coverage:**
- Role = regression tripwire → Phase 4 (`compare.py` exit codes) + Task 5.2 (proof). ✓
- 3 configs (core-only/full-sim/full-real) → Phases 1 / 2 / 3. ✓
- Hybrid engine (BDN core-only + harness full) → Phase 1 (BDN) + Phase 2 (harness). ✓
- Two-phase scenario (ingest throughput + read/join latency) → Tasks 1.2, 2.3, 2.4 metrics. ✓
- Machine-readable baseline + human rollup → Task 4.1 (`compare.py` + `BASELINE.md`). ✓
- Dedicated `perf/` tree, shared scenario lib → Phase 0. ✓
- Tolerances (±10% throughput, ±8% latency, ±15% p999, ±2% alloc), median-of-3, manual re-bless → Task 4.1 `thresholds.json` + `--bless`. ✓
- CI core+sim, real opt-in → Task 5.1 + `run.sh` `all` excluding real. ✓
- InternalsVisibleTo name-gating → Task 0.1. ✓
- full-sim boundary honesty → Global Constraints + Task 2.2. ✓

**Placeholder scan:** every code step carries real code; port steps name exact source paths + transforms; verification steps have exact commands + expected output. Remaining judgment calls (BDN metric key names, exact `AddOrUpdate` overload, HdrHistogram/Confluent package versions) are flagged with a "verify against <file>" instruction and a concrete fallback — not left blank.

**Type consistency:** `BaselineResult`/`Metric`/`EnvBlock`/`ResultWriter`/`EnvCapture` (Task 0.4) are consumed unchanged by `BdnResultExport` (1.3) and `HarnessResultExport` (2.4). Metric ids match the Global Constraints vocabulary. `DatasetFactory.Build()→Dataset` (0.3) and `Payloads.Encode→EncodedSet` (0.5) signatures are used verbatim downstream. Generated cache names (`BaselineProductCache` etc.) and members (`.Cache`, `.AddOrUpdate`, `.Query()`, `.JoinWithBaselineProductInfo()`, `.ProductIdIndex`) are consistent across Tasks 1.2, 2.2, 2.3, 3.1.

**Known risk flagged for the implementer:** the harness `IThroughputTest.Run` is synchronous but `full-real` is async — the plan uses `.GetAwaiter().GetResult()` (Task 3.1 Step 2). If the ported harness runs tests on a thread with a sync context this is safe (console app, none installed).

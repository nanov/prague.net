# Kafka Consumer Healthchecks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add split liveness/readiness `IHealthCheck` implementations for `KafkaCacheConsumer`, with state placed on existing statistics types and zero hot-path / healthy-path allocations.

**Architecture:** Hot-path writes (poll loop heartbeat + per-handler processing watchdog) land on existing `KafkaCachesConsumerStatistics` / `KafkaDataCacheStatistics` instances as plain `long` / `bool` / `int` fields. A pure `KafkaCachesHealthEvaluator` reads the snapshot and produces a verdict. Two thin `IHealthCheck` adapters call the evaluator. Configuration via `IOptions<KafkaCachesHealthOptions>`.

**Tech Stack:** .NET 9, Confluent.Kafka 2.12, `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`, NUnit (matches the existing `Kafka.IntegrationTests` project).

**Spec:** [`docs/superpowers/specs/2026-05-05-kafka-consumer-healthchecks-design.md`](../specs/2026-05-05-kafka-consumer-healthchecks-design.md)

---

## House-style reminders for every task

- Tabs (width 2), file-scoped namespaces, **usings inside the namespace**, `var` everywhere, K&R braces.
- Naming: `_camelCase` private fields, `IPascalCase` interfaces, `PascalCase` types/methods/properties/constants, `camelCase` parameters/locals.
- No LINQ on hot paths, no `string.Format`/interpolation in healthy-path branches, no `Volatile`/`Interlocked` for the new heartbeat fields (single writer per field, see spec §Performance).
- xUnit is NOT used — all tests are NUnit (`[Test]`, `[TestCase]`, `Assert.That`).

---

## File map

**Create:**
- `src/Prague.Kafka/Health/KafkaCachesHealthOptions.cs` — options POCO.
- `src/Prague.Kafka/Health/KafkaCachesHealthEvaluator.cs` — pure verdict computation.
- `src/Prague.Kafka/Health/KafkaCachesLivenessHealthCheck.cs` — `IHealthCheck` adapter.
- `src/Prague.Kafka/Health/KafkaCachesReadinessHealthCheck.cs` — `IHealthCheck` adapter.
- `src/Prague.Kafka/Health/HealthChecksBuilderExtensions.cs` — DI extensions.
- `tests/Prague.Kafka.Tests/Prague.Kafka.Tests.csproj` — new unit test project.
- `tests/Prague.Kafka.Tests/KafkaCachesHealthEvaluatorTests.cs` — evaluator unit tests.
- `tests/Prague.Kafka.Tests/KafkaCachesHealthAllocationTests.cs` — zero-alloc verification on healthy path.
- `tests/Prague.Kafka.Tests/LibrdkafkaBrokerStateParsingTests.cs` — `state` field parsing + `BrokerUpCount` aggregation.
- `tests/Prague.Kafka.IntegrationTests/HealthCheckTests.cs` — end-to-end with Testcontainers.

**Modify:**
- `src/Prague.Kafka/Statistics.cs` — add health fields to `KafkaDataCacheStatistics` + `KafkaCachesConsumerStatistics`; add `State` to `LibrdkafkaBrokerStats`; count UP brokers in `UpdateFromLibrdkafkaStats`.
- `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` — hot-path writes, fatal-latch sites, rebalance handler updates, propagate consumer stats to handlers.
- `src/Prague.Kafka/Prague.Kafka.csproj` — add HealthChecks.Abstractions ref + InternalsVisibleTo for new test project.
- `Prague.sln` and `Prague.Tests.slnf` — include the new test project.

---

## Task 1: Create `Prague.Kafka.Tests` unit test project

**Files:**
- Create: `tests/Prague.Kafka.Tests/Prague.Kafka.Tests.csproj`
- Modify: `Prague.sln`
- Modify: `Prague.Tests.slnf`
- Modify: `src/Prague.Kafka/Prague.Kafka.csproj` (add `InternalsVisibleTo`)

- [ ] **Step 1: Create the csproj**

Create `tests/Prague.Kafka.Tests/Prague.Kafka.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
    <PackageReference Include="NUnit" Version="4.5.1" />
    <PackageReference Include="NUnit.Analyzers" Version="4.12.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="9.0.11" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.11" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.11" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="NUnit.Framework"/>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Prague.Kafka\Prague.Kafka.csproj"/>
    <ProjectReference Include="..\..\src\Prague.Core\Prague.Core.csproj"/>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add `InternalsVisibleTo` for the new project**

Edit `src/Prague.Kafka/Prague.Kafka.csproj` — add a line inside the existing `InternalsVisibleTo` `<ItemGroup>` (between lines 13 and 19):

```xml
<InternalsVisibleTo Include="Prague.Kafka.Tests"/>
```

- [ ] **Step 3: Add to .sln**

Run:

```bash
dotnet sln Prague.sln add tests/Prague.Kafka.Tests/Prague.Kafka.Tests.csproj --solution-folder tests
```

- [ ] **Step 4: Add to test slnf**

Edit `Prague.Tests.slnf` — add this line to the `projects` array, alphabetically (it goes right after `Prague.Kafka.IntegrationTests`):

```json
"tests\\Prague.Kafka.Tests\\Prague.Kafka.Tests.csproj",
```

- [ ] **Step 5: Verify build**

Run:

```bash
dotnet build tests/Prague.Kafka.Tests/Prague.Kafka.Tests.csproj
```

Expected: Build succeeded. The project has zero tests; that's fine.

- [ ] **Step 6: Commit**

```bash
git add tests/Prague.Kafka.Tests \
        src/Prague.Kafka/Prague.Kafka.csproj \
        Prague.sln \
        Prague.Tests.slnf
git commit -m "test: scaffold Prague.Kafka.Tests unit test project"
```

---

## Task 2: Add HealthChecks.Abstractions package reference

**Files:**
- Modify: `src/Prague.Kafka/Prague.Kafka.csproj`

- [ ] **Step 1: Add the package reference**

Edit `src/Prague.Kafka/Prague.Kafka.csproj` — add inside the existing `PackageReference` `<ItemGroup>` (between lines 21 and 30), in alphabetical order:

```xml
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" Version="9.0.11"/>
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka/Prague.Kafka.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Prague.Kafka.csproj
git commit -m "build(kafka): reference HealthChecks.Abstractions"
```

---

## Task 3: Extend `LibrdkafkaBrokerStats` with `State` and parse it

**Files:**
- Modify: `src/Prague.Kafka/Statistics.cs:194-203` (add `State` to `LibrdkafkaBrokerStats`)
- Modify: `src/Prague.Kafka/Statistics.cs:24` area (add `BrokerUpCount` field + property + parsing)
- Test: `tests/Prague.Kafka.Tests/LibrdkafkaBrokerStateParsingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Prague.Kafka.Tests/LibrdkafkaBrokerStateParsingTests.cs`:

```csharp
namespace Prague.Kafka.Tests;

using System.Text.Json;

public sealed class LibrdkafkaBrokerStateParsingTests {
    [Test]
    public void UpdateFromLibrdkafkaStats_counts_brokers_with_state_UP() {
        var json = """
        {
          "rxmsgs": 0,
          "rxmsg_bytes": 0,
          "brokers": {
            "b1": { "state": "UP",   "rtt": {}, "throttle": {}, "int_latency": {} },
            "b2": { "state": "DOWN", "rtt": {}, "throttle": {}, "int_latency": {} },
            "b3": { "state": "UP",   "rtt": {}, "throttle": {}, "int_latency": {} }
          }
        }
        """;
        var snapshot = JsonSerializer.Deserialize(json,
            LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);

        var stats = new KafkaCachesConsumerStatistics();
        stats.UpdateFromLibrdkafkaStats(snapshot);

        Assert.That(stats.BrokerUpCount, Is.EqualTo(2));
    }

    [Test]
    public void UpdateFromLibrdkafkaStats_with_no_brokers_sets_zero() {
        var json = """{"rxmsgs":0,"rxmsg_bytes":0}""";
        var snapshot = JsonSerializer.Deserialize(json,
            LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);

        var stats = new KafkaCachesConsumerStatistics();
        stats.UpdateFromLibrdkafkaStats(snapshot);

        Assert.That(stats.BrokerUpCount, Is.EqualTo(0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/Prague.Kafka.Tests --filter LibrdkafkaBrokerStateParsingTests
```

Expected: Compile error — `LibrdkafkaStatsJsonContext` is internal but should resolve via `InternalsVisibleTo`; the failure will be `BrokerUpCount` does not exist on `KafkaCachesConsumerStatistics`.

- [ ] **Step 3: Add `State` to `LibrdkafkaBrokerStats`**

Edit `src/Prague.Kafka/Statistics.cs`. Replace the `LibrdkafkaBrokerStats` struct (currently at lines 194-203):

```csharp
internal readonly struct LibrdkafkaBrokerStats {
	[JsonPropertyName("state")]
	public string? State { get; init; }

	[JsonPropertyName("rtt")]
	public LibrdkafkaWindowStats Rtt { get; init; }

	[JsonPropertyName("throttle")]
	public LibrdkafkaWindowStats Throttle { get; init; }

	[JsonPropertyName("int_latency")]
	public LibrdkafkaWindowStats IntLatency { get; init; }
}
```

- [ ] **Step 4: Add `BrokerUpCount` field, property, and parsing**

Edit `src/Prague.Kafka/Statistics.cs`. In `KafkaCachesConsumerStatistics` (class starts at line 24):

After the existing `public int CachesLoadingCount { get; private set; }` line (currently line 47), add:

```csharp
/// <summary>
/// Number of brokers currently in "UP" state (from librdkafka stats).
/// </summary>
public int BrokerUpCount { get; private set; }
```

Then in `UpdateFromLibrdkafkaStats` (currently around line 93), add the broker-state count. Replace the entire method body:

```csharp
internal void UpdateFromLibrdkafkaStats(LibrdkafkaStatsSnapshot snapshot) {
	TotalMessagesReceived = snapshot.RxMsgs;
	TotalBytesReceived = snapshot.RxMsgBytes;

	if (snapshot.Brokers is null) {
		BrokerUpCount = 0;
		return;
	}

	long maxRtt = 0, maxThrottle = 0, maxIntLatency = 0;
	var upCount = 0;
	foreach (var broker in snapshot.Brokers.Values) {
		if (broker.State == "UP") upCount++;
		if (broker.Rtt.P99 > maxRtt) maxRtt = broker.Rtt.P99;
		if (broker.Throttle.P99 > maxThrottle) maxThrottle = broker.Throttle.P99;
		if (broker.IntLatency.P99 > maxIntLatency) maxIntLatency = broker.IntLatency.P99;
	}

	BrokerUpCount = upCount;
	BrokerLatencyMs = maxRtt / 1000;
	ThrottleMs = maxThrottle / 1000;
	QueueLatencyMs = maxIntLatency / 1000;
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/Prague.Kafka.Tests --filter LibrdkafkaBrokerStateParsingTests
```

Expected: 2 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Kafka/Statistics.cs \
        tests/Prague.Kafka.Tests/LibrdkafkaBrokerStateParsingTests.cs
git commit -m "feat(kafka): parse broker state and expose BrokerUpCount"
```

---

## Task 4: Add health-state fields to `KafkaDataCacheStatistics`

**Files:**
- Modify: `src/Prague.Kafka/Statistics.cs:113` area

- [ ] **Step 1: Add fields and read-only properties**

Edit `src/Prague.Kafka/Statistics.cs`. In `KafkaDataCacheStatistics` (class starts at line 113), after the existing `public FetchState FetchState { get; internal set; }` line, add:

```csharp
/// <summary>
/// 0 when the handler channel loop is idle. When non-zero, holds the
/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> of the moment
/// the current message started processing. Plain field; single writer (the
/// channel-loop task), single occasional reader (health checks). 64-bit
/// aligned, atomic on supported targets.
/// </summary>
internal long LastProcessingStartTimestamp;

/// <summary>
/// Latched true when the handler channel loop terminates with an exception.
/// One-way: never resets for the lifetime of this stats instance.
/// </summary>
internal bool IsLoopFaulted;

/// <summary>
/// Number of partitions currently assigned for this cache's topic.
/// </summary>
internal int AssignedPartitionCount;

public long LastProcessingStartTimestampUnsafe => LastProcessingStartTimestamp;
public bool IsLoopFaultedUnsafe => IsLoopFaulted;
public int  AssignedPartitionCountUnsafe => AssignedPartitionCount;
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Statistics.cs
git commit -m "feat(kafka): add per-cache health-state fields to KafkaDataCacheStatistics"
```

---

## Task 5: Add health-state fields to `KafkaCachesConsumerStatistics`

**Files:**
- Modify: `src/Prague.Kafka/Statistics.cs:24` area

- [ ] **Step 1: Add fields**

Edit `src/Prague.Kafka/Statistics.cs`. In `KafkaCachesConsumerStatistics`, after the new `BrokerUpCount` property added in Task 3, add:

```csharp
/// <summary>
/// Last <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> recorded by
/// the outer poll loop after <c>consumer.Consume(ct)</c> returned.
/// </summary>
internal long LastPollTimestamp;

/// <summary>
/// Latched true on any fatal Kafka error or any handler loop fault.
/// One-way: never resets for the lifetime of the consumer.
/// </summary>
internal bool IsFatalLatched;

/// <summary>
/// True when the most recent rebalance event was a partitions-lost event
/// and no successful re-assignment has occurred since.
/// </summary>
internal bool HasLostPartitions;

public long LastPollTimestampUnsafe => LastPollTimestamp;
public bool IsFatalLatchedUnsafe => IsFatalLatched;
public bool HasLostPartitionsUnsafe => HasLostPartitions;
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Statistics.cs
git commit -m "feat(kafka): add consumer health-state fields"
```

---

## Task 6: Add `KafkaCachesHealthOptions`

**Files:**
- Create: `src/Prague.Kafka/Health/KafkaCachesHealthOptions.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Prague.Kafka.Health;

public sealed class KafkaCachesHealthOptions {
	/// <summary>
	/// Maximum allowed time since the last <c>consumer.Consume</c> return
	/// before liveness fails. Default: 3 seconds.
	/// </summary>
	public TimeSpan PollLoopHeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(3);

	/// <summary>
	/// Maximum allowed time a single message may stay in-flight inside a
	/// handler's channel loop before liveness fails. Default: 5 seconds.
	/// </summary>
	public TimeSpan HandlerProcessingTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Minimum number of brokers in "UP" state required for readiness.
	/// Default: 1.
	/// </summary>
	public int MinBrokersUp { get; set; } = 1;
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Health/KafkaCachesHealthOptions.cs
git commit -m "feat(kafka-health): add KafkaCachesHealthOptions"
```

---

## Task 7: Implement `KafkaCachesHealthEvaluator` (TDD)

This is the core decision logic. We test the evaluator directly without spinning up `IHealthCheck` ceremony.

**Files:**
- Create: `src/Prague.Kafka/Health/KafkaCachesHealthEvaluator.cs`
- Create: `tests/Prague.Kafka.Tests/KafkaCachesHealthEvaluatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Prague.Kafka.Tests/KafkaCachesHealthEvaluatorTests.cs`:

```csharp
namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Health;
using Core;

public sealed class KafkaCachesHealthEvaluatorTests {
	private static KafkaCachesConsumerStatistics MakeHealthyStats(int caches = 2) {
		var stats = new KafkaCachesConsumerStatistics();
		stats.LastPollTimestamp = Stopwatch.GetTimestamp();
		stats.BrokerUpCount = 1;
		stats.HasLostPartitions = false;
		stats.IsFatalLatched = false;

		for (var i = 0; i < caches; i++) {
			var cache = stats.AddCache($"topic-{i}",
				new KafkaDataCacheStatistics($"topic-{i}", new DataCacheStatistics()));
			cache.AssignedPartitionCount = 1;
			cache.IsLoopFaulted = false;
			cache.LastProcessingStartTimestamp = 0;
			// All caches finished initial load.
			cache.SetInitialLoad(TimeSpan.FromMilliseconds(50));
		}
		// Mark "all caches loaded" via the existing field.
		stats.SetCachesLoadingCount(0);
		return stats;
	}

	private static KafkaCachesHealthOptions DefaultOptions() => new();

	[Test]
	public void Healthy_state_returns_Healthy_for_both_liveness_and_readiness() {
		var stats = MakeHealthyStats();
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Is.Empty);
	}

	[Test]
	public void Fatal_latched_fails_liveness() {
		var stats = MakeHealthyStats();
		stats.IsFatalLatched = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.FatalLatched));
	}

	[Test]
	public void Stale_poll_timestamp_fails_liveness() {
		var stats = MakeHealthyStats();
		// Backdate timestamp far past the timeout.
		stats.LastPollTimestamp =
			Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60); // 60s ago
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.PollLoopStalled));
	}

	[Test]
	public void Faulted_handler_loop_fails_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.IsLoopFaulted = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.HandlerLoopFaulted));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Idle_handler_does_not_fail_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.LastProcessingStartTimestamp = 0; // idle sentinel

		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
	}

	[Test]
	public void Long_running_handler_processing_fails_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.LastProcessingStartTimestamp =
			Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60); // 60s ago

		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures,
			Does.Contain(KafkaCachesHealthFailure.HandlerProcessingTimeout));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Caches_still_loading_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.SetCachesLoadingCount(1);
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.InitialLoadIncomplete));
	}

	[Test]
	public void Cache_with_zero_assigned_partitions_fails_only_readiness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.AssignedPartitionCount = 0;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.NoPartitionAssigned));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Brokers_below_minimum_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.BrokerUpCount = 0;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.BrokersDown));
	}

	[Test]
	public void Lost_partitions_flag_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.HasLostPartitions = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.PartitionsLost));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Prague.Kafka.Tests --filter KafkaCachesHealthEvaluatorTests
```

Expected: Compile errors — `KafkaCachesHealthEvaluator`, `KafkaCachesHealthFailure` are not defined.

- [ ] **Step 3: Implement the evaluator**

Create `src/Prague.Kafka/Health/KafkaCachesHealthEvaluator.cs`:

```csharp
namespace Prague.Kafka.Health;

using System.Diagnostics;

public enum KafkaCachesHealthFailure {
	FatalLatched,
	PollLoopStalled,
	HandlerLoopFaulted,
	HandlerProcessingTimeout,
	InitialLoadIncomplete,
	NoPartitionAssigned,
	BrokersDown,
	PartitionsLost
}

public readonly struct KafkaCachesHealthVerdict {
	public IReadOnlyList<KafkaCachesHealthFailure> LivenessFailures { get; init; }
	public IReadOnlyList<KafkaCachesHealthFailure> ReadinessFailures { get; init; }
	public IReadOnlyList<string> FailingCacheNames { get; init; }

	public bool IsLive => LivenessFailures.Count == 0;
	public bool IsReady => IsLive && ReadinessFailures.Count == 0;

	public static readonly KafkaCachesHealthVerdict Healthy = new() {
		LivenessFailures = Array.Empty<KafkaCachesHealthFailure>(),
		ReadinessFailures = Array.Empty<KafkaCachesHealthFailure>(),
		FailingCacheNames = Array.Empty<string>()
	};
}

public static class KafkaCachesHealthEvaluator {
	public static KafkaCachesHealthVerdict Evaluate(
		KafkaCachesConsumerStatistics stats,
		KafkaCachesHealthOptions options) {

		// Fast path: scan everything once. Allocate failure lists lazily.
		List<KafkaCachesHealthFailure>? live = null;
		List<KafkaCachesHealthFailure>? ready = null;
		List<string>? failingCaches = null;

		if (stats.IsFatalLatched)
			(live ??= new()).Add(KafkaCachesHealthFailure.FatalLatched);

		var pollAge = Stopwatch.GetElapsedTime(stats.LastPollTimestamp);
		if (pollAge >= options.PollLoopHeartbeatTimeout)
			(live ??= new()).Add(KafkaCachesHealthFailure.PollLoopStalled);

		foreach (var (name, cache) in stats.Caches) {
			if (cache.IsLoopFaulted) {
				(live ??= new()).Add(KafkaCachesHealthFailure.HandlerLoopFaulted);
				(failingCaches ??= new()).Add(name);
				continue;
			}
			var processingStart = cache.LastProcessingStartTimestamp;
			if (processingStart != 0 &&
				Stopwatch.GetElapsedTime(processingStart) >= options.HandlerProcessingTimeout) {
				(live ??= new()).Add(KafkaCachesHealthFailure.HandlerProcessingTimeout);
				(failingCaches ??= new()).Add(name);
			}
		}

		if (stats.CachesLoadingCount > 0)
			(ready ??= new()).Add(KafkaCachesHealthFailure.InitialLoadIncomplete);

		foreach (var (name, cache) in stats.Caches) {
			if (cache.AssignedPartitionCount < 1) {
				(ready ??= new()).Add(KafkaCachesHealthFailure.NoPartitionAssigned);
				(failingCaches ??= new()).Add(name);
			}
		}

		if (stats.BrokerUpCount < options.MinBrokersUp)
			(ready ??= new()).Add(KafkaCachesHealthFailure.BrokersDown);

		if (stats.HasLostPartitions)
			(ready ??= new()).Add(KafkaCachesHealthFailure.PartitionsLost);

		if (live is null && ready is null && failingCaches is null)
			return KafkaCachesHealthVerdict.Healthy;

		return new KafkaCachesHealthVerdict {
			LivenessFailures = (IReadOnlyList<KafkaCachesHealthFailure>?)live
				?? Array.Empty<KafkaCachesHealthFailure>(),
			ReadinessFailures = (IReadOnlyList<KafkaCachesHealthFailure>?)ready
				?? Array.Empty<KafkaCachesHealthFailure>(),
			FailingCacheNames = (IReadOnlyList<string>?)failingCaches
				?? Array.Empty<string>()
		};
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/Prague.Kafka.Tests --filter KafkaCachesHealthEvaluatorTests
```

Expected: 10 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/Health/KafkaCachesHealthEvaluator.cs \
        tests/Prague.Kafka.Tests/KafkaCachesHealthEvaluatorTests.cs
git commit -m "feat(kafka-health): add KafkaCachesHealthEvaluator with verdict computation"
```

---

## Task 8: Implement `KafkaCachesLivenessHealthCheck`

**Files:**
- Create: `src/Prague.Kafka/Health/KafkaCachesLivenessHealthCheck.cs`

- [ ] **Step 1: Implement the check**

Create `src/Prague.Kafka/Health/KafkaCachesLivenessHealthCheck.cs`:

```csharp
namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesLivenessHealthCheck : IHealthCheck {
	private readonly KafkaCachesStatistics _statistics;
	private readonly IOptionsMonitor<KafkaCachesHealthOptions> _options;

	public KafkaCachesLivenessHealthCheck(
		KafkaCachesStatistics statistics,
		IOptionsMonitor<KafkaCachesHealthOptions> options) {
		_statistics = statistics;
		_options = options;
	}

	public Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) {

		var opts = _options.CurrentValue;
		var anyFailure = false;
		Dictionary<string, object>? data = null;

		foreach (var (consumerName, consumerStats) in _statistics.Consumers) {
			var verdict = KafkaCachesHealthEvaluator.Evaluate(consumerStats, opts);
			if (verdict.IsLive) continue;

			anyFailure = true;
			data ??= new Dictionary<string, object>();
			data[consumerName + ".failures"] = verdict.LivenessFailures;
			if (verdict.FailingCacheNames.Count > 0)
				data[consumerName + ".caches"] = verdict.FailingCacheNames;
		}

		return Task.FromResult(anyFailure
			? HealthCheckResult.Unhealthy(description: null, exception: null, data: data)
			: HealthCheckResult.Healthy());
	}
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Health/KafkaCachesLivenessHealthCheck.cs
git commit -m "feat(kafka-health): add KafkaCachesLivenessHealthCheck"
```

---

## Task 9: Implement `KafkaCachesReadinessHealthCheck`

**Files:**
- Create: `src/Prague.Kafka/Health/KafkaCachesReadinessHealthCheck.cs`

- [ ] **Step 1: Implement the check**

Create `src/Prague.Kafka/Health/KafkaCachesReadinessHealthCheck.cs`:

```csharp
namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesReadinessHealthCheck : IHealthCheck {
	private readonly KafkaCachesStatistics _statistics;
	private readonly IOptionsMonitor<KafkaCachesHealthOptions> _options;

	public KafkaCachesReadinessHealthCheck(
		KafkaCachesStatistics statistics,
		IOptionsMonitor<KafkaCachesHealthOptions> options) {
		_statistics = statistics;
		_options = options;
	}

	public Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) {

		var opts = _options.CurrentValue;
		var anyLivenessFailure = false;
		var anyReadinessFailure = false;
		Dictionary<string, object>? data = null;

		foreach (var (consumerName, consumerStats) in _statistics.Consumers) {
			var verdict = KafkaCachesHealthEvaluator.Evaluate(consumerStats, opts);
			if (verdict.IsReady) continue;

			data ??= new Dictionary<string, object>();
			if (!verdict.IsLive) {
				anyLivenessFailure = true;
				data[consumerName + ".liveness_failures"] = verdict.LivenessFailures;
			}
			if (verdict.ReadinessFailures.Count > 0) {
				anyReadinessFailure = true;
				data[consumerName + ".readiness_failures"] = verdict.ReadinessFailures;
			}
			if (verdict.FailingCacheNames.Count > 0)
				data[consumerName + ".caches"] = verdict.FailingCacheNames;
		}

		if (anyLivenessFailure)
			return Task.FromResult(HealthCheckResult.Unhealthy(description: null, exception: null, data: data));
		if (anyReadinessFailure)
			return Task.FromResult(HealthCheckResult.Degraded(description: null, exception: null, data: data));
		return Task.FromResult(HealthCheckResult.Healthy());
	}
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Health/KafkaCachesReadinessHealthCheck.cs
git commit -m "feat(kafka-health): add KafkaCachesReadinessHealthCheck"
```

---

## Task 10: Add zero-allocation test on the healthy path

**Files:**
- Create: `tests/Prague.Kafka.Tests/KafkaCachesHealthAllocationTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/Prague.Kafka.Tests/KafkaCachesHealthAllocationTests.cs`:

```csharp
namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Health;
using Core;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesHealthAllocationTests {
	private static KafkaCachesStatistics MakeHealthy(int caches = 3) {
		var top = new KafkaCachesStatistics();
		var stats = top.GetOrAddConsumer("c1");
		stats.LastPollTimestamp = Stopwatch.GetTimestamp();
		stats.BrokerUpCount = 1;
		for (var i = 0; i < caches; i++) {
			var c = stats.AddCache($"t{i}",
				new KafkaDataCacheStatistics($"t{i}", new DataCacheStatistics()));
			c.AssignedPartitionCount = 1;
		}
		stats.SetCachesLoadingCount(0);
		return top;
	}

	[Test]
	public async Task Liveness_check_on_healthy_path_does_not_allocate() {
		var stats = MakeHealthy();
		var opts = Options.Create(new KafkaCachesHealthOptions());
		var monitor = new TestOptionsMonitor(opts.Value);
		var check = new KafkaCachesLivenessHealthCheck(stats, monitor);
		var ctx = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

		// warm-up to JIT.
		await check.CheckHealthAsync(ctx);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 32; i++)
			await check.CheckHealthAsync(ctx);
		var delta = GC.GetAllocatedBytesForCurrentThread() - before;

		// Allow at most a tiny per-call overhead (Task.FromResult result boxing
		// in some runtimes, etc.). 32 calls * <50 bytes/call ⇒ < 1.6 KB.
		Assert.That(delta, Is.LessThan(2048),
			$"Allocated {delta} bytes across 32 healthy checks");
	}

	[Test]
	public async Task Readiness_check_on_healthy_path_does_not_allocate() {
		var stats = MakeHealthy();
		var opts = Options.Create(new KafkaCachesHealthOptions());
		var monitor = new TestOptionsMonitor(opts.Value);
		var check = new KafkaCachesReadinessHealthCheck(stats, monitor);
		var ctx = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

		await check.CheckHealthAsync(ctx);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 32; i++)
			await check.CheckHealthAsync(ctx);
		var delta = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(delta, Is.LessThan(2048),
			$"Allocated {delta} bytes across 32 healthy checks");
	}

	private sealed class TestOptionsMonitor : IOptionsMonitor<KafkaCachesHealthOptions> {
		public TestOptionsMonitor(KafkaCachesHealthOptions value) => CurrentValue = value;
		public KafkaCachesHealthOptions CurrentValue { get; }
		public KafkaCachesHealthOptions Get(string? name) => CurrentValue;
		public IDisposable? OnChange(Action<KafkaCachesHealthOptions, string?> listener) => null;
	}
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test tests/Prague.Kafka.Tests --filter KafkaCachesHealthAllocationTests -c Release
```

Expected: 2 tests passed.

If any test fails, profile with `dotnet-counters` or step through a single call in a debugger and remove the offending allocation (e.g. unintended `Task.FromResult` boxing — wrap the result struct manually if needed; or interpolation that snuck in).

- [ ] **Step 3: Commit**

```bash
git add tests/Prague.Kafka.Tests/KafkaCachesHealthAllocationTests.cs
git commit -m "test(kafka-health): verify zero allocations on healthy check path"
```

---

## Task 11: Add DI extensions for health checks

**Files:**
- Create: `src/Prague.Kafka/Health/HealthChecksBuilderExtensions.cs`

- [ ] **Step 1: Implement the extensions**

Create `src/Prague.Kafka/Health/HealthChecksBuilderExtensions.cs`:

```csharp
namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

public static class HealthChecksBuilderExtensions {
	public static IHealthChecksBuilder AddPragueKafkaLiveness(
		this IHealthChecksBuilder builder,
		string name = "prague-kafka-live",
		HealthStatus failureStatus = HealthStatus.Unhealthy,
		IEnumerable<string>? tags = null) {
		builder.Services.AddOptions<KafkaCachesHealthOptions>();
		return builder.AddCheck<KafkaCachesLivenessHealthCheck>(name, failureStatus, tags);
	}

	public static IHealthChecksBuilder AddPragueKafkaReadiness(
		this IHealthChecksBuilder builder,
		string name = "prague-kafka-ready",
		HealthStatus failureStatus = HealthStatus.Degraded,
		IEnumerable<string>? tags = null) {
		builder.Services.AddOptions<KafkaCachesHealthOptions>();
		return builder.AddCheck<KafkaCachesReadinessHealthCheck>(name, failureStatus, tags);
	}
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Kafka/Health/HealthChecksBuilderExtensions.cs
git commit -m "feat(kafka-health): add AddPragueKafkaLiveness/Readiness DI extensions"
```

---

## Task 12: Wire outer poll-loop heartbeat in `KafkaCacheConsumer.Consume`

**Files:**
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs:444` (poll-loop write) and the existing fatal-error catches.

- [ ] **Step 1: Add the heartbeat write**

Edit `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`. In the `Consume` method, locate the line (currently 444):

```csharp
var consumeResult = consumer.Consume(ct);
```

Replace with:

```csharp
var consumeResult = consumer.Consume(ct);
_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();
```

- [ ] **Step 2: Latch fatal in fatal-error sites**

In the same `Consume` method, the existing `catch` blocks (currently lines 483-504) need `_statistics.IsFatalLatched = true;` set BEFORE `_manualReset.TrySetException(...)` in:

1. `catch (KafkaException e) when (e.Error.IsFatal)` (around line 483) — add as first statement of the block.
2. `catch (KafkaException e) when (e.Error.IsFatal is false)` inner branch where `isAppFatal` is true (around line 491-495) — add immediately after the log.
3. `catch (Exception e)` (around line 499) — add as first statement of the block.

After-edit example for the first one:

```csharp
catch (KafkaException e) when (e.Error.IsFatal) {
	_statistics.IsFatalLatched = true;
	_logger.FatalKafkaError(e);
	_manualReset.TrySetException(e);
	throw;
}
```

Apply the same pattern to the other two sites.

- [ ] **Step 3: Latch fatal also in the outer try/catch (around line 506-511)**

The `catch (Exception ex)` at the outer level (around line 508) — also needs `_statistics.IsFatalLatched = true;` as the first line.

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/IO/KafkaCacheConsumer.cs
git commit -m "feat(kafka): write poll-loop heartbeat and latch fatal on consumer stats"
```

---

## Task 13: Wire per-cache `AssignedPartitionCount` and `HasLostPartitions` in rebalance handlers

**Files:**
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs:373-398` (the three rebalance handlers).

- [ ] **Step 1: Update `SetPartitionsAssignedHandler`**

Edit `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`. Replace the body of `.SetPartitionsAssignedHandler((c, partitions) => { ... })` (currently lines 373-390) with:

```csharp
.SetPartitionsAssignedHandler((c, partitions) => {
	_assignedPartitions += partitions.Count;
	_statistics.SetAssignedPartitions(_assignedPartitions);
	_statistics.HasLostPartitions = false;
	foreach (var partition in partitions) {
		_logger.AssignedToTopic(partition.Topic);
		var watermaker = c.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
		if (watermaker is null) {
			_logger.NullWatermark(partition.Topic);
			throw new Exception("Kafka returned null watermark for topic: {partition.Topic}");
		}

		if (!_handlers.TryGetValue(partition.Topic, out var handler))
			continue;
		handler.SetHighWatermarkOffset(watermaker.High.Value);
		_cachesLoading++;
		_statistics.Caches[partition.Topic].AssignedPartitionCount++;
	}
	_statistics.SetCachesLoadingCount(_cachesLoading);
})
```

- [ ] **Step 2: Update `SetPartitionsRevokedHandler`**

Replace the body (currently lines 391-394):

```csharp
.SetPartitionsRevokedHandler((_, partitions) => {
	_assignedPartitions -= partitions.Count;
	_statistics.SetAssignedPartitions(_assignedPartitions);
	foreach (var partition in partitions) {
		if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
			cacheStats.AssignedPartitionCount--;
	}
})
```

- [ ] **Step 3: Update `SetPartitionsLostHandler`**

Replace the body (currently lines 395-398):

```csharp
.SetPartitionsLostHandler((_, partitions) => {
	_assignedPartitions -= partitions.Count;
	_statistics.SetAssignedPartitions(_assignedPartitions);
	_statistics.HasLostPartitions = true;
	foreach (var partition in partitions) {
		if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
			cacheStats.AssignedPartitionCount--;
	}
})
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/IO/KafkaCacheConsumer.cs
git commit -m "feat(kafka): track per-cache AssignedPartitionCount and HasLostPartitions"
```

---

## Task 14: Wire per-handler processing watchdog and loop-fault latch in `ChannelLoop`

**Files:**
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` — pass `KafkaCachesConsumerStatistics` to handler ctor; wrap message processing with start/clear writes; latch on terminal fault.

- [ ] **Step 1: Pass `KafkaCachesConsumerStatistics` to each handler**

Edit `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`. Modify the `KafkaCacheHandler` abstract class (currently line 16) to add:

```csharp
internal KafkaCachesConsumerStatistics? ConsumerStatistics { get; private protected set; }
internal void SetConsumerStatistics(KafkaCachesConsumerStatistics consumerStatistics) =>
	ConsumerStatistics = consumerStatistics;
```

Add this just below the `Statistics` abstract property declaration (currently line 73).

In `KafkaCacheConsumer` constructor (currently line 339), after the line `_handlers = kafkaCacheHandlers.Handlers;` (line 348), add:

```csharp
foreach (var handler in _handlers.Values)
	handler.SetConsumerStatistics(_statistics);
```

- [ ] **Step 2: Wrap message processing with start/clear writes in `ChannelLoop`**

Edit the `ChannelLoop` method in `KafkaCacheHandler<TCacheEntity, TKey, TVlaue>` (currently starting line 253). Replace the inner `while (reader.TryRead(out var result)) { ... }` body — the existing code currently runs from line 259 to ~308.

Find the existing block:

```csharp
while (await reader.WaitToReadAsync(ct))
while (reader.TryRead(out var result)) {
	if (result.IsPartitionEOF) {
		// ... existing code ...
	}
	// ... existing key-deserialize ...
	// ... existing dispatch ...
}
```

Wrap the per-message body with start/clear writes. Final form:

```csharp
while (await reader.WaitToReadAsync(ct))
while (reader.TryRead(out var result)) {
	_statistics.LastProcessingStartTimestamp = Stopwatch.GetTimestamp();
	try {
		if (result.IsPartitionEOF) {
			if (isLoading) {
				FlushBuffer(buffer!);
				buffer = null;

				var loadTime = Stopwatch.GetElapsedTime(_startTimestamp);
				_logger.CacheLoaded(Name, result.Offset, loadTime.TotalMilliseconds);
				_statistics.SetInitialLoad(loadTime);
				countdownEvent.Signal(loadTime);
				isLoading = false;
				_isLoading = false;
			}

			continue;
		}

		TKey key;
		try {
			key = CacheSerde<TKey>.Deserialize(result.Message.Key);
		} catch (Exception e) {
			_logger.ErrorDeserializingKey(e, Name, result.Offset);
			result.Message.Key.Dispose();
			result.Message.Value.Dispose();
			continue;
		}
		result.Message.Key.Dispose();

		if (isLoading) {
			buffer!.Add(key, result);
			if (buffer.IsFull)
				FlushBuffer(buffer);
			continue;
		}

		try {
			if (result.Message.Value.IsNull)
				await HandleDelete(isLoading, result.Message.Timestamp, key);
			else
				await HandleUpdate(isLoading, key, CacheSerde<TVlaue>.Deserialize(result.Message.Value),
					result.Message.Headers,
					result.Message.Timestamp,
					result.Offset);
		} catch (Exception e) {
			_logger.ErrorProcessingMessage(e, Name, result.Offset);
		}
		finally {
			result.Message.Value.Dispose();
		}
	}
	finally {
		_statistics.LastProcessingStartTimestamp = 0;
	}
}
```

Note the outer `try`/`finally` wraps the whole iteration body so the timestamp is always cleared. The existing inner `try`/`finally` for `result.Message.Value.Dispose()` is preserved.

- [ ] **Step 3: Latch loop-fault and consumer-fatal on terminal `catch`**

Still in `ChannelLoop`, the terminal `catch (Exception e)` (currently line 313) — replace its body:

```csharp
catch (Exception e) {
	_statistics.IsLoopFaulted = true;
	if (ConsumerStatistics is not null)
		ConsumerStatistics.IsFatalLatched = true;
	_logger.ChannelConsumptionError(e, Name);
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Prague.Kafka
```

Expected: Build succeeded.

- [ ] **Step 5: Run all unit tests so far**

```bash
dotnet test tests/Prague.Kafka.Tests
```

Expected: All previously-added unit tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Kafka/IO/KafkaCacheConsumer.cs
git commit -m "feat(kafka): per-handler processing watchdog and loop-fault latch"
```

---

## Task 15: Integration test — readiness reflects broker connectivity

**Files:**
- Create: `tests/Prague.Kafka.IntegrationTests/HealthCheckTests.cs`

This task uses Testcontainers. Read `tests/Prague.Kafka.IntegrationTests/Fixtures/` first to follow the existing pattern for spinning up a Kafka container. The example below assumes a `KafkaFixture` similar to existing tests.

- [ ] **Step 1: Read existing fixture conventions**

```bash
ls tests/Prague.Kafka.IntegrationTests/Fixtures
```

Inspect the existing fixture file to mirror its setup (DI container, `KafkaCachesStatistics` resolution, host startup). The patterns should match `DualClusterCacheTests.cs`.

- [ ] **Step 2: Write the broker-down integration test**

Create `tests/Prague.Kafka.IntegrationTests/HealthCheckTests.cs`:

```csharp
namespace Prague.Kafka.IntegrationTests;

using Prague.Kafka;
using Prague.Kafka.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

[TestFixture]
public sealed class HealthCheckTests {
	// Re-use the existing single-cluster fixture pattern from DualClusterCacheTests.
	// Bootstrap a host with one cache, wait for initial load, assert healthy.

	[Test]
	public async Task Liveness_and_readiness_pass_after_initial_load() {
		// Arrange: spin up Kafka container + host (mirror DualClusterCacheTests setup).
		// await using var fixture = await KafkaFixture.StartAsync();
		// var host = fixture.Host;
		// await host.DataCachesLoadCompletion();

		// var stats = host.Services.GetRequiredService<KafkaCachesStatistics>();
		// var liveness = ActivatorUtilities.CreateInstance<KafkaCachesLivenessHealthCheck>(host.Services);
		// var readiness = ActivatorUtilities.CreateInstance<KafkaCachesReadinessHealthCheck>(host.Services);

		// var live = await liveness.CheckHealthAsync(new HealthCheckContext());
		// var ready = await readiness.CheckHealthAsync(new HealthCheckContext());

		// Assert.That(live.Status, Is.EqualTo(HealthStatus.Healthy));
		// Assert.That(ready.Status, Is.EqualTo(HealthStatus.Healthy));
		Assert.Inconclusive("Wire to existing KafkaFixture pattern from DualClusterCacheTests.");
	}

	[Test]
	public async Task Readiness_degrades_when_broker_goes_down() {
		// 1. Start container, host, wait for load.
		// 2. Stop the Kafka container.
		// 3. Wait for librdkafka stats to register broker as not UP (statistics interval needs to be enabled).
		// 4. Assert readiness check returns Degraded.
		// 5. Assert liveness still Healthy (poll loop is still ticking with errors but not stalled).
		Assert.Inconclusive("Wire to existing KafkaFixture pattern from DualClusterCacheTests.");
	}
}
```

The `Inconclusive` placeholders are explicitly there because the integration-test fixture style in this repo varies — the implementer should mirror the patterns in `DualClusterCacheTests.cs` (read it before writing this file). The shape and assertions above are correct; the missing piece is the host bootstrap which must match the existing test conventions.

- [ ] **Step 3: Replace inconclusives with actual assertions matching `DualClusterCacheTests` style**

Read `tests/Prague.Kafka.IntegrationTests/DualClusterCacheTests.cs` to see how the host is built, how `StatisticsEnabled = true` is set on `KafkaCachesGlobalOptions`, how the container lifecycle is managed. Then replace each `Assert.Inconclusive` with the real implementation.

For the broker-down test specifically: `StatisticsEnabled = true` and `StatisticsIntervalSeconds = 1` must be set on `KafkaCachesGlobalOptions` so `_statistics.BrokerUpCount` is updated within the test window.

- [ ] **Step 4: Run the integration tests**

```bash
dotnet test tests/Prague.Kafka.IntegrationTests --filter HealthCheckTests
```

Expected: 2 tests passed.

- [ ] **Step 5: Commit**

```bash
git add tests/Prague.Kafka.IntegrationTests/HealthCheckTests.cs
git commit -m "test(kafka-health): integration tests for broker-down readiness"
```

---

## Task 16: Final verification

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test Prague.Tests.slnf
```

Expected: all green.

- [ ] **Step 2: Build the publishable solution filter**

```bash
dotnet build Prague.Publish.slnf -c Release
```

Expected: build succeeded with no warnings introduced by this change.

- [ ] **Step 3: Skim diff for hot-path correctness**

```bash
git log --oneline main..HEAD
git diff main...HEAD -- src/Prague.Kafka/IO/KafkaCacheConsumer.cs
```

Confirm:
- `_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();` appears immediately after `consumer.Consume(ct);` and nowhere else.
- `_statistics.LastProcessingStartTimestamp = Stopwatch.GetTimestamp();` is the first statement inside `try` of the per-message body, paired with `= 0` in `finally`.
- No `Volatile` / `Interlocked` on the new fields.
- No LINQ added on hot paths.
- `IsFatalLatched = true` precedes `_manualReset.TrySetException` at every fatal site.

- [ ] **Step 4: Final commit (if any merge cleanup needed) or stop here**

The previous task commits are sufficient. No final commit required.

---

## Self-review notes (already applied to the plan)

- Spec §Surface → Tasks 6, 8, 9, 11.
- Spec §State placement → Tasks 4, 5.
- Spec §Health logic → Task 7 (TDD covers all eight failure predicates plus the live/ready split).
- Spec §Hot-path changes → Tasks 12 (poll), 14 (per-handler).
- Spec §Rebalance handler changes → Task 13.
- Spec §Fatal-latch trigger sites → Tasks 12 (consumer fatals), 14 (handler-loop terminal catch).
- Spec §librdkafka stats extension → Task 3.
- Spec §Performance — health-check read path → Task 10 (zero-alloc test).
- Spec §Tests → Tasks 7, 10 (unit), 15 (integration).

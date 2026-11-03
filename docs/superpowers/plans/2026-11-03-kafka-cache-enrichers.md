# Kafka cache enrichers (`WithEnricher`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a host change a consumed value in place, from its own state, before Prague stores and indexes it — in both the initial load and the live phase.

**Architecture:** A new `ICacheEnricher<TKey, TValue>` is registered per cache through `WithEnricher<T>()` (type or factory form, exactly like `WithAfterHandler`). The handler resolves all registered enrichers once when it is built and calls them in `DispatchRaw` right after Prague's own header/timestamp enricher, before the value enters the load buffer or the live ring buffer. A throwing enricher is logged and the value is kept as enriched so far.

**Tech Stack:** .NET 9/10, Prague.Kafka (`src/Prague.Kafka`), NUnit, Testcontainers.Kafka for integration tests, DocFX-style docs under `www/docs` fed by compiled samples in `samples/Prague.Samples`.

Spec: `docs/superpowers/specs/2026-11-03-kafka-cache-enrichers-design.md`.

## Global Constraints

- Repository: `D:\Work\prague.net-fork`, branch `feat/kafka-cache-enrichers`. **Do not commit, push or open a PR** — the repository owner commits; leave every change in the working tree.
- `dotnet build Prague.sln` must stay clean: warnings are errors, XML doc checks are on for every project (a public member without `<summary>` fails the build).
- House style: tabs, K&R braces (`} catch (Exception e) {`, `} finally {`), one type per file unless the file already groups them, `[MethodImpl]` attributes on hot-path helpers as the surrounding code does. Follow `CLAUDE.md` and the neighbouring code in each file; never hand-edit `*.generated.cs`.
- Every test project is NUnit: `[TestFixture]`, `[Test]`, `Assert.That`.
- `Prague.Kafka.IntegrationTests` need Docker (available on this machine, `docker info` answers). Each test creates its own topic and uses its own `group.id`, as `AfterHandlerTests` does.
- Hot path: `DispatchRaw` gains exactly one `_enrichers.Length != 0` test per branch; the `try`/`catch` lives in a separate non-inlined method (see spec, "Hot path").
- The handler file names its value type parameter `TVlaue` (sic). Keep that spelling inside `KafkaCacheConsumer.cs`; everywhere else it is `TValue`.

---

### Task 1: The hook — interface, registration, handler wiring, unit tests

**Files:**
- Create: `src/Prague.Kafka/ICacheEnricher.cs`
- Modify: `src/Prague.Kafka/DependencyInjection.cs` (after `WithAfterHandler` overloads at lines 44–55; next to `BuildValueFilters` at lines 533–540; `Build` at lines 542–563)
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` (handler fields/ctor at lines 124–150; `DispatchRaw` load branch at line 253 and live branch at line 286; `KafkaCacheConsumerLog` at line 734)
- Test: `tests/Prague.Kafka.Tests/DependencyInjection/EnricherRegistrationTests.cs`

**Interfaces:**
- Produces: `Prague.Kafka.ICacheEnricher<in TKey, in TValue>` with `void Enrich(TKey key, TValue value)`.
- Produces: `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>.WithEnricher<TEnricher>()` and `.WithEnricher<TEnricher>(Func<IServiceProvider, TEnricher>)`, both returning the builder; `internal ICacheEnricher<TKey, TValue>[] BuildEnrichers(IServiceProvider sp)`.
- Produces: `KafkaCacheConsumerLog.EnricherError(this ILogger, Exception, string cacheName, long offset)`.

- [ ] **Step 1: Write the failing registration tests**

Create `tests/Prague.Kafka.Tests/DependencyInjection/EnricherRegistrationTests.cs`:

```csharp
namespace Prague.Kafka.Tests.DependencyInjection;

using Baseline.Scenario;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Covers <c>WithEnricher</c>: both overloads register a singleton <see cref="ICacheEnricher{TKey,TValue}"/>,
///   registration resolves nothing, and <c>BuildEnrichers</c> hands the handler the enrichers in registration
///   order. Broker-free: the builder is constructible in-process and <c>BuildEnrichers</c> exposes the built array.
/// </summary>
[TestFixture]
public class EnricherRegistrationTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private sealed class FirstEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	private sealed class SecondEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	[Test]
	public void TypeOverload_RegistersOneSingleton() {
		var services = new ServiceCollection();
		NewBuilder(services).WithEnricher<FirstEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = provider.GetServices<ICacheEnricher<int, BaselineProduct>>().ToArray();

		Assert.That(enrichers, Has.Length.EqualTo(1));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
		Assert.That(provider.GetRequiredService<ICacheEnricher<int, BaselineProduct>>(), Is.SameAs(enrichers[0]),
			"the type overload registers a singleton");
	}

	[Test]
	public void FactoryOverload_ResolvesThroughTheFactory_Once() {
		var services = new ServiceCollection();
		var calls = 0;
		NewBuilder(services).WithEnricher(sp => {
			calls++;
			return new FirstEnricher();
		});

		Assert.That(calls, Is.Zero, "registration must not resolve anything");

		using var provider = services.BuildServiceProvider();
		var first = provider.GetRequiredService<ICacheEnricher<int, BaselineProduct>>();
		var second = provider.GetRequiredService<ICacheEnricher<int, BaselineProduct>>();

		Assert.That(calls, Is.EqualTo(1));
		Assert.That(second, Is.SameAs(first));
	}

	[Test]
	public void BuildEnrichers_ReturnsTheEnrichersInRegistrationOrder() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services)
			.WithEnricher<FirstEnricher>()
			.WithEnricher<SecondEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = builder.BuildEnrichers(provider);

		Assert.That(enrichers, Has.Length.EqualTo(2));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
		Assert.That(enrichers[1], Is.TypeOf<SecondEnricher>());
	}

	[Test]
	public void BuildEnrichers_WithoutRegistrations_IsEmpty() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);

		using var provider = services.BuildServiceProvider();

		Assert.That(builder.BuildEnrichers(provider), Is.Empty);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~EnricherRegistrationTests"`
Expected: build error — `ICacheEnricher` and `WithEnricher` do not exist (CS0246 / CS1061).

- [ ] **Step 3: Add the interface**

Create `src/Prague.Kafka/ICacheEnricher.cs`:

```csharp
namespace Prague.Kafka;

/// <summary>
///   Changes a consumed value in place before the cache stores it. Registered per cache with
///   <see cref="KafkaCacheHandlerBuilder{TCacheEntity,TKey,TValue}.WithEnricher{TEnricher}()" />.
///   <para>
///     Runs for every value that passed the header, key and value filters, in both the initial load and the live
///     phase, after Prague's own <c>[DataCacheHeader]</c> / <c>[DataCacheFromTimestamp]</c> enrichment and before
///     the value enters the load buffer or the live worker. The value is not yet visible to any reader, so it is
///     changed in place; the indexes are built from the enriched value, and the live phase compares the enriched
///     value with the resident one to decide <see cref="UpdateType.Same" /> against <see cref="UpdateType.Update" />.
///   </para>
///   <para>
///     Runs synchronously on the single consume thread shared by every cache of the <c>AddKafkaCaches</c> section,
///     so it must not block or perform I/O: a dictionary lookup or a <c>TryGet</c> on another cache is the intended
///     cost. Another Prague cache read from here holds whatever its own consumption has reached — complete once
///     both caches are live, possibly partial while the section is still loading, because topics load concurrently.
///   </para>
///   <para>
///     A throw is logged (<c>EnricherError</c>) and the value is stored as enriched so far; the message is never
///     dropped and the remaining enrichers of that value still run. Several enrichers run in registration order.
///   </para>
/// </summary>
/// <typeparam name="TKey">The cache key type.</typeparam>
/// <typeparam name="TValue">The cache value type.</typeparam>
public interface ICacheEnricher<in TKey, in TValue> {
	/// <summary>Fills or changes fields of <paramref name="value" /> before it is stored.</summary>
	/// <param name="key">The deserialized key of the message.</param>
	/// <param name="value">The deserialized, header-enriched value; not yet in the cache.</param>
	void Enrich(TKey key, TValue value);
}
```

- [ ] **Step 4: Add the two `WithEnricher` overloads and `BuildEnrichers`**

In `src/Prague.Kafka/DependencyInjection.cs`, directly after the second `WithAfterHandler` overload (the one taking `Func<IServiceProvider, TAfterHandler> builder`, ends at line 55), add:

```csharp
	/// <summary>
	///   Registers <typeparamref name="TEnricher" /> as a singleton <see cref="ICacheEnricher{TKey,TValue}" /> of this
	///   cache. Several registrations run in registration order. See <see cref="ICacheEnricher{TKey,TValue}" /> for
	///   when and where it runs.
	/// </summary>
	/// <typeparam name="TEnricher">The enricher; the host's container constructs it.</typeparam>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>()
		where TEnricher : class, ICacheEnricher<TKey, TValue> {
		Services.AddSingleton<ICacheEnricher<TKey, TValue>, TEnricher>();
		return this;
	}

	/// <summary>
	///   Registers a singleton <see cref="ICacheEnricher{TKey,TValue}" /> of this cache built by
	///   <paramref name="builder" />, for enrichers with constructor arguments the container cannot supply on its own.
	/// </summary>
	/// <typeparam name="TEnricher">The enricher.</typeparam>
	/// <param name="builder">Builds the enricher from the root provider, once.</param>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>(
		Func<IServiceProvider, TEnricher> builder)
		where TEnricher : class, ICacheEnricher<TKey, TValue> {
		Services.AddSingleton<ICacheEnricher<TKey, TValue>>(builder);
		return this;
	}
```

Directly after `BuildValueFilters` (ends at line 540), add:

```csharp
	/// <summary>
	///   The enrichers registered for this cache's key and value types, in registration order; empty when none.
	///   Resolved once, when the handler is built — nothing is resolved on the ingestion path.
	/// </summary>
	internal ICacheEnricher<TKey, TValue>[] BuildEnrichers(IServiceProvider sp)
		=> sp.GetServices<ICacheEnricher<TKey, TValue>>().ToArray();
```

In `Build`, pass the array to the handler: change

```csharp
				BuildValueFilters(sp),
				sp.GetServices<ICacheAfterHandler<TKey, TValue>>(),
				sp.GetRequiredService<ILogger<KafkaCacheHandler<TCacheEntity, TKey, TValue>>>()));
```

to

```csharp
				BuildValueFilters(sp),
				sp.GetServices<ICacheAfterHandler<TKey, TValue>>(),
				BuildEnrichers(sp),
				sp.GetRequiredService<ILogger<KafkaCacheHandler<TCacheEntity, TKey, TValue>>>()));
```

- [ ] **Step 5: Wire the handler**

In `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`, class `KafkaCacheHandler<TCacheEntity, TKey, TVlaue>`:

Add the field after `_afterHandlers`:

```csharp
	private readonly ICacheAfterHandler<TKey, TVlaue>[] _afterHandlers;
	private readonly ICacheEnricher<TKey, TVlaue>[] _enrichers;
```

Add the constructor parameter after `afterHandlers` and assign it:

```csharp
	public KafkaCacheHandler(
		TCacheEntity cache,
		KafkaDataCacheStatistics statistics,
		KafkaHeaderFilters filtes,
		KafkaKeyFilters<TKey> keyFilters,
		KafkaValueFilters<TVlaue> valueFilters,
		IEnumerable<ICacheAfterHandler<TKey, TVlaue>> afterHandlers,
		ICacheEnricher<TKey, TVlaue>[] enrichers,
		ILogger logger) : base(filtes) {
		_cache = cache;
		_statistics = statistics;
		_keyFilters = keyFilters;
		_valueFilters = valueFilters;
		_logger = logger;
		_enricher = TVlaue.GetEnricher();
		_afterHandlers = afterHandlers.ToArray();
		_enrichers = enrichers;
	}
```

In `DispatchRaw`, load branch — change

```csharp
			value.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
			_enricher.Enrich(value, raw.Headers, timestamp);
			var buffer = _rawLoadBuffer ??= new ValueCompactingBuffer<TKey, TVlaue>(COMPACTING_BUFFER_CAPACITY);
```

to

```csharp
			value.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
			_enricher.Enrich(value, raw.Headers, timestamp);
			if (_enrichers.Length != 0)
				RunEnrichers(key, value, offset);
			var buffer = _rawLoadBuffer ??= new ValueCompactingBuffer<TKey, TVlaue>(COMPACTING_BUFFER_CAPACITY);
```

Live branch — change

```csharp
		liveValue.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
		_enricher.Enrich(liveValue, raw.Headers, timestamp);
		PublishRaw(RAW_KIND_UPDATE, key, liveValue, timestamp.UnixTimestampMs);
```

to

```csharp
		liveValue.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
		_enricher.Enrich(liveValue, raw.Headers, timestamp);
		if (_enrichers.Length != 0)
			RunEnrichers(key, liveValue, offset);
		PublishRaw(RAW_KIND_UPDATE, key, liveValue, timestamp.UnixTimestampMs);
```

Directly after `DispatchRaw` (before `PublishRawFiltered`), add:

```csharp
	/// <summary>
	///   Runs the host's enrichers over a value that is not yet in the cache. Kept out of <see cref="DispatchRaw" />
	///   and not inlined: a catch handler there would give that method's locals an EH write-through stack home,
	///   paid on every message by every consumer, enrichers or not. A throw is logged and the value keeps whatever
	///   the enrichers set before it; the remaining enrichers still run.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RunEnrichers(TKey key, TVlaue value, long offset) {
		foreach (var enricher in _enrichers)
			try {
				enricher.Enrich(key, value);
			} catch (Exception e) {
				_logger.EnricherError(e, Name, offset);
			}
	}
```

In `KafkaCacheConsumerLog`, after `ValueFilterError`, add:

```csharp
	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Enricher threw for {CacheName} - {Offset}; value stored as enriched so far")]
	public static partial void EnricherError(this ILogger logger, Exception exception, string cacheName, long offset);
```

- [ ] **Step 6: Build and run the unit tests**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~EnricherRegistrationTests"`
Expected: `Passed! - Failed: 0, Passed: 4` per target framework (net9.0 and net10.0).

Run: `dotnet test tests/Prague.Kafka.Tests`
Expected: everything still green (no other test constructs the handler directly; `Build` is the only call site).

---

### Task 2: Integration tests against a broker

**Files:**
- Test: `tests/Prague.Kafka.IntegrationTests/EnricherTests.cs`

**Interfaces:**
- Consumes: `ICacheEnricher<TKey, TValue>`, `WithEnricher<T>()`, `WithEnricher(Func<IServiceProvider, T>)` from Task 1; entities `FilterEntity` (`Id`, `Name`, `Value`, all settable) and `HeaderEnrichEntity` (`Id`, `Name`, `[DataCacheHeader] TenantId`, …) from `tests/Prague.Kafka.IntegrationTests/Entities`; `DualKafkaClusterFixture.CreateTopicAsync` / `NewProducer` / `BootstrapServersA`; `KafkaCachesLoader`.

- [ ] **Step 1: Write the tests**

Create `tests/Prague.Kafka.IntegrationTests/EnricherTests.cs`:

```csharp
namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Nanov.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
///   <c>WithEnricher</c> against a broker: the hook runs in both phases, after header enrichment, after the value
///   filters, in registration order, and a throw keeps the value as enriched so far.
/// </summary>
[TestFixture]
public class EnricherTests {
	private const string TopicPrefix = "it-enricher";

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	/// <summary>
	///   Runs whatever the test did. A failing test never reaches its own StopAsync call, and a consumer
	///   left alive stays a member of the group, which stalls later tests' initial loads.
	/// </summary>
	[TearDown]
	public async Task TearDownProviders() {
		foreach (var provider in _providers)
			try {
				await provider.GetRequiredService<IHostedService>().StopAsync(CancellationToken.None);
			}
			catch {
				// teardown must not mask the test's own failure
			}
			finally {
				(provider as IDisposable)?.Dispose();
			}

		_providers.Clear();
	}

	[Test]
	public async Task Enricher_RunsDuringTheInitialLoad() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var i = 1; i <= 3; i++)
				Produce(seeder, i, $"preload-{i}", i);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher<SuffixEnricher>());

		Assert.That(cache.Cache.Count, Is.EqualTo(3));
		for (var i = 1; i <= 3; i++) {
			Assert.That(cache.Cache.TryGet(i, out var value), Is.True);
			Assert.That(value!.Name, Is.EqualTo($"preload-{i}!"), "the load branch stores the enriched value");
		}

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_RunsInTheLivePhase() {
		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher<SuffixEnricher>());

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 7, "live", 7);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(7, out _));
		Assert.That(cache.Cache.TryGet(7, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("live!"), "the live branch stores the enriched value");

		await StopAsync(sp);
	}

	[Test]
	public async Task Enrichers_RunInRegistrationOrder_EachSeeingThePreviousChange() {
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithEnricher(_ => new AppendEnricher("A"))
			.WithEnricher(_ => new AppendEnricher("B")));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "x", 1);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("xAB"));

		await StopAsync(sp);
	}

	[Test]
	public async Task ThrowingEnricher_IsLogged_KeepsTheValueAsEnrichedSoFar_AndDoesNotStopTheOthers() {
		var logs = new CapturingLoggerProvider();
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithEnricher(_ => new AppendEnricher("1", throwForKey: 2))
			.WithEnricher(_ => new AppendEnricher("2")), logs);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "x", 1);
		Produce(producer, 2, "y", 2);
		Produce(producer, 3, "z", 3);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.Count == 3);
		Assert.That(cache.Cache.TryGet(1, out var first) && first!.Name == "x12", Is.True);
		Assert.That(cache.Cache.TryGet(2, out var second), Is.True);
		Assert.That(second!.Name, Is.EqualTo("y2"), "the first enricher threw before its change; the second still ran; the value is stored");
		Assert.That(cache.Cache.TryGet(3, out var third) && third!.Name == "z12", Is.True, "the next message is unaffected");
		Assert.That(logs.Errors.Count(m => m.Contains("Enricher threw")), Is.EqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_SeesTheHeaderEnrichedValue() {
		var (sp, cache) = await StartHeaderEntityAsync(b => b.WithEnricher<TenantIntoNameEnricher>());

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		var sent = new HeaderEnrichEntity { Id = 1, Name = "first", TenantId = 12345, EventType = "UserCreated" };
		var headers = new Headers();
		HeaderEnrichEntity.Derich(sent, headers);
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = headers
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("first@12345"), "the [DataCacheHeader] property is already set when the enricher runs");

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_DoesNotRunForAValueTheFilterRejected() {
		var seen = new SeenKeysEnricher();
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithValueFilter(static v => v.Value >= 0)
			.WithEnricher(_ => seen));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "rejected", -1);
		Produce(producer, 2, "accepted", 1);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(2, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(seen.Keys, Is.EquivalentTo(new[] { 2 }));

		await StopAsync(sp);
	}

	[Test]
	public async Task UnchangedEnrichment_IsSame_ChangedIsUpdate() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher<ValueFromIdEnricher>(), afterHandler: recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "same", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == 1));
		Assert.That(cache.Cache.TryGet(1, out var stored) && stored!.Value == 10, Is.True, "Value comes from the enricher");

		Produce(producer, 1, "same", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1));

		Produce(producer, 1, "renamed", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == 1));

		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1), Is.True,
			"an enrichment that reproduces the resident value reports Same");
		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == 1), Is.True);

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, int value) {
		var entity = new FilterEntity { Id = id, Name = name, Value = value };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartFilterEntityAsync(
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>> configure,
		CapturingLoggerProvider? logs = null,
		ICacheAfterHandler<int, FilterEntity>? afterHandler = null) {
		var services = NewServices(logs);
		if (afterHandler is not null)
			services.AddSingleton(afterHandler);
		services.AddKafkaCaches("KafkaConfig", b => configure(b.AddCache<FilterEntityCache, int, FilterEntity>(_topic)));
		var sp = await StartAsync(services);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private async Task<(IServiceProvider sp, HeaderEnrichEntityCache cache)> StartHeaderEntityAsync(
		Action<KafkaCacheHandlerBuilder<HeaderEnrichEntityCache, int, HeaderEnrichEntity>> configure) {
		var services = NewServices(null);
		services.AddKafkaCaches("KafkaConfig", b => configure(b.AddCache<HeaderEnrichEntityCache, int, HeaderEnrichEntity>(_topic)));
		var sp = await StartAsync(services);
		return (sp, sp.GetRequiredService<HeaderEnrichEntityCache>());
	}

	private static ServiceCollection NewServices(CapturingLoggerProvider? logs) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
				// Own group per provider: sharing one group.id across tests means each teardown
				// rebalances the group and can stall a neighbouring test's initial load.
				{ "KafkaConfig:ClientSettings:group.id", Guid.NewGuid().ToString() }
			})
			.Build();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging(logging => {
			if (logs is not null)
				logging.AddProvider(logs);
		});
		return services;
	}

	private async Task<IServiceProvider> StartAsync(ServiceCollection services) {
		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return sp;
	}

	private static async Task StopAsync(IServiceProvider sp) {
		var hosted = sp.GetRequiredService<IHostedService>();
		await hosted.StopAsync(CancellationToken.None);
		// Disposing cancels the consume loop, whose finally closes the consumer and leaves the group.
		(sp as IDisposable)?.Dispose();
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition()) {
			if (cts.IsCancellationRequested)
				return;
			await Task.Delay(50);
		}
	}

	private sealed class SuffixEnricher : ICacheEnricher<int, FilterEntity> {
		public void Enrich(int key, FilterEntity value) => value.Name += "!";
	}

	private sealed class AppendEnricher(string suffix, int? throwForKey = null) : ICacheEnricher<int, FilterEntity> {
		public void Enrich(int key, FilterEntity value) {
			if (key == throwForKey)
				throw new InvalidOperationException($"enricher {suffix} refuses key {key}");
			value.Name += suffix;
		}
	}

	private sealed class TenantIntoNameEnricher : ICacheEnricher<int, HeaderEnrichEntity> {
		public void Enrich(int key, HeaderEnrichEntity value) => value.Name = $"{value.Name}@{value.TenantId}";
	}

	private sealed class SeenKeysEnricher : ICacheEnricher<int, FilterEntity> {
		private readonly ConcurrentQueue<int> _keys = new();

		public IReadOnlyCollection<int> Keys => _keys;

		public void Enrich(int key, FilterEntity value) => _keys.Enqueue(key);
	}

	private sealed class ValueFromIdEnricher : ICacheEnricher<int, FilterEntity> {
		public void Enrich(int key, FilterEntity value) => value.Value = key * 10;
	}

	private sealed record Invocation(UpdateType UpdateType, int Key, FilterEntity? NewValue, FilterEntity? OldValue);

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<Invocation> _invocations = new();

		public bool Any(Func<Invocation, bool> predicate) => _invocations.Any(predicate);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_invocations.Enqueue(new Invocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>Collects Error-level log lines so a test can assert that a throw was reported exactly once.</summary>
	private sealed class CapturingLoggerProvider : ILoggerProvider {
		private readonly ConcurrentQueue<string> _errors = new();

		public IReadOnlyCollection<string> Errors => _errors;

		public ILogger CreateLogger(string categoryName) => new Logger(_errors);

		public void Dispose() { }

		private sealed class Logger(ConcurrentQueue<string> errors) : ILogger {
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
				if (logLevel >= LogLevel.Error)
					errors.Enqueue(formatter(state, exception));
			}
		}
	}
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test tests/Prague.Kafka.IntegrationTests --filter "FullyQualifiedName~EnricherTests"`
Expected: `Passed! - Failed: 0, Passed: 7` per target framework. Docker must be running; a socket error on every test means it is not.

If `HeaderEnrichEntityCache` or `FilterEntityCache` does not expose the generic parameters the way `AddCache<…>(_topic)` expects, copy the exact `AddCache` call from `AfterHandlerTests.StartAsync` / `EnrichmentTests` — those compile today.

- [ ] **Step 3: Run the whole integration assembly once**

Run: `dotnet test tests/Prague.Kafka.IntegrationTests`
Expected: all green; the handler constructor change must not have disturbed any other fixture.

---

### Task 3: Docs — context note, compiled sample, how-to article

**Files:**
- Modify: `context/kafka.md` (lines 10–12, the "Lifecycle & extension points" list)
- Create: `samples/Prague.Samples/Models/OrderSearchRow.cs`
- Create: `samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs`
- Create: `www/docs/articles/how-to/enrich-from-another-cache.md`
- Modify: `www/docs/toc.yml` (How-to Guides items, after "React to Changes" at line 38)
- Modify: `www/docs/index.md` (how-to list, after the "React to Changes" bullet at line 35)

**Interfaces:**
- Consumes: `ICacheEnricher<TKey, TValue>`, `WithEnricher<T>()`, `WithEnricher(Func<IServiceProvider, T>)` from Task 1; `Prague.Samples.Models.Customer` (`Id`, `Name`, `Tier` of enum `CustomerTier`) and its generated `CustomerCache`.
- Produces: `Prague.Samples.Models.OrderSearchRow` + generated `OrderSearchRowCache`; `Prague.Samples.HowTo.CustomerIntoSearchRow`.

- [ ] **Step 1: Extend `context/kafka.md`**

Replace the two bullets

```markdown
- Caches expose `ICacheAfterHandler`, `IEnrichable`, `IKafkaConfigurable` extension points.
- After-handlers fire on the **live phase only** (not during the initial load). Conditional updates (`UpdateType.Same`) suppress phantom churn — change detection compares structural equality (driven by `[DataCacheIgnoreEquality]`).
```

with

```markdown
- Caches expose `ICacheEnricher`, `ICacheAfterHandler`, `IEnrichable`, `IKafkaConfigurable` extension points.
- **Ingress order per accepted value:** tombstone check → key filters → value deserialization → value filters → `SetPragueMetadata` → the generated header/timestamp enricher → the host's `ICacheEnricher`s (`WithEnricher`, registration order, **both phases**) → load buffer / live ring. An enricher changes the not-yet-visible value in place, so indexes are built from the enriched value and the live `Same`/`Update` decision compares the enriched value. A throw is logged (`EnricherError`) and the value is stored as enriched so far. It runs on the consume thread: no I/O. Another cache read from an enricher may be partially loaded while the section is loading — topics and sections load concurrently (design: `docs/superpowers/specs/2026-11-03-kafka-cache-enrichers-design.md`).
- After-handlers fire on the **live phase only** (not during the initial load). Conditional updates (`UpdateType.Same`) suppress phantom churn — change detection compares structural equality (driven by `[DataCacheIgnoreEquality]`).
```

- [ ] **Step 2: Add the sample model**

Create `samples/Prague.Samples/Models/OrderSearchRow.cs`:

```csharp
// docs: the row the enrich-from-another-cache how-to fills in. A denormalised search row of an order: the
// producer writes the order fields, the consuming host fills CustomerName and CustomerTier from its own
// CustomerCache before the row is cached, so the Tier index is built from the host's view of the customer.
namespace Prague.Samples.Models;

using Prague.Core;

#region order-search-row
[DataCache]
public partial class OrderSearchRow {
	[DataCacheKey]
	public required string OrderId { get; init; }

	public required int CustomerId { get; init; }

	public required decimal Total { get; init; }

	// Filled by the host's ICacheEnricher, never by the producer: settable, not init-only.
	public string? CustomerName { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]                      // WithCustomerTier(...) over the ENRICHED value
	public CustomerTier CustomerTier { get; set; }
}
#endregion
```

- [ ] **Step 3: Add the compiled sample**

Create `samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs`:

```csharp
// docs: every code block of www/docs/articles/how-to/enrich-from-another-cache.md — an ICacheEnricher that
// fills OrderSearchRow from CustomerCache, its registration, and what the cache holds afterwards.
namespace Prague.Samples.HowTo;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Prague.Kafka;
using Prague.Samples.Models;

#region enrich-enricher
public sealed class CustomerIntoSearchRow(CustomerCache customers) : ICacheEnricher<string, OrderSearchRow> {
	public void Enrich(string key, OrderSearchRow row) {
		if (!customers.Cache.TryGet(row.CustomerId, out var customer))
			return;                                                // unknown customer: keep the producer's row as is

		row.CustomerName = customer.Name;                          // the row is not in the cache yet: change it in place
		row.CustomerTier = customer.Tier;
	}
}
#endregion

public static class EnrichFromAnotherCacheWiring {
	#region enrich-register
	public static void Register(KafkaCacheHandlersBuilder b) {
		b.AddCache<CustomerCache, int, Customer>();                // the cache the enricher reads
		b.AddCache<OrderSearchRowCache, string, OrderSearchRow>()
			.WithEnricher<CustomerIntoSearchRow>();                 // singleton ICacheEnricher<string, OrderSearchRow>, built by DI
	}
	#endregion
}

[TestFixture]
public class EnrichFromAnotherCache {
	[Test]
	public void TheIndexIsBuiltFromTheEnrichedValue() {
		var customers = new CustomerCache();
		customers.AddOrUpdate(new Customer { Id = 42, Email = "ada@example.com", Country = "GB", Tier = CustomerTier.Gold, Name = "Ada" });
		var rows = new OrderSearchRowCache();
		var enricher = new CustomerIntoSearchRow(customers);

		#region enrich-by-hand
		var row = new OrderSearchRow { OrderId = "ORD-001", CustomerId = 42, Total = 100m };   // what the producer sent
		enricher.Enrich(row.OrderId, row);                                                     // what the consumer does before storing
		rows.AddOrUpdate(row);                                                                 // stored and indexed as enriched

		using var gold = rows.Query().WithCustomerTier(CustomerTier.Gold).ExecutePooled();
		#endregion

		Assert.That(row.CustomerName, Is.EqualTo("Ada"));
		Assert.That(gold.Count, Is.EqualTo(1));
	}

	[Test]
	public void AnUnknownCustomerLeavesTheRowAsProduced() {
		var enricher = new CustomerIntoSearchRow(new CustomerCache());
		var row = new OrderSearchRow { OrderId = "ORD-002", CustomerId = 7, Total = 5m };

		enricher.Enrich(row.OrderId, row);

		Assert.That(row.CustomerName, Is.Null);
		Assert.That(row.CustomerTier, Is.EqualTo(default(CustomerTier)));
	}
}
```

If `Customer` has further `required` members than `Id`, `Email`, `Country`, `Tier`, `Name`, add them to the object initializer with any value — read `samples/Prague.Samples/Models/Customer.cs` first. If the generated query method is not named `WithCustomerTier`, open the generated file under `samples/Prague.Samples/obj` (or the IDE's generated-files view) and use the emitted name.

- [ ] **Step 4: Build and run the samples**

Run: `dotnet test samples/Prague.Samples --filter "FullyQualifiedName~EnrichFromAnotherCache"`
Expected: `Passed! - Failed: 0, Passed: 2` per target framework; the generator must have emitted `OrderSearchRowCache` and `WithCustomerTier`.

- [ ] **Step 5: Write the how-to article**

Create `www/docs/articles/how-to/enrich-from-another-cache.md`:

```markdown
---
title: Enrich from Another Cache
description: Fill fields of a consumed value from the host's own state with ICacheEnricher before Prague stores and indexes it, in both the initial load and the live phase.
---

# Enrich from Another Cache

**Goal:** a search row arrives with the fields its producer knew; before it is cached, fill the fields the *consumer* knows better — here the customer's name and tier from the host's own `CustomerCache` — so that the row's indexes are built from the consumer's view.

**Prerequisite:** two caches registered through `AddKafkaCaches` in one host. The code is in [`EnrichFromAnotherCache.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs); the registration block is compiled-only, the rest runs as tests.

## 1. Give the row somewhere to put the enriched fields

An enricher changes the value in place, so the fields it fills are settable, not `init`-only. An index on such a field is built from what the enricher wrote.

[!code-csharp[](../../../../samples/Prague.Samples/Models/OrderSearchRow.cs#order-search-row)]

## 2. Implement the enricher

`ICacheEnricher<TKey, TValue>.Enrich` is called for every value that passed the filters, in the initial load and in the live phase alike, after Prague filled the `[DataCacheHeader]` and `[DataCacheFromTimestamp]` properties and before the value enters the cache. Nothing else holds the instance yet.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-enricher)]

## 3. Register it

Type registration makes it a singleton `ICacheEnricher<string, OrderSearchRow>` built by the container, which is how it receives the `CustomerCache`; the factory form, `WithEnricher(sp => …)`, is for enrichers with arguments DI cannot supply. Several enrichers on one cache run in registration order, each seeing the previous one's changes.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-register)]

## 4. What the cache holds

The consumer is the only caller in production; a test is the other one. Enrich a row by hand, store it, and query the index that only the enriched value could satisfy.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-by-hand)]

In the live phase the cache compares the *enriched* value with the resident one, so a re-emitted message whose enrichment reproduces the resident row is reported as `Same` to after-handlers, and one that changes it as `Update`.

## Pitfalls

- **Blocking in the enricher.** It runs synchronously on the consume thread shared by every cache of the section, for every message of the initial replay too. A `TryGet` on another cache is the intended cost; no I/O, no locks that wait.
- **Reading a cache that is still loading.** Sections and topics load concurrently. While the host is loading, the cache an enricher reads may hold only part of its topic; a value enriched then keeps that view until the next message for its key. Treat a miss as "not known yet" rather than "does not exist" if that matters, and keep the producer's value in that case, as the sample does.
- **Throwing.** The throw is logged once per value (`EnricherError`) and the value is stored as enriched so far; the message is never dropped, and the enrichers after the one that threw still run. Fix the enricher; do not rely on the log to find stale rows.
- **Changing a key or a `[DataCacheIgnoreEquality]` field.** The key is already deserialized and is what the store uses; an enricher cannot move a value to another key. Fields excluded from equality do not make a re-emit an `Update`.
- **Expecting a re-run.** The hook runs on ingress only. A change in the cache it reads does not re-enrich rows that are already resident; that needs the next message for the row's key.
```

- [ ] **Step 6: Link the article from the TOC and the index**

In `www/docs/toc.yml`, after

```yaml
    - name: React to Changes
      href: articles/how-to/react-to-changes.md
```

add

```yaml
    - name: Enrich from Another Cache
      href: articles/how-to/enrich-from-another-cache.md
```

In `www/docs/index.md`, after the line

```markdown
- [**React to Changes**](articles/how-to/react-to-changes.md) — `ICacheAfterHandler` and every `UpdateType`.
```

add

```markdown
- [**Enrich from Another Cache**](articles/how-to/enrich-from-another-cache.md) — `ICacheEnricher`: fill a value from the host's state before it is stored and indexed.
```

- [ ] **Step 7: Verify every referenced region exists**

Run: `rg -n "#region (order-search-row|enrich-enricher|enrich-register|enrich-by-hand)" samples/Prague.Samples`
Expected: four matches, one per `[!code-csharp[]` reference in the article.

---

### Task 4: Whole-repository verification

**Files:** none new.

- [ ] **Step 1: Full build**

Run: `dotnet build Prague.sln`
Expected: `Build succeeded.` with `0 Warning(s)` for every project and both target frameworks.

- [ ] **Step 2: Full test filter**

Run: `dotnet test Prague.Tests.slnf`
Expected: every project green, including `Prague.Samples` (which the filter includes) and `Prague.Kafka.IntegrationTests` (Docker).

- [ ] **Step 3: Diff review**

Run: `git status --short` and `git diff --stat`
Expected: only the files named in Tasks 1–3 and 5 plus the spec and this plan; no `*.generated.cs`, no `*.DotSettings.user`, nothing under `bin/` or `obj/`. Leave everything uncommitted.

---

### Task 5: Bind enrichers to their cache (keyed registration)

Runs after Task 3 and before Task 4. Task 1 registered enrichers as plain `ICacheEnricher<TKey, TValue>` singletons, so two caches with the same key and value types shared every enricher. The registration becomes keyed by the cache type.

**Files:**
- Modify: `src/Prague.Kafka/DependencyInjection.cs` (the two `WithEnricher` overloads and `BuildEnrichers` added in Task 1)
- Modify: `tests/Prague.Kafka.Tests/DependencyInjection/EnricherRegistrationTests.cs`

**Interfaces:**
- Consumes: `ICacheEnricher<TKey, TValue>`, `WithEnricher<TEnricher>()`, `WithEnricher<TEnricher>(Func<IServiceProvider, TEnricher>)`, `BuildEnrichers(IServiceProvider)` from Task 1 — same names and signatures, only the registration changes.
- Produces: the enrichers of a cache are resolvable as `sp.GetKeyedServices<ICacheEnricher<TKey, TValue>>(typeof(TCacheEntity))`.

- [ ] **Step 1: Rewrite the registration tests for keyed services**

Replace the body of `tests/Prague.Kafka.Tests/DependencyInjection/EnricherRegistrationTests.cs` with:

```csharp
namespace Prague.Kafka.Tests.DependencyInjection;

using Baseline.Scenario;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Covers <c>WithEnricher</c>: both overloads register a singleton <see cref="ICacheEnricher{TKey,TValue}"/> keyed
///   by the cache type, registration resolves nothing, and <c>BuildEnrichers</c> hands the handler exactly this
///   cache's enrichers in registration order. Broker-free: the builder is constructible in-process and
///   <c>BuildEnrichers</c> exposes the built array.
/// </summary>
[TestFixture]
public class EnricherRegistrationTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private sealed class FirstEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	private sealed class SecondEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	[Test]
	public void TypeOverload_RegistersOneSingleton_KeyedByTheCacheType() {
		var services = new ServiceCollection();
		NewBuilder(services).WithEnricher<FirstEnricher>();

		using var provider = services.BuildServiceProvider();
		var keyed = provider.GetKeyedServices<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache)).ToArray();

		Assert.That(keyed, Has.Length.EqualTo(1));
		Assert.That(keyed[0], Is.TypeOf<FirstEnricher>());
		Assert.That(provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache)),
			Is.SameAs(keyed[0]), "the type overload registers a singleton");
		Assert.That(provider.GetServices<ICacheEnricher<int, BaselineProduct>>(), Is.Empty,
			"nothing lands in the unkeyed slot, so no other cache can pick it up");
	}

	[Test]
	public void FactoryOverload_ResolvesThroughTheFactory_Once() {
		var services = new ServiceCollection();
		var calls = 0;
		NewBuilder(services).WithEnricher(sp => {
			calls++;
			return new FirstEnricher();
		});

		Assert.That(calls, Is.Zero, "registration must not resolve anything");

		using var provider = services.BuildServiceProvider();
		var first = provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache));
		var second = provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache));

		Assert.That(calls, Is.EqualTo(1));
		Assert.That(second, Is.SameAs(first));
	}

	[Test]
	public void BuildEnrichers_ReturnsTheEnrichersInRegistrationOrder() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services)
			.WithEnricher<FirstEnricher>()
			.WithEnricher<SecondEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = builder.BuildEnrichers(provider);

		Assert.That(enrichers, Has.Length.EqualTo(2));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
		Assert.That(enrichers[1], Is.TypeOf<SecondEnricher>());
	}

	[Test]
	public void BuildEnrichers_IgnoresEnrichersRegisteredForAnotherCache() {
		var services = new ServiceCollection();
		// Another cache type with the same key and value types would register under its own type as the key;
		// any other key stands in for it here.
		services.AddKeyedSingleton<ICacheEnricher<int, BaselineProduct>, SecondEnricher>(typeof(string));
		var builder = NewBuilder(services).WithEnricher<FirstEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = builder.BuildEnrichers(provider);

		Assert.That(enrichers, Has.Length.EqualTo(1));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
	}

	[Test]
	public void BuildEnrichers_WithoutRegistrations_IsEmpty() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);

		using var provider = services.BuildServiceProvider();

		Assert.That(builder.BuildEnrichers(provider), Is.Empty);
	}
}
```

- [ ] **Step 2: Run the tests to verify the keyed ones fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~EnricherRegistrationTests"`
Expected: `TypeOverload_RegistersOneSingleton_KeyedByTheCacheType`, `FactoryOverload_ResolvesThroughTheFactory_Once` and `BuildEnrichers_IgnoresEnrichersRegisteredForAnotherCache` fail (no keyed service for the cache type; the unkeyed slot is not empty); the other two pass.

- [ ] **Step 3: Key the registration by the cache type**

In `src/Prague.Kafka/DependencyInjection.cs`, replace the two `WithEnricher` overloads and `BuildEnrichers` with:

```csharp
	/// <summary>
	///   Registers <typeparamref name="TEnricher" /> as a singleton <see cref="ICacheEnricher{TKey,TValue}" /> bound to
	///   this cache: the registration is keyed by <typeparamref name="TCacheEntity" />, so another cache with the
	///   same key and value types does not receive it. Several registrations run in registration order. See
	///   <see cref="ICacheEnricher{TKey,TValue}" /> for when and where it runs.
	/// </summary>
	/// <typeparam name="TEnricher">The enricher; the host's container constructs it.</typeparam>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>()
		where TEnricher : class, ICacheEnricher<TKey, TValue> {
		Services.AddKeyedSingleton<ICacheEnricher<TKey, TValue>, TEnricher>(typeof(TCacheEntity));
		return this;
	}

	/// <summary>
	///   Registers a singleton <see cref="ICacheEnricher{TKey,TValue}" /> bound to this cache and built by
	///   <paramref name="builder" />, for enrichers with constructor arguments the container cannot supply on its own.
	/// </summary>
	/// <typeparam name="TEnricher">The enricher.</typeparam>
	/// <param name="builder">Builds the enricher from the root provider, once.</param>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>(
		Func<IServiceProvider, TEnricher> builder)
		where TEnricher : class, ICacheEnricher<TKey, TValue> {
		Services.AddKeyedSingleton<ICacheEnricher<TKey, TValue>>(typeof(TCacheEntity), (sp, _) => builder(sp));
		return this;
	}
```

```csharp
	/// <summary>
	///   The enrichers registered for this cache, in registration order; empty when none. Keyed by the cache type,
	///   so a cache with the same key and value types in this or another section has its own set. Resolved once,
	///   when the handler is built — nothing is resolved on the ingestion path.
	/// </summary>
	internal ICacheEnricher<TKey, TValue>[] BuildEnrichers(IServiceProvider sp)
		=> sp.GetKeyedServices<ICacheEnricher<TKey, TValue>>(typeof(TCacheEntity)).ToArray();
```

- [ ] **Step 4: Build and run the unit tests**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~EnricherRegistrationTests"`
Expected: `Passed! - Failed: 0, Passed: 5` per target framework.

Run: `dotnet test tests/Prague.Kafka.IntegrationTests --filter "FullyQualifiedName~EnricherTests"`
Expected: `Passed! - Failed: 0, Passed: 7` per target framework — the integration tests register through the builder and must be unaffected (Docker).

- [ ] **Step 5: Align the docs that describe registration**

In `src/Prague.Kafka/ICacheEnricher.cs` the summary's first sentence ("Registered per cache with …") is now literally true; leave it. In `www/docs/articles/how-to/enrich-from-another-cache.md` (Task 3), section "3. Register it" must say the enricher is bound to this cache (keyed by the cache type) and the Pitfalls must not claim sharing by key/value type; fix the wording if Task 3 wrote otherwise.

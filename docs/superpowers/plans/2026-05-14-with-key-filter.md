# `WithKeyFilter` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `WithKeyFilter(Func<TKey, bool>)` to `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>` — a predicate over the deserialized key that runs inside the channel loop, drops the message (including tombstones, including during initial load), composes with AND across multiple calls, and on predicate-throw logs + treats as reject.

**Architecture:** Internal types `KafkaKeyFilters<TKey>` / `KafkaKeyFilter<TKey>` / `KafkaKeyPredicateFilter<TKey>` mirror the header-filter naming. They live on the generic `KafkaCacheHandler<TCacheEntity, TKey, TValue>` (not the non-generic base, since they are generic in `TKey`). Channel-loop call site is in `KafkaCacheHandler<...>.ChannelLoop`, immediately after `result.Message.Key.Dispose()`, before the `isLoading` branch — value bytes are disposed and (when not loading) `ExecuteAfterHandlersFilter()` fires. No-filter path is a single inlined `IsEmpty` field-length branch — zero overhead and zero allocations.

**Tech Stack:** .NET 9, C# `LangVersion=latest`, NUnit 4 (both `Prague.Kafka.Tests` and `Prague.Kafka.TestAdaptor.Tests` use NUnit), MessagePack-CSharp, Microsoft.Extensions.Logging source-gen, Confluent.Kafka.

**Reference spec:** `docs/superpowers/specs/2026-05-14-with-key-filter-design.md`.

**House style:** Tabs, width 2 indent. File-scoped namespaces with usings *inside* the namespace. K&R braces. `_camelCase` private fields. `var` everywhere. Apply `~/.claude/skills/high-performance-net/SKILL.md` on the channel-loop call site: no LINQ, no allocations per message, `try` body minimal, `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on `IsEmpty` / `ShouldProcess`.

**Note on test location vs spec:** The spec places pure unit tests in `tests/Prague.Kafka.Tests/Filters/`. Reality is that the existing `KafkaHeaderFiltersTests.cs` lives in `tests/Prague.Kafka.TestAdaptor.Tests/HeaderFilters/` (pure unit tests, no test-adaptor dependency — they just sit there for grouping). To match the established pattern this plan puts both unit and integration tests under `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/`.

---

## File Structure

**Created**
- `src/Prague.Kafka/Filters/KeyFilters.cs` — `KafkaKeyFilters<TKey>` + `KafkaKeyFilter<TKey>` + `KafkaKeyPredicateFilter<TKey>`. All `internal`.
- `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersTests.cs` — unit tests for the filter types.
- `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` — end-to-end tests through the test adaptor.

**Modified**
- `src/Prague.Kafka/DependencyInjection.cs` — new `_keyFilters` field on `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`, new `WithKeyFilter` method, pass `KafkaKeyFilters<TKey>.Create(_keyFilters)` to the handler ctor.
- `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` — new ctor parameter and `_keyFilters` field on `KafkaCacheHandler<TCacheEntity, TKey, TValue>`, filter block inserted in `ChannelLoop`, `KeyFilterError` `LoggerMessage` added to `KafkaCacheConsumerLog`.

---

## Task 1: Internal filter types (`KafkaKeyFilters<TKey>` + companions)

**Files:**
- Test: `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersTests.cs`
- Create: `src/Prague.Kafka/Filters/KeyFilters.cs`

- [ ] **Step 1: Write the failing unit tests**

Create the test file with the full content below:

```csharp
namespace Prague.Kafka.TestAdaptor.Tests.KeyFilters;

using Prague.Kafka.Filters;
using NUnit.Framework;

[TestFixture]
public class KafkaKeyFiltersTests {
	[Test]
	public void Create_WithNullList_ReturnsEmpty() {
		var filters = KafkaKeyFilters<int>.Create(null);
		Assert.That(filters.IsEmpty, Is.True);
	}

	[Test]
	public void Create_WithEmptyList_ReturnsEmpty() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>>());
		Assert.That(filters.IsEmpty, Is.True);
	}

	[Test]
	public void Create_WithOneFilter_IsNotEmpty() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0)
		});
		Assert.That(filters.IsEmpty, Is.False);
	}

	[Test]
	public void ShouldProcess_OnEmptyFilters_ReturnsTrue() {
		var filters = KafkaKeyFilters<int>.Create(null);
		Assert.That(filters.ShouldProcess(42), Is.True);
	}

	[Test]
	public void ShouldProcess_SinglePredicateTrue_ReturnsTrue() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0)
		});
		Assert.That(filters.ShouldProcess(1), Is.True);
	}

	[Test]
	public void ShouldProcess_SinglePredicateFalse_ReturnsFalse() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0)
		});
		Assert.That(filters.ShouldProcess(-1), Is.False);
	}

	[Test]
	public void ShouldProcess_TwoPredicatesBothTrue_ReturnsTrue() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0),
			new KafkaKeyPredicateFilter<int>(k => k < 100)
		});
		Assert.That(filters.ShouldProcess(50), Is.True);
	}

	[Test]
	public void ShouldProcess_TwoPredicatesSecondFalse_ReturnsFalse() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0),
			new KafkaKeyPredicateFilter<int>(k => k < 100)
		});
		Assert.That(filters.ShouldProcess(150), Is.False);
	}

	[Test]
	public void ShouldProcess_TwoPredicatesFirstFalse_ShortCircuits() {
		var secondCalled = false;
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(_ => false),
			new KafkaKeyPredicateFilter<int>(_ => { secondCalled = true; return true; })
		});
		Assert.That(filters.ShouldProcess(1), Is.False);
		Assert.That(secondCalled, Is.False);
	}

	[Test]
	public void PredicateFilter_PropagatesPredicateException() {
		var filter = new KafkaKeyPredicateFilter<int>(_ => throw new InvalidOperationException("boom"));
		Assert.That(() => filter.ShouldProcess(1), Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public void ShouldProcess_ReferenceTypeKeys_WorksWithEquality() {
		var filters = KafkaKeyFilters<string>.Create(new List<KafkaKeyFilter<string>> {
			new KafkaKeyPredicateFilter<string>(k => k.StartsWith("ok-"))
		});
		Assert.That(filters.ShouldProcess("ok-1"), Is.True);
		Assert.That(filters.ShouldProcess("no-1"), Is.False);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaKeyFiltersTests`
Expected: compile failure — `KafkaKeyFilters<>` / `KafkaKeyFilter<>` / `KafkaKeyPredicateFilter<>` do not exist yet.

- [ ] **Step 3: Implement the filter types**

Create `src/Prague.Kafka/Filters/KeyFilters.cs` with this exact content:

```csharp
namespace Prague.Kafka.Filters;

using System.Runtime.CompilerServices;

internal sealed class KafkaKeyFilters<TKey> {
	private static readonly KafkaKeyFilters<TKey> _empty = new(Array.Empty<KafkaKeyFilter<TKey>>());

	private readonly KafkaKeyFilter<TKey>[] _filters;

	private KafkaKeyFilters(KafkaKeyFilter<TKey>[] filters) {
		_filters = filters;
	}

	internal bool IsEmpty {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _filters.Length == 0;
	}

	internal static KafkaKeyFilters<TKey> Create(IReadOnlyList<KafkaKeyFilter<TKey>>? filters) {
		if (filters is null || filters.Count == 0)
			return _empty;
		var arr = new KafkaKeyFilter<TKey>[filters.Count];
		for (var i = 0; i < filters.Count; i++)
			arr[i] = filters[i];
		return new KafkaKeyFilters<TKey>(arr);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool ShouldProcess(TKey key) {
		foreach (var filter in _filters)
			if (!filter.ShouldProcess(key))
				return false;
		return true;
	}
}

internal abstract class KafkaKeyFilter<TKey> {
	public abstract bool ShouldProcess(TKey key);
}

internal sealed class KafkaKeyPredicateFilter<TKey> : KafkaKeyFilter<TKey> {
	private readonly Func<TKey, bool> _predicate;

	public KafkaKeyPredicateFilter(Func<TKey, bool> predicate) {
		_predicate = predicate;
	}

	public override bool ShouldProcess(TKey key) => _predicate(key);
}
```

`Prague.Kafka.TestAdaptor.Tests` already references `Prague.Kafka` and includes `[assembly: InternalsVisibleTo(...)]` (verify via `grep -rn "InternalsVisibleTo" src/Prague.Kafka/` — `Filters/HeaderFilters.cs` consumers in the test project compile, so the same wiring covers `KafkaKeyFilters<TKey>`). No project file edits required.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaKeyFiltersTests`
Expected: all 11 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/Filters/KeyFilters.cs tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersTests.cs
git commit -m "feat(filters): add KafkaKeyFilters<TKey> + KafkaKeyPredicateFilter<TKey>"
```

---

## Task 2: `WithKeyFilter` builder method

**Files:**
- Modify: `src/Prague.Kafka/DependencyInjection.cs` (`KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`)
- Test: `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` (first builder-level test only — the rest of the file lands in Task 4)

- [ ] **Step 1: Write the failing builder test**

Create `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` with this content:

```csharp
namespace Prague.Kafka.TestAdaptor.Tests.KeyFilters;

using Prague.Kafka;
using Prague.Kafka.TestAdaptor.Tests.TestEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class KafkaKeyFiltersIntegrationTests {
	private const string TestTopic = "key-filter-test-topic";
	private ServiceCollection _services = null!;
	private IKafkaCacheTestBuilderProvider _provider = null!;

	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private IServiceProvider BuildServiceProvider(Action<KafkaCacheHandlersBuilder> configure) {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "localhost:9092" }
			})
			.Build();

		_services.AddSingleton<IConfiguration>(configuration);
		_services.AddLogging();
		_services.AddKafkaCaches("KafkaConfig", configure);

		return _services.BuildServiceProvider();
	}

	[Test]
	public void WithKeyFilter_Builder_AcceptsPredicate_AndReturnsBuilder() {
		var sp = BuildServiceProvider(builder => {
			var b = builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 0);
			Assert.That(b, Is.Not.Null);
		});

		Assert.That(sp.GetRequiredService<KafkaCacheHandlers>(), Is.Not.Null);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaKeyFiltersIntegrationTests`
Expected: compile failure — `WithKeyFilter` does not exist on the builder.

- [ ] **Step 3: Add the builder field + method**

In `src/Prague.Kafka/DependencyInjection.cs`, inside class `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`:

Add a new field next to the existing `_filters` field (around line 34):

```csharp
private List<KafkaKeyFilter<TKey>>? _keyFilters;
```

Add this method just below the existing `WithHeaderNotEqualsFilter<THeaderValue>` (after the closing brace around line 224):

```csharp
/// <summary>
/// Allows messages to be filtered by the deserialized key using a custom predicate.
/// Multiple <c>WithKeyFilter</c> calls compose with AND. Predicate exceptions are caught
/// at the channel-loop call site, logged, and treated as a reject.
/// </summary>
/// <param name="predicate">Predicate receiving the deserialized <typeparamref name="TKey"/>; return true to keep, false to drop.</param>
/// <returns>The builder instance for chaining.</returns>
public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter(Func<TKey, bool> predicate) {
	_keyFilters ??= new List<KafkaKeyFilter<TKey>>();
	_keyFilters.Add(new KafkaKeyPredicateFilter<TKey>(predicate));
	return this;
}
```

The handler-construction site (`Build(...)` around line 238 in the same file) is updated in Task 3 — for now, the field is unused; the build remains green because `_keyFilters` is only assigned in the new method, and `Build()` ignores it.

- [ ] **Step 4: Run the test**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~WithKeyFilter_Builder_AcceptsPredicate`
Expected: 1 test passes.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/DependencyInjection.cs tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs
git commit -m "feat(filters): WithKeyFilter builder method on KafkaCacheHandlerBuilder"
```

---

## Task 3: Wire filters into the handler ctor

**Files:**
- Modify: `src/Prague.Kafka/DependencyInjection.cs` (`Build` method, lines ~238-244)
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` (`KafkaCacheHandler<TCacheEntity, TKey, TValue>` ctor + field, lines ~133-167)

This is a wiring-only refactor — no behavior change yet. Tests stay green; new behavior arrives in Task 4.

- [ ] **Step 1: Add the field + ctor parameter on `KafkaCacheHandler<TCacheEntity, TKey, TValue>`**

In `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`, find class `KafkaCacheHandler<TCacheEntity, TKey, TValue>` (around line 122).

Add a `using` at the top of the file if not already present:

```csharp
// already present near the top of the file:
using Filters;
```

(Verify by reading the existing usings — `Filters` is already imported because `KafkaCacheHandler` already references `KafkaHeaderFilters`.)

Add a new field near the other private readonly fields (around line 133, alongside `_afterHandlers`, `_cache`, etc.):

```csharp
private readonly KafkaKeyFilters<TKey> _keyFilters;
```

Modify the constructor signature (around line 145) — add `KafkaKeyFilters<TKey> keyFilters` after `KafkaHeaderFilters filtes`:

Before:

```csharp
public KafkaCacheHandler(
	TCacheEntity cache,
	KafkaDataCacheStatistics statistics,
	KafkaHeaderFilters filtes,
	IEnumerable<ICacheAfterHandler<TKey, TVlaue>> afterHandlers,
	ILogger logger) : base(filtes, Math.Min(_keyRingBufferCapacity, MAX_KEY_RING_BUFFER_SIZE)) {
```

After:

```csharp
public KafkaCacheHandler(
	TCacheEntity cache,
	KafkaDataCacheStatistics statistics,
	KafkaHeaderFilters filtes,
	KafkaKeyFilters<TKey> keyFilters,
	IEnumerable<ICacheAfterHandler<TKey, TVlaue>> afterHandlers,
	ILogger logger) : base(filtes, Math.Min(_keyRingBufferCapacity, MAX_KEY_RING_BUFFER_SIZE)) {
```

Inside the ctor body, add the assignment alongside the other field initializations (e.g., immediately after `_cache = cache;`):

```csharp
_keyFilters = keyFilters;
```

- [ ] **Step 2: Pass `KafkaKeyFilters<TKey>` from the builder**

In `src/Prague.Kafka/DependencyInjection.cs`, find the `Build` method on `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>` (around line 227).

Update the `new KafkaCacheHandler<TCacheEntity, TKey, TValue>(...)` invocation to pass `KafkaKeyFilters<TKey>.Create(_keyFilters)`:

Before:

```csharp
return new KeyValuePair<string, KafkaCacheHandler>(topicName,
	new KafkaCacheHandler<TCacheEntity, TKey, TValue>(
		cache,
		new KafkaDataCacheStatistics(topicName, cache.Statistics),
		KafkaHeaderFilters.Create(_filters),
		sp.GetServices<ICacheAfterHandler<TKey, TValue>>(),
		sp.GetRequiredService<ILogger<KafkaCacheHandler<TCacheEntity, TKey, TValue>>>()));
```

After:

```csharp
return new KeyValuePair<string, KafkaCacheHandler>(topicName,
	new KafkaCacheHandler<TCacheEntity, TKey, TValue>(
		cache,
		new KafkaDataCacheStatistics(topicName, cache.Statistics),
		KafkaHeaderFilters.Create(_filters),
		KafkaKeyFilters<TKey>.Create(_keyFilters),
		sp.GetServices<ICacheAfterHandler<TKey, TValue>>(),
		sp.GetRequiredService<ILogger<KafkaCacheHandler<TCacheEntity, TKey, TValue>>>()));
```

- [ ] **Step 3: Build the solution**

Run: `dotnet build Prague.sln`
Expected: build succeeds with no warnings related to these files.

- [ ] **Step 4: Run the existing tests to verify no regressions**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests`
Expected: all pre-existing tests pass; `KafkaKeyFiltersTests` (11 tests) and `WithKeyFilter_Builder_AcceptsPredicate_AndReturnsBuilder` (1 test) pass.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/DependencyInjection.cs src/Prague.Kafka/IO/KafkaCacheConsumer.cs
git commit -m "refactor(filters): thread KafkaKeyFilters<TKey> through handler ctor"
```

---

## Task 4: Channel-loop integration + `KeyFilterError` log

**Files:**
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` (`ChannelLoop` method ~line 256, `KafkaCacheConsumerLog` partial ~line 552)
- Test: `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` (append tests)

- [ ] **Step 1: Write the failing integration tests**

Append these tests to `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` (inside the existing class, before the closing brace). Also add the `using`s and the helper methods at the top of the class if missing:

Add to the existing `using` block at the top of the file:

```csharp
using System.Collections.Concurrent;
using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Hosting;
```

Add these helper members inside the class (after `BuildServiceProvider`):

```csharp
private void ProduceEntity(int id, string name) {
	var keyBytes = MessagePackSerializer.Serialize(id);
	var entity = new TestEntityWithLongTimestamp {
		Id = id,
		Name = name
	};
	var valueBytes = MessagePackSerializer.Serialize(entity);

	KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
		Message = new Message<byte[], byte[]> {
			Key = keyBytes,
			Value = valueBytes,
			Headers = new Headers()
		}
	});
}

private void ProduceDelete(int id) {
	var keyBytes = MessagePackSerializer.Serialize(id);

	KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
		Message = new Message<byte[], byte[]> {
			Key = keyBytes,
			Value = null!,
			Headers = new Headers()
		}
	});
}

private sealed class RecordingAfterHandler : ICacheAfterHandler<int, TestEntityWithLongTimestamp> {
	public ConcurrentBag<UpdateType> Updates { get; } = new();
	public ConcurrentBag<int> Keys { get; } = new();

	public ValueTask Handle(UpdateType updateType, int key, TestEntityWithLongTimestamp? newValue, TestEntityWithLongTimestamp? oldValue) {
		Updates.Add(updateType);
		Keys.Add(key);
		return ValueTask.CompletedTask;
	}
}
```

Append the test methods (still inside the `KafkaKeyFiltersIntegrationTests` class):

```csharp
[Test]
public async Task WithKeyFilter_RejectsKeysFailingPredicate_DuringInitialLoad() {
	// Pre-load five entities; only ids > 2 should pass the filter.
	for (var i = 1; i <= 5; i++)
		ProduceEntity(i, $"entity-{i}");

	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => k > 2);
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(300);

	var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
	Assert.That(cache.TryGet(1, out _), Is.False);
	Assert.That(cache.TryGet(2, out _), Is.False);
	Assert.That(cache.TryGet(3, out _), Is.True);
	Assert.That(cache.TryGet(4, out _), Is.True);
	Assert.That(cache.TryGet(5, out _), Is.True);

	await hosted.StopAsync(cts.Token);
}

[Test]
public async Task WithKeyFilter_RejectsKeysFailingPredicate_LivePhase() {
	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => k > 2);
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(150);

	// Live phase
	for (var i = 1; i <= 5; i++)
		ProduceEntity(i, $"entity-{i}");
	await Task.Delay(300);

	var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
	Assert.That(cache.TryGet(1, out _), Is.False);
	Assert.That(cache.TryGet(2, out _), Is.False);
	Assert.That(cache.TryGet(3, out _), Is.True);
	Assert.That(cache.TryGet(4, out _), Is.True);
	Assert.That(cache.TryGet(5, out _), Is.True);

	await hosted.StopAsync(cts.Token);
}

[Test]
public async Task WithKeyFilter_MultiplePredicates_ComposeWithAnd() {
	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => k > 1)
			.WithKeyFilter(k => k < 4);
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(150);

	for (var i = 1; i <= 5; i++)
		ProduceEntity(i, $"entity-{i}");
	await Task.Delay(300);

	var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
	Assert.That(cache.TryGet(1, out _), Is.False);
	Assert.That(cache.TryGet(2, out _), Is.True);
	Assert.That(cache.TryGet(3, out _), Is.True);
	Assert.That(cache.TryGet(4, out _), Is.False);
	Assert.That(cache.TryGet(5, out _), Is.False);

	await hosted.StopAsync(cts.Token);
}

[Test]
public async Task WithKeyFilter_FiresAfterHandlerFilter_OnLiveRejection() {
	var recording = new RecordingAfterHandler();
	_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => k > 2);
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(150);

	ProduceEntity(1, "rejected"); // fails predicate
	ProduceEntity(3, "accepted"); // passes
	await Task.Delay(300);

	Assert.That(recording.Updates.Count(u => u == UpdateType.Filtered), Is.GreaterThanOrEqualTo(1));
	Assert.That(recording.Updates.Count(u => u == UpdateType.Add), Is.GreaterThanOrEqualTo(1));

	await hosted.StopAsync(cts.Token);
}

[Test]
public async Task WithKeyFilter_TombstoneForFilteredKey_IsDropped() {
	var recording = new RecordingAfterHandler();
	_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => k > 2);
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(150);

	ProduceDelete(1); // key fails predicate; tombstone must not produce Delete or touch cache
	await Task.Delay(300);

	Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.EqualTo(0));

	await hosted.StopAsync(cts.Token);
}

[Test]
public async Task WithKeyFilter_PredicateThrows_IsTreatedAsReject_AndLoopKeepsRunning() {
	var sp = BuildServiceProvider(builder => {
		builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
			.WithKeyFilter(k => {
				if (k == 7)
					throw new InvalidOperationException("intentional");
				return true;
			});
	});

	var hosted = sp.GetRequiredService<IHostedService>();
	using var cts = new CancellationTokenSource();
	await hosted.StartAsync(cts.Token);
	await Task.Delay(150);

	ProduceEntity(7, "throws"); // predicate throws — treated as reject
	ProduceEntity(8, "ok");     // predicate succeeds — accepted
	await Task.Delay(300);

	var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
	Assert.That(cache.TryGet(7, out _), Is.False);
	Assert.That(cache.TryGet(8, out _), Is.True);

	await hosted.StopAsync(cts.Token);
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaKeyFiltersIntegrationTests`
Expected: the original `WithKeyFilter_Builder_AcceptsPredicate_AndReturnsBuilder` test still passes; the six new tests fail (cache still admits all keys / tombstones still process / etc.) because the channel-loop site has not been added yet.

- [ ] **Step 3: Add the channel-loop filter block**

In `src/Prague.Kafka/IO/KafkaCacheConsumer.cs`, find the `ChannelLoop` method on `KafkaCacheHandler<TCacheEntity, TKey, TValue>` (around line 256). Locate this exact region (current line 281-297):

Before:

```csharp
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
```

After (insert the new block between `result.Message.Key.Dispose();` and `if (isLoading) {`):

```csharp
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

if (!_keyFilters.IsEmpty) {
	bool shouldProcess;
	try {
		shouldProcess = _keyFilters.ShouldProcess(key);
	} catch (Exception e) {
		_logger.KeyFilterError(e, Name, result.Offset);
		shouldProcess = false;
	}
	if (!shouldProcess) {
		result.Message.Value.Dispose();
		if (!isLoading)
			await ExecuteAfterHandlersFilter();
		continue;
	}
}

if (isLoading) {
	buffer!.Add(key, result);
	if (buffer.IsFull)
		FlushBuffer(buffer);
	continue;
}
```

- [ ] **Step 4: Add the `KeyFilterError` logger source-gen method**

In the same file, find the `KafkaCacheConsumerLog` static partial class (around line 552). Add this method anywhere inside the class (group it near the other handler-side log methods, e.g. after `ErrorDeserializingKey` around line 571):

```csharp
[LoggerMessage(Level = LogLevel.Error,
	Message = "[Prague] Key filter predicate threw for {CacheName} - {Offset}")]
public static partial void KeyFilterError(this ILogger logger, Exception exception, string cacheName, long offset);
```

- [ ] **Step 5: Run the integration tests**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaKeyFiltersIntegrationTests`
Expected: all 7 tests in `KafkaKeyFiltersIntegrationTests` pass.

- [ ] **Step 6: Run the full Kafka test suites to verify no regressions**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests && dotnet test tests/Prague.Kafka.Tests`
Expected: all tests pass in both projects.

- [ ] **Step 7: Commit**

```bash
git add src/Prague.Kafka/IO/KafkaCacheConsumer.cs tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs
git commit -m "feat(filters): apply WithKeyFilter in channel loop with KeyFilterError log"
```

---

## Task 5: Whole-solution build + smoke

- [ ] **Step 1: Solution build**

Run: `dotnet build Prague.sln`
Expected: build succeeds; no new warnings.

- [ ] **Step 2: Run the full test slice**

Run: `dotnet test Prague.Tests.slnf`
Expected: every test project green.

- [ ] **Step 3: No commit if step 1 & 2 produced no changes; otherwise commit any minor fixups discovered**

```bash
# only if changes were made during this task
git add -A && git commit -m "chore: post-merge fixups"
```

---

## Self-review (done by plan author)

**Spec coverage:**
- Public API → Task 2.
- Internal types (`KafkaKeyFilters<TKey>` + `KafkaKeyFilter<TKey>` + `KafkaKeyPredicateFilter<TKey>`) → Task 1.
- Builder field + `Build()` change → Tasks 2 & 3.
- Handler ctor + field → Task 3.
- Channel-loop integration block + `KeyFilterError` log → Task 4.
- Semantics: initial-load filtering → Task 4 (`WithKeyFilter_RejectsKeysFailingPredicate_DuringInitialLoad`). Live-phase + after-handler-filter notification → Task 4 (`WithKeyFilter_FiresAfterHandlerFilter_OnLiveRejection`). Tombstones → Task 4 (`WithKeyFilter_TombstoneForFilteredKey_IsDropped`). Predicate-throw policy → Task 4 (`WithKeyFilter_PredicateThrows_IsTreatedAsReject_AndLoopKeepsRunning`). AND composition → Task 4 (`WithKeyFilter_MultiplePredicates_ComposeWithAnd`) plus Task 1 unit tests.

**Placeholder scan:** No TBD / TODO / "implement later" in code or instructions. Every code block is the exact text to write or replace.

**Type consistency:** Field is `_keyFilters` everywhere. Type is `KafkaKeyFilters<TKey>`. Method is `WithKeyFilter`. Log method is `KeyFilterError`. Test class is `KafkaKeyFiltersIntegrationTests`. All consistent.

**Existing-name note:** The handler's third ctor parameter remains misspelled `filtes` (existing code style — not changed in this plan). The new fourth parameter is `keyFilters` (correct spelling).

# Forced Writes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A write that bypasses the `CacheEquals` gate on the producing process and on every consumer, carried by an `X-Prague-Force` header.

**Architecture:** `InMemoryDataCache.AddOrUpdate(key, value, timestamp, force, out old)` (originally drafted as a separate `ForceAddOrUpdate`, see the revision note) is a write entry point with an always-true store predicate; the generated caches and the Kafka producer extensions expose it behind `force: true`; the producer stamps the header; the consumer reads it in the header gate it already runs and routes the message through the unconditional write on both the live and the load path. Spec: [`docs/superpowers/specs/2026-09-30-force-write-design.md`](../specs/2026-09-30-force-write-design.md).

**Tech Stack:** .NET 9/10, NUnit, Roslyn source generator (`Prague.Codegen`), `Nanov.Confluent.Kafka` raw API, Testcontainers (integration tests only).

> **Revision 2026-09-30 (after review):** the separate `ForceAddOrUpdate` methods shown in Tasks 1, 2 and 5 were replaced by one overload, `AddOrUpdate(key, value, timestamp, bool force, out oldValue)` (interface: `AddOrUpdate(document, timestamp, force, out old)`; generated caches emit only that shape); `AddOrUpdateAndProduce` now does `AddOrUpdate(key, document, now, force, out _)` and produces with `force` when it reports a change. Test fixtures are `Cache/AddOrUpdateForceTests.cs` and `Kafka/AddOrUpdateForceTests.cs`. The code blocks below are the superseded shape; the spec is authoritative.

## Global Constraints

- Deliver as **uncommitted edits** in `D:\Work\prague.net-fork` — no commits, no pushes (standing instruction for this fork). "Commit" steps are replaced by build/test checkpoints.
- `TreatWarningsAsErrors` is on; every project multi-targets `net9.0;net10.0`.
- `code-style` and `high-performance-net` skills apply to every `*.cs` touched.
- Never hand-edit `*.generated.cs` / `*.g.cs` — change `CacheGenerator.cs`.
- Header name is exactly `X-Prague-Force`; value is ASCII `1`; presence is the signal.
- The unforced write path stays byte-identical in behaviour; the only structural change to it is the extracted `ApplyToIndexes` helper, which is measured (Task 7).
- `Prague.Kafka.IntegrationTests` need Docker. Without it ~55 tests fail on the socket instantly; that is the only cause.

---

### Task 1: `InMemoryDataCache.ForceAddOrUpdate` + `IDataCache` surface

**Files:**
- Modify: `src/Prague.Core/InMemoryDataCache.cs:1443-1496` (the two `AddOrUpdate` bodies)
- Modify: `src/Prague.Core/IDataCacheEntity.cs:73-91` (`IDataCache<TKey, TValue>`)
- Create: `tests/Prague.Core.Tests/Cache/ForceAddOrUpdateTests.cs`

**Interfaces:**
- Produces: `InMemoryDataCache<TKey,TValue>.ForceAddOrUpdate(TKey key, TValue value[, long timestamp][, out TValue? oldValue]) : bool` (four overloads, always `true`); `IDataCache<TKey,TValue>.ForceAddOrUpdate(TValue document)`, `(document, out TValue? value)`, `(document, long timestamp)`, `(document, long timestamp, out TValue? value)`.

- [ ] **Step 1: Write the failing tests**

`tests/Prague.Core.Tests/Cache/ForceAddOrUpdateTests.cs`:

```csharp
namespace Prague.Core.Tests.Cache;

using Prague.Core;

/// <summary>
///   <c>ForceAddOrUpdate</c> is the unconditional write: it replaces the resident value even when
///   <c>CacheEquals</c> says the two are equal, walks every index, and never reports <c>Same</c>.
///   <c>AddOrUpdate</c> keeps its conditional contract; the last test pins that as the baseline.
/// </summary>
[TestFixture]
public class ForceAddOrUpdateTests {
	private sealed class Entity : ICacheEquatable<Entity>, ICacheClonable<Entity> {
		public int Id { get; init; }
		public string Name { get; init; } = "";
		public int Group { get; init; }

		public bool CacheEquals(Entity? other)
			=> other is not null && Id == other.Id && Name == other.Name && Group == other.Group;

		public int CacheGetHashCode() => HashCode.Combine(Id, Name, Group);

		public Entity Clone() => new() { Id = Id, Name = Name, Group = Group };
	}

	private static long T(int minutes) => minutes * 60_000L;

	private static Entity Same() => new() { Id = 1, Name = "a", Group = 7 };

	[Test]
	public void EqualValue_ReplacesTheResidentReference() {
		var cache = new InMemoryDataCache<int, Entity>();
		var first = Same();
		var second = Same();
		cache.AddOrUpdate(1, first, T(1));

		var changed = cache.ForceAddOrUpdate(1, second, T(2), out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.True);
			Assert.That(old, Is.SameAs(first));
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(second));
		});
	}

	[Test]
	public void EqualValue_RefreshesLastUpdated() {
		var cache = new InMemoryDataCache<int, Entity>();
		var lastUpdated = new LastUpdatedIndex<int>();
		cache.CacheLastUpdatedIndex(lastUpdated, static (_, e) => e.Group);
		cache.AddOrUpdate(1, Same(), T(1));

		cache.ForceAddOrUpdate(1, Same(), T(2));

		Assert.Multiple(() => {
			Assert.That(lastUpdated.TryGetLastUpdated(7, out var ts), Is.True);
			Assert.That(ts, Is.EqualTo(T(2)));
			Assert.That(lastUpdated.GetEntitiesCount(7), Is.EqualTo(1), "a forced update must not double-count the entity");
		});
	}

	[Test]
	public void EqualValue_LeavesKeySetAndUniqueIndexesResolving() {
		var cache = new InMemoryDataCache<int, Entity>();
		var named = cache.AddKeySetIndex(static (_, e) => e.Name.Length > 0);
		var byGroup = cache.AddKeyValueIndex(static (_, e) => e.Group);
		cache.AddOrUpdate(1, Same(), T(1));

		cache.ForceAddOrUpdate(1, Same(), T(2));

		Assert.Multiple(() => {
			Assert.That(named.Contains(1), Is.True);
			Assert.That(named.ApproximateCount, Is.EqualTo(1));
			Assert.That(byGroup.TryGetValue(7, out var key), Is.True);
			Assert.That(key, Is.EqualTo(1));
		});
	}

	[Test]
	public void AbsentKey_IsAdded() {
		var cache = new InMemoryDataCache<int, Entity>();
		var byGroup = cache.AddKeyValueIndex(static (_, e) => e.Group);
		var value = Same();

		var changed = cache.ForceAddOrUpdate(1, value, T(1), out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.True);
			Assert.That(old, Is.Null);
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(value));
			Assert.That(byGroup.TryGetValue(7, out var key), Is.True);
			Assert.That(key, Is.EqualTo(1));
		});
	}

	[Test]
	public void AddOrUpdate_EqualValue_StaysConditional() {
		var cache = new InMemoryDataCache<int, Entity>();
		var lastUpdated = new LastUpdatedIndex<int>();
		cache.CacheLastUpdatedIndex(lastUpdated, static (_, e) => e.Group);
		var first = Same();
		cache.AddOrUpdate(1, first, T(1));

		var changed = cache.AddOrUpdate(1, Same(), T(2), out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.False);
			Assert.That(old, Is.Null);
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(first));
			Assert.That(lastUpdated.TryGetLastUpdated(7, out var ts), Is.True);
			Assert.That(ts, Is.EqualTo(T(1)));
		});
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Prague.Core.Tests -f net9.0 --filter FullyQualifiedName~ForceAddOrUpdateTests`
Expected: build error CS1061 — `InMemoryDataCache<int, Entity>` has no `ForceAddOrUpdate`.

- [ ] **Step 3: Implement**

In `src/Prague.Core/InMemoryDataCache.cs`, replace the two bodies at lines 1443-1496 (`AddOrUpdate(key, value, timestamp, out oldValue)` through the end of `AddOrUpdate(key, value, timestamp)`) with:

```csharp
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value, long timestamp, out TValue? oldValue) {
		var r = _cache.AddOrUpdate(key,
			value,
			static (_, ov, nv) => !ov!.CacheEquals(nv));

		if (r.Operation is AddOrUpdateOperation.Same) {
			oldValue = default;
			return false;
		}

		ApplyToIndexes(key, in r, timestamp);
		oldValue = r.OldValue;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value) {
		return AddOrUpdate(key, value, DateTimeOffset.Now.ToUnixTimeMilliseconds());
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value, long timestamp) {
		var r = _cache.AddOrUpdate(key,
			value,
			static (_, ov, nv) => !ov!.CacheEquals(nv));

		if (r.Operation is AddOrUpdateOperation.Same)
			return false;

		ApplyToIndexes(key, in r, timestamp);
		return true;
	}

	/// <summary>
	///   Unconditional write: replaces the resident value even when <c>CacheEquals</c> says the two are
	///   equal, so every index sees an <c>Update</c> and the <c>LastUpdated</c> adapters refresh their
	///   timestamp. Returns whether the cache changed, as <see cref="AddOrUpdate(TKey,TValue,long,out TValue)"/>
	///   does — which here is always <c>true</c>, so a call site can switch between the two without
	///   changing how it reads the result. <paramref name="oldValue"/> is <c>null</c> when the key was added.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ForceAddOrUpdate(TKey key, TValue value, out TValue? oldValue) {
		return ForceAddOrUpdate(key, value, DateTimeOffset.Now.ToUnixTimeMilliseconds(), out oldValue);
	}

	/// <inheritdoc cref="ForceAddOrUpdate(TKey,TValue,out TValue)"/>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool ForceAddOrUpdate(TKey key, TValue value, long timestamp, out TValue? oldValue) {
		var r = _cache.AddOrUpdate(key, value, static (_, _, _) => true);
		ApplyToIndexes(key, in r, timestamp);
		oldValue = r.OldValue;
		return true;
	}

	/// <inheritdoc cref="ForceAddOrUpdate(TKey,TValue,out TValue)"/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ForceAddOrUpdate(TKey key, TValue value) {
		return ForceAddOrUpdate(key, value, DateTimeOffset.Now.ToUnixTimeMilliseconds());
	}

	/// <inheritdoc cref="ForceAddOrUpdate(TKey,TValue,out TValue)"/>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool ForceAddOrUpdate(TKey key, TValue value, long timestamp) {
		var r = _cache.AddOrUpdate(key, value, static (_, _, _) => true);
		ApplyToIndexes(key, in r, timestamp);
		return true;
	}

	// The store has already committed the write; fan it out to the indexes in registration order. An
	// Update whose OldValue is null is applied as an Add — no index has seen the key yet.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ApplyToIndexes(TKey key, in ConcurrentCacheStore<TKey, TValue>.UpdateResult r, long timestamp) {
		StatisticsCollector.Performed(r.Operation);

		foreach (var index in _indeces)
			if (r.Operation is AddOrUpdateOperation.Update && r.OldValue is not null)
				index.Update(key, r.KeyHash, r.OldValue, r.Value, timestamp);
			else
				index.Add(key, r.KeyHash, r.Value, timestamp);
	}
```

In `src/Prague.Core/IDataCacheEntity.cs`, after `bool AddOrUpdate(TValue document, long timestamp, out TValue? value);` (line 83) add:

```csharp

	/// <summary>Unconditional write — see <see cref="InMemoryDataCache{TKey,TValue}.ForceAddOrUpdate(TKey,TValue,long,out TValue)"/>.</summary>
	void ForceAddOrUpdate(TValue document);

	bool ForceAddOrUpdate(TValue document, out TValue? value);

	void ForceAddOrUpdate(TValue document, long timestamp);

	bool ForceAddOrUpdate(TValue document, long timestamp, out TValue? value);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Prague.Core.Tests -f net9.0 --filter FullyQualifiedName~ForceAddOrUpdateTests`
Expected: 5 passed.

Note: `dotnet build Prague.sln` will now fail on every generated cache (interface member not implemented) until Task 2 lands — that is expected; run only the Core test project here.

---

### Task 2: Codegen — `ForceAddOrUpdate` on generated caches, `force` on producer extensions

**Files:**
- Modify: `src/Prague.Codegen/CacheGenerator.cs:2479-2503` (`GenerateProducerHelper`), `:2505-2591` (`GenerateProducerExtensions`), `:7580-7616` (`GenerateCacheInterfaceMethods`)
- Create: `tests/Prague.Generated.Tests/Kafka/ForceAddOrUpdateTests.cs`

**Interfaces:**
- Consumes: Task 1's `InMemoryDataCache.ForceAddOrUpdate`; Task 3's `KafkaCacheProducer.Produce(topic, key, value, bool force)` (the generated call compiles only after Task 3 — do Task 3 before building the solution).
- Produces: generated `XxxCache.ForceAddOrUpdate(document[, timestamp][, out old])`; `CacheMarshall.Produce(cache, key, value, bool force = false)`; `AddOrUpdateAndProduce(this XxxCache cache, Xxx document, bool force = false)`; `XxxCacheProducerHelper.Produce(key, value, bool force = false)`.

- [ ] **Step 1: Write the failing test**

`tests/Prague.Generated.Tests/Kafka/ForceAddOrUpdateTests.cs`:

```csharp
namespace Prague.Generated.Tests.Kafka;

using NUnit.Framework;
using Prague.Generated.Tests.TestEntities;
using Prague.Kafka;

/// <summary>
///   The generator emits the unconditional write next to the conditional one, on the cache and on the
///   Kafka extension. Broker-free: no producer is configured, so on the forced path the produce step is
///   the throw — and it comes after the local write, which is the ordering the extension promises.
/// </summary>
[TestFixture]
public class ForceAddOrUpdateTests {
	[Test]
	public void GeneratedCache_ForceAddOrUpdate_ReplacesAnEqualDocument() {
		var cache = new TestEntityWithoutTimestampCache();
		var first = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		var second = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);

		Assert.That(cache.AddOrUpdate(second, out _), Is.False, "precondition: the documents are cache-equal");
		Assert.That(cache.ForceAddOrUpdate(second, out var old), Is.True);
		Assert.Multiple(() => {
			Assert.That(old, Is.SameAs(first));
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(second));
		});
	}

	[Test]
	public void AddOrUpdateAndProduce_Force_WritesLocallyThenProduces() {
		var cache = new TestEntityWithoutTimestampCache();
		var first = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		var second = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);

		// Unforced and equal: nothing changes and nothing is produced, so the missing producer is never reached.
		Assert.DoesNotThrow(() => cache.AddOrUpdateAndProduce(second));
		Assert.That(cache.TryGet(1, out var kept), Is.True);
		Assert.That(kept, Is.SameAs(first));

		// Forced: the local write lands first, then the produce step reaches the unconfigured producer.
		Assert.Throws<InvalidOperationException>(() => cache.AddOrUpdateAndProduce(second, force: true));
		Assert.That(cache.TryGet(1, out var replaced), Is.True);
		Assert.That(replaced, Is.SameAs(second));
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build tests/Prague.Generated.Tests -f net9.0`
Expected: CS0535 (generated caches do not implement `IDataCache<,>.ForceAddOrUpdate`) and CS1061 in the new test.

- [ ] **Step 3: Implement — cache interface methods**

In `GenerateCacheInterfaceMethods`, after the `AddOrUpdate(document, timestamp, out oldDocument)` emission (ends at line 7615) and before `// Remove overloads`, insert:

```csharp
		// ForceAddOrUpdate overloads — the unconditional write, same shapes as AddOrUpdate.
		var keyAccessor = keyPropertyName != null ? $"document.{keyPropertyName}" : "document.GetKey()";

		sb.AppendLine();
		sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
		sb.AppendLine($"        public void ForceAddOrUpdate({documentTypeName} document)");
		sb.AppendLine("            => ForceAddOrUpdate(document, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());");

		sb.AppendLine();
		sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
		sb.AppendLine($"        public void ForceAddOrUpdate({documentTypeName} document, long timestamp)");
		sb.AppendLine($"            => Cache.ForceAddOrUpdate({keyAccessor}, document, timestamp);");

		sb.AppendLine();
		sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
		sb.AppendLine($"        public bool ForceAddOrUpdate({documentTypeName} document, out {documentTypeName} oldDocument)");
		sb.AppendLine(
			"            => ForceAddOrUpdate(document, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out oldDocument);");

		sb.AppendLine();
		sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
		sb.AppendLine(
			$"        public bool ForceAddOrUpdate({documentTypeName} document, long timestamp, out {documentTypeName} oldDocument)");
		sb.AppendLine($"            => Cache.ForceAddOrUpdate({keyAccessor}, document, timestamp, out oldDocument);");
```

- [ ] **Step 4: Implement — producer helper and extensions**

`GenerateProducerHelper` (line 2495-2500) — change the `Produce` method to:

```csharp
			w.Method($"internal void Produce({keyTypeName} key, {typeName} value, bool force = false)", (ref CodeWriter w) => {
				w.If("_producer == null || _topicName == null", (ref CodeWriter w) => {
					w.Line("throw new System.InvalidOperationException(\"Producer not configured. Ensure this cache is registered with Kafka.\");");
				});
				w.Line("_producer.Produce(_topicName, key, value, force);");
			});
```

`GenerateProducerExtensions` — `CacheMarshall.Produce` (line 2523-2530) becomes:

```csharp
						w.Line();
						w.Summary("Produces a message to Kafka for the given cache instance.",
							"With <paramref name=\"force\"/> the message carries the X-Prague-Force header, and every consumer",
							"applies it unconditionally — see Prague.Kafka.CacheProducerExtensions.AddOrUpdateAndProduce.");
						w.Method($"public static void Produce({cacheClassName} cache, {keyTypeName} key, {typeName} value, bool force = false)", (ref CodeWriter w) => {
							w.If("cache._producer == null || cache._topicName == null", (ref CodeWriter w) => {
								w.Line("throw new System.InvalidOperationException(\"Producer not configured. Ensure this cache is registered with Kafka.\");");
							});
							w.Line("cache._producer.Produce(cache._topicName, key, value, force);");
						});
```

and `AddOrUpdateAndProduce` (line 2565-2577) becomes:

```csharp
					w.Line();
					w.Summary("Adds or updates a document in the cache and produces it to Kafka if changed.",
						"With <paramref name=\"force\"/> the write is unconditional: the resident value is replaced even when it is",
						"cache-equal, and the message is produced with the X-Prague-Force header so every consumer applies it the",
						"same way (after-handlers see Update, never Same). Filters still apply on the consumer; force is not an",
						"authorization override.");
					w.Method($"public static void AddOrUpdateAndProduce(this {cacheFullName} cache, {typeName} document, bool force = false)", (ref CodeWriter w) => {
						// Get key from document
						if (!string.IsNullOrEmpty(keyPropertyName))
							w.Line($"var key = document.{keyPropertyName};");
						else
							w.Line("var key = document.GetKey();");

						w.If("force", (ref CodeWriter w) => {
							w.Line("cache.Cache.ForceAddOrUpdate(key, document);");
							w.Line($"{namespaceName}.CacheMarshall.Produce(cache, key, document, force: true);");
							w.Line("return;");
						});
						w.Line();
						w.Line("var changed = cache.Cache.AddOrUpdate(key, document);");
						w.If("changed", (ref CodeWriter w) => {
							w.Line($"{namespaceName}.CacheMarshall.Produce(cache, key, document);");
						});
					});
```

- [ ] **Step 5: Run to verify it passes** (after Task 3, which the generated `Produce(..., force)` call needs)

Run: `dotnet test tests/Prague.Generated.Tests -f net9.0 --filter FullyQualifiedName~Kafka.ForceAddOrUpdateTests`
Expected: 2 passed. Then `dotnet test tests/Prague.Generated.Tests -f net9.0` — all green (nothing else changed shape).

---

### Task 3: Producer stamps `X-Prague-Force`

**Files:**
- Modify: `src/Prague.Kafka/KafkaCaches.cs`
- Modify: `src/Prague.Kafka/IO/KafkaCacheProducer.cs:61-83`

**Interfaces:**
- Produces: `KafkaCaches.ForceHeaderName` (`"X-Prague-Force"`), `KafkaCaches.ForceHeaderValue` (`"1"` as bytes); `KafkaCacheProducer.Produce<TKey, TCacheValue>(string topic, TKey key, TCacheValue value, bool force = false)`.

No broker-free test exists for the producer (it builds a real `IRawProducer`); the wire header is asserted in Task 6.

- [ ] **Step 1: Constants**

`src/Prague.Kafka/KafkaCaches.cs` becomes:

```csharp
namespace Prague.Kafka;

using Confluent.Kafka;

internal static class KafkaCaches {
	public const string ProducerInstanceIdHeaderName = "X-Producer-Id";

	/// <summary>
	///   Marks a forced write: a consumer applies the message unconditionally, bypassing the
	///   <c>CacheEquals</c> gate, in both the load and the live phase. Presence is the signal; the value is
	///   reserved and never inspected. Any producer may stamp it — the trust model is the same as for the
	///   instance-id header and the user header filters.
	/// </summary>
	public const string ForceHeaderName = "X-Prague-Force";

	public static readonly Guid InstanceId = Guid.NewGuid();
	public static readonly byte[] InstanceIdBytes = InstanceId.ToByteArray();
	public static readonly byte[] ForceHeaderValue = "1"u8.ToArray();

	public static Header ProducerInstanceHeader => new(ProducerInstanceIdHeaderName, InstanceIdBytes);
}
```

- [ ] **Step 2: Producer**

In `src/Prague.Kafka/IO/KafkaCacheProducer.cs` change the `Produce` signature and body:

```csharp
	/// <summary>
	///   Produce an upsert. With <paramref name="force"/> the record carries <c>X-Prague-Force</c>, and every
	///   consumer applies it even when the value is cache-equal to what it holds.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Produce<TKey, TCacheValue>(string topic, TKey key, TCacheValue value, bool force = false)
		where TKey : notnull, IEquatable<TKey>
		where TCacheValue : IEnrichable<TCacheValue> {
		if (_cts.IsCancellationRequested)
			return;

		var keyWriter = ScratchArrayWriterManager<TKey>.Rent();
		var valueWriter = ScratchArrayWriterManager<TCacheValue>.Rent();
		try {
			CacheSerde<TKey>.SerializeInto(key, keyWriter);
			CacheSerde<TCacheValue>.SerializeInto(value, valueWriter);

			var headers = new KafkaHeaders();
			headers.Add(KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes);
			TCacheValue.Derich(value, ref headers);
			// A static array: KafkaHeaders stores the memory by reference and needs it alive only until
			// RawProduce returns, so the forced path allocates nothing either.
			if (force)
				headers.Add(KafkaCaches.ForceHeaderName, KafkaCaches.ForceHeaderValue);

			RawProduceWithRetry(topic, keyWriter.WrittenSpan, valueWriter.WrittenSpan, in headers);
		}
		finally {
			ScratchArrayWriterManager<TKey>.Return(keyWriter);
			ScratchArrayWriterManager<TCacheValue>.Return(valueWriter);
		}
	}
```

- [ ] **Step 3: Build checkpoint**

Run: `dotnet build Prague.sln`
Expected: succeeds (Tasks 1–3 together make every project compile again). Then `dotnet test tests/Prague.Generated.Tests -f net9.0 --filter FullyQualifiedName~Kafka.ForceAddOrUpdateTests` → 2 passed (Task 2 Step 5).

---

### Task 4: Load-phase buffer carries the flag

**Files:**
- Modify: `src/Prague.Kafka/Utils/ValueCompactingBuffer.cs`
- Create: `tests/Prague.Kafka.Tests/Utils/ValueCompactingBufferTests.cs`

**Interfaces:**
- Produces: `ValueCompactingBuffer<TKey,TValue>.AddOrReplace(TKey key, TValue value, long timestampMs, bool forced)`; enumerator `Current : (TValue Value, long TimestampMs, bool Forced)`.

- [ ] **Step 1: Write the failing tests**

`tests/Prague.Kafka.Tests/Utils/ValueCompactingBufferTests.cs`:

```csharp
namespace Prague.Kafka.Tests.Utils;

using Prague.Kafka.Utils;

/// <summary>
///   The load-phase compaction buffer carries the forced-write flag with the surviving slot, and the
///   flag ORs across replaces of one key: a forced write that was compacted away would have been applied
///   unconditionally on the live path, so the write that supersedes it must be too (load/live parity).
/// </summary>
[TestFixture]
public class ValueCompactingBufferTests {
	private sealed class Value {
		public string Name = "";
	}

	private static Value V(string name) => new() { Name = name };

	private static List<(Value Value, long TimestampMs, bool Forced)> Drain(ValueCompactingBuffer<int, Value> buffer) {
		var result = new List<(Value, long, bool)>();
		foreach (var entry in buffer)
			result.Add(entry);
		return result;
	}

	[Test]
	public void UnforcedWrite_SurvivesUnforced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False);
	}

	[Test]
	public void ForcedThenUnforced_SurvivorIsForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.Multiple(() => {
			Assert.That(slots[0].Value.Name, Is.EqualTo("b"), "last write wins");
			Assert.That(slots[0].TimestampMs, Is.EqualTo(20));
			Assert.That(slots[0].Forced, Is.True, "the compacted-away forced write must still be applied unconditionally");
		});
	}

	[Test]
	public void UnforcedThenForced_SurvivorIsForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: false);
		buffer.AddOrReplace(1, V("b"), 20, forced: true);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.True);
	}

	[Test]
	public void RemoveThenUnforced_SurvivorIsNotForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.Remove(1);
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False, "a tombstone in between makes the later write a plain Add");
	}

	[Test]
	public void OtherKeys_DoNotInheritTheFlag() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.AddOrReplace(2, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(2));
		Assert.Multiple(() => {
			Assert.That(slots.Single(s => s.Value.Name == "a").Forced, Is.True);
			Assert.That(slots.Single(s => s.Value.Name == "b").Forced, Is.False);
		});
	}

	[Test]
	public void Clear_ResetsTheFlag() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.Clear();
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Prague.Kafka.Tests -f net9.0 --filter FullyQualifiedName~ValueCompactingBufferTests`
Expected: CS1739 — no `forced` parameter.

- [ ] **Step 3: Implement**

`src/Prague.Kafka/Utils/ValueCompactingBuffer.cs` — fields, ctor, `AddOrReplace`, enumerator:

```csharp
	private readonly IndexMap<TKey> _index;
	private readonly TValue?[] _values;
	private readonly long[] _timestamps;
	private readonly bool[] _forced;
	private int _count;

	public ValueCompactingBuffer(int capacity) {
		_index = new IndexMap<TKey>(capacity);
		_values = new TValue?[capacity];
		_timestamps = new long[capacity];
		_forced = new bool[capacity];
		_count = 0;
	}

	public int Count => _index.Count;

	/// <summary>
	///   Buffer or replace the latest value for <paramref name="key"/> (last-write-wins). The forced flag ORs
	///   across replaces: a forced write compacted away here would have been applied unconditionally on the
	///   live path, so the write that supersedes it is applied unconditionally too (load/live parity).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOrReplace(TKey key, TValue value, long timestampMs, bool forced) {
		if (_index.TryRemove(key, out var existingIdx)) {
			_values[existingIdx] = null; // vacate the superseded slot
			forced |= _forced[existingIdx];
		}

		var idx = _count++;
		_values[idx] = value;
		_timestamps[idx] = timestampMs;
		_forced[idx] = forced;
		_index.Insert(key, idx);
	}
```

`Remove` and `Clear` are unchanged: a vacated slot is never enumerated and every new slot writes its own flag, so a stale `_forced` entry is unreachable. Enumerator:

```csharp
	internal struct Enumerator {
		private readonly TValue?[] _values;
		private readonly long[] _timestamps;
		private readonly bool[] _forced;
		private readonly int _count;
		private int _index;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(TValue?[] values, long[] timestamps, bool[] forced, int count) {
			_values = values;
			_timestamps = timestamps;
			_forced = forced;
			_count = count;
			_index = -1;
		}

		public readonly (TValue Value, long TimestampMs, bool Forced) Current {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => (_values[_index]!, _timestamps[_index], _forced[_index]);
		}
		// MoveNext unchanged
	}
```

and `public Enumerator GetEnumerator() => new(_values, _timestamps, _forced, _count);`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Prague.Kafka.Tests -f net9.0 --filter FullyQualifiedName~ValueCompactingBufferTests`
Expected: 6 passed. (`KafkaCacheConsumer.cs` will not compile until Task 5 — the test project builds `Prague.Kafka`, so do Task 5's consumer edit for `AddOrReplace`/`FlushRawLoadBufferToCache` in the same pass if the build blocks; the two tasks are split for review, not for build order.)

---

### Task 5: Consumer reads the marker and applies unconditionally

**Files:**
- Modify: `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` — `DispatchRaw` abstract (`:39`), `EvaluateHeaderGate` (`:63-91`), constants (`:168-171`), `DispatchRaw` override (`:173-287`), `FlushRawLoadBufferToCache` (`:324-331`), `ApplyRawLiveAsync` / `HandleRawLiveUpdate` (`:348-361`), the gate call in `ConsumeRawLoop` (`:640-676`)

**Interfaces:**
- Consumes: Task 1's `IDataCache.ForceAddOrUpdate(document, timestamp[, out old])`; Task 3's `KafkaCaches.ForceHeaderName`; Task 4's `AddOrReplace(..., bool forced)` and 3-tuple enumerator.
- Produces: `KafkaCacheHandler.EvaluateHeaderGate(in RawHeaders headers, out bool forced)`; `DispatchRaw(in RawMessage raw, bool isLoading, bool forced)`.

Behaviour is asserted by Task 6 (integration). This task is a build checkpoint plus the existing broker-free suite.

- [ ] **Step 1: Header gate**

Replace `EvaluateHeaderGate`:

```csharp
	/// <summary>
	///   Span-based header filtering for the raw path — producer-instance self-filter plus the handler's
	///   configured header filters, evaluated against UTF-8 name/value spans with no allocation.
	///   <para>
	///   Returns the reason rather than a bare bool, because the consume loop treats one of them differently:
	///   <see cref="HeaderGate.MissingRequiredHeader" /> is waived for a tombstone, since a delete carries no
	///   headers to satisfy a <c>WithHeaderExistsFilter</c> with and requiring one would pin the key forever —
	///   against Prague's own producer included, as <c>KafkaCacheProducer.Delete</c> stamps no user headers.
	///   <see cref="HeaderGate.SelfProduced" /> and <see cref="HeaderGate.Rejected" /> stay absolute.
	///   </para>
	///   <para>
	///   <paramref name="forced"/> reports the <c>X-Prague-Force</c> marker. It is read here, in the one loop
	///   that already visits every header, so a forced write costs one more length-guarded compare per header
	///   and no second walk. It is orthogonal to the gate: a rejected forced message is still rejected.
	///   </para>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal HeaderGate EvaluateHeaderGate(in RawHeaders headers, out bool forced) {
		var filters = HeadersFilters;
		// One bit per required header name, collected as they are resolved. Required names compose with AND: a
		// single shared bool meant any one required header satisfied all of them.
		var seen = 0UL;
		forced = false;
		foreach (var (name, value) in headers) {
			if (System.Text.Ascii.Equals(name, KafkaCaches.ProducerInstanceIdHeaderName)
			    && value.SequenceEqual(KafkaCaches.InstanceIdBytes))
				return HeaderGate.SelfProduced;
			if (System.Text.Ascii.Equals(name, KafkaCaches.ForceHeaderName))
				forced = true;
			if (!filters.ShouldProcess(ref seen, name, value))
				return HeaderGate.Rejected;
		}

		// seen only ever collects bits that are in RequiredMask, so equality means every requirement was met.
		return seen == filters.RequiredMask ? HeaderGate.Accept : HeaderGate.MissingRequiredHeader;
	}
```

- [ ] **Step 2: Dispatch signature and constants**

Abstract: `internal abstract void DispatchRaw(in RawMessage raw, bool isLoading, bool forced);` with the summary gaining: `<paramref name="forced"/> routes the value through the unconditional write in either phase; a tombstone ignores it.`

Constants:

```csharp
	private const byte RAW_KIND_UPDATE = 0;
	private const byte RAW_KIND_DELETE = 1;
	private const byte RAW_KIND_FILTERED = 2;
	// A forced update rides in Kind rather than a second field, so RawWorkItem keeps its layout.
	private const byte RAW_KIND_FORCE_UPDATE = 3;
```

Override: `internal override void DispatchRaw(in RawMessage raw, bool isLoading, bool forced) {`. In the load branch replace `buffer.AddOrReplace(key, value, timestamp.UnixTimestampMs);` with `buffer.AddOrReplace(key, value, timestamp.UnixTimestampMs, forced);`. In the live branch replace the final publish with `PublishRaw(forced ? RAW_KIND_FORCE_UPDATE : RAW_KIND_UPDATE, key, liveValue, timestamp.UnixTimestampMs);`.

- [ ] **Step 3: Load flush and live apply**

```csharp
	private void FlushRawLoadBufferToCache() {
		if (_rawLoadBuffer is null)
			return;
		foreach (var (value, ts, forced) in _rawLoadBuffer)
			if (forced)
				_cache.ForceAddOrUpdate(value, ts);
			else
				_cache.AddOrUpdate(value, ts);

		_rawLoadBuffer.Clear();
	}
```

```csharp
	private ValueTask ApplyRawLiveAsync(byte kind, TKey key, TVlaue? value, long timestampMs)
		=> kind switch {
			RAW_KIND_UPDATE => HandleRawLiveUpdate(key, value!, timestampMs, force: false),
			RAW_KIND_FORCE_UPDATE => HandleRawLiveUpdate(key, value!, timestampMs, force: true),
			RAW_KIND_DELETE => HandleRawLiveDelete(key, timestampMs),
			_ => ExecuteAfterHandlers(UpdateType.Filtered, default!, null, null)
		};

	private ValueTask HandleRawLiveUpdate(TKey key, TVlaue value, long timestampMs, bool force) {
		TVlaue? old;
		var changed = force
			? _cache.ForceAddOrUpdate(value, timestampMs, out old)
			: _cache.AddOrUpdate(value, timestampMs, out old);
		return (changed, old) switch {
			(false, _) => ExecuteAfterHandlers(UpdateType.Same, key, value, null),
			(_, null) => ExecuteAfterHandlers(UpdateType.Add, key, value, null),
			_ => ExecuteAfterHandlers(UpdateType.Update, key, value, old)
		};
	}
```

- [ ] **Step 4: Consume loop**

```csharp
					HeaderGate gate;
					bool forced;
					try {
						gate = handler.EvaluateHeaderGate(raw.Headers, out forced);
					} catch (Exception e) {
						// (existing comment block unchanged)
						ct.ThrowIfCancellationRequested();
						_logger.HeaderFilterError(e, handler.Name, raw.Offset.Value);
						gate = HeaderGate.Rejected;
						forced = false;
					}
					…
					handler.DispatchRaw(in raw, !handler.IsInitialConsumeDone, forced);
```

- [ ] **Step 5: Build + broker-free suite**

Run: `dotnet build Prague.sln` → succeeds. Run: `dotnet test tests/Prague.Kafka.Tests -f net9.0` → all green (the header-gate state tests exercise `ShouldProcess`, untouched).

---

### Task 6: Integration tests (Docker)

**Files:**
- Create: `tests/Prague.Kafka.IntegrationTests/ForcedWriteTests.cs`

**Interfaces:**
- Consumes: everything above; `FilterEntity`/`FilterEntityCache` fixtures; `DualKafkaClusterFixture.NewProducer/NewConsumer/CreateTopicAsync`; `KafkaCacheProducer` keyed service `"KafkaConfig"`.

- [ ] **Step 1: Write the tests**

```csharp
namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using IO;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   A forced write — the <c>X-Prague-Force</c> header — is applied unconditionally: the resident value is
///   replaced even when cache-equal, in both phases, and after-handlers see <c>Update</c>, never <c>Same</c>.
///   It does not override ingress filters. Prague's own producer stamps the header when asked to.
/// </summary>
[TestFixture]
public class ForcedWriteTests {
	private const string TopicPrefix = "it-forced-write";
	private const string ForceHeaderName = "X-Prague-Force";
	private const int Key = 1;
	private const int SpacerFirstKey = 100;

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	/// <summary>
	///   Runs whatever the test did. A failing test never reaches its own StopAsync call, and a consumer
	///   left alive stays a member of the group — from then on every later test's join has to rebalance
	///   around a zombie, which is what makes initial loads stall.
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
	public async Task LivePhase_ForcedEqualValue_IsAppliedAsUpdate() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, Key, "same", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == Key));

		// Control: the identical payload, unforced, is Same.
		Produce(producer, Key, "same", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == Key));
		Assert.That(cache.Cache.TryGet(Key, out var beforeForce), Is.True);

		Produce(producer, Key, "same", forced: true);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == Key));

		var update = recording.Invocations.LastOrDefault(i => i.UpdateType == UpdateType.Update && i.Key == Key);
		Assert.That(update, Is.Not.Null, "a forced equal value must surface as Update, not Same");
		Assert.Multiple(() => {
			Assert.That(update!.NewValue!.Name, Is.EqualTo("same"));
			Assert.That(update.OldValue!.Name, Is.EqualTo("same"));
			Assert.That(update.OldValue, Is.SameAs(beforeForce), "old is the previously resident instance");
			Assert.That(update.NewValue.__PragueMetadata__.Offset, Is.GreaterThan(update.OldValue.__PragueMetadata__.Offset),
				"the resident object now carries the forced record's offset");
		});
		Assert.That(cache.Cache.TryGet(Key, out var afterForce), Is.True);
		Assert.That(afterForce, Is.SameAs(update!.NewValue), "the store swapped the reference");

		await StopAsync(sp);
	}

	/// <summary>
	///   Load/live parity across the compacting-buffer flush: the first value for the key is in the cache,
	///   not merely buffered, when the repeat arrives. An unforced repeat leaves the earlier record resident;
	///   a forced one replaces it — visible through the persisted offset, since the payloads are identical.
	/// </summary>
	[TestCase(false)]
	[TestCase(true)]
	public async Task LoadPhase_EqualRepeat_ReplacesOnlyWhenForced(bool forced) {
		var lastSpacer = SpacerFirstKey + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		long firstOffset, repeatOffset;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			firstOffset = (await ProduceAsync(seeder, Key, "same", forced: false)).Offset.Value;
			for (var i = SpacerFirstKey; i <= lastSpacer; i++)
				Produce(seeder, i, $"spacer-{i}", forced: false);
			repeatOffset = (await ProduceAsync(seeder, Key, "same", forced)).Offset.Value;
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True,
			"Spacer keys must be loaded — otherwise the flush boundary was never crossed and the case is vacuous");
		Assert.That(cache.Cache.TryGet(Key, out var resident), Is.True);
		Assert.Multiple(() => {
			Assert.That(resident!.Name, Is.EqualTo("same"));
			Assert.That(resident.__PragueMetadata__.Offset, Is.EqualTo(forced ? repeatOffset : firstOffset));
			Assert.That(recording.Invocations, Is.Empty, "the initial load never fires after-handlers, forced or not");
		});

		await StopAsync(sp);
	}

	[Test]
	public async Task ForcedWrite_DoesNotOverrideAValueFilter() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording,
			static handler => handler.WithValueFilter(static v => !v.Name.StartsWith("reject", StringComparison.Ordinal), treatAsDelete: true));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, Key, "keep", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == Key));

		Produce(producer, Key, "reject-me", forced: true);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Delete && i.Key == Key));

		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Delete && i.Key == Key), Is.True,
			"treatAsDelete must win over the force marker");
		Assert.That(cache.Cache.TryGet(Key, out _), Is.False);

		await StopAsync(sp);
	}

	[Test]
	public async Task PragueProducer_Force_StampsTheHeader() {
		var (sp, _) = await StartAsync(new RecordingAfterHandler());
		var pragueProducer = sp.GetRequiredKeyedService<KafkaCacheProducer>("KafkaConfig");

		pragueProducer.Produce(_topic, 1, new FilterEntity { Id = 1, Name = "plain", Value = 1 });
		pragueProducer.Produce(_topic, 2, new FilterEntity { Id = 2, Name = "forced", Value = 2 }, force: true);

		using var consumer = DualKafkaClusterFixture.NewConsumer(DualKafkaClusterFixture.BootstrapServersA);
		consumer.Subscribe(_topic);
		var records = new Dictionary<int, Headers>();
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (records.Count < 2 && DateTime.UtcNow < deadline) {
			var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
			if (result is null || result.IsPartitionEOF)
				continue;
			records[MessagePackSerializer.Deserialize<int>(result.Message.Key)] = result.Message.Headers;
		}

		Assert.That(records, Has.Count.EqualTo(2), "both records must reach the topic");
		Assert.Multiple(() => {
			Assert.That(records[1].TryGetLastBytes(ForceHeaderName, out _), Is.False, "an unforced produce carries no marker");
			Assert.That(records[2].TryGetLastBytes(ForceHeaderName, out var value), Is.True, "a forced produce carries the marker");
			Assert.That(value, Is.EqualTo("1"u8.ToArray()));
		});

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, bool forced) {
		producer.Produce(_topic, Message(id, name, forced));
	}

	private Task<DeliveryResult<byte[], byte[]>> ProduceAsync(IProducer<byte[], byte[]> producer, int id, string name, bool forced) {
		return producer.ProduceAsync(_topic, Message(id, name, forced));
	}

	private static Message<byte[], byte[]> Message(int id, string name, bool forced) {
		var headers = new Headers();
		if (forced)
			headers.Add(ForceHeaderName, "1"u8.ToArray());
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		return new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = headers
		};
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(
		RecordingAfterHandler recording,
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>>? configure = null) {
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
		services.AddLogging();
		services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording);
		services.AddKafkaCaches("KafkaConfig", b => {
			var handler = b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
			configure?.Invoke(handler);
		});

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private static async Task StopAsync(IServiceProvider sp) {
		var hosted = sp.GetRequiredService<IHostedService>();
		await hosted.StopAsync(CancellationToken.None);
		// Every cache in the process shares one group.id (KafkaCaches.InstanceId is static), so a consumer
		// left alive here stays a group member: it delays the rebalance the next test's join triggers, and
		// that test then waits on an initial load that cannot complete. Disposing cancels the consume loop,
		// whose finally closes the consumer and leaves the group.
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

	private sealed record Invocation(UpdateType UpdateType, int Key, FilterEntity? NewValue, FilterEntity? OldValue);

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<Invocation> _invocations = new();

		public IReadOnlyCollection<Invocation> Invocations => _invocations;

		public bool Any(Func<Invocation, bool> predicate) => _invocations.Any(predicate);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_invocations.Enqueue(new Invocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}
}
```

Check the exact builder type name returned by `b.AddCache<…>(topic)` before writing `StartAsync`'s `configure` parameter (`grep -n "AddCache<" src/Prague.Kafka/DependencyInjection.cs`); adjust the generic parameter list to match.

- [ ] **Step 2: Build**

Run: `dotnet build tests/Prague.Kafka.IntegrationTests -f net9.0`
Expected: succeeds.

- [ ] **Step 3: Run (Docker required)**

Run: `dotnet test tests/Prague.Kafka.IntegrationTests -f net9.0 --filter FullyQualifiedName~ForcedWriteTests`
Expected: 5 passed. Without Docker every test fails on the container socket within seconds — report that as "not run", not as a failure of the change.

---

### Task 7: Perf checkpoint for the shared index walk

**Files:** none modified.

- [ ] **Step 1: Baseline the unforced write path before/after**

The `ApplyToIndexes` extraction is the only structural change to the existing write path. Run the ingest benchmark on the pre-change tree (`git stash` the `src/` edits, keep the tests) and again on the post-change tree:

```bash
dotnet run -c Release --project perf/Prague.Baseline.Bdn --framework net9.0 -- --filter '*CoreIngestBenchmarks*'
```

Compare `IngestAll` mean and allocated bytes. Record machine load next to the numbers. Expected: within run-to-run noise (the helper is `AggressiveInlining` into `AggressiveOptimization` callers, so the JIT output should be equivalent). If the mean moves outside the perf tripwire's band for `ingest.*` (see `perf/README.md`), replace the helper with a third inline copy of the walk in the two `ForceAddOrUpdate` bodies and leave the two `AddOrUpdate` bodies exactly as they were.

---

### Task 8: Documentation

**Files:**
- Modify: `www/docs/articles/core-concepts/conditional-updates.md` (after "## Producer-side dispatch")
- Modify: `www/docs/articles/advanced/kafka-integration.md:197-217` (§ Producer)
- Modify: `context/core.md:7-11`
- Modify: `context/kafka.md:11`
- Modify: `context/kafka-filters.md:7,20`

- [ ] **Step 1: conditional-updates.md**

Replace the "Producer-side dispatch" section with:

```markdown
## Producer-side dispatch

`KafkaCacheProducer.Produce(topic, key, value)` always writes — there is no conditional skip on the raw producer. The generated `cache.AddOrUpdateAndProduce(document)` is the conditional one: it applies the document locally and produces it only when the local `AddOrUpdate` reported a change.

## Forcing a write

Sometimes the current state has to be re-emitted *as a change* — to refresh `LastUpdated`, to re-run projectors on every replica, to re-stamp the persisted offset — without inventing a field that differs. That is a **forced write**:

```csharp
cache.Cache.ForceAddOrUpdate(key, document);           // local, unconditional
cache.AddOrUpdateAndProduce(document, force: true);     // local + Kafka, unconditional everywhere
producer.Produce("orders", o.OrderId, o, force: true);  // raw producer, header only
```

A forced write replaces the resident value even when `CacheEquals` says equal, walks every index (the `LastUpdated` adapters refresh their timestamp), and — on the Kafka path — carries the `X-Prague-Force` header, so every consumer applies it the same way in both the load and the live phase. After-handlers see `Update` (or `Add` for a new key), never `Same`.

What force does **not** do:

- It does not bypass ingress policy. Header, key and value filters still run; a rejected forced message is skipped or deleted exactly as an unforced one would be.
- It does not touch tombstones — a delete is already unconditional.
- It does not re-apply on the producer that wrote it: the self-filter drops the echo, and the local apply happened in `AddOrUpdateAndProduce` itself.
- It does not fire after-handlers on the initial load; nothing does.

The header is presence-only (its value, `1`, is reserved). Any producer may stamp it. A consumer built before the header existed passes it as an unknown header and applies the message conditionally — it sees `Same`.
```

- [ ] **Step 2: kafka-integration.md § Producer**

After the code block, replace "The producer always writes — there is no producer-side dedup. …" with:

```markdown
The producer always writes — there is no producer-side dedup. If you need it, consult `cache.Cache.TryGet(...)` before calling `Produce`, or use the generated `cache.AddOrUpdateAndProduce(document)`, which produces only when the local write changed something.

`Produce(topic, key, value, force: true)` and `AddOrUpdateAndProduce(document, force: true)` stamp the `X-Prague-Force` header: every consumer then applies the record even when it is cache-equal to what it holds, and after-handlers see `Update` rather than `Same`. See [Forcing a write](../core-concepts/conditional-updates.md#forcing-a-write).
```

- [ ] **Step 3: context files**

`context/core.md`, after the third bullet under "InMemoryDataCache & IDataCache":

```markdown
- Writes are conditional: `AddOrUpdate` asks the store to replace only when `!old.CacheEquals(new)` and reports `Same` otherwise (no index walk, no `LastUpdated` refresh). `ForceAddOrUpdate` is the unconditional twin — same four overloads, always-true predicate, always returns `true`; both fan out to the indexes through one `ApplyToIndexes` helper. `IDataCache<TKey,TValue>` carries both.
```

`context/kafka.md` line 11, append to the bullet:

```markdown
 A record carrying `X-Prague-Force` (`KafkaCaches.ForceHeaderName`; `Produce(..., force: true)` / `AddOrUpdateAndProduce(doc, force: true)` stamp it) bypasses that gate: applied via `ForceAddOrUpdate` in both phases, after-handlers see `Update`/`Add`, never `Same`. On load the compacting buffer ORs the flag across replaces of one key (parity with what live would have done). Filters still apply; tombstones ignore it.
```

`context/kafka-filters.md` line 7, append to the `WithHeaderFilter` bullet after "so a producer never re-consumes its own writes.":

```markdown
 The same loop reports the `X-Prague-Force` marker (`out bool forced`) — one more length-guarded compare per header, no second walk; it is orthogonal to the gate result.
```

and line 20, "The **header** gate still walks every header regardless — the producer self-filter has to inspect each one" → "The **header** gate still walks every header regardless — the producer self-filter and the force marker have to inspect each one".

---

## Self-review

- **Spec coverage:** core API (T1), interface (T1), codegen incl. helper (T2), producer + constants (T3), buffer OR (T4), gate/dispatch/live/load (T5), tests broker-free (T1, T2, T4) and Docker (T6), perf guard (T7), docs (T8). Out-of-scope items untouched. ✔
- **Placeholders:** none; every step carries its code.
- **Type consistency (superseded by the revision note — the shipped shape is the single forced `AddOrUpdate` overload):** `ForceAddOrUpdate` overload shapes match between T1 (`InMemoryDataCache`, `IDataCache`), T2 (generated), T5 (consumer calls `ForceAddOrUpdate(value, ts)` and `(value, ts, out old)` on `IDataCache<TKey,TValue>`). `AddOrReplace(key, value, ts, forced)` matches T4/T5. `KafkaCaches.ForceHeaderName/ForceHeaderValue` match T3/T5. `DispatchRaw(in raw, isLoading, forced)` matches abstract/override/call site.

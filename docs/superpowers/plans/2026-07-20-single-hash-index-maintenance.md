# Single-Hash Index Maintenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hash the main key exactly once per `InMemoryDataCache` mutation and thread it through all index maintenance; benchmark before/after on composite-int and string PK models.

**Architecture:** `ConcurrentCacheStore` already computes and stores the key hash — surface it via `UpdateResult.KeyHash`/`TryRemove(out keyHash)`, cut the three internal `ICacheIndex` members over to carry `int keyHash`, and add pre-hashed entry points to `PooledSet` (consumes the hash as-is — same `DefaultKeyComparer`) and `PooledBTree` (recovers its raw tiebreak hash via the Fibonacci-mix inverse for value types, direct for ref types). The btree's DJB2 string tiebreak is retired for Marvin32 so threaded string hashes are consumable.

**Tech Stack:** .NET 9, NUnit (`tests/Prague.Core.Tests`, `tests/Prague.Generated.Tests`), BenchmarkDotNet (`benchmarks/Prague.Benchmarks`).

**Spec:** `docs/superpowers/specs/2026-07-20-single-hash-index-maintenance-design.md`

## Global Constraints

- House C# style: tabs (width 2), file-scoped namespaces with usings INSIDE the namespace, K&R braces, `var`, no braces on single-line statements, `_camelCase` private fields.
- High-performance rules apply — everything here is hot path: no LINQ, no allocations in loop bodies, `[MethodImpl(AggressiveInlining)]` on small hot methods, `[MemoryDiagnoser]` on benchmarks.
- `keyHash` ALWAYS means `default(DefaultKeyComparer<TKey>).GetHashCode(key)` — the store's form (Fibonacci-mixed for value types, raw Marvin32 for strings, raw for other ref types). Never a raw hash, never a different comparer's hash.
- Every pre-hashed entry point carries `Debug.Assert` verifying the hash against a local recompute.
- Do NOT run formatters or project-wide suites per task; each task runs only its own tests. Full suite runs once in Task 8.
- Benchmark commands run from repo root. Baseline numbers (Task 1) MUST be recorded before any production code changes land.

---

### Task 1: Benchmark + baseline numbers on current code

**Files:**
- Create: `benchmarks/Prague.Benchmarks/CacheIndexMaintenanceBenchmarks.cs`
- Create: `docs/superpowers/plans/2026-07-20-single-hash-bench-results.md`

**Interfaces:**
- Consumes: `InMemoryDataCache<TKey,TValue>` public API — `CacheKeyValueListIndex(Func<TKey,TValue,TIndexKey>)`, `CacheRangeIndex(Func<TKey,TValue,TIndexKey>)`, `AddOrUpdate(TKey, TValue, long timestamp)`.
- Produces: the benchmark class re-run unchanged in Task 8.

- [ ] **Step 1: Write the benchmark**

```csharp
namespace Prague.Benchmarks;

	using BenchmarkDotNet.Attributes;
	using Prague.Core;

public readonly record struct CompositeId(int A, int B);

public sealed class CompositeEntity : ICacheEquatable<CompositeEntity>, ICacheClonable<CompositeEntity> {
	public CompositeId Id { get; init; }
	public int CategoryId { get; init; }
	public int RegionId { get; init; }
	public long Timestamp { get; init; }
	public int Age { get; init; }

	public CompositeEntity Clone() => this; // immutable: identity clone keeps allocs out of the measured path

	public bool CacheEquals(CompositeEntity? other) =>
		other is not null && Id == other.Id && CategoryId == other.CategoryId
		&& RegionId == other.RegionId && Timestamp == other.Timestamp && Age == other.Age;

	public int CacheGetHashCode() => Id.GetHashCode();
}

public sealed class StringKeyEntity : ICacheEquatable<StringKeyEntity>, ICacheClonable<StringKeyEntity> {
	public string Id { get; init; } = null!;
	public int CategoryId { get; init; }
	public int RegionId { get; init; }
	public long Timestamp { get; init; }
	public int Age { get; init; }

	public StringKeyEntity Clone() => this;

	public bool CacheEquals(StringKeyEntity? other) =>
		other is not null && Id == other.Id && CategoryId == other.CategoryId
		&& RegionId == other.RegionId && Timestamp == other.Timestamp && Age == other.Age;

	public int CacheGetHashCode() => Id.GetHashCode();
}

/// <summary>
///   Measures main-key hashing overhead in index maintenance (see the 2026-07-20 single-hash
///   spec). Model per spec: 2 set indexes (CategoryId, RegionId; ~100 entities per group) +
///   2 btree indexes (Timestamp: unique ascending; Age: 64 distinct values → heavy duplicate
///   runs, forcing the btree's composite (key, hash) mode — the shape that re-hashes hardest).
///   Two PK shapes: composite (int,int) = cheap-hash floor; ~24-char string = expensive-hash
///   ceiling. AddAll = N fresh inserts. UpdateAll = alternate between two prebuilt generations
///   whose CategoryId/RegionId/Timestamp/Age all differ, so every AddOrUpdate is a real Update
///   that moves every index group (the double-hash path in set indexes). Entities are prebuilt
///   in GlobalSetup; explicit timestamps keep DateTimeOffset.Now out of the measurement.
/// </summary>
[MemoryDiagnoser]
public class CacheIndexMaintenanceBenchmarks {
	[Params(100_000)] public int N { get; set; }

	private CompositeEntity[] _compositeGen0 = null!;
	private CompositeEntity[] _compositeGen1 = null!;
	private StringKeyEntity[] _stringGen0 = null!;
	private StringKeyEntity[] _stringGen1 = null!;

	private InMemoryDataCache<CompositeId, CompositeEntity> _compositeUpdateCache = null!;
	private InMemoryDataCache<string, StringKeyEntity> _stringUpdateCache = null!;
	private InMemoryDataCache<CompositeId, CompositeEntity> _compositeRemoveCache = null!;
	private InMemoryDataCache<string, StringKeyEntity> _stringRemoveCache = null!;
	private int _round;

	private static CompositeEntity MakeComposite(int i, int gen, int groups) => new() {
		Id = new CompositeId(i, ~i),
		CategoryId = (i + gen) % groups,
		RegionId = (i * 31 + gen) % groups,
		Timestamp = 1_700_000_000_000L + i * 2 + gen,
		Age = (i + gen) % 64,
	};

	private static StringKeyEntity MakeString(string key, int i, int gen, int groups) => new() {
		Id = key,
		CategoryId = (i + gen) % groups,
		RegionId = (i * 31 + gen) % groups,
		Timestamp = 1_700_000_000_000L + i * 2 + gen,
		Age = (i + gen) % 64,
	};

	private static InMemoryDataCache<CompositeId, CompositeEntity> NewCompositeCache() {
		var cache = new InMemoryDataCache<CompositeId, CompositeEntity>();
		cache.CacheKeyValueListIndex(static (_, v) => v.CategoryId);
		cache.CacheKeyValueListIndex(static (_, v) => v.RegionId);
		cache.CacheRangeIndex(static (_, v) => v.Timestamp);
		cache.CacheRangeIndex(static (_, v) => v.Age);
		return cache;
	}

	private static InMemoryDataCache<string, StringKeyEntity> NewStringCache() {
		var cache = new InMemoryDataCache<string, StringKeyEntity>();
		cache.CacheKeyValueListIndex(static (_, v) => v.CategoryId);
		cache.CacheKeyValueListIndex(static (_, v) => v.RegionId);
		cache.CacheRangeIndex(static (_, v) => v.Timestamp);
		cache.CacheRangeIndex(static (_, v) => v.Age);
		return cache;
	}

	[GlobalSetup]
	public void Setup() {
		var groups = Math.Max(1, N / 100);
		_compositeGen0 = new CompositeEntity[N];
		_compositeGen1 = new CompositeEntity[N];
		_stringGen0 = new StringKeyEntity[N];
		_stringGen1 = new StringKeyEntity[N];
		for (var i = 0; i < N; i++) {
			_compositeGen0[i] = MakeComposite(i, 0, groups);
			_compositeGen1[i] = MakeComposite(i, 1, groups);
			var key = $"cust-{i:D19}"; // ~24 chars, shared prefix — id-like
			_stringGen0[i] = MakeString(key, i, 0, groups);
			_stringGen1[i] = MakeString(key, i, 1, groups);
		}

		_compositeUpdateCache = NewCompositeCache();
		_stringUpdateCache = NewStringCache();
		for (var i = 0; i < N; i++) {
			_compositeUpdateCache.AddOrUpdate(_compositeGen0[i].Id, _compositeGen0[i], i);
			_stringUpdateCache.AddOrUpdate(_stringGen0[i].Id, _stringGen0[i], i);
		}

		_round = 0;
	}

	[Benchmark]
	public void AddAll_Composite() {
		var cache = NewCompositeCache();
		var entities = _compositeGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.AddOrUpdate(entities[i].Id, entities[i], i);
	}

	[Benchmark]
	public void AddAll_String() {
		var cache = NewStringCache();
		var entities = _stringGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.AddOrUpdate(entities[i].Id, entities[i], i);
	}

	[Benchmark]
	public void UpdateAll_Composite() {
		var gen = ++_round & 1;
		var entities = gen == 0 ? _compositeGen0 : _compositeGen1;
		var ts = (long)_round * N;
		for (var i = 0; i < entities.Length; i++)
			_compositeUpdateCache.AddOrUpdate(entities[i].Id, entities[i], ts + i);
	}

	[Benchmark]
	public void UpdateAll_String() {
		var gen = ++_round & 1; // each benchmark runs in its own process — every Update method must flip its own generations
		var entities = gen == 0 ? _stringGen0 : _stringGen1;
		var ts = (long)_round * N;
		for (var i = 0; i < entities.Length; i++)
			_stringUpdateCache.AddOrUpdate(entities[i].Id, entities[i], ts + i);
	}

	// RemoveAll empties the cache, so each iteration gets a freshly repopulated one via
	// IterationSetup (excluded from the measurement window). Repopulation costs one AddAll
	// (~tens of ms), acceptable against a same-order measured loop.
	[IterationSetup(Target = nameof(RemoveAll_Composite))]
	public void FillCompositeRemoveCache() {
		_compositeRemoveCache = NewCompositeCache();
		for (var i = 0; i < N; i++)
			_compositeRemoveCache.AddOrUpdate(_compositeGen0[i].Id, _compositeGen0[i], i);
	}

	[Benchmark]
	public void RemoveAll_Composite() {
		var cache = _compositeRemoveCache;
		var entities = _compositeGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.Remove(entities[i].Id, N + i);
	}

	[IterationSetup(Target = nameof(RemoveAll_String))]
	public void FillStringRemoveCache() {
		_stringRemoveCache = NewStringCache();
		for (var i = 0; i < N; i++)
			_stringRemoveCache.AddOrUpdate(_stringGen0[i].Id, _stringGen0[i], i);
	}

	[Benchmark]
	public void RemoveAll_String() {
		var cache = _stringRemoveCache;
		var entities = _stringGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.Remove(entities[i].Id, N + i);
	}
}
```

Note: `AddAll_*` includes cache construction (~µs) against N=100k mutations (~tens of ms) — negligible, and identical before/after so the delta is clean.

- [ ] **Step 2: Verify it builds and runs a smoke pass**

Run: `dotnet build benchmarks/Prague.Benchmarks -c Release`
Expected: Build succeeded.

Run: `dotnet run -c Release --project benchmarks/Prague.Benchmarks -- --filter '*CacheIndexMaintenance*' --job Dry`
Expected: all 6 benchmarks execute without exceptions (Dry = 1 iteration, numbers meaningless).

- [ ] **Step 3: Record the baseline**

Run: `dotnet run -c Release --project benchmarks/Prague.Benchmarks -- --filter '*CacheIndexMaintenance*'`
Expected: full run (several minutes). Copy the results table verbatim into
`docs/superpowers/plans/2026-07-20-single-hash-bench-results.md` under a `## Baseline (before, commit <sha>)` heading.

- [ ] **Step 4: Commit**

```bash
git add benchmarks/Prague.Benchmarks/CacheIndexMaintenanceBenchmarks.cs docs/superpowers/plans/2026-07-20-single-hash-bench-results.md
git commit -m "bench: cache index maintenance baseline (composite + string PK)"
```

---

### Task 2: HashMixing — mix/unmix constants and roundtrip test

**Files:**
- Create: `src/Prague.Core/Utils/HashMixing.cs`
- Modify: `src/Prague.Core/Collections/IKeyComparer.cs:59-66` (DefaultKeyComparer value-type branch delegates to `HashMixing.Mix`)
- Test: `tests/Prague.Core.Tests/DataStructures/HashMixingTests.cs`

**Interfaces:**
- Produces: `internal static class Prague.Core.Utils.HashMixing` — `internal const uint Fibonacci = 2654435769U`, `internal const uint FibonacciInverse = 340573321U`, `internal static int Mix(int rawHash)`, `internal static int Unmix(int mixedHash)`. Task 5 consumes `Unmix`.

- [ ] **Step 1: Write the failing test**

```csharp
namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using Prague.Core.Utils;

[TestFixture]
public class HashMixingTests {
	[Test]
	public void UnmixInvertsMixOnBoundaryValues() {
		int[] values = [0, 1, -1, 2, 3, int.MaxValue, int.MinValue, 12345678, unchecked((int)0xDEADBEEF)];
		foreach (var v in values)
			Assert.That(HashMixing.Unmix(HashMixing.Mix(v)), Is.EqualTo(v));
	}

	[Test]
	public void UnmixInvertsMixOnRandomSweep() {
		var rng = new Random(42);
		for (var i = 0; i < 100_000; i++) {
			var v = rng.Next(int.MinValue, int.MaxValue);
			if (HashMixing.Unmix(HashMixing.Mix(v)) != v)
				Assert.Fail($"roundtrip failed for {v}");
		}
		Assert.Pass();
	}

	[Test]
	public void MixMatchesDefaultKeyComparerValueTypePath() {
		var comparer = default(DefaultKeyComparer<int>);
		int[] values = [0, 1, -1, 42, int.MaxValue, int.MinValue];
		foreach (var v in values)
			Assert.That(comparer.GetHashCode(v), Is.EqualTo(HashMixing.Mix(v.GetHashCode())));
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~HashMixingTests`
Expected: FAIL — compile error, `HashMixing` does not exist.

- [ ] **Step 3: Implement HashMixing and refactor DefaultKeyComparer**

`src/Prague.Core/Utils/HashMixing.cs`:

```csharp
namespace Prague.Core.Utils;

	using System.Runtime.CompilerServices;

/// <summary>
///   The Fibonacci (Knuth multiplicative) mix <c>DefaultKeyComparer</c> applies to value-type
///   hashes, plus its exact inverse. Multiplication by an odd constant is a bijection on uint,
///   so <c>Unmix(Mix(h)) == h</c> for every h. <c>PooledBTree</c> uses <see cref="Unmix"/> to
///   recover the raw <c>T.GetHashCode()</c> tiebreak hash from a store-computed key hash with
///   one multiply instead of re-hashing the key (2026-07-20 single-hash spec).
/// </summary>
internal static class HashMixing {
	internal const uint Fibonacci = 2654435769U; // floor(2^32 / golden ratio); odd → invertible
	internal const uint FibonacciInverse = 340573321U; // Fibonacci * FibonacciInverse ≡ 1 (mod 2^32)

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int Mix(int rawHash) => (int)((uint)rawHash * Fibonacci);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int Unmix(int mixedHash) => (int)((uint)mixedHash * FibonacciInverse);
}
```

In `DefaultKeyComparer<T>.GetHashCode` (`IKeyComparer.cs`), replace the two mix lines of the value-type branch:

```csharp
		if (typeof(T).IsValueType) {
			// Fibonacci hashing (Knuth) — see HashMixing for the constant and its inverse.
			// `int.GetHashCode()` returns the int identity, which is catastrophic for power-of-2 +
			// linear-probe tables (e.g. ValueDictionary) on sequential IDs. The Fibonacci mix spreads
			// bits across the full 32-bit range for ~1 extra cycle per probe. Harmless on prime-sized
			// tables (PooledSet, ValueSet, ConcurrentCacheStore) where FastMod already diffuses bits.
			return HashMixing.Mix(value!.GetHashCode());
		}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~HashMixingTests`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Utils/HashMixing.cs src/Prague.Core/Collections/IKeyComparer.cs tests/Prague.Core.Tests/DataStructures/HashMixingTests.cs
git commit -m "feat: HashMixing mix/unmix with verified Fibonacci inverse"
```

---

### Task 3: ConcurrentCacheStore surfaces the key hash

**Files:**
- Modify: `src/Prague.Core/Collections/ConcurrentCacheStore.cs` — `UpdateResult` struct (~line 1368), every `new UpdateResult(...)` construction site, `TryRemove`/`TryRemoveInternal` (~lines 73-115)
- Test: `tests/Prague.Core.Tests/Cache/ConcurrentCacheStoreHashingTests.cs` (extend)

**Interfaces:**
- Produces: `UpdateResult.KeyHash` (`int`, always `DefaultKeyComparer<TKey>.GetHashCode(key)`); `public bool TryRemove(TKey key, out TValue value, out int keyHash)`. Task 6 consumes both.

- [ ] **Step 1: Write the failing test**

Append to `ConcurrentCacheStoreHashingTests.cs`:

```csharp
	[Test]
	public void UpdateResultAndTryRemoveExposeDefaultComparerHash() {
		var store = new ConcurrentCacheStore<int, string>();
		var expected = default(DefaultKeyComparer<int>).GetHashCode(42);

		var r = store.AddOrUpdate(42, "v", static (_, _, _) => true);
		Assert.That(r.KeyHash, Is.EqualTo(expected));

		var r2 = store.AddOrUpdate(42, "w", static (_, _, _) => true);
		Assert.That(r2.KeyHash, Is.EqualTo(expected)); // update path too

		Assert.That(store.TryRemove(42, out _, out var removeHash), Is.True);
		Assert.That(removeHash, Is.EqualTo(expected));
	}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~ConcurrentCacheStoreHashingTests`
Expected: FAIL — compile error, no `KeyHash`, no 3-arg `TryRemove`.

- [ ] **Step 3: Implement**

`UpdateResult` gains the field (all existing ctor callers get the new argument — the compiler enumerates them; every construction site already has an `int hashcode`/`hashCode` local or parameter in scope):

```csharp
	public readonly struct UpdateResult {
		public readonly AddOrUpdateOperation Operation;
		public readonly TValue Value;
		public readonly TValue? OldValue;
		public readonly int KeyHash;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public UpdateResult(AddOrUpdateOperation operation, TValue newValue, TValue? oldValue, int keyHash) {
			Operation = operation;
			Value = newValue;
			OldValue = oldValue;
			KeyHash = keyHash;
		}
	}
```

`TryRemoveInternal` gains an `int hashCode` parameter and drops its internal `GetHashCode(key)` line; the public overloads compute it:

```csharp
	public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
		=> TryRemoveInternal(key, GetHashCode(key), out value, false, default);

	public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value, out int keyHash) {
		keyHash = GetHashCode(key);
		return TryRemoveInternal(key, keyHash, out value, false, default);
	}

	public bool TryRemove(KeyValuePair<TKey, TValue> item)
		=> TryRemoveInternal(item.Key, GetHashCode(item.Key), out _, true, item.Value);
```

- [ ] **Step 4: Run store tests to verify pass**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~ConcurrentCacheStore`
Expected: all PASS (hashing, locking, real-use-case suites — the struct change is additive).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/ConcurrentCacheStore.cs tests/Prague.Core.Tests/Cache/ConcurrentCacheStoreHashingTests.cs
git commit -m "feat: surface key hash from ConcurrentCacheStore mutations"
```

---

### Task 4: PooledSet pre-hashed Add/Remove

**Files:**
- Modify: `src/Prague.Core/Collections/PooledSet.cs:346-443` (`Add`/`Remove`)
- Test: `tests/Prague.Core.Tests/DataStructures/PooledSetPreHashedTests.cs` (create)

**Interfaces:**
- Produces: `internal bool Add(T item, int hashCode)`, `internal bool Remove(T item, int hashCode)` on `PooledSet<T, TKeyComparer>` — `hashCode` MUST equal the set's own `TKeyComparer.GetHashCode(item)`. Task 6 consumes them.

- [ ] **Step 1: Write the failing test**

```csharp
namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;

[TestFixture]
public class PooledSetPreHashedTests {
	[Test]
	public void PreHashedAddRemoveIsEquivalentToPublicPath() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		var comparer = default(DefaultKeyComparer<int>);
		for (var i = 0; i < 1000; i++)
			Assert.That(set.Add(i, comparer.GetHashCode(i)), Is.True);
		Assert.That(set.Count, Is.EqualTo(1000));
		for (var i = 0; i < 1000; i++)
			Assert.That(set.Contains(i), Is.True, $"missing {i}");
		Assert.That(set.Add(500, comparer.GetHashCode(500)), Is.False); // duplicate rejected
		for (var i = 0; i < 1000; i += 2)
			Assert.That(set.Remove(i, comparer.GetHashCode(i)), Is.True);
		Assert.That(set.Count, Is.EqualTo(500));
		for (var i = 1; i < 1000; i += 2)
			Assert.That(set.Contains(i), Is.True);
		set.Dispose();
	}
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~PooledSetPreHashedTests`
Expected: FAIL — compile error, no 2-arg `Add`.

- [ ] **Step 3: Implement**

In `PooledSet.cs`, the existing `Add(T item)` body starting at `var hashCode = GetHashCode(item);` becomes the pre-hashed method; the public method delegates. Identical treatment for `Remove`:

```csharp
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Add(T item) => Add(item, GetHashCode(item));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool Add(T item, int hashCode) {
		System.Diagnostics.Debug.Assert(hashCode == GetHashCode(item), "pre-hashed Add: hash produced by a different comparer");
		var tables = _tables;
		// ... existing body from `System.Diagnostics.Debug.Assert(!ReferenceEquals(tables, DisposedTables), ...)`
		// onward, completely unchanged ...
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(T item) => Remove(item, GetHashCode(item));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool Remove(T item, int hashCode) {
		System.Diagnostics.Debug.Assert(hashCode == GetHashCode(item), "pre-hashed Remove: hash produced by a different comparer");
		var tables = _tables;
		// ... existing body unchanged ...
	}
```

- [ ] **Step 4: Run set tests to verify pass**

Run: `dotnet test tests/Prague.Core.Tests -c Release --filter "FullyQualifiedName~PooledSet"`
Expected: PASS (new fixture + existing lifetime tests).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledSet.cs tests/Prague.Core.Tests/DataStructures/PooledSetPreHashedTests.cs
git commit -m "feat: pre-hashed PooledSet Add/Remove"
```

---

### Task 5: PooledBTree — Marvin unification, redundant-hash fix, pre-hashed entries

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` — `HashOf` (~line 276), append fast path (~line 734), `Add`/`Remove`/`Update` publics and their cores (~lines 709, 1003, 1179)
- Test: `tests/Prague.Generated.Tests/Cache/PooledBTreePreHashedTests.cs` (create; project already has IVT — `PooledBTreeTests` exercises this internal class)

**Interfaces:**
- Consumes: `HashMixing.Unmix` (Task 2).
- Produces: `internal bool Add(TIndex index, TValue value, int storeKeyHash)`, `internal bool Remove(TIndex index, TValue value, int storeKeyHash)`, `internal bool Update(TIndex oldIndex, TIndex newIndex, TValue value, int storeKeyHash)` — `storeKeyHash` is the store-form hash (`DefaultKeyComparer<TValue>`). Task 6 consumes them.

- [ ] **Step 1: Write the failing test**

```csharp
namespace Prague.Generated.Tests.Cache;

	using Prague.Core.Collections;

[TestFixture]
public class PooledBTreePreHashedTests {
	[Test]
	public void PreHashedMutationsMatchPublicOnes() {
		using var viaPublic = new PooledBTree<long, int>();
		using var viaPreHashed = new PooledBTree<long, int>();
		var comparer = default(DefaultKeyComparer<int>);

		// key = i % 100 → heavy duplicate runs → composite (key, hash) mode
		for (var i = 0; i < 10_000; i++) {
			Assert.That(viaPublic.Add(i % 100, i), Is.True);
			Assert.That(viaPreHashed.Add(i % 100, i, comparer.GetHashCode(i)), Is.True);
		}
		Assert.That(viaPreHashed.Length, Is.EqualTo(viaPublic.Length));

		// duplicate rejected identically
		Assert.That(viaPreHashed.Add(5, 5, comparer.GetHashCode(5)), Is.False);

		for (var i = 0; i < 10_000; i += 3) {
			Assert.That(viaPublic.Remove(i % 100, i), Is.True);
			Assert.That(viaPreHashed.Remove(i % 100, i, comparer.GetHashCode(i)), Is.True);
		}
		Assert.That(viaPreHashed.Length, Is.EqualTo(viaPublic.Length));

		// move a pair between keys
		Assert.That(viaPreHashed.Update(5, 999, 5, comparer.GetHashCode(5)), Is.True);
		Assert.That(viaPreHashed.Contains(999, 5), Is.True);
		Assert.That(viaPreHashed.Contains(5, 5), Is.False);
	}

	[Test]
	public void PreHashedStringValuesUseThreadedMarvinHash() {
		using var tree = new PooledBTree<int, string>();
		var comparer = default(DefaultKeyComparer<string>);
		for (var i = 0; i < 1000; i++) {
			var v = $"value-{i:D8}";
			Assert.That(tree.Add(i % 10, v, comparer.GetHashCode(v)), Is.True);
		}
		Assert.That(tree.Length, Is.EqualTo(1000));
		for (var i = 0; i < 1000; i += 2) {
			var v = $"value-{i:D8}";
			Assert.That(tree.Remove(i % 10, v, comparer.GetHashCode(v)), Is.True);
		}
		Assert.That(tree.Length, Is.EqualTo(500));
	}
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Prague.Generated.Tests -c Release --filter FullyQualifiedName~PooledBTreePreHashedTests`
Expected: FAIL — compile error, no 3-arg `Add`.

- [ ] **Step 3: Implement**

(a) **`HashOf` loses the string special-case** (Marvin unification — decision 4 of the spec). Replace the whole method body and rewrite its doc comment:

```csharp
	/// <summary>
	///   Null-tolerant raw (unmixed) hash of the value half — the tree's tiebreaker inside a
	///   run of equal index keys. Raw, not DefaultKeyComparer's Fibonacci-mixed form: the mix
	///   would destroy the value-order ↔ hash-order correlation that the O(1) monotonic-append
	///   fast path depends on for batch-stamped inserts (equal key, ascending id), and being a
	///   bijection it removes no collisions either. Strings use string.GetHashCode (Marvin32,
	///   ordinal-consistent, process-stable) — the same hash the store threads in precomputed,
	///   so pre-hashed entry points consume it for free (2026-07-20 single-hash spec; DJB2
	///   tiebreak retired). Hash ties are resolved by an Equals probe, so a collision costs
	///   time, never correctness. Null-safe on every path: a lock-free reader racing a shrink
	///   can observe a vacated slot as the ARGUMENT of a compare.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int HashOf(TValue value) {
		if (typeof(TValue).IsValueType)
			return value!.GetHashCode();
		return value is null ? 0 : value.GetHashCode();
	}
```

(b) **Append fast path** (~line 734): replace `cmpLast = HashOf(value).CompareTo(HashOf(last.Values[lastCount - 1]));` with `cmpLast = valueHash.CompareTo(HashOf(last.Values[lastCount - 1]));` — `valueHash` is already the local computed at the top of `AddCore`.

(c) **Pre-hashed entries.** Refactor so each core takes the value hash as a parameter and each public wrapper computes it before taking the lock (Add already always hashed; Remove/Update previously deferred hashing to the composite path — the eager hash on the public wrappers is acceptable: after Task 6 the only production callers of the public overloads are `CompoundIndex` with `byte` values):

```csharp
	// Recover the raw tiebreak hash from a store-form (DefaultKeyComparer) hash. Value types
	// were Fibonacci-mixed by the store — one inverse multiply undoes it exactly. Ref types
	// (including strings) were never mixed — the store hash IS the raw hash. JIT-folded.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int RawHashFromStoreHash(int storeKeyHash) =>
		typeof(TValue).IsValueType ? HashMixing.Unmix(storeKeyHash) : storeKeyHash;

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Add(TIndex index, TValue value) {
		var valueHash = HashOf(value);
		lock (_writeLock) {
			return AddCore(index, value, valueHash);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal bool Add(TIndex index, TValue value, int storeKeyHash) {
		var valueHash = RawHashFromStoreHash(storeKeyHash);
		System.Diagnostics.Debug.Assert(valueHash == HashOf(value), "pre-hashed Add: hash mismatch");
		lock (_writeLock) {
			return AddCore(index, value, valueHash);
		}
	}
```

Apply the same wrapper pattern to `Remove`/`RemoveCore` and `Update`/its core: the core's internal `var valueHash = HashOf(value);` lines become the parameter; nothing else in the cores changes. `Contains` and all read paths stay untouched. Add `using Prague.Core.Utils;` if not present (it is — `StringTools` was already imported; remove that import in Task 7 if it becomes unused).

- [ ] **Step 4: Run the btree suites to verify pass**

Run: `dotnet test tests/Prague.Generated.Tests -c Release --filter "FullyQualifiedName~PooledBTree"`
Expected: PASS — new fixture + `PooledBTreeTests` + `PooledBTreeDifferentialTests` (differential tests are order-agnostic; the Marvin switch only reorders within duplicate runs, which is documented-unspecified).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs tests/Prague.Generated.Tests/Cache/PooledBTreePreHashedTests.cs
git commit -m "feat: pre-hashed PooledBTree mutations; unify string tiebreak on Marvin32"
```

---

### Task 6: ICacheIndex cutover and InMemoryDataCache plumbing

**Files:**
- Modify: `src/Prague.Core/InMemoryDataCache.cs` — `ICacheIndex` (~line 12), fan-out loops (~lines 1388-1414, 1421-1445, 1462-1473), implementors: `CacheUniqueIndex` (~405-450), `CacheRangeIndex` (~524-547), `CacheKeySetIndex` (~726-747), 4× `LastUpdated*IndexAdapter` (~1031-1215)
- Modify: `src/Prague.Core/Indexing.cs` — `CacheKeyValueListIndex` (Add/Remove/Update + span-path per-key helpers, ~255-400), `CacheCollectionSymmetricKeyValueListIndex` (~738+)
- Test: existing suites in `tests/Prague.Core.Tests` + `tests/Prague.Generated.Tests` (update any direct callers of the internal members — the compiler enumerates them)

**Interfaces:**
- Consumes: `UpdateResult.KeyHash` / `TryRemove(key, out value, out keyHash)` (Task 3), `PooledSet.Add/Remove(item, hash)` (Task 4), `PooledBTree.Add/Remove/Update(..., storeKeyHash)` (Task 5).
- Produces: the new `ICacheIndex` shape — final state, no further consumers.

- [ ] **Step 1: Change the interface (this intentionally breaks the build; the compiler is the checklist)**

```csharp
public interface ICacheIndex<in TKey, in TValue> {
	internal void Add(TKey key, int keyHash, TValue value, long timestampMs);
	internal void Remove(TKey key, int keyHash, TValue value, long timestampMs);
	internal void Update(TKey key, int keyHash, TValue originalValue, TValue newValue, long timestampMs);
}
```

- [ ] **Step 2: Update the fan-out in `InMemoryDataCache`**

Both `AddOrUpdate(TKey, TValue, long, ...)` overloads:

```csharp
		foreach (var index in _indeces)
			if (r.Operation is AddOrUpdateOperation.Update) {
				// Only update if OldValue is not null
				if (r.OldValue is not null)
					index.Update(key, r.KeyHash, r.OldValue, r.Value, timestamp);
				else
					index.Add(key, r.KeyHash, r.Value, timestamp);
			}
			else {
				index.Add(key, r.KeyHash, r.Value, timestamp);
			}
```

`Remove(TKey, long, out TValue)`:

```csharp
		if (!_cache.TryRemove(key, out value, out var keyHash))
			return false;

		foreach (var index in _indeces)
			index.Remove(key, keyHash, value, timestampMs);
```

- [ ] **Step 3: Update every implementor (compiler-enumerated). Consumption rules:**

**Ignorers** — `CacheUniqueIndex`, `LastUpdatedIndexAdapter`, `LastUpdatedCustomTimeStampIndexAdapter`, `LastUpdatedFilteredIndexAdapter`, `LastUpdatedFilteredCustomTimeStampIndexAdapter`: add the `int keyHash` parameter, bodies unchanged (they hash group/index keys, not the main key).

**`CacheKeySetIndex`** — `_keys` is `PooledSet<TKey, DefaultKeyComparer<TKey>>` keyed by the main key:

```csharp
	public void Add(TKey key, int keyHash, TValue value, long timestampMs) {
		if (_predicate(key, value)) {
			lock (_lock) {
				_keys.Add(key, keyHash);
			}
		}
	}
```

(and symmetrically `Remove` → `_keys.Remove(key, keyHash)`; `Update` threads `keyHash` into whichever of Add/Remove it takes.)

**`CacheRangeIndex`**:

```csharp
	public void Add(TKey key, int keyHash, TValue value, long timestampMs) {
		_index.Add(_keySelector(key, value), key, keyHash);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key, int keyHash, TValue value, long timestamp) {
		_index.Remove(_keySelector(key, value), key, keyHash);
	}

	public void Update(TKey key, int keyHash, TValue originalValue, TValue newValue, long timestampMs) {
		var oldIndexKey = _keySelector(key, originalValue);
		var newIndexKey = _keySelector(key, newValue);
		if (EqualityComparer<TIndexKey>.Default.Equals(oldIndexKey, newIndexKey))
			return;
		_index.Update(oldIndexKey, newIndexKey, key, keyHash);
	}
```

(keep the existing culture-sensitivity comment above the equality check.)

**`CacheKeyValueListIndex`** — the inner `PooledSet` mutations thread the hash; the store lambdas' `TArgs` widens from `TKey` to `(TKey Key, int Hash)`:

```csharp
		var r = _cache.AddOrUpdate(newIndexKey,
			static (_, a) => { var s = new PooledSet<TKey, DefaultKeyComparer<TKey>>(); s.Add(a.Key, a.Hash); return s; },
			static (_, hs, a) => { hs.Add(a.Key, a.Hash); return hs; }, (Key: key, Hash: keyHash));
```

```csharp
		var rr = _cache.UpdateOrRemove(oldIndexKey, static (_, hs, a) => {
			hs.Remove(a.Key, a.Hash);
			if (hs.Count > 0)
				return (true, hs);
			hs.Dispose();
			return (false, null);
		}, (Key: key, Hash: keyHash));
```

The unified span path (scalar + collection modes) threads `keyHash` through its per-index-key helpers into the same pre-hashed `PooledSet` calls. `_cacheReverse` calls (`CacheUniqueIndex.Add(indexKey, keyHash, key, ts)`) pass the hash through untouched — the reverse store hashes `indexKey`, so it lands in an ignorer. `CacheCollectionSymmetricKeyValueListIndex` forwards `keyHash` into its forward/reverse `AddUnderKey`/`RemoveUnderKey` per-key set mutations the same way.

- [ ] **Step 4: Fix compiler-enumerated test callers**

Any test invoking the internal members directly gains a `default(DefaultKeyComparer<TKey>).GetHashCode(key)` argument. Run the build to enumerate: `dotnet build -c Release`.

- [ ] **Step 5: Run the affected suites**

Run: `dotnet test tests/Prague.Core.Tests -c Release`
Run: `dotnet test tests/Prague.Generated.Tests -c Release`
Expected: all PASS — behavior is invisible except fewer hash computations.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/InMemoryDataCache.cs src/Prague.Core/Indexing.cs tests/
git commit -m "feat: thread main-key hash through ICacheIndex maintenance"
```

---

### Task 7: Retire DJB2 if orphaned

**Files:**
- Modify: `src/Prague.Core/Utils/StringTools.cs` (delete `GetNonRandomizedHashCode` if unreferenced)

- [ ] **Step 1: Find remaining users**

Run: `grep -rn "GetNonRandomizedHashCode" src tests benchmarks`
Expected after Task 5: no `src` references outside `StringTools.cs` itself. If `ConcurrentSortedSet` or another consumer still calls it, STOP — leave `StringTools` untouched, note the consumer in the results doc, and skip Step 2.

- [ ] **Step 2: Delete the method (and its now-dead usings/tests, if any), then verify**

Run: `dotnet build -c Release && dotnet test tests/Prague.Core.Tests -c Release --filter FullyQualifiedName~StringTools`
Expected: build clean; any `StringTools` tests that pinned the deleted method are removed with it.

- [ ] **Step 3: Commit**

```bash
git add -A src/Prague.Core/Utils tests
git commit -m "chore: retire orphaned DJB2 string hash"
```

---

### Task 8: Full verification, after-numbers, results doc

**Files:**
- Modify: `docs/superpowers/plans/2026-07-20-single-hash-bench-results.md`

- [ ] **Step 1: Full test pass**

Run: `dotnet test -c Release`
Expected: every project green (Kafka integration tests may require local infra — if they fail on environment, note it and run the three unit projects individually).

- [ ] **Step 2: Re-run the benchmark**

Run: `dotnet run -c Release --project benchmarks/Prague.Benchmarks -- --filter '*CacheIndexMaintenance*'`
Copy the results table into the results doc under `## After (commit <sha>)`, followed by a `## Delta` section with a per-scenario mean-time and allocation comparison against the Task 1 baseline.

- [ ] **Step 3: Sanity-check the deltas**

Expected shape: `UpdateAll_String` shows the largest improvement; `AddAll_Composite` the smallest; `RemoveAll_*` sits between its model's Add and Update; allocations unchanged or lower everywhere. If any scenario REGRESSED beyond noise (>2%), stop and investigate before committing — the prime suspect is the string-descent Marvin cost in duplicate runs (fallback per spec: revert `HashOf`'s string branch to DJB2 by re-adding the `typeof(TValue) == typeof(string)` arm and un-doing Task 7).

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/plans/2026-07-20-single-hash-bench-results.md
git commit -m "bench: single-hash index maintenance before/after results"
```

---

### Task 9: PooledBTree leaf-stored hashes (tripwire response)

Task 8's tripwire fired: composites −8.0/−9.0/−0.6%, strings +13.9/+18.8/+55.7% (two confirming
runs). Decision 5 (spec §6, user-approved): store the tiebreak hash next to each stored value so
composite-descent comparisons become array reads — instead of the DJB2-revert fallback.

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` (LeafNode, InternalNode, every
  Values/SepValues write/shift/copy/split/merge site, every `HashOf(<stored expression>)` call site)
- Test: `tests/Prague.Generated.Tests/Cache/PooledBTreePreHashedTests.cs` (extend if a new seam
  appears; the existing differential + churn suites are the main evidence)

**Interfaces:**
- Consumes: the Task 5 pre-hashed entry points (hash already in hand at insert).
- Produces: no signature changes — internal representation only.

- [ ] **Step 1: Add the arrays**

`LeafNode` gains `public int[] ValueHashes` rented at `LeafCapacity`; `InternalNode` gains
`public int[] SepValueHashes` rented at `InternalCapacity - 1`. Both rented in the ctor beside
their siblings, both returned in `ReturnToPool` with the same try/catch pattern (no clear needed
— int arrays).

- [ ] **Step 2: Maintain the invariant at every mutation site**

`ValueHashes[i] == HashOf(Values[i])` for live slots; `SepValueHashes` mirrors `SepValues`.
Mechanically: wherever `Values`/`SepValues` is written or moved (insert shift, append fast path,
split copy loops incl. the ThreadStatic split buffers — widen `_splitSepValuesBuf` handling with
a parallel `int[]` buffer — merge/borrow, separator updates, RepairPath), the hash array gets the
identical operation. Hash writes happen BEFORE the `Count`/link publication that makes a slot
reader-visible (same ordering as Values today).

- [ ] **Step 3: Replace stored-value HashOf call sites with array reads**

Internal-node composite child search (`HashOf(sepValues[mid])` → `sepValueHashes[mid]`), leaf
composite lower bound, `TryFindPair` probes, backwards prev-leaf scans, append-fast-path
last-element check, `RepairPath` (`HashOf(target.Values[0])` → `target.ValueHashes[0]`).
After this step the ONLY `HashOf` calls remaining are on incoming values (public wrappers,
`Contains`). Grep-verify: `grep -n "HashOf(" src/Prague.Core/Collections/PooledBTree.cs` — every
surviving hit must be an incoming-value site; list them in the report.

- [ ] **Step 4: Run the btree suites (Release AND Debug, both TFMs)**

Run: `dotnet test tests/Prague.Generated.Tests -c Release --filter "FullyQualifiedName~PooledBTree"`
Run: `dotnet test tests/Prague.Generated.Tests -c Debug --filter "FullyQualifiedName~PooledBTree"`
Run: `dotnet test tests/Prague.Core.Tests -c Debug` (index traffic through the tree with asserts)
Expected: all green — representation change is invisible.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs tests/Prague.Generated.Tests
git commit -m "perf: store tiebreak hashes in PooledBTree nodes"
```

---

### Task 10: Re-verify and record final numbers

Repeat Task 8 verbatim (same commands, same `-f net9.0`, same tripwire) at the new HEAD. The
`## After`/`## Delta` sections are written fresh against the ORIGINAL baseline. Additionally
record an intermediate note: the pre-Task-9 measurement (composites −8/−9/−0.6%, strings
+14/+19/+56%) lives in the ledger and task-8 report for the PR narrative. Expected shape now:
ALL scenarios ≤ baseline; strings should show the largest wins.

---

### Task 11: Specialize hash storage to ref-type values (second tripwire response)

Task 10 measured (vs original baseline): AddAll −4.9%/−26.0%, UpdateAll −5.8%/−44.0%,
RemoveAll_String −15.5% — but RemoveAll_Composite +2.3–6% over three runs, and composite-tree
allocations up (+4B/entry arrays never read). Cause: value-type `HashOf` is identity-class, so
stored hashes are pure carry cost there. Decision (user-approved): store hashes ONLY for
ref-type `TValue`; value types recompute on the fly (pre-Task-9 behavior, measured −8/−9/−0.6%).

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs`

**Interfaces:** none change — representation only, JIT-folded per closed generic.

- [ ] **Step 1: Gate the arrays**

House pattern (`PooledSet.AtomicCopy`): a JIT-folded static readonly flag:

```csharp
	// JIT-folded per closed generic: value-type tiebreak hashes are identity-class raw
	// GetHashCode — recomputing beats maintaining a third parallel array (Task 10 measured
	// RemoveAll_Composite +2.3–6% with always-on storage). Ref-type hashes (Marvin over
	// strings) are O(length): stored once at insert, read forever.
	private static readonly bool StoreValueHashes = !typeof(TValue).IsValueType;
```

`LeafNode.ValueHashes` / `InternalNode.SepValueHashes` become nullable, rented only when
`StoreValueHashes`, returned only when non-null.

- [ ] **Step 2: Gate every lockstep write site**

Each paired hash-array operation from Task 9 becomes `if (StoreValueHashes) { … }` (dead-code
folded per instantiation). The [ThreadStatic] parallel split scratch is only touched under the
flag.

- [ ] **Step 3: Unify reads through one helper**

```csharp
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int HashAt(TValue[] values, int[]? hashes, int i) =>
		StoreValueHashes ? hashes![i] : HashOf(values[i]);
```

(adjust signature per call-site shape; separator variant symmetric). Every Task-9 array read
routes through it, so value-type instantiations fold back to exactly the pre-Task-9 codegen.

- [ ] **Step 4: Tests (Release AND Debug, both TFMs) and commit**

Run: `dotnet test tests/Prague.Generated.Tests -c Release --filter "FullyQualifiedName~PooledBTree"`
Run: `dotnet test tests/Prague.Generated.Tests -c Debug --filter "FullyQualifiedName~PooledBTree"`
Run: `dotnet test tests/Prague.Core.Tests -c Debug`
Expected: all green.

```bash
git add src/Prague.Core/Collections/PooledBTree.cs
git commit -m "perf: gate PooledBTree hash storage to ref-type values"
```

---

### Task 12: Final measurement

Repeat Task 10 verbatim at the new HEAD (same commands, `-f net9.0`, same tripwire, deltas vs
the ORIGINAL baseline). Expected: string scenarios keep −15…−44%; composite scenarios at or
below their pre-Task-9 marks (−8.0/−9.0/−0.6%); composite allocations back to baseline.

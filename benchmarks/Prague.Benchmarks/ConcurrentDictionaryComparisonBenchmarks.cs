namespace Prague.Benchmarks;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Core.Collections;

/// <summary>
///   Benchmarks comparing ConcurrentCacheStore vs ConcurrentDictionary
///   for ACTUAL use cases in the codebase
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConcurrentDictionaryComparisonBenchmarks {
	[Params(1000, 10_000)] public int ItemCount { get; set; }

	#region Helper Classes

	private class TestDocument : IEquatable<TestDocument> {
		public int Id { get; set; }
		public string Value { get; set; } = string.Empty;

		public bool Equals(TestDocument? other) {
			if (other is null) return false;
			return Id == other.Id && Value == other.Value;
		}

		public override bool Equals(object? obj) {
			return Equals(obj as TestDocument);
		}

		public override int GetHashCode() {
			return HashCode.Combine(Id, Value);
		}
	}

	#endregion

	#region UseCase1: CacheKeyValueIndex - Simple Key-Value with always-update

	[Benchmark(Description = "CacheStore: Simple Add/Update (always update)")]
	public void CacheStore_SimpleAddUpdate() {
		var cache = new ConcurrentCacheStore<int, string>();

		for (var i = 0; i < ItemCount; i++) cache.AddOrUpdate(i, $"value-{i}", static (_, _, _) => true);

		// Update all
		for (var i = 0; i < ItemCount; i++) cache.AddOrUpdate(i, $"updated-{i}", static (_, _, _) => true);
	}

	[Benchmark(Description = "ConcurrentDict: Simple Add/Update")]
	public void ConcurrentDict_SimpleAddUpdate() {
		var dict = new ConcurrentDictionary<int, string>();

		for (var i = 0; i < ItemCount; i++) dict.AddOrUpdate(i, $"value-{i}", (_, _) => $"value-{i}");

		// Update all
		for (var i = 0; i < ItemCount; i++) dict.AddOrUpdate(i, $"updated-{i}", (_, _) => $"updated-{i}");
	}

	#endregion

	#region UseCase2: CacheKeyValueListIndex - ImmutableHashSet values

	[Benchmark(Description = "CacheStore: ImmutableHashSet Add")]
	public void CacheStore_ImmutableHashSet_Add() {
		var cache = new ConcurrentCacheStore<int, ImmutableHashSet<int>>();

		// Add items to 100 different keys
		for (var i = 0; i < ItemCount; i++) {
			var key = i % 100;
			cache.AddOrUpdate(
				key,
				static (_, item) => ImmutableHashSet.Create(item),
				static (_, hashset, item) => hashset.Add(item),
				i
			);
		}
	}

	[Benchmark(Description = "ConcurrentDict: ImmutableHashSet Add")]
	public void ConcurrentDict_ImmutableHashSet_Add() {
		var dict = new ConcurrentDictionary<int, ImmutableHashSet<int>>();

		// Add items to 100 different keys
		for (var i = 0; i < ItemCount; i++) {
			var key = i % 100;
			dict.AddOrUpdate(
				key,
				_ => ImmutableHashSet.Create(i),
				(_, hashset) => hashset.Add(i)
			);
		}
	}

	[Benchmark(Description = "CacheStore: ImmutableHashSet Remove")]
	public void CacheStore_ImmutableHashSet_Remove() {
		var cache = new ConcurrentCacheStore<int, ImmutableHashSet<int>>();

		// Pre-populate
		for (var i = 0; i < ItemCount; i++) {
			var key = i % 100;
			cache.AddOrUpdate(
				key,
				static (_, item) => ImmutableHashSet.Create(item),
				static (_, hashset, item) => hashset.Add(item),
				i
			);
		}

		// Remove items
		for (var i = 0; i < ItemCount / 2; i++) {
			var key = i % 100;
			cache.UpdateOrIgnore(
				key,
				static (_, hashset, item) => hashset.Remove(item),
				i
			);
		}
	}

	[Benchmark(Description = "ConcurrentDict: ImmutableHashSet Remove")]
	public void ConcurrentDict_ImmutableHashSet_Remove() {
		var dict = new ConcurrentDictionary<int, ImmutableHashSet<int>>();

		// Pre-populate
		for (var i = 0; i < ItemCount; i++) {
			var key = i % 100;
			dict.AddOrUpdate(
				key,
				_ => ImmutableHashSet.Create(i),
				(_, hashset) => hashset.Add(i)
			);
		}

		// Remove items (using TryGetValue + TryUpdate pattern)
		for (var i = 0; i < ItemCount / 2; i++) {
			var key = i % 100;
			while (true)
				if (dict.TryGetValue(key, out var oldValue)) {
					var newValue = oldValue.Remove(i);
					if (dict.TryUpdate(key, newValue, oldValue))
						break;
				}
				else {
					break;
				}
		}
	}

	#endregion

	#region UseCase3: InMemoryDataCache - Equality-based updates

	[Benchmark(Description = "CacheStore: Equality-based Update")]
	public void CacheStore_EqualityBasedUpdate() {
		var cache = new ConcurrentCacheStore<int, TestDocument>();

		// Add initial documents
		for (var i = 0; i < ItemCount; i++)
			cache.AddOrUpdate(i, new TestDocument { Id = i, Value = "v1" }, static (_, _, _) => true);

		// Update only if different
		for (var i = 0; i < ItemCount; i++) {
			var newDoc = new TestDocument { Id = i, Value = i % 2 == 0 ? "v1" : "v2" };
			cache.AddOrUpdate(i, newDoc, static (_, old, newV) => !old.Equals(newV));
		}
	}

	[Benchmark(Description = "ConcurrentDict: Equality-based Update")]
	public void ConcurrentDict_EqualityBasedUpdate() {
		var dict = new ConcurrentDictionary<int, TestDocument>();

		// Add initial documents
		for (var i = 0; i < ItemCount; i++)
			dict.AddOrUpdate(i, new TestDocument { Id = i, Value = "v1" },
				(_, _) => new TestDocument { Id = i, Value = "v1" });

		// Update only if different
		for (var i = 0; i < ItemCount; i++) {
			var newDoc = new TestDocument { Id = i, Value = i % 2 == 0 ? "v1" : "v2" };
			dict.AddOrUpdate(i, newDoc, (_, old) => old.Equals(newDoc) ? old : newDoc);
		}
	}

	#endregion

	#region UseCase4: Concurrent Reads

	[Benchmark(Description = "CacheStore: Concurrent Reads")]
	public void CacheStore_ConcurrentReads() {
		var cache = new ConcurrentCacheStore<int, int>();

		// Pre-populate
		for (var i = 0; i < ItemCount; i++) cache.AddOrUpdate(i, i * 2, static (_, _, _) => true);

		// Concurrent reads
		var tasks = new Task[4];
		for (var t = 0; t < 4; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < ItemCount; i++) cache.TryGetValue(i, out _);
			});
		Task.WaitAll(tasks);
	}

	[Benchmark(Description = "ConcurrentDict: Concurrent Reads")]
	public void ConcurrentDict_ConcurrentReads() {
		var dict = new ConcurrentDictionary<int, int>();

		// Pre-populate
		for (var i = 0; i < ItemCount; i++) dict.AddOrUpdate(i, i * 2, (_, _) => i * 2);

		// Concurrent reads
		var tasks = new Task[4];
		for (var t = 0; t < 4; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < ItemCount; i++) dict.TryGetValue(i, out _);
			});
		Task.WaitAll(tasks);
	}

	#endregion

	#region UseCase5: Mixed Read/Write

	[Benchmark(Description = "CacheStore: Mixed Read/Write")]
	public void CacheStore_MixedReadWrite() {
		var cache = new ConcurrentCacheStore<int, int>();

		// Pre-populate
		for (var i = 0; i < ItemCount / 2; i++) cache.AddOrUpdate(i, i, static (_, _, _) => true);

		var tasks = new Task[4];
		for (var t = 0; t < 4; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				var random = new Random(threadId);
				for (var i = 0; i < ItemCount / 4; i++) {
					var key = random.Next(ItemCount);
					if (random.Next(2) == 0)
						cache.TryGetValue(key, out _);
					else
						cache.AddOrUpdate(key, key, static (_, _, _) => true);
				}
			});
		}

		Task.WaitAll(tasks);
	}

	[Benchmark(Description = "ConcurrentDict: Mixed Read/Write")]
	public void ConcurrentDict_MixedReadWrite() {
		var dict = new ConcurrentDictionary<int, int>();

		// Pre-populate
		for (var i = 0; i < ItemCount / 2; i++) dict.AddOrUpdate(i, i, (_, _) => i);

		var tasks = new Task[4];
		for (var t = 0; t < 4; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				var random = new Random(threadId);
				for (var i = 0; i < ItemCount / 4; i++) {
					var key = random.Next(ItemCount);
					if (random.Next(2) == 0)
						dict.TryGetValue(key, out _);
					else
						dict.AddOrUpdate(key, key, (_, _) => key);
				}
			});
		}

		Task.WaitAll(tasks);
	}

	#endregion
}

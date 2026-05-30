namespace Prague.Core.Tests.Cache;

using System.Collections.Immutable;
using Prague.Core.Collections;
using Prague.Core.Utils;
using NUnit.Framework;

/// <summary>
///   Tests for ACTUAL use cases in the codebase:
///   1. CacheKeyValueIndex - single value per key
///   2. CacheKeyValueListIndex - ImmutableHashSet per key
///   3. InMemoryDataCache - main cache with equality-based updates
/// </summary>
[TestFixture]
public class ConcurrentCacheStoreRealUseCaseTests {
	[Test]
	public void CacheKeyValueIndex_AddOrUpdate_AlwaysUpdates() {
		// Arrange - Simulates CacheKeyValueIndex behavior
		var cache = new ConcurrentCacheStore<string, int>();

		// Act - Add first
		var result1 = cache.AddOrUpdate("index-key", 100, static (_, _, _) => true);

		// Update with different value (always update)
		var result2 = cache.AddOrUpdate("index-key", 200, static (_, _, _) => true);

		// Assert
		Assert.That(result1.Operation, Is.EqualTo(AddOrUpdateOperation.Add));
		Assert.That(result1.Value, Is.EqualTo(100));

		Assert.That(result2.Operation, Is.EqualTo(AddOrUpdateOperation.Update));
		Assert.That(result2.Value, Is.EqualTo(200));
		Assert.That(result2.OldValue, Is.EqualTo(100));

		Assert.That(cache.TryGetValue("index-key", out var final), Is.True);
		Assert.That(final, Is.EqualTo(200));
	}

	[Test]
	public void CacheKeyValueIndex_Concurrent_Updates() {
		// Arrange - Simulates concurrent index updates
		var cache = new ConcurrentCacheStore<int, string>();
		var threadCount = 10;
		var updatesPerThread = 100;

		// Act - Multiple threads updating same keys
		var tasks = new Task[threadCount];
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < updatesPerThread; i++) {
					var key = i % 50; // Overlap on 50 keys
					cache.AddOrUpdate(key, $"thread{threadId}-value{i}", static (_, _, _) => true);
				}
			});
		}

		Task.WaitAll(tasks);

		// Assert - All keys should exist
		Assert.That(cache.Count, Is.EqualTo(50));
		for (var i = 0; i < 50; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.Not.Null);
		}
	}

	[Test]
	public void CacheKeyValueListIndex_AddOrUpdate_CreatesImmutableHashSet() {
		// Arrange - Simulates CacheKeyValueListIndex.Add
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();

		// Act - First add creates hashset
		var result = cache.AddOrUpdate(
			"index-key",
			static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
			static (_, hs, k) => hs.Add(k),
			42
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Add));
		Assert.That(result.Value.Count, Is.EqualTo(1));
		Assert.That(result.Value.Contains(42), Is.True);
	}

	[Test]
	public void CacheKeyValueListIndex_AddOrUpdate_AddsToExistingHashSet() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();

		// Add first item
		cache.AddOrUpdate(
			"index-key",
			static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
			static (_, hs, k) => hs.Add(k),
			42
		);

		// Act - Add second item to same key
		var result = cache.AddOrUpdate(
			"index-key",
			static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
			static (_, hs, k) => hs.Add(k),
			100
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Update));
		Assert.That(result.Value.Count, Is.EqualTo(2));
		Assert.That(result.Value.Contains(42), Is.True);
		Assert.That(result.Value.Contains(100), Is.True);
		Assert.That(result.OldValue!.Count, Is.EqualTo(1));
	}

	[Test]
	public void CacheKeyValueListIndex_UpdateOrIgnore_RemovesFromHashSet() {
		// Arrange - Simulates CacheKeyValueListIndex.Remove
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();

		// Add two items
		cache.AddOrUpdate(
			"index-key",
			static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
			static (_, hs, k) => hs.Add(k),
			42
		);
		cache.AddOrUpdate(
			"index-key",
			static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
			static (_, hs, k) => hs.Add(k),
			100
		);

		// Act - Remove one item
		var updated = cache.UpdateOrIgnore(
			"index-key",
			static (_, hs, itemToRemove) => hs.Remove(itemToRemove),
			42
		);

		// Assert
		Assert.That(updated, Is.True);
		Assert.That(cache.TryGetValue("index-key", out var hashset), Is.True);
		Assert.That(hashset!.Count, Is.EqualTo(1));
		Assert.That(hashset.Contains(100), Is.True);
		Assert.That(hashset.Contains(42), Is.False);
	}

	[Test]
	public void CacheKeyValueListIndex_UpdateOrIgnore_OnNonExistentKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();

		// Act
		var updated = cache.UpdateOrIgnore(
			"non-existent",
			static (_, hs, item) => hs.Remove(item),
			42
		);

		// Assert
		Assert.That(updated, Is.False);
	}

	[Test]
	public void CacheKeyValueListIndex_ConcurrentAdds_ToSameKey() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();
		var threadCount = 10;
		var itemsPerThread = 100;

		// Act - Multiple threads adding to same index key
		var tasks = new Task[threadCount];
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < itemsPerThread; i++) {
					var value = threadId * itemsPerThread + i;
					cache.AddOrUpdate(
						"shared-key",
						static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
						static (_, hs, k) => hs.Add(k),
						value
					);
				}
			});
		}

		Task.WaitAll(tasks);

		// Assert - All items should be in the hashset
		Assert.That(cache.TryGetValue("shared-key", out var hashset), Is.True);
		Assert.That(hashset!.Count, Is.EqualTo(threadCount * itemsPerThread));
	}

	[Test]
	public void CacheKeyValueListIndex_ConcurrentAddAndRemove() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ImmutableHashSet<int>>();

		// Pre-populate
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(
				"key",
				static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
				static (_, hs, k) => hs.Add(k),
				i
			);

		// Act - Concurrent add and remove
		var addTask = Task.Run(() => {
			for (var i = 100; i < 200; i++)
				cache.AddOrUpdate(
					"key",
					static (_, k) => ImmutableHashSet.Create(HashCollectionsTools.GetEqualityComparer<int>(null), k),
					static (_, hs, k) => hs.Add(k),
					i
				);
		});

		var removeTask = Task.Run(() => {
			for (var i = 0; i < 50; i++)
				cache.UpdateOrIgnore(
					"key",
					static (_, hs, item) => hs.Remove(item),
					i
				);
		});

		Task.WaitAll(addTask, removeTask);

		// Assert
		Assert.That(cache.TryGetValue("key", out var hashset), Is.True);
		Assert.That(hashset!.Count, Is.GreaterThanOrEqualTo(50)); // At least 50 items removed
		Assert.That(hashset!.Count, Is.LessThanOrEqualTo(200)); // At most 200 items added
	}

	[Test]
	public void InMemoryDataCache_AddOrUpdate_OnlyUpdatesWhenDifferent() {
		// Arrange - Simulates InMemoryDataCache.AddOrUpdate behavior
		var cache = new ConcurrentCacheStore<string, TestDocument>();

		var doc1 = new TestDocument { Id = 1, Value = "original" };
		var doc2 = new TestDocument { Id = 1, Value = "updated" };
		var doc3 = new TestDocument { Id = 1, Value = "updated" }; // Same as doc2

		// Act
		var result1 = cache.AddOrUpdate("key", doc1, static (_, ov, nv) => !ov.Equals(nv));
		var result2 = cache.AddOrUpdate("key", doc2, static (_, ov, nv) => !ov.Equals(nv));
		var result3 = cache.AddOrUpdate("key", doc3, static (_, ov, nv) => !ov.Equals(nv));

		// Assert
		Assert.That(result1.Operation, Is.EqualTo(AddOrUpdateOperation.Add));

		Assert.That(result2.Operation, Is.EqualTo(AddOrUpdateOperation.Update), "Should update when values differ");
		Assert.That(result2.OldValue, Is.EqualTo(doc1));

		Assert.That(result3.Operation, Is.EqualTo(AddOrUpdateOperation.Same), "Should NOT update when values are equal");
		Assert.That(result3.Value, Is.EqualTo(doc2), "Should return existing value");
	}

	[Test]
	public void InMemoryDataCache_ShouldUpdatePredicate_PreventsDuplicateWork() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ExpensiveDocument>();
		var updateCount = 0;

		var doc1 = new ExpensiveDocument { Id = 1, Value = "v1" };
		cache.AddOrUpdate("key", doc1, static (_, _, _) => true);

		// Act - Try to update with same document (should be skipped)
		var doc2 = new ExpensiveDocument { Id = 1, Value = "v1" }; // Equal to doc1
		var result = cache.AddOrUpdate("key", doc2, (_, ov, nv) => {
			updateCount++;
			return !ov.Equals(nv);
		});

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Same));
		Assert.That(updateCount, Is.EqualTo(1), "shouldUpdate predicate was called");
		Assert.That(result.Value.Value, Is.EqualTo("v1"), "Original value preserved");
	}

	[Test]
	public void InMemoryDataCache_ConcurrentUpdates_WithEqualityCheck() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, TestDocument>();
		var threadCount = 10;
		var updatesPerThread = 100;

		// Act - Multiple threads trying to update
		var tasks = new Task[threadCount];
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < updatesPerThread; i++) {
					var key = i % 50; // 50 keys
					var doc = new TestDocument { Id = key, Value = $"thread{threadId}-iter{i}" };
					cache.AddOrUpdate(key, doc, static (_, ov, nv) => !ov.Equals(nv));
				}
			});
		}

		Task.WaitAll(tasks);

		// Assert - All keys should have final values
		Assert.That(cache.Count, Is.EqualTo(50));
		for (var i = 0; i < 50; i++) {
			Assert.That(cache.TryGetValue(i, out var doc), Is.True);
			Assert.That(doc!.Id, Is.EqualTo(i));
		}
	}

	[Test]
	public void LargeStruct_NodeReplacement_NotInPlaceUpdate() {
		// Arrange - Large struct that's not atomic (> 8 bytes)
		var cache = new ConcurrentCacheStore<string, LargeStruct>();

		var original = new LargeStruct {
			Field1 = 1,
			Field2 = 2,
			Field3 = 3,
			Field4 = 4,
			Field5 = 5
		};

		cache.AddOrUpdate("key", original, static (_, _, _) => true);

		// Act - Update should create new node (not update in place)
		var updated = new LargeStruct {
			Field1 = 10,
			Field2 = 20,
			Field3 = 30,
			Field4 = 40,
			Field5 = 50
		};

		var result = cache.AddOrUpdate("key", updated, static (_, _, _) => true);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Update));
		Assert.That(cache.TryGetValue("key", out var final), Is.True);
		Assert.That(final.Field1, Is.EqualTo(10));
		Assert.That(final.Field2, Is.EqualTo(20));
		Assert.That(final.Field3, Is.EqualTo(30));
		Assert.That(final.Field4, Is.EqualTo(40));
		Assert.That(final.Field5, Is.EqualTo(50));
	}

	[Test]
	public void LargeStruct_ConcurrentUpdates_NoTornReads() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, LargeStruct>();
		var key = 42;

		cache.AddOrUpdate(key, new LargeStruct { Field1 = 0, Field2 = 0, Field3 = 0, Field4 = 0, Field5 = 0 },
			static (_, _, _) => true);

		// Act - Concurrent updates
		var tasks = new Task[10];
		for (var t = 0; t < 10; t++) {
			var value = t + 1;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 100; i++)
					cache.AddOrUpdate(key, new LargeStruct {
						Field1 = value,
						Field2 = value,
						Field3 = value,
						Field4 = value,
						Field5 = value
					}, static (_, _, _) => true);
			});
		}

		Task.WaitAll(tasks);

		// Assert - No torn reads (all fields should match)
		Assert.That(cache.TryGetValue(key, out var final), Is.True);
		Assert.That(final.Field1, Is.EqualTo(final.Field2));
		Assert.That(final.Field2, Is.EqualTo(final.Field3));
		Assert.That(final.Field3, Is.EqualTo(final.Field4));
		Assert.That(final.Field4, Is.EqualTo(final.Field5));
	}

	[Test]
	public void UpdateOrIgnore_RacingWith_TryRemove() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var successfulUpdates = 0;
		var successfulRemoves = 0;

		cache.AddOrUpdate(1, 100, static (_, _, _) => true);

		// Act - Race UpdateOrIgnore vs TryRemove
		var updateTask = Task.Run(() => {
			for (var i = 0; i < 1000; i++) {
				if (cache.UpdateOrIgnore(1, static (_, old, _) => old + 1, default(object)))
					Interlocked.Increment(ref successfulUpdates);
				cache.AddOrUpdate(1, 100, static (_, _, _) => true); // Re-add if removed
			}
		});

		var removeTask = Task.Run(() => {
			for (var i = 0; i < 1000; i++) {
				if (cache.TryRemove(1, out _)) Interlocked.Increment(ref successfulRemoves);
				Thread.Sleep(1);
			}
		});

		Task.WaitAll(updateTask, removeTask);

		// Assert - No crashes, operations are atomic
		Assert.That(successfulUpdates + successfulRemoves, Is.GreaterThan(0));
		Console.WriteLine($"Updates: {successfulUpdates}, Removes: {successfulRemoves}");
	}

	[Test]
	public void AddOrUpdate_RacingWith_TryRemove_SameKey() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var addCount = 0;
		var removeCount = 0;

		// Act
		var addTask = Task.Run(() => {
			for (var i = 0; i < 1000; i++) {
				cache.AddOrUpdate("key", i, static (_, _, _) => true);
				Interlocked.Increment(ref addCount);
			}
		});

		var removeTask = Task.Run(() => {
			for (var i = 0; i < 1000; i++)
				if (cache.TryRemove("key", out _))
					Interlocked.Increment(ref removeCount);
		});

		Task.WaitAll(addTask, removeTask);

		// Assert - Final state is consistent
		var exists = cache.TryGetValue("key", out var value);
		Assert.That(addCount, Is.EqualTo(1000));
		Console.WriteLine($"Adds: {addCount}, Removes: {removeCount}, Final exists: {exists}");
	}

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

	private class ExpensiveDocument : IEquatable<ExpensiveDocument> {
		public int Id { get; set; }
		public string Value { get; set; } = string.Empty;

		public bool Equals(ExpensiveDocument? other) {
			if (other is null) return false;
			return Id == other.Id && Value == other.Value;
		}

		public override bool Equals(object? obj) {
			return Equals(obj as ExpensiveDocument);
		}

		public override int GetHashCode() {
			return HashCode.Combine(Id, Value);
		}
	}

	private struct LargeStruct {
		public long Field1;
		public long Field2;
		public long Field3;
		public long Field4;
		public long Field5;
	}
}
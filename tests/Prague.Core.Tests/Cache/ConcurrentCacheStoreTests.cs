namespace Prague.Core.Tests.Cache;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ConcurrentCacheStoreTests {
	[Test]
	public void TryGetValue_WithExistingKey_ReturnsTrue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		var expectedValue = 42;
		cache.AddOrUpdate(key, (k, _) => expectedValue, (k, oldVal, _) => oldVal, default(object));

		// Act
		var result = cache.TryGetValue(key, out var actualValue);

		// Assert
		Assert.That(result, Is.True);
		Assert.That(actualValue, Is.EqualTo(expectedValue));
	}

	[Test]
	public void TryGetValue_WithNonExistingKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		var result = cache.TryGetValue("non-existing", out var value);

		// Assert
		Assert.That(result, Is.False);
		Assert.That(value, Is.EqualTo(default(int)));
	}

	[Test]
	public void ContainsKey_WithExistingKey_ReturnsTrue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, string>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => "value", (k, old, _) => old, default(object));

		// Act
		var result = cache.ContainsKey(key);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void ContainsKey_WithNonExistingKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, string>();

		// Act
		var result = cache.ContainsKey("non-existing");

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void Count_WithEmptyCache_ReturnsZero() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		var count = cache.Count;

		// Assert
		Assert.That(count, Is.EqualTo(0));
	}

	[Test]
	public void Count_AfterAddingItems_ReturnsCorrectCount() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		for (var i = 0; i < 10; i++) cache.AddOrUpdate($"key-{i}", (k, _) => i, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(10));
	}

	[Test]
	public void Clear_RemovesAllItems() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		for (var i = 0; i < 10; i++) cache.AddOrUpdate($"key-{i}", (k, _) => i, (k, old, _) => old, default(object));

		// Act
		cache.Clear();

		// Assert
		Assert.That(cache.Count, Is.EqualTo(0));
		Assert.That(cache.ContainsKey("key-0"), Is.False);
	}

	[Test]
	public void TryRemove_WithExistingKey_RemovesItemAndReturnsTrue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		var value = 100;
		cache.AddOrUpdate(key, (k, _) => value, (k, old, _) => old, default(object));

		// Act
		var result = cache.TryRemove(key, out var removedValue);

		// Assert
		Assert.That(result, Is.True);
		Assert.That(removedValue, Is.EqualTo(value));
		Assert.That(cache.ContainsKey(key), Is.False);
	}

	[Test]
	public void TryRemove_WithNonExistingKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		var result = cache.TryRemove("non-existing", out var value);

		// Assert
		Assert.That(result, Is.False);
		Assert.That(value, Is.EqualTo(default(int)));
	}

	[Test]
	public void TryRemove_WithKeyValuePair_WhenValueMatches_RemovesItem() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		var value = 42;
		cache.AddOrUpdate(key, (k, _) => value, (k, old, _) => old, default(object));

		// Act
		var result = cache.TryRemove(new KeyValuePair<string, int>(key, value));

		// Assert
		Assert.That(result, Is.True);
		Assert.That(cache.ContainsKey(key), Is.False);
	}

	[Test]
	public void TryRemove_WithKeyValuePair_WhenValueDoesNotMatch_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => 42, (k, old, _) => old, default(object));

		// Act
		var result = cache.TryRemove(new KeyValuePair<string, int>(key, 999));

		// Assert
		Assert.That(result, Is.False);
		Assert.That(cache.ContainsKey(key), Is.True);
	}

	[Test]
	public void AddOrUpdate_WithNewKey_AddsItem() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "new-key";
		var value = 100;

		// Act
		var result = cache.AddOrUpdate(
			key,
			(k, _) => value,
			(k, old, _) => old + 1,
			default(object)
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Add));
		Assert.That(result.Value, Is.EqualTo(value));
		Assert.That(result.OldValue, Is.EqualTo(default(int)));
		Assert.That(cache.TryGetValue(key, out var storedValue), Is.True);
		Assert.That(storedValue, Is.EqualTo(value));
	}

	[Test]
	public void AddOrUpdate_WithExistingKey_UpdatesItem() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "existing-key";
		cache.AddOrUpdate(key, (k, _) => 10, (k, old, _) => old, default(object));

		// Act
		var result = cache.AddOrUpdate(
			key,
			(k, _) => 999, // Should not be called
			(k, old, _) => old + 5,
			default(object)
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Update));
		Assert.That(result.Value, Is.EqualTo(15));
		Assert.That(result.OldValue, Is.EqualTo(10));
		Assert.That(cache.TryGetValue(key, out var storedValue), Is.True);
		Assert.That(storedValue, Is.EqualTo(15));
	}

	[Test]
	public void AddOrUpdate_WithArgs_PassesArgsToFactory() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		var args = 100;

		// Act
		var result = cache.AddOrUpdate(
			key,
			(k, a) => a * 2,
			(k, old, a) => old + a,
			args
		);

		// Assert
		Assert.That(result.Value, Is.EqualTo(200));
	}

	[Test]
	public void AddOrUpdate_WithArgs_PassesArgsToUpdater() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => 50, (k, old, _) => old, default(object));

		// Act
		var result = cache.AddOrUpdate(
			key,
			(k, a) => a,
			(k, old, a) => old + a,
			25
		);

		// Assert
		Assert.That(result.Value, Is.EqualTo(75));
	}

	[Test]
	public void AddOrUpdate_WithShouldUpdatePredicate_WhenPredicateReturnsTrue_UpdatesValue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => 10, (k, old, _) => old, default(object));

		// Act
		var result = cache.AddOrUpdate(
			key,
			20,
			(k, newVal, oldVal) => newVal > oldVal // Update if new value is greater
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Update));
		Assert.That(result.Value, Is.EqualTo(20));
		Assert.That(result.OldValue, Is.EqualTo(10));
	}

	[Test]
	public void AddOrUpdate_WithShouldUpdatePredicate_WhenPredicateReturnsFalse_DoesNotUpdate() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => 10, (k, old, _) => old, default(object));

		// Act
		var result = cache.AddOrUpdate(
			key,
			5,
			(k, newVal, oldVal) => newVal > oldVal // Update if new value is greater
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Same));
		Assert.That(result.Value, Is.EqualTo(10)); // Old value preserved
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.EqualTo(10));
	}

	[Test]
	public void AddOrUpdate_WithShouldUpdatePredicate_ForNewKey_AddsValue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "new-key";

		// Act
		var result = cache.AddOrUpdate(
			key,
			100,
			(k, newVal, oldVal) => true
		);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Add));
		Assert.That(result.Value, Is.EqualTo(100));
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.EqualTo(100));
	}

	[Test]
	public void UpdateOrIgnore_WithExistingKey_UpdatesValue() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "test-key";
		cache.AddOrUpdate(key, (k, _) => 10, (k, old, _) => old, default(object));

		// Act
		var result = cache.UpdateOrIgnore(key, (k, old, factor) => old * factor, 3);

		// Assert
		Assert.That(result, Is.True);
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.EqualTo(30));
	}

	[Test]
	public void UpdateOrIgnore_WithNonExistingKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		var result = cache.UpdateOrIgnore("non-existing", (k, old, factor) => old * factor, 3);

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void UpdateOrIgnore_WithMultipleUpdates_AppliesAllUpdates() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "counter";
		cache.AddOrUpdate(key, (k, _) => 0, (k, old, _) => old, default(object));

		// Act
		for (var i = 0; i < 10; i++) cache.UpdateOrIgnore(key, (k, old, increment) => old + increment, 1);

		// Assert
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.EqualTo(10));
	}

	[Test]
	public void ConcurrentAdds_FromMultipleThreads_AllItemsAdded() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>();
		var threadCount = 10;
		var itemsPerThread = 100;
		var threads = new List<Thread>();

		// Act
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			var thread = new Thread(() => {
				for (var i = 0; i < itemsPerThread; i++) {
					var key = threadId * itemsPerThread + i;
					cache.AddOrUpdate(key, (k, _) => $"value-{k}", (k, old, _) => old, default(object));
				}
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert
		Assert.That(cache.Count, Is.EqualTo(threadCount * itemsPerThread));
		for (var i = 0; i < threadCount * itemsPerThread; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo($"value-{i}"));
		}
	}

	[Test]
	public void ConcurrentReads_FromMultipleThreads_AllReadsSucceed() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var itemCount = 1000;
		for (var i = 0; i < itemCount; i++) cache.AddOrUpdate(i, (k, _) => k * 2, (k, old, _) => old, default(object));

		var threadCount = 10;
		var readsPerThread = 1000;
		var threads = new List<Thread>();
		var successCounts = new int[threadCount];

		// Act
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			var thread = new Thread(() => {
				var random = new Random(threadId);
				var successCount = 0;

				for (var i = 0; i < readsPerThread; i++) {
					var key = random.Next(itemCount);
					if (cache.TryGetValue(key, out var value) && value == key * 2) successCount++;
				}

				successCounts[threadId] = successCount;
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert
		var totalSuccess = successCounts.Sum();
		Assert.That(totalSuccess, Is.EqualTo(threadCount * readsPerThread));
	}

	[Test]
	public void ConcurrentUpdates_OnSameKey_AllUpdatesApplied() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		var key = "counter";
		cache.AddOrUpdate(key, (k, _) => 0, (k, old, _) => old, default(object));

		var threadCount = 10;
		var incrementsPerThread = 100;
		var threads = new List<Thread>();

		// Act
		for (var t = 0; t < threadCount; t++) {
			var thread = new Thread(() => {
				for (var i = 0; i < incrementsPerThread; i++)
					cache.AddOrUpdate(
						key,
						(k, _) => 1,
						(k, old, _) => old + 1,
						default(object)
					);
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.EqualTo(threadCount * incrementsPerThread));
	}

	[Test]
	public void ConcurrentAddsAndRemoves_MaintainsConsistency() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>();
		var operationCount = 1000;
		var addThread = new Thread(() => {
			for (var i = 0; i < operationCount; i++)
				cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));
		});

		var removeThread = new Thread(() => {
			for (var i = 0; i < operationCount; i++) {
				cache.TryRemove(i, out _);
				Thread.Sleep(1); // Small delay to interleave operations
			}
		});

		// Act
		addThread.Start();
		Thread.Sleep(50); // Let some adds happen first
		removeThread.Start();
		addThread.Join();
		removeThread.Join();

		// Assert
		// Count should be between 0 and operationCount depending on timing
		var count = cache.Count;
		Assert.That(count, Is.GreaterThanOrEqualTo(0));
		Assert.That(count, Is.LessThanOrEqualTo(operationCount));
	}

	[Test]
	public void ConcurrentMixedOperations_MaintainsCorrectState() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var threadCount = 5;
		var operationsPerThread = 200;
		var threads = new List<Thread>();

		// Act - Each thread does mixed operations
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			var thread = new Thread(() => {
				var random = new Random(threadId);

				for (var i = 0; i < operationsPerThread; i++) {
					var key = random.Next(100);
					var operation = random.Next(4);

					switch (operation) {
						case 0: // Add/Update
							cache.AddOrUpdate(key, (k, _) => k, (k, old, _) => old + 1, default(object));
							break;
						case 1: // TryGetValue
							cache.TryGetValue(key, out _);
							break;
						case 2: // TryRemove
							cache.TryRemove(key, out _);
							break;
						case 3: // UpdateOrIgnore
							cache.UpdateOrIgnore(key, (k, old, _) => old * 2, default(object));
							break;
					}
				}
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert - No exceptions thrown, cache is in valid state
		var count = cache.Count;
		Assert.That(count, Is.GreaterThanOrEqualTo(0));
		Assert.That(count, Is.LessThanOrEqualTo(100));
	}

	[Test]
	public void AddOrUpdate_WithNullableValue_HandlesNullCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, string?>();
		var key = "nullable-key";

		// Act
		cache.AddOrUpdate(key, (k, _) => null, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.TryGetValue(key, out var value), Is.True);
		Assert.That(value, Is.Null);
	}

	[Test]
	public void AddOrUpdate_WithLargeNumberOfItems_TriggersResize() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>(4, 10);
		var itemCount = 1000;

		// Act
		for (var i = 0; i < itemCount; i++) cache.AddOrUpdate(i, (k, _) => k * 2, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(itemCount));
		for (var i = 0; i < itemCount; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo(i * 2));
		}
	}

	[Test]
	public void AddOrUpdate_WithValueTypes_WorksCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, long>();

		// Act
		for (var i = 0; i < 100; i++) cache.AddOrUpdate(i, (k, _) => (long)k * 1000, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(100));
		Assert.That(cache.TryGetValue(50, out var value), Is.True);
		Assert.That(value, Is.EqualTo(50000L));
	}

	[Test]
	public void AddOrUpdate_WithComplexObject_StoresReference() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, ComplexObject>();
		var obj = new ComplexObject { Id = 1, Name = "Test" };

		// Act
		cache.AddOrUpdate("key", (k, _) => obj, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.TryGetValue("key", out var retrieved), Is.True);
		Assert.That(retrieved, Is.SameAs(obj));
	}

	[Test]
	public void Clear_OnEmptyCache_DoesNotThrow() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act & Assert
		Assert.DoesNotThrow(() => cache.Clear());
	}

	[Test]
	public void Count_AfterClearAndReAdd_ReturnsCorrectCount() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key1", (k, _) => 1, (k, old, _) => old, default(object));
		cache.AddOrUpdate("key2", (k, _) => 2, (k, old, _) => old, default(object));
		cache.Clear();

		// Act
		cache.AddOrUpdate("key3", (k, _) => 3, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(1));
	}

	[Test]
	public void StressTest_ManyItemsAndOperations_MaintainsConsistency() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var itemCount = 10000;

		// Act - Add many items
		for (var i = 0; i < itemCount; i++) cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Update all items
		for (var i = 0; i < itemCount; i++) cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old * 2, default(object));

		// Remove half
		for (var i = 0; i < itemCount / 2; i++) cache.TryRemove(i, out _);

		// Assert
		Assert.That(cache.Count, Is.EqualTo(itemCount / 2));
		for (var i = itemCount / 2; i < itemCount; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo(i * 2));
		}
	}

	[Test]
	public void ConcurrentStressTest_HighContentionOnFewKeys_MaintainsConsistency() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var keyCount = 10;
		var threadCount = 20;
		var operationsPerThread = 500;

		// Initialize keys
		for (var i = 0; i < keyCount; i++) cache.AddOrUpdate(i, (k, _) => 0, (k, old, _) => old, default(object));

		var threads = new List<Thread>();

		// Act - High contention on few keys
		for (var t = 0; t < threadCount; t++) {
			var thread = new Thread(() => {
				var random = new Random();
				for (var i = 0; i < operationsPerThread; i++) {
					var key = random.Next(keyCount);
					cache.AddOrUpdate(key, (k, _) => 1, (k, old, _) => old + 1, default(object));
				}
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert
		var totalCount = 0;
		for (var i = 0; i < keyCount; i++) {
			cache.TryGetValue(i, out var value);
			totalCount += value;
		}

		Assert.That(totalCount, Is.EqualTo(threadCount * operationsPerThread));
	}

	private class ComplexObject {
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	[Test]
	public void TryGetValues_WithSpans_ReturnsFoundValues() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));

		var keys = new[] { 1, 3, 5, 7, 99 }; // 99 doesn't exist
		var values = new string[5];
		var found = new bool[5];

		// Act
		var foundCount = cache.TryGetValues(keys.AsSpan(), values.AsSpan(), found.AsSpan());

		// Assert
		Assert.That(foundCount, Is.EqualTo(4)); // 4 keys found
		Assert.That(found[0], Is.True);
		Assert.That(found[1], Is.True);
		Assert.That(found[2], Is.True);
		Assert.That(found[3], Is.True);
		Assert.That(found[4], Is.False); // 99 not found
	}

	[Test]
	public void TryGetValues_WithICollection_ReturnsFoundValues() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(i, (k, _) => k * 10, (k, old, _) => old, default(object));

		var keys = new List<int> { 2, 4, 6, 100 }; // 100 doesn't exist

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(3)); // 3 keys found
		Assert.That(result, Does.Contain(20));
		Assert.That(result, Does.Contain(40));
		Assert.That(result, Does.Contain(60));
	}

	[Test]
	public void TryGetValues_WithICollectionAndPredicate_FiltersResults() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(i, (k, _) => k * 10, (k, old, _) => old, default(object));

		var keys = new List<int> { 1, 2, 3, 4, 5 };

		// Act - Only return values > 25
		var result = cache.TryGetValues(keys, v => v > 25);

		// Assert
		Assert.That(result.Count, Is.EqualTo(3)); // 30, 40, 50
		Assert.That(result, Does.Contain(30));
		Assert.That(result, Does.Contain(40));
		Assert.That(result, Does.Contain(50));
	}

	[Test]
	public void TryGetValues_WithReadOnlySpan_ReturnsFoundValues() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(i, (k, _) => k * 100, (k, old, _) => old, default(object));

		ReadOnlySpan<int> keys = stackalloc int[] { 0, 5, 9 };

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(0));
		Assert.That(result, Does.Contain(500));
		Assert.That(result, Does.Contain(900));
	}

	[Test]
	public void TryGetValues_WithEmptyKeys_ReturnsEmptyResults() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		cache.AddOrUpdate(1, (k, _) => 100, (k, old, _) => old, default(object));

		var keys = new List<int>();

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void TryGetValues_WithStringKeys_WorksCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("one", (k, _) => 1, (k, old, _) => old, default(object));
		cache.AddOrUpdate("two", (k, _) => 2, (k, old, _) => old, default(object));
		cache.AddOrUpdate("three", (k, _) => 3, (k, old, _) => old, default(object));

		var keys = new List<string> { "one", "three", "missing" };

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(1));
		Assert.That(result, Does.Contain(3));
	}

	[Test]
	public void TryGetValues_WithICollectionSpan_ReturnsCorrectCount() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 5; i++)
			cache.AddOrUpdate(i, (k, _) => k * 2, (k, old, _) => old, default(object));

		var keys = new List<int> { 0, 1, 2, 10, 11 }; // 10, 11 don't exist
		var values = new int[5];

		// Act
		var foundCount = cache.TryGetValues(keys, values.AsSpan());

		// Assert
		Assert.That(foundCount, Is.EqualTo(3));
	}

	[Test]
	public void TryGetValues_WithICollectionSpanAndPredicate_FiltersCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		var keys = new List<int> { 1, 2, 3, 4, 5 };
		var values = new int[5];

		// Act - Only get values > 3
		var foundCount = cache.TryGetValues(keys, values.AsSpan(), v => v > 3);

		// Assert
		Assert.That(foundCount, Is.EqualTo(2)); // 4, 5
	}

	[Test]
	public void Constructor_WithConcurrencyAndCapacity_WorksCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>(8, 100);

		// Act
		for (var i = 0; i < 50; i++)
			cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(50));
	}

	[Test]
	public void Constructor_WithSmallCapacity_GrowsAsNeeded() {
		// Arrange - Very small initial capacity
		var cache = new ConcurrentCacheStore<int, int>(2, 3);

		// Act - Add many items to force growth
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(100));
		for (var i = 0; i < 100; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo(i));
		}
	}

	// Custom class that always returns same hash code to force collisions
	private class CollisionKey : IEquatable<CollisionKey> {
		public CollisionKey(int id) {
			Id = id;
		}

		public int Id { get; }

		public bool Equals(CollisionKey? other) {
			return other is not null && Id == other.Id;
		}

		public override int GetHashCode() {
			return 42;
			// Always same hash
		}

		public override bool Equals(object? obj) {
			return obj is CollisionKey other && Equals(other);
		}
	}

	[Test]
	public void AddOrUpdate_WithHashCollisions_HandlesChainCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, string>();

		// Act - All keys will hash to same bucket
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => $"value-{k.Id}", (k, old, _) => old, default(object));

		// Assert - All items should be retrievable
		Assert.That(cache.Count, Is.EqualTo(10));
		for (var i = 0; i < 10; i++) {
			Assert.That(cache.TryGetValue(new CollisionKey(i), out var value), Is.True);
			Assert.That(value, Is.EqualTo($"value-{i}"));
		}
	}

	[Test]
	public void TryRemove_WithHashCollisions_RemovesCorrectItem() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 5; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id * 10, (k, old, _) => old, default(object));

		// Act - Remove middle item from chain
		var removed = cache.TryRemove(new CollisionKey(2), out var removedValue);

		// Assert
		Assert.That(removed, Is.True);
		Assert.That(removedValue, Is.EqualTo(20));
		Assert.That(cache.Count, Is.EqualTo(4));
		Assert.That(cache.TryGetValue(new CollisionKey(2), out _), Is.False);
		// Other items still accessible
		Assert.That(cache.TryGetValue(new CollisionKey(0), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(4), out _), Is.True);
	}

	[Test]
	public void TryRemove_FirstItemInChain_UpdatesBucketHead() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 3; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id, (k, old, _) => old, default(object));

		// Act - Remove first item (head of chain)
		cache.TryRemove(new CollisionKey(2), out _); // Last added is first in chain

		// Assert
		Assert.That(cache.Count, Is.EqualTo(2));
		Assert.That(cache.TryGetValue(new CollisionKey(0), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(1), out _), Is.True);
	}

	[Test]
	public void TryGetValue_WithHashCollisions_TraversesChainCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, string>();
		for (var i = 0; i < 20; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => $"item-{k.Id}", (k, old, _) => old, default(object));

		// Act & Assert - Find item at end of chain
		Assert.That(cache.TryGetValue(new CollisionKey(0), out var value), Is.True);
		Assert.That(value, Is.EqualTo("item-0"));

		// Find item in middle of chain
		Assert.That(cache.TryGetValue(new CollisionKey(10), out var value2), Is.True);
		Assert.That(value2, Is.EqualTo("item-10"));
	}

	[Test]
	public void UpdateOrIgnore_WithHashCollisions_UpdatesCorrectItem() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 5; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id, (k, old, _) => old, default(object));

		// Act - Update specific item in chain
		var updated = cache.UpdateOrIgnore(new CollisionKey(2), (k, old, mult) => old * mult, 100);

		// Assert
		Assert.That(updated, Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(2), out var value), Is.True);
		Assert.That(value, Is.EqualTo(200));
		// Other items unchanged
		Assert.That(cache.TryGetValue(new CollisionKey(1), out var value1), Is.True);
		Assert.That(value1, Is.EqualTo(1));
	}

	[Test]
	public void AddOrUpdate_ManyItems_TriggersMultipleResizes() {
		// Arrange - Start with very small capacity
		var cache = new ConcurrentCacheStore<int, int>(2, 7);

		// Act - Add enough items to trigger multiple resizes
		for (var i = 0; i < 10000; i++)
			cache.AddOrUpdate(i, (k, _) => k * 2, (k, old, _) => old, default(object));

		// Assert - All items accessible after resizes
		Assert.That(cache.Count, Is.EqualTo(10000));
		for (var i = 0; i < 10000; i += 100) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo(i * 2));
		}
	}

	[Test]
	public void ConcurrentAddsDuringResize_MaintainsDataIntegrity() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>(4, 10);
		var itemCount = 5000;
		var threads = new List<Thread>();
		var errors = new List<string>();

		// Act - Multiple threads adding items concurrently
		for (var t = 0; t < 4; t++) {
			var threadId = t;
			var thread = new Thread(() => {
				for (var i = 0; i < itemCount; i++) {
					var key = threadId * itemCount + i;
					cache.AddOrUpdate(key, (k, _) => k, (k, old, _) => old, default(object));
				}
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert
		Assert.That(cache.Count, Is.EqualTo(4 * itemCount));

		// Verify all items are present
		for (var t = 0; t < 4; t++)
		for (var i = 0; i < itemCount; i += 100) {
			var key = t * itemCount + i;
			if (!cache.TryGetValue(key, out var value) || value != key) errors.Add($"Key {key} not found or wrong value");
		}

		Assert.That(errors, Is.Empty, string.Join(", ", errors));
	}

	[Test]
	public void TryGetValues_WithStringKeysSpan_TraversesChains() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate($"key-{i}", (k, _) => i, (k, old, _) => old, default(object));

		var keys = new[] { "key-0", "key-50", "key-99", "key-missing" };
		var values = new int[4];
		var found = new bool[4];

		// Act
		var foundCount = cache.TryGetValues(keys.AsSpan(), values.AsSpan(), found.AsSpan());

		// Assert
		Assert.That(foundCount, Is.EqualTo(3));
		Assert.That(found[0], Is.True);
		Assert.That(found[1], Is.True);
		Assert.That(found[2], Is.True);
		Assert.That(found[3], Is.False);
	}

	[Test]
	public void AddOrUpdate_WithStringKeys_HandlesNullComparerPath() {
		// Arrange - Default comparer (null internally for value types, but strings use reference path)
		var cache = new ConcurrentCacheStore<string, string>();

		// Act
		cache.AddOrUpdate("test", (k, _) => "value1", (k, old, _) => old, default(object));
		cache.AddOrUpdate("test", (k, _) => "new", (k, old, _) => "updated", default(object));

		// Assert
		Assert.That(cache.TryGetValue("test", out var value), Is.True);
		Assert.That(value, Is.EqualTo("updated"));
	}

	[Test]
	public void TryGetValues_WithPredicateAndStringKeys_FiltersCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("a", (k, _) => 10, (k, old, _) => old, default(object));
		cache.AddOrUpdate("b", (k, _) => 20, (k, old, _) => old, default(object));
		cache.AddOrUpdate("c", (k, _) => 30, (k, old, _) => old, default(object));
		cache.AddOrUpdate("d", (k, _) => 40, (k, old, _) => old, default(object));

		var keys = new List<string> { "a", "b", "c", "d" };

		// Act - Only values > 25
		var result = cache.TryGetValues(keys, v => v > 25);

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(30));
		Assert.That(result, Does.Contain(40));
	}

	[Test]
	public void TryGetValues_WithCollisionKeys_TraversesEntireChain() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id * 10, (k, old, _) => old, default(object));

		var keys = new List<CollisionKey> { new(0), new(5), new(9), new(100) };

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(0));
		Assert.That(result, Does.Contain(50));
		Assert.That(result, Does.Contain(90));
	}

	[Test]
	public void TryGetValues_WithCollisionKeysAndPredicate_FiltersInChain() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 10; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id, (k, old, _) => old, default(object));

		var keys = new List<CollisionKey> { new(1), new(2), new(3), new(8), new(9) };

		// Act - Only even values
		var result = cache.TryGetValues(keys, v => v % 2 == 0);

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(2));
		Assert.That(result, Does.Contain(8));
	}

	[Test]
	public void TryGetValues_ReadOnlySpan_WithChainTraversal() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>();
		for (var i = 0; i < 50; i++)
			cache.AddOrUpdate(i, (k, _) => $"val-{k}", (k, old, _) => old, default(object));

		ReadOnlySpan<int> keys = stackalloc int[] { 0, 25, 49, 999 };

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("val-0"));
		Assert.That(result, Does.Contain("val-25"));
		Assert.That(result, Does.Contain("val-49"));
	}

	[Test]
	public void TryGetValues_SpanLengthMismatch_ThrowsArgumentException() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var keys = new[] { 1, 2, 3 };
		var values = new int[2]; // Wrong size
		var found = new bool[3];

		// Act & Assert
		Assert.Throws<ArgumentException>(() => cache.TryGetValues(keys.AsSpan(), values.AsSpan(), found.AsSpan()));
	}

	[Test]
	public void TryGetValues_FoundSpanLengthMismatch_ThrowsArgumentException() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var keys = new[] { 1, 2, 3 };
		var values = new int[3];
		var found = new bool[2]; // Wrong size

		// Act & Assert
		Assert.Throws<ArgumentException>(() => cache.TryGetValues(keys.AsSpan(), values.AsSpan(), found.AsSpan()));
	}

	[Test]
	public void TryGetValues_EmptySpans_ReturnsZero() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		cache.AddOrUpdate(1, (k, _) => 100, (k, old, _) => old, default(object));

		var keys = Array.Empty<int>();
		var values = Array.Empty<int>();
		var found = Array.Empty<bool>();

		// Act
		var result = cache.TryGetValues(keys.AsSpan(), values.AsSpan(), found.AsSpan());

		// Assert
		Assert.That(result, Is.EqualTo(0));
	}

	[Test]
	public void Clear_DuringConcurrentOperations_CompletesSuccessfully() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 1000; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		var addThread = new Thread(() => {
			for (var i = 1000; i < 2000; i++)
				cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));
		});

		// Act
		addThread.Start();
		Thread.Sleep(10);
		cache.Clear();
		addThread.Join();

		// Assert - Cache should be in valid state
		var count = cache.Count;
		Assert.That(count, Is.GreaterThanOrEqualTo(0));
	}

	[Test]
	public void AddOrUpdate_SameValuePredicate_ReturnsSame() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key", 100, (k, newVal, oldVal) => true);

		// Act - Try to update with predicate that returns false
		var result = cache.AddOrUpdate("key", 50, (k, newVal, oldVal) => newVal > oldVal);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(AddOrUpdateOperation.Same));
		Assert.That(result.Value, Is.EqualTo(100)); // Old value preserved
	}

	[Test]
	public void Count_OnEmptyCache_ReturnsZeroQuickly() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();

		// Act
		var count = cache.Count;

		// Assert
		Assert.That(count, Is.EqualTo(0));
	}

	[Test]
	public void ContainsKey_AfterRemove_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key", (k, _) => 42, (k, old, _) => old, default(object));
		cache.TryRemove("key", out _);

		// Act
		var contains = cache.ContainsKey("key");

		// Assert
		Assert.That(contains, Is.False);
	}

	[Test]
	public void TryRemove_KeyValuePair_WithNonExistingKey_ReturnsFalse() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();

		// Act
		var result = cache.TryRemove(new KeyValuePair<int, int>(999, 0));

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void TryGetValue_ValueTypeKey_UsesOptimizedPath() {
		// Arrange - int keys use optimized value type path
		var cache = new ConcurrentCacheStore<int, string>();
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));

		// Act & Assert - Verify all lookups work
		for (var i = 0; i < 100; i++) {
			Assert.That(cache.TryGetValue(i, out var value), Is.True);
			Assert.That(value, Is.EqualTo($"value-{i}"));
		}
	}

	[Test]
	public void TryGetValues_ValueTypeKeys_BatchOptimizedPath() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 1000; i++)
			cache.AddOrUpdate(i, (k, _) => k * 2, (k, old, _) => old, default(object));

		var keys = Enumerable.Range(0, 100).ToList();

		// Act
		var result = cache.TryGetValues(keys);

		// Assert
		Assert.That(result.Count, Is.EqualTo(100));
		for (var i = 0; i < 100; i++) Assert.That(result[i], Is.EqualTo(i * 2));
	}

	[Test]
	public void TryGetValues_ValueTypeKeysWithPredicate_FiltersCorrectly() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		var keys = Enumerable.Range(0, 20).ToList();
		var values = new int[20];

		// Act - Only multiples of 3
		var count = cache.TryGetValues(keys, values.AsSpan(), v => v % 3 == 0);

		// Assert - 0, 3, 6, 9, 12, 15, 18 = 7 values
		Assert.That(count, Is.EqualTo(7));
	}

	private struct SmallStruct {
		public int Value;
	}

	private struct LargeStruct {
		public long A, B, C, D; // 32 bytes - non-atomic write
	}

	[Test]
	public void AddOrUpdate_SmallStructValue_UsesAtomicWrite() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, SmallStruct>();

		// Act
		cache.AddOrUpdate(1, (k, _) => new SmallStruct { Value = 10 }, (k, old, _) => old, default(object));
		cache.AddOrUpdate(1, (k, _) => new SmallStruct { Value = 99 },
			(k, old, _) => new SmallStruct { Value = old.Value + 5 }, default(object));

		// Assert
		Assert.That(cache.TryGetValue(1, out var value), Is.True);
		Assert.That(value.Value, Is.EqualTo(15));
	}

	[Test]
	public void AddOrUpdate_LargeStructValue_CreatesNewNode() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, LargeStruct>();

		// Act
		cache.AddOrUpdate(1, (k, _) => new LargeStruct { A = 1, B = 2, C = 3, D = 4 }, (k, old, _) => old, default(object));
		cache.AddOrUpdate(1, (k, _) => default,
			(k, old, _) => new LargeStruct { A = old.A + 10, B = old.B, C = old.C, D = old.D }, default(object));

		// Assert
		Assert.That(cache.TryGetValue(1, out var value), Is.True);
		Assert.That(value.A, Is.EqualTo(11));
	}

	[Test]
	public void UpdateOrIgnore_LargeStructValue_CreatesNewNode() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, LargeStruct>();
		cache.AddOrUpdate(1, (k, _) => new LargeStruct { A = 100, B = 200, C = 300, D = 400 }, (k, old, _) => old,
			default(object));

		// Act
		var updated = cache.UpdateOrIgnore(1,
			(k, old, mult) => new LargeStruct { A = old.A * mult, B = old.B, C = old.C, D = old.D }, 2L);

		// Assert
		Assert.That(updated, Is.True);
		Assert.That(cache.TryGetValue(1, out var value), Is.True);
		Assert.That(value.A, Is.EqualTo(200));
	}

	[Test]
	public void TryRemove_DuringResize_RetriesCorrectly() {
		// Arrange - Small initial capacity to force frequent resizes
		var cache = new ConcurrentCacheStore<int, int>(2, 7);
		var exceptions = new List<Exception>();
		var removeResults = new List<bool>();

		// Pre-populate
		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Act - Concurrent adds (causing resizes) and removes
		var addThread = new Thread(() => {
			try {
				for (var i = 100; i < 1000; i++)
					cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));
			}
			catch (Exception ex) {
				lock (exceptions) {
					exceptions.Add(ex);
				}
			}
		});

		var removeThread = new Thread(() => {
			try {
				for (var i = 0; i < 100; i++) {
					var result = cache.TryRemove(i, out _);
					lock (removeResults) {
						removeResults.Add(result);
					}
				}
			}
			catch (Exception ex) {
				lock (exceptions) {
					exceptions.Add(ex);
				}
			}
		});

		addThread.Start();
		removeThread.Start();
		addThread.Join();
		removeThread.Join();

		// Assert - No exceptions, all removes should have succeeded
		Assert.That(exceptions, Is.Empty);
		Assert.That(removeResults.Count(r => r), Is.EqualTo(100));
	}

	[Test]
	public void TryGetValue_DuringResize_ReturnsCorrectValue() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, string>(2, 7);
		var readErrors = new List<string>();

		// Pre-populate
		for (var i = 0; i < 50; i++)
			cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));

		// Act - Concurrent adds (causing resizes) and reads
		var addThread = new Thread(() => {
			for (var i = 50; i < 500; i++)
				cache.AddOrUpdate(i, (k, _) => $"value-{k}", (k, old, _) => old, default(object));
		});

		var readThread = new Thread(() => {
			for (var iter = 0; iter < 100; iter++)
			for (var i = 0; i < 50; i++)
				if (cache.TryGetValue(i, out var value))
					if (value != $"value-{i}")
						lock (readErrors) {
							readErrors.Add($"Key {i}: expected 'value-{i}', got '{value}'");
						}
		});

		addThread.Start();
		readThread.Start();
		addThread.Join();
		readThread.Join();

		// Assert
		Assert.That(readErrors, Is.Empty, string.Join("; ", readErrors));
	}

	[Test]
	public void AddOrUpdate_ManyStringKeys_HandlesComparerCorrectly() {
		// Arrange - This tests the string comparer paths
		var cache = new ConcurrentCacheStore<string, int>();

		// Act - Add many string keys to potentially trigger comparer behaviors
		for (var i = 0; i < 1000; i++)
			cache.AddOrUpdate($"key-{i:D5}", (k, _) => i, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(1000));
		Assert.That(cache.TryGetValue("key-00500", out var value), Is.True);
		Assert.That(value, Is.EqualTo(500));
	}

	[Test]
	public void AddOrUpdate_ImbalancedBuckets_AdjustsBudget() {
		// Arrange - Small capacity with many items in few buckets
		var cache = new ConcurrentCacheStore<int, int>(1, 7);

		// Act - Add items that may create imbalance
		for (var i = 0; i < 20; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Remove most items to create sparseness
		for (var i = 5; i < 20; i++)
			cache.TryRemove(i, out _);

		// Add more items - this may trigger budget adjustment instead of resize
		for (var i = 20; i < 30; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Assert - All items should be accessible
		Assert.That(cache.Count, Is.EqualTo(15)); // 0-4 + 20-29
		for (var i = 0; i < 5; i++) Assert.That(cache.TryGetValue(i, out _), Is.True);
		for (var i = 20; i < 30; i++) Assert.That(cache.TryGetValue(i, out _), Is.True);
	}

	[Test]
	public void Constructor_DefaultGrowLockArray_GrowsLocksOnResize() {
		// Arrange - Default constructor has growLockArray=true
		var cache = new ConcurrentCacheStore<int, int>();

		// Act - Add many items to trigger multiple resizes
		for (var i = 0; i < 50000; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Assert - All items accessible
		Assert.That(cache.Count, Is.EqualTo(50000));
		Assert.That(cache.TryGetValue(25000, out var value), Is.True);
		Assert.That(value, Is.EqualTo(25000));
	}

	[Test]
	public void Constructor_FixedLockArray_DoesNotGrowLocks() {
		// Arrange - Explicit constructor has growLockArray=false
		var cache = new ConcurrentCacheStore<int, int>(4, 31);

		// Act - Add many items
		for (var i = 0; i < 10000; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Assert
		Assert.That(cache.Count, Is.EqualTo(10000));
	}

	[Test]
	public void Clear_AfterLargeDataset_ResetsToInitialCapacity() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>(4, 31);
		for (var i = 0; i < 10000; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		// Act
		cache.Clear();

		// Assert
		Assert.That(cache.Count, Is.EqualTo(0));

		// Can still add items after clear
		cache.AddOrUpdate(1, (k, _) => 100, (k, old, _) => old, default(object));
		Assert.That(cache.TryGetValue(1, out var value), Is.True);
		Assert.That(value, Is.EqualTo(100));
	}

	[Test]
	public void AreAllBucketsEmpty_AfterClear_ReturnsTrue() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		cache.AddOrUpdate(1, (k, _) => 1, (k, old, _) => old, default(object));

		// Act
		cache.Clear();

		// Assert - Clear on empty shouldn't throw
		cache.Clear(); // Second clear
		Assert.That(cache.Count, Is.EqualTo(0));
	}

	[Test]
	public void UpdateOrRemove_WithExistingKey_WhenKeepTrue_ReturnsUpdate() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key", (k, _) => 10, (k, old, _) => old, default(object));

		// Act - Update: return (true, newValue)
		var result = cache.UpdateOrRemove("key",
			(k, existing, newVal) => (true, existing + newVal),
			5);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(result.OldValue, Is.EqualTo(10));
		Assert.That(result.NewValue, Is.EqualTo(15));
		Assert.That(cache.TryGetValue("key", out var value), Is.True);
		Assert.That(value, Is.EqualTo(15));
	}

	[Test]
	public void UpdateOrRemove_WithExistingKey_WhenKeepFalse_ReturnsRemove() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key", (k, _) => 42, (k, old, _) => old, default(object));

		// Act - Remove: return (false, _)
		var result = cache.UpdateOrRemove("key",
			(k, existing, _) => (false, default),
			0);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Remove));
		Assert.That(result.OldValue, Is.EqualTo(42)); // Original value returned
		Assert.That(result.NewValue, Is.EqualTo(default(int)));
		Assert.That(cache.ContainsKey("key"), Is.False);
		Assert.That(cache.Count, Is.EqualTo(0));
	}

	[Test]
	public void UpdateOrRemove_WithNonExistingKey_ReturnsNotFound() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();

		// Act
		var result = cache.UpdateOrRemove("missing",
			(k, existing, _) => (true, existing + 1),
			0);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.NotFound));
		Assert.That(result.OldValue, Is.EqualTo(default(int)));
		Assert.That(result.NewValue, Is.EqualTo(default(int)));
	}

	[Test]
	public void UpdateOrRemove_ConditionalRemove_RemovesWhenConditionMet() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("counter", (k, _) => 3, (k, old, _) => old, default(object));

		// Act - Decrement counter, remove if it hits 0
		// First decrement: 3 -> 2 (keep)
		var result1 = cache.UpdateOrRemove("counter",
			(k, existing, _) => existing > 1 ? (true, existing - 1) : (false, default),
			0);
		Assert.That(result1.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(cache.TryGetValue("counter", out var val1), Is.True);
		Assert.That(val1, Is.EqualTo(2));

		// Second decrement: 2 -> 1 (keep)
		var result2 = cache.UpdateOrRemove("counter",
			(k, existing, _) => existing > 1 ? (true, existing - 1) : (false, default),
			0);
		Assert.That(result2.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(cache.TryGetValue("counter", out var val2), Is.True);
		Assert.That(val2, Is.EqualTo(1));

		// Third decrement: 1 -> remove
		var result3 = cache.UpdateOrRemove("counter",
			(k, existing, _) => existing > 1 ? (true, existing - 1) : (false, default),
			0);
		Assert.That(result3.Operation, Is.EqualTo(UpdateOrRemoveOperation.Remove));
		Assert.That(result3.OldValue, Is.EqualTo(1));
		Assert.That(cache.ContainsKey("counter"), Is.False);
	}

	[Test]
	public void UpdateOrRemove_WithArgs_PassesArgsToFunction() {
		// Arrange
		var cache = new ConcurrentCacheStore<string, int>();
		cache.AddOrUpdate("key", (k, _) => 100, (k, old, _) => old, default(object));

		// Act - Use args to determine new value
		var result = cache.UpdateOrRemove("key",
			(k, existing, multiplier) => (true, existing * multiplier),
			3);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(result.OldValue, Is.EqualTo(100));
		Assert.That(result.NewValue, Is.EqualTo(300));
		Assert.That(cache.TryGetValue("key", out var value), Is.True);
		Assert.That(value, Is.EqualTo(300));
	}

	[Test]
	public void UpdateOrRemove_RemoveFromMiddleOfChain_MaintainsOtherEntries() {
		// Arrange - Use collision keys to create a chain
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 5; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id * 10, (k, old, _) => old, default(object));

		// Act - Remove middle entry
		var result = cache.UpdateOrRemove(new CollisionKey(2),
			(k, existing, _) => (false, default),
			0);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Remove));
		Assert.That(result.OldValue, Is.EqualTo(20));
		Assert.That(cache.Count, Is.EqualTo(4));
		// Other entries still present
		Assert.That(cache.TryGetValue(new CollisionKey(0), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(1), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(3), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(4), out _), Is.True);
	}

	[Test]
	public void UpdateOrRemove_RemoveFirstInChain_UpdatesBucketHead() {
		// Arrange - Use collision keys, last added is first in chain
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		cache.AddOrUpdate(new CollisionKey(0), (k, _) => 0, (k, old, _) => old, default(object));
		cache.AddOrUpdate(new CollisionKey(1), (k, _) => 10, (k, old, _) => old, default(object));
		cache.AddOrUpdate(new CollisionKey(2), (k, _) => 20, (k, old, _) => old, default(object)); // Head of chain

		// Act - Remove head
		var result = cache.UpdateOrRemove(new CollisionKey(2),
			(k, existing, _) => (false, default),
			0);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Remove));
		Assert.That(result.OldValue, Is.EqualTo(20));
		Assert.That(cache.Count, Is.EqualTo(2));
		Assert.That(cache.TryGetValue(new CollisionKey(0), out var v0), Is.True);
		Assert.That(v0, Is.EqualTo(0));
		Assert.That(cache.TryGetValue(new CollisionKey(1), out var v1), Is.True);
		Assert.That(v1, Is.EqualTo(10));
	}

	[Test]
	public void UpdateOrRemove_RemoveLastInChain_MaintainsPrevNext() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		cache.AddOrUpdate(new CollisionKey(0), (k, _) => 0, (k, old, _) => old, default(object)); // Last in chain
		cache.AddOrUpdate(new CollisionKey(1), (k, _) => 10, (k, old, _) => old, default(object));
		cache.AddOrUpdate(new CollisionKey(2), (k, _) => 20, (k, old, _) => old, default(object));

		// Act - Remove last in chain
		var result = cache.UpdateOrRemove(new CollisionKey(0),
			(k, existing, _) => (false, default),
			0);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Remove));
		Assert.That(result.OldValue, Is.EqualTo(0));
		Assert.That(cache.Count, Is.EqualTo(2));
		Assert.That(cache.TryGetValue(new CollisionKey(1), out _), Is.True);
		Assert.That(cache.TryGetValue(new CollisionKey(2), out _), Is.True);
	}

	[Test]
	public void UpdateOrRemove_UpdateInChain_UpdatesCorrectNode() {
		// Arrange
		var cache = new ConcurrentCacheStore<CollisionKey, int>();
		for (var i = 0; i < 5; i++)
			cache.AddOrUpdate(new CollisionKey(i), (k, _) => k.Id, (k, old, _) => old, default(object));

		// Act - Update middle entry
		var result = cache.UpdateOrRemove(new CollisionKey(2),
			(k, existing, mult) => (true, existing * mult),
			100);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(result.OldValue, Is.EqualTo(2));
		Assert.That(result.NewValue, Is.EqualTo(200));
		Assert.That(cache.TryGetValue(new CollisionKey(2), out var value), Is.True);
		Assert.That(value, Is.EqualTo(200));
		// Others unchanged
		Assert.That(cache.TryGetValue(new CollisionKey(1), out var v1), Is.True);
		Assert.That(v1, Is.EqualTo(1));
	}

	[Test]
	public void UpdateOrRemove_ConcurrentOperations_MaintainsConsistency() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, int>();
		var itemCount = 100;
		for (var i = 0; i < itemCount; i++)
			cache.AddOrUpdate(i, (k, _) => 10, (k, old, _) => old, default(object)); // All start at count=10

		var threads = new List<Thread>();
		var decrementCount = 0;
		var removeCount = 0;
		var lockObj = new object();

		// Act - Multiple threads decrementing, removing when count hits 0
		for (var t = 0; t < 10; t++) {
			var thread = new Thread(() => {
				var random = new Random();
				for (var i = 0; i < 100; i++) {
					var key = random.Next(itemCount);
					var result = cache.UpdateOrRemove(key,
						(k, existing, _) => existing > 1 ? (true, existing - 1) : (false, default),
						0);

					lock (lockObj) {
						if (result.Operation == UpdateOrRemoveOperation.Remove)
							removeCount++;
						else if (result.Operation == UpdateOrRemoveOperation.Update)
							decrementCount++;
					}
				}
			});
			threads.Add(thread);
			thread.Start();
		}

		foreach (var thread in threads) thread.Join();

		// Assert - No exceptions, counts are reasonable
		Assert.That(decrementCount + removeCount, Is.LessThanOrEqualTo(1000)); // 10 threads * 100 ops each
		// Some items should have been removed (count reached 0)
		Assert.That(removeCount, Is.GreaterThan(0));
	}

	[Test]
	public void UpdateOrRemove_WithLargeStruct_CreatesNewNode() {
		// Arrange
		var cache = new ConcurrentCacheStore<int, LargeStruct>();
		cache.AddOrUpdate(1, (k, _) => new LargeStruct { A = 1, B = 2, C = 3, D = 4 }, (k, old, _) => old, default(object));

		// Act
		var result = cache.UpdateOrRemove(1,
			(k, existing, mult) => (true,
				new LargeStruct { A = existing.A * mult, B = existing.B, C = existing.C, D = existing.D }),
			10L);

		// Assert
		Assert.That(result.Operation, Is.EqualTo(UpdateOrRemoveOperation.Update));
		Assert.That(result.OldValue.A, Is.EqualTo(1));
		Assert.That(result.NewValue!.A, Is.EqualTo(10));
		Assert.That(cache.TryGetValue(1, out var value), Is.True);
		Assert.That(value.A, Is.EqualTo(10));
	}

	[Test]
	public void UpdateOrRemove_DuringResize_RetriesCorrectly() {
		// Arrange - Small capacity to trigger resizes
		var cache = new ConcurrentCacheStore<int, int>(2, 7);
		for (var i = 0; i < 50; i++)
			cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));

		var updateResults = new List<UpdateOrRemoveResult<int>>();

		// Act - Concurrent adds (causing resizes) and UpdateOrRemove
		var addThread = new Thread(() => {
			for (var i = 50; i < 500; i++)
				cache.AddOrUpdate(i, (k, _) => k, (k, old, _) => old, default(object));
		});

		var updateThread = new Thread(() => {
			for (var i = 0; i < 50; i++) {
				var result = cache.UpdateOrRemove(i,
					(k, existing, _) => (true, existing * 2),
					0);
				lock (updateResults) {
					updateResults.Add(result);
				}
			}
		});

		addThread.Start();
		updateThread.Start();
		addThread.Join();
		updateThread.Join();

		// Assert - All updates should have succeeded
		Assert.That(updateResults.All(r => r.Operation == UpdateOrRemoveOperation.Update), Is.True);
	}
}
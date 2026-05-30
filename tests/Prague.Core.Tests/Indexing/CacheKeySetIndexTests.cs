namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

/// <summary>
///   Tests for CacheKeySetIndex - an index that tracks keys where a predicate returns true.
///   This is used by [DataCacheHasValueIndex] to track keys where a nullable property has a value.
/// </summary>
[TestFixture]
public class CacheKeySetIndexTests {
	// Simple test entity with nullable properties - must implement ICacheEquatable and ICacheClonable
	private class TestEntity : ICacheEquatable<TestEntity>, ICacheClonable<TestEntity> {
		public int Id { get; set; }
		public string? Name { get; set; }
		public int? OptionalValue { get; set; }
		public DateTime? OptionalDate { get; set; }

		public bool CacheEquals(TestEntity? other) {
			if (other is null) return false;
			return Id == other.Id &&
			       Name == other.Name &&
			       OptionalValue == other.OptionalValue &&
			       OptionalDate == other.OptionalDate;
		}

		public int CacheGetHashCode() {
			return HashCode.Combine(Id, Name, OptionalValue, OptionalDate);
		}

		public TestEntity Clone() {
			return new TestEntity {
				Id = Id,
				Name = Name,
				OptionalValue = OptionalValue,
				OptionalDate = OptionalDate
			};
		}
	}

	#region Basic Operations

	[Test]
	public void Add_WhenPredicateTrue_ShouldAddKeyToIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test" });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.True);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(1));
	}

	[Test]
	public void Add_WhenPredicateFalse_ShouldNotAddKeyToIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.False);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(0));
	}

	[Test]
	public void Add_MixedPredicateResults_ShouldOnlyIndexTrueKeys() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3" });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = null });
		cache.AddOrUpdate(5, new TestEntity { Id = 5, Name = "Test5" });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.True);
		Assert.That(hasNameIndex.Contains(2), Is.False);
		Assert.That(hasNameIndex.Contains(3), Is.True);
		Assert.That(hasNameIndex.Contains(4), Is.False);
		Assert.That(hasNameIndex.Contains(5), Is.True);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(3));
	}

	[Test]
	public void GetKeys_ShouldReturnAllIndexedKeys() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3" });

		// Act
		var keys = hasNameIndex.GetKeys();

		// Assert
		Assert.That(keys, Has.Count.EqualTo(2));
		Assert.That(keys, Does.Contain(1));
		Assert.That(keys, Does.Contain(3));
		Assert.That(keys, Does.Not.Contain(2));
	}

	[Test]
	public void GetKeys_EmptyIndex_ShouldReturnEmptyCollection() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Act
		var keys = hasNameIndex.GetKeys();

		// Assert
		Assert.That(keys, Is.Empty);
	}

	#endregion

	#region Remove Operations

	[Test]
	public void Remove_ExistingKeyInIndex_ShouldRemoveFromIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test" });
		Assert.That(hasNameIndex.Contains(1), Is.True);

		// Act
		cache.Remove(1);

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.False);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(0));
	}

	[Test]
	public void Remove_KeyNotInIndex_ShouldNotAffectIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });

		// Act - Remove key that wasn't in index (Name was null)
		cache.Remove(2);

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.True);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(1));
	}

	[Test]
	public void Remove_MultipleKeys_ShouldUpdateIndexCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = "Test2" });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3" });

		// Act
		cache.Remove(1);
		cache.Remove(3);

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.False);
		Assert.That(hasNameIndex.Contains(2), Is.True);
		Assert.That(hasNameIndex.Contains(3), Is.False);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(1));
	}

	#endregion

	#region Update Operations

	[Test]
	public void Update_FromNullToValue_ShouldAddToIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null });
		Assert.That(hasNameIndex.Contains(1), Is.False);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Now has value" });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.True);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(1));
	}

	[Test]
	public void Update_FromValueToNull_ShouldRemoveFromIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Has value" });
		Assert.That(hasNameIndex.Contains(1), Is.True);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.False);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(0));
	}

	[Test]
	public void Update_ValueToValue_ShouldRemainInIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Original" });
		Assert.That(hasNameIndex.Contains(1), Is.True);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Updated" });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.True);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(1));
	}

	[Test]
	public void Update_NullToNull_ShouldRemainOutOfIndex() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null, OptionalValue = 1 });
		Assert.That(hasNameIndex.Contains(1), Is.False);

		// Act - Update but keep Name null
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null, OptionalValue = 2 });

		// Assert
		Assert.That(hasNameIndex.Contains(1), Is.False);
		Assert.That(hasNameIndex.ApproximateCount, Is.EqualTo(0));
	}

	#endregion

	#region Query Integration

	[Test]
	public void UseIndex_ShouldFilterToOnlyIndexedKeys() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3" });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = null });

		// Act
		var results = cache.Query()
			.UseIndex(hasNameIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(r => r.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void UseIndex_CombinedWithOtherIndexes_ShouldIntersect() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);
		var hasValueIndex = cache.AddKeySetIndex((key, entity) => entity.OptionalValue != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1", OptionalValue = 10 });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null, OptionalValue = 20 });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3", OptionalValue = null });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = "Test4", OptionalValue = 40 });

		// Act - Find entities with both Name AND OptionalValue
		var results = cache.Query()
			.UseIndex(hasNameIndex)
			.UseIndex(hasValueIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(r => r.Id), Is.EquivalentTo(new[] { 1, 4 }));
	}

	[Test]
	public void UseIndex_CombinedWithWhereFilter_ShouldApplyBoth() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Alpha" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Beta" });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = "Gamma" });

		// Act
		var results = cache.Query()
			.UseIndex(hasNameIndex)
			.Where(e => e.Name!.StartsWith("A") || e.Name!.StartsWith("G"))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(r => r.Name), Is.EquivalentTo(new[] { "Alpha", "Gamma" }));
	}

	[Test]
	public void UseIndex_EmptyIndex_ShouldReturnEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = null });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null });

		// Act
		var results = cache.Query()
			.UseIndex(hasNameIndex)
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	#endregion

	#region Multiple Indexes

	[Test]
	public void MultipleKeySetIndexes_ShouldAllBeMaintained() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);
		var hasValueIndex = cache.AddKeySetIndex((key, entity) => entity.OptionalValue != null);
		var hasDateIndex = cache.AddKeySetIndex((key, entity) => entity.OptionalDate != null);

		// Act
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test", OptionalValue = 10, OptionalDate = DateTime.Now });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null, OptionalValue = 20, OptionalDate = null });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test", OptionalValue = null, OptionalDate = DateTime.Now });

		// Assert
		Assert.That(hasNameIndex.GetKeys(), Is.EquivalentTo(new[] { 1, 3 }));
		Assert.That(hasValueIndex.GetKeys(), Is.EquivalentTo(new[] { 1, 2 }));
		Assert.That(hasDateIndex.GetKeys(), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void KeySetIndex_WithKeyValueListIndex_ShouldCoexist() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);
		var byValueIndex = cache.CacheKeyValueListIndex((key, entity) => entity.OptionalValue ?? 0);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1", OptionalValue = 100 });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null, OptionalValue = 100 });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3", OptionalValue = 200 });

		// Act
		var withName = hasNameIndex.GetKeys().ToList();
		var withValue100 = byValueIndex.GetValues(100).ToList();

		// Assert
		Assert.That(withName, Is.EquivalentTo(new[] { 1, 3 }));
		Assert.That(withValue100, Is.EquivalentTo(new[] { 1, 2 }));
	}

	#endregion

	#region Edge Cases

	[Test]
	public void AddKeySetIndex_AfterDataExists_ShouldStartEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = "Test2" });

		// Act - Add index AFTER data
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Assert - Index should be empty (doesn't retroactively index)
		Assert.That(hasNameIndex.GetKeys(), Is.Empty);
	}

	[Test]
	public void Contains_NonExistentKey_ShouldReturnFalse() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test" });

		// Act & Assert
		Assert.That(hasNameIndex.Contains(999), Is.False);
	}

	[Test]
	public void CustomPredicate_CanUseKeyInLogic() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		// Index only entities where key is even AND has a name
		var evenWithNameIndex = cache.AddKeySetIndex((key, entity) => key % 2 == 0 && entity.Name != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test1" }); // odd key
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = "Test2" }); // even key with name
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test3" }); // odd key
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = null }); // even key without name

		// Assert
		Assert.That(evenWithNameIndex.GetKeys(), Is.EquivalentTo(new[] { 2 }));
	}

	[Test]
	public void CustomPredicate_ComplexCondition() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		// Index entities that have either Name OR OptionalValue (but not both null)
		var hasAnyValueIndex = cache.AddKeySetIndex((key, entity) =>
			entity.Name != null || entity.OptionalValue != null);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, Name = "Test", OptionalValue = null });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, Name = null, OptionalValue = 10 });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, Name = "Test", OptionalValue = 20 });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, Name = null, OptionalValue = null });

		// Assert
		Assert.That(hasAnyValueIndex.GetKeys(), Is.EquivalentTo(new[] { 1, 2, 3 }));
		Assert.That(hasAnyValueIndex.Contains(4), Is.False);
	}

	#endregion

	#region Concurrency

	[Test]
	public void ConcurrentAdds_ShouldBeThreadSafe() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Act - Add items concurrently
		var tasks = new List<Task>();
		for (var i = 0; i < 10; i++) {
			var batch = i;
			tasks.Add(Task.Run(() => {
				for (var j = 0; j < 100; j++) {
					var key = batch * 100 + j;
					var hasName = j % 2 == 0; // Even j gets name
					cache.AddOrUpdate(key, new TestEntity {
						Id = key,
						Name = hasName ? $"Test{key}" : null
					});
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		// Assert
		var keys = hasNameIndex.GetKeys();
		Assert.That(keys, Has.Count.EqualTo(500)); // Half of 1000 should have names
	}

	[Test]
	public void ConcurrentReads_ShouldBeThreadSafe() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		for (var i = 0; i < 100; i++)
			cache.AddOrUpdate(i, new TestEntity { Id = i, Name = i % 2 == 0 ? $"Test{i}" : null });

		// Act - Read concurrently
		var exception = false;
		var tasks = new List<Task>();
		for (var i = 0; i < 20; i++)
			tasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 100; j++) {
						_ = hasNameIndex.Contains(j);
						_ = hasNameIndex.GetKeys();
						_ = hasNameIndex.ApproximateCount;
					}
				}
				catch {
					exception = true;
				}
			}));

		Task.WaitAll(tasks.ToArray());

		// Assert
		Assert.That(exception, Is.False);
	}

	[Test]
	public void ConcurrentReadAndWrite_ShouldBeThreadSafe() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestEntity>();
		var hasNameIndex = cache.AddKeySetIndex((key, entity) => entity.Name != null);

		// Pre-populate
		for (var i = 0; i < 50; i++)
			cache.AddOrUpdate(i, new TestEntity { Id = i, Name = $"Test{i}" });

		// Act
		var exception = false;
		var readTasks = new List<Task>();
		var writeTasks = new List<Task>();

		for (var i = 0; i < 10; i++)
			readTasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 50; j++) {
						_ = hasNameIndex.Contains(j);
						_ = hasNameIndex.GetKeys();
					}
				}
				catch {
					exception = true;
				}
			}));

		for (var i = 0; i < 5; i++) {
			var batch = i;
			writeTasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 20; j++) {
						var key = 50 + batch * 20 + j;
						cache.AddOrUpdate(key, new TestEntity { Id = key, Name = $"New{key}" });
					}
				}
				catch {
					exception = true;
				}
			}));
		}

		Task.WaitAll(readTasks.Concat(writeTasks).ToArray());

		// Assert
		Assert.That(exception, Is.False);
	}

	#endregion
}

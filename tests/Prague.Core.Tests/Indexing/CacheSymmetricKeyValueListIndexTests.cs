namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class CacheSymmetricKeyValueListIndexTests {
	private class TestEntity : ICacheEquatable<TestEntity>, ICacheClonable<TestEntity> {
		public int Id { get; set; }
		public string Category { get; set; } = "";

		public bool CacheEquals(TestEntity? other) {
			if (other is null) return false;
			return Id == other.Id && Category == other.Category;
		}

		public int CacheGetHashCode() => HashCode.Combine(Id, Category);

		public TestEntity Clone() => new() { Id = Id, Category = Category };
	}

	private InMemoryDataCache<int, TestEntity> _cache;
	private CacheSymmetricKeyValueListIndex<int, TestEntity, string> _index;

	private string GetReverse(int key) {
		Assert.That(_index.Reverse.TryGetValue(key, out var value), Is.True, $"Reverse index does not contain key {key}");
		return value!;
	}

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, TestEntity>();
		_index = _cache.CacheSymmetricKeyValueListIndex<string>((_, e) => e.Category);
	}

	[Test]
	public void Reverse_AfterAdd_ShouldMapKeyToIndexKey() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Category = "A" });
		_cache.AddOrUpdate(3, new TestEntity { Id = 3, Category = "B" });

		// Forward: "A" -> {1, 2}, "B" -> {3}
		Assert.That(_index.GetValues("A"), Has.Count.EqualTo(2));
		Assert.That(_index.GetValues("B"), Has.Count.EqualTo(1));

		// Reverse: key -> index key
		Assert.That(GetReverse(1), Is.EqualTo("A"));
		Assert.That(GetReverse(2), Is.EqualTo("A"));
		Assert.That(GetReverse(3), Is.EqualTo("B"));
	}

	[Test]
	public void Reverse_AfterRemove_ShouldRemoveMapping() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Category = "A" });

		Assert.That(GetReverse(1), Is.EqualTo("A"));
		Assert.That(GetReverse(2), Is.EqualTo("A"));

		_cache.Remove(1);

		Assert.That(_index.Reverse.ContainsKey(1), Is.False);
		Assert.That(GetReverse(2), Is.EqualTo("A"));
	}

	[Test]
	public void Reverse_AfterUpdate_ShouldReflectNewIndexKey() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });

		Assert.That(GetReverse(1), Is.EqualTo("A"));

		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "B" });

		Assert.That(GetReverse(1), Is.EqualTo("B"));
	}

	[Test]
	public void Reverse_AfterUpdateSameCategory_ShouldKeepMapping() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });

		Assert.That(GetReverse(1), Is.EqualTo("A"));
	}

	[Test]
	public void Reverse_MultipleKeysInSameCategory_AllMappedCorrectly() {
		for (var i = 1; i <= 5; i++)
			_cache.AddOrUpdate(i, new TestEntity { Id = i, Category = "X" });

		for (var i = 1; i <= 5; i++)
			Assert.That(GetReverse(i), Is.EqualTo("X"));
	}

	[Test]
	public void Reverse_MoveKeyBetweenCategories_ForwardAndReverseConsistent() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Category = "A" });

		// Move key 1 from A to B
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "B" });

		// Forward
		Assert.That(_index.GetValues("A"), Does.Contain(2));
		Assert.That(_index.GetValues("A"), Does.Not.Contain(1));
		Assert.That(_index.GetValues("B"), Does.Contain(1));

		// Reverse
		Assert.That(GetReverse(1), Is.EqualTo("B"));
		Assert.That(GetReverse(2), Is.EqualTo("A"));
	}

	[Test]
	public void Reverse_RemoveAll_ShouldBeEmpty() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Category = "B" });

		_cache.Remove(1);
		_cache.Remove(2);

		Assert.That(_index.Reverse.ContainsKey(1), Is.False);
		Assert.That(_index.Reverse.ContainsKey(2), Is.False);
		Assert.That(_index.Reverse.ApproximateCount, Is.EqualTo(0));
	}

	[Test]
	public void Reverse_AddRemoveAdd_ShouldTrackCorrectly() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "A" });
		Assert.That(GetReverse(1), Is.EqualTo("A"));

		_cache.Remove(1);
		Assert.That(_index.Reverse.ContainsKey(1), Is.False);

		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Category = "B" });
		Assert.That(GetReverse(1), Is.EqualTo("B"));
	}
}

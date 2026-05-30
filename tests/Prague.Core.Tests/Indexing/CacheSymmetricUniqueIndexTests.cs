namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class CacheSymmetricUniqueIndexTests {
	private class TestEntity : ICacheEquatable<TestEntity>, ICacheClonable<TestEntity> {
		public int Id { get; set; }
		public string Email { get; set; } = "";

		public bool CacheEquals(TestEntity? other) {
			if (other is null) return false;
			return Id == other.Id && Email == other.Email;
		}

		public int CacheGetHashCode() => HashCode.Combine(Id, Email);

		public TestEntity Clone() => new() { Id = Id, Email = Email };
	}

	private InMemoryDataCache<int, TestEntity> _cache;
	private CacheSymmetricUniqueIndex<int, TestEntity, string> _index;

	private string GetReverse(int key) {
		Assert.That(_index.Reverse.TryGetValue(key, out var value), Is.True, $"Reverse index does not contain key {key}");
		return value;
	}

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, TestEntity>();
		_index = _cache.AddSymmetricKeyValueIndex<string>((_, e) => e.Email);
	}


	[Test]
	public void Reverse_AfterAdd_ShouldMapKeyToIndexKey() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Email = "b@test.com" });

		Assert.That(GetReverse(1), Is.EqualTo("a@test.com"));
		Assert.That(GetReverse(2), Is.EqualTo("b@test.com"));
	}

	[Test]
	public void Reverse_AfterRemove_ShouldRemoveMapping() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Email = "b@test.com" });

		_cache.Remove(1);

		Assert.That(_index.Reverse.ContainsKey(1), Is.False);
		Assert.That(GetReverse(2), Is.EqualTo("b@test.com"));
	}

	[Test]
	public void Reverse_AfterUpdate_ShouldReflectNewIndexKey() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "old@test.com" });
		Assert.That(GetReverse(1), Is.EqualTo("old@test.com"));

		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "new@test.com" });
		Assert.That(GetReverse(1), Is.EqualTo("new@test.com"));
	}

	[Test]
	public void Reverse_AfterUpdateSameEmail_ShouldKeepMapping() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });

		Assert.That(GetReverse(1), Is.EqualTo("a@test.com"));
	}

	[Test]
	public void Reverse_ForwardAndReverseConsistent() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Email = "b@test.com" });

		// Forward: email -> key
		Assert.That(_index.TryGetValue("a@test.com", out var key1), Is.True);
		Assert.That(key1, Is.EqualTo(1));

		// Reverse: key -> email
		Assert.That(GetReverse(1), Is.EqualTo("a@test.com"));
	}

	[Test]
	public void Reverse_RemoveAll_ShouldBeEmpty() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "a@test.com" });
		_cache.AddOrUpdate(2, new TestEntity { Id = 2, Email = "b@test.com" });

		_cache.Remove(1);
		_cache.Remove(2);

		Assert.That(_index.Reverse.ContainsKey(1), Is.False);
		Assert.That(_index.Reverse.ContainsKey(2), Is.False);
		Assert.That(_index.Reverse.ApproximateCount, Is.EqualTo(0));
	}

	[Test]
	public void Reverse_AddRemoveAdd_ShouldTrackCorrectly() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "first@test.com" });
		Assert.That(GetReverse(1), Is.EqualTo("first@test.com"));

		_cache.Remove(1);
		Assert.That(_index.Reverse.ContainsKey(1), Is.False);

		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "second@test.com" });
		Assert.That(GetReverse(1), Is.EqualTo("second@test.com"));
	}

	[Test]
	public void Reverse_OldIndexKeyRemovedAfterUpdate() {
		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "old@test.com" });

		// Forward should have old key
		Assert.That(_index.ContainsKey("old@test.com"), Is.True);

		_cache.AddOrUpdate(1, new TestEntity { Id = 1, Email = "new@test.com" });

		// Forward: old gone, new present
		Assert.That(_index.ContainsKey("old@test.com"), Is.False);
		Assert.That(_index.ContainsKey("new@test.com"), Is.True);

		// Reverse: only new mapping
		Assert.That(GetReverse(1), Is.EqualTo("new@test.com"));
		Assert.That(_index.Reverse.ApproximateCount, Is.EqualTo(1));
	}
}

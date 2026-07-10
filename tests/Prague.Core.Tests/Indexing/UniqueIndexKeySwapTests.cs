namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

/// <summary>
///   The unique key-value index must stay consistent with cache content when an index-key
///   value moves between entities. A blind forward-slot remove drops a live mapping when
///   another entity has meanwhile taken over the old index key (key-swap / clobber).
/// </summary>
[TestFixture]
public class UniqueIndexKeySwapTests {
	private sealed class Entity : ICacheEquatable<Entity>, ICacheClonable<Entity> {
		public int Id { get; init; }
		public string Value { get; init; } = "";
		public bool CacheEquals(Entity? other) => other is not null && other.Id == Id && other.Value == Value;
		public int CacheGetHashCode() => HashCode.Combine(Id, Value);
		public Entity Clone() => new() { Id = Id, Value = Value };
	}

	[Test]
	public void KeySwapBetweenEntities_KeepsBothMappings() {
		var cache = new InMemoryDataCache<int, Entity>();
		var idx = cache.AddKeyValueIndex((_, v) => v.Value);

		cache.AddOrUpdate(1, new Entity { Id = 1, Value = "X" });
		cache.AddOrUpdate(2, new Entity { Id = 2, Value = "Y" });

		// Swap: entity 1 takes "Y" (transiently clobbering 2's mapping), then entity 2
		// takes "X". Removing 2's OLD key "Y" must not drop entity 1's live mapping.
		cache.AddOrUpdate(1, new Entity { Id = 1, Value = "Y" });
		cache.AddOrUpdate(2, new Entity { Id = 2, Value = "X" });

		Assert.That(idx.TryGetValue("Y", out var k1), Is.True, "live entity 1 lost from unique index");
		Assert.That(k1, Is.EqualTo(1));
		Assert.That(idx.TryGetValue("X", out var k2), Is.True, "live entity 2 lost from unique index");
		Assert.That(k2, Is.EqualTo(2));
	}

	[Test]
	public void RemoveAfterClobber_KeepsNewOwnerMapping() {
		var cache = new InMemoryDataCache<int, Entity>();
		var idx = cache.AddKeyValueIndex((_, v) => v.Value);

		cache.AddOrUpdate(1, new Entity { Id = 1, Value = "X" });
		cache.AddOrUpdate(2, new Entity { Id = 2, Value = "X" }); // clobbers the forward slot: X -> 2

		// Removing entity 1 (old owner of "X") must not drop entity 2's live mapping.
		cache.Remove(1);

		Assert.That(idx.TryGetValue("X", out var owner), Is.True, "live entity 2 lost from unique index");
		Assert.That(owner, Is.EqualTo(2));
	}
}

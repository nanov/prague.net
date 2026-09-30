namespace Prague.Core.Tests.Cache;

using Prague.Core;

/// <summary>
///   <c>AddOrUpdate(…, force: true, …)</c> is the unconditional write: it replaces the resident value even
///   when <c>CacheEquals</c> says the two are equal, walks every index, and never reports <c>Same</c>. With
///   <c>force: false</c> the overload keeps the conditional contract; the last two tests pin that baseline.
/// </summary>
[TestFixture]
public class AddOrUpdateForceTests {
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
	public void Forced_EqualValue_ReplacesTheResidentReference() {
		var cache = new InMemoryDataCache<int, Entity>();
		var first = Same();
		var second = Same();
		cache.AddOrUpdate(1, first, T(1));

		var changed = cache.AddOrUpdate(1, second, T(2), force: true, out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.True);
			Assert.That(old, Is.SameAs(first));
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(second));
		});
	}

	[Test]
	public void Forced_EqualValue_RefreshesLastUpdated() {
		var cache = new InMemoryDataCache<int, Entity>();
		var lastUpdated = new LastUpdatedIndex<int>();
		cache.CacheLastUpdatedIndex(lastUpdated, static (_, e) => e.Group);
		cache.AddOrUpdate(1, Same(), T(1));

		cache.AddOrUpdate(1, Same(), T(2), force: true, out _);

		Assert.Multiple(() => {
			Assert.That(lastUpdated.TryGetLastUpdated(7, out var ts), Is.True);
			Assert.That(ts, Is.EqualTo(T(2)));
			Assert.That(lastUpdated.GetEntitiesCount(7), Is.EqualTo(1), "a forced update must not double-count the entity");
		});
	}

	[Test]
	public void Forced_EqualValue_LeavesKeySetAndUniqueIndexesResolving() {
		var cache = new InMemoryDataCache<int, Entity>();
		var named = cache.AddKeySetIndex(static (_, e) => e.Name.Length > 0);
		var byGroup = cache.AddKeyValueIndex(static (_, e) => e.Group);
		cache.AddOrUpdate(1, Same(), T(1));

		cache.AddOrUpdate(1, Same(), T(2), force: true, out _);

		Assert.Multiple(() => {
			Assert.That(named.Contains(1), Is.True);
			Assert.That(named.ApproximateCount, Is.EqualTo(1));
			Assert.That(byGroup.TryGetValue(7, out var key), Is.True);
			Assert.That(key, Is.EqualTo(1));
		});
	}

	[Test]
	public void Forced_AbsentKey_IsAdded() {
		var cache = new InMemoryDataCache<int, Entity>();
		var byGroup = cache.AddKeyValueIndex(static (_, e) => e.Group);
		var value = Same();

		var changed = cache.AddOrUpdate(1, value, T(1), force: true, out var old);

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
	public void Unforced_EqualValue_StaysConditional() {
		var cache = new InMemoryDataCache<int, Entity>();
		var lastUpdated = new LastUpdatedIndex<int>();
		cache.CacheLastUpdatedIndex(lastUpdated, static (_, e) => e.Group);
		var first = Same();
		cache.AddOrUpdate(1, first, T(1));

		var changed = cache.AddOrUpdate(1, Same(), T(2), force: false, out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.False);
			Assert.That(old, Is.Null);
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(first));
			Assert.That(lastUpdated.TryGetLastUpdated(7, out var ts), Is.True);
			Assert.That(ts, Is.EqualTo(T(1)));
		});
	}

	[Test]
	public void Unforced_DifferentValue_StillUpdates() {
		var cache = new InMemoryDataCache<int, Entity>();
		var first = Same();
		var second = new Entity { Id = 1, Name = "b", Group = 7 };
		cache.AddOrUpdate(1, first, T(1));

		var changed = cache.AddOrUpdate(1, second, T(2), force: false, out var old);

		Assert.Multiple(() => {
			Assert.That(changed, Is.True);
			Assert.That(old, Is.SameAs(first));
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(second));
		});
	}
}

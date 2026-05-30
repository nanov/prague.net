namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class LastUpdatedIndexTests {
	// Simple test entity with a group key and timestamp
	private readonly struct TestEntity : ICacheEquatable<TestEntity>, ICacheClonable<TestEntity> {
		public int Id { get; init; }
		public int GroupId { get; init; }
		public long UpdatedAt { get; init; }

		public bool CacheEquals(TestEntity other) {
			return Id == other.Id && GroupId == other.GroupId && UpdatedAt == other.UpdatedAt;
		}

		public int CacheGetHashCode() {
			return HashCode.Combine(Id, GroupId, UpdatedAt);
		}

		public TestEntity Clone() {
			return this;
		}
	}

	private static long T(int minutes) {
		return minutes * 60 * 1000L;
	}

	private static long TMs(int minutes) {
		return minutes * 60 * 1000L;
	}

	[Test]
	public void DirectIndex_Add_TracksLatest() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, TMs(10));
		Assert.That(index.TryGetLastUpdated(100, out var ts1), Is.True);
		Assert.That(ts1, Is.EqualTo(TMs(10)), "First add");

		index.Add(100, TMs(20));
		Assert.That(index.TryGetLastUpdated(100, out var ts2), Is.True);
		Assert.That(ts2, Is.EqualTo(TMs(20)), "Should update to newer timestamp");

		index.Add(100, T(15));
		Assert.That(index.TryGetLastUpdated(100, out var ts3), Is.True);
		Assert.That(ts3, Is.EqualTo(TMs(20)), "Should NOT update to older timestamp");
	}

	[Test]
	public void DirectIndex_Remove_DecrementsCounterAndUpdatesTimestamp() {
		var index = new LastUpdatedIndex<int>();

		// Add twice to get count=2
		index.Add(100, T(10));
		index.Add(100, T(20));

		// Remove with new timestamp - should decrement count and update timestamp
		index.Remove(100, T(30));

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True, "Entry should remain (count was 2)");
		Assert.That(ts, Is.EqualTo(TMs(30)), "Timestamp should be updated to removal timestamp");
	}

	[Test]
	public void DirectIndex_Remove_RemovesWhenCountHitsZero() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));

		// Remove - count hits 0, entry removed
		index.Remove(100, T(20));

		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed when count hits 0");
	}

	[Test]
	public void DirectIndex_Remove_DefaultsToUtcNow() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(100, T(20));

		var nowAt = DateTimeOffset.UtcNow;
		index.Remove(100, nowAt.ToUnixTimeMilliseconds());

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(nowAt.ToUnixTimeMilliseconds()));
	}

	[Test]
	public void DirectIndex_Add_IncrementsCounter() {
		var index = new LastUpdatedIndex<int>();

		// First add - count=1
		index.Add(100, T(10));

		// Second add - count=2
		index.Add(100, T(20));

		// Third add - count=3
		index.Add(100, T(30));

		// Remove once - count=2, entry should remain
		index.Remove(100, T(40));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True, "Entry should remain after first remove (count=2)");

		// Remove again - count=1, entry should remain
		index.Remove(100, T(50));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True, "Entry should remain after second remove (count=1)");

		// Remove again - count=0, entry should be removed
		index.Remove(100, T(60));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed when count hits 0");
	}

	[Test]
	public void DirectIndex_Add_FirstAdd_SetsTimestamp() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(50));

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(50)));
	}

	[Test]
	public void DirectIndex_Add_NewerTimestamp_UpdatesValue() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(100, T(30)); // Newer timestamp

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(30)), "Should update to newer timestamp");
	}

	[Test]
	public void DirectIndex_Add_OlderTimestamp_KeepsExistingValue() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(30));
		index.Add(100, T(10)); // Older timestamp

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(30)), "Should NOT update to older timestamp");
	}

	[Test]
	public void DirectIndex_Add_OlderTimestamp_StillIncrementsCounter() {
		var index = new LastUpdatedIndex<int>();

		// Add with newer timestamp first
		index.Add(100, T(30));
		// Add with older timestamp - value stays, but counter should still increment
		index.Add(100, T(10));

		// Need 2 removes to delete (counter was incremented both times)
		index.Remove(100, T(40));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True, "Entry should remain after first remove");

		index.Remove(100, T(50));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed after second remove");
	}

	[Test]
	public void DirectIndex_Add_SameTimestamp_IncrementsCounter() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(20));
		index.Add(100, T(20)); // Same timestamp
		index.Add(100, T(20)); // Same timestamp again

		// Timestamp should remain the same
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(20)));

		// Need 3 removes to delete
		index.Remove(100, T(30));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True);
		index.Remove(100, T(40));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True);
		index.Remove(100, T(50));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False);
	}

	[Test]
	public void DirectIndex_Add_MultipleKeys_TrackedIndependently() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(200, T(20));
		index.Add(300, T(30));

		Assert.That(index.TryGetLastUpdated(100, out var ts100), Is.True);
		Assert.That(ts100, Is.EqualTo(TMs(10)));

		Assert.That(index.TryGetLastUpdated(200, out var ts200), Is.True);
		Assert.That(ts200, Is.EqualTo(TMs(20)));

		Assert.That(index.TryGetLastUpdated(300, out var ts300), Is.True);
		Assert.That(ts300, Is.EqualTo(TMs(30)));
	}

	[Test]
	public void DirectIndex_Add_UpdatesRangeIndex_OnAdd() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(200, T(20));
		index.Add(300, T(30));

		// Verify range queries work after adds
		var since15 = index.GetValuesGte(T(15)).ToList();
		Assert.That(since15, Has.Count.EqualTo(2));
		Assert.That(since15, Does.Contain(200));
		Assert.That(since15, Does.Contain(300));
	}

	[Test]
	public void DirectIndex_Add_UpdatesRangeIndex_OnTimestampUpdate() {
		var index = new LastUpdatedIndex<int>();

		// Initial add
		index.Add(100, T(10));

		// Verify it's in range < 15
		var before = index.GetValuesLte(T(15)).ToList();
		Assert.That(before, Does.Contain(100));

		// Add again with newer timestamp that moves it out of that range
		index.Add(100, T(50));

		// Now it should NOT be in range < 15
		var afterUpdate = index.GetValuesLte(T(15)).ToList();
		Assert.That(afterUpdate, Does.Not.Contain(100));

		// But should be in range >= 40
		var since40 = index.GetValuesGte(T(40)).ToList();
		Assert.That(since40, Does.Contain(100));
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_ReturnsZero_WhenKeyNotFound() {
		var index = new LastUpdatedIndex<int>();

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_ReturnsOne_AfterSingleAdd() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_IncrementsWithEachAdd() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));

		index.Add(100, T(20));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(2));

		index.Add(100, T(30));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(3));
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_DecrementsWithRemove() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(100, T(20));
		index.Add(100, T(30));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(3));

		index.Remove(100, T(40));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(2));

		index.Remove(100, T(50));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));

		index.Remove(100, T(60));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0), "Should be 0 after entry is removed");
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_UnchangedByUpdate() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));

		index.Update(100, T(20));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1), "Update should not change count");

		index.Update(100, T(30));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1), "Update should not change count");
	}

	[Test]
	public void DirectIndex_GetEntitiesCount_TracksMultipleKeysIndependently() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));
		index.Add(100, T(20));
		index.Add(200, T(10));
		index.Add(300, T(10));
		index.Add(300, T(20));
		index.Add(300, T(30));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(2));
		Assert.That(index.GetEntitiesCount(200), Is.EqualTo(1));
		Assert.That(index.GetEntitiesCount(300), Is.EqualTo(3));
	}

	[Test]
	public void DirectIndex_Update_DoesNotIncrementCounter() {
		var index = new LastUpdatedIndex<int>();

		// Add once - count=1
		index.Add(100, T(10));

		// Update multiple times - count should stay at 1
		index.Update(100, T(20));
		index.Update(100, T(30));
		index.Update(100, T(40));

		// Single remove should remove the entry since count=1
		index.Remove(100, T(50));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False,
			"Entry should be removed after single remove since Update doesn't increment counter");
	}

	[Test]
	public void DirectIndex_Update_UpdatesTimestamp() {
		var index = new LastUpdatedIndex<int>();

		index.Add(100, T(10));

		// Update with newer timestamp
		index.Update(100, T(30));
		Assert.That(index.TryGetLastUpdated(100, out var ts1), Is.True);
		Assert.That(ts1, Is.EqualTo(TMs(30)), "Update should update to newer timestamp");

		// Update with older timestamp - should NOT change
		index.Update(100, T(20));
		Assert.That(index.TryGetLastUpdated(100, out var ts2), Is.True);
		Assert.That(ts2, Is.EqualTo(TMs(30)), "Update should NOT update to older timestamp");
	}

	[Test]
	public void DirectIndex_MixedAddAndUpdate_CorrectCounter() {
		var index = new LastUpdatedIndex<int>();

		// Add twice - count=2
		index.Add(100, T(10));
		index.Add(100, T(20));

		// Update multiple times - count should stay at 2
		index.Update(100, T(30));
		index.Update(100, T(40));

		// First remove - count=1
		index.Remove(100, T(50));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.True, "Entry should remain after first remove");

		// Second remove - count=0, entry removed
		index.Remove(100, T(60));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed when count hits 0");
	}

	[Test]
	public void Add_SingleItem_TracksTimestamp() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId,
			(id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(10)));
	}

	[Test]
	public void Add_MultipleItemsSameGroup_TracksLatest() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) }, T(10));
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 100, UpdatedAt = T(30) }, T(30));
		cache.AddOrUpdate(3, new TestEntity { Id = 3, GroupId = 100, UpdatedAt = T(20) }, T(20));

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(30)), "Should track the latest timestamp");
	}

	[Test]
	public void Add_DifferentGroups_TrackedSeparately() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) }, T(10));
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 200, UpdatedAt = T(20) }, T(20));

		Assert.That(index.TryGetLastUpdated(100, out var ts100), Is.True);
		Assert.That(ts100, Is.EqualTo(TMs(10)));

		Assert.That(index.TryGetLastUpdated(200, out var ts200), Is.True);
		Assert.That(ts200, Is.EqualTo(TMs(20)));
	}

	[Test]
	public void Remove_SingleItem_RemovesFromIndex() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId,
			(id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.Remove(1);

		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed when count hits 0");
	}

	[Test]
	public void Remove_UpdatesTimestampFromEntity() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId,
			(id, e) => e.UpdatedAt);

		// Add two items to same group
		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 100, UpdatedAt = T(20) });

		var removeTime = new DateTimeOffset(1982, 9, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

		// Remove one - timestamp from removed entity is used
		cache.Remove(2, removeTime);

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True, "Entry should remain");
		Assert.That(ts, Is.EqualTo(removeTime), "Timestamp should be updated from removed entity");
	}

	[Test]
	public void Update_NewerTimestamp_UpdatesIndex() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(
			index,
			(id, e) => e.GroupId,
			(id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(20) });

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(20)));
	}

	[Test]
	public void MultipleCaches_SharedIndex_TracksLatestAcrossAll() {
		var index = new LastUpdatedIndex<int>();

		var gameCache = new InMemoryDataCache<int, TestEntity>();
		var marketCache = new InMemoryDataCache<int, TestEntity>();
		var infoCache = new InMemoryDataCache<int, TestEntity>();

		gameCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);
		marketCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);
		infoCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		gameCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		marketCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(30) }); // Latest
		infoCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(20) });

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(TMs(30)), "Should track latest across all caches");
	}

	[Test]
	public void MultipleCaches_CounterBasedRemove_RemovesOnlyWhenAllRemoved() {
		var index = new LastUpdatedIndex<int>();

		var gameCache = new InMemoryDataCache<int, TestEntity>();
		var marketCache = new InMemoryDataCache<int, TestEntity>();

		gameCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);
		marketCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		// Both caches add to the same group
		gameCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		marketCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(20) });

		var removeTime = new DateTimeOffset(1982, 9, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

		// First remove - counter decrements, entry remains with updated timestamp
		marketCache.Remove(1, removeTime);
		Assert.That(index.TryGetLastUpdated(100, out var ts1), Is.True, "Entry should remain after first remove");
		Assert.That(ts1, Is.EqualTo(removeTime), "Timestamp updated from removed entity");

		// Second remove - counter hits 0, entry removed
		gameCache.Remove(1);
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False, "Entry should be removed when count hits 0");
	}

	[Test]
	public void MultipleCaches_RemoveUpdatesTimestamp() {
		var index = new LastUpdatedIndex<int>();

		var gameCache = new InMemoryDataCache<int, TestEntity>();
		var marketCache = new InMemoryDataCache<int, TestEntity>();

		gameCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);
		marketCache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		// Add from both caches with older timestamps
		gameCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		marketCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(20) });

		// Remove with a newer timestamp - simulates "entity was removed at T(50)"
		marketCache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(50) });
		var removeTime = new DateTimeOffset(1982, 9, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
		marketCache.Remove(1, removeTime);

		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(removeTime), "Remove should update timestamp to reflect when removal happened");
	}

	[Test]
	public void GetKeysUpdatedSince_ReturnsCorrectGroups() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 200, UpdatedAt = T(20) });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, GroupId = 300, UpdatedAt = T(30) });

		var groups = index.GetValuesGte(T(15)).ToList();

		Assert.That(groups, Has.Count.EqualTo(2));
		Assert.That(groups, Does.Contain(200));
		Assert.That(groups, Does.Contain(300));
		Assert.That(groups, Does.Not.Contain(100));
	}

	[Test]
	public void GetKeysUpdatedBefore_ReturnsCorrectGroups() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 200, UpdatedAt = T(20) });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, GroupId = 300, UpdatedAt = T(30) });

		var groups = index.GetValuesLte(T(25)).ToList();

		Assert.That(groups, Has.Count.EqualTo(2));
		Assert.That(groups, Does.Contain(100));
		Assert.That(groups, Does.Contain(200));
		Assert.That(groups, Does.Not.Contain(300));
	}

	[Test]
	public void GetKeysUpdatedBetween_ReturnsCorrectGroups() {
		var index = new LastUpdatedIndex<int>();
		var cache = new InMemoryDataCache<int, TestEntity>();
		cache.CacheLastUpdatedIndex(index, (id, e) => e.GroupId, (id, e) => e.UpdatedAt);

		cache.AddOrUpdate(1, new TestEntity { Id = 1, GroupId = 100, UpdatedAt = T(10) });
		cache.AddOrUpdate(2, new TestEntity { Id = 2, GroupId = 200, UpdatedAt = T(20) });
		cache.AddOrUpdate(3, new TestEntity { Id = 3, GroupId = 300, UpdatedAt = T(30) });
		cache.AddOrUpdate(4, new TestEntity { Id = 4, GroupId = 400, UpdatedAt = T(40) });

		var groups = index.GetValuesBetween(T(15), T(35)).ToList();

		Assert.That(groups, Has.Count.EqualTo(2));
		Assert.That(groups, Does.Contain(200));
		Assert.That(groups, Does.Contain(300));
		Assert.That(groups, Does.Not.Contain(100));
		Assert.That(groups, Does.Not.Contain(400));
	}

	[Test]
	public void TryGetMaxForKeys_ReturnsLargestAmongPresentKeys() {
		var index = new LastUpdatedIndex<int>();
		index.Add(100, T(10));
		index.Add(200, T(30));
		index.Add(300, T(20));

		Span<int> keys = [100, 200, 300];
		Assert.That(index.TryGetMax(keys, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(30)));
	}

	[Test]
	public void TryGetMaxForKeys_SkipsAbsentKeys() {
		var index = new LastUpdatedIndex<int>();
		index.Add(100, T(10));
		index.Add(200, T(30));

		// 999 absent; max over present {100, 200} is T(30).
		Span<int> keys = [100, 999, 200];
		Assert.That(index.TryGetMax(keys, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(30)));
	}

	[Test]
	public void TryGetMaxForKeys_AllAbsent_ReturnsFalse() {
		var index = new LastUpdatedIndex<int>();
		index.Add(100, T(10));

		Span<int> keys = [998, 999];
		Assert.That(index.TryGetMax(keys, out var ts), Is.False);
		Assert.That(ts, Is.EqualTo(0L));
	}

	[Test]
	public void TryGetMaxForKeys_EmptySpan_ReturnsFalse() {
		var index = new LastUpdatedIndex<int>();
		index.Add(100, T(10));

		Assert.That(index.TryGetMax(ReadOnlySpan<int>.Empty, out _), Is.False);
	}
}
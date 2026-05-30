namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class FilteredLastUpdatedIndexAdapterTests {
	private readonly struct TestEntity {
		public int GroupId { get; init; }
		public bool Enabled { get; init; }
		public long UpdatedAt { get; init; }
	}

	// Tracks only entities whose Enabled flag is set.
	private readonly struct EnabledOnly : IDataCacheGlobalLastUpdateFilter<TestEntity> {
		public static bool Include(TestEntity value) => value.Enabled;
	}

	private static long T(int minutes) => minutes * 60 * 1000L;

	private static TestEntity Entity(int groupId, bool enabled, int updatedMinutes = 0) =>
		new() { GroupId = groupId, Enabled = enabled, UpdatedAt = T(updatedMinutes) };

	private static LastUpdatedFilteredIndexAdapter<int, TestEntity, int, EnabledOnly> NewAdapter(
		out LastUpdatedIndex<int> index) {
		index = new LastUpdatedIndex<int>();
		return new LastUpdatedFilteredIndexAdapter<int, TestEntity, int, EnabledOnly>(
			index,
			static (_, e) => e.GroupId);
	}

	[Test]
	public void Add_EnabledEntity_IsTracked() {
		var adapter = NewAdapter(out var index);

		adapter.Add(1, Entity(100, true), T(10));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(10)));
	}

	[Test]
	public void Add_DisabledEntity_IsIgnored() {
		var adapter = NewAdapter(out var index);

		adapter.Add(1, Entity(100, false), T(10));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));
		Assert.That(index.TryGetLastUpdated(100, out _), Is.False);
	}

	[Test]
	public void Remove_DisabledEntity_IsNoOp() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, true), T(10));

		// Removing a disabled entity must not touch the count.
		adapter.Remove(2, Entity(100, false), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
	}

	[Test]
	public void Remove_EnabledEntity_Decrements() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, true), T(10));
		adapter.Add(2, Entity(100, true), T(20));

		adapter.Remove(2, Entity(100, true), T(30));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
	}

	[Test]
	public void Update_EnabledToDisabled_RemovesFromIndex() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, true), T(10));

		adapter.Update(1, Entity(100, true), Entity(100, false), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));
	}

	[Test]
	public void Update_DisabledToEnabled_AddsToIndex() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, false), T(10));
		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));

		adapter.Update(1, Entity(100, false), Entity(100, true), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(20)));
	}

	[Test]
	public void Update_DisabledToDisabled_IsNoOp() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, false), T(10));

		adapter.Update(1, Entity(100, false), Entity(100, false), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));
	}

	[Test]
	public void Update_EnabledSameGroup_UpdatesTimestamp() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, true), T(10));

		adapter.Update(1, Entity(100, true), Entity(100, true), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(20)));
	}

	[Test]
	public void Update_EnabledGroupChanged_MovesBetweenGroups() {
		var adapter = NewAdapter(out var index);
		adapter.Add(1, Entity(100, true), T(10));

		adapter.Update(1, Entity(100, true), Entity(200, true), T(20));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(0));
		Assert.That(index.GetEntitiesCount(200), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(200, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(20)));
	}

	[Test]
	public void CustomTimestamp_UsesSelectorTimestamp() {
		var index = new LastUpdatedIndex<int>();
		var adapter = new LastUpdatedFilteredCustomTimeStampIndexAdapter<int, TestEntity, int, EnabledOnly>(
			index,
			static (_, e) => e.GroupId,
			static (_, e) => e.UpdatedAt);

		// AddOrUpdate timestamp (T(99)) must be ignored in favour of the entity's UpdatedAt.
		adapter.Add(1, Entity(100, true, 10), T(99));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(10)));
	}

	[Test]
	public void CustomTimestamp_DisabledToEnabled_UsesNewEntityTimestamp() {
		var index = new LastUpdatedIndex<int>();
		var adapter = new LastUpdatedFilteredCustomTimeStampIndexAdapter<int, TestEntity, int, EnabledOnly>(
			index,
			static (_, e) => e.GroupId,
			static (_, e) => e.UpdatedAt);
		adapter.Add(1, Entity(100, false, 10), T(99));

		adapter.Update(1, Entity(100, false, 10), Entity(100, true, 42), T(99));

		Assert.That(index.GetEntitiesCount(100), Is.EqualTo(1));
		Assert.That(index.TryGetLastUpdated(100, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(T(42)));
	}
}

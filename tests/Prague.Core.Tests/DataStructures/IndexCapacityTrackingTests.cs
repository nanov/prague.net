namespace Prague.Core.Tests.DataStructures;

	using Prague.Core;
	using Prague.Core.Collections;
	using NUnit.Framework;

/// <summary>
///   Rented-slot capacity reporting: PooledSet fires deltas only from cold paths
///   (construction / Grow / Dispose), list indexes aggregate them per index,
///   and DataCacheIndexStatistics exposes the live value for the OTel gauge
///   (prague.kafka.cache.index.capacity). Utilization = values / capacity makes
///   oversized-bucket slack visible in monitoring instead of requiring a memory
///   dump to diagnose.
/// </summary>
[TestFixture]
public class IndexCapacityTrackingTests {
	private sealed class RecordingListener : IPooledSetCapacityListener {
		public readonly List<int> Deltas = [];
		public long Sum;

		public void OnPooledSetCapacityChanged(int deltaSlots) {
			Deltas.Add(deltaSlots);
			Sum += deltaSlots;
		}
	}

	[Test]
	public void PooledSet_ColdPathsReportDeltas_SumTracksLiveCapacity() {
		var listener = new RecordingListener();
		var set = new PooledSet<long, DefaultKeyComparer<long>>(default, listener,
			DataCacheIndexAttribute.NonSetCapacity);

		Assert.That(listener.Deltas, Has.Count.EqualTo(1),
			"construction eagerly rents and reports the first generation");
		Assert.That(listener.Sum, Is.EqualTo(set.CapacitySlots));

		set.Add(1);
		Assert.That(listener.Deltas, Has.Count.EqualTo(1), "an Add within the first generation must not report");
		Assert.That(listener.Sum, Is.EqualTo(set.CapacitySlots));

		for (long i = 2; i <= 500; i++)
			set.Add(i);
		Assert.That(listener.Sum, Is.EqualTo(set.CapacitySlots), "grow ladder deltas must track the live generation");
		Assert.That(set.CapacitySlots, Is.GreaterThanOrEqualTo(500));

		// Remove never shrinks — capacity stays, no new deltas.
		var deltasBeforeRemove = listener.Deltas.Count;
		for (long i = 1; i <= 500; i++)
			set.Remove(i);
		Assert.That(listener.Deltas, Has.Count.EqualTo(deltasBeforeRemove), "Remove must not report capacity");
		Assert.That(listener.Sum, Is.EqualTo(set.CapacitySlots));

		set.Dispose();
		Assert.That(listener.Sum, Is.EqualTo(0), "Dispose must release the full reported capacity");
		Assert.That(set.CapacitySlots, Is.EqualTo(0));
	}

	[Test]
	public void PooledSet_DisposeOfNeverUsedSet_ReleasesTheConstructionReport() {
		var listener = new RecordingListener();
		var set = new PooledSet<long, DefaultKeyComparer<long>>(default, listener,
			DataCacheIndexAttribute.NonSetCapacity);
		set.Dispose();
		set.Dispose(); // idempotent

		Assert.That(listener.Deltas, Has.Count.EqualTo(2),
			"exactly construction + one Dispose; the second Dispose must not report");
		Assert.That(listener.Sum, Is.EqualTo(0),
			"Dispose must release exactly what construction reported");
	}

	[Test]
	public void CacheKeyValueListIndex_CapacityFollowsBucketLifecycle() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (key, _) => key % 3);
		var comparer = default(DefaultKeyComparer<long>);
		ICountableCacheIndex countable = index;

		Assert.That(countable.GetCapacitySlots(), Is.EqualTo(0));

		for (long key = 0; key < 30; key++)
			index.Add(key, comparer.GetHashCode(key), "v", 0);

		var reported = countable.GetCapacitySlots();
		Assert.That(reported, Is.GreaterThan(0));

		// The maintained aggregate must equal the sum over the live buckets.
		var actual = 0UL;
		for (long indexKey = 0; indexKey < 3; indexKey++) {
			var bucket = (PooledSet<long, DefaultKeyComparer<long>>)index.GetValues(indexKey);
			actual += (ulong)bucket.CapacitySlots;
		}

		Assert.That(reported, Is.EqualTo(actual));

		// Emptied buckets are disposed by the index and must release their capacity.
		for (long key = 0; key < 30; key++)
			index.Remove(key, comparer.GetHashCode(key), "v", 0);

		Assert.That(countable.GetCapacitySlots(), Is.EqualTo(0));
	}

	[Test]
	public void CacheKeyValueListIndex_UpdateMovingIndexKey_StaysConsistent() {
		var value = "a";
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);
		var comparer = default(DefaultKeyComparer<long>);
		ICountableCacheIndex countable = index;

		index.Add(1, comparer.GetHashCode(1), value, 0);
		var singleBucket = countable.GetCapacitySlots();
		Assert.That(singleBucket, Is.GreaterThan(0));

		// Move the key to another index key: the old bucket empties and is disposed.
		index.Update(1, comparer.GetHashCode(1), value, "bb", 0);
		Assert.That(countable.GetCapacitySlots(), Is.EqualTo(singleBucket),
			"one live bucket before and after the move");

		index.Remove(1, comparer.GetHashCode(1), "bb", 0);
		Assert.That(countable.GetCapacitySlots(), Is.EqualTo(0));
	}

	[Test]
	public void CacheKeySetIndex_ReportsBackingSetCapacity() {
		var index = new CacheKeySetIndex<long, string>(static (_, v) => v.Length > 0);
		var comparer = default(DefaultKeyComparer<long>);
		ICountableCacheIndex countable = index;

		Assert.That(countable.GetCapacitySlots(), Is.GreaterThan(0),
			"the backing set rents its first generation eagerly at construction");

		for (long key = 0; key < 10; key++)
			index.Add(key, comparer.GetHashCode(key), "v", 0);

		Assert.That(countable.GetCapacitySlots(), Is.GreaterThanOrEqualTo(index.ApproximateCount));
	}

	[Test]
	public void DataCacheIndexStatistics_ExposesLiveAndSnapshotCapacity() {
		var index = new FixedCountersIndex(keys: 3, values: 15, capacity: 32);
		var stats = new DataCacheIndexStatistics(DataCacheIndexType.Many, index);

		Assert.That(stats.LiveCapacitySlots, Is.EqualTo(32));
		Assert.That(stats.CapacitySlots, Is.EqualTo(0), "snapshot not taken yet");

		stats.TakeSnapshot();
		Assert.That(stats.CapacitySlots, Is.EqualTo(32));
	}

	[Test]
	public void DefaultInterfaceImplementation_ReportsZeroForNonTrackingIndexes() {
		ICountableCacheIndex range = new CacheRangeIndex<long, string, long>(static (key, _) => key);
		Assert.That(range.GetCapacitySlots(), Is.EqualTo(0));
	}

	private sealed class FixedCountersIndex(ulong keys, ulong values, ulong capacity) : ICountableCacheIndex {
		public ulong GetCounters(out ulong outValues) {
			outValues = values;
			return keys;
		}

		public ulong GetCapacitySlots() => capacity;
	}
}

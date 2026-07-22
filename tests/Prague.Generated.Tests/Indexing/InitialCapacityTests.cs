namespace Prague.Generated.Tests.Indexing;

using NUnit.Framework;
using Prague.Core.Collections;
using Prague.Generated.Tests.Models;

/// <summary>
///   End-to-end for the [DataCacheIndex(InitialCapacity = ...)] sizing hint:
///   attribute → source generator → Cache.CacheKeyValueListIndex(..., initialBucketCapacity)
///   → per-key bucket PooledSet first-generation capacity. Unhinted indexes keep the
///   default; the hint shrinks per-entity indexes whose buckets are
///   known to hold only a handful of values.
/// </summary>
[TestFixture]
public class InitialCapacityTests {
	private static int BucketCapacity(IReadOnlyCollection<long> bucket) =>
		((PooledSet<long, DefaultKeyComparer<long>>)bucket).CapacitySlots;

	[Test]
	public void HintedIndex_FirstBucketGeneration_HonorsTheHint() {
		var cache = new CapacityHintedEventCache();
		cache.AddOrUpdate(new CapacityHintedEvent { Id = 1, GroupId = 10, PlainGroupId = 10 });

		var hinted = BucketCapacity(cache.GroupIdIndex.GetValues(10));
		var plain = BucketCapacity(cache.PlainGroupIdIndex.GetValues(10));

		Assert.That(plain, Is.EqualTo(107),
			"an unhinted index must keep the default bucket geometry");
		Assert.That(hinted, Is.GreaterThanOrEqualTo(64),
			"the hinted bucket must rent at least InitialCapacity slots");
		Assert.That(hinted, Is.LessThan(plain),
			"the hint must shrink the first generation below the default");
	}

	[Test]
	public void HintedIndex_GrowsPastTheHintOnDemand() {
		var cache = new CapacityHintedEventCache();
		for (long id = 1; id <= 200; id++) {
			cache.AddOrUpdate(new CapacityHintedEvent { Id = id, GroupId = 7, PlainGroupId = 7 });
		}

		Assert.That(cache.GroupIdIndex.GetValues(7), Has.Count.EqualTo(200));
		Assert.That(cache.PlainGroupIdIndex.GetValues(7), Has.Count.EqualTo(200));
		Assert.That(BucketCapacity(cache.GroupIdIndex.GetValues(7)), Is.GreaterThanOrEqualTo(200),
			"the hint is a starting size, not a ceiling");
	}
}

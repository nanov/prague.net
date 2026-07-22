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

	private static int BucketCapacity(IReadOnlyCollection<int> bucket) =>
		((PooledSet<int, DefaultKeyComparer<int>>)bucket).CapacitySlots;

	[Test]
	public void HintedIndex_FirstBucketGeneration_HonorsTheHint() {
		var cache = new CapacityHintedEventCache();
		cache.AddOrUpdate(new CapacityHintedEvent { Id = 1, GroupId = 10, CtorHintedGroupId = 10, PlainGroupId = 10 });

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
	public void CtorSpelledHints_FlowThroughTheGeneratorToo() {
		var cache = new CapacityHintedEventCache();
		cache.AddOrUpdate(new CapacityHintedEvent {
			Id = 1, GroupId = 10, CtorHintedGroupId = 10, TypedCtorHintedGroupId = 10, PlainGroupId = 10
		});

		var ctorHinted = BucketCapacity(cache.CtorHintedGroupIdIndex.GetValues(10));
		var typedCtorHinted = BucketCapacity(cache.TypedCtorHintedGroupIdIndex.GetValues(10));

		Assert.That(ctorHinted, Is.GreaterThanOrEqualTo(8),
			"[DataCacheIndex(8)] must rent at least the requested slots");
		Assert.That(ctorHinted, Is.LessThan(107),
			"the constructor-spelled hint must shrink the first generation below the default");

		Assert.That(typedCtorHinted, Is.GreaterThanOrEqualTo(16),
			"[DataCacheIndex(DataCacheIndexType.Many, 16)] must rent at least the requested slots");
		Assert.That(typedCtorHinted, Is.LessThan(107),
			"the typed constructor-spelled hint must shrink the first generation below the default");
	}

	[Test]
	public void ClassLevelCtorHint_FlowsThroughTheGeneratorToo() {
		// External-type (class-level) spelling: (propertyName, indexName, indexType,
		// initialCapacity). The unhinted sibling index keeps the default geometry.
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "c-1", Status = "new", TotalAmount = 10m });

		var customer = BucketCapacity(cache.CustomerIdIndex.GetValues("c-1"));
		var status = BucketCapacity(cache.StatusIndex.GetValues("new"));

		Assert.That(customer, Is.EqualTo(107),
			"an unhinted class-level index must keep the default bucket geometry");
		Assert.That(status, Is.GreaterThanOrEqualTo(8).And.LessThan(107),
			"the (propertyName, indexName, indexType, capacity) spelling must reach the bucket");
	}

	[Test]
	public void HintedIndex_GrowsPastTheHintOnDemand() {
		var cache = new CapacityHintedEventCache();
		for (long id = 1; id <= 200; id++) {
			cache.AddOrUpdate(new CapacityHintedEvent { Id = id, GroupId = 7, CtorHintedGroupId = 7, PlainGroupId = 7 });
		}

		Assert.That(cache.GroupIdIndex.GetValues(7), Has.Count.EqualTo(200));
		Assert.That(cache.CtorHintedGroupIdIndex.GetValues(7), Has.Count.EqualTo(200));
		Assert.That(cache.PlainGroupIdIndex.GetValues(7), Has.Count.EqualTo(200));
		Assert.That(BucketCapacity(cache.GroupIdIndex.GetValues(7)), Is.GreaterThanOrEqualTo(200),
			"the hint is a starting size, not a ceiling");
	}
}

namespace Prague.Generated.Tests;

using Prague.Core;
using Prague.Generated.Tests.Fixtures;
using Prague.Generated.Tests.Fixtures.Entities;
using NUnit.Framework;

[TestFixture]
public class InMemoryCacheUnitTests {
	[Test]
	public void Basic() {
		var cache = new InMemoryDataCache<string, ProductListing>();
		foreach (var sportSearchEvent in CacheData.Listings)
			cache.AddOrUpdate(sportSearchEvent.CompositeId, sportSearchEvent);

		var ex = cache.TryGet(CacheData.Listings[0].CompositeId, out var searchEvent);
		Assert.That(ex, Is.True);
		Assert.That(searchEvent, Is.SameAs(CacheData.Listings[0]));
	}

	[Test]
	public void Index() {
		var cache = new InMemoryDataCache<string, ProductListing>();
		foreach (var sportSearchEvent in CacheData.Listings)
			cache.AddOrUpdate(sportSearchEvent.CompositeId, sportSearchEvent);

		var ex = cache.TryGet(CacheData.Listings[0].CompositeId, out var searchEvent);
		Assert.That(ex, Is.True);
		Assert.That(searchEvent, Is.SameAs(CacheData.Listings[0]));
	}
}
namespace Prague.Generated.Tests.Cache;

using Prague.Core;
using Prague.Generated.Tests.Fixtures;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;

[TestFixture]
public class InMemoryCacheComprehensiveTests {
	[Test]
	public void AddOrUpdate_WithAllEvents_StoresAllEventsCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();

		// Act
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - All 25 events should be stored
		foreach (var evt in CacheData.Listings) {
			Assert.That(cache.TryGet(evt.CompositeId, out var retrieved), Is.True,
				$"Event {evt.CompositeId} should be found");
			Assert.That(retrieved, Is.SameAs(evt),
				$"Retrieved event should be same instance");
		}
	}

	[Test]
	public void TryGet_WithNonExistingCompositeId_ReturnsFalse() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act & Assert
		Assert.That(cache.TryGet("999999_Invalid", out var value), Is.False);
		Assert.That(value, Is.Null);
	}

	[Test]
	public void AddOrUpdate_UpdateExistingEvent_ReplacesOldValue() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var originalEvent = CacheData.Listings[0];
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Create modified event
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			ProductName = "MODIFIED NAME",
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName,
			ListingStatus = ListingStatus.Active, // Changed from Draft
			ActiveVariantCount = 999 // Changed
		};

		// Act
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert
		Assert.That(cache.TryGet(originalEvent.CompositeId, out var retrieved), Is.True);
		Assert.That(retrieved.ProductName, Is.EqualTo("MODIFIED NAME"));
		Assert.That(retrieved.ActiveVariantCount, Is.EqualTo(999));
		Assert.That(retrieved.ListingStatus, Is.EqualTo(ListingStatus.Active));
	}

	[Test]
	public void AddOrUpdate_WithSameValue_DoesNotTriggerUpdate() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var evt = CacheData.Listings[0];

		// Act - Add twice with same reference
		cache.AddOrUpdate(evt.CompositeId, evt);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - Should succeed without error (Same operation)
		Assert.That(cache.TryGet(evt.CompositeId, out var retrieved), Is.True);
		Assert.That(retrieved, Is.SameAs(evt));
	}

	[Test]
	public void Remove_ExistingEvent_RemovesFromCache() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);
		var eventToRemove = CacheData.Listings[0];

		// Act
		cache.Remove(eventToRemove.CompositeId);

		// Assert
		Assert.That(cache.TryGet(eventToRemove.CompositeId, out _), Is.False);
	}

	[Test]
	public void Remove_NonExistingEvent_DoesNotThrow() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();

		// Act & Assert
		Assert.DoesNotThrow(() => cache.Remove("999999_Invalid"));
	}

	[Test]
	public void KeyValueIndex_ByProviderKey_FindsCorrectEvent() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act & Assert - SourceKey 61272471 (Kitchen electronics match)
		Assert.That(providerIndex.TryGetValue(61272471, out var compositeId), Is.True);
		Assert.That(cache.TryGet(compositeId, out var foundEvent), Is.True);
		Assert.That(foundEvent.ProductName, Does.Contain("Globex Corp"));
		Assert.That(foundEvent.DepartmentId, Is.EqualTo(31)); // Electronics
	}

	[Test]
	public void KeyValueIndex_ByProviderKey_AllEventsIndexed() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act & Assert - Dataset has 3 unique provider keys
		var uniqueProviderKeys = CacheData.Listings
			.Select(e => e.SourceKey)
			.Distinct()
			.ToList();

		foreach (var providerKey in uniqueProviderKeys) {
			Assert.That(providerIndex.TryGetValue(providerKey, out var compositeId), Is.True,
				$"SourceKey {providerKey} should be indexed");
			Assert.That(cache.TryGet(compositeId, out _), Is.True);
		}
	}

	[Test]
	public void KeyValueIndex_UpdateEvent_UpdatesIndex() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		var originalEvent = CacheData.Listings[0];
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update with different provider key
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = 99999999, // Changed
			ProductName = "MODIFIED",
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Old provider key should not exist, new one should
		Assert.That(providerIndex.TryGetValue(originalEvent.SourceKey, out _), Is.False,
			"Old provider key should be removed from index");
		Assert.That(providerIndex.TryGetValue(99999999, out var compositeId), Is.True,
			"New provider key should be in index");
		Assert.That(compositeId, Is.EqualTo(originalEvent.CompositeId));
	}

	[Test]
	public void KeyValueIndex_RemoveEvent_RemovesFromIndex() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		var evt = CacheData.Listings[0];
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		cache.Remove(evt.CompositeId);

		// Assert
		Assert.That(providerIndex.TryGetValue(evt.SourceKey, out _), Is.False);
	}

	[Test]
	public void ListIndex_ByDepartmentId_FindsAllElectronicsEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var electronicsEvents = sportIndex.GetValues(31); // Electronics

		// Assert - Should have 20 electronics events (4 events × 5 channels)
		Assert.That(electronicsEvents.Count, Is.EqualTo(20));
		foreach (var compositeId in electronicsEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.DepartmentId, Is.EqualTo(31));
			Assert.That(evt.DepartmentName, Is.EqualTo("Electronics"));
		}
	}

	[Test]
	public void ListIndex_ByDepartmentId_FindsAllAudioEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var audioEvents = sportIndex.GetValues(32); // Audio

		// Assert - Should have 10 audio events (2 events × 5 channels)
		Assert.That(audioEvents.Count, Is.EqualTo(10));
		foreach (var compositeId in audioEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.DepartmentId, Is.EqualTo(32));
			Assert.That(evt.DepartmentName, Is.EqualTo("Audio"));
		}
	}

	[Test]
	public void ListIndex_ByChannel_FindsAllOnlineEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var onlineEvents = channelIndex.GetValues((int)SalesChannel.Web);

		// Assert - Should have 6 online events (6 unique events)
		Assert.That(onlineEvents.Count, Is.EqualTo(6));
		foreach (var compositeId in onlineEvents) {
			Assert.That(cache.TryGet(compositeId, out var result), Is.True);
			Assert.That(result.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(result.CompositeId, Does.EndWith("_Online"));
		}
	}

	[Test]
	public void ListIndex_ByCountryId_FindsKitchenEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var countryIndex = cache.CacheKeyValueListIndex((key, evt) => evt.CategoryId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var kitchenEvents = countryIndex.GetValues(311); // Kitchen

		// Assert - Should have 5 Kitchen events (1 event × 5 channels)
		Assert.That(kitchenEvents.Count, Is.EqualTo(5));
		foreach (var compositeId in kitchenEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.CategoryId, Is.EqualTo(311));
			Assert.That(evt.CategoryName, Is.EqualTo("Kitchen"));
		}
	}

	[Test]
	public void ListIndex_ByLeagueId_FindsGardenAcmeEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var leagueIndex = cache.CacheKeyValueListIndex((key, evt) => evt.BrandId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var acmeListings = leagueIndex.GetValues(38343); // Garden Acme

		// Assert - Should have 15 events (3 matches × 5 channels)
		Assert.That(acmeListings.Count, Is.EqualTo(15));
		foreach (var compositeId in acmeListings) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.BrandId, Is.EqualTo(38343));
			Assert.That(evt.BrandName, Is.EqualTo("Acme"));
		}
	}

	[Test]
	public void ListIndex_NonExistingDepartmentId_ReturnsEmptyCollection() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var tennisEvents = sportIndex.GetValues(999); // Non-existing sport

		// Assert
		Assert.That(tennisEvents, Is.Empty);
	}

	[Test]
	public void ListIndex_UpdateDepartmentId_MovesEventBetweenIndexes() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		var originalEvent = CacheData.Listings[0]; // Electronics event
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Change from Electronics (31) to Audio (32)
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			DepartmentId = 32, // Changed to Audio
			DepartmentName = "Audio",
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should be in Audio index, not Electronics
		var electronicsEvents = sportIndex.GetValues(31);
		var audioEvents = sportIndex.GetValues(32);

		Assert.That(electronicsEvents, Does.Not.Contain(originalEvent.CompositeId),
			"Event should be removed from Electronics index");
		Assert.That(audioEvents, Does.Contain(originalEvent.CompositeId),
			"Event should be added to Audio index");
	}

	[Test]
	public void ListIndex_UpdateChannel_MovesEventBetweenIndexes() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);

		var originalEvent = CacheData.Listings.First(e => e.ChannelId == SalesChannel.Web);
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Change channel
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = SalesChannel.Mobile, // Changed
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert
		var onlineEvents = channelIndex.GetValues((int)SalesChannel.Web);
		var mobileEvents = channelIndex.GetValues((int)SalesChannel.Mobile);

		Assert.That(onlineEvents, Does.Not.Contain(originalEvent.CompositeId));
		Assert.That(mobileEvents, Does.Contain(originalEvent.CompositeId));
	}

	[Test]
	public void ListIndex_RemoveEvent_RemovesFromListIndex() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		var evt = CacheData.Listings[0];
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		cache.Remove(evt.CompositeId);

		// Assert
		var electronicsEvents = sportIndex.GetValues(31);
		Assert.That(electronicsEvents, Does.Not.Contain(evt.CompositeId));
	}

	[Test]
	public void MultipleIndexes_AllMaintainedCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);
		var leagueIndex = cache.CacheKeyValueListIndex((key, evt) => evt.BrandId);

		// Act
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - Department Index
		Assert.That(sportIndex.GetValues(31).Count, Is.EqualTo(20), "Electronics events");
		Assert.That(sportIndex.GetValues(32).Count, Is.EqualTo(10), "Audio events");

		// Assert - SalesChannel Index
		Assert.That(channelIndex.GetValues((int)SalesChannel.Web).Count, Is.EqualTo(6));
		Assert.That(channelIndex.GetValues((int)SalesChannel.Kiosk).Count, Is.EqualTo(6));
		Assert.That(channelIndex.GetValues((int)SalesChannel.Store).Count, Is.EqualTo(6));
		Assert.That(channelIndex.GetValues((int)SalesChannel.Mobile).Count, Is.EqualTo(6));
		Assert.That(channelIndex.GetValues((int)SalesChannel.Phone).Count, Is.EqualTo(6));

		// Assert - Provider Index (3 unique providers)
		Assert.That(providerIndex.TryGetValue(61272471, out _), Is.True);
		Assert.That(providerIndex.TryGetValue(61709212, out _), Is.True);
		Assert.That(providerIndex.TryGetValue(61709214, out _), Is.True);

		// Assert - Brand Index
		Assert.That(leagueIndex.GetValues(350).Count, Is.EqualTo(5), "Kitchen Initech");
		Assert.That(leagueIndex.GetValues(38343).Count, Is.EqualTo(15), "Garden Acme");
		Assert.That(leagueIndex.GetValues(34234).Count, Is.EqualTo(10), "Office Globex");
	}

	[Test]
	public void MultipleIndexes_UpdateEvent_AllIndexesUpdated() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		var originalEvent = CacheData.Listings[0];
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update multiple indexed fields
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = SalesChannel.Mobile, // Changed
			SourceKey = 99999999, // Changed
			ProductName = originalEvent.ProductName,
			DepartmentId = 32, // Changed to Audio
			DepartmentName = "Audio",
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - All indexes should reflect the update
		Assert.That(sportIndex.GetValues(31), Does.Not.Contain(originalEvent.CompositeId),
			"Should be removed from Electronics");
		Assert.That(sportIndex.GetValues(32), Does.Contain(originalEvent.CompositeId),
			"Should be added to Audio");

		Assert.That(channelIndex.GetValues((int)SalesChannel.Kiosk), Does.Not.Contain(originalEvent.CompositeId),
			"Should be removed from Kiosk");
		Assert.That(channelIndex.GetValues((int)SalesChannel.Mobile), Does.Contain(originalEvent.CompositeId),
			"Should be added to Mobile");

		Assert.That(providerIndex.TryGetValue(originalEvent.SourceKey, out _), Is.False,
			"Old provider key should be removed");
		Assert.That(providerIndex.TryGetValue(99999999, out _), Is.True,
			"New provider key should be added");
	}

	[Test]
	public void MultipleIndexes_RemoveEvent_AllIndexesCleared() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		var eventDoc = CacheData.Listings[0];
		cache.AddOrUpdate(eventDoc.CompositeId, eventDoc);

		// Act
		cache.Remove(eventDoc.CompositeId);

		// Assert - Should be removed from all indexes
		Assert.That(sportIndex.GetValues(eventDoc.DepartmentId), Does.Not.Contain(eventDoc.CompositeId));
		Assert.That(channelIndex.GetValues((int)eventDoc.ChannelId), Does.Not.Contain(eventDoc.CompositeId));
		Assert.That(providerIndex.TryGetValue(eventDoc.SourceKey, out _), Is.False);
	}

	[Test]
	public void ComplexQuery_FindElectronicsEventsInGarden() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var countryIndex = cache.CacheKeyValueListIndex((key, evt) => evt.CategoryId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find electronics events in Garden
		var electronicsEvents = sportIndex.GetValues(31);
		var gardenEvents = countryIndex.GetValues(330);
		var electronicsInGarden = electronicsEvents.Intersect(gardenEvents).ToList();

		// Assert - Should have 15 events (3 German matches × 5 channels)
		Assert.That(electronicsInGarden.Count, Is.EqualTo(15));
		foreach (var compositeId in electronicsInGarden) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.DepartmentId, Is.EqualTo(31));
			Assert.That(evt.CategoryId, Is.EqualTo(330));
			Assert.That(evt.CategoryName, Is.EqualTo("Garden"));
		}
	}

	[Test]
	public void ComplexQuery_FindAudioOnMobileChannel() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var audioEvents = sportIndex.GetValues(32);
		var mobileEvents = channelIndex.GetValues((int)SalesChannel.Mobile);
		var audioOnMobile = audioEvents.Intersect(mobileEvents).ToList();

		// Assert - Should have 2 events (2 audio matches on Mobile)
		Assert.That(audioOnMobile.Count, Is.EqualTo(2));
		foreach (var compositeId in audioOnMobile) {
			Assert.That(cache.TryGet(compositeId, out var result), Is.True);
			Assert.That(result.DepartmentId, Is.EqualTo(32));
			Assert.That(result.ChannelId, Is.EqualTo(SalesChannel.Mobile));
		}
	}

	[Test]
	public void ComplexQuery_CountEventsByLeagueAndChannel() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var leagueIndex = cache.CacheKeyValueListIndex((key, evt) => evt.BrandId);
		var channelIndex = cache.CacheKeyValueListIndex((key, evt) => (int)evt.ChannelId);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find Acme events on Web channel
		var acmeListings = leagueIndex.GetValues(38343);
		var onlineEvents = channelIndex.GetValues((int)SalesChannel.Web);
		var liga3Online = acmeListings.Intersect(onlineEvents).ToList();

		// Assert - Should have 3 events (3 German Acme matches online)
		Assert.That(liga3Online.Count, Is.EqualTo(3));
	}

	[Test]
	public void EdgeCase_AddIndexAfterDataIsPopulated_IndexBuildsCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();

		// Add all data first
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Add index AFTER data is populated
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		// Assert - Index should be empty because it wasn't notified of past additions
		var electronicsEvents = sportIndex.GetValues(31);
		Assert.That(electronicsEvents, Is.Empty,
			"Index added after data won't have historical data");
	}

	[Test]
	public void EdgeCase_UpdateWithSameIndexValue_DoesNotCauseIssues() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);

		var originalEvent = CacheData.Listings[0];
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update with same DepartmentId
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = "MODIFIED NAME",
			DepartmentId = originalEvent.DepartmentId, // Same
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should still be in the same index
		var electronicsEvents = sportIndex.GetValues(31);
		Assert.That(electronicsEvents, Does.Contain(originalEvent.CompositeId));
		Assert.That(cache.TryGet(originalEvent.CompositeId, out var retrieved), Is.True);
		Assert.That(retrieved.ProductName, Is.EqualTo("MODIFIED NAME"));
	}

	[Test]
	public void EdgeCase_MultipleEventsWithSameProviderKey_LastOneWins() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var providerIndex = cache.AddKeyValueIndex((key, evt) => evt.SourceKey);

		// Note: In real data, same SourceKey appears across channels
		// This tests that 1-to-1 index keeps last value
		var events = CacheData.Listings
			.Where(e => e.SourceKey == 61272471)
			.ToList();

		// Act - Add all events with same provider key
		foreach (var evt in events) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - Index should point to one of them (last one)
		Assert.That(providerIndex.TryGetValue(61272471, out var compositeId), Is.True);
		Assert.That(events.Select(e => e.CompositeId), Does.Contain(compositeId));
	}

	[Test]
	public void RangeIndex_WithDuplicateTimestamps_StoresAllValues() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		// Act - Add events with same timestamp (same event across different channels)
		var eventsWithSameTimestamp = CacheData.Listings
			.Where(e => e.Timestamp == 1762006974) // First event's timestamp
			.ToList();

		foreach (var evt in eventsWithSameTimestamp) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - All 5 channels for same event should be stored
		Assert.That(eventsWithSameTimestamp.Count, Is.EqualTo(5), "Should have 5 channels");
		var allKeys = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(allKeys.Count, Is.EqualTo(5), "All 5 keys should be retrievable");

		foreach (var evt in eventsWithSameTimestamp)
			Assert.That(allKeys, Does.Contain(evt.CompositeId),
				$"Should contain {evt.CompositeId}");
	}

	[Test]
	public void RangeIndex_RemoveDuplicateTimestamp_RemovesOnlySpecificValue() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		var eventsWithSameTimestamp = CacheData.Listings
			.Where(e => e.Timestamp == 1762006974)
			.ToList();

		foreach (var evt in eventsWithSameTimestamp) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Remove one specific event
		var eventToRemove = eventsWithSameTimestamp[0];
		cache.Remove(eventToRemove.CompositeId);

		// Assert - Other events with same timestamp should still exist
		var remainingKeys = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(remainingKeys.Count, Is.EqualTo(4), "Should have 4 remaining");
		Assert.That(remainingKeys, Does.Not.Contain(eventToRemove.CompositeId));

		foreach (var evt in eventsWithSameTimestamp.Skip(1)) Assert.That(remainingKeys, Does.Contain(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_AddRemoveAdd_WorksCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);
		var evt = CacheData.Listings[0];

		// Act & Assert - Add
		cache.AddOrUpdate(evt.CompositeId, evt);
		var keys1 = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(keys1, Does.Contain(evt.CompositeId));

		// Remove
		cache.Remove(evt.CompositeId);
		var keys2 = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(keys2, Does.Not.Contain(evt.CompositeId));

		// Add again
		cache.AddOrUpdate(evt.CompositeId, evt);
		var keys3 = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(keys3, Does.Contain(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_MultipleAddsWithSameKey_NoDuplicates() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);
		var evt = CacheData.Listings[0];

		// Act - Add same event multiple times
		cache.AddOrUpdate(evt.CompositeId, evt);
		cache.AddOrUpdate(evt.CompositeId, evt);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - Should only appear once
		var keys = timestampIndex.GetValuesGte(0).ToList();
		var count = keys.Count(k => k == evt.CompositeId);
		Assert.That(count, Is.EqualTo(1), "Event should appear exactly once");
	}

	[Test]
	public void RangeIndex_RangeBetween_WithDuplicatesAndRemovals() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		// Add all events
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Remove some events from the middle of timestamp range
		var eventsToRemove = CacheData.Listings
			.Where(e => e.Timestamp == 1762105214) // Tyrell Corp match
			.Take(2)
			.ToList();

		foreach (var evt in eventsToRemove) cache.Remove(evt.CompositeId);

		// Act - Query range that includes removed events
		var minTimestamp = CacheData.Listings.Min(e => e.Timestamp ?? 0);
		var maxTimestamp = CacheData.Listings.Max(e => e.Timestamp ?? 0);
		var keysInRange = timestampIndex.GetValuesBetween(minTimestamp, maxTimestamp).ToList();

		// Assert - Should have all events minus removed ones
		Assert.That(keysInRange.Count, Is.EqualTo(30 - 2), "Should have 28 events");

		foreach (var removedEvt in eventsToRemove) Assert.That(keysInRange, Does.Not.Contain(removedEvt.CompositeId));
	}

	[Test]
	public void RangeIndex_RangeFrom_AfterMultipleRemoves() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Remove all audio events
		var audioEvents = CacheData.Listings
			.Where(e => e.DepartmentId == 32)
			.ToList();

		foreach (var evt in audioEvents) cache.Remove(evt.CompositeId);

		// Act - Query from start
		var allKeys = timestampIndex.GetValuesGte(0).ToList();

		// Assert - Should only have electronics events (20)
		Assert.That(allKeys.Count, Is.EqualTo(20), "Should have 20 electronics events");

		foreach (var audioEvt in audioEvents) Assert.That(allKeys, Does.Not.Contain(audioEvt.CompositeId));
	}

	[Test]
	public void RangeIndex_RangeTo_WithSelectiveRemovals() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Remove every other Web event
		var onlineEvents = CacheData.Listings
			.Where(e => e.ChannelId == SalesChannel.Web)
			.ToList();

		for (var i = 0; i < onlineEvents.Count; i += 2) cache.Remove(onlineEvents[i].CompositeId);

		// Act
		var maxTimestamp = CacheData.Listings.Max(e => e.Timestamp ?? 0);
		var allKeys = timestampIndex.GetValuesLte(maxTimestamp).ToList();

		// Assert
		Assert.That(allKeys.Count, Is.EqualTo(30 - 3), "Should have 27 events");
	}

	[Test]
	public void RangeIndex_UpdateTimestamp_MovesInIndex() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		var originalEvent = CacheData.Listings[0];
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update with different timestamp
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			Timestamp = 9999999999, // Very high timestamp
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should be queryable at new timestamp
		var oldTimestampKeys = timestampIndex.GetValuesBetween(
			originalEvent.Timestamp ?? 0,
			originalEvent.Timestamp ?? 0).ToList();
		var newTimestampKeys = timestampIndex.GetValuesGte(9999999999).ToList();

		Assert.That(oldTimestampKeys, Does.Not.Contain(originalEvent.CompositeId),
			"Should not be at old timestamp");
		Assert.That(newTimestampKeys, Does.Contain(originalEvent.CompositeId),
			"Should be at new timestamp");
	}

	[Test]
	public void RangeIndex_StressTest_ManyDuplicatesAndRemovals() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		// Add all events multiple times
		for (var iteration = 0; iteration < 3; iteration++)
			foreach (var evt in CacheData.Listings)
				cache.AddOrUpdate(evt.CompositeId, evt);

		// Remove and re-add various events
		var eventsToToggle = CacheData.Listings.Take(10).ToList();
		foreach (var evt in eventsToToggle) {
			cache.Remove(evt.CompositeId);
			cache.AddOrUpdate(evt.CompositeId, evt);
			cache.Remove(evt.CompositeId);
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		// Act
		var allKeys = timestampIndex.GetValuesGte(0).ToList();

		// Assert - Should have all 30 events exactly once
		Assert.That(allKeys.Count, Is.EqualTo(30));
		Assert.That(allKeys.Distinct().Count(), Is.EqualTo(30), "All keys should be unique");

		foreach (var evt in CacheData.Listings) Assert.That(allKeys, Does.Contain(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_MultipleIndexes_RemovalAffectsBoth() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.ActiveVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		var eventToRemove = CacheData.Listings[0];

		// Act
		cache.Remove(eventToRemove.CompositeId);

		// Assert - Removed from both range indexes
		var timestampKeys = timestampIndex.GetValuesGte(0).ToList();
		var betCountKeys = betCountIndex.GetValuesGte(0).ToList();

		Assert.That(timestampKeys, Does.Not.Contain(eventToRemove.CompositeId));
		Assert.That(betCountKeys, Does.Not.Contain(eventToRemove.CompositeId));
	}
}
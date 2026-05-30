namespace Prague.Generated.Tests.Indexing;

using System.Diagnostics;
using Prague.Core;
using Prague.Generated.Tests.Fixtures;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;

[TestFixture]
public class CacheRangeIndexTests {
	// Helper method to create a fresh copy of a ProductListing
	private static ProductListing CloneEvent(ProductListing source) {
		return new ProductListing {
			CompositeId = source.CompositeId,
			Id = source.Id,
			ChannelId = source.ChannelId,
			SourceKey = source.SourceKey,
			ProductName = source.ProductName,
			ReleaseDate = source.ReleaseDate,
			Timestamp = source.Timestamp,
			DepartmentId = source.DepartmentId,
			DepartmentName = source.DepartmentName,
			DepartmentDisplayName = source.DepartmentDisplayName,
			DepartmentDisplayOrder = source.DepartmentDisplayOrder,
			DepartmentDisabled = source.DepartmentDisabled,
			CategoryId = source.CategoryId,
			CategoryName = source.CategoryName,
			CategoryDisplayName = source.CategoryDisplayName,
			CategoryDisplayOrder = source.CategoryDisplayOrder,
			CategoryDisabled = source.CategoryDisabled,
			BrandId = source.BrandId,
			BrandName = source.BrandName,
			BrandDisplayName = source.BrandDisplayName,
			BrandDisplayOrder = source.BrandDisplayOrder,
			BrandDisabled = source.BrandDisabled,
			ManufacturerId = source.ManufacturerId,
			ManufacturerName = source.ManufacturerName,
			ManufacturerDisplayName = source.ManufacturerDisplayName,
			SupplierId = source.SupplierId,
			SupplierName = source.SupplierName,
			SupplierDisplayName = source.SupplierDisplayName,
			ListingStatus = source.ListingStatus,
			ListingStatusManual = source.ListingStatusManual,
			ListingStatusModeration = source.ListingStatusModeration,
			ModerationAction = source.ModerationAction,
			StockStatus = source.StockStatus,
			IsFeatured = source.IsFeatured,
			FeaturedOrder = source.FeaturedOrder,
			ActiveVariantCount = source.ActiveVariantCount,
			TotalVariantCount = source.TotalVariantCount,
			IndexedAt = source.IndexedAt,
			ListingTypeId = source.ListingTypeId,
			CatalogId = source.CatalogId,
			IsPublished = source.IsPublished,
			StatusNote = source.StatusNote,
			IsPinned = source.IsPinned,
			HasPendingReviews = source.HasPendingReviews,
			IsManuallyEdited = source.IsManuallyEdited,
			HasDiscount = source.HasDiscount,
			HasVariants = source.HasVariants
		};
	}

	[Test]
	public void RangeIndex_ByEventDate_AddsAllEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Act
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - All events should be queryable
		var allEvents = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(allEvents.Count, Is.EqualTo(30), "All 30 events should be indexed");
	}

	[Test]
	public void RangeIndeexAddne() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		// Act
		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);
	}

	[Test]
	public void RangeIndex_ByTimestamp_AddsAllEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		// Act
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert
		var allEvents = timestampIndex.GetValuesGte(0).ToList();
		Assert.That(allEvents.Count, Is.EqualTo(30));
	}

	[Test]
	public void GetValuesGte_FindEventsAfterDate() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events on or after Nov 2, 2025
		var cutoffDate = new DateTime(2025, 11, 2, 0, 0, 0, DateTimeKind.Utc);
		var eventsAfter = dateIndex.GetValuesGte(cutoffDate).ToList();

		// Assert - Should find events from Nov 2 onwards
		Assert.That(eventsAfter.Count, Is.GreaterThan(0));
		foreach (var compositeId in eventsAfter) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.ReleaseDate, Is.GreaterThanOrEqualTo(cutoffDate),
				$"Event {evt.ProductName} date should be >= {cutoffDate}");
		}
	}

	[Test]
	public void GetValuesGte_WithMinValue_ReturnsAllEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query from beginning of time
		var allEvents = dateIndex.GetValuesGte(DateTime.MinValue).ToList();

		// Assert
		Assert.That(allEvents.Count, Is.EqualTo(30));
	}

	[Test]
	public void GetValuesGte_WithFutureDate_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query far in the future
		var futureEvents = dateIndex.GetValuesGte(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToList();

		// Assert
		Assert.That(futureEvents, Is.Empty);
	}

	[Test]
	public void GetValuesGte_ByActiveVariantCount_FindsHighActivityEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.ActiveVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events with active bet types >= 0
		var activeEvents = betCountIndex.GetValuesGte(0).ToList();

		// Assert
		Assert.That(activeEvents.Count, Is.GreaterThan(0));
		foreach (var compositeId in activeEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.ActiveVariantCount, Is.GreaterThanOrEqualTo(0));
		}
	}

	[Test]
	public void GetValuesLte_FindEventsBeforeDate() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events up to and including Nov 1, 2025 end of day
		var cutoffDate = new DateTime(2025, 11, 1, 23, 59, 59, DateTimeKind.Utc);
		var eventsBefore = dateIndex.GetValuesLte(cutoffDate).ToList();

		// Assert
		Assert.That(eventsBefore.Count, Is.GreaterThan(0));
		foreach (var compositeId in eventsBefore) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.ReleaseDate, Is.LessThanOrEqualTo(cutoffDate),
				$"Event {evt.ProductName} date should be <= {cutoffDate}");
		}
	}

	[Test]
	public void GetValuesLte_WithMaxValue_ReturnsAllEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var allEvents = dateIndex.GetValuesLte(DateTime.MaxValue).ToList();

		// Assert
		Assert.That(allEvents.Count, Is.EqualTo(30));
	}

	[Test]
	public void GetValuesLte_WithPastDate_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query far in the past
		var pastEvents = dateIndex.GetValuesLte(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToList();

		// Assert
		Assert.That(pastEvents, Is.Empty);
	}

	[Test]
	public void GetValuesLte_ByTotalVariantCount_FindsLowVariantEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events with total bet types <= 100
		var lowBetEvents = betCountIndex.GetValuesLte(100).ToList();

		// Assert
		foreach (var compositeId in lowBetEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.TotalVariantCount, Is.LessThanOrEqualTo(100));
		}
	}

	[Test]
	public void GetValuesBetween_FindEventsInDateRange() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events on Nov 1, 2025 (full day)
		var startDate = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc);
		var endDate = new DateTime(2025, 11, 1, 23, 59, 59, DateTimeKind.Utc);
		var eventsInRange = dateIndex.GetValuesBetween(startDate, endDate).ToList();

		// Assert
		Assert.That(eventsInRange.Count, Is.GreaterThan(0));
		foreach (var compositeId in eventsInRange) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.ReleaseDate, Is.GreaterThanOrEqualTo(startDate));
			Assert.That(evt.ReleaseDate, Is.LessThanOrEqualTo(endDate));
		}
	}

	[Test]
	public void GetValuesBetween_FindEventsInTimestampRange() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Get min and max timestamps from data
		var allTimestamps = CacheData.Listings
			.Select(e => e.Timestamp ?? 0)
			.Where(t => t > 0)
			.OrderBy(t => t)
			.ToList();

		if (!allTimestamps.Any()) {
			Assert.Inconclusive("No valid timestamps in test data");
			return;
		}

		var minTimestamp = allTimestamps.First();
		var maxTimestamp = allTimestamps.Last();

		// Act - Find events in middle range
		var midPoint = (maxTimestamp - minTimestamp) / 2 + minTimestamp;
		var eventsInRange = timestampIndex.GetValuesBetween(midPoint, maxTimestamp).ToList();

		// Assert
		foreach (var compositeId in eventsInRange) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			var timestamp = evt.Timestamp ?? 0;
			Assert.That(timestamp, Is.GreaterThanOrEqualTo(midPoint));
			Assert.That(timestamp, Is.LessThanOrEqualTo(maxTimestamp));
		}
	}

	[Test]
	public void GetValuesBetween_WithEmptyRange_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query range where no events exist
		var startDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var endDate = new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Utc);
		var eventsInRange = dateIndex.GetValuesBetween(startDate, endDate).ToList();

		// Assert
		Assert.That(eventsInRange, Is.Empty);
	}

	[Test]
	public void GetValuesBetween_ByBetCount_FindsModerateActivityEvents() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events with 50-170 total bet types
		var moderateEvents = betCountIndex.GetValuesBetween(50, 170).ToList();

		// Assert
		Assert.That(moderateEvents.Count, Is.GreaterThan(0));
		foreach (var compositeId in moderateEvents) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.TotalVariantCount, Is.GreaterThanOrEqualTo(50));
			Assert.That(evt.TotalVariantCount, Is.LessThanOrEqualTo(170));
		}
	}

	[Test]
	public void RangeIndex_UpdateEventDate_UpdatesIndexCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var originalEvent = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		var originalDate = originalEvent.ReleaseDate;
		var newDate = originalDate.AddDays(10);

		// Act - Update event with new date
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			ReleaseDate = newDate, // Changed
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should be in new date range, not old
		var oldRangeEvents = dateIndex.GetValuesBetween(
			originalDate.AddMinutes(-1),
			originalDate.AddMinutes(1)
		).ToList();
		var newRangeEvents = dateIndex.GetValuesBetween(
			newDate.AddMinutes(-1),
			newDate.AddMinutes(1)
		).ToList();

		Assert.That(oldRangeEvents, Does.Not.Contain(originalEvent.CompositeId),
			"Event should be removed from old date range");
		Assert.That(newRangeEvents, Does.Contain(originalEvent.CompositeId),
			"Event should be added to new date range");
	}

	[Test]
	public void RangeIndex_UpdateBetCount_MovesInSortedOrder() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		var originalEvent = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update bet count from 170 to 50
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			ReleaseDate = originalEvent.ReleaseDate,
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName,
			TotalVariantCount = 50 // Changed from 170
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should be findable in lower range
		var lowBetEvents = betCountIndex.GetValuesBetween(40, 60).ToList();
		var highBetEvents = betCountIndex.GetValuesBetween(160, 180).ToList();

		Assert.That(lowBetEvents, Does.Contain(originalEvent.CompositeId));
		Assert.That(highBetEvents, Does.Not.Contain(originalEvent.CompositeId));
	}

	[Test]
	public void RangeIndex_UpdateWithSameValue_DoesNotCauseIssues() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var originalEvent = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Update with same date
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = "MODIFIED NAME",
			ReleaseDate = originalEvent.ReleaseDate, // Same
			DepartmentId = originalEvent.DepartmentId,
			DepartmentName = originalEvent.DepartmentName,
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - Should still be findable in same range
		var eventsInRange = dateIndex.GetValuesBetween(
			originalEvent.ReleaseDate.AddMinutes(-1),
			originalEvent.ReleaseDate.AddMinutes(1)
		).ToList();

		Assert.That(eventsInRange, Does.Contain(originalEvent.CompositeId));
		Assert.That(cache.TryGet(originalEvent.CompositeId, out var retrieved), Is.True);
		Assert.That(retrieved.ProductName, Is.EqualTo("MODIFIED NAME"));
	}

	[Test]
	public void RangeIndex_RemoveEvent_RemovesFromRangeQueries() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Verify it's there first
		var beforeRemove = dateIndex.GetValuesBetween(
			evt.ReleaseDate.AddMinutes(-1),
			evt.ReleaseDate.AddMinutes(1)
		).ToList();
		Assert.That(beforeRemove, Does.Contain(evt.CompositeId));

		// Act
		cache.Remove(evt.CompositeId);

		// Assert
		var afterRemove = dateIndex.GetValuesBetween(
			evt.ReleaseDate.AddMinutes(-1),
			evt.ReleaseDate.AddMinutes(1)
		).ToList();
		Assert.That(afterRemove, Does.Not.Contain(evt.CompositeId));
	}

	[Test]
	public void MultipleIndexes_RangeAndListIndexes_BothMaintained() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportListIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var dateRangeIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);
		var betCountRangeIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		// Act
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Assert - List index works
		var electronicsEvents = sportListIndex.GetValues(31).ToList();
		Assert.That(electronicsEvents.Count, Is.EqualTo(20));

		// Assert - Date range index works
		var allByDate = dateRangeIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(allByDate.Count, Is.EqualTo(30));

		// Assert - Variant count range index works
		var allByBetCount = betCountRangeIndex.GetValuesGte(0).ToList();
		Assert.That(allByBetCount.Count, Is.EqualTo(30));
	}

	[Test]
	public void MultipleIndexes_UpdateEvent_AllIndexesReflectChange() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportListIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var dateRangeIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var originalEvent = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(originalEvent.CompositeId, originalEvent);

		// Act - Change both sport and date
		var newDate = originalEvent.ReleaseDate.AddDays(100);
		var modifiedEvent = new ProductListing {
			CompositeId = originalEvent.CompositeId,
			Id = originalEvent.Id,
			ChannelId = originalEvent.ChannelId,
			SourceKey = originalEvent.SourceKey,
			ProductName = originalEvent.ProductName,
			ReleaseDate = newDate, // Changed
			DepartmentId = 32, // Changed to Audio
			DepartmentName = "Audio",
			CategoryId = originalEvent.CategoryId,
			CategoryName = originalEvent.CategoryName,
			BrandId = originalEvent.BrandId,
			BrandName = originalEvent.BrandName
		};
		cache.AddOrUpdate(modifiedEvent.CompositeId, modifiedEvent);

		// Assert - List index updated
		var electronicsEvents = sportListIndex.GetValues(31);
		var audioEvents = sportListIndex.GetValues(32);
		Assert.That(electronicsEvents, Does.Not.Contain(originalEvent.CompositeId));
		Assert.That(audioEvents, Does.Contain(originalEvent.CompositeId));

		// Assert - Range index updated
		var newRangeEvents = dateRangeIndex.GetValuesBetween(
			newDate.AddMinutes(-1),
			newDate.AddMinutes(1)
		).ToList();
		Assert.That(newRangeEvents, Does.Contain(originalEvent.CompositeId));
	}

	[Test]
	public void ComplexQuery_CombineRangeAndListIndexes() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var sportListIndex = cache.CacheKeyValueListIndex((key, evt) => evt.DepartmentId);
		var dateRangeIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find Electronics events on Nov 1, 2025
		var electronicsEvents = sportListIndex.GetValues(31).ToHashSet();
		var nov1Start = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc);
		var nov1End = new DateTime(2025, 11, 1, 23, 59, 59, DateTimeKind.Utc);
		var nov1Events = dateRangeIndex.GetValuesBetween(nov1Start, nov1End).ToHashSet();

		var electronicsOnNov1 = electronicsEvents.Intersect(nov1Events).ToList();

		// Assert
		Assert.That(electronicsOnNov1.Count, Is.GreaterThan(0));
		foreach (var compositeId in electronicsOnNov1) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.DepartmentId, Is.EqualTo(31), "Should be electronics");
			Assert.That(evt.ReleaseDate, Is.GreaterThanOrEqualTo(nov1Start));
			Assert.That(evt.ReleaseDate, Is.LessThanOrEqualTo(nov1End));
		}
	}

	[Test]
	public void ComplexQuery_FindHighActivityEventsInDateRange() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateRangeIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);
		var betCountRangeIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Find events with 100+ bet types in November 2025
		var novStart = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc);
		var novEnd = new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc);
		var novemberEvents = dateRangeIndex.GetValuesBetween(novStart, novEnd).ToHashSet();
		var highActivityEvents = betCountRangeIndex.GetValuesGte(100).ToHashSet();

		var highActivityInNov = novemberEvents.Intersect(highActivityEvents).ToList();

		// Assert
		Assert.That(highActivityInNov.Count, Is.GreaterThan(0));
		foreach (var compositeId in highActivityInNov) {
			Assert.That(cache.TryGet(compositeId, out var evt), Is.True);
			Assert.That(evt.ReleaseDate.Month, Is.EqualTo(11));
			Assert.That(evt.ReleaseDate.Year, Is.EqualTo(2025));
			Assert.That(evt.TotalVariantCount, Is.GreaterThanOrEqualTo(100));
		}
	}

	[Test]
	public void RangeIndex_WithNullableValues_HandlesCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		// Use coalescing to handle nullable Timestamp
		var timestampIndex = cache.CacheRangeIndex((key, evt) => evt.Timestamp ?? 0);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var allEvents = timestampIndex.GetValuesGte(0).ToList();

		// Assert - Should handle all events even if some timestamps are null
		Assert.That(allEvents.Count, Is.EqualTo(30));
	}

	[Test]
	public void RangeIndex_AddIndexAfterData_IndexIsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();

		// Add all data first
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Add index AFTER data is populated
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Assert - Index should be empty (no historical data)
		var allEvents = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(allEvents, Is.Empty,
			"Range index added after data won't have historical data");
	}

	[Test]
	public void GetValuesGt_ExcludesBoundaryValue() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Add values 1 through 10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, i);

		// Act - Get values strictly greater than 5 (should exclude 5)
		var results = rangeIndex.GetValuesGt(5).ToList();

		// Assert
		Assert.That(results, Does.Not.Contain(5), "GT should exclude the boundary value");
		Assert.That(results, Has.Count.EqualTo(5), "Should have 6, 7, 8, 9, 10");
		foreach (var key in results) {
			cache.TryGet(key, out var val);
			Assert.That(val.Value, Is.GreaterThan(5));
		}
	}

	[Test]
	public void GetValuesGt_VsGetValuesGte_DiffersByOne() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var gteResults = betCountIndex.GetValuesGte(100).ToHashSet();
		var gtResults = betCountIndex.GetValuesGt(100).ToHashSet();

		// Assert - Gte should include events with exactly 100, Gt should not
		var eventsExactly100 = gteResults.Except(gtResults).ToList();

		foreach (var compositeId in eventsExactly100) {
			cache.TryGet(compositeId, out var evt);
			Assert.That(evt.TotalVariantCount, Is.EqualTo(100));
		}
	}

	[Test]
	public void GetValuesLt_ExcludesBoundaryValue() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Add values 1 through 10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, i);

		// Act - Get values strictly less than 5 (should exclude 5)
		var results = rangeIndex.GetValuesLt(5).ToList();

		// Assert
		Assert.That(results, Does.Not.Contain(5), "LT should exclude the boundary value");
		Assert.That(results, Has.Count.EqualTo(4), "Should have 1, 2, 3, 4");
		foreach (var key in results) {
			cache.TryGet(key, out var val);
			Assert.That(val.Value, Is.LessThan(5));
		}
	}

	[Test]
	public void GetValuesLt_VsGetValuesLte_DiffersByOne() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.ActiveVariantCount);

		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var lteResults = betCountIndex.GetValuesLte(50).ToHashSet();
		var ltResults = betCountIndex.GetValuesLt(50).ToHashSet();

		// Assert - Lte should include events with exactly 50, Lt should not
		var eventsExactly50 = lteResults.Except(ltResults).ToList();

		foreach (var compositeId in eventsExactly50) {
			cache.TryGet(compositeId, out var evt);
			Assert.That(evt.ActiveVariantCount, Is.EqualTo(50));
		}
	}

	[Test]
	public void GetValuesBetween_StrictBoundaries_ExcludesBoth() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Add values 1 through 10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, i);

		// Act - Get values strictly between 3 and 8 (exclude both 3 and 8)
		var results = rangeIndex.GetValuesBetween(3, 8, false, false).ToList();

		// Assert
		Assert.That(results, Does.Not.Contain(3), "Should exclude lower boundary");
		Assert.That(results, Does.Not.Contain(8), "Should exclude upper boundary");
		Assert.That(results, Has.Count.EqualTo(4), "Should have 4, 5, 6, 7");
		foreach (var key in results) {
			cache.TryGet(key, out var val);
			Assert.That(val.Value, Is.GreaterThan(3).And.LessThan(8));
		}
	}

	[Test]
	public void GetValuesBetween_IncludeFromExcludeTo() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Add values 1 through 10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, i);

		// Act - Get values: 5 <= x < 8
		var results = rangeIndex.GetValuesBetween(5, 8, true, false).ToList();

		// Assert
		Assert.That(results, Does.Contain(5), "Should include lower boundary");
		Assert.That(results, Does.Not.Contain(8), "Should exclude upper boundary");
		Assert.That(results, Has.Count.EqualTo(3), "Should have 5, 6, 7");
	}

	[Test]
	public void GetValuesBetween_ExcludeFromIncludeTo() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Add values 1 through 10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, i);

		// Act - Get values: 5 < x <= 8
		var results = rangeIndex.GetValuesBetween(5, 8, false, true).ToList();

		// Assert
		Assert.That(results, Does.Not.Contain(5), "Should exclude lower boundary");
		Assert.That(results, Does.Contain(8), "Should include upper boundary");
		Assert.That(results, Has.Count.EqualTo(3), "Should have 6, 7, 8");
	}

	[Test]
	public void RangeIndex_MultipleItemsSameValue_AllReturned() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.ActiveVariantCount);

		// Create multiple events with same ActiveVariantCount = 42
		var events = new List<ProductListing>();
		for (var i = 0; i < 5; i++) {
			var evt = new ProductListing {
				CompositeId = $"evt-{i}",
				Id = i,
				ChannelId = SalesChannel.Web,
				SourceKey = 1,
				ProductName = $"Event {i}",
				ReleaseDate = DateTime.UtcNow.AddDays(i),
				DepartmentId = 31,
				DepartmentName = "Electronics",
				CategoryId = 1,
				CategoryName = "Category",
				BrandId = 1,
				BrandName = "Brand",
				ActiveVariantCount = 42 // All same
			};
			events.Add(evt);
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		// Act
		var results = betCountIndex.GetValuesGte(42).ToList();

		// Assert - Should find all 5 events
		Assert.That(results, Has.Count.GreaterThanOrEqualTo(5));
		foreach (var evt in events) Assert.That(results, Does.Contain(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_DuplicateValues_RemoveOne_OthersRemain() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		// Create 3 events with same TotalVariantCount = 100
		var evt1 = new ProductListing {
			CompositeId = "evt-1",
			Id = 1,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 1",
			ReleaseDate = DateTime.UtcNow,
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};
		var evt2 = new ProductListing {
			CompositeId = "evt-2",
			Id = 2,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 2",
			ReleaseDate = DateTime.UtcNow.AddDays(1),
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};
		var evt3 = new ProductListing {
			CompositeId = "evt-3",
			Id = 3,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 3",
			ReleaseDate = DateTime.UtcNow.AddDays(2),
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};

		cache.AddOrUpdate(evt1.CompositeId, evt1);
		cache.AddOrUpdate(evt2.CompositeId, evt2);
		cache.AddOrUpdate(evt3.CompositeId, evt3);

		// Act - Remove one
		cache.Remove(evt2.CompositeId);

		// Assert - Other two should still be there
		var results = betCountIndex.GetValuesGte(100).ToList();
		Assert.That(results, Does.Contain(evt1.CompositeId));
		Assert.That(results, Does.Not.Contain(evt2.CompositeId));
		Assert.That(results, Does.Contain(evt3.CompositeId));
	}

	[Test]
	public void RangeIndex_DuplicateValues_UpdateOne_MovesCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		// Create 2 events with same TotalVariantCount = 100
		var evt1 = new ProductListing {
			CompositeId = "evt-1",
			Id = 1,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 1",
			ReleaseDate = DateTime.UtcNow,
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};
		var evt2 = new ProductListing {
			CompositeId = "evt-2",
			Id = 2,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 2",
			ReleaseDate = DateTime.UtcNow.AddDays(1),
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};

		cache.AddOrUpdate(evt1.CompositeId, evt1);
		cache.AddOrUpdate(evt2.CompositeId, evt2);

		// Act - Update evt1 to have different count (create new instance)
		var updatedEvt1 = new ProductListing {
			CompositeId = evt1.CompositeId,
			Id = evt1.Id,
			ChannelId = evt1.ChannelId,
			SourceKey = evt1.SourceKey,
			ProductName = evt1.ProductName,
			ReleaseDate = evt1.ReleaseDate,
			DepartmentId = evt1.DepartmentId,
			DepartmentName = evt1.DepartmentName,
			CategoryId = evt1.CategoryId,
			CategoryName = evt1.CategoryName,
			BrandId = evt1.BrandId,
			BrandName = evt1.BrandName,
			TotalVariantCount = 200 // Changed
		};
		cache.AddOrUpdate(updatedEvt1.CompositeId, updatedEvt1);

		// Assert
		var range100 = betCountIndex.GetValuesBetween(95, 105).ToList();
		var range200 = betCountIndex.GetValuesBetween(195, 205).ToList();

		Assert.That(range100, Does.Not.Contain(evt1.CompositeId));
		Assert.That(range100, Does.Contain(evt2.CompositeId));
		Assert.That(range200, Does.Contain(evt1.CompositeId));
		Assert.That(range200, Does.Not.Contain(evt2.CompositeId));
	}

	[Test]
	public void RangeIndex_EmptyIndex_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Act - Query empty index
		var results = dateIndex.GetValuesGte(DateTime.MinValue).ToList();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void RangeIndex_SingleItem_GteIncludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var results = dateIndex.GetValuesGte(evt.ReleaseDate).ToList();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0], Is.EqualTo(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_SingleItem_GtExcludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query with strict GT at exact value
		var results = dateIndex.GetValuesGt(evt.ReleaseDate).ToList();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void RangeIndex_SingleItem_LteIncludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act
		var results = dateIndex.GetValuesLte(evt.ReleaseDate).ToList();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0], Is.EqualTo(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_SingleItem_LtExcludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query with strict LT at exact value
		var results = dateIndex.GetValuesLt(evt.ReleaseDate).ToList();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void RangeIndex_SingleItem_BetweenInclusive_IncludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query range that exactly matches the item
		var results = dateIndex.GetValuesBetween(evt.ReleaseDate, evt.ReleaseDate).ToList();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0], Is.EqualTo(evt.CompositeId));
	}

	[Test]
	public void RangeIndex_SingleItem_BetweenExclusive_ExcludesIt() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var evt = CloneEvent(CacheData.Listings[0]);
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Query range with exclusive boundaries at exact value
		var results = dateIndex.GetValuesBetween(
			evt.ReleaseDate,
			evt.ReleaseDate,
			false,
			false
		).ToList();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void RangeIndex_ConcurrentAdds_AllItemsIndexed() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Act - Add items concurrently
		var tasks = new List<Task>();
		for (var i = 0; i < 10; i++) {
			var index = i;
			tasks.Add(Task.Run(() => {
				for (var j = 0; j < 10; j++) {
					var evt = new ProductListing {
						CompositeId = $"evt-{index}-{j}",
						Id = index * 10 + j,
						ChannelId = SalesChannel.Web,
						SourceKey = 1,
						ProductName = $"Event {index}-{j}",
						ReleaseDate = DateTime.UtcNow.AddDays(index * 10 + j),
						DepartmentId = 31,
						DepartmentName = "Electronics",
						CategoryId = 1,
						CategoryName = "Category",
						BrandId = 1,
						BrandName = "Brand"
					};
					cache.AddOrUpdate(evt.CompositeId, evt);
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		// Assert - All 100 items should be indexed
		var allEvents = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(allEvents, Has.Count.EqualTo(100));
	}

	[Test]
	public void RangeIndex_ConcurrentReads_NoExceptions() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Pre-populate
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Read concurrently
		var tasks = new List<Task>();
		var exception = false;
		for (var i = 0; i < 20; i++)
			tasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 100; j++) {
						var results = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
						Assert.That(results, Is.Not.Empty);
					}
				}
				catch {
					exception = true;
				}
			}));
		Task.WaitAll(tasks.ToArray());

		// Assert
		Assert.That(exception, Is.False, "No exceptions should occur during concurrent reads");
	}

	[Test]
	public void RangeIndex_ConcurrentReadAndWrite_Consistent() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Pre-populate
		foreach (var evt in CacheData.Listings) cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Read and write concurrently
		var readTasks = new List<Task>();
		var writeTasks = new List<Task>();
		var exception = false;

		for (var i = 0; i < 10; i++)
			readTasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 50; j++) {
						var results = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
						// Just verify it doesn't crash
					}
				}
				catch {
					exception = true;
				}
			}));

		for (var i = 0; i < 5; i++) {
			var index = i;
			writeTasks.Add(Task.Run(() => {
				try {
					for (var j = 0; j < 20; j++) {
						var evt = new ProductListing {
							CompositeId = $"concurrent-{index}-{j}",
							Id = index * 100 + j,
							ChannelId = SalesChannel.Web,
							SourceKey = 1,
							ProductName = $"Concurrent Event {index}-{j}",
							ReleaseDate = DateTime.UtcNow.AddDays(index * 20 + j),
							DepartmentId = 31,
							DepartmentName = "Electronics",
							CategoryId = 1,
							CategoryName = "Category",
							BrandId = 1,
							BrandName = "Brand"
						};
						cache.AddOrUpdate(evt.CompositeId, evt);
					}
				}
				catch {
					exception = true;
				}
			}));
		}

		Task.WaitAll(readTasks.Concat(writeTasks).ToArray());

		// Assert
		Assert.That(exception, Is.False, "No exceptions should occur during concurrent operations");
	}

	[Test]
	public void RangeIndex_ConcurrentUpdates_SameKey_NoCorruption() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		// Create a fresh instance instead of using shared test data
		var originalEvt = new ProductListing {
			CompositeId = "test-concurrent-update",
			Id = 999,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Concurrent Test Event",
			ReleaseDate = DateTime.UtcNow,
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};
		cache.AddOrUpdate(originalEvt.CompositeId, originalEvt);

		// Act - Update same key concurrently with different values in batches
		// Each update creates a new object to avoid reference sharing issues
		var tasks = new List<Task>();

		// Split into 4 tasks, each doing 3 sequential updates to reduce contention
		for (var batch = 0; batch < 4; batch++) {
			var batchIndex = batch;
			tasks.Add(Task.Run(() => {
				for (var i = 0; i < 3; i++) {
					var count = (batchIndex * 3 + i) * 10;
					var updatedEvt = new ProductListing {
						CompositeId = originalEvt.CompositeId,
						Id = originalEvt.Id,
						ChannelId = originalEvt.ChannelId,
						SourceKey = originalEvt.SourceKey,
						ProductName = originalEvt.ProductName,
						ReleaseDate = originalEvt.ReleaseDate,
						DepartmentId = originalEvt.DepartmentId,
						DepartmentName = originalEvt.DepartmentName,
						CategoryId = originalEvt.CategoryId,
						CategoryName = originalEvt.CategoryName,
						BrandId = originalEvt.BrandId,
						BrandName = originalEvt.BrandName,
						TotalVariantCount = count
					};
					cache.AddOrUpdate(updatedEvt.CompositeId, updatedEvt);
					// Small delay to reduce contention
					Thread.Sleep(1);
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		// Assert - Event should be queryable and have one of the values
		cache.TryGet(originalEvt.CompositeId, out var finalEvt);
		Assert.That(finalEvt, Is.Not.Null);
		Assert.That(finalEvt.TotalVariantCount, Is.GreaterThanOrEqualTo(0));

		// Should be findable at its current value
		var results = betCountIndex.GetValuesGte(finalEvt.TotalVariantCount).ToList();
		Assert.That(results, Does.Contain(originalEvt.CompositeId));
	}

	[Test]
	public void RangeIndex_ConcurrentRemoves_NoExceptions() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Add events
		var events = new List<ProductListing>();
		for (var i = 0; i < 20; i++) {
			var evt = new ProductListing {
				CompositeId = $"evt-{i}",
				Id = i,
				ChannelId = SalesChannel.Web,
				SourceKey = 1,
				ProductName = $"Event {i}",
				ReleaseDate = DateTime.UtcNow.AddDays(i),
				DepartmentId = 31,
				DepartmentName = "Electronics",
				CategoryId = 1,
				CategoryName = "Category",
				BrandId = 1,
				BrandName = "Brand"
			};
			events.Add(evt);
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		// Act - Remove in batches to avoid potential deadlocks
		var exception = false;
		var tasks = new List<Task>();

		// Split into 4 groups of 5 items each - more realistic concurrency pattern
		for (var batch = 0; batch < 4; batch++) {
			var batchIndex = batch;
			tasks.Add(Task.Run(() => {
				try {
					for (var i = 0; i < 5; i++) {
						var idx = batchIndex * 5 + i;
						cache.Remove(events[idx].CompositeId);
						// Small delay to reduce contention
						Thread.Sleep(1);
					}
				}
				catch {
					exception = true;
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		// Assert
		Assert.That(exception, Is.False, "No exceptions should occur during concurrent removes");
		var remaining = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(remaining, Is.Empty);
	}

	[Test]
	[Category("Performance")]
	public void RangeIndex_LargeDataset_AddsEfficiently() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		var stopwatch = Stopwatch.StartNew();

		// Act - Add 10,000 events
		for (var i = 0; i < 10000; i++) {
			var evt = new ProductListing {
				CompositeId = $"evt-{i}",
				Id = i,
				ChannelId = SalesChannel.Web,
				SourceKey = 1,
				ProductName = $"Event {i}",
				ReleaseDate = DateTime.UtcNow.AddSeconds(i),
				DepartmentId = 31,
				DepartmentName = "Electronics",
				CategoryId = 1,
				CategoryName = "Category",
				BrandId = 1,
				BrandName = "Brand"
			};
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		stopwatch.Stop();

		// Assert
		Console.WriteLine($"Added 10,000 events in {stopwatch.ElapsedMilliseconds}ms");
		var allEvents = dateIndex.GetValuesGte(DateTime.MinValue).ToList();
		Assert.That(allEvents, Has.Count.EqualTo(10000));
	}

	[Test]
	[Category("Performance")]
	public void RangeIndex_LargeDataset_RangeQueryEfficient() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var dateIndex = cache.CacheRangeIndex((key, evt) => evt.ReleaseDate);

		// Add 10,000 events spread over 100 days
		var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		for (var i = 0; i < 10000; i++) {
			var evt = new ProductListing {
				CompositeId = $"evt-{i}",
				Id = i,
				ChannelId = SalesChannel.Web,
				SourceKey = 1,
				ProductName = $"Event {i}",
				ReleaseDate = baseDate.AddMinutes(i),
				DepartmentId = 31,
				DepartmentName = "Electronics",
				CategoryId = 1,
				CategoryName = "Category",
				BrandId = 1,
				BrandName = "Brand"
			};
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		// Act - Query a small range (1 day = 1440 minutes, so query day 2-3)
		var stopwatch = Stopwatch.StartNew();
		var startRange = baseDate.AddMinutes(1440 * 2); // Day 2
		var endRange = baseDate.AddMinutes(1440 * 3); // Day 3
		var results = dateIndex.GetValuesBetween(startRange, endRange).ToList();
		stopwatch.Stop();

		// Assert - Should be fast (sub-millisecond ideally)
		Console.WriteLine(
			$"Range query on 10,000 items took {stopwatch.ElapsedMilliseconds}ms, found {results.Count} results");
		Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100), "Range query should be efficient");
		Assert.That(results.Count, Is.GreaterThan(0), "Should find events in the queried range");
	}

	[Test]
	[Category("Performance")]
	public void RangeIndex_RepeatedUpdates_NoMemoryLeak() {
		// Arrange
		var cache = new InMemoryDataCache<string, ProductListing>();
		var betCountIndex = cache.CacheRangeIndex((key, evt) => evt.TotalVariantCount);

		var evt = new ProductListing {
			CompositeId = "evt-1",
			Id = 1,
			ChannelId = SalesChannel.Web,
			SourceKey = 1,
			ProductName = "Event 1",
			ReleaseDate = DateTime.UtcNow,
			DepartmentId = 31,
			DepartmentName = "Electronics",
			CategoryId = 1,
			CategoryName = "Category",
			BrandId = 1,
			BrandName = "Brand",
			TotalVariantCount = 100
		};
		cache.AddOrUpdate(evt.CompositeId, evt);

		// Act - Update same event 10,000 times with different values
		for (var i = 0; i < 10000; i++) {
			evt.TotalVariantCount = i;
			cache.AddOrUpdate(evt.CompositeId, evt);
		}

		// Assert - Should only have one entry in the index
		var allResults = betCountIndex.GetValuesGte(0).ToList();
		Assert.That(allResults, Has.Count.EqualTo(1),
			"Index should contain only one entry despite many updates");
	}

	[Test]
	public void RangeIndex_MinMaxIntValues_HandlesCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Act - Add items with extreme values
		cache.AddOrUpdate(1, int.MinValue);
		cache.AddOrUpdate(2, 0);
		cache.AddOrUpdate(3, int.MaxValue);

		// Assert
		var allValues = rangeIndex.GetValuesGte(int.MinValue).ToList();
		Assert.That(allValues, Has.Count.EqualTo(3));

		var minOnly = rangeIndex.GetValuesBetween(int.MinValue, int.MinValue).ToList();
		Assert.That(minOnly, Has.Count.EqualTo(1));
		Assert.That(minOnly[0], Is.EqualTo(1));

		var maxOnly = rangeIndex.GetValuesBetween(int.MaxValue, int.MaxValue).ToList();
		Assert.That(maxOnly, Has.Count.EqualTo(1));
		Assert.That(maxOnly[0], Is.EqualTo(3));
	}

	[Test]
	public void RangeIndex_NegativeValues_HandlesCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Act - Add negative and positive values
		cache.AddOrUpdate(1, -100);
		cache.AddOrUpdate(2, -50);
		cache.AddOrUpdate(3, 0);
		cache.AddOrUpdate(4, 50);
		cache.AddOrUpdate(5, 100);

		// Assert
		var negativeValues = rangeIndex.GetValuesLte(-1).ToList();
		Assert.That(negativeValues, Has.Count.EqualTo(2));

		var positiveValues = rangeIndex.GetValuesGte(1).ToList();
		Assert.That(positiveValues, Has.Count.EqualTo(2));

		var aroundZero = rangeIndex.GetValuesBetween(-25, 25).ToList();
		Assert.That(aroundZero, Has.Count.EqualTo(1));
		Assert.That(aroundZero[0], Is.EqualTo(3));
	}
}
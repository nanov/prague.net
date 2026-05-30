namespace Prague.Generated.Tests.Cache;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;
#if true

/// <summary>
///   Comprehensive test suite for generated ProductListingCache
///   Tests all generated methods, indexes, and query builder functionality
/// </summary>
[TestFixture]
public class GeneratedCacheTests {
	[SetUp]
	public void SetUp() {
		_cache = new ProductListingCache();
		_testData = CreateTestData();
	}

	private ProductListingCache _cache;
	private List<ProductListing> _testData;

	private List<ProductListing> CreateTestData() {
		var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);

		return new List<ProductListing> {
			new() {
				CompositeId = "1_Online",
				Id = 1,
				ChannelId = SalesChannel.Web,
				DepartmentId = 1, // Electronics
				CategoryId = 10,
				BrandId = 100,
				ReleaseDate = baseDate,
				ListingStatus = ListingStatus.Draft,
				StockStatus = StockStatus.InStock,
				IsFeatured = true,
				FeaturedOrder = 10,
				ActiveVariantCount = 50,
				ListingTypeId = 1,
				IsPublished = false,
				HasDiscount = false,
				ProductName = "Team A vs Team B",
				DepartmentName = "Electronics",
				CategoryName = "England",
				BrandName = "Premier Brand",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			new() {
				CompositeId = "2_Online",
				Id = 2,
				ChannelId = SalesChannel.Web,
				DepartmentId = 1, // Electronics
				CategoryId = 10,
				BrandId = 101,
				ReleaseDate = baseDate.AddDays(1),
				ListingStatus = ListingStatus.Active,
				StockStatus = StockStatus.Reserved,
				IsFeatured = false,
				FeaturedOrder = 50,
				ActiveVariantCount = 30,
				ListingTypeId = 1,
				IsPublished = true,
				HasDiscount = true,
				ProductName = "Team C vs Team D",
				DepartmentName = "Electronics",
				CategoryName = "England",
				BrandName = "Championship",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			new() {
				CompositeId = "3_Mobile",
				Id = 3,
				ChannelId = SalesChannel.Mobile,
				DepartmentId = 2, // Audio
				CategoryId = 20,
				BrandId = 200,
				ReleaseDate = baseDate.AddDays(2),
				ListingStatus = ListingStatus.Draft,
				StockStatus = StockStatus.InStock,
				IsFeatured = true,
				FeaturedOrder = 5,
				ActiveVariantCount = 40,
				ListingTypeId = 2,
				IsPublished = false,
				HasDiscount = false,
				ProductName = "Lakers vs Celtics",
				DepartmentName = "Audio",
				CategoryName = "USA",
				BrandName = "NBA",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			new() {
				CompositeId = "4_Online",
				Id = 4,
				ChannelId = SalesChannel.Web,
				DepartmentId = 2, // Audio
				CategoryId = 20,
				BrandId = 200,
				ReleaseDate = baseDate.AddHours(6),
				ListingStatus = ListingStatus.Archived,
				StockStatus = StockStatus.InStock,
				IsFeatured = false,
				FeaturedOrder = 100,
				ActiveVariantCount = 0,
				ListingTypeId = 2,
				IsPublished = false,
				HasDiscount = false,
				ProductName = "Bulls vs Heat",
				DepartmentName = "Audio",
				CategoryName = "USA",
				BrandName = "NBA",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			}
		};
	}

	[Test]
	public void AddOrUpdate_SingleDocument_ShouldAddToCache() {
		// Arrange
		var doc = _testData[0];

		// Act
		_cache.AddOrUpdate(doc);

		// Assert
		var retrieved = _cache.TryGet(doc.CompositeId, out var result);
		Assert.That(retrieved, Is.True);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Id, Is.EqualTo(doc.Id));
	}

	[Test]
	public void AddOrUpdate_MultipleDocuments_ShouldAddAllToCache() {
		// Act
		foreach (var doc in _testData) _cache.AddOrUpdate(doc);

		// Assert
		foreach (var doc in _testData) {
			var retrieved = _cache.TryGet(doc.CompositeId, out var result);
			Assert.That(retrieved, Is.True);
			Assert.That(result?.Id, Is.EqualTo(doc.Id));
		}
	}

	[Test]
	public void AddOrUpdate_ExistingDocument_ShouldUpdate() {
		// Arrange
		var doc = _testData[0];
		_cache.AddOrUpdate(doc);

		// Act
		var updated = new ProductListing {
			CompositeId = doc.CompositeId,
			Id = doc.Id,
			ProductName = "Updated Event Name",
			ChannelId = doc.ChannelId,
			DepartmentId = doc.DepartmentId,
			CategoryId = doc.CategoryId,
			BrandId = doc.BrandId,
			ReleaseDate = doc.ReleaseDate,
			ListingStatus = doc.ListingStatus,
			StockStatus = doc.StockStatus,
			IsFeatured = doc.IsFeatured,
			FeaturedOrder = doc.FeaturedOrder,
			ActiveVariantCount = doc.ActiveVariantCount,
			ListingTypeId = doc.ListingTypeId,
			IsPublished = doc.IsPublished,
			HasDiscount = doc.HasDiscount,
			DepartmentName = doc.DepartmentName,
			CategoryName = doc.CategoryName,
			BrandName = doc.BrandName,
			CategoryDisabled = false,
			BrandDisabled = false,
			DepartmentDisabled = false
		};
		_cache.AddOrUpdate(updated);

		// Assert
		var retrieved = _cache.TryGet(doc.CompositeId, out var result);
		Assert.That(retrieved, Is.True);
		Assert.That(result?.ProductName, Is.EqualTo("Updated Event Name"));
	}

	[Test]
	public void Remove_ExistingDocument_ShouldRemoveFromCache() {
		// Arrange
		var doc = _testData[0];
		_cache.AddOrUpdate(doc);

		// Act
		_cache.Remove(doc.CompositeId);

		// Assert
		var retrieved = _cache.TryGet(doc.CompositeId, out _);
		Assert.That(retrieved, Is.False);
	}

	[Test]
	public void TryGet_NonExistentKey_ShouldReturnFalse() {
		// Act
		var retrieved = _cache.TryGet("nonexistent", out var result);

		// Assert
		Assert.That(retrieved, Is.False);
		Assert.That(result, Is.Null);
	}

	[Test]
	public void Query_WithId_SingleValue_ShouldReturnMatchingDocument() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithId(1L)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	[Test]
	public void Query_WithId_ListOfValues_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithId(new List<long> { 1, 3 })
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(r => r.Id), Is.EquivalentTo(new[] { 1L, 3L }));
	}

	[Test]
	public void Query_WithChannelId_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(3)); // 3 Web events
		Assert.That(results.All(r => r.ChannelId == SalesChannel.Web), Is.True);
	}

	[Test]
	public void Query_WithDepartmentId_MultipleValues_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithDepartmentId(new List<long> { 1, 2 })
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(4)); // All events
	}

	[Test]
	public void Query_WithLeagueId_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithBrandId(100L)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].BrandId, Is.EqualTo(100));
	}

	[Test]
	public void Query_WithEventStatus_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithListingStatus(ListingStatus.Draft)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.All(r => r.ListingStatus == ListingStatus.Draft), Is.True);
	}

	[Test]
	public void Query_WithIsLive_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithIsPublished(true)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].IsPublished, Is.True);
	}

	[Test]
	public void Query_WithHasIsBoostedOdds_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithHasDiscount(true)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].HasDiscount, Is.True);
	}

	[Test]
	public void Query_WithEventDateRange_BothBounds_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		var from = new DateTime(2025, 1, 1);
		var to = new DateTime(2025, 1, 2);

		// Act
		var results = _cache.Query()
			.WithReleaseDate(q => q.Gte(from).Lte(to))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // Events on Jan 1 and within bounds
	}

	[Test]
	public void Query_WithEventDateRange_OnlyFrom_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		var from = new DateTime(2025, 1, 2);

		// Act
		var results = _cache.Query()
			.WithReleaseDate(q => q.Gte(from))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // Events on Jan 2 and later
	}

	[Test]
	public void Query_WithEventDateRange_OnlyTo_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		var to = new DateTime(2025, 1, 2, 23, 59, 59);

		// Act
		var results = _cache.Query()
			.WithReleaseDate(q => q.Lte(to))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(3)); // Events before or on Jan 2
	}

	[Test]
	public void Query_WithTopEventOrderRange_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithFeaturedOrder(q => q.Gte(0).Lte(50))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(3)); // Orders 5, 10, 50
	}

	[Test]
	public void Query_WithActiveVariantCountRange_ShouldReturnMatchingDocuments() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithActiveVariantCount(q => q.Gt(0).Lt(50))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // 30 and 40
	}

	[Test]
	public void Query_MultipleIndexes_ShouldIntersectResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.WithDepartmentId(1L)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // Web + Electronics
		Assert.That(results.All(r => r.ChannelId == SalesChannel.Web && r.DepartmentId == 1), Is.True);
	}

	[Test]
	public void Query_IndexAndRangeQuery_ShouldIntersectResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		var from = new DateTime(2025, 1, 1);
		var to = new DateTime(2025, 1, 2, 23, 59, 59);

		// Act
		var results = _cache.Query()
			.WithDepartmentId(1L)
			.WithReleaseDate(q => q.Gte(from).Lte(to))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // Electronics events in date range
	}

	[Test]
	public void Query_WithWhere_ShouldFilterResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.Where(doc => doc.ActiveVariantCount > 30)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2)); // 50 and 40 (30 is excluded)
	}

	[Test]
	public void Query_MultipleWhereFilters_ShouldCombineWithAnd() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.Where(doc => doc.IsFeatured)
			.Where(doc => doc.DepartmentId == 1)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1)); // Only first event
	}

	[Test]
	public void Query_IndexAndWhere_ShouldCombine() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.Where(doc => doc.IsFeatured)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1)); // Web + TopEvent
	}

	// [Test]
	// public void Query_SortByDateAsc_ShouldSortCorrectly() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.Execute();
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(4));
	// 	Assert.That(result[0].ReleaseDate, Is.LessThan(result[1].ReleaseDate));
	// }
	//
	// [Test]
	// public void Query_SortByDateDesc_ShouldSortCorrectly() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateDesc()
	// 		.Execute();
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(4));
	// 	Assert.That(result[0].ReleaseDate, Is.GreaterThan(result[1].ReleaseDate));
	// }

	// [Test]
	// public void Query_SortWithCustomComparer_ShouldSortCorrectly() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	var comparer = Comparer<ProductListing>.Create((x, y) =>
	// 		x.ActiveVariantCount.CompareTo(y.ActiveVariantCount));
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.Sort(comparer)
	// 		.Execute();
	//
	// 	// Assert
	// 	Assert.That(result[0].ActiveVariantCount, Is.EqualTo(0));
	// 	Assert.That(result[^1].ActiveVariantCount, Is.EqualTo(50));
	// }

	// [Test]
	// public void Query_ExecuteWithSkip_ShouldSkipResults() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.Execute(2);
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(2));
	// 	Assert.That(result.TotalCount, Is.EqualTo(4));
	// }
	//
	// [Test]
	// public void Query_ExecuteWithTake_ShouldLimitResults() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.Execute(0, 2);
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(2));
	// 	Assert.That(result.TotalCount, Is.EqualTo(4));
	// }

	// [Test]
	// public void Query_ExecuteWithSkipAndTake_ShouldPaginate() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.Execute(1, 2);
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(2));
	// 	Assert.That(result.TotalCount, Is.EqualTo(4));
	// }

	[Test]
	public void Query_ExecuteWithoutSorting_WithPagination_ShouldWork() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var result = _cache.Query()
			.Execute(1, 2);

		// Assert
		Assert.That(result, Has.Count.EqualTo(2));
		Assert.That(result.TotalCount, Is.EqualTo(4));
	}

	[Test]
	public void Query_EmptyCache_ShouldReturnEmpty() {
		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void Query_NoMatchingResults_ShouldReturnEmpty() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Store) // No Store channel in test data
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void Query_ChainedIndexes_NoIntersection_ShouldReturnEmpty() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Mobile) // Only event 3
			.WithDepartmentId(1L) // Only events 1, 2
			.Execute();

		// Assert
		Assert.That(results, Is.Empty); // No intersection
	}

	[Test]
	public void AddOrUpdate_AfterRemove_ShouldMaintainIndexes() {
		// Arrange
		var doc = _testData[0];
		_cache.AddOrUpdate(doc);
		_cache.Remove(doc.CompositeId);

		// Act
		_cache.AddOrUpdate(doc);
		var results = _cache.Query()
			.WithId(doc.Id)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
	}

	[Test]
	public void Update_ChangingIndexedValue_ShouldUpdateIndexes() {
		// Arrange
		var doc = _testData[0];
		_cache.AddOrUpdate(doc);

		// Act - Change DepartmentId
		var updated = new ProductListing {
			CompositeId = doc.CompositeId,
			Id = doc.Id,
			ChannelId = doc.ChannelId,
			DepartmentId = 999, // Changed
			CategoryId = doc.CategoryId,
			BrandId = doc.BrandId,
			ReleaseDate = doc.ReleaseDate,
			ListingStatus = doc.ListingStatus,
			StockStatus = doc.StockStatus,
			IsFeatured = doc.IsFeatured,
			FeaturedOrder = doc.FeaturedOrder,
			ActiveVariantCount = doc.ActiveVariantCount,
			ListingTypeId = doc.ListingTypeId,
			IsPublished = doc.IsPublished,
			HasDiscount = doc.HasDiscount,
			ProductName = doc.ProductName,
			DepartmentName = doc.DepartmentName,
			CategoryName = doc.CategoryName,
			BrandName = doc.BrandName,
			CategoryDisabled = false,
			BrandDisabled = false,
			DepartmentDisabled = false
		};
		_cache.AddOrUpdate(updated);

		// Assert - Old DepartmentId should have no results
		var oldResults = _cache.Query()
			.WithDepartmentId(1L)
			.Execute();
		Assert.That(oldResults, Is.Empty);

		// Assert - New DepartmentId should find it
		var newResults = _cache.Query()
			.WithDepartmentId(999L)
			.Execute();
		Assert.That(newResults, Has.Count.EqualTo(1));
	}

	[Test]
	public void ExecuteCloned_ShouldReturnClonedResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithDepartmentId(1L)
			.ExecuteCloned();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));

		// Verify they are clones (different references)
		foreach (var result in results) {
			var cached = _cache.TryGet(result.CompositeId, out var cachedDoc);
			Assert.That(cached, Is.True);
			Assert.That(result, Is.Not.SameAs(cachedDoc));
			Assert.That(result.Id, Is.EqualTo(cachedDoc!.Id));
		}
	}

	[Test]
	public void ExecuteCloned_ModifyingResults_ShouldNotAffectCache() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithId(1L)
			.ExecuteCloned();

		var cloned = results[0];
		cloned.ProductName = "Modified Name";

		// Assert - Cache should have original value
		var cached = _cache.TryGet(cloned.CompositeId, out var cachedDoc);
		Assert.That(cached, Is.True);
		Assert.That(cachedDoc!.ProductName, Is.Not.EqualTo("Modified Name"));
		Assert.That(cachedDoc.ProductName, Is.EqualTo(_testData[0].ProductName));
	}

	[Test]
	public void ExecuteCloned_WithPagination_ShouldReturnClonedResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var result = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.ExecuteCloned(1, 2);

		// Assert
		Assert.That(result, Has.Count.EqualTo(2));
		Assert.That(result.TotalCount, Is.EqualTo(3)); // 3 Web events total

		// Verify they are clones
		foreach (var item in result) {
			var cached = _cache.TryGet(item.CompositeId, out var cachedDoc);
			Assert.That(cached, Is.True);
			Assert.That(item, Is.Not.SameAs(cachedDoc));
		}
	}

	// [Test]
	// public void ExecuteCloned_WithSorting_ShouldReturnSortedClonedResults() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.ExecuteCloned();
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(4));
	// 	Assert.That(result[0].ReleaseDate, Is.LessThanOrEqualTo(result[1].ReleaseDate));
	//
	// 	// Verify they are clones
	// 	foreach (var item in result) {
	// 		var cached = _cache.TryGet(item.CompositeId, out var cachedDoc);
	// 		Assert.That(cached, Is.True);
	// 		Assert.That(item, Is.Not.SameAs(cachedDoc));
	// 	}
	// }

	// [Test]
	// public void ExecuteCloned_WithSortingAndPagination_ShouldReturnSortedClonedPage() {
	// 	// Arrange
	// 	foreach (var doc in _testData)
	// 		_cache.AddOrUpdate(doc);
	//
	// 	// Act
	// 	var result = _cache.Query()
	// 		.SortByDateAsc()
	// 		.ExecuteCloned(1, 2);
	//
	// 	// Assert
	// 	Assert.That(result, Has.Count.EqualTo(2));
	// 	Assert.That(result.TotalCount, Is.EqualTo(4));
	//
	// 	// Verify sorting
	// 	Assert.That(result[0].ReleaseDate, Is.LessThanOrEqualTo(result[1].ReleaseDate));
	//
	// 	// Verify they are clones
	// 	foreach (var item in result) {
	// 		var cached = _cache.TryGet(item.CompositeId, out var cachedDoc);
	// 		Assert.That(cached, Is.True);
	// 		Assert.That(item, Is.Not.SameAs(cachedDoc));
	// 	}
	// }

	[Test]
	public void ExecuteCloned_EmptyResults_ShouldReturnEmpty() {
		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Store)
			.ExecuteCloned();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void ExecuteCloned_WithSkipBeyondTotal_ShouldReturnEmptyWithCorrectTotal() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var result = _cache.Query()
			.ExecuteCloned(10, 5);

		// Assert
		Assert.That(result, Is.Empty);
		Assert.That(result.TotalCount, Is.EqualTo(4));
	}

	[Test]
	public void ExecuteCloned_ComplexQuery_ShouldReturnClonedResults() {
		// Arrange
		foreach (var doc in _testData)
			_cache.AddOrUpdate(doc);

		// Act
		var results = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.WithListingStatus(ListingStatus.Draft)
			.Where(doc => doc.IsFeatured)
			.ExecuteCloned();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));

		var cloned = results[0];
		var cached = _cache.TryGet(cloned.CompositeId, out var cachedDoc);
		Assert.That(cached, Is.True);
		Assert.That(cloned, Is.Not.SameAs(cachedDoc));
		Assert.That(cloned.ProductName, Is.EqualTo(cachedDoc!.ProductName));
	}
}
#endif
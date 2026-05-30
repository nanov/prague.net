namespace Prague.Generated.Tests.Query;


#if false

/// <summary>
///   Test suite for OR query functionality in CacheQueryBuilder
/// </summary>
[TestFixture]
public class OrQueryTests {
	[SetUp]
	public void SetUp() {
		_cache = new ProductListingCache();
		_testData = CreateTestData();
		foreach (var doc in _testData) _cache.AddOrUpdate(doc);
	}

	private ProductListingCache _cache;
	private List<ProductListing> _testData;

	private List<ProductListing> CreateTestData() {
		var baseDate = new DateTime(2025, 1, 1, 12, 0, 0);

		return new List<ProductListing> {
			// Department 1 (Electronics), Brand 100, Web
			new() {
				CompositeId = "1_Online",
				Id = 1,
				ChannelId = SalesChannel.Web,
				DepartmentId = 1,
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
				ProductName = "Desk Lamp 1",
				DepartmentName = "Electronics",
				CategoryName = "England",
				BrandName = "Premier Brand",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			// Department 1 (Electronics), Brand 101, Web
			new() {
				CompositeId = "2_Online",
				Id = 2,
				ChannelId = SalesChannel.Web,
				DepartmentId = 1,
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
				ProductName = "Desk Lamp 2",
				DepartmentName = "Electronics",
				CategoryName = "England",
				BrandName = "Championship",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			// Department 2 (Audio), Brand 200, Mobile
			new() {
				CompositeId = "3_Mobile",
				Id = 3,
				ChannelId = SalesChannel.Mobile,
				DepartmentId = 2,
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
				ProductName = "Audio Game 1",
				DepartmentName = "Audio",
				CategoryName = "USA",
				BrandName = "NBA",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			// Department 2 (Audio), Brand 200, Web
			new() {
				CompositeId = "4_Online",
				Id = 4,
				ChannelId = SalesChannel.Web,
				DepartmentId = 2,
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
				ProductName = "Audio Game 2",
				DepartmentName = "Audio",
				CategoryName = "USA",
				BrandName = "NBA",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			// Department 3 (Office), Brand 300, Web
			new() {
				CompositeId = "5_Online",
				Id = 5,
				ChannelId = SalesChannel.Web,
				DepartmentId = 3,
				CategoryId = 30,
				BrandId = 300,
				ReleaseDate = baseDate.AddDays(3),
				ListingStatus = ListingStatus.Draft,
				StockStatus = StockStatus.InStock,
				IsFeatured = false,
				FeaturedOrder = 20,
				ActiveVariantCount = 25,
				ListingTypeId = 3,
				IsPublished = false,
				HasDiscount = true,
				ProductName = "Office Chair 1",
				DepartmentName = "Office",
				CategoryName = "France",
				BrandName = "Roland Garros",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			},
			// Department 3 (Office), Brand 301, Mobile
			new() {
				CompositeId = "6_Mobile",
				Id = 6,
				ChannelId = SalesChannel.Mobile,
				DepartmentId = 3,
				CategoryId = 30,
				BrandId = 301,
				ReleaseDate = baseDate.AddDays(4),
				ListingStatus = ListingStatus.Active,
				StockStatus = StockStatus.Reserved,
				IsFeatured = true,
				FeaturedOrder = 3,
				ActiveVariantCount = 35,
				ListingTypeId = 3,
				IsPublished = true,
				HasDiscount = false,
				ProductName = "Office Chair 2",
				DepartmentName = "Office",
				CategoryName = "France",
				BrandName = "Wimbledon",
				CategoryDisabled = false,
				BrandDisabled = false,
				DepartmentDisabled = false
			}
		};
	}

	[Test]
	public void Or_TwoBranches_ReturnsUnionOfBothIndexes() {
		// Arrange & Act - Find events with DepartmentId=1 OR DepartmentId=2
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.Execute();

		// Assert - Should return 4 events (2 football + 2 audio)
		Assert.That(result.Count, Is.EqualTo(4));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
	}

	[Test]
	public void Or_ThreeBranches_ReturnsUnionOfAllIndexes() {
		// Arrange & Act - Find events with DepartmentId=1 OR DepartmentId=2 OR DepartmentId=3
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 3L))
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void Or_SingleBranch_ReturnsSameAsDirectIndex() {
		// Arrange & Act
		var orResult = _cache.Cache.Query()
			.Or(qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L))
			.Execute();

		var directResult = _cache.Cache.Query()
			.UseIndex(_cache.DepartmentIdIndex, 1L)
			.Execute();

		// Assert
		Assert.That(orResult.Count, Is.EqualTo(directResult.Count));
		Assert.That(orResult.Count, Is.EqualTo(2));
	}

	[Test]
	public void Or_EmptyBranches_ReturnsAllItems() {
		// Arrange & Act - Empty branches array
		var result = _cache.Cache.Query()
			.Or(Span<Func<CoreCacheQueryBuilder<string, ProductListing>,
				CoreCacheQueryBuilder<string, ProductListing>>>.Empty)
			.Execute();

		// Assert - Should return all items (no filter applied)
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void Or_AfterUseIndex_IntersectsWithExistingCandidates() {
		// Arrange & Act
		// First filter: SalesChannel = Web (items 1, 2, 4, 5)
		// Then OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Result should be intersection: (1, 2, 4, 5) ∩ (1, 2, 3, 4) = (1, 2, 4)
		var result = _cache.Cache.Query()
			.UseIndex(_cache.ChannelIdIndex, SalesChannel.Web)
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		foreach (var item in result) {
			Assert.That(item.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void Or_BeforeUseIndex_AndIsAppliedAfter() {
		// Arrange & Act
		// First OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Then filter: SalesChannel = Web (items 1, 2, 4, 5)
		// Result should be: (1, 2, 3, 4) ∩ (1, 2, 4, 5) = (1, 2, 4)
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.UseIndex(_cache.ChannelIdIndex, SalesChannel.Web)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		foreach (var item in result) {
			Assert.That(item.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void Or_MultipleOrClauses_ChainsCorrectly() {
		// Arrange & Act
		// OR1: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// OR2: BrandId=100 OR BrandId=200 (items 1, 3, 4)
		// Result: (1, 2, 3, 4) ∩ (1, 3, 4) = (1, 3, 4)
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.Or(
				qb => qb.UseIndex(_cache.LeagueIdIndex, 100L),
				qb => qb.UseIndex(_cache.LeagueIdIndex, 200L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 4L }));
	}

	[Test]
	public void Or_DifferentIndexTypes_WorksCorrectly() {
		// Arrange & Act - Mix KeyValueList indexes in OR
		// DepartmentId=1 (items 1, 2) OR BrandId=200 (items 3, 4)
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.LeagueIdIndex, 200L))
			.Execute();

		// Assert - Should return items 1, 2, 3, 4
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 3L, 4L }));
	}

	[Test]
	public void Or_WithMultipleIndexesPerBranch_WorksCorrectly() {
		// Arrange & Act
		// Branch 1: DepartmentId=1 AND BrandId=100 (item 1)
		// Branch 2: DepartmentId=2 AND BrandId=200 (items 3, 4)
		// Result: Union = (1, 3, 4)
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L).UseIndex(_cache.LeagueIdIndex, 100L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L).UseIndex(_cache.LeagueIdIndex, 200L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 4L }));
	}

	[Test]
	public void Or_WithWhereFilter_FilterAppliesAfterOr() {
		// Arrange & Act
		// OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Where: IsFeatured = true (items 1, 3)
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.Where(x => x.IsFeatured)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		foreach (var item in result) {
			Assert.That(item.IsFeatured, Is.True);
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void Or_WithNoMatchingResults_ReturnsEmpty() {
		// Arrange & Act - Non-existent sport IDs
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 999L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 998L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void Or_OneMatchingOneMissing_ReturnsMatchingOnly() {
		// Arrange & Act
		// DepartmentId=1 exists (items 1, 2), DepartmentId=999 doesn't exist
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 999L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.EqualTo(1L));
	}

	[Test]
	public void Or_OverlappingResults_NoDuplicates() {
		// Arrange & Act
		// Both branches return some of the same items
		// DepartmentId=1 (items 1, 2) OR ChannelId=Web includes item 1, 2
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.ChannelIdIndex, SalesChannel.Web))
			.Execute();

		// Assert - items 1, 2, 4, 5 (no duplicates)
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 4L, 5L }));
	}

	[Test]
	public void Or_SameValueInBothBranches_NoDuplicates() {
		// Arrange & Act - Same index value in both branches
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L))
			.Execute();

		// Assert - Should still be 2 items, no duplicates
		Assert.That(result.Count, Is.EqualTo(2));
	}

	[Test]
	public void Or_WithPagination_ReturnsCorrectPage() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 3L))
			.Execute(2, 2);

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result.TotalCount, Is.EqualTo(6));
	}

	[Test]
	public void Or_ExecutePooled_WorksCorrectly() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.ExecutePooled();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
		result.Dispose();
	}

	[Test]
	public void Or_ExecuteCloned_WorksCorrectly() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.ExecuteCloned();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
	}

	[Test]
	public void Or_FourBranches_ReturnsUnionOfAll() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.LeagueIdIndex, 100L),
				qb => qb.UseIndex(_cache.LeagueIdIndex, 101L),
				qb => qb.UseIndex(_cache.LeagueIdIndex, 200L),
				qb => qb.UseIndex(_cache.LeagueIdIndex, 300L))
			.Execute();

		// Assert - items 1, 2, 3, 4, 5 (all except item 6 which has league 301)
		Assert.That(result.Count, Is.EqualTo(5));
		foreach (var item in result)
			Assert.That(item.BrandId, Is.AnyOf(100L, 101L, 200L, 300L));
	}

	[Test]
	public void Or_Count_ReturnsCorrectCount() {
		// Arrange & Act
		var count = _cache.Cache.Query()
			.Or(
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 1L),
				qb => qb.UseIndex(_cache.DepartmentIdIndex, 2L))
			.Count();

		// Assert
		Assert.That(count, Is.EqualTo(4));
	}

	[Test]
	public void OrWithArgs_TwoBranches_ReturnsUnionOfBothIndexes() {
		// Arrange & Act - Using args to avoid closures
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 1L),
				static (qb, args) => qb.UseIndex(args.Index, 2L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Execute();

		// Assert - Should return 4 events (2 football + 2 audio)
		Assert.That(result.Count, Is.EqualTo(4));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
	}

	[Test]
	public void OrWithArgs_ThreeBranches_ReturnsUnionOfAllIndexes() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 1L),
				static (qb, args) => qb.UseIndex(args.Index, 2L),
				static (qb, args) => qb.UseIndex(args.Index, 3L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void OrWithArgs_FourBranches_ReturnsUnionOfAll() {
		// Arrange & Act
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 100L),
				static (qb, args) => qb.UseIndex(args.Index, 101L),
				static (qb, args) => qb.UseIndex(args.Index, 200L),
				static (qb, args) => qb.UseIndex(args.Index, 300L),
				(Index: _cache.LeagueIdIndex, Dummy: 0))
			.Execute();

		// Assert - items 1, 2, 3, 4, 5 (all except item 6 which has league 301)
		Assert.That(result.Count, Is.EqualTo(5));
		foreach (var item in result)
			Assert.That(item.BrandId, Is.AnyOf(100L, 101L, 200L, 300L));
	}

	[Test]
	public void OrWithArgs_WithDifferentIndexTypes_WorksCorrectly() {
		// Arrange & Act - Mix KeyValueList indexes in OR using args tuple
		// DepartmentId=1 (items 1, 2) OR BrandId=200 (items 3, 4)
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.DeptIndex, 1L),
				static (qb, args) => qb.UseIndex(args.LeagueIndex, 200L),
				(DeptIndex: _cache.DepartmentIdIndex, LeagueIndex: _cache.LeagueIdIndex))
			.Execute();

		// Assert - Should return items 1, 2, 3, 4
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 3L, 4L }));
	}

	[Test]
	public void OrWithArgs_WithMultipleIndexesPerBranch_WorksCorrectly() {
		// Arrange & Act
		// Branch 1: DepartmentId=1 AND BrandId=100 (item 1)
		// Branch 2: DepartmentId=2 AND BrandId=200 (items 3, 4)
		// Result: Union = (1, 3, 4)
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.DeptIndex, 1L).UseIndex(args.LeagueIndex, 100L),
				static (qb, args) => qb.UseIndex(args.DeptIndex, 2L).UseIndex(args.LeagueIndex, 200L),
				(DeptIndex: _cache.DepartmentIdIndex, LeagueIndex: _cache.LeagueIdIndex))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 4L }));
	}

	[Test]
	public void OrWithArgs_AfterUseIndex_IntersectsWithExistingCandidates() {
		// Arrange & Act
		// First filter: SalesChannel = Web (items 1, 2, 4, 5)
		// Then OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Result should be intersection: (1, 2, 4, 5) ∩ (1, 2, 3, 4) = (1, 2, 4)
		var result = _cache.Cache.Query()
			.UseIndex(_cache.ChannelIdIndex, SalesChannel.Web)
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 1L),
				static (qb, args) => qb.UseIndex(args.Index, 2L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		foreach (var item in result) {
			Assert.That(item.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void OrWithArgs_WithValueTypeArgs_WorksCorrectly() {
		// Arrange & Act - Using value type args (long values)
		var args = (Index: _cache.DepartmentIdIndex, Value1: 1L, Value2: 2L);
		var result = _cache.Cache.Query()
			.Or(
				static (qb, a) => qb.UseIndex(a.Index, a.Value1),
				static (qb, a) => qb.UseIndex(a.Index, a.Value2),
				args)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
	}

	[Test]
	public void OrWithArgs_WithWhereFilter_FilterAppliesAfterOr() {
		// Arrange & Act
		// OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Where: IsFeatured = true (items 1, 3)
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 1L),
				static (qb, args) => qb.UseIndex(args.Index, 2L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Where(x => x.IsFeatured)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		foreach (var item in result) {
			Assert.That(item.IsFeatured, Is.True);
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void OrWithArgs_Count_ReturnsCorrectCount() {
		// Arrange & Act
		var count = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 1L),
				static (qb, args) => qb.UseIndex(args.Index, 2L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Count();

		// Assert
		Assert.That(count, Is.EqualTo(4));
	}

	[Test]
	public void OrWithArgs_ParamsOverload_WorksCorrectly() {
		// Arrange & Act - Using the params ReadOnlySpan overload with args
		var args = (Index: _cache.DepartmentIdIndex, Dummy: 0);
		var result = _cache.Cache.Query()
			.Or(args,
				static (qb, a) => qb.UseIndex(a.Index, 1L),
				static (qb, a) => qb.UseIndex(a.Index, 2L),
				static (qb, a) => qb.UseIndex(a.Index, 3L))
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void OrWithArgs_NoMatchingResults_ReturnsEmpty() {
		// Arrange & Act - Non-existent sport IDs
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.Index, 999L),
				static (qb, args) => qb.UseIndex(args.Index, 998L),
				(Index: _cache.DepartmentIdIndex, Dummy: 0))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void OrWithArgs_OverlappingResults_NoDuplicates() {
		// Arrange & Act - Both branches return some of the same items
		var result = _cache.Cache.Query()
			.Or(
				static (qb, args) => qb.UseIndex(args.DeptIndex, 1L),
				static (qb, args) => qb.UseIndex(args.ChannelIndex, SalesChannel.Web),
				(DeptIndex: _cache.DepartmentIdIndex, ChannelIndex: _cache.ChannelIdIndex))
			.Execute();

		// Assert - items 1, 2, 4, 5 (no duplicates)
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 4L, 5L }));
	}

	[Test]
	public void GeneratedOr_TwoBranches_ReturnsUnionOfBothIndexes() {
		// Arrange & Act - Using generated query builder with Or method
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.Execute();

		// Assert - Should return 4 events (2 football + 2 audio)
		Assert.That(result.Count, Is.EqualTo(4));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
	}

	[Test]
	public void GeneratedOr_ThreeBranches_ReturnsUnionOfAllIndexes() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L),
				qb => qb.WithDepartmentId(3L))
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void GeneratedOr_FourBranches_ReturnsUnionOfAll() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				qb => qb.WithBrandId(100L),
				qb => qb.WithBrandId(101L),
				qb => qb.WithBrandId(200L),
				qb => qb.WithBrandId(300L))
			.Execute();

		// Assert - items 1, 2, 3, 4, 5 (all except item 6 which has league 301)
		Assert.That(result.Count, Is.EqualTo(5));
		foreach (var item in result)
			Assert.That(item.BrandId, Is.AnyOf(100L, 101L, 200L, 300L));
	}

	[Test]
	public void GeneratedOr_WithDifferentIndexTypes_WorksCorrectly() {
		// Arrange & Act - Mix different indexes in OR
		// DepartmentId=1 (items 1, 2) OR BrandId=200 (items 3, 4)
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithBrandId(200L))
			.Execute();

		// Assert - Should return items 1, 2, 3, 4
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 3L, 4L }));
	}

	[Test]
	public void GeneratedOr_WithMultipleIndexesPerBranch_WorksCorrectly() {
		// Arrange & Act
		// Branch 1: DepartmentId=1 AND BrandId=100 (item 1)
		// Branch 2: DepartmentId=2 AND BrandId=200 (items 3, 4)
		// Result: Union = (1, 3, 4)
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L).WithBrandId(100L),
				qb => qb.WithDepartmentId(2L).WithBrandId(200L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 4L }));
	}

	[Test]
	public void GeneratedOr_AfterWithIndex_IntersectsWithExistingCandidates() {
		// Arrange & Act
		// First filter: SalesChannel = Web (items 1, 2, 4, 5)
		// Then OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Result should be intersection: (1, 2, 4, 5) ∩ (1, 2, 3, 4) = (1, 2, 4)
		var result = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		foreach (var item in result) {
			Assert.That(item.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void GeneratedOr_WithWhereFilter_FilterAppliesAfterOr() {
		// Arrange & Act
		// OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Where: IsFeatured = true (items 1, 3)
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.Where(x => x.IsFeatured)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		foreach (var item in result) {
			Assert.That(item.IsFeatured, Is.True);
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void GeneratedOr_Count_ReturnsCorrectCount() {
		// Arrange & Act
		var count = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.Count();

		// Assert
		Assert.That(count, Is.EqualTo(4));
	}

	[Test]
	public void GeneratedOr_NoMatchingResults_ReturnsEmpty() {
		// Arrange & Act - Non-existent sport IDs
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(999L),
				qb => qb.WithDepartmentId(998L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedOr_OverlappingResults_NoDuplicates() {
		// Arrange & Act - Both branches return some of the same items
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithChannelId(SalesChannel.Web))
			.Execute();

		// Assert - items 1, 2, 4, 5 (no duplicates)
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 4L, 5L }));
	}

	[Test]
	public void GeneratedOr_WithPagination_ReturnsCorrectPage() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L),
				qb => qb.WithDepartmentId(3L))
			.Execute(2, 2);

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result.TotalCount, Is.EqualTo(6));
	}

	[Test]
	public void GeneratedOr_ExecutePooled_WorksCorrectly() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.ExecutePooled();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
		result.Dispose();
	}

	[Test]
	public void GeneratedOr_ExecuteCloned_WorksCorrectly() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				qb => qb.WithDepartmentId(1L),
				qb => qb.WithDepartmentId(2L))
			.ExecuteCloned();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
	}

	[Test]
	public void GeneratedOrWithArgs_TwoBranches_ReturnsUnionOfBothIndexes() {
		// Arrange & Act - Using args to avoid closures with generated builder
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Value1),
				static (qb, args) => qb.WithDepartmentId(args.Value2),
				(Value1: 1L, Value2: 2L))
			.Execute();

		// Assert - Should return 4 events (2 football + 2 audio)
		Assert.That(result.Count, Is.EqualTo(4));
		foreach (var item in result)
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
	}

	[Test]
	public void GeneratedOrWithArgs_ThreeBranches_ReturnsUnionOfAllIndexes() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Value1),
				static (qb, args) => qb.WithDepartmentId(args.Value2),
				static (qb, args) => qb.WithDepartmentId(args.Value3),
				(Value1: 1L, Value2: 2L, Value3: 3L))
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void GeneratedOrWithArgs_FourBranches_ReturnsUnionOfAll() {
		// Arrange & Act
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithBrandId(args.V1),
				static (qb, args) => qb.WithBrandId(args.V2),
				static (qb, args) => qb.WithBrandId(args.V3),
				static (qb, args) => qb.WithBrandId(args.V4),
				(V1: 100L, V2: 101L, V3: 200L, V4: 300L))
			.Execute();

		// Assert - items 1, 2, 3, 4, 5 (all except item 6 which has league 301)
		Assert.That(result.Count, Is.EqualTo(5));
		foreach (var item in result)
			Assert.That(item.BrandId, Is.AnyOf(100L, 101L, 200L, 300L));
	}

	[Test]
	public void GeneratedOrWithArgs_WithDifferentIndexTypes_WorksCorrectly() {
		// Arrange & Act - Mix different indexes in OR using args
		// DepartmentId=1 (items 1, 2) OR BrandId=200 (items 3, 4)
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.DepartmentId),
				static (qb, args) => qb.WithBrandId(args.BrandId),
				(DepartmentId: 1L, BrandId: 200L))
			.Execute();

		// Assert - Should return items 1, 2, 3, 4
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 3L, 4L }));
	}

	[Test]
	public void GeneratedOrWithArgs_WithMultipleIndexesPerBranch_WorksCorrectly() {
		// Arrange & Act
		// Branch 1: DepartmentId=1 AND BrandId=100 (item 1)
		// Branch 2: DepartmentId=2 AND BrandId=200 (items 3, 4)
		// Result: Union = (1, 3, 4)
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Dept1).WithBrandId(args.League1),
				static (qb, args) => qb.WithDepartmentId(args.Dept2).WithBrandId(args.League2),
				(Dept1: 1L, League1: 100L, Dept2: 2L, League2: 200L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 3L, 4L }));
	}

	[Test]
	public void GeneratedOrWithArgs_AfterWithIndex_IntersectsWithExistingCandidates() {
		// Arrange & Act
		// First filter: SalesChannel = Web (items 1, 2, 4, 5)
		// Then OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Result should be intersection: (1, 2, 4, 5) ∩ (1, 2, 3, 4) = (1, 2, 4)
		var result = _cache.Query()
			.WithChannelId(SalesChannel.Web)
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Dept1),
				static (qb, args) => qb.WithDepartmentId(args.Dept2),
				(Dept1: 1L, Dept2: 2L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3));
		foreach (var item in result) {
			Assert.That(item.ChannelId, Is.EqualTo(SalesChannel.Web));
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void GeneratedOrWithArgs_WithWhereFilter_FilterAppliesAfterOr() {
		// Arrange & Act
		// OR: DepartmentId=1 OR DepartmentId=2 (items 1, 2, 3, 4)
		// Where: IsFeatured = true (items 1, 3)
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Dept1),
				static (qb, args) => qb.WithDepartmentId(args.Dept2),
				(Dept1: 1L, Dept2: 2L))
			.Where(x => x.IsFeatured)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2));
		foreach (var item in result) {
			Assert.That(item.IsFeatured, Is.True);
			Assert.That(item.DepartmentId, Is.AnyOf(1L, 2L));
		}
	}

	[Test]
	public void GeneratedOrWithArgs_Count_ReturnsCorrectCount() {
		// Arrange & Act
		var count = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Dept1),
				static (qb, args) => qb.WithDepartmentId(args.Dept2),
				(Dept1: 1L, Dept2: 2L))
			.Count();

		// Assert
		Assert.That(count, Is.EqualTo(4));
	}

	[Test]
	public void GeneratedOrWithArgs_ThreeBranches_WorksCorrectly() {
		// Arrange & Act - Using the 3-branch overload with args
		var args = (Dept1: 1L, Dept2: 2L, Dept3: 3L);
		var result = _cache.Query()
			.Or(
				static (qb, a) => qb.WithDepartmentId(a.Dept1),
				static (qb, a) => qb.WithDepartmentId(a.Dept2),
				static (qb, a) => qb.WithDepartmentId(a.Dept3),
				args)
			.Execute();

		// Assert - Should return all 6 events
		Assert.That(result.Count, Is.EqualTo(6));
	}

	[Test]
	public void GeneratedOrWithArgs_NoMatchingResults_ReturnsEmpty() {
		// Arrange & Act - Non-existent sport IDs
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.Dept1),
				static (qb, args) => qb.WithDepartmentId(args.Dept2),
				(Dept1: 999L, Dept2: 998L))
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedOrWithArgs_OverlappingResults_NoDuplicates() {
		// Arrange & Act - Both branches return some of the same items
		var result = _cache.Query()
			.Or(
				static (qb, args) => qb.WithDepartmentId(args.DepartmentId),
				static (qb, args) => qb.WithChannelId(args.ChannelId),
				(DepartmentId: 1L, ChannelId: SalesChannel.Web))
			.Execute();

		// Assert - items 1, 2, 4, 5 (no duplicates)
		Assert.That(result.Count, Is.EqualTo(4));
		var ids = result.Select(x => x.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 4L, 5L }));
	}
}
#endif
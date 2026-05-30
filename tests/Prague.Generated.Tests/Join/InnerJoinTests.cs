// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

/// <summary>
/// Tests for inner join functionality (InnerJoinMany and InnerJoinOne).
/// Inner joins filter out left items that don't have matching right items.
/// </summary>
[TestFixture]
public class InnerJoinTests {
	private DataCacheRegistry _registry = null!;
	private CatalogCategoryCache _categoryCache = null!;
	private CatalogBrandCache _brandCache = null!;
	private CatalogListingCache _listingCache = null!;
	private CatalogListingInfoCache _listingInfoCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CatalogCategoryCache>()
			.Register<CatalogBrandCache>()
			.Register<CatalogListingCache>()
			.Register<CatalogListingInfoCache>()
			.Build();

		_categoryCache = _registry.GetCache<CatalogCategoryCache>();
		_brandCache = _registry.GetCache<CatalogBrandCache>();
		_listingCache = _registry.GetCache<CatalogListingCache>();
		_listingInfoCache = _registry.GetCache<CatalogListingInfoCache>();
	}

	[TearDown]
	public void TearDown() {
		// Registry doesn't implement IDisposable
	}

	#region InnerJoinMany Tests

	/*
	[Test]
	public void InnerJoinMany_SingleItemWithMatch_ReturnsOne() {
		// Simple test with one category that has one brand
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		// Verify index works
		Assert.That(_brandCache.CatalogCategoryIdIndex.ContainsKey(1), Is.True);
		Assert.That(_brandCache.CatalogCategoryIdIndex.ContainsKey(2), Is.False);

		// Inner join should return the match
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
		Assert.That(results[0].Right.Count, Is.EqualTo(1));
	}

	[Test]
	public void InnerJoinMany_FiltersOutItemsWithNoMatches() {
		// Arrange: 3 categories, only 2 have brands
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 3, Name = "Westland", Code = "FR" }); // No brands

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });

		// Act: Inner join - should exclude Westland (no brands)
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Left.Id != 3), Is.True, "Westland should be excluded");
		Assert.That(results.All(r => r.Right.Count > 0), Is.True, "All results should have brands");
	}

	[Test]
	public void InnerJoinMany_WithFilter_FiltersBasedOnRightFilterToo() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });

		// Northland has brands in 2024 and 2023
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 1, Name = "Standard Line", Season = 2023 });
		// Eastland has only 2023 brands
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 3, CatalogCategoryId = 2, Name = "Initech Line", Season = 2023 });

		// Act: Inner join with filter for 2024 season - Eastland should be excluded
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex,
				q => q.Where(l => l.Season == 2024))
			.Execute();

		// Assert: Only Northland has 2024 brands
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
		Assert.That(results[0].Right.Count, Is.EqualTo(1));
	}

	[Test]
	public void InnerJoinMany_EmptyRightCache_ReturnsNoResults() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });
		// No brands added

		// Act
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinMany_AllItemsHaveMatches_ReturnsAll() {
		// Arrange: All categories have brands
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });

		// Act
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));
	}

	[Test]
	public void InnerJoinMany_WithPagination_PaginatesCorrectly() {
		// Arrange: 3 categories with brands
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "A-Category", Code = "A" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "B-Category", Code = "B" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 3, Name = "C-Category", Code = "C" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 4, Name = "D-Category", Code = "D" }); // No brands

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Brand A", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 2, Name = "Brand B", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 3, CatalogCategoryId = 3, Name = "Brand C", Season = 2024 });

		// Act: Skip 1, take 1
		var results = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute(skip: 1, take: 1);

		// Assert: Should get 1 result
		Assert.That(results.Count, Is.EqualTo(1));
	}
	*/

	#endregion

	#region InnerJoinOne Tests

	[Test]
	public void InnerJoinOne_FiltersOutItemsWithNoMatches() {
		// Arrange: 3 categories, only 2 have match info
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 3, Name = "Westland", Code = "FR" }); // No match info

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 1, CatalogCategoryId = 1, ListingId = 1, Warehouse = "Harbor Depot" });
		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 2, CatalogCategoryId = 2, ListingId = 2, Warehouse = "Summit Depot" });

		// Act: Inner join - should exclude Westland
		var builder = _categoryCache.Cache.Query()
			.InnerJoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex);
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Left.Id != 3), Is.True, "Westland should be excluded");
		Assert.That(results.All(r => r.Right != null), Is.True, "All results should have match info");
	}

	[Test]
	public void InnerJoinOne_WithFilter_FiltersBasedOnRightFilterToo() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 1, CatalogCategoryId = 1, Warehouse = "Harbor Depot", Attendance = 90000 });
		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 2, CatalogCategoryId = 2, Warehouse = "Summit Depot", Attendance = 50000 });

		// Act: Inner join with filter for attendance > 80000 - Eastland should be excluded
		var builder = _categoryCache.Cache.Query()
			.InnerJoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex,
				q => q.Where(m => m.Attendance > 80000));
		var results = builder.Execute();

		// Assert: Only Northland has high attendance
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
	}

	[Test]
	public void InnerJoinOne_EmptyRightCache_ReturnsNoResults() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		// No match info added

		// Act
		var builder = _categoryCache.Cache.Query()
			.InnerJoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex);
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinOne_AllItemsHaveMatches_ReturnsAll() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ES" });

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 1, CatalogCategoryId = 1, Warehouse = "Harbor Depot" });
		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 2, CatalogCategoryId = 2, Warehouse = "Summit Depot" });

		// Act
		var builder = _categoryCache.Cache.Query()
			.InnerJoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex);
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));
	}

	#endregion

	#region Comparison with Left Join Tests

	/*
	[Test]
	public void JoinMany_vs_InnerJoinMany_DifferentResults() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Westland", Code = "FR" }); // No brands

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		// Act: Left join
		var leftJoinResults = _categoryCache.Cache.Query()
			.JoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		// Act: Inner join
		var innerJoinResults = _categoryCache.Cache.Query()
			.InnerJoinMany(_brandCache.Cache, _brandCache.CatalogCategoryIdIndex)
			.Execute();

		// Assert: Left join includes Westland with empty brands
		Assert.That(leftJoinResults.Count, Is.EqualTo(2));
		Assert.That(leftJoinResults.Any(r => r.Left.Name == "Westland" && r.Right.Count == 0), Is.True);

		// Assert: Inner join excludes Westland
		Assert.That(innerJoinResults.Count, Is.EqualTo(1));
		Assert.That(innerJoinResults[0].Left.Name, Is.EqualTo("Northland"));
	}
	*/

	[Test]
	public void JoinOne_vs_InnerJoinOne_DifferentResults() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Westland", Code = "FR" }); // No match info

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 1, CatalogCategoryId = 1, Warehouse = "Harbor Depot" });

		// Act: Left join
		var leftBuilder = _categoryCache.Cache.Query()
			.JoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex);
		var leftJoinResults = leftBuilder.Execute();

		// Act: Inner join
		var innerBuilder = _categoryCache.Cache.Query()
			.InnerJoinOne(_listingInfoCache.Cache, _listingInfoCache.CatalogCategoryIdIndex);
		var innerJoinResults = innerBuilder.Execute();

		// Assert: Left join includes Westland with null Right
		Assert.That(leftJoinResults.Count, Is.EqualTo(2));
		Assert.That(leftJoinResults.Any(r => r.Left.Name == "Westland" && r.Right == null), Is.True);

		// Assert: Inner join excludes Westland
		Assert.That(innerJoinResults.Count, Is.EqualTo(1));
		Assert.That(innerJoinResults[0].Left.Name, Is.EqualTo("Northland"));
	}

	#endregion

	#region Generated InnerJoinWith{Entity} Tests

	/*
	[Test]
	public void InnerJoinWithCatalogBrand_Generated_Works() {
		// Arrange
		_categoryCache.AddJoinedCaches(_brandCache, _listingCache, _listingInfoCache, _registry.GetCache<CatalogOfferCache>());

		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Westland", Code = "FR" }); // No brands

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		// Act: Use generated InnerJoinWith method
		var results = _categoryCache.Query()
			.InnerJoinWithCatalogBrand()
			.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
	}

	[Test]
	public void InnerJoinWithCatalogListingInfo_Generated_Works() {
		// Arrange
		_categoryCache.AddJoinedCaches(_brandCache, _listingCache, _listingInfoCache, _registry.GetCache<CatalogOfferCache>());

		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "EN" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Westland", Code = "FR" }); // No match info

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo { Id = 1, CatalogCategoryId = 1, Warehouse = "Harbor Depot" });

		// Act: Use generated InnerJoinWith method
		var results = _categoryCache.Query()
			.InnerJoinWithCatalogListingInfo()
			.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
	}
	*/

	#endregion
}

#endif

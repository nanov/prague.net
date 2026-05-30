namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

#region Test Entities for Boolean HasValue/HasNotValue Indexes

[DataCache]
public partial class ProductWithBooleanFlags {
	[DataCacheKey] public int Id { get; set; }
	public required string Name { get; set; }

	[DataCacheHasValueIndex]
	public bool IsActive { get; set; }

	[DataCacheHasValueIndex]
	public bool IsFeatured { get; set; }

	[DataCacheHasNotValueIndex]
	public bool IsDiscontinued { get; set; }

	[DataCacheHasNotValueIndex]
	public bool IsOutOfStock { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required string Category { get; set; }
}

#endregion

/// <summary>
/// Tests for DataCacheHasValueIndex and DataCacheHasNotValueIndex with boolean properties.
/// For boolean types:
/// - HasValue indexes items where the property is true
/// - HasNotValue indexes items where the property is false
/// </summary>
[TestFixture]
public class HasValueIndexBooleanTests {
	private DataCacheRegistry _registry = null!;
	private ProductWithBooleanFlagsCache _cache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<ProductWithBooleanFlagsCache>()
			.Build();
		_cache = _registry.GetCache<ProductWithBooleanFlagsCache>();

		// Set up test data
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 1, Name = "Product A", IsActive = true, IsFeatured = true, IsDiscontinued = false, IsOutOfStock = false, Category = "Electronics"
		});
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 2, Name = "Product B", IsActive = false, IsFeatured = false, IsDiscontinued = true, IsOutOfStock = true, Category = "Books"
		});
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 3, Name = "Product C", IsActive = true, IsFeatured = false, IsDiscontinued = false, IsOutOfStock = true, Category = "Electronics"
		});
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 4, Name = "Product D", IsActive = false, IsFeatured = true, IsDiscontinued = true, IsOutOfStock = false, Category = "Toys"
		});
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 5, Name = "Product E", IsActive = true, IsFeatured = true, IsDiscontinued = false, IsOutOfStock = false, Category = "Books"
		});
	}

	#region Generated Index Fields Tests

	[Test]
	public void GeneratedCache_ShouldHaveIsActiveIndex() {
		Assert.That(_cache.HasIsActiveIndex, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHaveIsFeaturedIndex() {
		Assert.That(_cache.HasIsFeaturedIndex, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasNotIsDiscontinuedIndex() {
		Assert.That(_cache.HasNotIsDiscontinuedIndex, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasNotIsOutOfStockIndex() {
		Assert.That(_cache.HasNotIsOutOfStockIndex, Is.Not.Null);
	}

	#endregion

	#region HasValue (true) Index Population Tests

	[Test]
	public void HasIsActiveIndex_ShouldContainItemsWhereIsActiveIsTrue() {
		// Products 1, 3, 5 have IsActive = true
		var keys = _cache.HasIsActiveIndex.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(3));
		Assert.That(keys, Contains.Item(1));
		Assert.That(keys, Contains.Item(3));
		Assert.That(keys, Contains.Item(5));
	}

	[Test]
	public void HasIsFeaturedIndex_ShouldContainItemsWhereIsFeaturedIsTrue() {
		// Products 1, 4, 5 have IsFeatured = true
		var keys = _cache.HasIsFeaturedIndex.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(3));
		Assert.That(keys, Contains.Item(1));
		Assert.That(keys, Contains.Item(4));
		Assert.That(keys, Contains.Item(5));
	}

	#endregion

	#region HasNotValue (false) Index Population Tests

	[Test]
	public void HasNotIsDiscontinuedIndex_ShouldContainItemsWhereIsDiscontinuedIsFalse() {
		// Products 1, 3, 5 have IsDiscontinued = false
		var keys = _cache.HasNotIsDiscontinuedIndex.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(3));
		Assert.That(keys, Contains.Item(1));
		Assert.That(keys, Contains.Item(3));
		Assert.That(keys, Contains.Item(5));
	}

	[Test]
	public void HasNotIsOutOfStockIndex_ShouldContainItemsWhereIsOutOfStockIsFalse() {
		// Products 1, 4, 5 have IsOutOfStock = false
		var keys = _cache.HasNotIsOutOfStockIndex.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(3));
		Assert.That(keys, Contains.Item(1));
		Assert.That(keys, Contains.Item(4));
		Assert.That(keys, Contains.Item(5));
	}

	#endregion

	#region Query Method Tests - HasValue (true)

	[Test]
	public void WithIsActive_ReturnsActiveProducts() {
		// Act - IsActive = true
		var results = _cache.Query()
			.WithIsActive()
			.Execute();

		// Assert - Products 1, 3, 5 are active
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	[Test]
	public void WithIsFeatured_ReturnsFeaturedProducts() {
		// Act - IsFeatured = true
		var results = _cache.Query()
			.WithIsFeatured()
			.Execute();

		// Assert - Products 1, 4, 5 are featured
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 4, 5 }));
	}

	#endregion

	#region Query Method Tests - HasNotValue (false)

	[Test]
	public void WithoutIsDiscontinued_ReturnsNonDiscontinuedProducts() {
		// Act - IsDiscontinued = false
		var results = _cache.Query()
			.WithoutIsDiscontinued()
			.Execute();

		// Assert - Products 1, 3, 5 are not discontinued
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	[Test]
	public void WithoutIsOutOfStock_ReturnsInStockProducts() {
		// Act - IsOutOfStock = false
		var results = _cache.Query()
			.WithoutIsOutOfStock()
			.Execute();

		// Assert - Products 1, 4, 5 are in stock
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 4, 5 }));
	}

	#endregion

	#region Combined Boolean Index Tests

	[Test]
	public void CombinedIndexes_ActiveAndFeatured() {
		// Act - IsActive = true AND IsFeatured = true
		var results = _cache.Query()
			.WithIsActive()
			.WithIsFeatured()
			.Execute();

		// Assert - Products 1, 5 are both active AND featured
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 5 }));
	}

	[Test]
	public void CombinedIndexes_NotDiscontinuedAndInStock() {
		// Act - IsDiscontinued = false AND IsOutOfStock = false
		var results = _cache.Query()
			.WithoutIsDiscontinued()
			.WithoutIsOutOfStock()
			.Execute();

		// Assert - Products 1, 5 are not discontinued AND in stock
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 5 }));
	}

	[Test]
	public void CombinedIndexes_ActiveAndNotDiscontinued() {
		// Act - IsActive = true AND IsDiscontinued = false
		var results = _cache.Query()
			.WithIsActive()
			.WithoutIsDiscontinued()
			.Execute();

		// Assert - Products 1, 3, 5 are active AND not discontinued
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	[Test]
	public void CombinedIndexes_WithRegularIndex() {
		// Act - IsActive = true AND Category = "Electronics"
		var results = _cache.Query()
			.WithIsActive()
			.WithCategory("Electronics")
			.Execute();

		// Assert - Products 1, 3 are active electronics
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	#endregion

	#region Dynamic Update Tests

	[Test]
	public void Update_FromTrueToFalse_ShouldRemoveFromHasValueIndex() {
		// Initially Product 1 has IsActive = true
		var initialResults = _cache.Query()
			.WithIsActive()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 1), Is.True);

		// Update Product 1 to IsActive = false
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 1, Name = "Product A", IsActive = false, IsFeatured = true, IsDiscontinued = false, IsOutOfStock = false, Category = "Electronics"
		});

		// Now Product 1 should NOT be in the active index
		var updatedResults = _cache.Query()
			.WithIsActive()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 1), Is.False);
		Assert.That(updatedResults.Count, Is.EqualTo(2));
	}

	[Test]
	public void Update_FromFalseToTrue_ShouldAddToHasValueIndex() {
		// Initially Product 2 has IsActive = false
		var initialResults = _cache.Query()
			.WithIsActive()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 2), Is.False);

		// Update Product 2 to IsActive = true
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 2, Name = "Product B", IsActive = true, IsFeatured = false, IsDiscontinued = true, IsOutOfStock = true, Category = "Books"
		});

		// Now Product 2 should be in the active index
		var updatedResults = _cache.Query()
			.WithIsActive()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.True);
		Assert.That(updatedResults.Count, Is.EqualTo(4));
	}

	[Test]
	public void Update_FromFalseToTrue_ShouldRemoveFromHasNotValueIndex() {
		// Initially Product 2 has IsDiscontinued = true (so it's in the HasNotValueIndex for false items)
		// Actually, HasNotValue tracks false items, so Product 2 (true) should NOT be in it
		var initialResults = _cache.Query()
			.WithoutIsDiscontinued()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 2), Is.False);

		// Update Product 2 to IsDiscontinued = false
		_cache.AddOrUpdate(new ProductWithBooleanFlags {
			Id = 2, Name = "Product B", IsActive = false, IsFeatured = false, IsDiscontinued = false, IsOutOfStock = true, Category = "Books"
		});

		// Now Product 2 should be in the not-discontinued index (false values)
		var updatedResults = _cache.Query()
			.WithoutIsDiscontinued()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.True);
		Assert.That(updatedResults.Count, Is.EqualTo(4));
	}

	#endregion

	#region Remove Tests

	[Test]
	public void Remove_ExistingKeyInBooleanIndex_ShouldRemoveFromIndex() {
		// Product 1 is in the active index
		var initialKeys = _cache.HasIsActiveIndex.GetKeys().ToArray();
		Assert.That(initialKeys, Contains.Item(1));

		// Remove Product 1
		_cache.Remove(1);

		// Should no longer be in index
		var updatedKeys = _cache.HasIsActiveIndex.GetKeys().ToArray();
		Assert.That(updatedKeys, Does.Not.Contain(1));
		Assert.That(updatedKeys.Length, Is.EqualTo(2));
	}

	#endregion

	#region Edge Cases

	[Test]
	public void EmptyCache_ReturnsNoResults() {
		// Create fresh cache
		var registry = new DataCacheRegistryBuilder()
			.Register<ProductWithBooleanFlagsCache>()
			.Build();
		var emptyCache = registry.GetCache<ProductWithBooleanFlagsCache>();

		var results = emptyCache.Query()
			.WithIsActive()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void AllBooleanIndexes_MaintainedCorrectly() {
		// Verify all boolean indexes are correctly maintained
		Assert.That(_cache.HasIsActiveIndex.GetKeys().Count(), Is.EqualTo(3)); // 1, 3, 5
		Assert.That(_cache.HasIsFeaturedIndex.GetKeys().Count(), Is.EqualTo(3)); // 1, 4, 5
		Assert.That(_cache.HasNotIsDiscontinuedIndex.GetKeys().Count(), Is.EqualTo(3)); // 1, 3, 5 (false)
		Assert.That(_cache.HasNotIsOutOfStockIndex.GetKeys().Count(), Is.EqualTo(3)); // 1, 4, 5 (false)
	}

	#endregion

	#region Semantic Meaning Tests

	[Test]
	public void WithIsActive_SemanticallyClear_MeansActiveProducts() {
		// The method name "WithIsActive" clearly means "products where IsActive is true"
		var results = _cache.Query()
			.WithIsActive()
			.Execute();

		Assert.That(results.All(r => r.IsActive), Is.True);
	}

	[Test]
	public void WithoutIsDiscontinued_SemanticallyClear_MeansNotDiscontinued() {
		// The method name "WithoutIsDiscontinued" clearly means "products where IsDiscontinued is false"
		var results = _cache.Query()
			.WithoutIsDiscontinued()
			.Execute();

		Assert.That(results.All(r => !r.IsDiscontinued), Is.True);
	}

	#endregion
}

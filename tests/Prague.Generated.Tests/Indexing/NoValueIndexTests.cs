namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

#region Test Entities for No-Value Index

public enum InventoryStatus {
	InStock,
	OutOfStock,
	Discontinued
}

[DataCache]
public partial class NoValueIndexProduct {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	// Indexes items where Stock is NOT > 0 (i.e., Stock <= 0)
	[DataCacheNoValueIndex(CompareOp.GreaterThan, 0)]
	public int Stock { get; set; }

	// Indexes items where Price is NOT >= 100 (i.e., Price < 100)
	[DataCacheNoValueIndex(CompareOp.GreaterThanOrEqual, 100)]
	public decimal Price { get; set; }

	// Indexes items where Status is NOT Active (i.e., Status != Active)
	[DataCacheNoValueIndex(CompareOp.Equal, InventoryStatus.InStock)]
	public InventoryStatus Status { get; set; }

	// Indexes items where Priority is NOT < 10 (i.e., Priority >= 10)
	[DataCacheNoValueIndex(CompareOp.LessThan, 10)]
	public int Priority { get; set; }

	// Indexes items where CategoryId is NOT != 0 (i.e., CategoryId == 0)
	[DataCacheNoValueIndex(CompareOp.NotEqual, 0)]
	public int CategoryId { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string Category { get; set; } = "";
}

#endregion

/// <summary>
/// Tests for DataCacheNoValueIndex - creates key set indexes based on inverted comparison conditions.
/// This is the opposite of DataCacheValueIndex.
/// </summary>
[TestFixture]
public class NoValueIndexTests {
	private DataCacheRegistry _registry = null!;
	private NoValueIndexProductCache _cache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<NoValueIndexProductCache>()
			.Build();
		_cache = _registry.GetCache<NoValueIndexProductCache>();

		// Set up test data
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 1, Name = "Product A", Stock = 50, Price = 150m, Status = InventoryStatus.InStock, Priority = 5, CategoryId = 1, Category = "Electronics"
		});
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 0, Price = 50m, Status = InventoryStatus.OutOfStock, Priority = 15, CategoryId = 2, Category = "Books"
		});
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 3, Name = "Product C", Stock = 100, Price = 200m, Status = InventoryStatus.InStock, Priority = 3, CategoryId = 0, Category = "Electronics"
		});
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 4, Name = "Product D", Stock = 0, Price = 99.99m, Status = InventoryStatus.Discontinued, Priority = 20, CategoryId = 3, Category = "Toys"
		});
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 5, Name = "Product E", Stock = 25, Price = 100m, Status = InventoryStatus.OutOfStock, Priority = 8, CategoryId = 0, Category = "Books"
		});
	}

	#region Generated Index Fields Tests

	[Test]
	public void GeneratedCache_ShouldHaveStockLessThanOrEqual0Index() {
		// Index name should be: Stock + LessThanOrEqual + 0 + Index (inverted from GreaterThan)
		Assert.That(_cache.StockLessThanOrEqual0Index, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHavePriceLessThan100Index() {
		// Index name should be: Price + LessThan + 100 + Index (inverted from GreaterThanOrEqual)
		Assert.That(_cache.PriceLessThan100Index, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHaveStatusNotEqualInStockIndex() {
		// Index name should be: Status + NotEqual + InStock + Index (inverted from Equal)
		Assert.That(_cache.StatusNotEqualInStockIndex, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHavePriorityGreaterThanOrEqual10Index() {
		// Index name should be: Priority + GreaterThanOrEqual + 10 + Index (inverted from LessThan)
		Assert.That(_cache.PriorityGreaterThanOrEqual10Index, Is.Not.Null);
	}

	[Test]
	public void GeneratedCache_ShouldHaveCategoryIdEqual0Index() {
		// Index name should be: CategoryId + Equal + 0 + Index (inverted from NotEqual)
		Assert.That(_cache.CategoryIdEqual0Index, Is.Not.Null);
	}

	#endregion

	#region Index Population Tests

	[Test]
	public void StockLessThanOrEqual0Index_ShouldContainItemsWithZeroOrNegativeStock() {
		// Products 2 and 4 have Stock = 0 (NOT > 0)
		var keys = _cache.StockLessThanOrEqual0Index.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(2));
		Assert.That(keys, Contains.Item(2));
		Assert.That(keys, Contains.Item(4));
	}

	[Test]
	public void PriceLessThan100Index_ShouldContainItemsWithPriceBelow100() {
		// Products 2 (50), 4 (99.99) have Price < 100 (NOT >= 100)
		var keys = _cache.PriceLessThan100Index.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(2));
		Assert.That(keys, Contains.Item(2));
		Assert.That(keys, Contains.Item(4));
	}

	[Test]
	public void StatusNotEqualInStockIndex_ShouldContainItemsNotInStock() {
		// Products 2 (OutOfStock), 4 (Discontinued), 5 (OutOfStock) are NOT InStock
		var keys = _cache.StatusNotEqualInStockIndex.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(3));
		Assert.That(keys, Contains.Item(2));
		Assert.That(keys, Contains.Item(4));
		Assert.That(keys, Contains.Item(5));
	}

	[Test]
	public void PriorityGreaterThanOrEqual10Index_ShouldContainLowPriorityItems() {
		// Products 2 (15), 4 (20) have Priority >= 10 (NOT < 10)
		var keys = _cache.PriorityGreaterThanOrEqual10Index.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(2));
		Assert.That(keys, Contains.Item(2));
		Assert.That(keys, Contains.Item(4));
	}

	[Test]
	public void CategoryIdEqual0Index_ShouldContainItemsWithCategoryId0() {
		// Products 3, 5 have CategoryId = 0 (NOT != 0, i.e., == 0)
		var keys = _cache.CategoryIdEqual0Index.GetKeys().ToArray();
		Assert.That(keys.Length, Is.EqualTo(2));
		Assert.That(keys, Contains.Item(3));
		Assert.That(keys, Contains.Item(5));
	}

	#endregion

	#region NOT GreaterThan Tests (becomes LessThanOrEqual)

	[Test]
	public void WithStockLessThanOrEqual0_ReturnsProductsWithNoStock() {
		// Act - Stock NOT > 0 means Stock <= 0
		var results = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();

		// Assert - Products 2, 4 have stock <= 0
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4 }));
	}

	#endregion

	#region NOT GreaterThanOrEqual Tests (becomes LessThan)

	[Test]
	public void WithPriceLessThan100_ReturnsCheapProducts() {
		// Act - Price NOT >= 100 means Price < 100
		var results = _cache.Query()
			.WithPriceLessThan100()
			.Execute();

		// Assert - Products 2 (50), 4 (99.99) have price < 100
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4 }));
	}

	#endregion

	#region NOT Equal Tests (becomes NotEqual)

	[Test]
	public void WithStatusNotEqualInStock_ReturnsNonInStockProducts() {
		// Act - Status NOT == InStock means Status != InStock
		var results = _cache.Query()
			.WithStatusNotEqualInStock()
			.Execute();

		// Assert - Products 2, 4, 5 are NOT InStock
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4, 5 }));
	}

	#endregion

	#region NOT LessThan Tests (becomes GreaterThanOrEqual)

	[Test]
	public void WithPriorityGreaterThanOrEqual10_ReturnsLowPriorityProducts() {
		// Act - Priority NOT < 10 means Priority >= 10 (lower number = higher priority)
		var results = _cache.Query()
			.WithPriorityGreaterThanOrEqual10()
			.Execute();

		// Assert - Products 2 (15), 4 (20) have priority >= 10
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4 }));
	}

	#endregion

	#region NOT NotEqual Tests (becomes Equal)

	[Test]
	public void WithCategoryIdEqual0_ReturnsProductsWithoutCategory() {
		// Act - CategoryId NOT != 0 means CategoryId == 0
		var results = _cache.Query()
			.WithCategoryIdEqual0()
			.Execute();

		// Assert - Products 3, 5 have CategoryId == 0
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 3, 5 }));
	}

	#endregion

	#region Combined Index Tests

	[Test]
	public void CombinedIndexes_NotInStockAndCheap() {
		// Act - NOT InStock AND Price < 100
		var results = _cache.Query()
			.WithStatusNotEqualInStock()
			.WithPriceLessThan100()
			.Execute();

		// Assert - Products 2, 4 are NOT InStock AND have price < 100
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4 }));
	}

	[Test]
	public void CombinedIndexes_NoStockAndLowPriority() {
		// Act - Stock <= 0 AND Priority >= 10
		var results = _cache.Query()
			.WithStockLessThanOrEqual0()
			.WithPriorityGreaterThanOrEqual10()
			.Execute();

		// Assert - Products 2 (stock 0, priority 15), 4 (stock 0, priority 20)
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 4 }));
	}

	[Test]
	public void CombinedIndexes_WithRegularIndex() {
		// Act - CategoryId == 0 AND Category = "Books"
		var results = _cache.Query()
			.WithCategoryIdEqual0()
			.WithCategory("Books")
			.Execute();

		// Assert - Only Product 5 has CategoryId == 0 AND Category == "Books"
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(5));
	}

	#endregion

	#region Dynamic Update Tests

	[Test]
	public void Update_FromPositiveStockToZero_ShouldAddToNoStockIndex() {
		// Initially Product 1 has Stock = 50 (> 0)
		var initialResults = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 1), Is.False);

		// Update Product 1 to have zero stock
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 1, Name = "Product A", Stock = 0, Price = 150m, Status = InventoryStatus.InStock, Priority = 5, CategoryId = 1, Category = "Electronics"
		});

		// Now Product 1 should be in the no-stock index
		var updatedResults = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 1), Is.True);
		Assert.That(updatedResults.Count, Is.EqualTo(3));
	}

	[Test]
	public void Update_FromZeroStockToPositive_ShouldRemoveFromNoStockIndex() {
		// Initially Product 2 has Stock = 0
		var initialResults = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 2), Is.True);

		// Update Product 2 to have positive stock
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 10, Price = 50m, Status = InventoryStatus.OutOfStock, Priority = 15, CategoryId = 2, Category = "Books"
		});

		// Now Product 2 should NOT be in the no-stock index
		var updatedResults = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.False);
		Assert.That(updatedResults.Count, Is.EqualTo(1));
	}

	[Test]
	public void Update_StatusChange_ShouldUpdateNoInStockIndex() {
		// Initially Product 2 is OutOfStock (NOT InStock)
		var initialResults = _cache.Query()
			.WithStatusNotEqualInStock()
			.Execute();
		Assert.That(initialResults.Count, Is.EqualTo(3));

		// Update Product 2 to InStock
		_cache.AddOrUpdate(new NoValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 0, Price = 50m, Status = InventoryStatus.InStock, Priority = 15, CategoryId = 2, Category = "Books"
		});

		// Now should have only 2 non-InStock products
		var updatedResults = _cache.Query()
			.WithStatusNotEqualInStock()
			.Execute();
		Assert.That(updatedResults.Count, Is.EqualTo(2));
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.False);
	}

	#endregion

	#region Remove Tests

	[Test]
	public void Remove_ExistingKeyInIndex_ShouldRemoveFromIndex() {
		// Product 2 is in the no-stock index
		var initialKeys = _cache.StockLessThanOrEqual0Index.GetKeys().ToArray();
		Assert.That(initialKeys, Contains.Item(2));

		// Remove Product 2
		_cache.Remove(2);

		// Should no longer be in index
		var updatedKeys = _cache.StockLessThanOrEqual0Index.GetKeys().ToArray();
		Assert.That(updatedKeys, Does.Not.Contain(2));
		Assert.That(updatedKeys.Length, Is.EqualTo(1));
	}

	#endregion

	#region Edge Cases

	[Test]
	public void EmptyCache_ReturnsNoResults() {
		// Create fresh cache
		var registry = new DataCacheRegistryBuilder()
			.Register<NoValueIndexProductCache>()
			.Build();
		var emptyCache = registry.GetCache<NoValueIndexProductCache>();

		var results = emptyCache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MultipleNoValueIndexes_AllMaintainedCorrectly() {
		// Verify all no-value indexes are correctly maintained
		Assert.That(_cache.StockLessThanOrEqual0Index.GetKeys().Count(), Is.EqualTo(2));
		Assert.That(_cache.PriceLessThan100Index.GetKeys().Count(), Is.EqualTo(2));
		Assert.That(_cache.StatusNotEqualInStockIndex.GetKeys().Count(), Is.EqualTo(3));
		Assert.That(_cache.PriorityGreaterThanOrEqual10Index.GetKeys().Count(), Is.EqualTo(2));
		Assert.That(_cache.CategoryIdEqual0Index.GetKeys().Count(), Is.EqualTo(2));
	}

	#endregion

	#region Comparison with ValueIndex behavior

	[Test]
	public void NoValueIndex_IsInverseOfValueIndex() {
		// If we had a ValueIndex with Stock > 0, it would return Products 1, 3, 5
		// Our NoValueIndex with Stock > 0 returns the inverse: Products 2, 4
		var noValueResults = _cache.Query()
			.WithStockLessThanOrEqual0()
			.Execute();

		var allProducts = _cache.Query().Execute();
		var expectedInverse = allProducts
			.Where(p => p.Stock <= 0)
			.Select(p => p.Id)
			.OrderBy(x => x)
			.ToArray();

		var actualIds = noValueResults.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(actualIds, Is.EqualTo(expectedInverse));
	}

	#endregion
}

namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

#region Test Entities for Value Index

public enum ProductStatus {
	Draft,
	Active,
	Discontinued,
	OutOfStock
}

[DataCache]
public partial class ValueIndexProduct {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	[DataCacheValueIndex(CompareOp.GreaterThan, 0)]
	public int Stock { get; set; }

	[DataCacheValueIndex(CompareOp.GreaterThanOrEqual, 100)]
	public decimal Price { get; set; }

	[DataCacheValueIndex(CompareOp.Equal, ProductStatus.Active)]
	public ProductStatus Status { get; set; }

	[DataCacheValueIndex(CompareOp.LessThan, 10)]
	public int Priority { get; set; }

	[DataCacheValueIndex(CompareOp.NotEqual, 0)]
	public int CategoryId { get; set; }
}

#endregion

/// <summary>
/// Tests for DataCacheValueIndex - creates key set indexes based on comparison conditions.
/// </summary>
[TestFixture]
public class ValueIndexTests {
	private DataCacheRegistry _registry = null!;
	private ValueIndexProductCache _cache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<ValueIndexProductCache>()
			.Build();
		_cache = _registry.GetCache<ValueIndexProductCache>();

		// Set up test data
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 1, Name = "Product A", Stock = 50, Price = 150m, Status = ProductStatus.Active, Priority = 5, CategoryId = 1
		});
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 0, Price = 50m, Status = ProductStatus.Draft, Priority = 15, CategoryId = 2
		});
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 3, Name = "Product C", Stock = 100, Price = 200m, Status = ProductStatus.Active, Priority = 3, CategoryId = 0
		});
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 4, Name = "Product D", Stock = 0, Price = 99.99m, Status = ProductStatus.Discontinued, Priority = 20, CategoryId = 3
		});
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 5, Name = "Product E", Stock = 25, Price = 100m, Status = ProductStatus.OutOfStock, Priority = 8, CategoryId = 0
		});
	}

	#region GreaterThan Tests

	[Test]
	public void WithStockGreaterThan0_ReturnsProductsWithPositiveStock() {
		// Act - Stock > 0
		var results = _cache.Query()
			.WithStockGreaterThan0()
			.Execute();

		// Assert - Products 1, 3, 5 have stock > 0
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	#endregion

	#region GreaterThanOrEqual Tests

	[Test]
	public void WithPriceGreaterThanOrEqual100_ReturnsExpensiveProducts() {
		// Act - Price >= 100
		var results = _cache.Query()
			.WithPriceGreaterThanOrEqual100()
			.Execute();

		// Assert - Products 1 (150), 3 (200), 5 (100) have price >= 100
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	#endregion

	#region Equal Tests

	[Test]
	public void WithStatusEqualActive_ReturnsActiveProducts() {
		// Act - Status == Active
		var results = _cache.Query()
			.WithStatusEqualActive()
			.Execute();

		// Assert - Products 1, 3 are Active
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	#endregion

	#region LessThan Tests

	[Test]
	public void WithPriorityLessThan10_ReturnsHighPriorityProducts() {
		// Act - Priority < 10 (lower number = higher priority)
		var results = _cache.Query()
			.WithPriorityLessThan10()
			.Execute();

		// Assert - Products 1 (5), 3 (3), 5 (8) have priority < 10
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	#endregion

	#region NotEqual Tests

	[Test]
	public void WithCategoryIdNotEqual0_ReturnsProductsWithCategory() {
		// Act - CategoryId != 0
		var results = _cache.Query()
			.WithCategoryIdNotEqual0()
			.Execute();

		// Assert - Products 1 (1), 2 (2), 4 (3) have CategoryId != 0
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 4 }));
	}

	#endregion

	#region Combined Index Tests

	[Test]
	public void CombinedIndexes_ActiveAndInStock() {
		// Act - Active AND Stock > 0
		var results = _cache.Query()
			.WithStatusEqualActive()
			.WithStockGreaterThan0()
			.Execute();

		// Assert - Products 1, 3 are Active AND have stock > 0
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void CombinedIndexes_HighPriorityAndExpensive() {
		// Act - Priority < 10 AND Price >= 100
		var results = _cache.Query()
			.WithPriorityLessThan10()
			.WithPriceGreaterThanOrEqual100()
			.Execute();

		// Assert - Products 1 (priority 5, price 150), 3 (priority 3, price 200), 5 (priority 8, price 100)
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 5 }));
	}

	#endregion

	#region Dynamic Update Tests

	[Test]
	public void IndexUpdates_WhenValueChanges() {
		// Initially Product 2 has Stock = 0
		var initialResults = _cache.Query()
			.WithStockGreaterThan0()
			.Execute();
		Assert.That(initialResults.Any(r => r.Id == 2), Is.False);

		// Update Product 2 to have positive stock
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 10, Price = 50m, Status = ProductStatus.Draft, Priority = 15, CategoryId = 2
		});

		// Now Product 2 should be in the index
		var updatedResults = _cache.Query()
			.WithStockGreaterThan0()
			.Execute();
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.True);
		Assert.That(updatedResults.Count, Is.EqualTo(4));
	}

	[Test]
	public void IndexUpdates_WhenStatusChanges() {
		// Initially Product 2 is Draft
		var initialResults = _cache.Query()
			.WithStatusEqualActive()
			.Execute();
		Assert.That(initialResults.Count, Is.EqualTo(2));

		// Update Product 2 to Active
		_cache.AddOrUpdate(new ValueIndexProduct {
			Id = 2, Name = "Product B", Stock = 0, Price = 50m, Status = ProductStatus.Active, Priority = 15, CategoryId = 2
		});

		// Now should have 3 active products
		var updatedResults = _cache.Query()
			.WithStatusEqualActive()
			.Execute();
		Assert.That(updatedResults.Count, Is.EqualTo(3));
		Assert.That(updatedResults.Any(r => r.Id == 2), Is.True);
	}

	#endregion

	#region Edge Cases

	[Test]
	public void EmptyCache_ReturnsNoResults() {
		// Create fresh cache
		var registry = new DataCacheRegistryBuilder()
			.Register<ValueIndexProductCache>()
			.Build();
		var emptyCache = registry.GetCache<ValueIndexProductCache>();

		var results = emptyCache.Query()
			.WithStockGreaterThan0()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void RemoveItem_UpdatesIndex() {
		// Get initial count
		var initialResults = _cache.Query()
			.WithStockGreaterThan0()
			.Execute();
		var initialCount = initialResults.Count;

		// Remove Product 1 (has stock > 0)
		_cache.Remove(1);

		// Should have one less result
		var updatedResults = _cache.Query()
			.WithStockGreaterThan0()
			.Execute();
		Assert.That(updatedResults.Count, Is.EqualTo(initialCount - 1));
		Assert.That(updatedResults.Any(r => r.Id == 1), Is.False);
	}

	#endregion
}

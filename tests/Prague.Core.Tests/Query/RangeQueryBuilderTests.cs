namespace Prague.Core.Tests.Query;

using Prague.Core;
using NUnit.Framework;

/// <summary>
///   Tests for RangeQueryBuilder functionality through the UseIndex API.
///   These tests verify that the range query builder correctly filters results.
/// </summary>
[TestFixture]
public class RangeQueryBuilderTests {
	[Test]
	public void UseIndex_WithGte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values >= 5
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(6), "Should find values 5-10");
		Assert.That(result, Does.Contain("Value5"));
		Assert.That(result, Does.Contain("Value10"));
		Assert.That(result, Does.Not.Contain("Value4"));
	}

	[Test]
	public void UseIndex_WithGt_ExcludesBoundary() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values > 5 (should exclude 5)
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gt(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 6-10 (excluding 5)");
		Assert.That(result, Does.Contain("Value6"));
		Assert.That(result, Does.Contain("Value10"));
		Assert.That(result, Does.Not.Contain("Value5"), "Should exclude boundary value 5");
	}

	[Test]
	public void UseIndex_WithLte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values <= 5
		var query = cache.Query().UseIndex(rangeIndex, q => q.Lte(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 1-5");
		Assert.That(result, Does.Contain("Value1"));
		Assert.That(result, Does.Contain("Value5"));
		Assert.That(result, Does.Not.Contain("Value6"));
	}

	[Test]
	public void UseIndex_WithLt_ExcludesBoundary() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values < 5 (should exclude 5)
		var query = cache.Query().UseIndex(rangeIndex, q => q.Lt(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4), "Should find values 1-4 (excluding 5)");
		Assert.That(result, Does.Contain("Value1"));
		Assert.That(result, Does.Contain("Value4"));
		Assert.That(result, Does.Not.Contain("Value5"), "Should exclude boundary value 5");
	}

	[Test]
	public void UseIndex_WithGteAndLte_IncludesBothBoundaries() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 <= values <= 7
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(3).Lte(7));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 3-7");
		Assert.That(result, Does.Contain("Value3"), "Should include lower boundary");
		Assert.That(result, Does.Contain("Value7"), "Should include upper boundary");
		Assert.That(result, Does.Not.Contain("Value2"));
		Assert.That(result, Does.Not.Contain("Value8"));
	}

	[Test]
	public void UseIndex_WithGtAndLt_ExcludesBothBoundaries() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 < values < 7
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gt(3).Lt(7));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3), "Should find values 4-6 (excluding 3 and 7)");
		Assert.That(result, Does.Contain("Value4"));
		Assert.That(result, Does.Contain("Value6"));
		Assert.That(result, Does.Not.Contain("Value3"), "Should exclude lower boundary");
		Assert.That(result, Does.Not.Contain("Value7"), "Should exclude upper boundary");
	}

	[Test]
	public void UseIndex_WithGteAndLt_MixedBoundaries() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 <= values < 7
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(3).Lt(7));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4), "Should find values 3-6");
		Assert.That(result, Does.Contain("Value3"), "Should include lower boundary");
		Assert.That(result, Does.Contain("Value6"));
		Assert.That(result, Does.Not.Contain("Value7"), "Should exclude upper boundary");
	}

	[Test]
	public void UseIndex_WithGtAndLte_MixedBoundaries() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 < values <= 7
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gt(3).Lte(7));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4), "Should find values 4-7");
		Assert.That(result, Does.Not.Contain("Value3"), "Should exclude lower boundary");
		Assert.That(result, Does.Contain("Value4"));
		Assert.That(result, Does.Contain("Value7"), "Should include upper boundary");
	}

	[Test]
	public void UseIndex_WithDateTime_Gte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, CacheEquatable<DateTime>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => value);

		var baseDate = new DateTime(2025, 1, 1);
		for (var i = 0; i < 10; i++) {
			var date = baseDate.AddDays(i);
			cache.AddOrUpdate($"item{i}", date);
		}

		// Act - Find dates >= Jan 6
		var cutoffDate = baseDate.AddDays(5);
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(cutoffDate));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find 5 dates >= cutoff");
	}

	[Test]
	public void UseIndex_WithDateTime_Lte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, CacheEquatable<DateTime>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => value);

		var baseDate = new DateTime(2025, 1, 1);
		for (var i = 0; i < 10; i++) {
			var date = baseDate.AddDays(i);
			cache.AddOrUpdate($"item{i}", date);
		}

		// Act - Find dates <= Jan 6
		var cutoffDate = baseDate.AddDays(5);
		var query = cache.Query().UseIndex(rangeIndex, q => q.Lte(cutoffDate));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(6), "Should find 6 dates <= cutoff");
	}

	[Test]
	public void UseIndex_WithDateTime_Range_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, CacheEquatable<DateTime>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => value);

		var baseDate = new DateTime(2025, 1, 1);
		for (var i = 0; i < 10; i++) {
			var date = baseDate.AddDays(i);
			cache.AddOrUpdate($"item{i}", date);
		}

		// Act - Find dates between Jan 4 and Jan 8
		var startDate = baseDate.AddDays(3);
		var endDate = baseDate.AddDays(7);
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(startDate).Lte(endDate));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find 5 dates in range");
	}

	[Test]
	public void UseIndex_WithNoMatchingResults_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Add test data 1-10
		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Query for values > 100
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gt(100));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void UseIndex_WithEmptyCache_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		// Act
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void UseIndex_WithSingleValueMatchingGte_ReturnsValue() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);
		cache.AddOrUpdate(5, "Value5");

		// Act
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gte(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(1));
		Assert.That(result[0], Is.EqualTo("Value5"));
	}

	[Test]
	public void UseIndex_WithSingleValueAndGt_ReturnsEmpty() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);
		cache.AddOrUpdate(5, "Value5");

		// Act - Gt(5) should exclude 5
		var query = cache.Query().UseIndex(rangeIndex, q => q.Gt(5));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result, Is.Empty, "GT should exclude boundary value");
	}

	[Test]
	public void UseIndex_RangePlusListIndex_IntersectsCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestItem>();
		var rangeIndex = cache.CacheRangeIndex((key, item) => item.Priority);
		var categoryIndex = cache.CacheKeyValueListIndex((key, item) => item.Category);

		// Add test data
		cache.AddOrUpdate(1, new TestItem { Priority = 5, Category = "A" });
		cache.AddOrUpdate(2, new TestItem { Priority = 10, Category = "A" });
		cache.AddOrUpdate(3, new TestItem { Priority = 15, Category = "B" });
		cache.AddOrUpdate(4, new TestItem { Priority = 20, Category = "A" });

		// Act - Find category A items with priority >= 10
		var query = cache.Query()
			.UseIndex(categoryIndex, "A")
			.UseIndex(rangeIndex, q => q.Gte(10));
		var result = query.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2), "Should find items 2 and 4");
		Assert.That(result.Select(x => x.Priority).OrderBy(x => x).ToArray(),
			Is.EqualTo(new[] { 10, 20 }));
	}

	[Test]
	public void UseIndex_WithArgs_Gte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values >= 5 using TArgs overload
		var args = 5;
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Gte(a), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(6), "Should find values 5-10");
		Assert.That(result, Does.Contain("Value5"));
		Assert.That(result, Does.Contain("Value10"));
	}

	[Test]
	public void UseIndex_WithArgs_GteAndLte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 <= values <= 7 using TArgs overload with tuple
		var args = (Min: 3, Max: 7);
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Gte(a.Min).Lte(a.Max), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 3-7");
		Assert.That(result, Does.Contain("Value3"));
		Assert.That(result, Does.Contain("Value7"));
	}

	[Test]
	public void UseIndex_WithArgs_GtAndLt_ExcludesBoundaries() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find 3 < values < 7 using TArgs overload
		var args = (Lower: 3, Upper: 7);
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Gt(a.Lower).Lt(a.Upper), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3), "Should find values 4-6");
		Assert.That(result, Does.Not.Contain("Value3"));
		Assert.That(result, Does.Not.Contain("Value7"));
	}

	[Test]
	public void UseIndex_WithArgs_DateTime_Range_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<string, CacheEquatable<DateTime>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => value);

		var baseDate = new DateTime(2025, 1, 1);
		for (var i = 0; i < 10; i++) {
			var date = baseDate.AddDays(i);
			cache.AddOrUpdate($"item{i}", date);
		}

		// Act - Find dates between Jan 4 and Jan 8 using TArgs
		var args = (Start: baseDate.AddDays(3), End: baseDate.AddDays(7));
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Gte(a.Start).Lte(a.End), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find 5 dates in range");
	}

	[Test]
	public void UseIndex_WithArgs_CombinedWithListIndex_IntersectsCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestItem>();
		var rangeIndex = cache.CacheRangeIndex((key, item) => item.Priority);
		var categoryIndex = cache.CacheKeyValueListIndex((key, item) => item.Category);

		cache.AddOrUpdate(1, new TestItem { Priority = 5, Category = "A" });
		cache.AddOrUpdate(2, new TestItem { Priority = 10, Category = "A" });
		cache.AddOrUpdate(3, new TestItem { Priority = 15, Category = "B" });
		cache.AddOrUpdate(4, new TestItem { Priority = 20, Category = "A" });

		// Act - Find category A items with priority >= 10 using TArgs
		var args = (Category: "A", MinPriority: 10);
		var query = cache.Query()
			.UseIndex(categoryIndex, args.Category)
			.UseIndex(rangeIndex, static (q, a) => q.Gte(a.MinPriority), args);
		var result = query.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2), "Should find items 2 and 4");
		Assert.That(result.Select(x => x.Priority).OrderBy(x => x).ToArray(),
			Is.EqualTo(new[] { 10, 20 }));
	}

	[Test]
	public void UseIndex_WithArgs_Lt_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values < 5 using TArgs overload
		var args = 5;
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Lt(a), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4), "Should find values 1-4");
		Assert.That(result, Does.Not.Contain("Value5"));
	}

	[Test]
	public void UseIndex_WithArgs_Lte_FiltersCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - Find values <= 5 using TArgs overload
		var args = 5;
		var query = cache.Query().UseIndex(rangeIndex, static (q, a) => q.Lte(a), args);
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 1-5");
		Assert.That(result, Does.Contain("Value5"));
	}

	// Regression tests for the missed Dispose() on the non-first `.Lt()` (strict upper-bound)
	// branch in CacheQueryBuilderCoreCombined.UseIndexCore. That branch builds a ValueIntersect
	// whose Dispose() runs the intersection Flush (removes unmarked candidates). Without the
	// Dispose the Flush never ran, so a standalone `.Lt()` applied AFTER a prior narrowing failed
	// to prune out-of-range candidates and the query returned a superset.

	[Test]
	public void UseIndex_LtAfterListIndex_PrunesOutOfRangeCandidates() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestItem>();
		var rangeIndex = cache.CacheRangeIndex((key, item) => item.Priority);
		var categoryIndex = cache.CacheKeyValueListIndex((key, item) => item.Category);

		cache.AddOrUpdate(1, new TestItem { Priority = 5, Category = "A" });
		cache.AddOrUpdate(2, new TestItem { Priority = 10, Category = "A" });
		cache.AddOrUpdate(3, new TestItem { Priority = 15, Category = "B" });
		cache.AddOrUpdate(4, new TestItem { Priority = 20, Category = "A" });

		// Act - category A first (seeds candidates {1,2,4}), then a standalone strict `< 15`.
		// Only priorities 5 and 10 qualify; priority 20 must be pruned.
		var query = cache.Query()
			.UseIndex(categoryIndex, "A")
			.UseIndex(rangeIndex, q => q.Lt(15));
		var result = query.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(2), "Should prune the out-of-range priority-20 item");
		Assert.That(result.Select(x => x.Priority).OrderBy(x => x).ToArray(),
			Is.EqualTo(new[] { 5, 10 }));
	}

	[Test]
	public void UseIndex_LtAfterListIndex_ExcludesBoundaryAndOutOfRange() {
		// Arrange
		var cache = new InMemoryDataCache<int, TestItem>();
		var rangeIndex = cache.CacheRangeIndex((key, item) => item.Priority);
		var categoryIndex = cache.CacheKeyValueListIndex((key, item) => item.Category);

		cache.AddOrUpdate(1, new TestItem { Priority = 10, Category = "A" });
		cache.AddOrUpdate(2, new TestItem { Priority = 10, Category = "A" }); // boundary, must be excluded by Lt(10)
		cache.AddOrUpdate(3, new TestItem { Priority = 30, Category = "A" }); // out of range
		cache.AddOrUpdate(4, new TestItem { Priority = 3, Category = "A" });  // in range

		// Act - category A first, then strict `< 10`.
		var query = cache.Query()
			.UseIndex(categoryIndex, "A")
			.UseIndex(rangeIndex, q => q.Lt(10));
		var result = query.Execute();

		// Assert - only priority 3 survives; 10 (boundary) and 30 (out of range) are pruned.
		Assert.That(result.Count, Is.EqualTo(1), "Lt(10) after a prior filter must exclude the boundary and out-of-range items");
		Assert.That(result.Single().Priority, Is.EqualTo(3));
	}

	[Test]
	public void UseIndex_GteThenSeparateLt_PrunesCorrectly() {
		// Arrange
		var cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		var rangeIndex = cache.CacheRangeIndex((key, value) => key);

		for (var i = 1; i <= 10; i++) cache.AddOrUpdate(i, $"Value{i}");

		// Act - two separate range calls: Gte(3) seeds candidates, then a standalone Lt(8).
		// The second call takes the non-first `(None, Than)` branch. Expect 3,4,5,6,7.
		var query = cache.Query()
			.UseIndex(rangeIndex, q => q.Gte(3))
			.UseIndex(rangeIndex, q => q.Lt(8));
		var result = query.Execute().Select(x => x.Value).ToList();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find values 3-7");
		Assert.That(result, Does.Contain("Value3"));
		Assert.That(result, Does.Contain("Value7"));
		Assert.That(result, Does.Not.Contain("Value2"), "below the Gte lower bound");
		Assert.That(result, Does.Not.Contain("Value8"), "at/above the Lt upper bound");
		Assert.That(result, Does.Not.Contain("Value10"), "must be pruned by the standalone Lt");
	}

	[Test]
	public void UseIndex_WithArgs_LtAfterListIndex_PrunesCorrectly() {
		// Arrange - exercises the TArgs overload path into the same non-first `.Lt()` branch.
		var cache = new InMemoryDataCache<int, TestItem>();
		var rangeIndex = cache.CacheRangeIndex((key, item) => item.Priority);
		var categoryIndex = cache.CacheKeyValueListIndex((key, item) => item.Category);

		cache.AddOrUpdate(1, new TestItem { Priority = 5, Category = "A" });
		cache.AddOrUpdate(2, new TestItem { Priority = 25, Category = "A" });
		cache.AddOrUpdate(3, new TestItem { Priority = 8, Category = "A" });

		// Act
		var args = 10;
		var query = cache.Query()
			.UseIndex(categoryIndex, "A")
			.UseIndex(rangeIndex, static (q, a) => q.Lt(a), args);
		var result = query.Execute();

		// Assert - priorities 5 and 8 survive; 25 is pruned.
		Assert.That(result.Count, Is.EqualTo(2), "Should prune the out-of-range priority-25 item");
		Assert.That(result.Select(x => x.Priority).OrderBy(x => x).ToArray(),
			Is.EqualTo(new[] { 5, 8 }));
	}

	private class TestItem : ICacheEquatable<TestItem>, ICacheClonable<TestItem> {
		public int Priority { get; set; }
		public string Category { get; set; } = string.Empty;

		public TestItem Clone() {
			return new TestItem { Priority = Priority, Category = Category };
		}

		public bool CacheEquals(TestItem? other) {
			if (other is null) return false;
			return Priority == other.Priority && Category == other.Category;
		}

		public int CacheGetHashCode() {
			return HashCode.Combine(Priority, Category);
		}
	}
}
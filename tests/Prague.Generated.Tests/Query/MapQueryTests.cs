namespace Prague.Generated.Tests.Query;

#if false
/// <summary>
///   Tests for the Map method on CacheQueryBuilder.
///   Verifies that mapping transformations work correctly with various query scenarios.
/// </summary>
[TestFixture]
public class MapQueryTests {
	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, CacheEquatable<string>>();
		_categoryIndex = _cache.CacheKeyValueListIndex((key, value) => value.Value.Split(':')[0]);

		// Add test data: "category:name"
		_cache.AddOrUpdate(1, "fruits:apple");
		_cache.AddOrUpdate(2, "fruits:banana");
		_cache.AddOrUpdate(3, "fruits:orange");
		_cache.AddOrUpdate(4, "vegetables:carrot");
		_cache.AddOrUpdate(5, "vegetables:broccoli");
		_cache.AddOrUpdate(6, "vegetables:spinach");
		_cache.AddOrUpdate(7, "fruits:grape");
		_cache.AddOrUpdate(8, "vegetables:lettuce");
		_cache.AddOrUpdate(9, "fruits:strawberry");
		_cache.AddOrUpdate(10, "vegetables:tomato");
	}

	private InMemoryDataCache<int, CacheEquatable<string>> _cache;
	private CacheKeyValueListIndex<int, CacheEquatable<string>, string> _categoryIndex;

	[Test]
	public void Map_SimpleTransformation_ReturnsTransformedResults() {
		// Arrange - Map from full string to just the name part
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("apple"));
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("grape"));
		Assert.That(result, Does.Contain("strawberry"));

		// Should not contain vegetables
		Assert.That(result, Does.Not.Contain("carrot"));
	}

	[Test]
	public void Map_ToComplexObject_CreatesNewObjects() {
		// Arrange - Map to a complex DTO
		Func<CacheEquatable<string>, ItemDto> mapper = item => {
			var parts = item.Value.Split(':');
			return new ItemDto {
				Category = parts[0],
				Name = parts[1],
				NameLength = parts[1].Length
			};
		};

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "vegetables")
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(5));

		var carrot = result.First(x => x.Name == "carrot");
		Assert.That(carrot.Category, Is.EqualTo("vegetables"));
		Assert.That(carrot.NameLength, Is.EqualTo(6));

		var broccoli = result.First(x => x.Name == "broccoli");
		Assert.That(broccoli.NameLength, Is.EqualTo(8));
	}

	[Test]
	public void Map_WithFilter_AppliesFilterBeforeMapping() {
		// Arrange - Filter for long names, then map to uppercase
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1].ToUpper();

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Where(item => item.Value.Split(':')[1].Length > 5) // Only long names
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(3)); // "banana" (6), "orange" (6), "strawberry" (10)
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("BANANA"));
		Assert.That(result, Does.Contain("ORANGE"));
		Assert.That(result, Does.Contain("STRAWBERRY"));
		Assert.That(result, Does.Not.Contain("APPLE")); // Too short
		Assert.That(result, Does.Not.Contain("GRAPE")); // Too short
	}

	[Test]
	public void Map_WithPagination_ReturnsCorrectPage() {
		// Arrange - Map to name only
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act - Skip first 2, take 3
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute(2, 3);

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5), "Total count should still be 5");
		Assert.That(result.Count, Is.EqualTo(3), "Should return only 3 items");
	}

	[Test]
	public void Map_WithSorting_SortsBeforeMapping() {
		// Arrange - Map to name, but sort by original value first
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];
		var comparer = Comparer<CacheEquatable<string>>.Create((a, b) =>
			string.Compare(a.Value, b.Value, StringComparison.Ordinal));

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute(comparer);

		// Assert
		Assert.That(result.Count, Is.EqualTo(5));
		// Results should be sorted by full value (fruits:apple, fruits:banana, etc.)
		Assert.That(result[0], Is.EqualTo("apple"));
		Assert.That(result[1], Is.EqualTo("banana"));
		Assert.That(result[2], Is.EqualTo("grape"));
		Assert.That(result[3], Is.EqualTo("orange"));
		Assert.That(result[4], Is.EqualTo("strawberry"));
	}

	[Test]
	public void Map_WithMultipleIndices_CombinesCorrectly() {
		// Arrange - Query with OR, then map
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act - Get both fruits and vegetables, then map
		var result = _cache.Query()
			.Or(
				q => q.UseIndex(_categoryIndex, "fruits"),
				q => q.UseIndex(_categoryIndex, "vegetables")
			)
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(10), "Should get all items");
		Assert.That(result.Count, Is.EqualTo(10));
	}

	[Test]
	public void Map_ToSameType_WorksCorrectly() {
		// Arrange - Map string to string (identity-ish)
		Func<CacheEquatable<string>, CacheEquatable<string>> mapper = item =>
			new CacheEquatable<string>(item.Value.ToUpper());

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result.All(x => x.Value == x.Value.ToUpper()), Is.True);
	}

	[Test]
	public void Map_WithPooledExecution_WorksCorrectly() {
		// Arrange
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "vegetables")
			.Map(mapper)
			.ExecutePooled();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("carrot"));
		Assert.That(result, Does.Contain("broccoli"));

		// Clean up pooled resources
		result.Dispose();
	}

	[Test]
	public void Map_EmptyResults_ReturnsEmptyMappedResults() {
		// Arrange
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act - Query for non-existent category
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "nonexistent")
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(0));
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void Map_WithPaginationBeyondResults_ReturnsEmptyWithCorrectTotal() {
		// Arrange
		Func<CacheEquatable<string>, string> mapper = item => item.Value.Split(':')[1];

		// Act - Skip beyond available results
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute(100, 10);

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5), "Total count should still be accurate");
		Assert.That(result.Count, Is.EqualTo(0), "No items in this page");
	}

	[Test]
	public void Map_ToNumericType_CalculatesCorrectly() {
		// Arrange - Map to name length (int)
		Func<CacheEquatable<string>, int> mapper = item => item.Value.Split(':')[1].Length;

		// Act
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(mapper)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain(5)); // apple, grape
		Assert.That(result, Does.Contain(6)); // banana, orange
		Assert.That(result, Does.Contain(10)); // strawberry
	}

	// ───────────────────── Map with TArgs ─────────────────────
	/*

	[Test]
	public void Map_WithArgs_PassesArgToMapper() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(static (item, suffix) => item.Value.Split(':')[1] + suffix, "!")
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("apple!"));
		Assert.That(result, Does.Contain("banana!"));
	}

	[Test]
	public void Map_WithArgs_WithSorting() {
		var comparer = Comparer<CacheEquatable<string>>.Create((a, b) =>
			string.Compare(a.Value, b.Value, StringComparison.Ordinal));

		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(static (item, upper) => upper ? item.Value.Split(':')[1].ToUpper() : item.Value.Split(':')[1], true)
			.Execute(comparer);

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result[0], Is.EqualTo("APPLE"));
	}

	// ───────────────────── Map with IMapper struct ─────────────────────

	private struct NameExtractor : IMapper<CacheEquatable<string>, string> {
		public string Map(CacheEquatable<string> value) => value.Value.Split(':')[1];
	}

	private struct NameLengthMapper : IMapper<CacheEquatable<string>, int> {
		public int Map(CacheEquatable<string> value) => value.Value.Split(':')[1].Length;
	}

	[Test]
	public void Map_WithStructMapper_ReturnsTransformedResults() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("apple"));
		Assert.That(result, Does.Contain("banana"));
	}

	[Test]
	public void Map_WithStructMapper_ToNumericType() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<int, NameLengthMapper>(new NameLengthMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain(5));  // apple, grape
		Assert.That(result, Does.Contain(6));  // banana, orange
		Assert.That(result, Does.Contain(10)); // strawberry
	}

	// ───────────────────── Map then Sort (on mapped type) ─────────────────────

	[Test]
	public void Map_ThenSort_SortsOnMappedType() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result[0], Is.EqualTo("apple"));
		Assert.That(result[1], Is.EqualTo("banana"));
		Assert.That(result[2], Is.EqualTo("grape"));
		Assert.That(result[3], Is.EqualTo("orange"));
		Assert.That(result[4], Is.EqualTo("strawberry"));
	}

	[Test]
	public void Map_ThenSort_Descending() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<int, NameLengthMapper>(new NameLengthMapper())
			.Sort(Comparer<int>.Create((a, b) => b.CompareTo(a)))
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result[0], Is.EqualTo(10)); // strawberry
	}

	[Test]
	public void Map_ThenSort_WithPagination() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Sort(StringComparer.Ordinal)
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo("banana"));
		Assert.That(result[1], Is.EqualTo("grape"));
	}


	// ───────────────────── Where on mapped results ─────────────────────

	[Test]
	public void Map_ThenWhere_FiltersOnMappedType() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(name => name.Length > 5)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3)); // banana(6), orange(6), strawberry(10)
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("strawberry"));
		Assert.That(result, Does.Not.Contain("apple"));
		Assert.That(result, Does.Not.Contain("grape"));
	}

	[Test]
	public void Map_ThenWhere_MultipleWheres() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<int, NameLengthMapper>(new NameLengthMapper())
			.Where(len => len > 4)
			.Where(len => len < 7)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4)); // apple(5), grape(5), banana(6), orange(6)
		Assert.That(result, Does.Contain(5));
		Assert.That(result, Does.Contain(6));
		Assert.That(result, Does.Not.Contain(10));
	}

	[Test]
	public void Map_ThenWhere_FiltersAllOut() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(name => name.StartsWith("z"))
			.Execute();

		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void Map_ThenWhere_WithArgs_FiltersOnMappedType() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map(static (item, prefix) => prefix + item.Value.Split(':')[1], "f_")
			.Where(name => name.Length > 7)
			.Execute();

		// f_apple(7), f_banana(8), f_orange(8), f_grape(7), f_strawberry(12)
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("f_banana"));
		Assert.That(result, Does.Contain("f_orange"));
		Assert.That(result, Does.Contain("f_strawberry"));
	}

	[Test]
	public void Map_ThenWhereThenSort_CombinesCorrectly() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(name => name.Length > 5)
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result[0], Is.EqualTo("banana"));
		Assert.That(result[1], Is.EqualTo("orange"));
		Assert.That(result[2], Is.EqualTo("strawberry"));
	}

	[Test]
	public void Map_ThenWhere_WithSortedExecute() {
		var comparer = Comparer<CacheEquatable<string>>.Create((a, b) =>
			string.Compare(a.Value, b.Value, StringComparison.Ordinal));

		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(name => name.Length > 5)
			.Execute(comparer);

		Assert.That(result.Count, Is.EqualTo(3));
		// Sorted by original TValue, then mapped, then filtered
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("strawberry"));
	}

	[Test]
	public void Map_ThenWhere_WithPooled() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(name => name.Length > 5)
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("banana"));
		result.Dispose();
	}

	// ───────────────────── Where with struct IPredicate ─────────────────────

	private struct LongNamePredicate : IPredicate<string> {
		public bool Should(string value) => value.Length > 5;
	}

	private struct ShortNamePredicate : IPredicate<string> {
		public bool Should(string value) => value.Length <= 5;
	}

	[Test]
	public void Map_ThenWhereStructPredicate_FiltersOnMappedType() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(new LongNamePredicate())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("strawberry"));
		Assert.That(result, Does.Not.Contain("apple"));
		Assert.That(result, Does.Not.Contain("grape"));
	}

	[Test]
	public void Map_ThenWhereStructPredicate_ShortNames() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(new ShortNamePredicate())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain("apple"));
		Assert.That(result, Does.Contain("grape"));
	}

	[Test]
	public void Map_ThenWhereStructPredicate_MixedWithDelegate() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(new LongNamePredicate())
			.Where(name => name.StartsWith("b"))
			.Execute();

		Assert.That(result.Count, Is.EqualTo(1));
		Assert.That(result, Does.Contain("banana"));
	}

	[Test]
	public void Map_ThenWhereStructPredicateThenSort() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.Map<string, NameExtractor>(new NameExtractor())
			.Where(new LongNamePredicate())
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result[0], Is.EqualTo("banana"));
		Assert.That(result[1], Is.EqualTo("orange"));
		Assert.That(result[2], Is.EqualTo("strawberry"));
	}

	// ───────────────────── MapWhere (store-level map+filter) ─────────────────────

	private struct FruitNameMapper : ICacheWhereMapper<CacheEquatable<string>, string> {
		public CacheMapResult<string> MapOrFilter(CacheEquatable<string> value) {
			var parts = value.Value.Split(':');
			if (parts[0] == "fruits")
				return CacheMapResult<string>.Ok(parts[1]);
			return CacheMapResult<string>.Skip();
		}
	}

	private struct LongNameWhereMapper : ICacheWhereMapper<CacheEquatable<string>, string> {
		public CacheMapResult<string> MapOrFilter(CacheEquatable<string> value) {
			var name = value.Value.Split(':')[1];
			if (name.Length > 5)
				return CacheMapResult<string>.Ok(name);
			return CacheMapResult<string>.Skip();
		}
	}

	private struct AllPassNameMapper : ICacheWhereMapper<CacheEquatable<string>, string> {
		public CacheMapResult<string> MapOrFilter(CacheEquatable<string> value) {
			return CacheMapResult<string>.Ok(value.Value.Split(':')[1]);
		}
	}

	private struct AllSkipNameMapper : ICacheWhereMapper<CacheEquatable<string>, string> {
		public CacheMapResult<string> MapOrFilter(CacheEquatable<string> value) {
			return CacheMapResult<string>.Skip();
		}
	}

	[Test]
	public void MapWhere_FiltersAndMaps() {
		// FruitNameMapper filters only fruits and extracts name
		var result = _cache.Query()
			.MapWhere<string, FruitNameMapper>(new FruitNameMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("apple"));
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("grape"));
		Assert.That(result, Does.Contain("strawberry"));
		Assert.That(result, Does.Not.Contain("carrot"));
	}

	[Test]
	public void MapWhere_FiltersAllOut() {
		var result = _cache.Query()
			.MapWhere<string, AllSkipNameMapper>(new AllSkipNameMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_FiltersNoneOut() {
		var result = _cache.Query()
			.MapWhere<string, AllPassNameMapper>(new AllPassNameMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(10));
	}

	[Test]
	public void MapWhere_WithIndexFilter() {
		// Use index to get fruits, then MapWhere extracts names of long-named fruits
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "fruits")
			.MapWhere<string, LongNameWhereMapper>(new LongNameWhereMapper())
			.Execute();

		// fruits: apple(5), banana(6), orange(6), grape(5), strawberry(10)
		// long names (>5): banana, orange, strawberry
		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain("banana"));
		Assert.That(result, Does.Contain("orange"));
		Assert.That(result, Does.Contain("strawberry"));
	}

	[Test]
	public void MapWhere_WithPagination() {
		var result = _cache.Query()
			.MapWhere<string, AllPassNameMapper>(new AllPassNameMapper())
			.Execute(2, 3);

		Assert.That(result.TotalCount, Is.EqualTo(10));
		Assert.That(result.Count, Is.EqualTo(3));
	}

	[Test]
	public void MapWhere_WithPooled() {
		var result = _cache.Query()
			.MapWhere<string, FruitNameMapper>(new FruitNameMapper())
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result, Does.Contain("apple"));
		result.Dispose();
	}

	[Test]
	public void MapWhere_ThenSort() {
		var result = _cache.Query()
			.MapWhere<string, FruitNameMapper>(new FruitNameMapper())
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result[0], Is.EqualTo("apple"));
		Assert.That(result[1], Is.EqualTo("banana"));
		Assert.That(result[2], Is.EqualTo("grape"));
		Assert.That(result[3], Is.EqualTo("orange"));
		Assert.That(result[4], Is.EqualTo("strawberry"));
	}

	[Test]
	public void MapWhere_ThenSort_WithPagination() {
		var result = _cache.Query()
			.MapWhere<string, FruitNameMapper>(new FruitNameMapper())
			.Sort(StringComparer.Ordinal)
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(5));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo("banana"));
		Assert.That(result[1], Is.EqualTo("grape"));
	}

	[Test]
	public void MapWhere_WithIndex_ThenSort() {
		var result = _cache.Query()
			.UseIndex(_categoryIndex, "vegetables")
			.MapWhere<string, AllPassNameMapper>(new AllPassNameMapper())
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result[0], Is.EqualTo("broccoli"));
		Assert.That(result[1], Is.EqualTo("carrot"));
		Assert.That(result[2], Is.EqualTo("lettuce"));
		Assert.That(result[3], Is.EqualTo("spinach"));
		Assert.That(result[4], Is.EqualTo("tomato"));
	}
	*/

	// Helper DTO for testing complex object mapping
	private class ItemDto {
		public string Category { get; set; }
		public string Name { get; set; }
		public int NameLength { get; set; }
	}
}
#endif
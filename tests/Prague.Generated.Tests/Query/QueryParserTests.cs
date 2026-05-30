namespace Prague.Generated.Tests.Query;

using Prague.Core;
using Prague.Tests.Models;
using NUnit.Framework;

[TestFixture]
public class QueryParserTests {
	private static QueryParserTestModelCache CreateTestCache() {
		var cache = new QueryParserTestModelCache();

		cache.AddOrUpdate(new QueryParserTestModel {
			Id = 1,
			Category = "Electronics",
			UserId = 100,
			Status = TestStatus.Active,
			Priority = TestPriority.High,
			Name = "Laptop",
			Description = "Gaming laptop",
			Timestamp = 1000,
			Score = 85
		});

		cache.AddOrUpdate(new QueryParserTestModel {
			Id = 2,
			Category = "Electronics",
			UserId = 101,
			Status = TestStatus.Active,
			Priority = TestPriority.Medium,
			Name = "Phone",
			Description = "Smartphone",
			Timestamp = 2000,
			Score = 90
		});

		cache.AddOrUpdate(new QueryParserTestModel {
			Id = 3,
			Category = "Books",
			UserId = 100,
			Status = TestStatus.Inactive,
			Priority = TestPriority.Low,
			Name = "Novel",
			Description = "Fiction book",
			Timestamp = 3000,
			Score = 75
		});

		cache.AddOrUpdate(new QueryParserTestModel {
			Id = 4,
			Category = "Electronics",
			UserId = 102,
			Status = TestStatus.Pending,
			Priority = TestPriority.Critical,
			Name = "Tablet",
			Description = "Android tablet",
			Timestamp = 4000,
			Score = 95
		});

		cache.AddOrUpdate(new QueryParserTestModel {
			Id = 5,
			Category = "Books",
			UserId = 101,
			Status = TestStatus.Active,
			Priority = TestPriority.High,
			Name = "Textbook",
			Description = "Computer Science",
			Timestamp = 5000,
			Score = 80
		});

		return cache;
	}

	[Test]
	public void QueryParser_ImplementsInterface() {
		var cache = CreateTestCache();
		Assert.That(cache.QueryParser, Is.InstanceOf<IQueryParser<QueryParserTestModel>>());
	}

	[Test]
	public void StringQuery_WithSingleIndexedField_UsesIndex() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Category=Electronics");
		Assert.That(results, Has.Count.EqualTo(3));
		foreach (var item in results) Assert.That(item.Category, Is.EqualTo("Electronics"));
	}

	[Test]
	public void StringQuery_WithEnumField_ParsesCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Status=Active");
		Assert.That(results, Has.Count.EqualTo(3));
		foreach (var item in results) Assert.That(item.Status, Is.EqualTo(TestStatus.Active));
	}

	[Test]
	public void StringQuery_WithMultipleValues_UsesInClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("UserId=100,101");
		Assert.That(results, Has.Count.EqualTo(4));
		foreach (var item in results) Assert.That(item.UserId, Is.AnyOf(100, 101));
	}

	// Range operator tests with Range index (Timestamp)
	[Test]
	public void StringQuery_WithRangeIndex_Gt_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp.gt=3000");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 4000 and 5000
		foreach (var item in results) Assert.That(item.Timestamp, Is.GreaterThan(3000));
	}

	[Test]
	public void StringQuery_WithRangeIndex_Gte_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp.gte=3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 3000, 4000, 5000
		foreach (var item in results) Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(3000));
	}

	[Test]
	public void StringQuery_WithRangeIndex_Lt_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp.lt=3000");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 1000 and 2000
		foreach (var item in results) Assert.That(item.Timestamp, Is.LessThan(3000));
	}

	[Test]
	public void StringQuery_WithRangeIndex_Lte_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp.lte=3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 1000, 2000, 3000
		foreach (var item in results) Assert.That(item.Timestamp, Is.LessThanOrEqualTo(3000));
	}

	[Test]
	public void StringQuery_WithRangeIndex_Equality_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp=3000");
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Timestamp, Is.EqualTo(3000));
	}

	// Range operator tests without Range index (Score uses Where clause)
	[Test]
	public void StringQuery_WithoutRangeIndex_Gt_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score.gt=85");
		Assert.That(results, Has.Count.EqualTo(2)); // Score 90 and 95
		foreach (var item in results) Assert.That(item.Score, Is.GreaterThan(85));
	}

	[Test]
	public void StringQuery_WithoutRangeIndex_Gte_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score.gte=85");
		Assert.That(results, Has.Count.EqualTo(3)); // Score 85, 90, 95
		foreach (var item in results) Assert.That(item.Score, Is.GreaterThanOrEqualTo(85));
	}

	[Test]
	public void StringQuery_WithoutRangeIndex_Lt_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score.lt=85");
		Assert.That(results, Has.Count.EqualTo(2)); // Score 75 and 80
		foreach (var item in results) Assert.That(item.Score, Is.LessThan(85));
	}

	[Test]
	public void StringQuery_WithoutRangeIndex_Lte_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score.lte=85");
		Assert.That(results, Has.Count.EqualTo(3)); // Score 75, 80, 85
		foreach (var item in results) Assert.That(item.Score, Is.LessThanOrEqualTo(85));
	}

	// Combined tests
	[Test]
	public void StringQuery_WithRangeAndEquality_CombinesFilters() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Category=Electronics&Timestamp.gte=2000");
		Assert.That(results, Has.Count.EqualTo(2)); // Phone (2000) and Tablet (4000)
		foreach (var item in results) {
			Assert.That(item.Category, Is.EqualTo("Electronics"));
			Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(2000));
		}
	}

	[Test]
	public void StringQuery_WithMultipleRangeOperators_CombinesFilters() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp.gte=2000&Timestamp.lte=4000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 2000, 3000, 4000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(2000));
			Assert.That(item.Timestamp, Is.LessThanOrEqualTo(4000));
		}
	}

	[Test]
	public void StringQuery_CaseInsensitiveOperators() {
		var cache = CreateTestCache();
		var resultsLower = cache.QueryParser.StringQuery("Timestamp.gte=3000");
		var resultsUpper = cache.QueryParser.StringQuery("Timestamp.GTE=3000");
		var resultsMixed = cache.QueryParser.StringQuery("Timestamp.Gte=3000");

		Assert.That(resultsLower, Has.Count.EqualTo(3));
		Assert.That(resultsUpper, Has.Count.EqualTo(3));
		Assert.That(resultsMixed, Has.Count.EqualTo(3));
	}

	// New query format tests - Range syntax (10..100)
	[Test]
	public void StringQuery_WithRangeSyntax_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp=2000..4000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 2000, 3000, 4000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(2000));
			Assert.That(item.Timestamp, Is.LessThanOrEqualTo(4000));
		}
	}

	[Test]
	public void StringQuery_WithRangeSyntax_OnNonRangeIndex_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score=80..90");
		Assert.That(results, Has.Count.EqualTo(3)); // Score 80, 85, 90
		foreach (var item in results) {
			Assert.That(item.Score, Is.GreaterThanOrEqualTo(80));
			Assert.That(item.Score, Is.LessThanOrEqualTo(90));
		}
	}

	// New query format tests - Array syntax [1,2,3]
	[Test]
	public void StringQuery_WithArraySyntax_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("UserId=[100,101]");
		Assert.That(results, Has.Count.EqualTo(4));
		foreach (var item in results) Assert.That(item.UserId, Is.AnyOf(100, 101));
	}

	[Test]
	public void StringQuery_WithArraySyntax_StringField_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Category=[Electronics,Books]");
		Assert.That(results, Has.Count.EqualTo(5)); // All items have one of these categories
	}

	// New query format tests - Comparison operators (field>=10, field>10, field<=10, field<10)
	[Test]
	public void StringQuery_WithGreaterThanOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp>3000");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 4000 and 5000
		foreach (var item in results) Assert.That(item.Timestamp, Is.GreaterThan(3000));
	}

	[Test]
	public void StringQuery_WithGreaterThanOrEqualOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp>=3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 3000, 4000, 5000
		foreach (var item in results) Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(3000));
	}

	[Test]
	public void StringQuery_WithLessThanOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp<3000");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 1000 and 2000
		foreach (var item in results) Assert.That(item.Timestamp, Is.LessThan(3000));
	}

	[Test]
	public void StringQuery_WithLessThanOrEqualOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp<=3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 1000, 2000, 3000
		foreach (var item in results) Assert.That(item.Timestamp, Is.LessThanOrEqualTo(3000));
	}

	[Test]
	public void StringQuery_WithComparisonOperator_OnNonRangeIndex_UsesWhereClause() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Score>85");
		Assert.That(results, Has.Count.EqualTo(2)); // Score 90 and 95
		foreach (var item in results) Assert.That(item.Score, Is.GreaterThan(85));
	}

	// Alternative operator syntax tests (=> and =<)
	[Test]
	public void StringQuery_WithAlternativeGteOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp=>3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 3000, 4000, 5000
		foreach (var item in results) Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(3000));
	}

	[Test]
	public void StringQuery_WithAlternativeLteOperator_FiltersCorrectly() {
		var cache = CreateTestCache();
		var results = cache.QueryParser.StringQuery("Timestamp=<3000");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 1000, 2000, 3000
		foreach (var item in results) Assert.That(item.Timestamp, Is.LessThanOrEqualTo(3000));
	}

	// Bracket notation tests for exclusive/inclusive ranges
	[Test]
	public void StringQuery_WithInclusiveBrackets_FiltersCorrectly() {
		var cache = CreateTestCache();
		// [2000..4000] - inclusive on both ends (same as 2000..4000)
		var results = cache.QueryParser.StringQuery("Timestamp=[2000..4000]");
		Assert.That(results, Has.Count.EqualTo(3)); // Timestamp 2000, 3000, 4000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(2000));
			Assert.That(item.Timestamp, Is.LessThanOrEqualTo(4000));
		}
	}

	[Test]
	public void StringQuery_WithExclusiveBrackets_FiltersCorrectly() {
		var cache = CreateTestCache();
		// (2000..4000) - exclusive on both ends
		var results = cache.QueryParser.StringQuery("Timestamp=(2000..4000)");
		Assert.That(results, Has.Count.EqualTo(1)); // Only Timestamp 3000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThan(2000));
			Assert.That(item.Timestamp, Is.LessThan(4000));
		}
	}

	[Test]
	public void StringQuery_WithMixedBrackets_InclusiveExclusive_FiltersCorrectly() {
		var cache = CreateTestCache();
		// [2000..4000) - inclusive start, exclusive end
		var results = cache.QueryParser.StringQuery("Timestamp=[2000..4000)");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 2000, 3000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThanOrEqualTo(2000));
			Assert.That(item.Timestamp, Is.LessThan(4000));
		}
	}

	[Test]
	public void StringQuery_WithMixedBrackets_ExclusiveInclusive_FiltersCorrectly() {
		var cache = CreateTestCache();
		// (2000..4000] - exclusive start, inclusive end
		var results = cache.QueryParser.StringQuery("Timestamp=(2000..4000]");
		Assert.That(results, Has.Count.EqualTo(2)); // Timestamp 3000, 4000
		foreach (var item in results) {
			Assert.That(item.Timestamp, Is.GreaterThan(2000));
			Assert.That(item.Timestamp, Is.LessThanOrEqualTo(4000));
		}
	}

	[Test]
	public void StringQuery_WithBracketNotation_OnNonRangeIndex_UsesWhereClause() {
		var cache = CreateTestCache();
		// [80..90) - inclusive start, exclusive end on non-range index
		var results = cache.QueryParser.StringQuery("Score=[80..90)");
		Assert.That(results, Has.Count.EqualTo(2)); // Score 80, 85 (90 is excluded)
		foreach (var item in results) {
			Assert.That(item.Score, Is.GreaterThanOrEqualTo(80));
			Assert.That(item.Score, Is.LessThan(90));
		}
	}

	[Test]
	public void StringQuery_WithExclusiveBrackets_OnNonRangeIndex_UsesWhereClause() {
		var cache = CreateTestCache();
		// (75..90) - exclusive on both ends on non-range index
		var results = cache.QueryParser.StringQuery("Score=(75..90)");
		Assert.That(results, Has.Count.EqualTo(2)); // Score 80, 85 (75 and 90 are excluded)
		foreach (var item in results) {
			Assert.That(item.Score, Is.GreaterThan(75));
			Assert.That(item.Score, Is.LessThan(90));
		}
	}
}

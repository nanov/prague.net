namespace Prague.Generated.Tests.Join;

#if false
#region Test Entities for Forward Inner Join Predicates

[DataCache]
public partial class TestBrand {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Region { get; set; } = "";
	public int MinRating { get; set; }
	public bool IsActive { get; set; } = true;
}

[DataCache]
public partial class TestProduct {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	[DataCacheForeignKey<TestBrand>(DataCacheJoinType.OneToMany)]
	public int BrandId { get; set; }

	public string Region { get; set; } = "";
	public int Rating { get; set; }
	public bool IsFeatured { get; set; }
}

#endregion

/// <summary>
/// Tests for forward inner join with predicate filtering.
/// This feature allows the join predicate to access both the left and right items
/// for correlated filtering, e.g., .InnerJoinWithLeague((product, brand) => brand.Region == product.Region)
/// </summary>
[TestFixture]
public class ItemAwareJoinTests {
	private DataCacheRegistry _registry = null!;
	private TestBrandCache _brandCache = null!;
	private TestProductCache _productCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<TestBrandCache>()
			.Register<TestProductCache>()
			.Build();
		_brandCache = _registry.GetCache<TestBrandCache>();
		_productCache = _registry.GetCache<TestProductCache>();

		// Set up test data
		_brandCache.AddOrUpdate(new TestBrand { Id = 1, Name = "Globex Line", Region = "Europe", MinRating = 80, IsActive = true });
		_brandCache.AddOrUpdate(new TestBrand { Id = 2, Name = "Initech Line", Region = "Europe", MinRating = 75, IsActive = true });
		_brandCache.AddOrUpdate(new TestBrand { Id = 3, Name = "Vandelay Line", Region = "Americas", MinRating = 60, IsActive = true });
		_brandCache.AddOrUpdate(new TestBrand { Id = 4, Name = "Inactive Brand", Region = "Europe", MinRating = 50, IsActive = false });

		_productCache.AddOrUpdate(new TestProduct { Id = 1, Name = "Acme Widget", BrandId = 1, Region = "Europe", Rating = 90, IsFeatured = true });
		_productCache.AddOrUpdate(new TestProduct { Id = 2, Name = "Globex Gizmo", BrandId = 2, Region = "Europe", Rating = 95, IsFeatured = true });
		_productCache.AddOrUpdate(new TestProduct { Id = 3, Name = "Soylent Combo", BrandId = 3, Region = "Americas", Rating = 70, IsFeatured = false });
		_productCache.AddOrUpdate(new TestProduct { Id = 4, Name = "Test Product", BrandId = 4, Region = "Europe", Rating = 55, IsFeatured = false });
		// Product with mismatched region (product in Americas but brand in Europe)
		_productCache.AddOrUpdate(new TestProduct { Id = 5, Name = "Cross Region Product", BrandId = 1, Region = "Americas", Rating = 85, IsFeatured = false });
	}

	#region Basic Forward Join Tests

	[Test]
	public void ForwardJoin_WithoutFilter_ReturnsAllMatches() {
		// Act
		var results = _productCache.Query()
			.JoinWithTestBrand()
			.Execute();

		// Assert - All 5 products should have their brands joined
		Assert.That(results.Count, Is.EqualTo(5));
		Assert.That(results.All(r => r.Right != null), Is.True);
	}

	[Test]
	public void ForwardJoin_WithSimpleFilter_FiltersRightSide() {
		// Act - Only join with active brands
		var results = _productCache.Query()
			.JoinWithTestBrand(q => q.Where(l => l.IsActive))
			.Execute();

		// Assert - Product 4 should have null Right (brand is inactive)
		Assert.That(results.Count, Is.EqualTo(5));
		var product4 = results.First(r => r.Left.Id == 4);
		Assert.That(game4.Right, Is.Null, "Product 4's brand is inactive, should be null");

		// Other products should have their brands
		var product1 = results.First(r => r.Left.Id == 1);
		Assert.That(game1.Right, Is.Not.Null);
	}

	[Test]
	public void InnerForwardJoin_WithSimpleFilter_ExcludesInactiveLeagues() {
		// Act - Inner join with simple filter (no predicate)
		var results = _productCache.Query()
			.InnerJoinWithTestBrand(q => q.Where(l => l.IsActive))
			.Execute();

		// Assert - Product 4 should be excluded (inactive brand)
		Assert.That(results.Count, Is.EqualTo(4));
		Assert.That(results.Any(r => r.Left.Id == 4), Is.False, "Product 4 with inactive brand should be excluded");
	}

	#endregion

	#region Inner Join with Predicate Tests

	[Test]
	public void InnerJoin_WithPredicate_MatchingRegion_Works() {
		// Act - Inner join: only include products where region matches
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) => brand.Region == product.Region)
			.Execute();

		// Assert - Product 5 should be excluded (Americas product with European brand)
		Assert.That(results.Count, Is.EqualTo(4), "Should exclude product 5 (region mismatch)");
		Assert.That(results.Any(r => r.Left.Id == 5), Is.False, "Product 5 should be excluded");

		// All remaining products should have their brands
		Assert.That(results.All(r => r.Right != null), Is.True);

		// Verify the correct products are included
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3, 4 }));
	}

	[Test]
	public void InnerJoin_WithPredicate_RatingComparison_Works() {
		// Act - Inner join: only include products that meet minimum rating
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) => product.Rating >= brand.MinRating)
			.Execute();

		// Assert - All products meet their brand's min rating
		Assert.That(results.Count, Is.EqualTo(5));

		// Product 1: Rating 90 >= MinRating 80 (Globex Line) - included
		Assert.That(results.Any(r => r.Left.Id == 1), Is.True);
		// Product 4: Rating 55 >= MinRating 50 (Inactive Brand) - included
		Assert.That(results.Any(r => r.Left.Id == 4), Is.True);
	}

	[Test]
	public void InnerJoin_WithPredicate_RatingTooLow_ExcludesGame() {
		// Add a low-rated product
		_productCache.AddOrUpdate(new TestProduct { Id = 100, Name = "Low Rating Product", BrandId = 1, Region = "Europe", Rating = 50 });

		// Act - Inner join: only include products that meet minimum rating
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) => product.Rating >= brand.MinRating)
			.Execute();

		// Assert - Product 100 should be excluded (rating 50 < min 80)
		Assert.That(results.Any(r => r.Left.Id == 100), Is.False, "Low-rated product should be excluded");
	}

	[Test]
	public void InnerJoin_WithPredicate_CombinedConditions_Works() {
		// Act - Compare region AND check rating
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) =>
				brand.Region == product.Region && product.Rating >= brand.MinRating)
			.Execute();

		// Assert
		// Product 1: Europe/Europe, 90 >= 80 - should be included
		Assert.That(results.Any(r => r.Left.Id == 1), Is.True);

		// Product 5: Americas/Europe mismatch - should NOT be included
		Assert.That(results.Any(r => r.Left.Id == 5), Is.False);

		// Product 3: Americas/Americas, 70 >= 60 - should be included
		Assert.That(results.Any(r => r.Left.Id == 3), Is.True);
	}

	[Test]
	public void InnerJoin_WithPredicate_AccessesMultipleProperties() {
		// Act - Complex predicate using multiple properties from both items
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) =>
				brand.Region == product.Region || product.IsFeatured)
			.Execute();

		// Assert
		// Featured products (1, 2) should always match regardless of region
		Assert.That(results.Any(r => r.Left.Id == 1), Is.True, "Featured product should match");
		Assert.That(results.Any(r => r.Left.Id == 2), Is.True, "Featured product should match");

		// Non-featured products match only if region matches
		Assert.That(results.Any(r => r.Left.Id == 3), Is.True, "Non-featured product with region match should match");
		Assert.That(results.Any(r => r.Left.Id == 5), Is.False, "Non-featured product with region mismatch should not match");
	}

	#endregion

	#region Inner Join with Predicate and Argument Tests

	[Test]
	public void InnerJoin_WithPredicateAndArg_AllowsStaticLambda() {
		// Act - Inner join with static lambda using argument
		var minRating = 70;
		var results = _productCache.Query()
			.InnerJoinWithTestBrand(static (product, brand, arg) => product.Rating >= arg, minRating)
			.Execute();

		// Assert - Only products with rating >= 70 should be included
		Assert.That(results.All(r => r.Left.Rating >= 70), Is.True);

		// Products 1 (90), 2 (95), 3 (70), 5 (85) should be included
		// Products 4 (55) should be excluded
		Assert.That(results.Any(r => r.Left.Id == 4), Is.False);
		Assert.That(results.Count, Is.EqualTo(4));
	}

	[Test]
	public void InnerJoin_WithPredicateAndArg_MultipleProperties() {
		// Act - Static lambda with complex argument
		var criteria = (Region: "Europe", MinRating: 80);
		var results = _productCache.Query()
			.InnerJoinWithTestBrand(static (product, brand, arg) =>
				brand.Region == arg.Region && product.Rating >= arg.MinRating, criteria)
			.Execute();

		// Assert - Only products with European LEAGUE and rating >= 80 should be included
		// Product 1: Brand=Europe, Rating 90 - included
		// Product 2: Brand=Europe, Rating 95 - included
		// Product 4: Brand=Europe, Rating 55 - excluded (rating too low)
		// Product 5: Brand=Europe (Globex Line), Rating 85 - included (brand region is Europe!)
		// Product 3: Brand=Americas, Rating 70 - excluded (wrong brand region)
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 5 }));
	}

	#endregion

	#region Generic InnerJoin (without attribute) Tests

	[Test]
	public void GenericInnerJoin_WithPredicate_Works() {
		// Act - Use the generic InnerJoin method (no DataCacheForeignKey attribute needed)
		var results = _productCache.Query()
			.InnerJoin(
				_brandCache.Cache,
				product => product.BrandId,
				(product, brand) => brand.Region == product.Region)
			.Execute();

		// Assert - Product 5 should be excluded (Americas product with European brand)
		Assert.That(results.Count, Is.EqualTo(4), "Should exclude product 5 (region mismatch)");
		Assert.That(results.Any(r => r.Left.Id == 5), Is.False, "Product 5 should be excluded");
	}

	[Test]
	public void GenericInnerJoin_WithPredicateAndArg_Works() {
		// Act - Generic InnerJoin with static lambdas and argument (both key selector and predicate receive arg)
		var minRating = 70;
		var results = _productCache.Query()
			.InnerJoin(
				_brandCache.Cache,
				static (product, arg) => product.BrandId, // key selector also receives arg
				static (product, brand, arg) => product.Rating >= arg,
				minRating)
			.Execute();

		// Assert - Only products with rating >= 70
		Assert.That(results.All(r => r.Left.Rating >= 70), Is.True);
		Assert.That(results.Any(r => r.Left.Id == 4), Is.False, "Product 4 (rating 55) should be excluded");
		Assert.That(results.Count, Is.EqualTo(4));
	}

	[Test]
	public void GenericInnerJoin_WithArgUsedInBothFunctions_Works() {
		// Act - Argument is used in both key selector and predicate
		var criteria = (BrandIdOffset: 0, MinRating: 80);
		var results = _productCache.Query()
			.InnerJoin(
				_brandCache.Cache,
				static (product, arg) => product.BrandId + arg.BrandIdOffset, // key selector uses arg
				static (product, brand, arg) => product.Rating >= arg.MinRating, // predicate uses arg
				criteria)
			.Execute();

		// Assert - Only products with rating >= 80
		// Products 1 (90), 2 (95), 5 (85) should be included
		// Products 3 (70), 4 (55) should be excluded
		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 5 }));
	}

	[Test]
	public void GenericInnerJoin_WithDifferentCache_Works() {
		// This test shows we can join with ANY cache without needing attributes
		// Create an unrelated cache that happens to share the same key type
		var standaloneBrandCache = new TestBrandCache();
		standaloneBrandCache.AddOrUpdate(new TestBrand { Id = 1, Name = "Standalone Brand", Region = "Asia", MinRating = 100 });
		standaloneBrandCache.AddOrUpdate(new TestBrand { Id = 2, Name = "Another Brand", Region = "Europe", MinRating = 50 });
		standaloneBrandCache.AddOrUpdate(new TestBrand { Id = 3, Name = "Third Brand", Region = "Americas", MinRating = 60 });

		// Act - Join with the standalone cache (not from registry)
		var results = _productCache.Query()
			.InnerJoin(
				standaloneBrandCache.Cache,
				product => product.BrandId,
				(product, brand) => brand.Region == product.Region)
			.Execute();

		// Assert - Only products where region matches standalone brand's region
		// Product 1: BrandId=1, Region=Europe, but standalone brand 1 is Asia - excluded
		// Product 2: BrandId=2, Region=Europe, standalone brand 2 is Europe - included
		// Product 3: BrandId=3, Region=Americas, standalone brand 3 is Americas - included
		// Product 4: BrandId=4, no standalone brand 4 - excluded (key not found)
		// Product 5: BrandId=1, Region=Americas, standalone brand 1 is Asia - excluded
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 2, 3 }));
	}

	#endregion

	#region Edge Cases

	[Test]
	public void InnerJoin_WithPredicate_NoMatches_ReturnsEmpty() {
		// Act - Predicate that matches nothing
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) => brand.MinRating > 1000)
			.Execute();

		// Assert - No results (all filtered out)
		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoin_WithPredicate_AllMatch_ReturnsAll() {
		// Act - Predicate that always returns true
		var results = _productCache.Query()
			.InnerJoinWithTestBrand((product, brand) => true)
			.Execute();

		// Assert - All products should be included
		Assert.That(results.Count, Is.EqualTo(5));
	}

	[Test]
	public void InnerJoin_WithPredicate_EmptyCache_ReturnsEmpty() {
		// Create fresh cache
		var registry = new DataCacheRegistryBuilder()
			.Register<TestBrandCache>()
			.Register<TestProductCache>()
			.Build();
		var emptyProductCache = registry.GetCache<TestProductCache>();

		var results = emptyProductCache.Query()
			.InnerJoinWithTestBrand((product, brand) => true)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	#endregion
}
#endif
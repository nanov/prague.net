namespace Prague.Generated.Tests.Indexing;
#if false

[TestFixture]
public class GlobalLastUpdateIndexTests {
	[SetUp]
	public void SetUp() {
		// Create registry which creates all caches and global indexes
		_registry = new DataCacheRegistryBuilder()
			.Register<TestCatalogProductCache>()
			.Register<TestCatalogMakerCache>()
			.Register<TestProductCache>()
			.Register<TestProductItemCache>()
			.Build();

		// Get caches from registry
		_eventCache = _registry.GetCache<TestCatalogProductCache>();
		_makerCache = _registry.GetCache<TestCatalogMakerCache>();
		_productCache = _registry.GetCache<TestProductCache>();
		_productItemCache = _registry.GetCache<TestProductItemCache>();

		// Get global indexes from registry
		_catalogLastUpdatedIndex = _registry.GetGlobalIndex<CatalogLastUpdatedIndex>();
		_brandLastUpdatedIndex = _registry.GetGlobalIndex<BrandLastUpdatedIndex>();
		_productLastUpdatedIndex = _registry.GetGlobalIndex<ProductLastUpdatedIndex>();

		// Use unique IDs per test to isolate from other tests
		_testBaseId = Interlocked.Increment(ref _testCounter) * 1000;
	}

	private DataCacheRegistry _registry = null!;
	private TestCatalogProductCache _eventCache = null!;
	private TestCatalogMakerCache _makerCache = null!;
	private TestProductCache _productCache = null!;
	private TestProductItemCache _productItemCache = null!;
	private CatalogLastUpdatedIndex _catalogLastUpdatedIndex = null!;
	private BrandLastUpdatedIndex _brandLastUpdatedIndex = null!;
	private ProductLastUpdatedIndex _productLastUpdatedIndex = null!;

	// Use unique base IDs for each test to avoid interference
	private static int _testCounter;
	private int _testBaseId;

	private int UniqueDepartmentId(int offset = 0) {
		return _testBaseId + offset;
	}

	private long UniqueBrandId(int offset = 0) {
		return _testBaseId * 100L + offset;
	}

	private int UniqueProductId(int offset = 0) {
		return _testBaseId + offset;
	}

	private int UniqueProductItemId(int offset = 0) {
		return _testBaseId + 100 + offset;
	}

	[Test]
	public void CatalogLastUpdatedIndex_FromRegistry_IsSameInstance() {
		// Getting the same type from registry should return the same instance
		var instance1 = _registry.GetGlobalIndex<CatalogLastUpdatedIndex>();
		var instance2 = _registry.GetGlobalIndex<CatalogLastUpdatedIndex>();

		Assert.That(instance1, Is.SameAs(instance2));
	}

	[Test]
	public void CatalogLastUpdatedIndex_Index_IsNotNull() {
		Assert.That(_catalogLastUpdatedIndex.Index, Is.Not.Null);
	}

	[Test]
	public void BrandLastUpdatedIndex_FromRegistry_IsSameInstance() {
		var instance1 = _registry.GetGlobalIndex<BrandLastUpdatedIndex>();
		var instance2 = _registry.GetGlobalIndex<BrandLastUpdatedIndex>();

		Assert.That(instance1, Is.SameAs(instance2));
	}

	[Test]
	public void BrandLastUpdatedIndex_Index_IsNotNull() {
		Assert.That(_brandLastUpdatedIndex.Index, Is.Not.Null);
	}

	[Test]
	public void AddOrUpdate_WithTimestamp_UpdatesCatalogLastUpdatedIndex() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var evt = new TestCatalogProduct {
			EventId = $"event_{_testBaseId}_1",
			Name = "Test Event",
			DepartmentId = departmentId,
			BrandId = UniqueBrandId(),
			UpdatedAt = DateTimeOffset.UtcNow
		};

		// Act
		_eventCache.AddOrUpdate(evt, timestamp);

		// Assert - the global index should track the sport
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(departmentId, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(timestamp));
	}

	[Test]
	public void AddOrUpdate_WithTimestampProperty_UsesPropertyTimestamp() {
		// Arrange
		var brandId = UniqueBrandId();
		var propertyTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
		var evt = new TestCatalogProduct {
			EventId = $"event_{_testBaseId}_1",
			Name = "Test Event",
			DepartmentId = UniqueDepartmentId(),
			BrandId = brandId,
			UpdatedAt = propertyTimestamp
		};

		// Act - using current time as AddOrUpdate timestamp
		_eventCache.AddOrUpdate(evt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

		// Assert - BrandLastUpdatedIndex should use the UpdatedAt property, not AddOrUpdate timestamp
		var index = _brandLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(brandId, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(propertyTimestamp.ToUnixTimeMilliseconds()));
	}

	[Test]
	public void MultipleEvents_SameDepartment_TracksLatestTimestamp() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var timestamp1 = 1000L;
		var timestamp2 = 2000L;

		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId, BrandId = UniqueBrandId(2) };

		// Act
		_eventCache.AddOrUpdate(evt1, timestamp1);
		_eventCache.AddOrUpdate(evt2, timestamp2);

		// Assert - should have the latest timestamp
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(departmentId, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(timestamp2));
	}

	[Test]
	public void MultipleEvents_DifferentDepartments_TracksEachSeparately() {
		// Arrange
		var departmentId1 = UniqueDepartmentId(1);
		var departmentId2 = UniqueDepartmentId(2);
		var timestamp1 = 1000L;
		var timestamp2 = 2000L;

		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId1, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId2, BrandId = UniqueBrandId(2) };

		// Act
		_eventCache.AddOrUpdate(evt1, timestamp1);
		_eventCache.AddOrUpdate(evt2, timestamp2);

		// Assert
		var index = _catalogLastUpdatedIndex.Index;

		Assert.That(index.TryGetLastUpdated(departmentId1, out var lastUpdated1), Is.True);
		Assert.That(lastUpdated1, Is.EqualTo(timestamp1));

		Assert.That(index.TryGetLastUpdated(departmentId2, out var lastUpdated2), Is.True);
		Assert.That(lastUpdated2, Is.EqualTo(timestamp2));
	}

	[Test]
	public void SharedGlobalIndex_BothCachesUpdateSameIndex() {
		// Arrange - both TestCatalogProduct and TestCatalogMaker share CatalogLastUpdatedIndex
		var departmentId = UniqueDepartmentId();
		var eventTimestamp = 1000L;
		var makerTimestamp = 2000L;

		var evt = new TestCatalogProduct { EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = UniqueBrandId() };
		var maker = new TestCatalogMaker { MakerId = $"maker_{_testBaseId}_1", MakerName = "Globex", DepartmentId = departmentId };

		// Act
		_eventCache.AddOrUpdate(evt, eventTimestamp);
		_makerCache.AddOrUpdate(maker, makerTimestamp);

		// Assert - both updates should affect the same global index
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(departmentId, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(makerTimestamp)); // maker was added later
	}

	[Test]
	public void SharedGlobalIndex_EntitiesCountReflectsBothCaches() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId, BrandId = UniqueBrandId(2) };
		var maker = new TestCatalogMaker { MakerId = $"maker_{_testBaseId}_1", MakerName = "Globex", DepartmentId = departmentId };

		// Act
		_eventCache.AddOrUpdate(evt1, 1000);
		_eventCache.AddOrUpdate(evt2, 2000);
		_makerCache.AddOrUpdate(maker, 3000);

		// Assert - should count all entities from both caches
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.GetEntitiesCount(departmentId), Is.EqualTo(3));
	}

	[Test]
	public void Remove_DecrementsEntityCount() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId, BrandId = UniqueBrandId(2) };

		_eventCache.AddOrUpdate(evt1, 1000);
		_eventCache.AddOrUpdate(evt2, 2000);

		// Act
		_eventCache.Remove($"event_{_testBaseId}_1", 3000);

		// Assert
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.GetEntitiesCount(departmentId), Is.EqualTo(1));
	}

	[Test]
	public void Remove_AllEntities_RemovesFromIndex() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var evt = new TestCatalogProduct { EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = UniqueBrandId() };
		_eventCache.AddOrUpdate(evt, 1000);

		// Act
		_eventCache.Remove($"event_{_testBaseId}_1", 2000);

		// Assert - sport should no longer be in the index
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(departmentId, out _), Is.False);
		Assert.That(index.GetEntitiesCount(departmentId), Is.EqualTo(0));
	}

	[Test]
	public void Update_ExistingEntity_UpdatesTimestamp() {
		// Arrange
		var departmentId = UniqueDepartmentId();
		var brandId = UniqueBrandId();
		var evt = new TestCatalogProduct { EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = brandId };
		_eventCache.AddOrUpdate(evt, 1000);

		// Act - update the same entity with new timestamp (must be a NEW object to trigger update)
		var evtUpdated = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId, BrandId = brandId, Name = "Updated Name" };
		_eventCache.AddOrUpdate(evtUpdated, 2000);

		// Assert
		var index = _catalogLastUpdatedIndex.Index;
		Assert.That(index.TryGetLastUpdated(departmentId, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(2000));
		Assert.That(index.GetEntitiesCount(departmentId), Is.EqualTo(1)); // Still just one entity
	}

	[Test]
	public void Update_ChangingDepartmentId_MovesEntityBetweenGroups() {
		// Arrange
		var departmentId1 = UniqueDepartmentId(1);
		var departmentId2 = UniqueDepartmentId(2);
		var brandId = UniqueBrandId();
		var evt = new TestCatalogProduct { EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId1, BrandId = brandId };
		_eventCache.AddOrUpdate(evt, 1000);

		// Act - change the sport (must be a NEW object to trigger update)
		var evtUpdated = new TestCatalogProduct { EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId2, BrandId = brandId };
		_eventCache.AddOrUpdate(evtUpdated, 2000);

		// Assert
		var index = _catalogLastUpdatedIndex.Index;

		// Old sport should be removed
		Assert.That(index.TryGetLastUpdated(departmentId1, out _), Is.False);
		Assert.That(index.GetEntitiesCount(departmentId1), Is.EqualTo(0));

		// New sport should be tracked
		Assert.That(index.TryGetLastUpdated(departmentId2, out var lastUpdated), Is.True);
		Assert.That(lastUpdated, Is.EqualTo(2000));
		Assert.That(index.GetEntitiesCount(departmentId2), Is.EqualTo(1));
	}

	[Test]
	public void RangeQuery_GetValuesGte_ReturnsMatchingDepartments() {
		// Arrange - use unique timestamps for this test
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;
		var departmentId1 = UniqueDepartmentId(1);
		var departmentId2 = UniqueDepartmentId(2);
		var departmentId3 = UniqueDepartmentId(3);

		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId1, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId2, BrandId = UniqueBrandId(2) };
		var evt3 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_3", DepartmentId = departmentId3, BrandId = UniqueBrandId(3) };

		_eventCache.AddOrUpdate(evt1, baseTs + 1000);
		_eventCache.AddOrUpdate(evt2, baseTs + 2000);
		_eventCache.AddOrUpdate(evt3, baseTs + 3000);

		// Act
		var index = _catalogLastUpdatedIndex.Index;
		var result = index.GetValuesGte(baseTs + 2000);

		// Assert - should return sports 2 and 3 (updated at baseTs+2000 and baseTs+3000)
		Assert.That(result, Does.Contain(departmentId2));
		Assert.That(result, Does.Contain(departmentId3));
		Assert.That(result, Does.Not.Contain(departmentId1));
	}

	[Test]
	public void RangeQuery_GetValuesLt_ReturnsMatchingDepartments() {
		// Arrange - use unique timestamps for this test
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;
		var departmentId1 = UniqueDepartmentId(1);
		var departmentId2 = UniqueDepartmentId(2);
		var departmentId3 = UniqueDepartmentId(3);

		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId1, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId2, BrandId = UniqueBrandId(2) };
		var evt3 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_3", DepartmentId = departmentId3, BrandId = UniqueBrandId(3) };

		_eventCache.AddOrUpdate(evt1, baseTs + 1000);
		_eventCache.AddOrUpdate(evt2, baseTs + 2000);
		_eventCache.AddOrUpdate(evt3, baseTs + 3000);

		// Act
		var index = _catalogLastUpdatedIndex.Index;
		var result = index.GetValuesLt(baseTs + 2000);

		// Assert - should return only sport 1 (updated at baseTs+1000)
		Assert.That(result, Does.Contain(departmentId1));
		Assert.That(result, Does.Not.Contain(departmentId2));
		Assert.That(result, Does.Not.Contain(departmentId3));
	}

	[Test]
	public void RangeQuery_GetValuesBetween_ReturnsMatchingDepartments() {
		// Arrange - use unique timestamps for this test
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;
		var departmentId1 = UniqueDepartmentId(1);
		var departmentId2 = UniqueDepartmentId(2);
		var departmentId3 = UniqueDepartmentId(3);
		var departmentId4 = UniqueDepartmentId(4);

		var evt1 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_1", DepartmentId = departmentId1, BrandId = UniqueBrandId(1) };
		var evt2 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_2", DepartmentId = departmentId2, BrandId = UniqueBrandId(2) };
		var evt3 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_3", DepartmentId = departmentId3, BrandId = UniqueBrandId(3) };
		var evt4 = new TestCatalogProduct
			{ EventId = $"event_{_testBaseId}_4", DepartmentId = departmentId4, BrandId = UniqueBrandId(4) };

		_eventCache.AddOrUpdate(evt1, baseTs + 1000);
		_eventCache.AddOrUpdate(evt2, baseTs + 2000);
		_eventCache.AddOrUpdate(evt3, baseTs + 3000);
		_eventCache.AddOrUpdate(evt4, baseTs + 4000);

		// Act
		var index = _catalogLastUpdatedIndex.Index;
		var result = index.GetValuesBetween(baseTs + 1500, baseTs + 3500);

		// Assert - should return sports 2 and 3 (updated at baseTs+2000 and baseTs+3000)
		Assert.That(result, Does.Contain(departmentId2));
		Assert.That(result, Does.Contain(departmentId3));
		Assert.That(result, Does.Not.Contain(departmentId1));
		Assert.That(result, Does.Not.Contain(departmentId4));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_ReturnsProductsUpdatedAfterTimestamp() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated after baseTs + 2000
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 2000)
			.Execute();

		// Assert - should return products 3 and 4 (updated at baseTs+3000 and baseTs+4000)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(4)));
	}


	[Test]
	public void UseIndex_LastUpdatedIndex_MultipleItems_ReturnsProductsUpdatedAfterTimestamp() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };
		var productItem1 = new TestProductItem { ItemId = UniqueProductItemId(1), ProductId = product1.ProductId };
		var productItem3 = new TestProductItem { ItemId = UniqueProductItemId(2), ProductId = product3.ProductId };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);
		_productItemCache.AddOrUpdate(productItem1, baseTs + 5000);
		_productItemCache.AddOrUpdate(productItem3, baseTs + 6000);


		// Act - query products updated after baseTs + 2000
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex, baseTs + 2000)
			.Execute();

		var numberOfProduct1Entites = _productLastUpdatedIndex.Index.GetEntitiesCount(product1.ProductId);

		// Assert - should return products 3 and 4 (updated at baseTs+3000 and baseTs+4000)
		Assert.That(numberOfProduct1Entites, Is.EqualTo(2));
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(1)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(4)));
	}


	[Test]
	public void UseIndex_LastUpdatedIndex_ReturnsEmptyWhenNoMatchingProducts() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);

		// Act - query products updated after baseTs + 5000 (none exist)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 5000)
			.Execute();

		// Assert - should return empty
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_ReturnsAllWhenAllMatchTimestamp() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - query products updated after baseTs (all should match)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs)
			.Execute();

		// Assert - should return all 3 products
		Assert.That(results, Has.Count.EqualTo(3));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WorksWithWhereFilter() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated after baseTs + 1500 AND category = "A"
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500)
			.Where(p => p.Category == "A")
			.Execute();

		// Assert - should return only product 3 (updated after timestamp AND category A)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_IntersectsWithOtherIndexes() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - first use LastUpdatedIndex, then use primary key index to intersect
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500)
			.UseIndex(_productCache.Cache.KeyIndex, UniqueProductId(3))
			.Execute();

		// Assert - should return only product 3 (intersection of updated > baseTs+1500 AND key = 3)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_AsFirstIndex_ThenIntersectWithKeyIndex() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 3000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - use LastUpdatedIndex first, then key index with multiple keys
		ReadOnlySpan<int> keys = [UniqueProductId(1), UniqueProductId(3)];
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 500)
			.UseIndex(_productCache.Cache.KeyIndex, keys)
			.Execute();

		// Assert - should return products 1 and 3 (both updated > baseTs+500 AND in the key list)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(1)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_IntersectionWithNoOverlap_ReturnsEmpty() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);

		// Act - product 1 updated at baseTs+1000, but we query for updated > baseTs+1500 AND key = 1
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500)
			.UseIndex(_productCache.Cache.KeyIndex, UniqueProductId(1))
			.Execute();

		// Assert - should return empty (product 1 was updated before the threshold)
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithPagination() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated after baseTs with pagination
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs)
			.Execute(1, 2);

		// Assert - should return 2 products, with total count of 4
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.TotalCount, Is.EqualTo(4));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_ReturnsProductsInRange() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated between baseTs + 1500 and baseTs + 3000 (inclusive)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 3000)
			.Execute();

		// Assert - should return products 2 and 3 (updated at baseTs+2000 and baseTs+3000)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_UpperBoundIsInclusive() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - query products updated between baseTs + 500 and exactly baseTs + 2000 (inclusive)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 500, baseTs + 2000)
			.Execute();

		// Assert - should include product 2 (updated exactly at the upper bound)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(1)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_LowerBoundIsExclusive() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - query products updated between exactly baseTs + 1000 (exclusive) and baseTs + 3000 (inclusive)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1000, baseTs + 3000)
			.Execute();

		// Assert - should NOT include product 1 (updated exactly at the lower bound which is exclusive)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Not.Contain(UniqueProductId(1)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_ReturnsEmptyWhenNoMatchingProducts() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 5000);

		// Act - query products updated between baseTs + 2000 and baseTs + 4000 (none exist in this range)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 2000, baseTs + 4000)
			.Execute();

		// Assert - should return empty
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_WorksWithWhereFilter() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated between baseTs + 1500 and baseTs + 3500 AND category = "A"
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 3500)
			.Where(p => p.Category == "A")
			.Execute();

		// Assert - should return only product 3 (in range AND category A)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_IntersectsWithOtherIndexes() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - use range index, then intersect with key index
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 3500)
			.UseIndex(_productCache.Cache.KeyIndex, UniqueProductId(3))
			.Execute();

		// Assert - should return only product 3 (in range AND key = 3)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_UsingGlobalIndexInterface() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - use the IDataCacheGlobalLastUpdateIndex interface overload with range
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex, baseTs + 1500, baseTs + 3000)
			.Execute();

		// Assert - should return products 2 and 3 (updated at baseTs+2000 and baseTs+3000)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_SingleItemInRange() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 4000);

		// Act - query products updated between baseTs + 1500 and baseTs + 2500 (only product 2)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 2500)
			.Execute();

		// Assert - should return only product 2
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_ExactMatchOnUpperBound() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);

		// Act - query where upper bound exactly matches product2's timestamp
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 2000)
			.Execute();

		// Assert - product 2 should be included (upper bound is inclusive)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithRange_WithPagination() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };
		var product5 = new TestProduct
			{ ProductId = UniqueProductId(5), Name = "Product 5", Price = 50.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);
		_productCache.AddOrUpdate(product5, baseTs + 5000);

		// Act - query products updated between baseTs + 1500 and baseTs + 4500 with pagination
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, baseTs + 4500)
			.Execute(1, 2);

		// Assert - 3 products in range (2, 3, 4), paginated to 2 results
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.TotalCount, Is.EqualTo(3));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_Long_ReturnsProductsUpdatedAfterTimestamp() {
		// Arrange
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - use the generated UpdatedAfter method
		var results = _productCache.Query()
			.UpdatedAfter(baseTs + 1500)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_LongWithOutMax_ReturnsMaxTimestamp() {
		// Arrange
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act
		var results = _productCache.Query()
			.UpdatedAfter(baseTs + 500, out var max)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(max, Is.EqualTo(baseTs + 3000));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_LongRange_ReturnsProductsInRange() {
		// Arrange
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - range from baseTs + 1500 to baseTs + 3000 (inclusive)
		var results = _productCache.Query()
			.UpdatedAfter(baseTs + 1500, baseTs + 3000)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(2)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_DateTimeOffset_ReturnsProductsUpdatedAfterTime() {
		// Arrange
		var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
		var baseTs = baseTime.ToUnixTimeMilliseconds();

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 60000); // 1 minute later

		// Act
		var results = _productCache.Query()
			.UpdatedAfter(baseTime.AddSeconds(30))
			.Execute();

		// Assert - only product2 should be returned
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_DateTimeOffsetWithOutMax_ReturnsMaxTimestamp() {
		// Arrange
		var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
		var baseTs = baseTime.ToUnixTimeMilliseconds();

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);

		// Act
		var results = _productCache.Query()
			.UpdatedAfter(baseTime, out var max)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(max, Is.EqualTo(baseTs + 2000));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_DateTimeOffsetRange_ReturnsProductsInRange() {
		// Arrange
		var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
		var baseTs = baseTime.ToUnixTimeMilliseconds();

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 30000); // 30 seconds later
		_productCache.AddOrUpdate(product3, baseTs + 120000); // 2 minutes later

		// Act - range from 15 seconds to 1 minute after base
		var results = _productCache.Query()
			.UpdatedAfter(baseTime.AddSeconds(15), baseTime.AddMinutes(1))
			.Execute();

		// Assert - only product2 should be in range
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_CombinedWithOtherFilters() {
		// Arrange
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - UpdatedAfter combined with Where filter
		var results = _productCache.Query()
			.UpdatedAfter(baseTs + 1500)
			.Where(p => p.Category == "A")
			.Execute();

		// Assert - only product3 (updated after 1500 AND category A)
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
	}

	[Test]
	public void QueryBuilder_UpdatedAfter_CombinedWithKeyIndex() {
		// Arrange
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - UpdatedAfter combined with WithKey
		var results = _productCache.Query()
			.UpdatedAfter(baseTs + 500)
			.WithProductId(UniqueProductId(2))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_ReturnsMaxTimestamp() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated after baseTs + 1500 and get max timestamp
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, out var max)
			.Execute();

		// Assert - should return products 2, 3, 4 and max should be baseTs + 4000
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(max, Is.EqualTo(baseTs + 4000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_UsingGlobalIndexInterface() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - use the IDataCacheGlobalLastUpdateIndex interface overload with out max
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex, baseTs + 500, out var max)
			.Execute();

		// Assert - should return all 3 products and max should be baseTs + 3000
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(max, Is.EqualTo(baseTs + 3000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_ReturnsZeroWhenNoMatches() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);

		// Act - query products updated after baseTs + 5000 (none exist)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 5000, out var max)
			.Execute();

		// Assert - should return empty and max should be 0
		Assert.That(results, Is.Empty);
		Assert.That(max, Is.EqualTo(0));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_SingleMatch() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 5000);

		// Act - query products updated after baseTs + 2000 (only product 2)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 2000, out var max)
			.Execute();

		// Assert - should return product 2 and max should be baseTs + 5000
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(2)));
		Assert.That(max, Is.EqualTo(baseTs + 5000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_WorksWithWhereFilter() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query products updated after baseTs + 500 AND category = "A"
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 500, out var max)
			.Where(p => p.Category == "A")
			.Execute();

		// Assert - max should be from the index query (baseTs + 4000), not filtered results
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(1)));
		Assert.That(results.Select(p => p.ProductId), Does.Contain(UniqueProductId(3)));
		Assert.That(max, Is.EqualTo(baseTs + 4000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_IntersectsWithOtherIndexes() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - use LastUpdatedIndex first, then intersect with key index
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, out var max)
			.UseIndex(_productCache.Cache.KeyIndex, UniqueProductId(3))
			.Execute();

		// Assert - max should be from the first index query before intersection
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].ProductId, Is.EqualTo(UniqueProductId(3)));
		Assert.That(max, Is.EqualTo(baseTs + 4000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_AllMatchingProducts() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);

		// Act - query all products (updatedAfter = 0)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, 0, out var max)
			.Execute();

		// Assert - should return all 3 products and max should be baseTs + 3000
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(max, Is.EqualTo(baseTs + 3000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_WithPagination() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };
		var product4 = new TestProduct
			{ ProductId = UniqueProductId(4), Name = "Product 4", Price = 40.0m, Category = "B" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 3000);
		_productCache.AddOrUpdate(product4, baseTs + 4000);

		// Act - query with pagination
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs, out var max)
			.Execute(1, 2);

		// Assert - max should still be the overall max from the index
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.TotalCount, Is.EqualTo(4));
		Assert.That(max, Is.EqualTo(baseTs + 4000));
	}

	[Test]
	public void UseIndex_LastUpdatedIndex_WithOutMax_MaxIsFromMatchingItemsOnly() {
		// Arrange - use unique timestamps
		var baseTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _testBaseId * 10000L;

		var product1 = new TestProduct
			{ ProductId = UniqueProductId(1), Name = "Product 1", Price = 10.0m, Category = "A" };
		var product2 = new TestProduct
			{ ProductId = UniqueProductId(2), Name = "Product 2", Price = 20.0m, Category = "B" };
		var product3 = new TestProduct
			{ ProductId = UniqueProductId(3), Name = "Product 3", Price = 30.0m, Category = "A" };

		_productCache.AddOrUpdate(product1, baseTs + 1000);
		_productCache.AddOrUpdate(product2, baseTs + 2000);
		_productCache.AddOrUpdate(product3, baseTs + 5000);

		// Act - query products updated after baseTs + 1500 (products 2 and 3)
		var results = _productCache.Cache.Query()
			.UseIndex(_productLastUpdatedIndex.Index, baseTs + 1500, out var max)
			.Execute();

		// Assert - max should be baseTs + 5000 (from product 3, not product 1)
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(max, Is.EqualTo(baseTs + 5000));
	}
}
# endif
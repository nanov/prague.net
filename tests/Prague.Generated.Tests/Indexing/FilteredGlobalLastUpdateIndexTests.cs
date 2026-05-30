namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using Prague.Generated.Tests.Models;
using NUnit.Framework;

[TestFixture]
public class FilteredGlobalLastUpdateIndexTests {
	private DataCacheRegistry _registry = null!;
	private FilteredCatalogProductCache _cache = null!;
	private EnabledDepartmentIndex _departmentIndex = null!;
	private EnabledBrandIndex _brandIndex = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<FilteredCatalogProductCache>()
			.Build();
		_cache = _registry.GetCache<FilteredCatalogProductCache>();
		_departmentIndex = _registry.GetGlobalIndex<EnabledDepartmentIndex>();
		_brandIndex = _registry.GetGlobalIndex<EnabledBrandIndex>();
	}

	private static FilteredCatalogProduct Product(string id, int dept, bool enabled, long updatedAtMs = 0) =>
		new() { EventId = id, DepartmentId = dept, IsEnabled = enabled, BrandId = dept, UpdatedAtMs = updatedAtMs };

	[Test]
	public void Add_OnlyEnabledProductsAreCounted() {
		_cache.AddOrUpdate(Product("a", 5, enabled: true));
		_cache.AddOrUpdate(Product("b", 5, enabled: true));
		_cache.AddOrUpdate(Product("c", 5, enabled: false));

		Assert.That(_departmentIndex.GetEntitiesCount(5), Is.EqualTo(2));
	}

	[Test]
	public void Update_EnabledToDisabled_LeavesTheIndex() {
		_cache.AddOrUpdate(Product("a", 7, enabled: true));
		Assert.That(_departmentIndex.GetEntitiesCount(7), Is.EqualTo(1));

		_cache.AddOrUpdate(Product("a", 7, enabled: false));

		Assert.That(_departmentIndex.GetEntitiesCount(7), Is.EqualTo(0));
	}

	[Test]
	public void Update_DisabledToEnabled_EntersTheIndex() {
		_cache.AddOrUpdate(Product("a", 9, enabled: false));
		Assert.That(_departmentIndex.GetEntitiesCount(9), Is.EqualTo(0));

		_cache.AddOrUpdate(Product("a", 9, enabled: true));

		Assert.That(_departmentIndex.GetEntitiesCount(9), Is.EqualTo(1));
	}

	[Test]
	public void CustomTimestamp_MaxReflectsOnlyEnabledProducts() {
		_cache.AddOrUpdate(Product("a", 3, enabled: true, updatedAtMs: 1_000));
		_cache.AddOrUpdate(Product("b", 3, enabled: true, updatedAtMs: 2_000));
		// A disabled product with a much newer timestamp must NOT move the max.
		_cache.AddOrUpdate(Product("c", 3, enabled: false, updatedAtMs: 9_000));

		Assert.That(_brandIndex.TryGetMax(out var ts, out var brandId), Is.True);
		Assert.That(brandId, Is.EqualTo(3L));
		Assert.That(ts, Is.EqualTo(2_000));
	}

	[Test]
	public void TryGetMax_OnGeneratedIndex_ReflectsOnlyEnabledProducts() {
		_cache.AddOrUpdate(Product("a", 1, enabled: true, updatedAtMs: 5_000));
		// Disabled product with a newer timestamp must not count.
		_cache.AddOrUpdate(Product("b", 2, enabled: false, updatedAtMs: 9_000));

		Span<long> brands = [1L, 2L];

		Assert.That(_brandIndex.TryGetMax(brands, out var ts), Is.True);
		Assert.That(ts, Is.EqualTo(5_000), "only the enabled brand 1 (5000) counts; disabled brand 2 (9000) is excluded");
	}

	[Test]
	public void UpdatedAfter_OnKeyWithBothIndexes_UsesUnfilteredIndex() {
		// KeyedProduct's key property declares the FILTERED global index first, then the unfiltered one.
		// The keyless UpdatedAfter must be backed by the UNFILTERED index, so a disabled entity still appears.
		var registry = new DataCacheRegistryBuilder().Register<KeyedProductCache>().Build();
		var cache = registry.GetCache<KeyedProductCache>();

		cache.AddOrUpdate(new KeyedProduct { ProductId = 1, IsEnabled = true });
		cache.AddOrUpdate(new KeyedProduct { ProductId = 2, IsEnabled = false });

		var results = cache.Query().UpdatedAfter(0L).Execute();

		var sawEnabled = false;
		var sawDisabled = false;
		foreach (var p in results) {
			if (p.ProductId == 1) sawEnabled = true;
			if (p.ProductId == 2) sawDisabled = true;
		}

		Assert.That(sawEnabled, Is.True);
		Assert.That(sawDisabled, Is.True,
			"disabled product must appear: keyless UpdatedAfter is backed by the unfiltered index, not the filtered one declared first");
	}
}

// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[DataCache]
public partial class CatalogProduct {
	[DataCacheKey] public int Id { get; set; }

	public string HomeName { get; set; }
	public string AwayName { get; set; }
}

[DataCache]
public partial class ProductLiveObject {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int ProductId { get; set; }

	public long TimeElapsedMs { get; set; }

}

[DataCache]
public partial class ProductOffer {
	public int Price;

	[DataCacheKey]
	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int ProductId { get; set; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public int MakerGroup { get; set; }
}

[TestFixture]
public class BasicJoinTests {
	[Test]
	public void Basic_Test() {
		var registry = new DataCacheRegistryBuilder()
			.Register<CatalogProductCache>()
			.Register<ProductLiveObjectCache>()
			.Register<ProductOfferCache>()
			.Build();
		var productCache = registry.GetCache<CatalogProductCache>();
		var productLiveCache = registry.GetCache<ProductLiveObjectCache>();
		var productOfferCache = registry.GetCache<ProductOfferCache>();

		productCache.AddOrUpdate(new CatalogProduct { Id = 1, AwayName = "Acme", HomeName = "Globex" });
		productCache.AddOrUpdate(new CatalogProduct { Id = 2, AwayName = "Initech", HomeName = "Umbrella" });
		productCache.AddOrUpdate(new CatalogProduct { Id = 3, AwayName = "Tyrell", HomeName = "Hooli" });

		productLiveCache.AddOrUpdate(new ProductLiveObject { Id = 1, ProductId = 1, TimeElapsedMs = 10 });
		productLiveCache.AddOrUpdate(new ProductLiveObject { Id = 2, ProductId = 2, TimeElapsedMs = 20 });
		productLiveCache.AddOrUpdate(new ProductLiveObject { Id = 3, ProductId = 3, TimeElapsedMs = 30 });


		var uniquieId = 1;
		for (var i = 0; i < 4; i++)
		for (var j = 0; j < 10; j++)
			productOfferCache.AddOrUpdate(new ProductOffer
				{ Id = uniquieId++, ProductId = i + 1, Price = j * 10, MakerGroup = i + 1 });

		var b1 = productCache
			.Cache
			.Query()
			.JoinOne(productLiveCache.Cache, productLiveCache.ProductIdIndex);
		var b2 = b1.JoinMany(productOfferCache.Cache, productOfferCache.ProductIdIndex);
		var g = b2.Execute();

		Assert.That(g.Count, Is.EqualTo(3));
		// Assert.That(g[0].Right2.Count, Is.EqualTo(10));
	}

	[Test]
	public void Generated_RangeQuery_WithArgs_Gte_FiltersCorrectly() {
		var registry = new DataCacheRegistryBuilder().Register<ProductOfferCache>().Build();
		var productOfferCache = registry.GetCache<ProductOfferCache>();

		// Add offers with MakerGroup 1-5
		for (var i = 1; i <= 10; i++)
			productOfferCache.AddOrUpdate(new ProductOffer { Id = i, ProductId = 1, MakerGroup = i });

		// Act - Find offers with MakerGroup >= 5 using TArgs
		var args = 5;
		var result = productOfferCache.Query()
			.WithMakerGroup(static (q, a) => q.Gte(a), args)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(6), "Should find MakerGroups 5-10");
	}

	[Test]
	public void Generated_RangeQuery_WithArgs_Range_FiltersCorrectly() {
		var registry = new DataCacheRegistryBuilder().Register<ProductOfferCache>().Build();
		var productOfferCache = registry.GetCache<ProductOfferCache>();

		// Add offers with MakerGroup 1-10
		for (var i = 1; i <= 10; i++)
			productOfferCache.AddOrUpdate(new ProductOffer { Id = i, ProductId = 1, MakerGroup = i });

		// Act - Find offers with 3 <= MakerGroup <= 7 using TArgs with tuple
		var args = (Min: 3, Max: 7);
		var result = productOfferCache.Query()
			.WithMakerGroup(static (q, a) => q.Gte(a.Min).Lte(a.Max), args)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(5), "Should find MakerGroups 3-7");
	}

	[Test]
	public void Generated_RangeQuery_WithArgs_CombinedWithOtherIndex_IntersectsCorrectly() {
		var registry = new DataCacheRegistryBuilder().Register<ProductOfferCache>().Build();
		var productOfferCache = registry.GetCache<ProductOfferCache>();

		// Add offers for different products
		var id = 1;
		for (var gameId = 1; gameId <= 3; gameId++)
		for (var mg = 1; mg <= 5; mg++)
			productOfferCache.AddOrUpdate(new ProductOffer { Id = id++, ProductId = gameId, MakerGroup = mg });

		// Act - Find offers for ProductId=2 with MakerGroup >= 3 using TArgs
		var args = (ProductId: 2, MinMakerGroup: 3);
		var result = productOfferCache.Query()
			.WithProductId(args.ProductId)
			.WithMakerGroup(static (q, a) => q.Gte(a.MinMakerGroup), args)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(3), "Should find 3 offers (MakerGroups 3,4,5 for ProductId 2)");
		Assert.That(result.All(m => m.ProductId == 2), Is.True);
		Assert.That(result.All(m => m.MakerGroup >= 3), Is.True);
	}
}

#endif

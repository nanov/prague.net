namespace Prague.Generated.Tests.Join;
#if false
// Test models for join functionality
[DataCache]
public partial class Brand {
	[DataCacheKey] public int BrandId { get; set; }

	public string Name { get; set; } = "";
	public string Country { get; set; } = "";
}

[DataCache]
public partial class Product {
	[DataCacheKey] public int ProductId { get; set; }

	public string Name { get; set; } = "";

	[DataCacheForeignKey<Brand>]
	[DataCacheIndex(DataCacheIndexType.Many)]
	public int BrandId { get; set; }

	public DateTime StartTime { get; set; }
}

[DataCache]
public partial class Offer {
	[DataCacheKey] public int OfferId { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int ProductId { get; set; }

	public int BrandId { get; set; } // Added for left-key indexing in chained joins

	public string Name { get; set; } = "";
	public decimal Price { get; set; }
}

public class JoinTests {
	[Test]
	public void JoinOne_Raw_Test() {
		var brandCache = new InMemoryDataCache<int, Brand>(); // new BrandCache();
		var productCache = new InMemoryDataCache<int, Product>(); // new ProductCache();
		var productCacheBrandIndex = productCache.CacheKeyValueListIndex((key, product) => product.BrandId);

		var brand = new Brand { BrandId = 1, Name = "Globex Line", Country = "Northland" };
		var brand2 = new Brand { BrandId = 2, Name = "B Series", Country = "Northland" };
		brandCache.AddOrUpdate(brand.BrandId, brand);
		brandCache.AddOrUpdate(brand2.BrandId, brand2);
		var product1 = new Product { ProductId = 1, Name = "Acme Widget", BrandId = 1, StartTime = DateTime.Now };
		var product2 = new Product { ProductId = 2, Name = "Soylent Gadget", BrandId = 1, StartTime = DateTime.Now };
		productCache.AddOrUpdate(product1.ProductId, product1);
		productCache.AddOrUpdate(product2.ProductId, product2);
		var l = brandCache.Query().JoinMany(productCache, productCacheBrandIndex).Execute();
		;
	}

	#region Chained Join Tests

	[Test]
	public void JoinManyMany_ChainedJoin_ShouldWork() {
		// Arrange: Brand -> Products -> Offers (Many -> Many)
		// NEW DESIGN: All indexes reference the LEFT entity's key (BrandId)
		var brandCache = new InMemoryDataCache<int, Brand>();
		var productCache = new InMemoryDataCache<int, Product>();
		var offerCache = new InMemoryDataCache<int, Offer>();

		var productCacheBrandIndex = productCache.CacheKeyValueListIndex((key, product) => product.BrandId);
		// Offers are indexed by BrandId (the left key), not ProductId
		var offerCacheBrandIndex = offerCache.CacheKeyValueListIndex((key, offer) => offer.BrandId);

		// Add test data
		var brand = new Brand { BrandId = 1, Name = "Globex Line", Country = "Northland" };
		brandCache.AddOrUpdate(brand.BrandId, brand);

		var product1 = new Product { ProductId = 1, Name = "Acme Widget", BrandId = 1, StartTime = DateTime.Now };
		var product2 = new Product { ProductId = 2, Name = "Soylent Gadget", BrandId = 1, StartTime = DateTime.Now };
		productCache.AddOrUpdate(product1.ProductId, product1);
		productCache.AddOrUpdate(product2.ProductId, product2);

		// Offers now have BrandId to enable indexing by left key
		var offer1 = new Offer { OfferId = 1, ProductId = 1, BrandId = 1, Name = "1X2", Price = 1.5m };
		var offer2 = new Offer { OfferId = 2, ProductId = 1, BrandId = 1, Name = "Bundle", Price = 1.8m };
		var offer3 = new Offer { OfferId = 3, ProductId = 2, BrandId = 1, Name = "1X2", Price = 2.0m };
		offerCache.AddOrUpdate(offer1.OfferId, offer1);
		offerCache.AddOrUpdate(offer2.OfferId, offer2);
		offerCache.AddOrUpdate(offer3.OfferId, offer3);


		// Act: Chain JoinMany -> JoinMany (both indexed by BrandId)
		var results = brandCache.Query()
			.JoinMany(productCache, productCacheBrandIndex)
			.JoinMany(offerCache, offerCacheBrandIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		var (leftValue, products, offers) = results[0];
		Assert.That(leftValue.BrandId, Is.EqualTo(1));
		Assert.That(products, Has.Count.EqualTo(2));
		Assert.That(offers, Has.Count.EqualTo(3));
	}

	[Test]
	public void JoinOneOne_ChainedJoin_ShouldWork() {
		// Arrange: User -> Profile -> Settings
		// NEW DESIGN: All indexes reference the LEFT entity's key (UserId)
		var userCache = new InMemoryDataCache<int, TestUser>();
		var profileCache = new InMemoryDataCache<int, TestProfile>();
		var settingsCache = new InMemoryDataCache<int, TestSettings>();

		var profileByUserIndex = profileCache.AddKeyValueIndex((key, profile) => profile.UserId);
		// Settings indexed by UserId (the left key), not ProfileId
		var settingsByUserIndex = settingsCache.AddKeyValueIndex((key, settings) => settings.UserId);

		// Add test data
		var user = new TestUser { UserId = 1, Name = "John" };
		userCache.AddOrUpdate(user.UserId, user);

		var profile = new TestProfile { ProfileId = 101, UserId = 1, Bio = "Developer" };
		profileCache.AddOrUpdate(profile.ProfileId, profile);

		// Settings now has UserId to enable indexing by left key
		var settings = new TestSettings { SettingsId = 1001, ProfileId = 101, UserId = 1, Theme = "Dark" };
		settingsCache.AddOrUpdate(settings.SettingsId, settings);

		// Act: Chain JoinOne -> JoinOne (both indexed by UserId)
		var results = userCache.Query()
			.JoinOne(profileCache, profileByUserIndex)
			.JoinOne(settingsCache, settingsByUserIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		var (userResult, profileResult, settingsResult) = results[0];
		Assert.That(userResult.UserId, Is.EqualTo(1));
		Assert.That(profileResult, Is.Not.Null);
		Assert.That(profileResult!.ProfileId, Is.EqualTo(101));
		Assert.That(settingsResult, Is.Not.Null);
		Assert.That(settingsResult!.Theme, Is.EqualTo("Dark"));
	}

	[Test]
	public void JoinOneMany_ChainedJoin_ShouldWork() {
		// Arrange: User -> Profile (One) -> Posts (Many)
		// NEW DESIGN: All indexes reference the LEFT entity's key (UserId)
		var userCache = new InMemoryDataCache<int, TestUser>();
		var profileCache = new InMemoryDataCache<int, TestProfile>();
		var postCache = new InMemoryDataCache<int, TestPost>();

		var profileByUserIndex = profileCache.AddKeyValueIndex((key, profile) => profile.UserId);
		// Posts indexed by UserId (the left key), not ProfileId
		var postsByUserIndex = postCache.CacheKeyValueListIndex((key, post) => post.UserId);

		// Add test data
		var user = new TestUser { UserId = 1, Name = "John" };
		userCache.AddOrUpdate(user.UserId, user);

		var profile = new TestProfile { ProfileId = 101, UserId = 1, Bio = "Developer" };
		profileCache.AddOrUpdate(profile.ProfileId, profile);

		// Posts now have UserId to enable indexing by left key
		var post1 = new TestPost { PostId = 1, ProfileId = 101, UserId = 1, Title = "First Post" };
		var post2 = new TestPost { PostId = 2, ProfileId = 101, UserId = 1, Title = "Second Post" };
		postCache.AddOrUpdate(post1.PostId, post1);
		postCache.AddOrUpdate(post2.PostId, post2);

		// Act: Chain JoinOne -> JoinMany (both indexed by UserId)
		var results = userCache.Query()
			.JoinOne(profileCache, profileByUserIndex)
			.JoinMany(postCache, postsByUserIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		var (userResult, profileResult, posts) = results[0];
		Assert.That(userResult.UserId, Is.EqualTo(1));
		Assert.That(profileResult, Is.Not.Null);
		Assert.That(profileResult!.ProfileId, Is.EqualTo(101));
		Assert.That(posts, Has.Count.EqualTo(2));
	}

	[Test]
	public void JoinManyOne_ChainedJoin_ShouldWork() {
		// Arrange: Brand -> Products (Many) -> Warehouse (One)
		// NEW DESIGN: All indexes reference the LEFT entity's key (BrandId)
		var brandCache = new InMemoryDataCache<int, Brand>();
		var productCache = new InMemoryDataCache<int, Product>();
		var warehouseCache = new InMemoryDataCache<int, TestWarehouse>();

		var productsByBrandIndex = productCache.CacheKeyValueListIndex((key, product) => product.BrandId);
		// Warehouse indexed by BrandId (the left key), not ProductId
		var warehouseByBrandIndex = warehouseCache.AddKeyValueIndex((key, warehouse) => warehouse.BrandId);

		// Add test data
		var brand = new Brand { BrandId = 1, Name = "Globex Line", Country = "Northland" };
		brandCache.AddOrUpdate(brand.BrandId, brand);

		var product1 = new Product { ProductId = 1, Name = "Acme Widget", BrandId = 1, StartTime = DateTime.Now };
		var product2 = new Product { ProductId = 2, Name = "Soylent Gadget", BrandId = 1, StartTime = DateTime.Now };
		productCache.AddOrUpdate(product1.ProductId, product1);
		productCache.AddOrUpdate(product2.ProductId, product2);

		// Warehouse now has BrandId to enable indexing by left key (one warehouse per brand)
		var warehouse1 = new TestWarehouse { WarehouseId = 1, ProductId = 1, BrandId = 1, Name = "Harbor Depot" };
		warehouseCache.AddOrUpdate(warehouse1.WarehouseId, warehouse1);

		// Act: Chain JoinMany -> JoinOne (both indexed by BrandId)
		var results = brandCache.Query()
			.JoinMany(productCache, productsByBrandIndex)
			.JoinOne(warehouseCache, warehouseByBrandIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		var (brandResult, products, warehouse) = results[0];
		Assert.That(brandResult.BrandId, Is.EqualTo(1));
		Assert.That(products, Has.Count.EqualTo(2));
		Assert.That(warehouse, Is.Not.Null);
	}

	[Test]
	public void JoinOneOneOne_TripleChainedJoin_ShouldWork() {
		// Arrange: User -> Profile -> Settings -> Preferences (3 one-to-one joins)
		// NEW DESIGN: All indexes reference the LEFT entity's key (UserId)
		var userCache = new InMemoryDataCache<int, TestUser>();
		var profileCache = new InMemoryDataCache<int, TestProfile>();
		var settingsCache = new InMemoryDataCache<int, TestSettings>();
		var preferencesCache = new InMemoryDataCache<int, TestPreferences>();

		var profileByUserIndex = profileCache.AddKeyValueIndex((key, profile) => profile.UserId);
		var settingsByUserIndex = settingsCache.AddKeyValueIndex((key, settings) => settings.UserId);
		var preferencesByUserIndex = preferencesCache.AddKeyValueIndex((key, prefs) => prefs.UserId);

		// Add test data
		var user = new TestUser { UserId = 1, Name = "John" };
		userCache.AddOrUpdate(user.UserId, user);

		var profile = new TestProfile { ProfileId = 101, UserId = 1, Bio = "Developer" };
		profileCache.AddOrUpdate(profile.ProfileId, profile);

		// Settings has UserId
		var settings = new TestSettings { SettingsId = 1001, ProfileId = 101, UserId = 1, Theme = "Dark" };
		settingsCache.AddOrUpdate(settings.SettingsId, settings);

		// Preferences has UserId
		var preferences = new TestPreferences { PreferencesId = 10001, SettingsId = 1001, UserId = 1, Language = "EN" };
		preferencesCache.AddOrUpdate(preferences.PreferencesId, preferences);

		// Act: Chain JoinOne -> JoinOne -> JoinOne (all indexed by UserId)
		var results = userCache.Query()
			.JoinOne(profileCache, profileByUserIndex)
			.JoinOne(settingsCache, settingsByUserIndex)
			.JoinOne(preferencesCache, preferencesByUserIndex)
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		var (userResult, profileResult, settingsResult, prefsResult) = results[0];
		Assert.That(userResult.UserId, Is.EqualTo(1));
		Assert.That(profileResult, Is.Not.Null);
		Assert.That(settingsResult, Is.Not.Null);
		Assert.That(prefsResult, Is.Not.Null);
		Assert.That(prefsResult!.Language, Is.EqualTo("EN"));
	}

	#endregion
}

#region Additional Test Models for Chained Joins

[DataCache]
public partial class TestUser {
	[DataCacheKey] public int UserId { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestProfile {
	[DataCacheKey] public int ProfileId { get; set; }

	public int UserId { get; set; }
	public string Bio { get; set; } = "";
}

[DataCache]
public partial class TestSettings {
	[DataCacheKey] public int SettingsId { get; set; }

	public int ProfileId { get; set; }
	public int UserId { get; set; } // Added for left-key indexing in chained joins
	public string Theme { get; set; } = "";
}

[DataCache]
public partial class TestPreferences {
	[DataCacheKey] public int PreferencesId { get; set; }

	public int SettingsId { get; set; }
	public int UserId { get; set; } // Added for left-key indexing in chained joins
	public string Language { get; set; } = "";
}

[DataCache]
public partial class TestPost {
	[DataCacheKey] public int PostId { get; set; }

	public int ProfileId { get; set; }
	public int UserId { get; set; } // Added for left-key indexing in chained joins
	public string Title { get; set; } = "";
}

[DataCache]
public partial class TestWarehouse {
	[DataCacheKey] public int WarehouseId { get; set; }

	public int ProductId { get; set; }
	public int BrandId { get; set; } // Added for left-key indexing in chained joins
	public string Name { get; set; } = "";
}

#endregion
#endif
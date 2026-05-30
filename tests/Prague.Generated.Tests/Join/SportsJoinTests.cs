// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class CatalogJoinTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CatalogCategoryCache>()
			.Register<CatalogBrandCache>()
			.Register<CatalogListingCache>()
			.Register<CatalogListingInfoCache>()
			.Register<CatalogOfferCache>()
			.Build();
		_categoryCache = _registry.GetCache<CatalogCategoryCache>();
		_brandCache = _registry.GetCache<CatalogBrandCache>();
		_listingCache = _registry.GetCache<CatalogListingCache>();
		_listingInfoCache = _registry.GetCache<CatalogListingInfoCache>();
		_offerCache = _registry.GetCache<CatalogOfferCache>();
	}

	private DataCacheRegistry _registry = null!;
	private CatalogCategoryCache _categoryCache = null!;
	private CatalogBrandCache _brandCache = null!;
	private CatalogListingCache _listingCache = null!;
	private CatalogListingInfoCache _listingInfoCache = null!;
	private CatalogOfferCache _offerCache = null!;

	[Test]
	public void JoinWithCatalogBrand_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ESP" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 1, Name = "Standard Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 3, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });

		// Act
		var builder = _categoryCache.Query()
			.JoinWithCatalogBrand();
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));

		var england = results.FirstOrDefault(r => r.Left.Name == "Northland");
		Assert.That(england, Is.Not.Null);
		Assert.That(england.Right.Count, Is.EqualTo(2)); // Globex Line + Standard Line

		var spain = results.FirstOrDefault(r => r.Left.Name == "Eastland");
		Assert.That(spain, Is.Not.Null);
		Assert.That(spain.Right.Count, Is.EqualTo(1)); // Initech Line
	}

	[Test]
	public void JoinWithCatalogListing_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });
		_listingCache.AddOrUpdate(new CatalogListing {
			Id = 2, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Soylent", Supplier = "Hooli", Status = "Draft"
		});

		// Act
		var builder = _categoryCache.Query()
			.JoinWithCatalogListing();
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(2));
	}

	[Test]
	public void JoinWithCatalogListingInfo_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo {
			Id = 1, CatalogCategoryId = 1, ListingId = 1, Warehouse = "Central Depot", Inspector = "Pat Inspector",
			Attendance = 60000
		});

		// Act
		var builder = _categoryCache.Query()
			.JoinWithCatalogListingInfo();
		var results = builder.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right, Is.Not.Null);
		Assert.That(results[0].Right!.Warehouse, Is.EqualTo("Central Depot"));
	}

	[Test]
	public void Chained_2Level_Brand_Listing_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 1, Name = "Standard Line", Season = 2024 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });
		_listingCache.AddOrUpdate(new CatalogListing {
			Id = 2, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Soylent", Supplier = "Hooli", Status = "Draft"
		});

		// Act - 2-level chain: CatalogCategory -> CatalogBrand (Many) -> CatalogListing (Many)
		var b1 = _categoryCache.Query()
			.JoinWithCatalogBrand();
		var b2 = b1.JoinWithCatalogListing();
		var results = b2.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
		Assert.That(results[0].Right.Count, Is.EqualTo(2)); // 2 brands
		Assert.That(results[0].Right2.Count, Is.EqualTo(2)); // 2 listings
	}

	[Test]
	public void Chained_3Level_Brand_Listing_ListingInfo_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });
		_listingCache.AddOrUpdate(new CatalogListing {
			Id = 2, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Soylent", Supplier = "Hooli", Status = "Draft"
		});

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo {
			Id = 1, CatalogCategoryId = 1, ListingId = 1, Warehouse = "Central Depot", Inspector = "Pat Inspector",
			Attendance = 60000
		});

		var b1 = _categoryCache.Query()
			.JoinWithCatalogBrand();
		var b2 = b1.JoinWithCatalogListing();
		var b3 = b2.JoinWithCatalogListingInfo();
		var results = b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
		Assert.That(results[0].Right.Count, Is.EqualTo(1)); // 1 brand
		Assert.That(results[0].Right2.Count, Is.EqualTo(2)); // 2 listings
		Assert.That(results[0].Right3, Is.Not.Null); // CatalogListingInfo
		Assert.That(results[0].Right3!.Warehouse, Is.EqualTo("Central Depot"));
	}

	[Test]
	public void Chained_4Level_Brand_Listing_ListingInfo_Offer_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo {
			Id = 1, CatalogCategoryId = 1, ListingId = 1, Warehouse = "Central Depot", Inspector = "Pat Inspector",
			Attendance = 60000
		});

		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 1, CatalogCategoryId = 1, ListingId = 1, OfferName = "Top Offer", Price = 1.85m, IsActive = true });
		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 2, CatalogCategoryId = 1, ListingId = 1, OfferName = "Bundle Offer", Price = 1.75m, IsActive = true });
		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 3, CatalogCategoryId = 1, ListingId = 1, OfferName = "Combo Offer", Price = 1.90m, IsActive = false });

		// Act - 4-level chain: CatalogCategory -> CatalogBrand -> CatalogListing -> CatalogListingInfo -> CatalogOffer
		var b1 = _categoryCache.Query()
			.JoinWithCatalogBrand();
		var b2 = b1.JoinWithCatalogListing();
		var b3 = b2.JoinWithCatalogListingInfo();
		var b4 = b3.JoinWithCatalogOffer();
		var results = b4.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Northland"));
		Assert.That(results[0].Right.Count, Is.EqualTo(1)); // 1 brand
		Assert.That(results[0].Right2.Count, Is.EqualTo(1)); // 1 listing
		Assert.That(results[0].Right3, Is.Not.Null); // CatalogListingInfo
		Assert.That(results[0].Right4.Count, Is.EqualTo(3)); // 3 offers
	}

	[Test]
	public void Chained_4Level_WithFilters_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 1, Name = "Standard Line", Season = 2023 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });
		_listingCache.AddOrUpdate(new CatalogListing {
			Id = 2, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Soylent", Supplier = "Hooli", Status = "Archived"
		});

		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 1, CatalogCategoryId = 1, ListingId = 1, OfferName = "Top Offer", Price = 1.85m, IsActive = true });
		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 2, CatalogCategoryId = 1, ListingId = 1, OfferName = "Bundle Offer", Price = 1.75m, IsActive = false });

		// Act - With filters
		var b1 = _categoryCache.Query()
			.JoinWithCatalogBrand(q => q.Where(l => l.Season == 2024)); // Only 2024 brands
		var b2 = b1.JoinWithCatalogListing(q => q.Where(g => g.Status == "Active")); // Only active listings
		var b3 = b2.JoinWithCatalogOffer(q => q.Where(m => m.IsActive)); // Only active offers
		var results = b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(1)); // Only Globex Line (2024)
		Assert.That(results[0].Right[0].Name, Is.EqualTo("Globex Line"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(1)); // Only Active listing
		Assert.That(results[0].Right2[0].Manufacturer, Is.EqualTo("Acme"));
		Assert.That(results[0].Right3.Count, Is.EqualTo(1)); // Only active offer
		Assert.That(results[0].Right3[0].OfferName, Is.EqualTo("Top Offer"));
	}

	[Test]
	public void Chained_DifferentOrder_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });

		_listingInfoCache.AddOrUpdate(new CatalogListingInfo {
			Id = 1, CatalogCategoryId = 1, ListingId = 1, Warehouse = "Central Depot", Inspector = "Pat Inspector",
			Attendance = 60000
		});

		_offerCache.AddOrUpdate(new CatalogOffer
			{ Id = 1, CatalogCategoryId = 1, ListingId = 1, OfferName = "Top Offer", Price = 1.85m, IsActive = true });

		// Act - Different order: CatalogListingInfo -> CatalogBrand -> CatalogOffer -> CatalogListing
		var b1 = _categoryCache.Query()
			.JoinWithCatalogListingInfo();
		var b2 = b1.JoinWithCatalogBrand();
		var b3 = b2.JoinWithCatalogOffer();
		var b4 = b3.JoinWithCatalogListing();
		var results = b4.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right, Is.Not.Null); // CatalogListingInfo (One)
		Assert.That(results[0].Right2.Count, Is.EqualTo(1)); // CatalogBrand (Many)
		Assert.That(results[0].Right3.Count, Is.EqualTo(1)); // CatalogOffer (Many)
		Assert.That(results[0].Right4.Count, Is.EqualTo(1)); // CatalogListing (Many)
	}

	// [Test]
	// public void MultipleCategories_WithJoins_Works() {
	// 	// Arrange
	// 	_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });
	// 	_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ESP" });
	// 	_categoryCache.AddOrUpdate(new CatalogCategory { Id = 3, Name = "Garden", Code = "GER" });
	//
	// 	_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
	// 	_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });
	// 	_brandCache.AddOrUpdate(new CatalogBrand { Id = 3, CatalogCategoryId = 3, Name = "Vandelay Line", Season = 2024 });
	//
	// 	_brandCache.Query().JoinOne(_brandCache.CatalogCategoryIdIndex.Re, , )
	//
	//
	// 	// Act
	// 	var b1 = _categoryCache.Query()
	// 		.JoinWithCatalogBrand();
	// 	var b2 = b1.JoinWithCatalogListing();
	// 	var results = b2.Execute();
	// }


	[Test]
	public void MultipleCategories_WithJoins_Works() {
		// Arrange
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 1, Name = "Northland", Code = "ENG" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 2, Name = "Eastland", Code = "ESP" });
		_categoryCache.AddOrUpdate(new CatalogCategory { Id = 3, Name = "Garden", Code = "GER" });

		_brandCache.AddOrUpdate(new CatalogBrand { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 2, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });
		_brandCache.AddOrUpdate(new CatalogBrand { Id = 3, CatalogCategoryId = 3, Name = "Vandelay Line", Season = 2024 });

		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 1, CatalogCategoryId = 1, BrandId = 1, Manufacturer = "Acme", Supplier = "Wonka", Status = "Active" });
		_listingCache.AddOrUpdate(new CatalogListing
			{ Id = 2, CatalogCategoryId = 2, BrandId = 2, Manufacturer = "Initech", Supplier = "Acme", Status = "Active" });

		// Act
		var b1 = _categoryCache.Query()
			.JoinWithCatalogBrand();
		var b2 = b1.JoinWithCatalogListing();
		var results = b2.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(3));

		var england = results.FirstOrDefault(r => r.Left.Name == "Northland");
		Assert.That(england, Is.Not.Null);
		Assert.That(england.Right.Count, Is.EqualTo(1)); // Globex Line
		Assert.That(england.Right2.Count, Is.EqualTo(1)); // 1 listing

		var spain = results.FirstOrDefault(r => r.Left.Name == "Eastland");
		Assert.That(spain, Is.Not.Null);
		Assert.That(spain.Right.Count, Is.EqualTo(1)); // Initech Line
		Assert.That(spain.Right2.Count, Is.EqualTo(1)); // 1 listing

		var germany = results.FirstOrDefault(r => r.Left.Name == "Garden");
		Assert.That(germany, Is.Not.Null);
		Assert.That(germany.Right.Count, Is.EqualTo(1)); // Vandelay Line
		Assert.That(germany.Right2.Count, Is.EqualTo(0)); // No listings
	}
}

#endif

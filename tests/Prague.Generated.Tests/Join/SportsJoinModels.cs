namespace Prague.Generated.Tests.Join;

using Prague.Core;

[DataCache]
public partial class CatalogCategory {
	[DataCacheKey] public int Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string Code { get; set; } = string.Empty;
}

[DataCache]
public partial class CatalogBrandTier {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<CatalogCategory>(DataCacheJoinType.OneToMany)]
	public int CatalogBrandId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Season { get; set; }
}


[DataCache]
public partial class CatalogBrand {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<CatalogCategory>(DataCacheJoinType.OneToMany)]
	public int CatalogCategoryId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Season { get; set; }
}

[DataCache]
public partial class CatalogListing {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<CatalogCategory>(DataCacheJoinType.OneToMany)]
	public int CatalogCategoryId { get; set; }

	public int BrandId { get; set; } // Not a FK for join purposes

	public string Manufacturer { get; set; } = string.Empty;

	public string Supplier { get; set; } = string.Empty;

	public string Status { get; set; } = string.Empty;
}

[DataCache]
public partial class CatalogListingInfo {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<CatalogCategory>(DataCacheJoinType.OneToOne)]
	public int CatalogCategoryId { get; set; }

	public int ListingId { get; set; } // Not a FK for join purposes

	public string Warehouse { get; set; } = string.Empty;

	public string Inspector { get; set; } = string.Empty;

	public int Attendance { get; set; }
}

[DataCache]
public partial class CatalogOffer {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<CatalogCategory>(DataCacheJoinType.OneToMany)]
	public int CatalogCategoryId { get; set; }

	public int ListingId { get; set; } // Not a FK for join purposes

	public string OfferName { get; set; } = string.Empty;

	public decimal Price { get; set; }

	public bool IsActive { get; set; }
}
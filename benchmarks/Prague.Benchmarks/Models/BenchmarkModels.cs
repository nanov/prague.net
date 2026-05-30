namespace Prague.Benchmarks.Models;

using Core;

/// <summary>
///   Represents a product/listing entity - the root of our join hierarchy.
/// </summary>
[DataCache]
public partial class BenchmarkProduct {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public int Range { get; set; }

	public string Manufacturer { get; set; } = string.Empty;
	public string Supplier { get; set; } = string.Empty;
	public string Brand { get; set; } = string.Empty;
	public string Category { get; set; } = string.Empty;
	public DateTime StartTime { get; set; }
	public string Status { get; set; } = string.Empty;
	public int PrimaryValue { get; set; }
	public int SecondaryValue { get; set; }
	public bool IsPublished { get; set; }
	public string DepartmentType { get; set; } = string.Empty;
	public int Priority { get; set; }
	public long LastUpdated { get; set; }
}

/// <summary>
///   Represents additional product information - one-to-one relationship with Product.
/// </summary>
[DataCache]
public partial class BenchmarkProductInfo {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<BenchmarkProduct>(DataCacheJoinType.OneToOne)]
	public int ProductId { get; set; }

	public string Warehouse { get; set; } = string.Empty;
	public string Inspector { get; set; } = string.Empty;
	public int StockCount { get; set; }
	public string Packaging { get; set; } = string.Empty;
	public string Dimensions { get; set; } = string.Empty;
	public string ShippingCarrier { get; set; } = string.Empty;
	public int LeadTimeDays { get; set; }
	public string Section { get; set; } = string.Empty;
	public string PrimaryColor { get; set; } = string.Empty;
	public string SecondaryColor { get; set; } = string.Empty;
	public int PrimaryWeight { get; set; }
	public int SecondaryWeight { get; set; }
	public int PrimaryWarrantyMonths { get; set; }
	public int SecondaryWarrantyMonths { get; set; }
	public int PrimaryReturns { get; set; }
	public int SecondaryReturns { get; set; }
	public long LastUpdated { get; set; }
}

/// <summary>
///   Represents an offer for a product - one-to-many relationship with Product.
/// </summary>
[DataCache]
public partial class BenchmarkOffer {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<BenchmarkProduct>(DataCacheJoinType.OneToMany)]
	public int ProductId { get; set; }

	public string OfferName { get; set; } = string.Empty;
	public string OfferType { get; set; } = string.Empty;
	public decimal BasePrice { get; set; }
	public decimal ListPrice { get; set; }
	public decimal SalePrice { get; set; }
	public decimal Tier { get; set; }
	public bool IsActive { get; set; }
	public bool IsSuspended { get; set; }
	public int DisplayOrder { get; set; }
	public string Category { get; set; } = string.Empty;
	public DateTime OpenTime { get; set; }
	public DateTime? CloseTime { get; set; }
	public decimal MaxQuantity { get; set; }
	public decimal MinQuantity { get; set; }
	public long LastUpdated { get; set; }
}

namespace Prague.Generated.Tests.Fixtures.Entities;

using Prague.Core;
using Enums;

/// <summary>
///   Denormalized catalog document for efficient product search with full static data
/// </summary>
[DataCache]
[DataCacheSort("ByDateAsc", "ReleaseDate:1", "Id:1")]
[DataCacheSort("ByDateDesc", "ReleaseDate:-1", "Id:-1")]
public partial class ProductListing {
	[DataCacheKey] public string CompositeId { get; set; } // Format: {ProductId}_{Channel}

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public long Id { get; set; } // Same as the catalog product Id

	[DataCacheIndex(DataCacheIndexType.Many)]
	public SalesChannel ChannelId { get; set; } = SalesChannel.Web; // Web

	// Source information
	public byte SourceId { get; set; }
	public long SourceKey { get; set; }

	// Listing basic info
	public string ProductName { get; set; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public DateTime ReleaseDate { get; set; }

	public long? Timestamp { get; set; }

	// Department hierarchy with full branded data - FLATTENED for object pooling
	// Department
	[DataCacheIndex(DataCacheIndexType.Many)]
	public long DepartmentId { get; set; }

	public string DepartmentName { get; set; }
	public string? DepartmentDisplayName { get; set; }
	public int? DepartmentDisplayOrder { get; set; }
	public bool DepartmentDisabled { get; set; }

	// Category
	[DataCacheIndex(DataCacheIndexType.Many)]
	public long CategoryId { get; set; }

	public string CategoryName { get; set; }
	public string? CategoryDisplayName { get; set; }
	public int? CategoryDisplayOrder { get; set; }
	public bool CategoryDisabled { get; set; }

	// Brand
	[DataCacheIndex(DataCacheIndexType.Many)]
	public long BrandId { get; set; }

	public string BrandName { get; set; }
	public string? BrandDisplayName { get; set; }
	public int? BrandDisplayOrder { get; set; }
	public bool BrandDisabled { get; set; }

	// Makers
	public long? ManufacturerId { get; set; }
	public string? ManufacturerName { get; set; }
	public string? ManufacturerDisplayName { get; set; }

	public long? SupplierId { get; set; }
	public string? SupplierName { get; set; }
	public string? SupplierDisplayName { get; set; }

	// Status information
	[DataCacheIndex(DataCacheIndexType.Many)]
	public ListingStatus ListingStatus { get; set; } // Draft, Active, Archived, etc.

	public ListingStatus? ListingStatusManual { get; set; } // Manual override status
	public ListingStatus? ListingStatusModeration { get; set; } // Moderation override status
	public ModerationAction ModerationAction { get; set; } = ModerationAction.None;

	[DataCacheIndex(DataCacheIndexType.Many)]
	public StockStatus StockStatus { get; set; } // InStock, Reserved, etc.

	[DataCacheIndex(DataCacheIndexType.Many)]
	public bool IsFeatured { get; set; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public int FeaturedOrder { get; set; }

	// Variant information
	[DataCacheIndex(DataCacheIndexType.Range)]
	public int ActiveVariantCount { get; set; }

	public int TotalVariantCount { get; set; }

	public DateTime IndexedAt { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int ListingTypeId { get; set; }

	public int? CatalogId { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public bool IsPublished { get; set; }

	public string? StatusNote { get; set; }


	public bool? IsPinned { get; set; }
	public bool HasPendingReviews { get; set; }
	public bool IsManuallyEdited { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public bool HasDiscount { get; set; }

	public bool HasVariants { get; set; }
}
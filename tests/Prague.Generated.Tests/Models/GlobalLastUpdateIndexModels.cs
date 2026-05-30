namespace Prague.Generated.Tests.Models;

using Prague.Core;

/// <summary>
///   A partial class that will be completed by the source generator to implement
///   a global last update index tracking by DepartmentId.
/// </summary>
public partial class CatalogLastUpdatedIndex : IDataCacheGlobalLastUpdateIndex<int> {
}

/// <summary>
///   A partial class that will be completed by the source generator to implement
///   a global last update index tracking by BrandId.
/// </summary>
public partial class BrandLastUpdatedIndex : IDataCacheGlobalLastUpdateIndex<long> {
}

/// <summary>
///   Test entity for global last update index - a catalog product.
/// </summary>
[DataCache]
public partial class TestCatalogProduct {
	[DataCacheKey] public string EventId { get; set; } = "";

	public string Name { get; set; } = "";

	/// <summary>
	///   Department ID - will be tracked by CatalogLastUpdatedIndex.
	///   Uses timestamp from AddOrUpdate method.
	/// </summary>
	[DataCacheGlobalLastUpdateIndex<CatalogLastUpdatedIndex>]
	public int DepartmentId { get; set; }

	/// <summary>
	///   Brand ID - will be tracked by BrandLastUpdatedIndex.
	///   Uses the UpdatedAt property as timestamp.
	/// </summary>
	[DataCacheGlobalLastUpdateIndex<BrandLastUpdatedIndex>(nameof(UpdatedAt))]
	public long BrandId { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
///   Another entity that shares the same CatalogLastUpdatedIndex.
///   This tests that multiple caches can share the same global index.
/// </summary>
[DataCache]
public partial class TestCatalogMaker {
	[DataCacheKey] public string MakerId { get; set; } = "";

	public string MakerName { get; set; } = "";

	/// <summary>
	///   Department ID - shares the same CatalogLastUpdatedIndex as TestCatalogProduct.
	/// </summary>
	[DataCacheGlobalLastUpdateIndex<CatalogLastUpdatedIndex>]
	public int DepartmentId { get; set; }
}

public partial class ProductLastUpdatedIndex : IDataCacheGlobalLastUpdateIndex<int> {
}

[DataCache]
public partial class TestProduct {
	/// <summary>
	///   Primary key - also tracked by ProductLastUpdatedIndex.
	/// </summary>
	[DataCacheKey]
	[DataCacheGlobalLastUpdateIndex<ProductLastUpdatedIndex>]
	public int ProductId { get; set; }

	public string Name { get; set; } = "";

	public decimal Price { get; set; }

	public string Category { get; set; } = "";
}

[DataCache]
public partial class TestProductItem {
	[DataCacheKey] public int ItemId { get; set; }

	[DataCacheGlobalLastUpdateIndex<ProductLastUpdatedIndex>]
	public int ProductId { get; set; }
}

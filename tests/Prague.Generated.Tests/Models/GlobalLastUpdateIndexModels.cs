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

/// <summary>
///   Global index scoped to enabled products only, via a filter struct.
/// </summary>
public partial class EnabledDepartmentIndex : IDataCacheGlobalLastUpdateIndex<int> {
}

public partial class EnabledBrandIndex : IDataCacheGlobalLastUpdateIndex<long> {
}

/// <summary>
///   Membership predicate: only enabled products contribute to the filtered indexes.
/// </summary>
public readonly struct EnabledProductFilter : IDataCacheGlobalLastUpdateFilter<FilteredCatalogProduct> {
	public static bool Include(FilteredCatalogProduct value) => value.IsEnabled;
}

/// <summary>
///   Test entity for FILTERED global last update indexes. Only products with
///   <see cref="IsEnabled"/> set are tracked, and membership is re-evaluated on every update.
/// </summary>
[DataCache]
public partial class FilteredCatalogProduct {
	[DataCacheKey] public string EventId { get; set; } = "";

	public bool IsEnabled { get; set; }

	/// <summary>Tracked by EnabledDepartmentIndex only when IsEnabled (AddOrUpdate timestamp).</summary>
	[DataCacheGlobalLastUpdateIndex<EnabledDepartmentIndex, EnabledProductFilter>]
	public int DepartmentId { get; set; }

	/// <summary>Tracked by EnabledBrandIndex only when IsEnabled (UpdatedAtMs timestamp).</summary>
	[DataCacheGlobalLastUpdateIndex<EnabledBrandIndex, EnabledProductFilter>(nameof(UpdatedAtMs))]
	public long BrandId { get; set; }

	public long UpdatedAtMs { get; set; }
}

public partial class KeyedAllIndex : IDataCacheGlobalLastUpdateIndex<int> {
}

public partial class KeyedEnabledIndex : IDataCacheGlobalLastUpdateIndex<int> {
}

public readonly struct KeyedEnabledFilter : IDataCacheGlobalLastUpdateFilter<KeyedProduct> {
	public static bool Include(KeyedProduct value) => value.IsEnabled;
}

/// <summary>
///   The key property carries BOTH a filtered global index (declared first) and an unfiltered one. The
///   keyless UpdatedAfter must be backed by the UNFILTERED index regardless of attribute order, otherwise
///   UpdatedAfter would silently return only filter-passing entities.
/// </summary>
[DataCache]
public partial class KeyedProduct {
	[DataCacheKey]
	[DataCacheGlobalLastUpdateIndex<KeyedEnabledIndex, KeyedEnabledFilter>]
	[DataCacheGlobalLastUpdateIndex<KeyedAllIndex>]
	public int ProductId { get; set; }

	public bool IsEnabled { get; set; }
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

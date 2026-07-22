namespace Prague.Generated.Tests.Models;

using Prague.Core;

/// <summary>
///   Simulates a "third-party" type that we don't own, but we define it here for testing.
///   In a real scenario, this would be in a separate assembly that we can't modify.
///   Note: For external types that truly can't be modified, we need ICacheEquatable and ICacheClonable
///   implementations which currently require the type to be partial.
/// </summary>
public partial class ThirdPartyProduct {
	public string Sku { get; set; } = "";
	public string Name { get; set; } = "";
	public decimal Price { get; set; }
	public int StockCount { get; set; }
	public string Category { get; set; } = "";
}

/// <summary>
///   Another simulated external type - an order from a third-party system.
/// </summary>
public partial class ThirdPartyOrder {
	public int OrderId { get; set; }
	public string CustomerId { get; set; } = "";
	public decimal TotalAmount { get; set; }
	public string Status { get; set; } = "";
	public DateTime CreatedAt { get; set; }
}

/// <summary>
///   Cache for ThirdPartyProduct using DataCache&lt;T&gt;.
///   The cache class is partial and gets the implementation generated.
/// </summary>
[DataCache<ThirdPartyProduct>(nameof(ThirdPartyProduct.Sku))]
[DataCacheIndex(nameof(ThirdPartyProduct.Category), DataCacheIndexType.Many)]
[DataCacheIndex(nameof(ThirdPartyProduct.Price), DataCacheIndexType.Range)]
[DataCacheIgnoreEquality(nameof(ThirdPartyProduct.StockCount))]
public partial class ProductCache {
}

/// <summary>
///   Cache for ThirdPartyOrder using DataCache&lt;T&gt;.
/// </summary>
[DataCache<ThirdPartyOrder>(nameof(ThirdPartyOrder.OrderId))]
[DataCacheTopic("ThirdParty.Orders.Cache")]
[DataCacheIndex(nameof(ThirdPartyOrder.CustomerId), DataCacheIndexType.Many)]
[DataCacheIndex(nameof(ThirdPartyOrder.Status), "Status", DataCacheIndexType.Many, 8)]
[DataCacheIndex(nameof(ThirdPartyOrder.TotalAmount), "AmountIndex", DataCacheIndexType.Range)]
public partial class ThirdPartyOrderCache {
}
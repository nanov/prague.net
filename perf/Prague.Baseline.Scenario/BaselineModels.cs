namespace Prague.Baseline.Scenario;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("baseline-products")]
[MessagePackObject]
public partial class BaselineProduct : IDataCacheItem<int, BaselineProduct> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheIndex(DataCacheIndexType.Range)] public int Range { get; set; }
	[Key(2)] public string Category { get; set; } = string.Empty;
	[Key(3)] public string Status { get; set; } = string.Empty;
	[Key(4)] public bool IsPublished { get; set; }
	[Key(5)] public int PrimaryValue { get; set; }
	[Key(6)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}

[DataCache]
[DataCacheTopic("baseline-product-infos")]
[MessagePackObject]
public partial class BaselineProductInfo : IDataCacheItem<int, BaselineProductInfo> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheForeignKey<BaselineProduct>(DataCacheJoinType.OneToOne)] public int ProductId { get; set; }
	[Key(2)] public string Warehouse { get; set; } = string.Empty;
	[Key(3)] public int StockCount { get; set; }
	[Key(4)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}

[DataCache]
[DataCacheTopic("baseline-offers")]
[MessagePackObject]
public partial class BaselineOffer : IDataCacheItem<int, BaselineOffer> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] [DataCacheForeignKey<BaselineProduct>(DataCacheJoinType.OneToMany)] public int ProductId { get; set; }
	[Key(2)] public bool IsActive { get; set; }
	[Key(3)] public decimal BasePrice { get; set; }
	[Key(4)] public int DisplayOrder { get; set; }
	[Key(5)] public long LastUpdated { get; set; }
	public string GetCacheKey() => Id.ToString();
}

namespace TestModels;

using Prague.Core;
using MessagePack;

[DataCache]
[DataCacheTopic("Cache.[v:brandId].LiveObject")]
public sealed partial class ClassLiveObjectCacheItem {
	[DataCacheKey] public long EventId { get; set; }

	public OldBaseTelemetry? LiveObject { get; set; }
}
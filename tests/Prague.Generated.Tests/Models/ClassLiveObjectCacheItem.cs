namespace TestModels;

using Prague.Core;
using MessagePack;

[DataCache]
[DataCacheTopic("Cache.[v:brandId].LiveObject")]
public sealed partial class ClassLiveObjectCacheItem {
	[Key(0)] [DataCacheKey] public long EventId { get; set; }

	[Key(1)] public OldBaseTelemetry? LiveObject { get; set; }
}
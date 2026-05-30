namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-events")]
[MessagePackObject]
public partial class EventWithNumericHeaders : IDataCacheItem<int, EventWithNumericHeaders> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }

	[Key(1)] public string Name { get; set; } = "";

	[Key(2)] [DataCacheHeader] public int TenantId { get; set; }

	[Key(3)] [DataCacheHeader] public long EventTimestamp { get; set; }

	public int GetCacheKey() => Id;
}

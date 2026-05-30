namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-enrich")]
[MessagePackObject]
public partial class HeaderEnrichEntity : IDataCacheItem<int, HeaderEnrichEntity> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }

	[Key(1)] public string Name { get; set; } = "";

	[Key(2)] [DataCacheHeader] public int TenantId { get; set; }

	[Key(3)] [DataCacheHeader] public string? EventType { get; set; }

	[Key(4)] [DataCacheHeader("custom-header")] public string? CustomValue { get; set; }

	[Key(5)] [DataCacheFromTimestamp] public long CreatedAt { get; set; }

	public int GetCacheKey() => Id;
}

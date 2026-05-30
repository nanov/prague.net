namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-filter")]
[MessagePackObject]
public partial class FilterEntity : IDataCacheItem<int, FilterEntity> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }

	[Key(1)] public string Name { get; set; } = "";

	[Key(2)] public int Value { get; set; }

	public int GetCacheKey() => Id;
}

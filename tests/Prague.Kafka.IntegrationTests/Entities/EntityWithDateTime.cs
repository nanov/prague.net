namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-datetime")]
[MessagePackObject]
public partial class EntityWithDateTime : IDataCacheItem<int, EntityWithDateTime> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] public string Name { get; set; } = "";
	[Key(2)] public DateTime CreatedAt { get; set; }
	[Key(3)] public DateTime? UpdatedAt { get; set; }
	[Key(4)] public Guid CorrelationId { get; set; }

	public int GetCacheKey() => Id;
}

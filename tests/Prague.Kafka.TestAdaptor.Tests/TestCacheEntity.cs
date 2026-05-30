namespace Prague.Kafka.TestAdaptor.Tests;

using Core;
using MessagePack;

[DataCache("TestEntityCache")]
[MessagePackObject]
public partial class TestEntity : IDataCacheItem<string, TestEntity> {
	[Key(0)] [DataCacheKey] public string Id { get; set; } = "";

	[Key(1)] public string Name { get; set; } = "";

	[Key(2)] public int Value { get; set; }

	public string GetCacheKey() {
		return Id;
	}
}
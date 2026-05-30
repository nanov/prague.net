namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-products")]
[MessagePackObject]
public partial class Product : IDataCacheItem<int, Product> {
    [Key(0)] [DataCacheKey] public int Id { get; set; }

    [Key(1)] public string Name { get; set; } = "";

    [Key(2)] public decimal Price { get; set; }

    [Key(3)] public string Category { get; set; } = "";

    public string GetCacheKey() => Id.ToString();
}

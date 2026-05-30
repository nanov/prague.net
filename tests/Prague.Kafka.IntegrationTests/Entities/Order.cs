namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-orders")]
[MessagePackObject]
public partial class Order : IDataCacheItem<int, Order> {
    [Key(0)] [DataCacheKey] public int Id { get; set; }

    [Key(1)] public int ProductId { get; set; }

    [Key(2)] public int Quantity { get; set; }

    [Key(3)] public string Status { get; set; } = "pending";

    public int GetCacheKey() => Id;
}

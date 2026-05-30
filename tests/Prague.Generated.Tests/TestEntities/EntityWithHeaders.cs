namespace Prague.Generated.Tests.TestEntities;

using Prague.Core;

[DataCache]
public partial class EntityWithHeaders : IDataCacheItem<int, EntityWithHeaders> {
	public int Id { get; set; }

	[DataCacheHeader] public int TenantId { get; set; }

	[DataCacheHeader] public string? EventType { get; set; }

	[DataCacheHeader("custom-header")] public string? CustomValue { get; set; }

	[DataCacheHeader] public long Timestamp { get; set; }

	[DataCacheFromTimestamp] public long CreatedAt { get; set; }

	public int GetKey() {
		return Id;
	}

	public void SetKey(int key) {
		Id = key;
	}
}
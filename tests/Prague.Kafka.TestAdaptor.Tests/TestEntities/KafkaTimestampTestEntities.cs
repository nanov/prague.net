namespace Prague.Kafka.TestAdaptor.Tests.TestEntities;

using Core;

[DataCache]
public partial class KafkaEntityWithLongTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long Timestamp { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithNullableLongTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long? Timestamp { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithDateTime {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTime CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithNullableDateTime {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTime? CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithDateTimeOffset {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTimeOffset CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithNullableDateTimeOffset {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTimeOffset? CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class KafkaEntityWithMultipleTimestamps {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long Timestamp { get; set; }

	[DataCacheFromTimestamp] public DateTime CreatedAt { get; set; }

	[DataCacheFromTimestamp] public DateTimeOffset? UpdatedAt { get; set; }

	public string Name { get; set; } = "";
}
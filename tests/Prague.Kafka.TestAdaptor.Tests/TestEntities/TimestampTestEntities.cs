namespace Prague.Kafka.TestAdaptor.Tests.TestEntities;

using Core;

[DataCache]
public partial class TestEntityWithLongTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long Timestamp { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithNullableLongTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long? Timestamp { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithDateTimeTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTime CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithNullableDateTimeTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTime? CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithDateTimeOffsetTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTimeOffset CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithNullableDateTimeOffsetTimestamp {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public DateTimeOffset? CreatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithMultipleTimestamps {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheFromTimestamp] public long CreatedTimestamp { get; set; }

	[DataCacheFromTimestamp] public DateTime? UpdatedAt { get; set; }

	public string Name { get; set; } = "";
}

[DataCache]
public partial class TestEntityWithoutTimestamp {
	[DataCacheKey] public int Id { get; set; }

	public string Name { get; set; } = "";
}
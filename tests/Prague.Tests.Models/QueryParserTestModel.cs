namespace Prague.Tests.Models;

using Core;

public enum TestStatus {
	Active,
	Inactive,
	Pending
}

public enum TestPriority {
	Low,
	Medium,
	High,
	Critical
}

[DataCache]
public partial class QueryParserTestModel : IDataCacheItem<int, QueryParserTestModel>,
	ICacheClonable<QueryParserTestModel>, ICacheEquatable<QueryParserTestModel> {
	[DataCacheKey] public required int Id { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required string Category { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required int UserId { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required TestStatus Status { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required TestPriority Priority { get; init; }

	public required string Name { get; init; }

	public required string Description { get; init; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public required long Timestamp { get; init; }

	public required int Score { get; init; }

	public int Key => Id;
}

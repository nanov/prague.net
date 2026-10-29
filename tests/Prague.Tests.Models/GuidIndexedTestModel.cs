namespace Prague.Tests.Models;

using Core;

// A Guid on every index kind the query parser reads, so the generated parser has to parse a Guid.
[DataCache]
public partial class GuidIndexedTestModel : IDataCacheItem<int, GuidIndexedTestModel>,
	ICacheClonable<GuidIndexedTestModel>, ICacheEquatable<GuidIndexedTestModel> {
	[DataCacheKey] public required int Id { get; init; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public required Guid OwnerId { get; init; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public required Guid ExternalId { get; init; }

	[DataCacheIndex(DataCacheIndexType.Range)]
	public required Guid SortId { get; init; }

	public int Key => Id;
}

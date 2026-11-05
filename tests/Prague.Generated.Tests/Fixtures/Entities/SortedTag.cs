namespace Prague.Generated.Tests.Fixtures.Entities;

using Prague.Core;

/// <summary>
///   String-keyed sort fixture: <c>[DataCacheSort]</c> comparers must order strings ordinally, independent of the
///   current culture, so a page sorted on a string column is identical on every machine.
/// </summary>
[DataCache]
[DataCacheSort("ByName", "Name:1", "Id:1")]
[DataCacheSort("ByCodeDesc", "Code:-1", "Id:1")]
public partial class SortedTag {
	[DataCacheKey] public required int Id { get; init; }

	[DataCacheIndex] public int Group { get; init; }

	public required string Name { get; init; }

	public string? Code { get; init; }
}

namespace Prague.Generated.Tests.Fixtures.Entities;

using Prague.Core;

/// <summary>
///   Settable fixture for the change-a-row contract: a resident instance changed in place reads as <c>Same</c>
///   and leaves its index stale; a <c>Clone()</c> changed and written back is an <c>Update</c>.
/// </summary>
[DataCache]
public partial class MutableTicket {
	[DataCacheKey]
	public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string State { get; set; } = "";

	public List<string> Tags { get; set; } = new();
}

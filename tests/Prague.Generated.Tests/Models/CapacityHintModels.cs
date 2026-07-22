namespace Prague.Generated.Tests.Models;

using Prague.Core;

// Exercises the InitialCapacity sizing hint in both spellings — the named property
// and the constructor argument: the hinted Many index must hand the hint to its
// per-key bucket PooledSets as the first-generation capacity, while the unhinted
// sibling keeps the default start.
[DataCache]
[DataCacheTopic]
public partial class CapacityHintedEvent {
	[DataCacheKey] public long Id { get; set; }

	[DataCacheIndex(InitialCapacity = 64)]
	public long GroupId { get; set; }

	[DataCacheIndex(8)]
	public long CtorHintedGroupId { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many, 16)]
	public long TypedCtorHintedGroupId { get; set; }

	[DataCacheIndex]
	public long PlainGroupId { get; set; }
}

namespace Prague.Generated.Tests.Models;

using Prague.Core;

// Exercises [DataCacheIndex(InitialCapacity = ...)]: the hinted Many index must
// hand the hint to its per-key bucket PooledSets as the first-generation capacity,
// while the unhinted sibling keeps the default tiny start.
[DataCache]
[DataCacheTopic]
public partial class CapacityHintedEvent {
	[DataCacheKey] public long Id { get; set; }

	[DataCacheIndex(InitialCapacity = 64)]
	public long GroupId { get; set; }

	[DataCacheIndex]
	public long PlainGroupId { get; set; }
}

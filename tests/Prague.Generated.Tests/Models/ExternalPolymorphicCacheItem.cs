namespace Prague.Generated.Tests.Models;

using Prague.Core;
using Prague.Tests.Models;
using MessagePack;

/// <summary>
///   Cache item that references polymorphic types from an external assembly (Prague.Tests.Models).
///   This tests that the code generator can find derived types from referenced assemblies.
/// </summary>
[MessagePackObject]
[DataCache]
public sealed partial class ExternalPolymorphicCacheItem {
	[Key(0)] [DataCacheKey] public long EventId { get; set; }

	[Key(1)] public IExternalTelemetry? LiveData { get; set; }
}

/// <summary>
///   Another cache item using the base class instead of interface.
/// </summary>
[MessagePackObject]
[DataCache]
public sealed partial class ExternalPolymorphicBaseClassCacheItem {
	[Key(0)] [DataCacheKey] public long EventId { get; set; }

	[Key(1)] public ExternalBaseTelemetry? LiveData { get; set; }
}
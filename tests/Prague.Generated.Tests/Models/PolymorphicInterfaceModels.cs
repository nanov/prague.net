namespace Prague.Generated.Tests.Models;

using Prague.Core;
using global::TestModels;
using MessagePack;

[MessagePackObject]
[DataCache(CacheClassName = nameof(LiveObjectCache))]
[DataCacheTopic("Cache.[v:brandId].LiveObject")]
public sealed partial class LiveObjectCacheItem {
	[Key(0)] [DataCacheKey] public long EventId { get; set; }

	[Key(1)] public IBaseTelemetry? LiveObject { get; set; }
}

[Union(0, typeof(DefaultTelemetry))]
[Union(1, typeof(GaugeTelemetry))]
[Union(2, typeof(SensorTelemetry))]
public interface IBaseTelemetry {
	DeviceType DeviceKind { get; }
	List<PeriodReading>? PeriodScore { get; set; }
	List<ReadingSample> ReadingSamples { get; set; }
	string SourceUrl { get; set; }
	int PrimaryValue { get; set; }
	int SecondaryValue { get; set; }
	ReadingPhase? PeriodId { get; set; }
	string? Minute { get; set; }
	string? PeriodDisplayName { get; set; }
	string StoppageTimeAnnounced { get; set; }
	string StoppageTime { get; set; }
	bool IncludeTimeInCurrentPeriod { get; set; }
	bool IsTimeBased { get; set; }
	bool ScoresIsSetBased { get; }
}

[MessagePackObject]
public class BaseTelemetry : IBaseTelemetry {
	[IgnoreMember] public virtual DeviceType DeviceKind { get; }

	[Key(0)] public List<PeriodReading>? PeriodScore { get; set; }

	[Key(1)] public List<ReadingSample> ReadingSamples { get; set; }

	[Key(2)] public string SourceUrl { get; set; }

	[Key(3)] public int PrimaryValue { get; set; } = 0;

	[Key(4)] public int SecondaryValue { get; set; } = 0;

	[Key(5)] public ReadingPhase? PeriodId { get; set; }

	[Key(6)] public string? Minute { get; set; }

	[Key(7)] public string? PeriodDisplayName { get; set; }

	[Key(8)] public string StoppageTimeAnnounced { get; set; }

	[Key(9)] public string StoppageTime { get; set; }

	[Key(10)] public bool IncludeTimeInCurrentPeriod { get; set; }

	[IgnoreMember] public virtual bool IsTimeBased { get; set; }

	[IgnoreMember] public virtual bool ScoresIsSetBased { get; }
}

[MessagePackObject]
public sealed class DefaultTelemetry : BaseTelemetry {
	[IgnoreMember] public override DeviceType DeviceKind => default;
}

[MessagePackObject]
public class SensorTelemetry : BaseTelemetry {
	[IgnoreMember] public override DeviceType DeviceKind => DeviceType.Sensor;

	[Key(11)] public string ActiveNode { get; set; }

	[Key(12)] public int? SecondaryReadingValue { get; set; }

	[Key(13)] public int? PrimaryReadingValue { get; set; }

	[IgnoreMember] public override bool ScoresIsSetBased => true;
}

[MessagePackObject]
public class GaugeTelemetry : BaseTelemetry {
	[IgnoreMember] public override DeviceType DeviceKind => DeviceType.Gauge;

	[Key(11)] public string ActiveNode { get; set; }

	[IgnoreMember] public override bool ScoresIsSetBased => true;
}

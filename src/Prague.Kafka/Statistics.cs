namespace Prague.Kafka;

using System.Collections.Frozen;
using System.Text.Json.Serialization;
using Core;

public sealed class KafkaCachesStatistics {
	private FrozenDictionary<string, KafkaCachesConsumerStatistics> _consumers =
		FrozenDictionary<string, KafkaCachesConsumerStatistics>.Empty;

	public FrozenDictionary<string, KafkaCachesConsumerStatistics> Consumers => _consumers;

	public KafkaCachesConsumerStatistics GetOrAddConsumer(string consumerName) {
		if (_consumers.TryGetValue(consumerName, out var consumer))
			return consumer;
		var stats = new KafkaCachesConsumerStatistics();
		var consumersDict = _consumers.ToDictionary();
		consumersDict.Add(consumerName, stats);
		_consumers = consumersDict.ToFrozenDictionary();
		return stats;
	}
}

public sealed class KafkaCachesConsumerStatistics {
	private FrozenDictionary<string, KafkaDataCacheStatistics> _caches =
		FrozenDictionary<string, KafkaDataCacheStatistics>.Empty;

	private DataCacheStatistics[] _cachesStatistics = Array.Empty<DataCacheStatistics>();

	public FrozenDictionary<string, KafkaDataCacheStatistics> Caches => _caches;
	public TimeSpan InitialLoadTime { get; internal set; }

	/// <summary>
	/// Total number of messages received by the consumer (from librdkafka stats).
	/// </summary>
	public long TotalMessagesReceived { get; private set; }

	/// <summary>
	/// Total bytes received by the consumer (from librdkafka stats).
	/// </summary>
	public long TotalBytesReceived { get; private set; }

	/// <summary>
	/// Number of partitions currently assigned to this consumer.
	/// </summary>
	public int AssignedPartitions { get; private set; }
	public int CachesLoadingCount { get; private set; }

	/// <summary>
	/// Number of brokers currently in "UP" state (from librdkafka stats).
	/// Populated when <see cref="KafkaCachesGlobalOptions.StatisticsEnabled"/>
	/// is <c>true</c> (the default); stays 0 if statistics are disabled.
	/// </summary>
	internal int BrokerUpCount;
	public int BrokerUpCountUnsafe => BrokerUpCount;

	/// <summary>
	/// Last <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> recorded by
	/// the outer poll loop after <c>consumer.Consume(ct)</c> returned.
	/// </summary>
	internal long LastPollTimestamp;

	/// <summary>
	/// Latched true on any fatal Kafka error or any handler loop fault.
	/// One-way: never resets for the lifetime of the consumer.
	/// </summary>
	internal bool IsFatalLatched;

	/// <summary>
	/// True when the most recent rebalance event was a partitions-lost event
	/// and no successful re-assignment has occurred since.
	/// </summary>
	internal bool HasLostPartitions;

	public long LastPollTimestampUnsafe => LastPollTimestamp;
	public bool IsFatalLatchedUnsafe => IsFatalLatched;
	public bool HasLostPartitionsUnsafe => HasLostPartitions;

	internal void SetAssignedPartitions(int count) => AssignedPartitions = count;
	internal void SetCachesLoadingCount(int count) => CachesLoadingCount = count;

	/// <summary>
	/// Broker round-trip time in milliseconds (max p99 across all brokers).
	/// </summary>
	public long BrokerLatencyMs { get; private set; }

	/// <summary>
	/// Broker throttle time in milliseconds (max p99 across all brokers).
	/// </summary>
	public long ThrottleMs { get; private set; }

	/// <summary>
	/// Internal queue latency in milliseconds (max p99 across all brokers).
	/// </summary>
	public long QueueLatencyMs { get; private set; }

	internal void AddCaches(KafkaCacheHandlers handlers) {
		foreach (var handler in handlers.Handlers)
			AddCache(handler.Key, handler.Value.Statistics);
	}

	internal KafkaDataCacheStatistics AddCache(string name, KafkaDataCacheStatistics kafkaCachesStatistics) {
		if (_caches.TryGetValue(name, out var cache))
			return cache;
		var cachesDict = _caches.ToDictionary();
		var idx = _cachesStatistics.Length;
		Array.Resize(ref _cachesStatistics, idx + 1);
		_cachesStatistics[idx] = kafkaCachesStatistics.Statistics;
		cachesDict.Add(name, kafkaCachesStatistics);
		_caches = cachesDict.ToFrozenDictionary();
		return kafkaCachesStatistics;
	}


	internal void TakeCachesSnapshot(DateTimeOffset timestmap) {
		DataCacheStatisticsMarshall.TakeSnapshot(_cachesStatistics, timestmap);
	}

	internal void TakeCachesSnapshot(DateTimeOffset timestmap, TimeSpan periodLength) {
		DataCacheStatisticsMarshall.TakeSnapshot(_cachesStatistics, timestmap, periodLength);
	}

	internal void UpdateFromLibrdkafkaStats(LibrdkafkaStatsSnapshot snapshot) {
		TotalMessagesReceived = snapshot.RxMsgs;
		TotalBytesReceived = snapshot.RxMsgBytes;

		if (snapshot.Brokers is null) {
			BrokerUpCount = 0;
			return;
		}

		long maxRtt = 0, maxThrottle = 0, maxIntLatency = 0;
		var upCount = 0;
		foreach (var broker in snapshot.Brokers.Values) {
			if (broker.State == "UP") upCount++;
			if (broker.Rtt.P99 > maxRtt) maxRtt = broker.Rtt.P99;
			if (broker.Throttle.P99 > maxThrottle) maxThrottle = broker.Throttle.P99;
			if (broker.IntLatency.P99 > maxIntLatency) maxIntLatency = broker.IntLatency.P99;
		}

		BrokerUpCount = upCount;
		BrokerLatencyMs = maxRtt / 1000;
		ThrottleMs = maxThrottle / 1000;
		QueueLatencyMs = maxIntLatency / 1000;
	}
}

public sealed class KafkaDataCacheStatistics {
	public TimeSpan InitialLoadTime;


	public KafkaDataCacheStatistics(string topicName, DataCacheStatistics statistics) {
		TopicName = topicName;
		Statistics = statistics;
	}

	internal DataCacheStatistics Statistics { get; }

	public string TopicName { get; }

	/// <summary>
	/// Snapshot size taken at the last snapshot time.
	/// </summary>
	public ulong Size => Statistics.Size;

	/// <summary>
	/// Live size - current size of the cache (not snapshot).
	/// </summary>
	public ulong LiveSize => Statistics.LiveSize;

	public IReadOnlyDictionary<string, DataCacheIndexStatistics> Indexes => Statistics.Indexes;

	public DateTimeOffset CollectedAt => Statistics.CollectedAt;
	public TimeSpan PeriodLength => Statistics.PeriodLength;

	/// <summary>
	/// Total number of messages received for this topic (sum across partitions, from librdkafka stats).
	/// </summary>
	public long TotalMessagesReceived { get; internal set; }

	/// <summary>
	/// Total bytes received for this topic (sum across partitions, from librdkafka stats).
	/// </summary>
	public long TotalBytesReceived { get; internal set; }

	/// <summary>
	/// Number of pre-fetched messages in fetch queue.
	/// </summary>
	public int FetchQueueCount { get; internal set; }

	/// <summary>
	/// Bytes in fetch queue.
	/// </summary>
	public int FetchQueueSize { get; internal set; }

	/// <summary>
	/// Consumer fetch state for this partition.
	/// </summary>
	public FetchState FetchState { get; internal set; }

	/// <summary>
	/// 0 when the handler channel loop is idle. When non-zero, holds the
	/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> of the moment
	/// the current message started processing. Plain field; single writer (the
	/// channel-loop task), single occasional reader (health checks). 64-bit
	/// aligned, atomic on supported targets.
	/// </summary>
	internal long LastProcessingStartTimestamp;

	/// <summary>
	/// Latched true when the handler channel loop terminates with an exception.
	/// One-way: never resets for the lifetime of this stats instance.
	/// </summary>
	internal bool IsLoopFaulted;

	/// <summary>
	/// Number of partitions currently assigned for this cache's topic.
	/// </summary>
	internal int AssignedPartitionCount;

	public long LastProcessingStartTimestampUnsafe => LastProcessingStartTimestamp;
	public bool IsLoopFaultedUnsafe => IsLoopFaulted;
	public int  AssignedPartitionCountUnsafe => AssignedPartitionCount;

	internal void SetInitialLoad(TimeSpan initialLoadTime) {
		InitialLoadTime = initialLoadTime;
	}
}

public enum FetchState {
	None,
	Stopping,
	Stopped,
	OffsetQuery,
	OffsetWait,
	Active
}

internal readonly struct LibrdkafkaStatsSnapshot {
	[JsonPropertyName("rxmsgs")]
	public long RxMsgs { get; init; }

	[JsonPropertyName("rxmsg_bytes")]
	public long RxMsgBytes { get; init; }

	[JsonPropertyName("brokers")]
	public Dictionary<string, LibrdkafkaBrokerStats>? Brokers { get; init; }

	[JsonPropertyName("topics")]
	public Dictionary<string, LibrdkafkaTopicStats>? Topics { get; init; }
}

internal readonly struct LibrdkafkaBrokerStats {
	[JsonPropertyName("state")]
	public string? State { get; init; }

	[JsonPropertyName("rtt")]
	public LibrdkafkaWindowStats Rtt { get; init; }

	[JsonPropertyName("throttle")]
	public LibrdkafkaWindowStats Throttle { get; init; }

	[JsonPropertyName("int_latency")]
	public LibrdkafkaWindowStats IntLatency { get; init; }
}

internal readonly struct LibrdkafkaWindowStats {
	[JsonPropertyName("min")]
	public long Min { get; init; }

	[JsonPropertyName("max")]
	public long Max { get; init; }

	[JsonPropertyName("avg")]
	public long Avg { get; init; }

	[JsonPropertyName("cnt")]
	public long Cnt { get; init; }

	[JsonPropertyName("p99")]
	public long P99 { get; init; }
}

internal readonly struct LibrdkafkaTopicStats {
	[JsonPropertyName("partitions")]
	public Dictionary<string, LibrdkafkaPartitionStats>? Partitions { get; init; }
}

internal readonly struct LibrdkafkaPartitionStats {
	[JsonPropertyName("rxmsgs")]
	public long RxMsgs { get; init; }

	[JsonPropertyName("rxbytes")]
	public long RxBytes { get; init; }

	[JsonPropertyName("fetchq_cnt")]
	public int FetchQueueCount { get; init; }

	[JsonPropertyName("fetchq_size")]
	public int FetchQueueSize { get; init; }

	[JsonPropertyName("fetch_state")]
	public string? FetchState { get; init; }
}

[JsonSerializable(typeof(LibrdkafkaStatsSnapshot))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class LibrdkafkaStatsJsonContext : JsonSerializerContext {
}

[JsonSerializable(typeof(KafkaCachesStatistics))]
[JsonSerializable(typeof(KafkaCachesConsumerStatistics))]
[JsonSerializable(typeof(KafkaDataCacheStatistics))]
[JsonSerializable(typeof(DataCacheStatistics))]
[JsonSerializable(typeof(DataCacheIndexStatistics))]
[JsonSerializable(typeof(Dictionary<string, KafkaCachesConsumerStatistics>))]
[JsonSerializable(typeof(Dictionary<string, KafkaDataCacheStatistics>))]
[JsonSerializable(typeof(Dictionary<string, DataCacheIndexStatistics>))]
public partial class KafkaCachesJsonContext : JsonSerializerContext {
}

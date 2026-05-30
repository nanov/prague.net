namespace Prague.Kafka.Health;

using System.Diagnostics;
using System.Runtime.CompilerServices;

internal static class KafkaCachesHealthPredicates {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFatal(KafkaCachesConsumerStatistics s) => s.IsFatalLatched;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPollStalled(KafkaCachesConsumerStatistics s, TimeSpan timeout) =>
		Stopwatch.GetElapsedTime(s.LastPollTimestamp) >= timeout;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasLostPartitions(KafkaCachesConsumerStatistics s) => s.HasLostPartitions;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasBrokersDown(KafkaCachesConsumerStatistics s, int minBrokersUp) =>
		s.BrokerUpCount < minBrokersUp;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasIncompleteInitialLoad(KafkaCachesConsumerStatistics s) => s.CachesLoadingCount > 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsLoopFaulted(KafkaDataCacheStatistics c) => c.IsLoopFaulted;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasProcessingTimeout(KafkaDataCacheStatistics c, TimeSpan timeout) =>
		c.LastProcessingStartTimestamp != 0
			&& Stopwatch.GetElapsedTime(c.LastProcessingStartTimestamp) >= timeout;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasNoPartitionAssigned(KafkaDataCacheStatistics c) => c.AssignedPartitionCount < 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsConsumerHealthy(KafkaCachesConsumerStatistics s, KafkaCachesHealthOptions opts) =>
		!IsFatal(s)
			&& !IsPollStalled(s, opts.PollLoopHeartbeatTimeout)
			&& !HasLostPartitions(s)
			&& !HasBrokersDown(s, opts.MinBrokersUp)
			&& !HasIncompleteInitialLoad(s);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsCacheHealthy(KafkaDataCacheStatistics c, KafkaCachesHealthOptions opts) =>
		!IsLoopFaulted(c)
			&& !HasNoPartitionAssigned(c)
			&& !HasProcessingTimeout(c, opts.HandlerProcessingTimeout);
}

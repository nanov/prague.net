namespace Prague.Kafka.Health;

using static KafkaCachesHealthPredicates;

public enum KafkaCachesHealthFailure {
	FatalLatched,
	PollLoopStalled,
	HandlerLoopFaulted,
	HandlerProcessingTimeout,
	InitialLoadIncomplete,
	NoPartitionAssigned,
	BrokersDown,
	PartitionsLost
}

public readonly struct KafkaCachesHealthVerdict {
	public IReadOnlyList<KafkaCachesHealthFailure> LivenessFailures { get; init; }
	public IReadOnlyList<KafkaCachesHealthFailure> ReadinessFailures { get; init; }
	public IReadOnlyList<string> FailingCacheNames { get; init; }

	public bool IsLive => LivenessFailures.Count == 0;
	public bool IsReady => IsLive && ReadinessFailures.Count == 0;

	public static readonly KafkaCachesHealthVerdict Healthy = new() {
		LivenessFailures = Array.Empty<KafkaCachesHealthFailure>(),
		ReadinessFailures = Array.Empty<KafkaCachesHealthFailure>(),
		FailingCacheNames = Array.Empty<string>()
	};
}

public static class KafkaCachesHealthEvaluator {
	public static KafkaCachesHealthVerdict Evaluate(
		KafkaCachesConsumerStatistics stats,
		KafkaCachesHealthOptions options) {

		// Fast path: scan everything once. Allocate failure lists lazily.
		List<KafkaCachesHealthFailure>? live = null;
		List<KafkaCachesHealthFailure>? ready = null;
		List<string>? failingCaches = null;

		if (IsFatal(stats))
			(live ??= new()).Add(KafkaCachesHealthFailure.FatalLatched);

		if (IsPollStalled(stats, options.PollLoopHeartbeatTimeout))
			(live ??= new()).Add(KafkaCachesHealthFailure.PollLoopStalled);

		foreach (var (name, cache) in stats.Caches) {
			if (IsLoopFaulted(cache)) {
				(live ??= new()).Add(KafkaCachesHealthFailure.HandlerLoopFaulted);
				(failingCaches ??= new()).Add(name);
				continue;
			}
			if (HasProcessingTimeout(cache, options.HandlerProcessingTimeout)) {
				(live ??= new()).Add(KafkaCachesHealthFailure.HandlerProcessingTimeout);
				(failingCaches ??= new()).Add(name);
			}
		}

		if (HasIncompleteInitialLoad(stats))
			(ready ??= new()).Add(KafkaCachesHealthFailure.InitialLoadIncomplete);

		foreach (var (name, cache) in stats.Caches) {
			if (HasNoPartitionAssigned(cache)) {
				(ready ??= new()).Add(KafkaCachesHealthFailure.NoPartitionAssigned);
				(failingCaches ??= new()).Add(name);
			}
		}

		if (HasBrokersDown(stats, options.MinBrokersUp))
			(ready ??= new()).Add(KafkaCachesHealthFailure.BrokersDown);

		if (HasLostPartitions(stats))
			(ready ??= new()).Add(KafkaCachesHealthFailure.PartitionsLost);

		if (live is null && ready is null && failingCaches is null)
			return KafkaCachesHealthVerdict.Healthy;

		return new KafkaCachesHealthVerdict {
			LivenessFailures = (IReadOnlyList<KafkaCachesHealthFailure>?)live
				?? Array.Empty<KafkaCachesHealthFailure>(),
			ReadinessFailures = (IReadOnlyList<KafkaCachesHealthFailure>?)ready
				?? Array.Empty<KafkaCachesHealthFailure>(),
			FailingCacheNames = (IReadOnlyList<string>?)failingCaches
				?? Array.Empty<string>()
		};
	}
}

namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Health;
using Core;

public sealed class KafkaCachesHealthPredicatesTests {
	private static KafkaCachesConsumerStatistics MakeHealthyStats(int caches = 2) {
		var stats = new KafkaCachesConsumerStatistics();
		stats.LastPollTimestamp = Stopwatch.GetTimestamp();
		stats.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> {
				["b1"] = new LibrdkafkaBrokerStats { State = "UP" }
			}
		});
		stats.HasLostPartitions = false;
		stats.IsFatalLatched = false;

		for (var i = 0; i < caches; i++) {
			var cache = stats.AddCache($"topic-{i}",
				new KafkaDataCacheStatistics($"topic-{i}", new DataCacheStatistics(DataCacheNoOpStatisticsCollector.Default)));
			cache.AssignedPartitionCount = 1;
			cache.IsLoopFaulted = false;
			cache.LastProcessingStartTimestamp = 0;
			cache.SetInitialLoad(TimeSpan.FromMilliseconds(50));
		}
		stats.SetCachesLoadingCount(0);
		return stats;
	}

	private static KafkaCachesHealthOptions DefaultOptions() => new();

	private static KafkaDataCacheStatistics FirstCache(KafkaCachesConsumerStatistics stats) =>
		stats.Caches.Values.First();

	[Test]
	public void IsFatal_returns_false_on_healthy_stats_and_true_when_latched() {
		var stats = MakeHealthyStats();
		Assert.That(KafkaCachesHealthPredicates.IsFatal(stats), Is.False);

		stats.IsFatalLatched = true;
		Assert.That(KafkaCachesHealthPredicates.IsFatal(stats), Is.True);
	}

	[Test]
	public void IsPollStalled_returns_false_when_recent_and_true_when_stale() {
		var stats = MakeHealthyStats();
		var opts = DefaultOptions();
		Assert.That(KafkaCachesHealthPredicates.IsPollStalled(stats, opts.PollLoopHeartbeatTimeout), Is.False);

		stats.LastPollTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60);
		Assert.That(KafkaCachesHealthPredicates.IsPollStalled(stats, opts.PollLoopHeartbeatTimeout), Is.True);
	}

	[Test]
	public void HasLostPartitions_returns_false_on_healthy_and_true_when_flagged() {
		var stats = MakeHealthyStats();
		Assert.That(KafkaCachesHealthPredicates.HasLostPartitions(stats), Is.False);

		stats.HasLostPartitions = true;
		Assert.That(KafkaCachesHealthPredicates.HasLostPartitions(stats), Is.True);
	}

	[Test]
	public void HasBrokersDown_returns_false_at_or_above_minimum_and_true_below() {
		var stats = MakeHealthyStats();
		Assert.That(KafkaCachesHealthPredicates.HasBrokersDown(stats, 1), Is.False);

		stats.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot { Brokers = null });
		Assert.That(KafkaCachesHealthPredicates.HasBrokersDown(stats, 1), Is.True);
	}

	[Test]
	public void HasIncompleteInitialLoad_returns_false_at_zero_and_true_when_positive() {
		var stats = MakeHealthyStats();
		Assert.That(KafkaCachesHealthPredicates.HasIncompleteInitialLoad(stats), Is.False);

		stats.SetCachesLoadingCount(1);
		Assert.That(KafkaCachesHealthPredicates.HasIncompleteInitialLoad(stats), Is.True);
	}

	[Test]
	public void IsLoopFaulted_returns_false_on_healthy_and_true_when_set() {
		var stats = MakeHealthyStats();
		var cache = FirstCache(stats);
		Assert.That(KafkaCachesHealthPredicates.IsLoopFaulted(cache), Is.False);

		cache.IsLoopFaulted = true;
		Assert.That(KafkaCachesHealthPredicates.IsLoopFaulted(cache), Is.True);
	}

	[Test]
	public void HasProcessingTimeout_handles_idle_recent_and_stale_timestamps() {
		var stats = MakeHealthyStats();
		var cache = FirstCache(stats);
		var opts = DefaultOptions();

		// Idle sentinel: never times out.
		cache.LastProcessingStartTimestamp = 0;
		Assert.That(KafkaCachesHealthPredicates.HasProcessingTimeout(cache, opts.HandlerProcessingTimeout), Is.False);

		// Recently started: not yet stale.
		cache.LastProcessingStartTimestamp = Stopwatch.GetTimestamp();
		Assert.That(KafkaCachesHealthPredicates.HasProcessingTimeout(cache, opts.HandlerProcessingTimeout), Is.False);

		// Long-running: stale beyond timeout.
		cache.LastProcessingStartTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60);
		Assert.That(KafkaCachesHealthPredicates.HasProcessingTimeout(cache, opts.HandlerProcessingTimeout), Is.True);
	}

	[Test]
	public void HasNoPartitionAssigned_returns_false_when_assigned_and_true_when_zero() {
		var stats = MakeHealthyStats();
		var cache = FirstCache(stats);
		Assert.That(KafkaCachesHealthPredicates.HasNoPartitionAssigned(cache), Is.False);

		cache.AssignedPartitionCount = 0;
		Assert.That(KafkaCachesHealthPredicates.HasNoPartitionAssigned(cache), Is.True);
	}

	[Test]
	public void IsConsumerHealthy_is_true_on_healthy_and_false_on_each_individual_condition() {
		var opts = DefaultOptions();

		var baseline = MakeHealthyStats();
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(baseline, opts), Is.True);

		var fatal = MakeHealthyStats();
		fatal.IsFatalLatched = true;
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(fatal, opts), Is.False);

		var stalledPoll = MakeHealthyStats();
		stalledPoll.LastPollTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60);
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(stalledPoll, opts), Is.False);

		var lost = MakeHealthyStats();
		lost.HasLostPartitions = true;
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(lost, opts), Is.False);

		var brokers = MakeHealthyStats();
		brokers.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot { Brokers = null });
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(brokers, opts), Is.False);

		var loading = MakeHealthyStats();
		loading.SetCachesLoadingCount(1);
		Assert.That(KafkaCachesHealthPredicates.IsConsumerHealthy(loading, opts), Is.False);
	}

	[Test]
	public void IsCacheHealthy_is_true_on_healthy_and_false_on_each_individual_condition() {
		var opts = DefaultOptions();

		var baselineStats = MakeHealthyStats();
		var baseline = FirstCache(baselineStats);
		Assert.That(KafkaCachesHealthPredicates.IsCacheHealthy(baseline, opts), Is.True);

		var faultedStats = MakeHealthyStats();
		var faulted = FirstCache(faultedStats);
		faulted.IsLoopFaulted = true;
		Assert.That(KafkaCachesHealthPredicates.IsCacheHealthy(faulted, opts), Is.False);

		var noPartStats = MakeHealthyStats();
		var noPart = FirstCache(noPartStats);
		noPart.AssignedPartitionCount = 0;
		Assert.That(KafkaCachesHealthPredicates.IsCacheHealthy(noPart, opts), Is.False);

		var timeoutStats = MakeHealthyStats();
		var timeout = FirstCache(timeoutStats);
		timeout.LastProcessingStartTimestamp = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60);
		Assert.That(KafkaCachesHealthPredicates.IsCacheHealthy(timeout, opts), Is.False);
	}
}

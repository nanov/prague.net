namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Health;
using Core;

public sealed class KafkaCachesHealthEvaluatorTests {
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
			// All caches finished initial load.
			cache.SetInitialLoad(TimeSpan.FromMilliseconds(50));
		}
		// Mark "all caches loaded" via the existing field.
		stats.SetCachesLoadingCount(0);
		return stats;
	}

	private static KafkaCachesHealthOptions DefaultOptions() => new();

	[Test]
	public void Healthy_state_returns_Healthy_for_both_liveness_and_readiness() {
		var stats = MakeHealthyStats();
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Is.Empty);
	}

	[Test]
	public void Fatal_latched_fails_liveness() {
		var stats = MakeHealthyStats();
		stats.IsFatalLatched = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.FatalLatched));
	}

	[Test]
	public void Stale_poll_timestamp_fails_liveness() {
		var stats = MakeHealthyStats();
		// Backdate timestamp far past the timeout.
		stats.LastPollTimestamp =
			Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60); // 60s ago
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.PollLoopStalled));
	}

	[Test]
	public void Faulted_handler_loop_fails_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.IsLoopFaulted = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Does.Contain(KafkaCachesHealthFailure.HandlerLoopFaulted));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Idle_handler_does_not_fail_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.LastProcessingStartTimestamp = 0; // idle sentinel

		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
	}

	[Test]
	public void Long_running_handler_processing_fails_liveness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.LastProcessingStartTimestamp =
			Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60); // 60s ago

		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures,
			Does.Contain(KafkaCachesHealthFailure.HandlerProcessingTimeout));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Caches_still_loading_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.SetCachesLoadingCount(1);
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.InitialLoadIncomplete));
	}

	[Test]
	public void Cache_with_zero_assigned_partitions_fails_only_readiness() {
		var stats = MakeHealthyStats();
		var firstCache = stats.Caches.Values.First();
		firstCache.AssignedPartitionCount = 0;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.NoPartitionAssigned));
		Assert.That(v.FailingCacheNames, Does.Contain(firstCache.TopicName));
	}

	[Test]
	public void Brokers_below_minimum_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot { Brokers = null });
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.BrokersDown));
	}

	[Test]
	public void Lost_partitions_flag_fails_only_readiness() {
		var stats = MakeHealthyStats();
		stats.HasLostPartitions = true;
		var v = KafkaCachesHealthEvaluator.Evaluate(stats, DefaultOptions());

		Assert.That(v.LivenessFailures, Is.Empty);
		Assert.That(v.ReadinessFailures, Does.Contain(KafkaCachesHealthFailure.PartitionsLost));
	}
}

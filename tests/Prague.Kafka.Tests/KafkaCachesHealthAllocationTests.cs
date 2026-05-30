namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Health;
using Core;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesHealthAllocationTests {
	private static KafkaCachesStatistics MakeHealthy(int caches = 3) {
		var top = new KafkaCachesStatistics();
		var stats = top.GetOrAddConsumer("c1");
		stats.LastPollTimestamp = Stopwatch.GetTimestamp();
		stats.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> {
				["b1"] = new LibrdkafkaBrokerStats { State = "UP" }
			}
		});
		for (var i = 0; i < caches; i++) {
			var c = stats.AddCache($"t{i}",
				new KafkaDataCacheStatistics($"t{i}", new DataCacheStatistics(DataCacheNoOpStatisticsCollector.Default)));
			c.AssignedPartitionCount = 1;
		}
		stats.SetCachesLoadingCount(0);
		return top;
	}

	[Test]
	public async Task Liveness_check_on_healthy_path_does_not_allocate() {
		var stats = MakeHealthy();
		var monitor = new TestOptionsMonitor(new KafkaCachesHealthOptions());
		var check = new KafkaCachesLivenessHealthCheck(stats, monitor);
		var ctx = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

		// warm-up to JIT.
		await check.CheckHealthAsync(ctx);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 32; i++)
			await check.CheckHealthAsync(ctx);
		var delta = GC.GetAllocatedBytesForCurrentThread() - before;

		// Allow at most a tiny per-call overhead (Task.FromResult result boxing
		// in some runtimes, etc.). 32 calls * <50 bytes/call ⇒ < 1.6 KB.
		Assert.That(delta, Is.LessThan(2048),
			$"Allocated {delta} bytes across 32 healthy checks");
	}

	[Test]
	public async Task Readiness_check_on_healthy_path_does_not_allocate() {
		var stats = MakeHealthy();
		var monitor = new TestOptionsMonitor(new KafkaCachesHealthOptions());
		var check = new KafkaCachesReadinessHealthCheck(stats, monitor);
		var ctx = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

		await check.CheckHealthAsync(ctx);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 32; i++)
			await check.CheckHealthAsync(ctx);
		var delta = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(delta, Is.LessThan(2048),
			$"Allocated {delta} bytes across 32 healthy checks");
	}

	private sealed class TestOptionsMonitor : IOptionsMonitor<KafkaCachesHealthOptions> {
		public TestOptionsMonitor(KafkaCachesHealthOptions value) => CurrentValue = value;
		public KafkaCachesHealthOptions CurrentValue { get; }
		public KafkaCachesHealthOptions Get(string? name) => CurrentValue;
		public IDisposable? OnChange(Action<KafkaCachesHealthOptions, string?> listener) => null;
	}
}

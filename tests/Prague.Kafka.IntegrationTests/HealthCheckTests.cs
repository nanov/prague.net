namespace Prague.Kafka.IntegrationTests;

using Entities;
using Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Options;
using Testcontainers.Kafka;

[TestFixture]
public class HealthCheckTests {
	private const string Topic = "hc-tests-products";
	private const string ConfigSection = "kafkaCaches";

	// ---------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------

	private static ServiceProvider BuildServiceProvider(
		string bootstrapServers,
		string topicName,
		Action<KafkaCachesGlobalOptions>? globalOpts = null) {

		var services = new ServiceCollection();

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ $"{ConfigSection}:BootstrapServers", bootstrapServers }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();

		// Apply global options (statistics etc.) before registering caches so the
		// Configure<> call merges with whatever AddKafkaCaches also registers.
		if (globalOpts is not null)
			services.Configure<KafkaCachesGlobalOptions>(globalOpts);

		services.AddKafkaCaches(ConfigSection, b => {
			b.AddCache<ProductCache, int, Product>(topicName);
		});

		services.AddHealthChecks()
			.AddPragueKafkaLiveness()
			.AddPragueKafkaReadiness();

		return services.BuildServiceProvider();
	}

	private static async Task StartAndLoadAsync(ServiceProvider sp, CancellationToken ct) {
		var hostedServices = sp.GetServices<IHostedService>().ToList();
		await Task.WhenAll(hostedServices.Select(h => h.StartAsync(ct)));

		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(ct);
	}

	private static async Task StopAsync(ServiceProvider sp) {
		var hostedServices = sp.GetServices<IHostedService>().ToList();
		await Task.WhenAll(hostedServices.Select(h => h.StopAsync(CancellationToken.None)));
	}

	// ---------------------------------------------------------------------------
	// Test 1: Liveness and readiness pass after initial load
	// ---------------------------------------------------------------------------

	[Test]
	public async Task Liveness_and_readiness_pass_after_initial_load() {
		// Reuse the containers already started by the shared fixture.
		var bootstrapServers = DualKafkaClusterFixture.BootstrapServersA;

		var topicSuffix = Guid.NewGuid().ToString("N")[..8];
		var topic = $"{Topic}-{topicSuffix}";
		await DualKafkaClusterFixture.CreateTopicAsync(bootstrapServers, topic);

		// Statistics must be enabled so librdkafka fires the stats handler and
		// populates BrokerUpCount (used by the readiness check).
		// StatisticsIntervalSeconds must be > 1 due to the guard in KafkaCacheConsumer.
		using var sp = BuildServiceProvider(
			bootstrapServers,
			topic,
			g => {
				g.StatisticsEnabled = true;
				g.StatisticsIntervalSeconds = 2;
			});

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		await StartAndLoadAsync(sp, cts.Token);

		// Wait for at least two stats ticks (2 s interval) so BrokerUpCount is
		// populated even accounting for container cold-start latency.
		await Task.Delay(TimeSpan.FromSeconds(7), cts.Token);

		var statistics = sp.GetRequiredService<KafkaCachesStatistics>();
		var opts       = sp.GetRequiredService<IOptionsMonitor<KafkaCachesHealthOptions>>();
		var liveness   = new KafkaCachesLivenessHealthCheck(statistics, opts);
		var readiness  = new KafkaCachesReadinessHealthCheck(statistics, opts);
		var ctx        = new HealthCheckContext();

		var liveResult  = await liveness.CheckHealthAsync(ctx, cts.Token);
		var readyResult = await readiness.CheckHealthAsync(ctx, cts.Token);

		await StopAsync(sp);

		Assert.That(liveResult.Status,  Is.EqualTo(HealthStatus.Healthy), "Liveness should be Healthy after initial load");
		Assert.That(readyResult.Status, Is.EqualTo(HealthStatus.Healthy), "Readiness should be Healthy after initial load");
	}

	// ---------------------------------------------------------------------------
	// Test 2: Readiness degrades when broker goes down
	// ---------------------------------------------------------------------------

	[Test]
	public async Task Readiness_degrades_when_broker_goes_down() {
		// Spin up a dedicated container for this test so we can safely stop it
		// without affecting the other tests that rely on the shared fixture.
		var container = new KafkaBuilder("confluentinc/cp-kafka:7.5.0")
			.WithImage("confluentinc/cp-kafka:7.5.0")
			.Build();

		await container.StartAsync();

		try {
			var bootstrapServers = container.GetBootstrapAddress();
			var topicSuffix = Guid.NewGuid().ToString("N")[..8];
			var topic = $"{Topic}-{topicSuffix}";
			await DualKafkaClusterFixture.CreateTopicAsync(bootstrapServers, topic);

			using var sp = BuildServiceProvider(
				bootstrapServers,
				topic,
				g => {
					g.StatisticsEnabled = true;
					g.StatisticsIntervalSeconds = 2;
				});

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
			await StartAndLoadAsync(sp, cts.Token);

			// Confirm everything healthy before we pull the broker.
			await Task.Delay(TimeSpan.FromSeconds(7), cts.Token);

			var statistics = sp.GetRequiredService<KafkaCachesStatistics>();
			var opts       = sp.GetRequiredService<IOptionsMonitor<KafkaCachesHealthOptions>>();
			var liveness   = new KafkaCachesLivenessHealthCheck(statistics, opts);
			var readiness  = new KafkaCachesReadinessHealthCheck(statistics, opts);
			var ctx        = new HealthCheckContext();

			var liveBeforeStop  = await liveness.CheckHealthAsync(ctx, cts.Token);
			var readyBeforeStop = await readiness.CheckHealthAsync(ctx, cts.Token);

			Assert.That(liveBeforeStop.Status,  Is.EqualTo(HealthStatus.Healthy), "Liveness should be Healthy before broker stops");
			Assert.That(readyBeforeStop.Status, Is.EqualTo(HealthStatus.Healthy), "Readiness should be Healthy before broker stops");

			// Kill the Kafka container.
			await container.StopAsync();

			// librdkafka reports broker state every StatisticsIntervalSeconds = 1 s.
			// Wait several ticks to ensure at least one stats snapshot has landed with
			// BrokerUpCount = 0.
			await Task.Delay(TimeSpan.FromSeconds(10));

			var liveAfterStop  = await liveness.CheckHealthAsync(ctx);
			var readyAfterStop = await readiness.CheckHealthAsync(ctx);

			await StopAsync(sp);

			// The consume loop is still ticking (librdkafka reconnects), so
			// liveness must remain Healthy.
			Assert.That(liveAfterStop.Status, Is.EqualTo(HealthStatus.Healthy),
				"Liveness should remain Healthy even after broker stops (librdkafka keeps trying)");

			// Readiness should degrade because BrokerUpCount drops below MinBrokersUp.
			Assert.That(readyAfterStop.Status, Is.EqualTo(HealthStatus.Degraded),
				"Readiness should be Degraded after broker stops");
		}
		finally {
			await container.DisposeAsync();
		}
	}
}

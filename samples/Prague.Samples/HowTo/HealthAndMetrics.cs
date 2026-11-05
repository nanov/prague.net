// docs: every code block of www/docs/articles/how-to/health-and-metrics.md — the liveness/readiness checks,
// what their verdicts mean, and the OpenTelemetry instruments. The ASP.NET mapping is compiled-only; the
// evaluator, the health-check service and the meter registration run without a broker.
namespace Prague.Samples.HowTo;

using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using OpenTelemetry.Metrics;
using Prague.Kafka;
using Prague.Kafka.Health;
using Prague.Kafka.OpenTelemetry;

public static class HealthAndMetricsWiring {
	#region health-register
	public static void AddPragueHealth(this IServiceCollection services) {
		services.AddHealthChecks()
			.AddPragueKafkaLiveness(tags: ["live"])              // name "prague-kafka-live"; failure → Unhealthy
			.AddPragueKafkaReadiness(tags: ["ready"]);           // name "prague-kafka-ready"; failure → Degraded

		services.Configure<KafkaCachesHealthOptions>(o => {
			o.PollLoopHeartbeatTimeout = TimeSpan.FromSeconds(3);   // liveness: consumer poll loop silent this long
			o.HandlerProcessingTimeout = TimeSpan.FromSeconds(5);   // liveness: one message in a handler this long
			o.MinBrokersUp = 1;                                     // readiness: needs StatisticsEnabled (default); 0 to disable
		});
	}

	public static void MapPragueHealth(this WebApplication app) {
		app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
		app.MapHealthChecks("/health/ready", new HealthCheckOptions {
			Predicate = r => r.Tags.Contains("ready"),
			ResultStatusCodes = {
				[HealthStatus.Healthy] = StatusCodes.Status200OK,
				[HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,   // the default returns 200 for Degraded
				[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
			},
		});
	}
	#endregion

	#region health-concurrent-start
	public static void AnswerProbesDuringTheLoad(this IServiceCollection services) {
		// Start hosted services together, so the web server answers while the Kafka worker is still loading:
		// liveness judges the consumer, readiness reports InitialLoadIncomplete until the data is in.
		services.Configure<HostOptions>(o => o.ServicesStartConcurrently = true);
	}
	#endregion

	#region metrics-register
	public static void AddPragueMetrics(this IServiceCollection services) {
		services.AddOpenTelemetry().WithMetrics(m => m
			.AddPragueKafkaInstrumentation()                     // meter "Prague.Kafka": prague.kafka.* instruments
			// .AddPragueKafkaInstrumentation("acme")            // → acme.prague.kafka.*
			// .AddPrometheusExporter()                          // or OTLP; any exporter works
		);
	}
	#endregion
}

[TestFixture]
public class HealthAndMetrics {
	[Test]
	public void TheEvaluatorNamesTheFailures() {
		#region health-verdict
		var statistics = new KafkaCachesStatistics();           // the singleton AddKafkaCaches registers
		var consumer = statistics.GetOrAddConsumer("kafkaCaches");   // one per AddKafkaCaches section, never polled yet

		var verdict = KafkaCachesHealthEvaluator.Evaluate(consumer, new KafkaCachesHealthOptions());

		// Liveness: the poll loop has never reported in. Readiness: no broker is known to be UP.
		Console.WriteLine(string.Join(", ", verdict.LivenessFailures));    // PollLoopStalled
		Console.WriteLine(string.Join(", ", verdict.ReadinessFailures));   // BrokersDown
		#endregion

		Assert.That(verdict.IsLive, Is.False);
		Assert.That(verdict.IsReady, Is.False);
		Assert.That(verdict.LivenessFailures, Is.EqualTo(new[] { KafkaCachesHealthFailure.PollLoopStalled }));
		Assert.That(verdict.ReadinessFailures, Is.EqualTo(new[] { KafkaCachesHealthFailure.BrokersDown }));

		var healthy = KafkaCachesHealthEvaluator.Evaluate(consumer, new KafkaCachesHealthOptions {
			PollLoopHeartbeatTimeout = TimeSpan.FromDays(365_000),     // Stopwatch time since 0 is finite
			MinBrokersUp = 0,
		});
		Assert.That(healthy.IsReady, Is.True);
	}

	[Test]
	public async Task TheChecksReportThroughHealthCheckService() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<KafkaCachesStatistics>();
		services.AddPragueHealth();
		using var provider = services.BuildServiceProvider();

		#region health-report
		var health = provider.GetRequiredService<HealthCheckService>();

		var noConsumers = await health.CheckHealthAsync();      // before AddKafkaCaches started anything: nothing to judge
		provider.GetRequiredService<KafkaCachesStatistics>().GetOrAddConsumer("kafkaCaches");
		var stalled = await health.CheckHealthAsync();

		foreach (var (name, entry) in stalled.Entries)
			Console.WriteLine($"{name}: {entry.Status} {string.Join(" ", entry.Data.Select(d => $"{d.Key}={d.Value}"))}");
		// prague-kafka-live: Unhealthy kafkaCaches.failures=[PollLoopStalled]
		// prague-kafka-ready: Unhealthy kafkaCaches.liveness_failures=[PollLoopStalled] kafkaCaches.readiness_failures=[BrokersDown]
		#endregion

		Assert.That(noConsumers.Status, Is.EqualTo(HealthStatus.Healthy));
		Assert.That(stalled.Entries["prague-kafka-live"].Status, Is.EqualTo(HealthStatus.Unhealthy));
		Assert.That(stalled.Entries["prague-kafka-ready"].Status, Is.EqualTo(HealthStatus.Unhealthy), "a liveness failure makes readiness Unhealthy, not Degraded");
		Assert.That(stalled.Entries["prague-kafka-live"].Data, Does.ContainKey("kafkaCaches.failures"));
		Assert.That(stalled.Entries["prague-kafka-ready"].Data, Does.ContainKey("kafkaCaches.readiness_failures"));
	}

	[Test]
	public void TheMeterPublishesEightInstruments() {
		var services = new ServiceCollection();
		services.AddSingleton<KafkaCachesStatistics>();
		services.AddPragueMetrics();
		using var provider = services.BuildServiceProvider();

		#region metrics-instruments
		var names = new SortedSet<string>();
		using var listener = new MeterListener {
			InstrumentPublished = (instrument, _) => {
				if (instrument.Meter.Name == "Prague.Kafka")
					names.Add(instrument.Name);
			},
		};
		listener.Start();
		provider.GetRequiredService<MeterProvider>();           // builds the provider, which creates the instruments
		#endregion

		Assert.That(names, Is.EqualTo(new[] {
			"prague.kafka.cache.health",
			"prague.kafka.cache.index.capacity",
			"prague.kafka.cache.index.size",
			"prague.kafka.cache.messages.received",
			"prague.kafka.cache.size",
			"prague.kafka.consumer.broker.rtt.p99",
			"prague.kafka.consumer.health",
			"prague.kafka.consumer.partitions.assigned",
		}));
	}
}

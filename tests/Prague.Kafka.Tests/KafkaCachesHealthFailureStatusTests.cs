namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Prague.Kafka.Health;

/// <summary>
///   <c>AddPragueKafkaLiveness</c> / <c>AddPragueKafkaReadiness</c> take a <c>failureStatus</c>; the checks must
///   report it, so a caller can make readiness <see cref="HealthStatus.Unhealthy" /> instead of remapping
///   <see cref="HealthStatus.Degraded" /> to 503 on the endpoint. A registration not made by Prague carries
///   <see cref="HealthCheckRegistration" />'s Unhealthy default, which is not a choice: readiness failures
///   degrade there, as they did before the failureStatus was honoured.
/// </summary>
public sealed class KafkaCachesHealthFailureStatusTests {
	private static async Task<HealthReport> Check(Action<IHealthChecksBuilder> register, Action<KafkaCachesConsumerStatistics> arrange) {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<KafkaCachesStatistics>();
		register(services.AddHealthChecks());
		using var provider = services.BuildServiceProvider();
		arrange(provider.GetRequiredService<KafkaCachesStatistics>().GetOrAddConsumer("c1"));
		return await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
	}

	// Live (fresh poll heartbeat), not ready (no broker UP).
	private static void BrokersDown(KafkaCachesConsumerStatistics s) => s.LastPollTimestamp = Stopwatch.GetTimestamp();

	// Not live: the poll loop has not reported for longer than the 3 s default.
	private static void PollStalled(KafkaCachesConsumerStatistics s) =>
		s.LastPollTimestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 60;

	[Test]
	public async Task Readiness_reports_its_failureStatus() {
		var report = await Check(b => b.AddPragueKafkaReadiness(failureStatus: HealthStatus.Unhealthy), BrokersDown);
		Assert.That(report.Entries["prague-kafka-ready"].Status, Is.EqualTo(HealthStatus.Unhealthy));
	}

	// Caller tags and Prague's marker together: the endpoint filter still matches, and failureStatus is honoured.
	[Test]
	public async Task Readiness_with_caller_tags_reports_its_failureStatus() {
		var report = await Check(b => b.AddPragueKafkaReadiness(failureStatus: HealthStatus.Unhealthy, tags: ["ready"]), BrokersDown);
		var entry = report.Entries["prague-kafka-ready"];
		Assert.That(entry.Status, Is.EqualTo(HealthStatus.Unhealthy));
		Assert.That(entry.Tags, Does.Contain("ready"));
	}

	[Test]
	public async Task Readiness_defaults_to_Degraded() {
		var report = await Check(b => b.AddPragueKafkaReadiness(), BrokersDown);
		Assert.That(report.Entries["prague-kafka-ready"].Status, Is.EqualTo(HealthStatus.Degraded));
	}

	[Test]
	public async Task Readiness_is_Unhealthy_when_liveness_fails_whatever_its_failureStatus() {
		var report = await Check(b => b.AddPragueKafkaReadiness(failureStatus: HealthStatus.Degraded), PollStalled);
		Assert.That(report.Entries["prague-kafka-ready"].Status, Is.EqualTo(HealthStatus.Unhealthy));
	}

	[Test]
	public async Task Liveness_reports_its_failureStatus() {
		var report = await Check(b => b.AddPragueKafkaLiveness(failureStatus: HealthStatus.Degraded), PollStalled);
		Assert.That(report.Entries["prague-kafka-live"].Status, Is.EqualTo(HealthStatus.Degraded));
	}

	[Test]
	public async Task Liveness_defaults_to_Unhealthy() {
		var report = await Check(b => b.AddPragueKafkaLiveness(), PollStalled);
		Assert.That(report.Entries["prague-kafka-live"].Status, Is.EqualTo(HealthStatus.Unhealthy));
	}

	// The .NET default registration is Unhealthy: a check added by hand (not through AddPragueKafkaReadiness)
	// has made no choice, so readiness failures degrade rather than take the service out of rotation.
	[Test]
	public async Task Readiness_added_by_hand_degrades_at_the_Unhealthy_default() {
		var report = await Check(b => {
			b.Services.AddOptions<KafkaCachesHealthOptions>();
			b.AddCheck<KafkaCachesReadinessHealthCheck>("hand-added", failureStatus: HealthStatus.Unhealthy);
		}, BrokersDown);
		Assert.That(report.Entries["hand-added"].Status, Is.EqualTo(HealthStatus.Degraded));
	}

	// A consumer's own readiness check that wraps Prague's and passes its own HealthCheckContext — an untagged
	// registration.
	[Test]
	public async Task A_wrapper_context_degrades() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddOptions<KafkaCachesHealthOptions>();
		services.AddSingleton<KafkaCachesStatistics>();
		using var provider = services.BuildServiceProvider();
		BrokersDown(provider.GetRequiredService<KafkaCachesStatistics>().GetOrAddConsumer("c1"));
		var check = new KafkaCachesReadinessHealthCheck(
			provider.GetRequiredService<KafkaCachesStatistics>(),
			provider.GetRequiredService<IOptionsMonitor<KafkaCachesHealthOptions>>());
		var context = new HealthCheckContext {
			Registration = new HealthCheckRegistration("wrapped", check, HealthStatus.Unhealthy, tags: null),
		};
		var result = await check.CheckHealthAsync(context);
		Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
	}
}

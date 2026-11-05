namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prague.Kafka.Health;

/// <summary>
///   <c>AddPragueKafkaLiveness</c> / <c>AddPragueKafkaReadiness</c> take a <c>failureStatus</c>; the checks must
///   report it, so a caller can make readiness <see cref="HealthStatus.Unhealthy" /> instead of remapping
///   <see cref="HealthStatus.Degraded" /> to 503 on the endpoint.
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
}

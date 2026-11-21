namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

public static class HealthChecksBuilderExtensions {
	// Marks a registration as Prague's own, so the readiness check knows the failureStatus on it was chosen
	// (AddPragueKafkaReadiness's default is Degraded). A check added any other way — plain AddCheck, or a
	// wrapper passing its own context — carries no marker, and readiness failures there degrade, as they did
	// before the failureStatus was honoured: HealthCheckRegistration's own default is Unhealthy, which would
	// otherwise take services out of rotation on a lost partition or a down broker.
	public const string ReadinessTag = "prague-kafka-readiness";

	public static IHealthChecksBuilder AddPragueKafkaLiveness(
		this IHealthChecksBuilder builder,
		string name = "prague-kafka-live",
		HealthStatus failureStatus = HealthStatus.Unhealthy,
		IEnumerable<string>? tags = null) {
		builder.Services.AddOptions<KafkaCachesHealthOptions>();
		return builder.AddCheck<KafkaCachesLivenessHealthCheck>(name, failureStatus, tags ?? Array.Empty<string>());
	}

	public static IHealthChecksBuilder AddPragueKafkaReadiness(
		this IHealthChecksBuilder builder,
		string name = "prague-kafka-ready",
		HealthStatus failureStatus = HealthStatus.Degraded,
		IEnumerable<string>? tags = null) {
		builder.Services.AddOptions<KafkaCachesHealthOptions>();
		return builder.AddCheck<KafkaCachesReadinessHealthCheck>(
			name, failureStatus, tags is null ? [ReadinessTag] : [.. tags, ReadinessTag]);
	}
}

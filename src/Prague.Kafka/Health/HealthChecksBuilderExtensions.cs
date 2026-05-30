namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

public static class HealthChecksBuilderExtensions {
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
		return builder.AddCheck<KafkaCachesReadinessHealthCheck>(name, failureStatus, tags ?? Array.Empty<string>());
	}
}

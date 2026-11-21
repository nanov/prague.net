namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesReadinessHealthCheck : IHealthCheck {
	private static readonly Task<HealthCheckResult> _healthy =
		Task.FromResult(HealthCheckResult.Healthy());

	private readonly KafkaCachesStatistics _statistics;
	private readonly IOptionsMonitor<KafkaCachesHealthOptions> _options;

	public KafkaCachesReadinessHealthCheck(
		KafkaCachesStatistics statistics,
		IOptionsMonitor<KafkaCachesHealthOptions> options) {
		_statistics = statistics;
		_options = options;
	}

	public Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) {

		var opts = _options.CurrentValue;
		var anyLivenessFailure = false;
		var anyReadinessFailure = false;
		Dictionary<string, object>? data = null;

		foreach (var (consumerName, consumerStats) in _statistics.Consumers) {
			var verdict = KafkaCachesHealthEvaluator.Evaluate(consumerStats, opts);
			if (verdict.IsReady) continue;

			data ??= new Dictionary<string, object>();
			if (!verdict.IsLive) {
				anyLivenessFailure = true;
				data[consumerName + ".liveness_failures"] = verdict.LivenessFailures;
			}
			if (verdict.ReadinessFailures.Count > 0) {
				anyReadinessFailure = true;
				data[consumerName + ".readiness_failures"] = verdict.ReadinessFailures;
			}
			if (verdict.FailingCacheNames.Count > 0)
				data[consumerName + ".caches"] = verdict.FailingCacheNames;
		}

		if (anyLivenessFailure)
			return Task.FromResult(
				HealthCheckResult.Unhealthy(description: null, exception: null, data: data));
		// A readiness failure reports the failureStatus of a registration AddPragueKafkaReadiness made — its tag
		// marks the failureStatus as chosen, default Degraded (HealthChecksBuilderExtensions.ReadinessTag). Any
		// other registration (plain AddCheck, a wrapper passing its own context) carries HealthCheckRegistration's
		// Unhealthy default, which is not a choice: readiness failures there degrade, as they did before the
		// failureStatus was honoured, so caches served from memory stay in service through a lost partition.
		if (anyReadinessFailure) {
			var status = context.Registration is { } registration && registration.Tags.Contains(HealthChecksBuilderExtensions.ReadinessTag)
				? registration.FailureStatus
				: HealthStatus.Degraded;
			return Task.FromResult(new HealthCheckResult(status, data: data));
		}
		return _healthy;
	}
}

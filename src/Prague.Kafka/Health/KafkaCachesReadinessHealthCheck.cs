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
		if (anyReadinessFailure)
			return Task.FromResult(
				HealthCheckResult.Degraded(description: null, exception: null, data: data));
		return _healthy;
	}
}

namespace Prague.Kafka.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

public sealed class KafkaCachesLivenessHealthCheck : IHealthCheck {
	private static readonly Task<HealthCheckResult> _healthy =
		Task.FromResult(HealthCheckResult.Healthy());

	private readonly KafkaCachesStatistics _statistics;
	private readonly IOptionsMonitor<KafkaCachesHealthOptions> _options;

	public KafkaCachesLivenessHealthCheck(
		KafkaCachesStatistics statistics,
		IOptionsMonitor<KafkaCachesHealthOptions> options) {
		_statistics = statistics;
		_options = options;
	}

	public Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) {

		var opts = _options.CurrentValue;
		var anyFailure = false;
		Dictionary<string, object>? data = null;

		foreach (var (consumerName, consumerStats) in _statistics.Consumers) {
			var verdict = KafkaCachesHealthEvaluator.Evaluate(consumerStats, opts);
			if (verdict.IsLive) continue;

			anyFailure = true;
			data ??= new Dictionary<string, object>();
			data[consumerName + ".failures"] = verdict.LivenessFailures;
			if (verdict.FailingCacheNames.Count > 0)
				data[consumerName + ".caches"] = verdict.FailingCacheNames;
		}

		if (anyFailure)
			return Task.FromResult(
				HealthCheckResult.Unhealthy(description: null, exception: null, data: data));
		return _healthy;
	}
}

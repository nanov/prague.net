namespace Prague.Kafka.OpenTelemetry;

using Prague.Kafka.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::OpenTelemetry.Metrics;

public static class PragueKafkaInstrumentationExtensions {
	public static MeterProviderBuilder AddPragueKafkaInstrumentation(
		this MeterProviderBuilder builder, string prefix = "") {

		builder.AddMeter(PragueKafkaMetricsReporter.MeterName);
		if (builder is IDeferredMeterProviderBuilder deferred) {
			deferred.Configure((sp, b) => b.AddInstrumentation(() => new PragueKafkaMetricsReporter(
				sp.GetRequiredService<KafkaCachesStatistics>(),
				sp.GetRequiredService<IOptions<KafkaCachesHealthOptions>>().Value,
				prefix)));
		}
		return builder;
	}
}

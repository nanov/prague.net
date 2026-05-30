namespace Prague.Kafka.Health;

public sealed class KafkaCachesHealthOptions {
	/// <summary>
	/// Maximum allowed time since the last <c>consumer.Consume</c> return
	/// before liveness fails. Default: 3 seconds.
	/// </summary>
	public TimeSpan PollLoopHeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(3);

	/// <summary>
	/// Maximum allowed time a single message may stay in-flight inside a
	/// handler's channel loop before liveness fails. Default: 5 seconds.
	/// </summary>
	public TimeSpan HandlerProcessingTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Minimum number of brokers in "UP" state required for readiness.
	/// Default: 1.
	/// <para>
	/// Requires <c>KafkaCachesGlobalOptions.StatisticsEnabled = true</c> (the
	/// default). If you explicitly disable statistics, librdkafka stops
	/// reporting broker state, so the broker-UP counter stays at 0 and
	/// readiness will always be Degraded — set this to 0 in that case to
	/// disable the predicate.
	/// </para>
	/// </summary>
	public int MinBrokersUp { get; set; } = 1;
}

namespace Prague.Kafka.Options;

using Nanov.Kafka;

public sealed record KafkaCachesOptions {
	public required string BootstrapServers { get; set; }
	public Dictionary<string, string> ClientSettings { get; set; } = new();
	public Dictionary<string, string> Vars { get; init; } = new();

	/// <summary>
	///   SASL/OAUTHBEARER token refresh handler attached to this cluster's cache consumer and cache producer.
	///   librdkafka invokes it with the client and the <c>sasl.oauthbearer.config</c> value; it must call
	///   <see cref="ClientExtensions.OAuthBearerSetToken" /> or <see cref="ClientExtensions.OAuthBearerSetTokenFailure" />.
	/// </summary>
	public Action<IClient, string>? OAuthBearerTokenRefreshHandler { get; set; }
}

public sealed record KafkaCachesGlobalOptions {
	public uint StatisticsIntervalSeconds { get; set; } = 60;
	public bool StatisticsEnabled { get; set; } = true;
	public bool EndpointsMapped { get; set; } = false;
	internal HashSet<string> ClusterNames { get; } = new();
}

namespace Prague.Kafka.Options;

public sealed record KafkaCachesOptions {
	public required string BootstrapServers { get; set; }
	public Dictionary<string, string> ClientSettings { get; set; } = new();
	public Dictionary<string, string> Vars { get; init; } = new();
}

public sealed record KafkaCachesGlobalOptions {
	public uint StatisticsIntervalSeconds { get; set; } = 60;
	public bool StatisticsEnabled { get; set; } = true;
	public bool EndpointsMapped { get; set; } = false;
	internal HashSet<string> ClusterNames { get; } = new();
}

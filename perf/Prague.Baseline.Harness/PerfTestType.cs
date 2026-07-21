namespace Prague.Baseline.Harness;

using System;

/// <summary>A discovered perf-test type plus its kind (throughput / latency).</summary>
public sealed class PerfTestType {
	public PerfTestType(Type type) {
		Type = type;
		IsThroughput = typeof(IThroughputTest).IsAssignableFrom(type);
		IsLatency = typeof(ILatencyTest).IsAssignableFrom(type);
	}

	public Type Type { get; }
	public bool IsThroughput { get; }
	public bool IsLatency { get; }

	public string Name => Type.Name;
	public string? FullName => Type.FullName;
}

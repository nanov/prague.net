namespace Prague.Baseline.Harness;

/// <summary>
///   A latency perf test: send single operations through a worker / queue and record per-operation
///   round-trip (or one-way) latency into an HdrHistogram. Mirrors Disruptor-net's <c>ILatencyTest</c>.
/// </summary>
public interface ILatencyTest {
	/// <summary>Physical CPUs the test needs to run meaningfully.</summary>
	int RequiredProcessorCount { get; }

	/// <summary>Run one pass, recording samples into <paramref name="sessionContext" />'s histogram.</summary>
	void Run(LatencySessionContext sessionContext);
}

namespace Prague.Baseline.Harness;

/// <summary>
///   A throughput perf test: push a fixed number of operations through a worker / queue as fast as
///   possible and report operations-per-second. Mirrors Disruptor-net's <c>IThroughputTest</c>.
/// </summary>
public interface IThroughputTest {
	/// <summary>Physical CPUs the test needs to run meaningfully (producer + consumer = 2).</summary>
	int RequiredProcessorCount { get; }

	/// <summary>Run one pass; return the number of operations performed (used for the ops/sec figure).</summary>
	long Run(ThroughputSessionContext sessionContext);
}

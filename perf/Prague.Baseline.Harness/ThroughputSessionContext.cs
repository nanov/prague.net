namespace Prague.Baseline.Harness;

using System;
using System.Diagnostics;

/// <summary>
///   Per-run state for a throughput test: the elapsed-time stopwatch. Ported from Disruptor-net.
/// </summary>
public sealed class ThroughputSessionContext {
	private readonly Stopwatch _stopwatch = new();

	public TimeSpan ElapsedTime => _stopwatch.Elapsed;

	public void Reset() => _stopwatch.Reset();

	public void Start() => _stopwatch.Start();
	public void Stop() => _stopwatch.Stop();
}

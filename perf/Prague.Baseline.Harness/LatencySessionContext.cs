namespace Prague.Baseline.Harness;

using System;
using System.Diagnostics;
using HdrHistogram;

/// <summary>
///   Per-run state for a latency test: a stopwatch plus an HdrHistogram recording nanosecond samples
///   (range up to 10s, 4 significant digits). Ported from Disruptor-net.
/// </summary>
public sealed class LatencySessionContext {
	private readonly Stopwatch _stopwatch = new();

	public LongHistogram Histogram { get; } = new(10000000000L, 4);

	public TimeSpan ElapsedTime => _stopwatch.Elapsed;

	public void Reset() {
		Histogram.Reset();
		_stopwatch.Reset();
	}

	public void Start() => _stopwatch.Start();
	public void Stop() => _stopwatch.Stop();
}

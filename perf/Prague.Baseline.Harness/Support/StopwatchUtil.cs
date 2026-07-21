namespace Prague.Baseline.Harness.Support;

using System.Diagnostics;

/// <summary>
///   Converts between <see cref="Stopwatch" /> ticks and wall-clock units for latency sampling. Ported
///   from Disruptor-net's StopwatchUtil. <see cref="Stopwatch" /> ticks are NOT <see cref="System.TimeSpan" />
///   ticks — they run at <see cref="Stopwatch.Frequency" />.
/// </summary>
public static class StopwatchUtil {
	private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

	public static long GetTimestamp() => Stopwatch.GetTimestamp();

	public static long ToNanoseconds(long ticks) => (long)(ticks * NanosecondsPerTick);

	/// <summary>Number of stopwatch ticks spanning <paramref name="microseconds" />.</summary>
	public static long GetTimestampFromMicroseconds(long microseconds) =>
		(long)(microseconds * 1000.0 / NanosecondsPerTick);

	/// <summary>Number of stopwatch ticks spanning <paramref name="nanoseconds" />.</summary>
	public static long GetTimestampFromNanoseconds(long nanoseconds) =>
		(long)(nanoseconds / NanosecondsPerTick);
}

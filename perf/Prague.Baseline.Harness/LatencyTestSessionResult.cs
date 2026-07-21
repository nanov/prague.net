namespace Prague.Baseline.Harness;

using System;
using System.Globalization;
using System.IO;
using System.Text;
using HdrHistogram;

/// <summary>One run's latency result (or the failing exception). Ported from Disruptor-net.</summary>
public sealed class LatencyTestSessionResult {
	private readonly Exception? _exception;

	public LongHistogram? Histogram { get; }
	public TimeSpan Duration { get; }
	public int Gen0 { get; }
	public int Gen1 { get; }
	public int Gen2 { get; }

	public LatencyTestSessionResult(LongHistogram histogram, TimeSpan duration, int gen0, int gen1, int gen2) {
		Histogram = histogram;
		Duration = duration;
		Gen0 = gen0;
		Gen1 = gen1;
		Gen2 = gen2;
	}

	public LatencyTestSessionResult(Exception exception) => _exception = exception;

	public long P(double percentile) => Histogram?.GetValueAtPercentile(percentile) ?? 0;

	public void AppendDetailedHtmlReport(int runId, StringBuilder sb) {
		if (_exception != null || Histogram is null) {
			sb.AppendLine(" <tr>");
			sb.AppendLine($"     <td>{runId}</td>");
			sb.AppendLine("     <td>FAILED</td>");
			sb.AppendLine($"     <td>{_exception?.Message}</td>");
			sb.AppendLine("     <td></td>");
			sb.AppendLine(" </tr>");
			return;
		}

		sb.AppendLine(" <tr>");
		sb.AppendLine($"     <td>{runId}</td>");
		sb.AppendLine("     <td><pre>");
		using (var writer = new StringWriter(sb))
			Histogram.OutputPercentileDistribution(writer, 1, 1000.0);
		sb.AppendLine("</pre></td>");
		sb.AppendLine($"     <td>{Duration.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)} (ms)</td>");
		sb.AppendLine($"     <td>{Gen0} - {Gen1} - {Gen2}</td>");
		sb.AppendLine(" </tr>");
	}

	public override string ToString() {
		if (_exception != null)
			return $"Run: FAILED: {_exception.Message}";

		return FormattableString.Invariant(
			$"Run: Duration: {Duration.TotalMilliseconds:N0} ms - GC: {Gen0}-{Gen1}-{Gen2} - Median: {Us(50)} - P90: {Us(90)} - P99: {Us(99)} - P99.9: {Us(99.9)}");

		string Us(double percentile) => $"{P(percentile) / 1000.0:0.00} µs";
	}
}

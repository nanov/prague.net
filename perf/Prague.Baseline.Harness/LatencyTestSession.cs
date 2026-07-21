namespace Prague.Baseline.Harness;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Prague.Baseline.Harness.Support;

/// <summary>
///   Runs a latency test N times, collecting per-run HdrHistograms + GC counts, prints percentile
///   summaries, and optionally writes an HTML report + appends to a daily CSV. Ported from Disruptor-net.
/// </summary>
public sealed class LatencyTestSession : IDisposable {
	private readonly ILatencyTest _test;
	private readonly ProgramOptions _options;
	private readonly string _resultDirectoryPath;

	public LatencyTestSession(ILatencyTest test, ProgramOptions options, string resultDirectoryPath) {
		_test = test;
		_options = options;
		_resultDirectoryPath = resultDirectoryPath;
	}

	public List<LatencyTestSessionResult> Execute() {
		var results = Run();
		Report(results);
		return results;
	}

	private List<LatencyTestSessionResult> Run() {
		Console.WriteLine();
		Console.Write($"Latency Test => {_test.GetType().Name}, Runs => {_options.RunCountForLatencyTest}");
		if (_options.HasCustomCpuSet)
			Console.Write($", Cpus: [{string.Join(", ", _options.CpuSet)}]");
		Console.WriteLine();

		var results = new List<LatencyTestSessionResult>();
		var context = new LatencySessionContext();

		for (var i = 0; i < _options.RunCountForLatencyTest; i++) {
			GC.Collect();
			GC.WaitForPendingFinalizers();

			context.Reset();

			var beforeGen0 = GC.CollectionCount(0);
			var beforeGen1 = GC.CollectionCount(1);
			var beforeGen2 = GC.CollectionCount(2);

			LatencyTestSessionResult result;
			try {
				_test.Run(context);
				result = new LatencyTestSessionResult(
					context.Histogram, context.ElapsedTime,
					GC.CollectionCount(0) - beforeGen0,
					GC.CollectionCount(1) - beforeGen1,
					GC.CollectionCount(2) - beforeGen2);
			}
			catch (Exception ex) {
				result = new LatencyTestSessionResult(ex);
			}

			Console.WriteLine(result);
			results.Add(result);
		}

		return results;
	}

	private void Report(List<LatencyTestSessionResult> results) {
		var spec = ComputerSpecifications.GetCurrent();
		if (_options.PrintComputerSpecifications) {
			Console.WriteLine();
			Console.Write(spec.ToString());
		}

		if (!_options.GenerateReport)
			return;

		Directory.CreateDirectory(_resultDirectoryPath);
		var path = Path.Combine(_resultDirectoryPath, $"{_test.GetType().Name}-{DateTime.UtcNow:yyyy-MM-dd HH-mm-ss}.html");
		File.WriteAllText(path, BuildReport(results, spec));

		var totalsPath = Path.Combine(_resultDirectoryPath, $"Totals-{DateTime.Now:yyyy-MM-dd}.csv");
		foreach (var result in results)
			File.AppendAllText(totalsPath,
				FormattableString.Invariant($"{DateTime.Now:HH:mm:ss},{_test.GetType().Name},{result.P(50)},{result.P(90)},{result.P(99)}{Environment.NewLine}"));

		if (_options.OpenReport)
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
	}

	private string BuildReport(List<LatencyTestSessionResult> results, ComputerSpecifications spec) {
		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE HTML>")
			.AppendLine("<html><head><title>Prague Baseline - Latency Report</title></head><body>")
			.AppendLine("Local time: " + DateTime.Now + "<br>")
			.AppendLine("UTC time: " + DateTime.UtcNow)
			.AppendLine("<h2>Host configuration</h2>");
		spec.AppendHtml(sb);
		if (!Stopwatch.IsHighResolution)
			sb.AppendLine("<b><font color='red'>No high-resolution timer — latencies may be inaccurate.</font></b><br>");

		sb.AppendLine("<h2>Test configuration</h2>")
			.AppendLine("Test: " + _test.GetType().FullName + "<br>")
			.AppendLine("Runs: " + _options.RunCountForLatencyTest + "<br>")
			.AppendLine("<h2>Detailed test results</h2>")
			.AppendLine("<table border=\"1\">")
			.AppendLine("<tr><td>Run</td><td>Latencies (hdr histogram)</td><td>Duration (ms)</td><td># GC (0-1-2)</td></tr>");
		for (var i = 0; i < results.Count; i++)
			results[i].AppendDetailedHtmlReport(i, sb);
		sb.AppendLine("</table></body></html>");
		return sb.ToString();
	}

	public void Dispose() {
		if (_test is IDisposable disposable)
			disposable.Dispose();
	}
}

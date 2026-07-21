namespace Prague.Baseline.Harness;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Prague.Baseline.Harness.Support;

/// <summary>
///   Runs a throughput test N times, collecting per-run ops/sec and GC counts, prints each run, and
///   optionally writes an HTML report + appends to a daily CSV. Ported from Disruptor-net.
/// </summary>
public sealed class ThroughputTestSession : IDisposable {
	private readonly IThroughputTest _test;
	private readonly ProgramOptions _options;
	private readonly string _resultDirectoryPath;

	public ThroughputTestSession(IThroughputTest test, ProgramOptions options, string resultDirectoryPath) {
		_test = test;
		_options = options;
		_resultDirectoryPath = resultDirectoryPath;
	}

	public List<ThroughputTestSessionResult> Execute() {
		var results = Run();
		Report(results);
		return results;
	}

	private List<ThroughputTestSessionResult> Run() {
		Console.WriteLine();
		Console.Write($"Throughput Test => {_test.GetType().Name}, Runs => {_options.RunCountForThroughputTest}");
		if (_options.HasCustomCpuSet)
			Console.Write($", Cpus: [{string.Join(", ", _options.CpuSet)}]");
		Console.WriteLine();

		var results = new List<ThroughputTestSessionResult>();
		var context = new ThroughputSessionContext();

		for (var i = 0; i < _options.RunCountForThroughputTest; i++) {
			GC.Collect();
			GC.WaitForPendingFinalizers();

			context.Reset();

			var beforeGen0 = GC.CollectionCount(0);
			var beforeGen1 = GC.CollectionCount(1);
			var beforeGen2 = GC.CollectionCount(2);

			ThroughputTestSessionResult result;
			try {
				var totalOperations = _test.Run(context);
				result = new ThroughputTestSessionResult(
					totalOperations, context.ElapsedTime,
					GC.CollectionCount(0) - beforeGen0,
					GC.CollectionCount(1) - beforeGen1,
					GC.CollectionCount(2) - beforeGen2);
			}
			catch (Exception ex) {
				result = new ThroughputTestSessionResult(ex);
			}

			Console.WriteLine(result);
			results.Add(result);
		}

		return results;
	}

	private void Report(List<ThroughputTestSessionResult> results) {
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
		var average = results.Where(r => r.Duration > TimeSpan.Zero).Select(r => r.OpsPerSecond).DefaultIfEmpty(0).Average();
		File.AppendAllText(totalsPath,
			FormattableString.Invariant($"{DateTime.Now:HH:mm:ss},{_test.GetType().Name},{average}{Environment.NewLine}"));

		if (_options.OpenReport)
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
	}

	private string BuildReport(List<ThroughputTestSessionResult> results, ComputerSpecifications spec) {
		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE HTML>")
			.AppendLine("<html><head><title>Prague Baseline - Throughput Report</title></head><body>")
			.AppendLine("Local time: " + DateTime.Now + "<br>")
			.AppendLine("UTC time: " + DateTime.UtcNow)
			.AppendLine("<h2>Host configuration</h2>");
		spec.AppendHtml(sb);
		if (spec.PhysicalCoreCount < 2)
			sb.AppendLine("<b><font color='red'>These tests want at least 2 physical cores.</font></b><br>");

		sb.AppendLine("<h2>Test configuration</h2>")
			.AppendLine("Test: " + _test.GetType().FullName + "<br>")
			.AppendLine("Runs: " + _options.RunCountForThroughputTest + "<br>")
			.AppendLine("<h2>Detailed test results</h2>")
			.AppendLine("<table border=\"1\">")
			.AppendLine("<tr><td>Run</td><td>Ops/sec</td><td>Duration (ms)</td><td># GC (0-1-2)</td></tr>");
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

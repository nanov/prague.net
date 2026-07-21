namespace Prague.Baseline.Harness;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public static class Program {
	public static void Main(string[] args) {
		if (!ProgramOptions.TryParse(args, out var options)) {
			ProgramOptions.PrintUsage();
			return;
		}

		if (!options.Validate())
			return;

		if (options.ConfigSpecified) {
			RunConfig(options);
			return;
		}

		var testTypes = new PerfTestTypeSelector(options).GetPerfTestTypes();
		foreach (var testType in testTypes)
			RunTestForType(testType, options);
	}

	private static void RunTestForType(PerfTestType perfTestType, ProgramOptions options) {
		var outputDirectoryPath = Path.Combine(AppContext.BaseDirectory, "results");

		if (perfTestType.IsThroughput) {
			var test = (IThroughputTest)CreateTest(perfTestType, options);
			if (!ValidateTest(test.RequiredProcessorCount, options))
				return;
			using var session = new ThroughputTestSession(test, options, outputDirectoryPath);
			session.Execute();
			return;
		}

		if (perfTestType.IsLatency) {
			var test = (ILatencyTest)CreateTest(perfTestType, options);
			if (!ValidateTest(test.RequiredProcessorCount, options))
				return;
			using var session = new LatencyTestSession(test, options, outputDirectoryPath);
			session.Execute();
			return;
		}

		throw new NotSupportedException($"Invalid test type: {perfTestType.Name}");
	}

	// config -> the test class names that make it up. A named config runs exactly these and emits the
	// common-schema <config>.json. Tests that do not yet exist (e.g. RealIngestThroughputTest, Task 3)
	// are referenced by name and skipped gracefully when absent.
	private static readonly Dictionary<string, string[]> ConfigTests = new(StringComparer.OrdinalIgnoreCase) {
		["full-sim"] = ["SimIngestThroughputTest", "QueryMixLatencyTest"],
		["core-only"] = ["CoreIngestThroughputTest", "QueryMixLatencyTest"],
		["full-real"] = ["RealIngestThroughputTest", "QueryMixLatencyTest"],
		["concurrent"] = ["ConcurrentReadThroughputTest", "ConcurrentQueryLatencyTest"],
	};

	private static void RunConfig(ProgramOptions options) {
		if (!ConfigTests.TryGetValue(options.Config, out var testNames)) {
			Console.Error.WriteLine($"Unknown config: {options.Config}. Expected one of: {string.Join(", ", ConfigTests.Keys)}.");
			return;
		}

		var outputDirectoryPath = Path.Combine(AppContext.BaseDirectory, "results");
		var discovered = Discover();

		var ingestOps = 0d;
		long p50 = 0, p99 = 0, p999 = 0;

		foreach (var name in testNames) {
			var perfTestType = discovered.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			if (perfTestType is null) {
				Console.WriteLine($"[baseline] test {name} not found; skipping.");
				continue;
			}

			if (perfTestType.IsThroughput) {
				var test = (IThroughputTest)CreateTest(perfTestType, options);
				if (!ValidateTest(test.RequiredProcessorCount, options))
					continue;
				using var session = new ThroughputTestSession(test, options, outputDirectoryPath);
				var opsPerRun = session.Execute()
					.Where(r => r.Duration > TimeSpan.Zero)
					.Select(r => r.OpsPerSecond)
					.ToList();
				if (opsPerRun.Count > 0)
					ingestOps = Median(opsPerRun);
				continue;
			}

			if (perfTestType.IsLatency) {
				var test = (ILatencyTest)CreateTest(perfTestType, options);
				if (!ValidateTest(test.RequiredProcessorCount, options))
					continue;
				using var session = new LatencyTestSession(test, options, outputDirectoryPath);
				var valid = session.Execute()
					.Where(r => r.Histogram is not null)
					.OrderBy(r => r.P(50))
					.ToList();
				if (valid.Count > 0) {
					var median = valid[valid.Count / 2];
					p50 = median.P(50);
					p99 = median.P(99);
					p999 = median.P(99.9);
				}
				continue;
			}

			throw new NotSupportedException($"Invalid test type: {perfTestType.Name}");
		}

		var (throughputMetricId, throughputUnit) = options.Config.Equals("concurrent", StringComparison.OrdinalIgnoreCase)
			? ("read.throughput", "reads/s")
			: ("ingest.throughput", "ent/s");
		HarnessResultExport.Emit(options.Config, options.OutPath, throughputMetricId, throughputUnit, ingestOps, p50, p99, p999);
	}

	private static List<PerfTestType> Discover() =>
		Assembly.GetExecutingAssembly()
			.GetTypes()
			.Where(t => t is { IsClass: true, IsAbstract: false } &&
				(typeof(IThroughputTest).IsAssignableFrom(t) || typeof(ILatencyTest).IsAssignableFrom(t)))
			.Select(t => new PerfTestType(t))
			.ToList();

	// Median of the per-run values: sort ascending and take the middle element.
	private static double Median(List<double> values) {
		values.Sort();
		return values[values.Count / 2];
	}

	// Tests take either (ProgramOptions) — to read the CPU set — or a parameterless ctor.
	private static object CreateTest(PerfTestType testType, ProgramOptions options) {
		var withOptions = testType.Type.GetConstructor([typeof(ProgramOptions)]);
		if (withOptions is not null)
			return withOptions.Invoke([options]);

		return Activator.CreateInstance(testType.Type)
			?? throw new InvalidOperationException($"Could not instantiate {testType.Name}.");
	}

	private static bool ValidateTest(int requiredProcessorCount, ProgramOptions options) {
		var available = Environment.ProcessorCount;
		if (requiredProcessorCount > available) {
			Console.Error.WriteLine("Error: insufficient CPUs to run the test efficiently.");
			Console.Error.WriteLine($"Required = {requiredProcessorCount}, available = {available}");
			return false;
		}

		if (options.HasCustomCpuSet && requiredProcessorCount > options.CpuSet.Length) {
			Console.Error.WriteLine("Error: CPU set too small for the test.");
			Console.Error.WriteLine($"Required = {requiredProcessorCount}, CPU set length = {options.CpuSet.Length}");
			return false;
		}

		return true;
	}
}

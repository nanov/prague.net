namespace Prague.Baseline.Harness;

using System;
using System.IO;

public static class Program {
	public static void Main(string[] args) {
		if (!ProgramOptions.TryParse(args, out var options)) {
			ProgramOptions.PrintUsage();
			return;
		}

		if (!options.Validate())
			return;

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

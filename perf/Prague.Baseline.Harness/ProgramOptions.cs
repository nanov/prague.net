namespace Prague.Baseline.Harness;

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
///   Command-line options for the perf runner. Trimmed port of Disruptor-net's <c>ProgramOptions</c>
///   (the Disruptor-specific wait-strategy / IPC switches are dropped — our workers have neither).
/// </summary>
public sealed class ProgramOptions {
	public static int DefaultRunCountForLatencyTest => 3;
	public static int DefaultRunCountForThroughputTest => 7;
	public static int[] DefaultCpuSet { get; } = Enumerable.Range(0, Environment.ProcessorCount).ToArray();

	private int[] _cpuSet = DefaultCpuSet;

	public int? RunCount { get; private set; }
	public string? Target { get; private set; }
	public string? From { get; private set; }
	public bool PrintComputerSpecifications { get; private set; } = true;
	public bool GenerateReport { get; private set; } = true;
	public bool OpenReport { get; private set; }
	public bool IncludeLatency { get; private set; } = true;
	public bool IncludeThroughput { get; private set; } = true;

	public int[] CpuSet {
		get => _cpuSet;
		private set {
			_cpuSet = value;
			HasCustomCpuSet = true;
		}
	}

	public bool HasCustomCpuSet { get; private set; }

	public int RunCountForLatencyTest => RunCount ?? DefaultRunCountForLatencyTest;
	public int RunCountForThroughputTest => RunCount ?? DefaultRunCountForThroughputTest;

	/// <summary>The CPU index to pin the role at <paramref name="index" /> to, or null when no custom set was given.</summary>
	public int? GetCustomCpu(int index) =>
		HasCustomCpuSet && index < CpuSet.Length ? CpuSet[index] : null;

	public static bool TryParse(string[] args, out ProgramOptions options) {
		options = new ProgramOptions();

		for (var index = 0; index < args.Length; index++) {
			var arg = args[index];

			if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase)) {
				if (index + 1 == args.Length) return false;
				options.Target = args[++index];
				continue;
			}

			if (arg.Equals("--from", StringComparison.OrdinalIgnoreCase)) {
				if (index + 1 == args.Length) return false;
				options.From = args[++index];
				continue;
			}

			if (arg.Equals("--runs", StringComparison.OrdinalIgnoreCase)) {
				if (index + 1 == args.Length || !int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out var runs) || runs <= 0)
					return false;
				options.RunCount = runs;
				index++;
				continue;
			}

			if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase)) {
				if (!TryParseBool(args, ref index, out var value)) return false;
				options.GenerateReport = value;
				continue;
			}

			if (arg.Equals("--open-report", StringComparison.OrdinalIgnoreCase)) {
				if (!TryParseBool(args, ref index, out var value)) return false;
				options.OpenReport = value;
				continue;
			}

			if (arg.Equals("--print-spec", StringComparison.OrdinalIgnoreCase)) {
				if (!TryParseBool(args, ref index, out var value)) return false;
				options.PrintComputerSpecifications = value;
				continue;
			}

			if (arg.Equals("--include-latency", StringComparison.OrdinalIgnoreCase)) {
				if (!TryParseBool(args, ref index, out var value)) return false;
				options.IncludeLatency = value;
				continue;
			}

			if (arg.Equals("--include-throughput", StringComparison.OrdinalIgnoreCase)) {
				if (!TryParseBool(args, ref index, out var value)) return false;
				options.IncludeThroughput = value;
				continue;
			}

			if (arg.Equals("--cpus", StringComparison.OrdinalIgnoreCase)) {
				if (index + 1 == args.Length ||
					Regex.Match(args[index + 1], @"^(?<cpu0>\d+)(?:,(?<cpun>\d+))*$") is not { Success: true } match)
					return false;
				options.CpuSet = [
					int.Parse(match.Groups["cpu0"].Value, CultureInfo.InvariantCulture),
					.. match.Groups["cpun"].Captures.Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture))
				];
				index++;
				continue;
			}

			return false;
		}

		return true;
	}

	private static bool TryParseBool(string[] args, ref int index, out bool value) {
		value = false;
		if (index + 1 == args.Length || !bool.TryParse(args[index + 1], out value))
			return false;
		index++;
		return true;
	}

	public bool Validate() {
		if (HasCustomCpuSet && CpuSet.Except(DefaultCpuSet).Any()) {
			Console.WriteLine($"Invalid cpus: [{string.Join(", ", CpuSet)}], available CPU range: [0-{Environment.ProcessorCount - 1}]");
			return false;
		}

		return true;
	}

	public static void PrintUsage() {
		var options = new ProgramOptions();
		Console.WriteLine("Usage:");
		Console.WriteLine($"  {AppDomain.CurrentDomain.FriendlyName} [options]");
		Console.WriteLine();
		Console.WriteLine("Options:");
		Console.WriteLine("  --target <name|all>                 Test name (substring match) or \"all\". Default: interactive selection.");
		Console.WriteLine("  --from <name>                       First test to run when running all (alphabetical).");
		Console.WriteLine($"  --runs <count>                      Runs per test. Default: {DefaultRunCountForThroughputTest} throughput / {DefaultRunCountForLatencyTest} latency.");
		Console.WriteLine($"  --report <true|false>               Write an HTML report. Default: {options.GenerateReport}.");
		Console.WriteLine($"  --open-report <true|false>          Open the HTML report when done. Default: {options.OpenReport}.");
		Console.WriteLine($"  --print-spec <true|false>           Print computer specifications. Default: {options.PrintComputerSpecifications}.");
		Console.WriteLine($"  --include-latency <true|false>      Include latency tests. Default: {options.IncludeLatency}.");
		Console.WriteLine($"  --include-throughput <true|false>   Include throughput tests. Default: {options.IncludeThroughput}.");
		Console.WriteLine("  --cpus <c0,c1,...>                  CPU set for affinity. c0 = producer, c1 = consumer.");
		Console.WriteLine();
	}
}

namespace Prague.Benchmarks;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

internal static class Program {
	/// <summary>The two production shapes every quick run carries as the no-tax check.</summary>
	private static readonly string[] QuickTrio = ["ListListListSortBoundedJoinOne", "TimeWindowListListSortBoundedJoinTwo"];

	private static void Main(string[] args) {
		if (args.Length > 0 && args[0] == "--quick") {
			RunQuick(args.AsSpan(1));
			return;
		}

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}

	/// <summary>
	///   <c>--quick [--target &lt;category&gt;]...</c> — one short in-process job over shapes A and B plus the
	///   given categories, ending in one line per method: mean, ratio to the category's baseline, bytes per
	///   operation. It steers work; a keep-or-kill decision still takes one full single-category run.
	/// </summary>
	private static void RunQuick(ReadOnlySpan<string> rest) {
		var categories = new List<string>(QuickTrio);
		for (var i = 0; i < rest.Length; i++) {
			if (rest[i] == "--target" && i + 1 < rest.Length)
				categories.Add(rest[++i]);
			else if (!rest[i].StartsWith("--", StringComparison.Ordinal))
				categories.Add(rest[i]);
		}

		var job = Job.ShortRun
			.WithToolchain(InProcessNoEmitToolchain.Instance)
			.WithWarmupCount(3)
			.WithIterationCount(5)
			.WithId("Quick");

		var config = ManualConfig.CreateEmpty()
			.AddJob(job)
			.AddFilter(new AnyCategoriesFilter(categories.ToArray()))
			.AddColumnProvider(DefaultColumnProviders.Instance)
			.AddLogger(ConsoleLogger.Default)
			.WithOptions(ConfigOptions.DisableLogFile | ConfigOptions.JoinSummary);

		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(["--filter", "*"], config);

		Console.WriteLine();
		Console.WriteLine("quick — short job, steer only; decide on a full single-category run");
		Console.WriteLine($"{"category",-44} {"method",-16} {"mean",12} {"ratio",7} {"alloc",8}");
		foreach (var summary in summaries)
		foreach (var group in summary.Reports.GroupBy(r => r.BenchmarkCase.Descriptor.Categories.FirstOrDefault() ?? string.Empty).OrderBy(g => g.Key)) {
			var baseline = group.FirstOrDefault(r => r.BenchmarkCase.Descriptor.Baseline)?.ResultStatistics?.Mean;
			foreach (var report in group) {
				var mean = report.ResultStatistics?.Mean;
				var ratio = mean is { } m && baseline is { } b && m > 0 ? $"{b / m,7:F2}" : $"{"-",7}";
				var alloc = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
				var name = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
				var suffix = name.LastIndexOf('_') is var cut and >= 0 ? name[(cut + 1)..] : name;
				Console.WriteLine($"{group.Key,-44} {suffix,-16} {FormatNs(mean),12} {ratio} {FormatBytes(alloc),8}");
			}
		}
	}

	private static string FormatNs(double? ns) => ns switch {
		null => "-",
		>= 1_000_000 => $"{ns.Value / 1_000_000:F2} ms",
		>= 1_000 => $"{ns.Value / 1_000:F2} us",
		_ => $"{ns.Value:F1} ns",
	};

	private static string FormatBytes(long? bytes) => bytes switch {
		null => "-",
		0 => "0 B",
		_ => $"{bytes.Value} B",
	};
}

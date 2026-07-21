namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Running;

internal static class Program {
	private static void Main(string[] args) {
		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		BdnResultExport.Emit(summaries, "perf/out/core-only.json");
	}
}

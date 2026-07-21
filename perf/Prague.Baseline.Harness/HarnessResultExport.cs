namespace Prague.Baseline.Harness;

using Prague.Baseline.Scenario;

/// <summary>
///   Maps the harness session metrics into the common <see cref="BaselineResult" /> schema and writes
///   <c>&lt;config&gt;.json</c> into the output directory. Metric ids are stable and shared with the BDN engine.
/// </summary>
internal static class HarnessResultExport {
	public static void Emit(string config, string outDir,
		double ingestOpsPerSecMedian, long p50Ns, long p99Ns, long p999Ns) {
		var metrics = new List<Metric> {
			new("ingest.throughput", "ent/s", ingestOpsPerSecMedian, true),
			new("query.multiJoin.p50", "ns", p50Ns, false),
			new("query.multiJoin.p99", "ns", p99Ns, false),
			new("query.multiJoin.p999", "ns", p999Ns, false),
		};
		var result = new BaselineResult(
			EnvCapture.MachineClass(), config,
			Environment.GetEnvironmentVariable("PRAGUE_PERF_COMMIT") ?? "local",
			DateTime.UtcNow.ToString("O"), EnvCapture.Current(), metrics);
		ResultWriter.Write(Path.Combine(outDir, config + ".json"), result);
		Console.WriteLine($"[baseline] wrote {config}.json");
	}
}

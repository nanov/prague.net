namespace Prague.Baseline.Scenario;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record Metric(string Id, string Unit, double Value, bool HigherIsBetter);

public sealed record EnvBlock(string Cpu, string Os, string Dotnet, int CoreCount);

public sealed record BaselineResult(
	string MachineClass, string Config, string Commit, string TimestampUtc,
	EnvBlock Env, IReadOnlyList<Metric> Metrics);

public static class ResultWriter {
	private static readonly JsonSerializerOptions Options = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
	};

	public static string ToJson(BaselineResult result) => JsonSerializer.Serialize(result, Options);

	public static void Write(string path, BaselineResult result) {
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(path, ToJson(result));
	}
}

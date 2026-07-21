namespace Prague.Core.Tests;

using System.Text.Json;
using NUnit.Framework;
using Prague.Baseline.Scenario;

[TestFixture]
public class BaselineResultSchemaTests {
	[Test]
	public void ToJson_RoundTrips_MetricsAndCamelCases() {
		var result = new BaselineResult(
			"apple-m4pro-darwin", "core-only", "abc123", "2026-07-21T00:00:00Z",
			new EnvBlock("Apple M4 Pro", "Darwin", ".NET 9.0", 12),
			new[] { new Metric("ingest.throughput", "ent/s", 1000.0, true) });

		var json = ResultWriter.ToJson(result);
		Assert.That(json, Does.Contain("\"machineClass\""));
		Assert.That(json, Does.Contain("\"ingest.throughput\""));

		using var doc = JsonDocument.Parse(json);
		Assert.That(doc.RootElement.GetProperty("metrics").GetArrayLength(), Is.EqualTo(1));
		Assert.That(doc.RootElement.GetProperty("metrics")[0].GetProperty("higherIsBetter").GetBoolean(), Is.True);
	}

	[Test]
	public void Write_BareFilename_CreatesFileWithoutThrowing() {
		var result = new BaselineResult(
			"apple-m4pro-darwin", "core-only", "abc123", "2026-07-21T00:00:00Z",
			new EnvBlock("Apple M4 Pro", "Darwin", ".NET 9.0", 12),
			new[] { new Metric("ingest.throughput", "ent/s", 1000.0, true) });

		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		var originalDir = Directory.GetCurrentDirectory();
		try {
			Directory.SetCurrentDirectory(tempDir);
			ResultWriter.Write("baseline-write-test.json", result);
			Assert.That(File.Exists("baseline-write-test.json"), Is.True);
		} finally {
			Directory.SetCurrentDirectory(originalDir);
			Directory.Delete(tempDir, recursive: true);
		}
	}
}

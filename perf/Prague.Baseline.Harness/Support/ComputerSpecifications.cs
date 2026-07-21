namespace Prague.Baseline.Harness.Support;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
///   Host / runtime description for the report header. Dependency-free version (Disruptor-net uses
///   Hardware.Info for physical-core counts; here we report logical processors and let the OS layer
///   fill in the rest). Physical-core detection is best-effort and may be unknown.
/// </summary>
public sealed class ComputerSpecifications {
	public static ComputerSpecifications GetCurrent() => new();

	public int LogicalCoreCount => Environment.ProcessorCount;

	// Without a hardware-info dependency we can't reliably split physical vs logical cores; assume the
	// logical count as a lower bound for the "enough cores?" warning.
	public int PhysicalCoreCount => Environment.ProcessorCount;

	public override string ToString() {
		var sb = new StringBuilder();
		foreach (var line in GetLines())
			sb.AppendLine(line);
		return sb.ToString();
	}

	public void AppendHtml(StringBuilder sb) {
		foreach (var line in GetLines()) {
			sb.Append(line);
			sb.AppendLine("<br>");
		}
	}

	private static IEnumerable<string> GetLines() {
		yield return $"OperatingSystem: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
		yield return $"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier})";
		yield return $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}";
		yield return $"LogicalProcessors: {Environment.ProcessorCount}";
		yield return $"GC: {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}, Concurrent: {System.Runtime.GCSettings.LatencyMode}";
	}
}

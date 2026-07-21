namespace Prague.Baseline.Scenario;

using System.Runtime.InteropServices;

public static class EnvCapture {
	public static EnvBlock Current() => new(
		Cpu: CpuName(),
		Os: RuntimeInformation.OSDescription,
		Dotnet: RuntimeInformation.FrameworkDescription,
		CoreCount: Environment.ProcessorCount);

	public static string MachineClass() {
		var env = Environment.GetEnvironmentVariable("PRAGUE_PERF_MACHINE");
		if (!string.IsNullOrWhiteSpace(env)) return env;
		var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin"
			: RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
			: "windows";
		var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		return Slug(CpuName()) + "-" + os + "-" + arch;
	}

	private static string CpuName() {
		// Best-effort; overridden by PRAGUE_PERF_MACHINE in CI.
		return Environment.GetEnvironmentVariable("PRAGUE_PERF_CPU") ?? "cpu";
	}

	private static string Slug(string s) {
		if (s.Length > 256) s = s[..256];
		Span<char> buf = stackalloc char[s.Length];
		var n = 0;
		foreach (var c in s) {
			if (char.IsLetterOrDigit(c)) buf[n++] = char.ToLowerInvariant(c);
			else if (n > 0 && buf[n - 1] != '-') buf[n++] = '-';
		}
		return new string(buf[..n]).Trim('-');
	}
}

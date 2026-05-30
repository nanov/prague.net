namespace Prague.Kafka.Internal;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Confluent.Kafka;
#if NET9_0_OR_GREATER
using Ascii = System.Text.Ascii;

#else
using Ascii = System.MemoryExtensions;
#endif

internal static class ConfigTools {
	private static readonly Regex _envRegex = new(@"\[e:(\w+)\]", RegexOptions.Compiled | RegexOptions.NonBacktracking);
	private static readonly Regex _varRegex = new(@"\[v:(\w+)\]", RegexOptions.Compiled | RegexOptions.NonBacktracking);

	public static string BuildConfigValue(IReadOnlyDictionary<string, string> vars, string value) {
		return ReplaceEnvValues(ReplaceVarValues(vars, value.Trim()));
	}

	private static string ReplaceVarValues(IReadOnlyDictionary<string, string> vars, string value) {
		value = _varRegex.Replace(value, m => {
			var key = m.Groups[1].Value;
			return vars.TryGetValue(key, out var r) ? r : m.Value;
		});
		return value;
	}

	private static string ReplaceEnvValues(string value) {
		value = _envRegex.Replace(value, m => {
			var key = m.Groups[1].Value;
			var v = Environment.GetEnvironmentVariable(key);
			return v ?? m.Value;
		});
		return value;
	}
}

internal static class Extensions {
	public static bool TryGetFirstBytes(this Headers headers, string headerName, out ReadOnlySpan<byte> bytes) {
		var headersSpan = CollectionsMarshal.AsSpan(Unsafe.As<List<IHeader>>(headers.BackingList));
		foreach (ref readonly var header in headersSpan)
			if (Ascii.Equals(headerName, header.Key)) {
				bytes = header.GetValueBytes();
				return true;
			}

		bytes = ReadOnlySpan<byte>.Empty;
		return false;
	}
}
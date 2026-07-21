namespace Prague.Baseline.Harness;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
///   Discovers concrete <see cref="IThroughputTest" /> / <see cref="ILatencyTest" /> types in this
///   assembly and narrows them down per the command-line options (target substring, from-marker,
///   include flags). Reflection-based discovery — startup-only, not a hot path.
/// </summary>
public sealed class PerfTestTypeSelector {
	private readonly ProgramOptions _options;

	public PerfTestTypeSelector(ProgramOptions options) => _options = options;

	public List<PerfTestType> GetPerfTestTypes() {
		var all = DiscoverAll();

		if (!string.IsNullOrEmpty(_options.Target)) {
			if (_options.Target.Equals("all", StringComparison.OrdinalIgnoreCase))
				return Order(FilterByKind(all)).ToList();

			var matches = all
				.Where(t => t.Name.Contains(_options.Target, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count == 0)
				Console.WriteLine($"No test matched target [{_options.Target}].");
			return Order(matches).ToList();
		}

		return PromptSelection(Order(FilterByKind(all)).ToList());
	}

	private IEnumerable<PerfTestType> FilterByKind(IEnumerable<PerfTestType> types) =>
		types.Where(t =>
			(t.IsThroughput && _options.IncludeThroughput) ||
			(t.IsLatency && _options.IncludeLatency));

	private IEnumerable<PerfTestType> Order(IEnumerable<PerfTestType> types) {
		var ordered = types.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(_options.From))
			return ordered;
		return ordered.SkipWhile(t => !t.Name.StartsWith(_options.From, StringComparison.OrdinalIgnoreCase));
	}

	private static List<PerfTestType> DiscoverAll() =>
		Assembly.GetExecutingAssembly()
			.GetTypes()
			.Where(t => t is { IsClass: true, IsAbstract: false } &&
				(typeof(IThroughputTest).IsAssignableFrom(t) || typeof(ILatencyTest).IsAssignableFrom(t)))
			.Select(t => new PerfTestType(t))
			.ToList();

	private static List<PerfTestType> PromptSelection(List<PerfTestType> ordered) {
		if (ordered.Count == 0) {
			Console.WriteLine("No tests available.");
			return ordered;
		}

		Console.WriteLine("Select a test to run (enter a number, a name substring, or \"all\"):");
		for (var i = 0; i < ordered.Count; i++)
			Console.WriteLine($"  [{i}] {ordered[i].Name}");
		Console.Write("> ");

		var input = Console.ReadLine()?.Trim();
		if (string.IsNullOrEmpty(input))
			return [];
		if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
			return ordered;
		if (int.TryParse(input, out var idx) && idx >= 0 && idx < ordered.Count)
			return [ordered[idx]];

		return ordered.Where(t => t.Name.Contains(input, StringComparison.OrdinalIgnoreCase)).ToList();
	}
}

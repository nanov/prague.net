namespace Prague.Core.Tests.Infrastructure;

/// <summary>
///   Process-wide ledger of every array the tracking pools have handed out and not yet
///   received back. Tests snapshot it, run a scenario, quiesce, and assert the delta is
///   zero. Double-returns and foreign returns are recorded as violations instead of
///   being forwarded to the inner pool, so a buggy path cannot corrupt the shared pool
///   mid-run — the test fails on the violation counter instead.
///
///   Bookkeeping is a plain Dictionary under a lock, NOT a ConcurrentDictionary: the
///   free-list reuses entry slots, so steady-state Register/Unregister allocates nothing
///   and the suite's per-query allocation-comparison tests stay undistorted.
/// </summary>
internal static class LeakTracker {
	private static readonly Dictionary<object, string> Live = new(4096, ReferenceEqualityComparer.Instance);
	private static readonly List<string> Violations = [];

	// Off by default: rent-stack capture allocates enough to distort the suite's
	// allocation-comparison tests. LeakAssert.Balanced turns it on around its scenarios,
	// where diagnostics matter and allocation doesn't.
	internal static bool CaptureRentStacks { get; set; }

	internal static void Register(object array) {
		var stack = CaptureRentStacks ? Environment.StackTrace : "<rent stack capture disabled>";
		lock (Live)
			Live[array] = stack;
	}

	internal static bool Unregister(object array) {
		lock (Live)
			return Live.Remove(array);
	}

	internal static void ReportViolation(string message) {
		lock (Violations)
			Violations.Add(message);
	}

	internal static LeakSnapshot Snapshot() {
		lock (Live)
			lock (Violations)
				return new([..Live.Keys], Violations.Count);
	}

	internal static void AssertBalanced(in LeakSnapshot baseline) {
		string[] violations;
		lock (Violations)
			violations = [..Violations];
		var newViolations = violations.Length - baseline.ViolationCount;
		if (newViolations > 0)
			Assert.Fail($"{newViolations} pool violation(s) (double- or foreign return):\n{string.Join("\n---\n", violations[^newViolations..])}");

		var leaked = new List<string>();
		lock (Live)
			foreach (var (array, stack) in Live)
				if (!baseline.Contains(array))
					leaked.Add($"{array.GetType().Name} rented at:\n{stack}");

		if (leaked.Count > 0)
			Assert.Fail($"{leaked.Count} rented array(s) never returned:\n{string.Join("\n---\n", leaked)}");
	}
}

internal readonly struct LeakSnapshot {
	private readonly HashSet<object> _live;

	internal int ViolationCount { get; }

	internal LeakSnapshot(object[] live, int violationCount) {
		_live = new(live, ReferenceEqualityComparer.Instance);
		ViolationCount = violationCount;
	}

	internal bool Contains(object array) => _live.Contains(array);
}

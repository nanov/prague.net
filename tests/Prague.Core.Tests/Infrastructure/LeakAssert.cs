namespace Prague.Core.Tests.Infrastructure;

using Prague.Core.Collections;

internal static class LeakAssert {
	/// <summary>
	///   Runs the scenario twice: the first pass warms lazy per-closed-generic statics
	///   (<c>PooledSet&lt;T&gt;.Empty</c>, disposed-sentinel generations) whose arrays are
	///   intentionally never returned; the second pass runs against a snapshot and must
	///   come back to exactly zero new outstanding arrays and zero pool violations.
	///   Quiescing collects abandoned boxed enumerators (finalizer unpins) and drains
	///   the ReaderGate limbo so deferred returns count as returned.
	/// </summary>
	internal static void Balanced(Action scenario) {
		LeakTracker.CaptureRentStacks = true;
		try {
			scenario();
			Quiesce();
			var baseline = LeakTracker.Snapshot();
			scenario();
			Quiesce();
			LeakTracker.AssertBalanced(in baseline);
		} finally {
			LeakTracker.CaptureRentStacks = false;
		}
	}

	internal static void Quiesce() {
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		ReaderGate.TryDrain();
		ReaderGate.TryDrain();
	}
}

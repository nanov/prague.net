namespace Prague.Baseline.Harness.Support;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Shared helpers for the perf tests. Ported / trimmed from Disruptor-net.</summary>
public static class PerfTestUtil {
	/// <summary>Start an action on a dedicated long-running thread (not a thread-pool work item).</summary>
	public static Task StartLongRunning(Action action) =>
		Task.Factory.StartNew(action, TaskCreationOptions.LongRunning);

	public static void FailIf(bool condition, string message) {
		if (condition)
			throw new InvalidOperationException(message);
	}

	public static void FailIfNot(long expected, long actual, string? message = null) {
		if (expected != actual)
			throw new InvalidOperationException(message ?? $"Test failed: expected {expected}, got {actual}");
	}

	/// <summary>Busy-spin until <paramref name="condition" /> holds (used to wait on the consumer's progress).</summary>
	public static void SpinUntil(Func<bool> condition) {
		var spin = new SpinWait();
		while (!condition())
			spin.SpinOnce();
	}
}

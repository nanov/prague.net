namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Prague.Kafka.Internal;

/// <summary>
///   Dispose contract of the ring-buffer workers. The case that matters is a worker thread that has
///   not left its loop by the join deadline: it still owns the wait handles, and it must never be
///   reported as a clean drain.
/// </summary>
[TestFixture]
public class ValueBufferedWorkerDisposeTests {
	private struct Item {
		public int Value;
	}

	/// <summary>Blocks inside the handler until released, so Dispose is guaranteed to hit its deadline.</summary>
	private sealed class BlockingWorker : AsyncValueBufferedWorker<Item> {
		private readonly ManualResetEventSlim _entered = new(false);
		private readonly ManualResetEventSlim _release = new(false);

		public BlockingWorker() : base(4, "PragueTestBlockingWorker") { }

		public bool WaitUntilHandlerEntered(TimeSpan timeout) => _entered.Wait(timeout);

		public void ReleaseHandler() => _release.Set();

		protected override ValueTask ProcessAsync(ref ConsumeScope<Item> scope, CancellationToken cancellationToken) {
			scope.Release();
			_entered.Set();
			_release.Wait();
			return default;
		}
	}

	/// <summary>Drains normally; nothing blocks.</summary>
	private sealed class PassiveWorker : AsyncValueBufferedWorker<Item> {
		public PassiveWorker() : base(4, "PragueTestPassiveWorker") { }

		protected override ValueTask ProcessAsync(ref ConsumeScope<Item> scope, CancellationToken cancellationToken) {
			scope.Release();
			return default;
		}
	}

	private static void Publish(ValueBufferedWorkerBase<Item> worker, int value) {
		using var scope = worker.Publish();
		Assert.That(scope.IsOpen, Is.True, "publish scope must be open on a live worker");
		ref var slot = ref scope.Event();
		slot.Value = value;
	}

	[Test]
	public void Dispose_WhenWorkerStillRunning_FaultsCompletionInsteadOfReportingSuccess() {
		var worker = new BlockingWorker();
		worker.Start();
		Publish(worker, 1);
		Assert.That(worker.WaitUntilHandlerEntered(TimeSpan.FromSeconds(5)), Is.True,
			"handler must be running before Dispose, otherwise the abandonment path is not exercised");

		var sw = Stopwatch.StartNew();
		worker.Dispose();
		sw.Stop();

		Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
			"Dispose must stay bounded by its join deadline");
		Assert.That(worker.Completion.IsCompleted, Is.True,
			"Dispose must settle Completion before returning; a caller awaiting it would otherwise hang");
		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.False,
			"an abandoned worker must not report a clean drain");
		Assert.That(async () => await worker.Completion, Throws.TypeOf<TimeoutException>());

		// The thread owned the handles the whole time, so releasing it must not fault on a disposed one.
		worker.ReleaseHandler();
		Assert.DoesNotThrow(() => worker.Dispose(), "a second Dispose after abandonment must be a no-op");
	}

	[Test]
	public void Dispose_WhenWorkerExitsInTime_CompletesSuccessfully() {
		var worker = new PassiveWorker();
		worker.Start();
		Publish(worker, 1);

		worker.Dispose();

		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.True,
			"a worker that exited within the deadline drained cleanly");
		Assert.DoesNotThrow(() => worker.Dispose(), "Dispose must be idempotent");
	}

	[Test]
	public void Dispose_WithoutStart_IsACleanCloseNotAnAbandonment() {
		var worker = new PassiveWorker();

		Assert.DoesNotThrow(() => worker.Dispose(),
			"disposing a never-started worker must not block on a thread that was never launched");
		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.True,
			"there is no thread to abandon, so this is a clean close");
	}
}

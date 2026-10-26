namespace Prague.Kafka.Tests;

using System.Diagnostics;
using Nanov.HighPerformance.Threading.Workers;

/// <summary>
///   Dispose contract of the live-phase ring-buffer worker (<see cref="AsyncValueSpWorker{T, THandler}"/>) that
///   shutdown relies on: <c>KafkaCacheHandler.IsDrained</c> reads a faulted <c>Completion</c> as a drain
///   abandoned at the join deadline. A worker that has not left its loop by then must never report a clean drain.
/// </summary>
[TestFixture]
public class LiveWorkerDisposeContractTests {
	private struct Item {
		public int Value;
	}

	/// <summary>Blocks inside the handler until released, so Dispose is guaranteed to hit its deadline.</summary>
	private readonly struct BlockingHandler(ManualResetEventSlim entered, ManualResetEventSlim release) : IAsyncValueWorkerHandler<Item> {
		public ValueTask HandleAsync(ref HandleScope<Item> scope, CancellationToken cancellationToken) {
			scope.Release();
			entered.Set();
			release.Wait();
			return default;
		}

		public void Clear(ref Item value) { }

		public void OnError(Exception error) { }

		public ValueTask OnCompletedAsync(Exception? error) => default;
	}

	/// <summary>An after-handler that never finishes: the handler returns a ValueTask that stays pending.</summary>
	private readonly struct StuckAsyncHandler(ManualResetEventSlim entered, TaskCompletionSource never) : IAsyncValueWorkerHandler<Item> {
		public ValueTask HandleAsync(ref HandleScope<Item> scope, CancellationToken cancellationToken) {
			scope.Release();
			entered.Set();
			return new ValueTask(never.Task);
		}

		public void Clear(ref Item value) { }

		public void OnError(Exception error) { }

		public ValueTask OnCompletedAsync(Exception? error) => default;
	}

	/// <summary>Drains normally; nothing blocks.</summary>
	private readonly struct PassiveHandler : IAsyncValueWorkerHandler<Item> {
		public ValueTask HandleAsync(ref HandleScope<Item> scope, CancellationToken cancellationToken) {
			scope.Release();
			return default;
		}

		public void Clear(ref Item value) { }

		public void OnError(Exception error) { }

		public ValueTask OnCompletedAsync(Exception? error) => default;
	}

	private static void Publish<THandler>(AsyncValueSpWorker<Item, THandler> worker, int value)
		where THandler : struct, IAsyncValueWorkerHandler<Item> {
		using var scope = worker.Publish();
		Assert.That(scope.IsOpen, Is.True, "publish scope must be open on a live worker");
		ref var slot = ref scope.Event();
		slot.Value = value;
	}

	private static void AssertAbandoned<THandler>(AsyncValueSpWorker<Item, THandler> worker)
		where THandler : struct, IAsyncValueWorkerHandler<Item> {
		var sw = Stopwatch.StartNew();
		worker.Dispose();
		sw.Stop();

		Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Dispose must stay bounded by its join deadline");
		Assert.That(worker.Completion.IsCompleted, Is.True,
			"Dispose must settle Completion before returning; a caller awaiting it would otherwise hang");
		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.False, "an abandoned worker must not report a clean drain");
		Assert.That(async () => await worker.Completion, Throws.TypeOf<TimeoutException>());
	}

	[Test]
	public void Dispose_WhenHandlerBlocks_FaultsCompletionInsteadOfReportingSuccess() {
		using var entered = new ManualResetEventSlim(false);
		using var release = new ManualResetEventSlim(false);
		var worker = new AsyncValueSpWorker<Item, BlockingHandler>(new BlockingHandler(entered, release), 4);
		worker.Start();
		Publish(worker, 1);
		Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True,
			"handler must be running before Dispose, otherwise the abandonment path is not exercised");

		AssertAbandoned(worker);

		// The loop owned the handles the whole time, so releasing it must not fault on a disposed one.
		release.Set();
		Assert.DoesNotThrow(() => worker.Dispose(), "a second Dispose after abandonment must be a no-op");
	}

	[Test]
	public void Dispose_WhenAfterHandlerNeverCompletes_FaultsCompletionInsteadOfReportingSuccess() {
		using var entered = new ManualResetEventSlim(false);
		var never = new TaskCompletionSource();
		var worker = new AsyncValueSpWorker<Item, StuckAsyncHandler>(new StuckAsyncHandler(entered, never), 4);
		worker.Start();
		Publish(worker, 1);
		Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True, "handler must be awaiting before Dispose");

		AssertAbandoned(worker);

		never.SetResult();
		Assert.DoesNotThrow(() => worker.Dispose(), "a second Dispose after abandonment must be a no-op");
	}

	[Test]
	public void Dispose_WhenWorkerExitsInTime_CompletesSuccessfully() {
		var worker = new AsyncValueSpWorker<Item, PassiveHandler>(default, 4);
		worker.Start();
		Publish(worker, 1);

		worker.Dispose();

		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.True, "a worker that exited within the deadline drained cleanly");
		Assert.DoesNotThrow(() => worker.Dispose(), "Dispose must be idempotent");
	}

	[Test]
	public void Dispose_WithoutStart_IsACleanCloseNotAnAbandonment() {
		var worker = new AsyncValueSpWorker<Item, PassiveHandler>(default, 4);

		Assert.DoesNotThrow(() => worker.Dispose(), "disposing a never-started worker must not block on a loop that never ran");
		Assert.That(worker.Completion.IsCompletedSuccessfully, Is.True, "there is no loop to abandon, so this is a clean close");
	}
}

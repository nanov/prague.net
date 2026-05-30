namespace Prague.Kafka.Internal;

/// <summary>
///   Async ring-buffer worker. Same scope-based publish/consume APIs as the base — <see cref="ValueBufferedWorkerBase{T}.Publish"/>
///   returns a <see cref="PublishScope{T}"/>, <see cref="ProcessAsync"/> receives a <see cref="ConsumeScope{T}"/>
///   by ref — but the consume handler returns a <see cref="ValueTask"/> for in-flight async work
///   (after-handlers are async).
///   <para>
///     <see cref="ProcessAsync"/> must NOT be marked <c>async</c> (a ref struct can't cross an
///     <c>await</c>). Read the slot and call <see cref="ConsumeScope{T}.Release"/> synchronously, then
///     return a <see cref="ValueTask"/> from a nested <c>async</c> helper:
///   </para>
///   <code>
///   protected override ValueTask ProcessAsync(ref ConsumeScope&lt;T&gt; scope, CancellationToken ct) {
///       ref var slot = ref scope.Event();
///       var a = slot.A; var b = slot.B;
///       scope.Release();              // producer can reuse the slot now
///       return DoWorkAsync(a, b, ct); // actual async work lives here
///   }
///   </code>
///   <para>
///     Runs on a dedicated thread with synchronous kernel waits (zero alloc). Sync-completing
///     ValueTasks run entirely synchronously; for truly async ones the dedicated thread parks on
///     <c>_processDoneEvt</c> until the continuation fires. No <c>SynchronizationContext</c> on the
///     dedicated thread, so no deadlock risk.
///   </para>
/// </summary>
internal abstract class AsyncValueBufferedWorker<T> : ValueBufferedWorkerBase<T> where T : struct {
	private readonly Action _onProcessComplete;
	private readonly AutoResetEvent _processDoneEvt = new(false);

	protected AsyncValueBufferedWorker(int capacity, string? threadName = null)
		: base(capacity, threadName ?? $"AsyncValueBufferedWorker<{typeof(T).Name}>") {
		_onProcessComplete = () => _processDoneEvt.Set();
	}

	/// <summary>
	///   Invoked on the worker thread for each dispatched slot. Must NOT be declared <c>async</c>.
	///   Read the slot, call <see cref="ConsumeScope{T}.Release"/>, then return a <see cref="ValueTask"/>
	///   from a nested <c>async</c> helper.
	/// </summary>
	protected abstract ValueTask ProcessAsync(ref ConsumeScope<T> scope, CancellationToken cancellationToken);

	protected virtual ValueTask OnCompletedAsync(Exception? exception) => default;

	protected sealed override void InvokeProcess(ref ConsumeScope<T> scope) {
#pragma warning disable CA2012
		var vt = ProcessAsync(ref scope, CancellationToken);
		var awaiter = vt.GetAwaiter();
		if (!awaiter.IsCompleted) {
			awaiter.UnsafeOnCompleted(_onProcessComplete);
			_processDoneEvt.WaitOne();
		}

		awaiter.GetResult();
#pragma warning restore CA2012
	}

	protected sealed override void RunOnCompleted(Exception? exception) {
#pragma warning disable CA2012
		var vt = OnCompletedAsync(exception);
		var awaiter = vt.GetAwaiter();
		if (!awaiter.IsCompleted) {
			awaiter.UnsafeOnCompleted(_onProcessComplete);
			_processDoneEvt.WaitOne();
		}

		awaiter.GetResult();
#pragma warning restore CA2012
	}

	public override void Dispose() {
		base.Dispose();
		_processDoneEvt.Dispose();
	}
}

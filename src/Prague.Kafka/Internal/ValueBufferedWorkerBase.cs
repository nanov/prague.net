namespace Prague.Kafka.Internal;

using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>
///   Shared machinery for <see cref="AsyncValueBufferedWorker{T}"/>: a power-of-two SPSC ring buffer,
///   the sync primitives, the dedicated worker thread, the Loop, and the scope-based
///   <see cref="Publish"/>. Ported (BCL-only) from the reference Kafka lib's ValueBufferedWorker.
///   The producer (consume thread) writes a slot in place through a <see cref="PublishScope{T}"/>;
///   the worker thread drains in FIFO order and hands each slot to the user handler via a
///   <see cref="ConsumeScope{T}"/>.
/// </summary>
internal abstract class ValueBufferedWorkerBase<T> : IDisposable where T : struct {
	private const int SpinThreshold = 200;

	/// <summary>How long <see cref="Dispose"/> waits for the worker thread to leave <see cref="Loop"/>.</summary>
	private const int DisposeJoinTimeoutMs = 1000;

	internal readonly T[] _buffer;
	internal readonly int _mask;
	internal readonly AutoResetEvent _itemEvt = new(false);
	internal readonly AutoResetEvent _spaceEvt = new(false);
	internal int _head; // consumer index (worker thread)
	internal int _tail; // producer index (dispatching thread)

	private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Thread _thread;
	private volatile bool _completed; // soft stop (TryComplete) — drain remaining
	private volatile bool _stop; // hard stop (Dispose / cancel) — drop remaining
	private volatile bool _started; // Start() called — distinguishes "no thread" from "thread won't exit"
	private Exception? _completeException;
	private CancellationToken _ct;
	private CancellationTokenRegistration _ctRegistration;

	protected ValueBufferedWorkerBase(int capacity, string? threadName) {
		capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 1));
		_buffer = new T[capacity];
		_mask = capacity - 1;
		_thread = new Thread(Loop) {
			IsBackground = true,
			Name = threadName ?? $"ValueBufferedWorker<{typeof(T).Name}>"
		};
	}

	public Task Completion => _completionTcs.Task;

	/// <summary>
	///   Approximate items currently queued (Tail − Head). Plain reads — int loads are atomic on .NET
	///   and the gauge accepts approximation; reuses the ring indices for zero hot-path cost.
	/// </summary>
	public int QueuedApprox => _tail - _head;

	/// <summary>Ring capacity (rounded up to a power of two).</summary>
	public int Capacity => _buffer.Length;

	protected CancellationToken CancellationToken => _ct;

	public void Start(CancellationToken ct = default) {
		_ct = ct;
		if (ct.CanBeCanceled)
			_ctRegistration = ct.Register(static s => ((ValueBufferedWorkerBase<T>)s!).OnCancel(), this);
		_started = true;
		_thread.Start();
	}

	private void OnCancel() {
		_stop = true;
		_itemEvt.Set();
		_spaceEvt.Set();
	}

	/// <summary>
	///   Claim the next slot for writing. Blocks (spin then kernel wait) if the ring is full. The
	///   returned <see cref="PublishScope{T}"/> exposes a <see langword="ref"/> to the slot via
	///   <see cref="PublishScope{T}.Event"/>; disposing it publishes the slot and signals the consumer.
	///   If the worker is completed or disposed, the returned scope is "closed"
	///   (<see cref="PublishScope{T}.IsOpen"/> is false) and Dispose is a no-op.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public PublishScope<T> Publish() {
		if (_completed || _stop)
			return default;
		var spins = 0;
		while (_tail - Volatile.Read(ref _head) == _buffer.Length) {
			if (_stop || _completed)
				return default;
			if (spins++ < SpinThreshold) {
				Thread.SpinWait(10);
				continue;
			}

			_spaceEvt.WaitOne();
			spins = 0;
		}

		return new PublishScope<T>(this, _tail);
	}

	public bool TryComplete(Exception? exception = null) {
		if (_completed)
			return false;
		_completeException = exception;
		_completed = true;
		_itemEvt.Set();
		return true;
	}

	/// <summary>
	///   Invoke the user handler for a single slot. The async subclass blocks the worker thread on the
	///   resulting <see cref="ValueTask"/>.
	/// </summary>
	protected abstract void InvokeProcess(ref ConsumeScope<T> scope);

	/// <summary>Called once after the worker has drained and is about to exit.</summary>
	protected virtual void RunOnCompleted(Exception? exception) { }

	private void Loop() {
		try {
			while (!_stop) {
				var spins = 0;
				while (_head == Volatile.Read(ref _tail)) {
					if (_stop || _completed)
						return;
					if (spins++ < SpinThreshold) {
						Thread.SpinWait(10);
						continue;
					}

					_itemEvt.WaitOne();
					spins = 0;
				}

				var scope = new ConsumeScope<T>(this, _head);
				try {
					InvokeProcess(ref scope);
				}
				catch {
					// TODO: log — handler exceptions must not kill the worker loop
				}
				finally {
					// Safety net: ensure _head advances even if the handler didn't release. Idempotent.
					scope.Release();
				}
			}
		}
		finally {
			var ex = _completeException
				?? (_ct.IsCancellationRequested ? new OperationCanceledException(_ct) : null);
			try {
				RunOnCompleted(ex);
			}
			catch {
				// TODO: log
			}

			if (ex is OperationCanceledException)
				_completionTcs.TrySetCanceled(_ct);
			else if (ex is not null)
				_completionTcs.TrySetException(ex);
			else
				_completionTcs.TrySetResult();
			_ctRegistration.Dispose();
		}
	}

	/// <summary>
	///   True once the worker thread has been observed leaving <see cref="Loop"/>. While it is false
	///   after a <see cref="Dispose"/> attempt, the thread is still running and still owns the wait
	///   handles, so no one may dispose them.
	/// </summary>
	protected bool WorkerThreadExited { get; private set; }

	public virtual void Dispose() {
		if (_stop)
			return;
		_stop = true;
		if (!_started) {
			// Never launched: no thread to abandon and nothing queued can ever be drained by one, so
			// this is a clean close rather than an abandonment.
			WorkerThreadExited = true;
			_completionTcs.TrySetResult();
			_itemEvt.Dispose();
			_spaceEvt.Dispose();
			GC.SuppressFinalize(this);
			return;
		}

		_itemEvt.Set();
		_spaceEvt.Set();
		var exited = false;
		try {
			exited = _thread.Join(DisposeJoinTimeoutMs);
		}
		catch {
			// ignore
		}

		WorkerThreadExited = exited;
		if (!exited) {
			// The thread is still inside Loop: parked on a handle, or blocked in the user handler.
			// Disposing the handles here throws ObjectDisposedException *on that thread*, from outside
			// the per-item try, so it unwinds straight to Loop's finally and completes the TCS as
			// success -- an abandoned worker reporting a clean drain, which is what let
			// WaitForCompletionAsync claim a drain that never happened. Leave the handles to their
			// finalizers (SafeHandle owns the kernel object) and fault the completion instead.
			_completionTcs.TrySetException(new TimeoutException(
				$"[Prague] Worker '{_thread.Name}' did not exit within {DisposeJoinTimeoutMs} ms. "
				+ $"Approximately {QueuedApprox} queued item(s) were not drained."));
			return;
		}

		_itemEvt.Dispose();
		_spaceEvt.Dispose();
		GC.SuppressFinalize(this);
	}
}

/// <summary>
///   In-flight publish handle. Pattern-based <c>using</c> picks up <see cref="Dispose"/> by name.
///   Don't stash, return, or escape it — the scope must fall out of its <c>using</c> in the same
///   stack frame it was claimed in, or the slot never publishes. <c>default</c> is the "closed" scope
///   returned by <see cref="ValueBufferedWorkerBase{T}.Publish"/> when the worker stopped accepting.
/// </summary>
internal readonly ref struct PublishScope<T> where T : struct {
	private readonly ValueBufferedWorkerBase<T>? _worker;
	private readonly int _tail;

	internal PublishScope(ValueBufferedWorkerBase<T> worker, int tail) {
		_worker = worker;
		_tail = tail;
	}

	/// <summary>False for the "closed" scope returned when the worker rejected the claim.</summary>
	public bool IsOpen => _worker is not null;

	/// <summary>Writable ref to the claimed slot. Throws if the scope is closed.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T Event() {
		if (_worker is null)
			throw new InvalidOperationException("Publish scope is closed; the worker rejected the claim.");
		return ref _worker._buffer[_tail & _worker._mask];
	}

	/// <summary>Publish the slot and signal the consumer. No-op on a closed scope.</summary>
	public void Dispose() {
		if (_worker is null)
			return;
		Volatile.Write(ref _worker._tail, _tail + 1);
		_worker._itemEvt.Set();
	}
}

/// <summary>
///   In-flight consume handle passed to the user handler. Gives ref access to the slot via
///   <see cref="Event"/> and lets the handler release the slot early via <see cref="Release"/> so the
///   producer can reuse it while the handler is still running. If the handler doesn't release, the
///   worker loop's <c>finally</c> does. <see cref="Release"/> is idempotent.
/// </summary>
internal ref struct ConsumeScope<T> where T : struct {
	private readonly ValueBufferedWorkerBase<T> _worker;
	private readonly int _head;

	internal ConsumeScope(ValueBufferedWorkerBase<T> worker, int head) {
		_worker = worker;
		_head = head;
		IsReleased = false;
	}

	/// <summary>True once the slot has been released (by the handler or the loop).</summary>
	public bool IsReleased { get; private set; }

	/// <summary>
	///   Ref to the slot. Call before <see cref="Release"/>; after Release the slot may be overwritten
	///   by the producer at any time.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly ref T Event() {
		if (IsReleased)
			throw new InvalidOperationException("ConsumeScope already released; the slot may have been reused.");
		return ref _worker._buffer[_head & _worker._mask];
	}

	/// <summary>
	///   Release the slot — clear it, advance <c>_head</c>, and wake a producer waiting for space.
	///   After this call the slot contents are not safe to read. Idempotent.
	/// </summary>
	public void Release() {
		if (IsReleased)
			return;
		IsReleased = true;
		ref var slot = ref _worker._buffer[_head & _worker._mask];
		slot = default;
		Volatile.Write(ref _worker._head, _head + 1);
		_worker._spaceEvt.Set();
	}
}

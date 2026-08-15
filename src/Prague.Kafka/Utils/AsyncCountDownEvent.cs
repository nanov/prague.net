namespace Prague.Kafka.Utils;

internal sealed class AsyncCountdownEvent {
	private readonly KafkaCachesConsumerStatistics _statistics;
	// RunContinuationsAsynchronously is load-bearing. Signal() runs on the dedicated consume-loop thread
	// (FlushRawLoadBufferAndGoLive). Without it, every awaiter of WaitAsync resumes INLINE on that thread,
	// so the caller's post-startup code executes on the poll loop: the consumer stops polling, cannot
	// rejoin a group rebalance, and cannot observe cancellation until the caller's continuation returns.
	private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _count;

	public AsyncCountdownEvent(int initialCount, KafkaCachesConsumerStatistics statistics) {
		_statistics = statistics;
		_count = initialCount;
		if (_count == 0)
			_tcs.TrySetResult(true);
	}

	public void TrySetCanceled()
		=> _tcs.TrySetCanceled();

	public void TrySetException(Exception exception)
		=> _tcs.TrySetException(exception);

	public void Signal(TimeSpan loadTime) {
		if (Interlocked.Decrement(ref _count) > 0)
			return;
		_statistics.InitialLoadTime = loadTime;
		_tcs.TrySetResult(true);
	}

	/// <summary>
	///   Wait for every cache to signal its initial load. Honouring <paramref name="ct" /> is what stops a
	///   load that never completes — partition never assigned, EOF never observed — from blocking the
	///   caller forever with no diagnostic.
	/// </summary>
	public Task WaitAsync(CancellationToken ct = default)
		=> ct.CanBeCanceled ? _tcs.Task.WaitAsync(ct) : _tcs.Task;
}
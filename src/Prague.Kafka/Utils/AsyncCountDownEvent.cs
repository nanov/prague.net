namespace Prague.Kafka.Utils;

internal sealed class AsyncCountdownEvent {
	private readonly KafkaCachesConsumerStatistics _statistics;
	private readonly TaskCompletionSource<bool> _tcs = new();
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

	public Task WaitAsync()
		=> _tcs.Task;
}
namespace Prague.Kafka;

using System.Diagnostics;
using Core;
using Core.Utils;
using IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Options;

internal class KafkaCachesLoader: IDisposable {
	private readonly IReadOnlyList<KafkaCacheConsumer> _consumers;
	private readonly IDataCacheRegistry _registry;
	private readonly ILogger<KafkaCachesLoader> _logger;
	private readonly Lock _startLoadingLock = new();
	private CancellationTokenSource? _stoppingCts;
	private Task? _loadingTask;
	private volatile bool _disposed;

	public KafkaCachesLoader(IServiceProvider serviceProvider, IOptions<KafkaCachesGlobalOptions> globalOptions, IDataCacheRegistry registry, ILogger<KafkaCachesLoader> logger) {
		_consumers = globalOptions.Value.ClusterNames
			.Select(name => serviceProvider.GetRequiredKeyedService<KafkaCacheConsumer>(name))
			.ToList();
		_registry = registry;
		_logger = logger;
	}


	public Task StartAsync(CancellationToken cancellationToken) {
		if (_loadingTask is not null)
			return _loadingTask;
		lock (_startLoadingLock) {
			if (_loadingTask is not null)
				return _loadingTask;
			_loadingTask = StartLoadingAsync(cancellationToken);
			return _loadingTask;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken) {
		if (!_disposed)
			_stoppingCts?.Cancel();
		await Task.WhenAll(_consumers.Select(x => x.WaitForCompletionAsync()));
	}

	private async Task StartLoadingAsync(CancellationToken cancellationToken) {
		try {
			_stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var now = Stopwatch.GetTimestamp();
			foreach (var consumer in _consumers)
				await consumer.ExecuteAsync(_stoppingCts.Token);
			try {
				await Task.WhenAll(_consumers.Select(x => x.WaitForInitialLoadAsync(_stoppingCts.Token)));
			}
			catch (OperationCanceledException) {
				// Report unconditionally: the caller's own token firing IS the timeout case, and a bare
				// TaskCanceledException here is what made stalled loads undiagnosable.
				throw new TimeoutException(
					$"[Prague] Initial cache load did not complete after "
					+ $"{Stopwatch.GetElapsedTime(now).TotalSeconds:F1}s. Caches still awaiting partition EOF: ["
					+ string.Join(", ", _consumers.SelectMany(c => c.PendingCaches)) + "]");
			}
			// TODO: source generated logs
			_logger.LogInformation("[Prague] Kafka caches loaded in {ElapsedMilliseconds} ms", Stopwatch.GetElapsedTime(now).TotalMilliseconds);
			DataCacheRegistryMarshall.SetLoaded(_registry, null);
		} catch (Exception e) {
			DataCacheRegistryMarshall.SetLoaded(_registry, e);
			throw;
		}
	}

	/// <summary>
	///   Cancels and releases the load/stop source, then the consumers' own sources. Cancelling before
	///   releasing keeps a host that was disposed without a StopAsync from leaving raw loops running.
	/// </summary>
	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;
		_stoppingCts?.Cancel();
		_stoppingCts?.Dispose();
		foreach (var consumer in _consumers)
			consumer.Dispose();
	}
}


internal class KafkaCachesBackgroundWorker : IHostedService, IDisposable {
	private readonly KafkaCachesLoader _loader;

	public KafkaCachesBackgroundWorker(KafkaCachesLoader loader) {
		_loader = loader;
	}

	public Task StartAsync(CancellationToken cancellationToken) {
		return _loader.StartAsync(cancellationToken);
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		return _loader.StopAsync(cancellationToken);
	}

	public void Dispose()
		=> _loader.Dispose();
}

public abstract class KafkaCachesBackgroundService: IHostedService, IDisposable {
	private readonly IDataCacheRegistry _cacheRegistry;
	private Task? _executeTask;
        private CancellationTokenSource? _stoppingCts;
        private volatile bool _disposed;
        public virtual Task? ExecuteTask => _executeTask;

        public KafkaCachesBackgroundService(IDataCacheRegistry cacheRegistry) {
	        _cacheRegistry = cacheRegistry;
        }

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        public virtual async Task StartAsync(CancellationToken cancellationToken) {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await _cacheRegistry.LoadingCompletion;
            _executeTask = ExecuteAsync(_stoppingCts.Token);
            if (_executeTask.IsFaulted)
	            throw _executeTask.Exception;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken) {
            if (_executeTask == null)
                return;

            try {
	            if (!_disposed)
		            _stoppingCts!.Cancel();
            }
            finally {
                await _executeTask.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

        }

        public virtual void Dispose() {
	        if (_disposed)
		        return;
	        _disposed = true;
	        _stoppingCts?.Cancel();
	        _stoppingCts?.Dispose();
        }

}

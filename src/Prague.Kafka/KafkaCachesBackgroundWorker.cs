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
		_stoppingCts?.Cancel();
		await Task.WhenAll(_consumers.Select(x => x.WaitForCompletionAsync()));
	}

	private async Task StartLoadingAsync(CancellationToken cancellationToken) {
		try {
			_stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var now = Stopwatch.GetTimestamp();
			foreach (var consumer in _consumers)
				await consumer.ExecuteAsync(_stoppingCts.Token);
			await Task.WhenAll(_consumers.Select(x => x.WaitForInitialLoadAsync()));
			// TODO: source generated logs
			_logger.LogInformation("[Prague] Kafka caches loaded in {ElapsedMilliseconds} ms", Stopwatch.GetElapsedTime(now).TotalMilliseconds);
			DataCacheRegistryMarshall.SetLoaded(_registry, null);
		} catch (Exception e) {
			DataCacheRegistryMarshall.SetLoaded(_registry, e);
			throw;
		}
	}

	public void Dispose()
		=> _stoppingCts?.Cancel();
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
                _stoppingCts!.Cancel(); }
            finally {
                await _executeTask.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

        }

        public virtual void Dispose()
					=> _stoppingCts?.Cancel();

}

namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
///   #48: the host's shutdown token is the only bound on an after-handler that will not return, and
///   abandoning a drain must be reported rather than passed off as a clean stop.
/// </summary>
[TestFixture]
public class ShutdownDrainTests {
	private const string TopicPrefix = "it-shutdown-drain";

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();
	private readonly List<BlockingAfterHandler> _handlers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[TearDown]
	public async Task TearDownProviders() {
		// Release first: a handler left blocked keeps a worker thread alive, and the provider teardown
		// below is what closes the consumer and leaves the group.
		foreach (var handler in _handlers)
			handler.Release();
		_handlers.Clear();

		foreach (var provider in _providers)
			try {
				await provider.GetRequiredService<IHostedService>().StopAsync(CancellationToken.None);
			}
			catch {
				// teardown must not mask the test's own failure
			}
			finally {
				(provider as IDisposable)?.Dispose();
			}

		_providers.Clear();
	}

	[Test]
	public async Task StopAsync_WhenAnAfterHandlerBlocks_HonoursTheTokenAndNamesTheCache() {
		var blocking = new BlockingAfterHandler();
		_handlers.Add(blocking);
		var logs = new CapturingLoggerProvider();

		var sp = BuildServices(blocking, logs).BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(startCts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(startCts.Token);

		// Live phase: this message reaches the after-handler, which then never returns.
		Produce(1);
		Assert.That(blocking.WaitUntilEntered(TimeSpan.FromSeconds(20)), Is.True,
			"the after-handler must be blocking before the stop, otherwise the drain would succeed");

		using var stopCts = new CancellationTokenSource();
		await stopCts.CancelAsync();
		var sw = Stopwatch.StartNew();
		Assert.DoesNotThrowAsync(async () => await hosted.StopAsync(stopCts.Token),
			"the host giving up on the wait is not a Prague fault");
		sw.Stop();

		Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
			"an already-cancelled shutdown token must cut the drain short");
		Assert.That(logs.Messages.Any(m => m.Contains("still draining") && m.Contains(nameof(FilterEntityCache))),
			Is.True,
			$"the abandoned drain must name the cache; got: {string.Join(" | ", logs.Messages)}");

		blocking.Release();
	}

	/// <summary>
	///   The worker's completion is <em>Canceled</em> on a graceful stop, because its token is the
	///   shutdown token. That must read as drained, or every normal shutdown would cry abandonment.
	/// </summary>
	[Test]
	public async Task StopAsync_WhenDrainCompletes_ReportsNothing() {
		var logs = new CapturingLoggerProvider();
		var sp = BuildServices(afterHandler: null, logs).BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(startCts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(startCts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Produce(1);
		for (var i = 0; i < 200 && !cache.Cache.TryGet(1, out _); i++)
			await Task.Delay(50);
		Assert.That(cache.Cache.TryGet(1, out _), Is.True,
			"the live worker must have handled a record, so that its completion is Canceled rather than absent");

		await hosted.StopAsync(CancellationToken.None);

		Assert.That(logs.Messages.Any(m => m.Contains("still draining")), Is.False,
			$"a clean stop must not report an abandoned drain; got: {string.Join(" | ", logs.Messages)}");
	}

	/// <summary>
	///   Without a deadline of its own, this is the case that used to hang: the worker's Dispose skipped
	///   its join because cancellation had already set the stop flag, so the drain waited on an
	///   after-handler that never returns.
	/// </summary>
	[Test]
	public async Task StopAsync_WithoutAToken_IsStillBoundedByTheWorkerJoinDeadline() {
		var blocking = new BlockingAfterHandler();
		_handlers.Add(blocking);
		var logs = new CapturingLoggerProvider();

		var sp = BuildServices(blocking, logs).BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(startCts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(startCts.Token);

		Produce(1);
		Assert.That(blocking.WaitUntilEntered(TimeSpan.FromSeconds(20)), Is.True,
			"the after-handler must be blocking before the stop");

		var sw = Stopwatch.StartNew();
		await hosted.StopAsync(CancellationToken.None);
		sw.Stop();

		Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)),
			"the worker's join deadline must bound the drain even with no shutdown token");
		Assert.That(logs.Messages.Any(m => m.Contains("still draining") && m.Contains(nameof(FilterEntityCache))),
			Is.True,
			$"an abandoned worker must be reported, not silently suppressed; got: {string.Join(" | ", logs.Messages)}");

		blocking.Release();
	}

	private ServiceCollection BuildServices(ICacheAfterHandler<int, FilterEntity>? afterHandler,
		CapturingLoggerProvider logs) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
				{ "KafkaConfig:ClientSettings:group.id", Guid.NewGuid().ToString() }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
		if (afterHandler is not null)
			services.AddSingleton(afterHandler);
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
		});
		return services;
	}

	private void Produce(int id) {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(new FilterEntity { Id = id, Name = $"e-{id}", Value = id }),
			Headers = new Headers()
		});
		producer.Flush(TimeSpan.FromSeconds(10));
	}

	private sealed class BlockingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ManualResetEventSlim _entered = new(false);
		private readonly ManualResetEventSlim _release = new(false);

		public bool WaitUntilEntered(TimeSpan timeout) => _entered.Wait(timeout);

		public void Release() => _release.Set();

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_entered.Set();
			_release.Wait(TimeSpan.FromSeconds(60));
			return ValueTask.CompletedTask;
		}
	}

	private sealed class CapturingLoggerProvider : ILoggerProvider {
		private readonly ConcurrentQueue<string> _messages = new();

		public IReadOnlyCollection<string> Messages => _messages;

		public ILogger CreateLogger(string categoryName) => new Capturing(_messages);

		public void Dispose() { }

		private sealed class Capturing(ConcurrentQueue<string> sink) : ILogger {
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter) => sink.Enqueue(formatter(state, exception));
		}
	}
}

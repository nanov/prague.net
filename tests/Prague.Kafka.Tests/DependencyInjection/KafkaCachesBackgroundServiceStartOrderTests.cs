namespace Prague.Kafka.Tests.DependencyInjection;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prague.Core;
using Prague.Core.Utils;

/// <summary>
///   A <see cref="KafkaCachesBackgroundService" /> must not hold up host start while it waits for the load. The
///   host starts hosted services one after another in registration order, so a service registered before
///   <c>AddKafkaCaches</c> that waits for the load inside <c>StartAsync</c> blocks the Kafka worker that would
///   complete it: the host never starts, and no token can stop the wait.
/// </summary>
// The class is obsolete; these tests pin its behaviour until it is removed.
#pragma warning disable CS0618
[TestFixture]
public class KafkaCachesBackgroundServiceStartOrderTests {
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

	private sealed class RecordingService : KafkaCachesBackgroundService {
		public readonly CapturingLogger Log;
		public readonly FakeLifetime Lifetime;
		public readonly TaskCompletionSource<bool> SawLoadedRegistry = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly IDataCacheRegistry _registry;
		private readonly Func<CancellationToken, Task>? _body;

		public RecordingService(IDataCacheRegistry registry, Func<CancellationToken, Task>? body = null)
			: this(registry, body, new CapturingLogger(), new FakeLifetime()) { }

		private RecordingService(IDataCacheRegistry registry, Func<CancellationToken, Task>? body, CapturingLogger log,
			FakeLifetime lifetime) : base(registry, log, lifetime) {
			_registry = registry;
			_body = body;
			Log = log;
			Lifetime = lifetime;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken) {
			SawLoadedRegistry.TrySetResult(_registry.LoadingCompletion.IsCompletedSuccessfully);
			return _body?.Invoke(stoppingToken) ?? Task.Delay(Timeout.Infinite, stoppingToken);
		}
	}

	private sealed class FakeLifetime : IHostApplicationLifetime {
		public int StopCalls;
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public void StopApplication() => Interlocked.Increment(ref StopCalls);
	}

	private sealed class CapturingLogger : ILogger {
		public readonly List<(LogLevel Level, Exception? Exception)> Entries = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter) {
			lock (Entries)
				Entries.Add((logLevel, exception));
		}
	}

	/// <summary>Stands in for the Kafka worker: its StartAsync completes the load.</summary>
	private sealed class LoaderStandIn(IDataCacheRegistry registry) : IHostedService {
		public Task StartAsync(CancellationToken cancellationToken) {
			DataCacheRegistryMarshall.SetLoaded(registry, null);
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	// What Host.StartAsync does with the default HostOptions.ServicesStartConcurrently = false.
	private static async Task StartSequentially(IEnumerable<IHostedService> services, CancellationToken cancellationToken) {
		foreach (var service in services)
			await service.StartAsync(cancellationToken);
	}

	[Test]
	public async Task RegisteredBeforeTheLoader_HostStarts_AndExecuteRunsAfterTheLoad() {
		var registry = new DataCacheRegistryBuilder().Build();
		using var service = new RecordingService(registry);
		using var cts = new CancellationTokenSource(Deadline);

		var start = StartSequentially([service, new LoaderStandIn(registry)], cts.Token);

		Assert.That(await Task.WhenAny(start, Task.Delay(Deadline)), Is.SameAs(start), "host start deadlocked");
		Assert.That(await service.SawLoadedRegistry.Task.WaitAsync(Deadline), Is.True);
		await service.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task RegisteredAfterTheLoader_ExecuteRunsAfterTheLoad() {
		var registry = new DataCacheRegistryBuilder().Build();
		using var service = new RecordingService(registry);

		await StartSequentially([new LoaderStandIn(registry), service], CancellationToken.None).WaitAsync(Deadline);

		Assert.That(await service.SawLoadedRegistry.Task.WaitAsync(Deadline), Is.True);
		await service.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task StoppedBeforeTheLoad_StopReturns_AndExecuteNeverRuns() {
		var registry = new DataCacheRegistryBuilder().Build();
		using var service = new RecordingService(registry);

		var start = service.StartAsync(CancellationToken.None);
		Assert.That(await Task.WhenAny(start, Task.Delay(Deadline)), Is.SameAs(start), "StartAsync waited for the load");

		await service.StopAsync(CancellationToken.None).WaitAsync(Deadline);
		Assert.That(service.SawLoadedRegistry.Task.IsCompleted, Is.False);
	}

	[Test]
	public async Task ExecuteThrows_LogsTheError_AndStopsTheHost() {
		var registry = new DataCacheRegistryBuilder().Build();
		var boom = new InvalidOperationException("boom");
		using var service = new RecordingService(registry, async _ => {
			await Task.Yield();
			throw boom;
		});

		await service.StartAsync(CancellationToken.None);
		DataCacheRegistryMarshall.SetLoaded(registry, null);

		Assert.That(async () => await service.ExecuteTask!.WaitAsync(Deadline), Throws.Exception.SameAs(boom));
		Assert.That(service.Log.Entries, Has.One.Matches<(LogLevel Level, Exception? Exception)>(
			e => e.Level == LogLevel.Error && ReferenceEquals(e.Exception, boom)));
		Assert.That(service.Lifetime.StopCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task StoppedWhileExecuting_DoesNotLogOrStopTheHost() {
		var registry = new DataCacheRegistryBuilder().Build();
		using var service = new RecordingService(registry);

		await service.StartAsync(CancellationToken.None);
		DataCacheRegistryMarshall.SetLoaded(registry, null);
		await service.SawLoadedRegistry.Task.WaitAsync(Deadline);
		await service.StopAsync(CancellationToken.None).WaitAsync(Deadline);

		Assert.That(service.Log.Entries, Is.Empty);
		Assert.That(service.Lifetime.StopCalls, Is.Zero);
	}

	[Test]
	public async Task LoadFails_ExecuteNeverRuns_AndTheServiceLeavesTheReportToTheWorker() {
		var registry = new DataCacheRegistryBuilder().Build();
		using var service = new RecordingService(registry);

		await service.StartAsync(CancellationToken.None);
		DataCacheRegistryMarshall.SetLoaded(registry, new TimeoutException("load"));
		await service.ExecuteTask!.WaitAsync(Deadline);

		Assert.That(service.SawLoadedRegistry.Task.IsCompleted, Is.False);
		Assert.That(service.Log.Entries, Is.Empty);
		Assert.That(service.Lifetime.StopCalls, Is.Zero);
	}
}

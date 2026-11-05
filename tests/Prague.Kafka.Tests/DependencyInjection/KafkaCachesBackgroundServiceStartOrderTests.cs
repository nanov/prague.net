namespace Prague.Kafka.Tests.DependencyInjection;

using Microsoft.Extensions.Hosting;
using Prague.Core;
using Prague.Core.Utils;

/// <summary>
///   A <see cref="KafkaCachesBackgroundService" /> must not hold up host start while it waits for the load. The
///   host starts hosted services one after another in registration order, so a service registered before
///   <c>AddKafkaCaches</c> that waits for the load inside <c>StartAsync</c> blocks the Kafka worker that would
///   complete it: the host never starts, and no token can stop the wait.
/// </summary>
[TestFixture]
public class KafkaCachesBackgroundServiceStartOrderTests {
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

	private sealed class RecordingService(IDataCacheRegistry registry) : KafkaCachesBackgroundService(registry) {
		public readonly TaskCompletionSource<bool> SawLoadedRegistry = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly IDataCacheRegistry _registry = registry;

		protected override Task ExecuteAsync(CancellationToken stoppingToken) {
			SawLoadedRegistry.TrySetResult(_registry.LoadingCompletion.IsCompletedSuccessfully);
			return Task.Delay(Timeout.Infinite, stoppingToken);
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
}

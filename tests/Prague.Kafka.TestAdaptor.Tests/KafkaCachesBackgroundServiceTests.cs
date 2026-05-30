namespace Prague.Kafka.TestAdaptor.Tests;

using Prague.Core;
using Prague.Core.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class KafkaCachesBackgroundServiceTests {
	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private const string TestTopic = "background-service-test-topic";
	private ServiceCollection _services = null!;
	private IKafkaCacheTestBuilderProvider _provider = null!;

	private IServiceProvider BuildServiceProvider(Action<KafkaCacheHandlersBuilder>? configure = null) {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "localhost:9092" }
			})
			.Build();

		_services.AddSingleton<IConfiguration>(configuration);
		_services.AddLogging();
		_services.AddKafkaCaches("KafkaConfig", configure ?? (builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic);
		}));

		return _services.BuildServiceProvider();
	}

	[Test]
	public async Task KafkaCachesBackgroundService_WaitsForLoadingCompletion_BeforeExecuting() {
		// Arrange
		var executionOrder = new List<string>();
		var sp = BuildServiceProvider();

		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new TestBackgroundService(registry, executionOrder);

		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();

		// Start the test background service (should wait for loading)
		var testServiceTask = testService.StartAsync(cts.Token);

		// Give a moment to ensure the service is waiting
		await Task.Delay(50);

		// Start the kafka caches loader
		await hostedService.StartAsync(cts.Token);

		// Wait for initial load
		await Task.Delay(200);

		// Now wait for test service to complete startup
		await testServiceTask;

		// Assert - ExecuteAsync should have been called after loading completed
		Assert.That(executionOrder, Contains.Item("ExecuteAsync"));

		await testService.StopAsync(cts.Token);
		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task KafkaCachesBackgroundService_ExecuteTask_IsAvailableAfterStart() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new TestBackgroundService(registry, new List<string>());
		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);
		await testService.StartAsync(cts.Token);

		// Assert
		Assert.That(testService.ExecuteTask, Is.Not.Null);

		await testService.StopAsync(cts.Token);
		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task KafkaCachesBackgroundService_StopAsync_CancelsExecution() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new LongRunningBackgroundService(registry);
		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);
		await testService.StartAsync(cts.Token);

		// Give time for ExecuteAsync to start
		await Task.Delay(50);

		// Stop should cancel the execution
		await testService.StopAsync(CancellationToken.None);

		// Assert
		Assert.That(testService.WasCancelled, Is.True);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task KafkaCachesBackgroundService_Dispose_CancelsExecution() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new LongRunningBackgroundService(registry);
		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);
		await testService.StartAsync(cts.Token);

		// Give time for ExecuteAsync to start
		await Task.Delay(50);

		// Dispose should trigger cancellation
		testService.Dispose();

		// Wait for cancellation to propagate
		await Task.Delay(100);

		// Assert
		Assert.That(testService.WasCancelled, Is.True);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public void KafkaCachesBackgroundService_WhenLoadingFails_ThrowsOnStart() {
		// Arrange
		var registry = new DataCacheRegistryBuilder().Build();
		var testService = new TestBackgroundService(registry, new List<string>());

		// Act & Assert
		using var cts = new CancellationTokenSource();

		// Complete loading with an exception using the marshall
		DataCacheRegistryMarshall.SetLoaded(registry, new InvalidOperationException("Loading failed"));

		var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await testService.StartAsync(cts.Token);
		});

		Assert.That(ex!.Message, Is.EqualTo("Loading failed"));
	}

	[Test]
	public async Task KafkaCachesBackgroundService_WhenExecuteAsyncThrows_StartAsyncThrows() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new ThrowingBackgroundService(registry);
		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Assert - should throw because ExecuteAsync throws synchronously
		var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await testService.StartAsync(cts.Token);
		});

		Assert.That(ex!.Message, Is.EqualTo("ExecuteAsync failed"));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task KafkaCachesBackgroundService_StopAsync_WhenNotStarted_ReturnsImmediately() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new TestBackgroundService(registry, new List<string>());

		// Act & Assert - should not throw
		using var cts = new CancellationTokenSource();
		await testService.StopAsync(cts.Token);

		Assert.That(testService.ExecuteTask, Is.Null);
	}

	[Test]
	public async Task KafkaCachesBackgroundService_CanAccessCaches_InExecuteAsync() {
		// Arrange
		var sp = BuildServiceProvider();
		var registry = sp.GetRequiredService<IDataCacheRegistry>();
		var testService = new CacheAccessingBackgroundService(registry);
		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);
		await testService.StartAsync(cts.Token);

		// Wait for ExecuteAsync to access the cache
		await Task.Delay(100);

		// Assert
		Assert.That(testService.CacheWasAccessible, Is.True);

		await testService.StopAsync(cts.Token);
		await hostedService.StopAsync(cts.Token);
	}

	private class TestBackgroundService : KafkaCachesBackgroundService {
		private readonly List<string> _executionOrder;

		public TestBackgroundService(IDataCacheRegistry cacheRegistry, List<string> executionOrder)
			: base(cacheRegistry) {
			_executionOrder = executionOrder;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken) {
			_executionOrder.Add("ExecuteAsync");
			return Task.CompletedTask;
		}
	}

	private class LongRunningBackgroundService : KafkaCachesBackgroundService {
		public bool WasCancelled { get; private set; }

		public LongRunningBackgroundService(IDataCacheRegistry cacheRegistry)
			: base(cacheRegistry) {
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
			try {
				await Task.Delay(Timeout.Infinite, stoppingToken);
			}
			catch (OperationCanceledException) {
				WasCancelled = true;
			}
		}
	}

	private class ThrowingBackgroundService : KafkaCachesBackgroundService {
		public ThrowingBackgroundService(IDataCacheRegistry cacheRegistry)
			: base(cacheRegistry) {
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken) {
			throw new InvalidOperationException("ExecuteAsync failed");
		}
	}

	private class CacheAccessingBackgroundService : KafkaCachesBackgroundService {
		private readonly IDataCacheRegistry _registry;
		public bool CacheWasAccessible { get; private set; }

		public CacheAccessingBackgroundService(IDataCacheRegistry cacheRegistry)
			: base(cacheRegistry) {
			_registry = cacheRegistry;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken) {
			try {
				var cache = _registry.GetCache<TestEntityCache>();
				CacheWasAccessible = cache != null;
			}
			catch {
				CacheWasAccessible = false;
			}
			return Task.CompletedTask;
		}
	}

}

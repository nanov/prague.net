namespace Prague.Kafka.TestAdaptor.Tests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class AfterHandlerTests {
	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private const string TestTopic = "after-handler-test-topic";
	private ServiceCollection _services = null!;
	private IKafkaCacheTestBuilderProvider _provider = null!;

	private IServiceProvider BuildServiceProvider(Action<KafkaCacheHandlersBuilder> configure) {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "localhost:9092" }
			})
			.Build();

		_services.AddSingleton<IConfiguration>(configuration);
		_services.AddLogging();
		_services.AddKafkaCaches("KafkaConfig", configure);

		return _services.BuildServiceProvider();
	}

	[Test]
	public void UpdateType_HasCorrectValues() {
		// Assert all expected values exist
		Assert.That(Enum.IsDefined(typeof(UpdateType), UpdateType.Filtered), Is.True);
		Assert.That(Enum.IsDefined(typeof(UpdateType), UpdateType.Same), Is.True);
		Assert.That(Enum.IsDefined(typeof(UpdateType), UpdateType.Add), Is.True);
		Assert.That(Enum.IsDefined(typeof(UpdateType), UpdateType.Update), Is.True);
		Assert.That(Enum.IsDefined(typeof(UpdateType), UpdateType.Delete), Is.True);

		// Assert values are as expected
		Assert.That((int)UpdateType.Filtered, Is.EqualTo(0));
		Assert.That((int)UpdateType.Same, Is.EqualTo(1));
		Assert.That((int)UpdateType.Add, Is.EqualTo(2));
		Assert.That((int)UpdateType.Update, Is.EqualTo(3));
		Assert.That((int)UpdateType.Delete, Is.EqualTo(4));
	}

	[Test]
	public void WithAfterHandler_RegistersHandlerInDI() {
		// Arrange & Act
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic)
				.WithAfterHandler<TestAfterHandler>();
		});

		// Assert
		var handlers = sp.GetServices<ICacheAfterHandler<string, TestEntity>>().ToArray();
		Assert.That(handlers, Has.Length.EqualTo(1));
		Assert.That(handlers[0], Is.TypeOf<TestAfterHandler>());
	}

	[Test]
	public void WithAfterHandler_WithFactory_RegistersHandlerInDI() {
		// Arrange & Act
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic)
				.WithAfterHandler<TestAfterHandler>(_ => new TestAfterHandler());
		});

		// Assert
		var handlers = sp.GetServices<ICacheAfterHandler<string, TestEntity>>().ToArray();
		Assert.That(handlers, Has.Length.EqualTo(1));
		Assert.That(handlers[0], Is.TypeOf<TestAfterHandler>());
	}

	[Test]
	public void WithAfterHandler_MultipleHandlers_RegistersAllInDI() {
		// Arrange & Act
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic)
				.WithAfterHandler<TestAfterHandler>()
				.WithAfterHandler<AnotherTestAfterHandler>();
		});

		// Assert
		var handlers = sp.GetServices<ICacheAfterHandler<string, TestEntity>>().ToArray();
		Assert.That(handlers, Has.Length.EqualTo(2));
	}

	[Test]
	public void WithAfterHandler_WithFactory_CanAccessServiceProvider() {
		// Arrange
		var configValue = "test-config-value";
		_services.AddSingleton(new TestConfiguration { Value = configValue });

		// Act
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic)
				.WithAfterHandler<ConfigurableAfterHandler>(provider => {
					var config = provider.GetRequiredService<TestConfiguration>();
					return new ConfigurableAfterHandler(config.Value);
				});
		});

		// Assert
		var handlers = sp.GetServices<ICacheAfterHandler<string, TestEntity>>().ToArray();
		Assert.That(handlers, Has.Length.EqualTo(1));
		var handler = (ConfigurableAfterHandler)handlers[0];
		Assert.That(handler.ConfigValue, Is.EqualTo(configValue));
	}

	[Test]
	public async Task AfterHandler_OnAdd_ReceivesCorrectParameters() {
		// Arrange
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		// Start the consumer
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);

		// Wait for initial load to complete
		var cacheHandlers = sp.GetRequiredService<KafkaCacheHandlers>();
		// Give time for EOF processing
		await Task.Delay(100);

		// Act - produce a new entity
		var entity = new TestEntity { Id = "test-1", Name = "Test Entity", Value = 42 };
		ProduceEntity(entity);

		// Wait for processing
		await Task.Delay(200);

		// Assert
		Assert.That(recordingHandler.Invocations.Count, Is.GreaterThanOrEqualTo(1));
		var addInvocation = recordingHandler.Invocations.FirstOrDefault(i => i.UpdateType == UpdateType.Add);
		Assert.That(addInvocation, Is.Not.Null);
		Assert.That(addInvocation!.Key, Is.EqualTo("test-1"));
		Assert.That(addInvocation.NewValue, Is.Not.Null);
		Assert.That(addInvocation.NewValue!.Name, Is.EqualTo("Test Entity"));
		Assert.That(addInvocation.OldValue, Is.Null);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_OnUpdate_ReceivesOldAndNewValues() {
		// Arrange
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100); // Wait for initial load

		// Act - produce initial entity
		var entity1 = new TestEntity { Id = "test-update", Name = "Original", Value = 1 };
		ProduceEntity(entity1);
		await Task.Delay(200);

		// Update the entity
		var entity2 = new TestEntity { Id = "test-update", Name = "Updated", Value = 2 };
		ProduceEntity(entity2);
		await Task.Delay(200);

		// Assert
		var updateInvocation = recordingHandler.Invocations.LastOrDefault(i => i.UpdateType == UpdateType.Update);
		Assert.That(updateInvocation, Is.Not.Null);
		Assert.That(updateInvocation!.Key, Is.EqualTo("test-update"));
		Assert.That(updateInvocation.NewValue!.Name, Is.EqualTo("Updated"));
		Assert.That(updateInvocation.OldValue!.Name, Is.EqualTo("Original"));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_OnDelete_ReceivesDeleteUpdateType() {
		// Arrange
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(150);

		// Add entity first
		var entity = new TestEntity { Id = "test-delete", Name = "To Delete", Value = 1 };
		ProduceEntity(entity);
		await Task.Delay(300);

		// Act - delete entity (null value)
		ProduceDelete("test-delete");
		await Task.Delay(300);

		// Assert
		var deleteInvocation = recordingHandler.Invocations.LastOrDefault(i => i.UpdateType == UpdateType.Delete);
		Assert.That(deleteInvocation, Is.Not.Null,
			$"Expected Delete invocation. Got: {string.Join(", ", recordingHandler.Invocations.Select(i => $"{i.UpdateType}:{i.Key}"))}");
		Assert.That(deleteInvocation!.Key, Is.EqualTo("test-delete"));
		Assert.That(deleteInvocation.NewValue, Is.Null);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_OnSameValue_ReceivesSameUpdateType() {
		// Arrange
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Add entity
		var entity = new TestEntity { Id = "test-same", Name = "Same", Value = 1 };
		ProduceEntity(entity);
		await Task.Delay(200);

		// Produce same entity again
		ProduceEntity(entity);
		await Task.Delay(200);

		// Assert
		var sameInvocation = recordingHandler.Invocations.LastOrDefault(i => i.UpdateType == UpdateType.Same);
		Assert.That(sameInvocation, Is.Not.Null);
		Assert.That(sameInvocation!.Key, Is.EqualTo("test-same"));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_MultipleHandlers_AllAreInvoked() {
		// Arrange
		var handler1 = new RecordingAfterHandler();
		var handler2 = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(handler1);
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(handler2);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Act
		var entity = new TestEntity { Id = "test-multi", Name = "Multi", Value = 1 };
		ProduceEntity(entity);
		await Task.Delay(200);

		// Assert - both handlers should have been invoked
		Assert.That(handler1.Invocations.Any(i => i.Key == "test-multi"), Is.True);
		Assert.That(handler2.Invocations.Any(i => i.Key == "test-multi"), Is.True);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_HandlerThrowsException_DoesNotStopProcessing() {
		// Arrange
		var throwingHandler = new ThrowingAfterHandler();
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(throwingHandler);
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Act - produce multiple entities
		for (var i = 0; i < 3; i++) {
			var entity = new TestEntity { Id = $"test-throw-{i}", Name = $"Entity {i}", Value = i };
			ProduceEntity(entity);
		}

		await Task.Delay(300);

		// Assert - recording handler should still receive all messages despite throwing handler
		Assert.That(recordingHandler.Invocations.Count(i => i.Key.StartsWith("test-throw-")), Is.EqualTo(3));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_DuringInitialLoad_NotInvoked() {
		// Arrange - Pre-populate topic before starting consumer
		for (var i = 0; i < 5; i++) {
			var entity = new TestEntity { Id = $"preload-{i}", Name = $"Preloaded {i}", Value = i };
			ProduceEntity(entity);
		}

		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var hostedService = sp.GetRequiredService<IHostedService>();

		// Act
		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);

		// Wait for initial load to complete
		await Task.Delay(300);

		// Assert - no invocations during initial load
		Assert.That(recordingHandler.Invocations.Count(i => i.Key.StartsWith("preload-")), Is.EqualTo(0));

		// But new messages after initial load should trigger handlers
		var newEntity = new TestEntity { Id = "after-load", Name = "After Load", Value = 99 };
		ProduceEntity(newEntity);
		await Task.Delay(200);

		Assert.That(recordingHandler.Invocations.Any(i => i.Key == "after-load"), Is.True);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task AfterHandler_CacheIsUpdatedBeforeHandlerInvoked() {
		// Arrange
		TestEntityCache? capturedCache = null;
		TestEntity? capturedCacheValue = null;

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic)
				.WithAfterHandler<CacheCheckingAfterHandler>(provider => {
					capturedCache = provider.GetRequiredService<TestEntityCache>();
					return new CacheCheckingAfterHandler(capturedCache,
						(key, value) => { capturedCacheValue = capturedCache.TryGet(key, out var v) ? v : null; });
				});
		});

		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Act
		var entity = new TestEntity { Id = "cache-check", Name = "Cache Check", Value = 42 };
		ProduceEntity(entity);
		await Task.Delay(200);

		// Assert - cache should have the value when handler is invoked
		Assert.That(capturedCacheValue, Is.Not.Null);
		Assert.That(capturedCacheValue!.Name, Is.EqualTo("Cache Check"));

		await hostedService.StopAsync(cts.Token);
	}

	private void ProduceEntity(TestEntity entity) {
		var keyBytes = MessagePackSerializer.Serialize(entity.Id);
		var valueBytes = MessagePackSerializer.Serialize(entity);

		KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
			Message = new Message<byte[], byte[]> {
				Key = keyBytes,
				Value = valueBytes,
				Headers = new Headers()
			}
		});
	}

	private void ProduceDelete(string key) {
		var keyBytes = MessagePackSerializer.Serialize(key);

		KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
			Message = new Message<byte[], byte[]> {
				Key = keyBytes,
				Value = null!,
				Headers = new Headers()
			}
		});
	}

	private class TestConfiguration {
		public string Value { get; set; } = "";
	}

	private class TestAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			return ValueTask.CompletedTask;
		}
	}

	private class AnotherTestAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			return ValueTask.CompletedTask;
		}
	}

	private class ConfigurableAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ConfigurableAfterHandler(string configValue) {
			ConfigValue = configValue;
		}

		public string ConfigValue { get; }

		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			return ValueTask.CompletedTask;
		}
	}

	private class RecordingAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ConcurrentBag<AfterHandlerInvocation> Invocations { get; } = new();

		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			Invocations.Add(new AfterHandlerInvocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}

	private record AfterHandlerInvocation(UpdateType UpdateType, string Key, TestEntity? NewValue, TestEntity? OldValue);

	private class ThrowingAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			throw new InvalidOperationException("Test exception from handler");
		}
	}

	private class CacheCheckingAfterHandler : ICacheAfterHandler<string, TestEntity> {
		private readonly TestEntityCache _cache;
		private readonly Action<string, TestEntity?> _onHandle;

		public CacheCheckingAfterHandler(TestEntityCache cache, Action<string, TestEntity?> onHandle) {
			_cache = cache;
			_onHandle = onHandle;
		}

		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			_onHandle(key, newValue);
			return ValueTask.CompletedTask;
		}
	}
}
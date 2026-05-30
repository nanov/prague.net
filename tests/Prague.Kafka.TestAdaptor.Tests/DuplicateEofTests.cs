namespace Prague.Kafka.TestAdaptor.Tests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class DuplicateEofTests {
	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private const string TestTopic = "duplicate-eof-test-topic";
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
	public async Task DuplicateEof_DoesNotCrash() {
		// Arrange - pre-populate some data
		for (var i = 0; i < 3; i++)
			ProduceEntity(new TestEntity { Id = $"preload-{i}", Name = $"Preloaded {i}", Value = i });

		var sp = BuildServiceProvider();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(200);

		// Act - simulate duplicate EOF (e.g. from rebalancing)
		KafkaCacheTestBuilderProviderMarshall.SimulatePartitionEof(_provider, TestTopic);
		await Task.Delay(200);

		// Assert - cache should still be functional
		var cache = sp.GetRequiredService<TestEntityCache>();
		Assert.That(cache.TryGet("preload-0", out var entity), Is.True);
		Assert.That(entity!.Name, Is.EqualTo("Preloaded 0"));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task DuplicateEof_CacheContinuesToProcessMessages() {
		// Arrange
		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(200);

		// Act - simulate duplicate EOF
		KafkaCacheTestBuilderProviderMarshall.SimulatePartitionEof(_provider, TestTopic);
		await Task.Delay(100);

		// Produce a message after the duplicate EOF
		ProduceEntity(new TestEntity { Id = "after-eof", Name = "After EOF", Value = 42 });
		await Task.Delay(200);

		// Assert - message should be processed and after handler invoked
		var cache = sp.GetRequiredService<TestEntityCache>();
		Assert.That(cache.TryGet("after-eof", out var entity), Is.True);
		Assert.That(entity!.Name, Is.EqualTo("After EOF"));

		Assert.That(recordingHandler.Invocations.Any(i => i.Key == "after-eof" && i.UpdateType == UpdateType.Add), Is.True);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task MultipleEofs_CacheRemainsStable() {
		// Arrange - pre-populate
		for (var i = 0; i < 5; i++)
			ProduceEntity(new TestEntity { Id = $"initial-{i}", Name = $"Initial {i}", Value = i });

		var recordingHandler = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<string, TestEntity>>(recordingHandler);

		var sp = BuildServiceProvider();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(200);

		// Act - simulate multiple EOFs
		for (var i = 0; i < 5; i++) {
			KafkaCacheTestBuilderProviderMarshall.SimulatePartitionEof(_provider, TestTopic);
			await Task.Delay(50);
		}

		// Produce messages interleaved
		for (var i = 0; i < 3; i++) {
			ProduceEntity(new TestEntity { Id = $"post-eof-{i}", Name = $"Post EOF {i}", Value = 100 + i });
			await Task.Delay(50);
		}

		await Task.Delay(300);

		// Assert - all messages should be in the cache
		var cache = sp.GetRequiredService<TestEntityCache>();
		for (var i = 0; i < 5; i++)
			Assert.That(cache.TryGet($"initial-{i}", out _), Is.True, $"initial-{i} missing from cache");

		for (var i = 0; i < 3; i++)
			Assert.That(cache.TryGet($"post-eof-{i}", out _), Is.True, $"post-eof-{i} missing from cache");

		// After handlers should have been invoked for post-EOF messages
		Assert.That(recordingHandler.Invocations.Count(i => i.Key.StartsWith("post-eof-")), Is.EqualTo(3));

		// After handlers should NOT have been invoked for initial load messages
		Assert.That(recordingHandler.Invocations.Count(i => i.Key.StartsWith("initial-")), Is.EqualTo(0));

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

	private class RecordingAfterHandler : ICacheAfterHandler<string, TestEntity> {
		public ConcurrentBag<AfterHandlerInvocation> Invocations { get; } = new();

		public ValueTask Handle(UpdateType updateType, string key, TestEntity? newValue, TestEntity? oldValue) {
			Invocations.Add(new AfterHandlerInvocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}

	private record AfterHandlerInvocation(UpdateType UpdateType, string Key, TestEntity? NewValue, TestEntity? OldValue);
}

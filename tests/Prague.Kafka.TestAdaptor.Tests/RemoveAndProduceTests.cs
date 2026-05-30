namespace Prague.Kafka.TestAdaptor.Tests;

using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class RemoveAndProduceTests {
	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private const string TestTopic = "remove-produce-test-topic";
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
	public async Task RemoveAndProduce_ExistingEntity_RemovesFromCacheAndReturnsTrue() {
		// Arrange
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Add an entity first via direct cache manipulation
		var entity = new TestEntity { Id = "remove-test-1", Name = "To Remove", Value = 42 };
		cache.AddOrUpdate(entity);

		// Verify entity is in cache
		Assert.That(cache.TryGet("remove-test-1", out _), Is.True);

		// Act - remove and produce
		var result = cache.RemoveAndProduce("remove-test-1");

		// Assert
		Assert.That(result, Is.True, "RemoveAndProduce should return true for existing entity");
		Assert.That(cache.TryGet("remove-test-1", out _), Is.False, "Entity should be removed from cache");

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task RemoveAndProduce_NonExistingEntity_ReturnsFalse() {
		// Arrange
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Act - try to remove non-existing entity
		var result = cache.RemoveAndProduce("non-existing-key");

		// Assert
		Assert.That(result, Is.False, "RemoveAndProduce should return false for non-existing entity");

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task RemoveAndProduce_AfterAddOrUpdateAndProduce_RemovesCorrectly() {
		// Arrange
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Add via AddOrUpdateAndProduce
		var entity = new TestEntity { Id = "add-remove-test", Name = "Add Then Remove", Value = 100 };
		cache.AddOrUpdateAndProduce(entity);

		// Verify added
		Assert.That(cache.TryGet("add-remove-test", out var addedEntity), Is.True);
		Assert.That(addedEntity!.Name, Is.EqualTo("Add Then Remove"));

		// Act - remove via RemoveAndProduce
		var result = cache.RemoveAndProduce("add-remove-test");

		// Assert
		Assert.That(result, Is.True);
		Assert.That(cache.TryGet("add-remove-test", out _), Is.False);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task RemoveAndProduce_MultipleEntities_RemovesAllCorrectly() {
		// Arrange
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Add multiple entities
		for (var i = 0; i < 5; i++) {
			var entity = new TestEntity { Id = $"multi-remove-{i}", Name = $"Entity {i}", Value = i };
			cache.AddOrUpdate(entity);
		}

		// Verify all added
		for (var i = 0; i < 5; i++) Assert.That(cache.TryGet($"multi-remove-{i}", out _), Is.True);

		// Act - remove all entities
		var results = new List<bool>();
		for (var i = 0; i < 5; i++) results.Add(cache.RemoveAndProduce($"multi-remove-{i}"));

		// Assert
		Assert.That(results, Is.All.True, "All removes should return true");

		// Verify all entities are removed from cache
		for (var i = 0; i < 5; i++) Assert.That(cache.TryGet($"multi-remove-{i}", out _), Is.False);

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public void RemoveAndProduce_WithoutProducerConfigured_ThrowsInvalidOperationException() {
		// Arrange - build service provider but don't start consumer (producer not configured)
		var services = new ServiceCollection();
		services.AddSingleton<TestEntityCache>();

		var sp = services.BuildServiceProvider();
		var cache = sp.GetRequiredService<TestEntityCache>();

		// Add an entity to ensure there's something to remove
		cache.AddOrUpdate(new TestEntity { Id = "no-producer-test", Name = "Test", Value = 1 });

		// Act & Assert
		Assert.Throws<InvalidOperationException>(() => cache.RemoveAndProduce("no-producer-test"),
			"Should throw when producer is not configured");
	}

	[Test]
	public void CacheMarshallDelete_WithoutProducerConfigured_ThrowsInvalidOperationException() {
		// Arrange - build service provider but don't start consumer (producer not configured)
		var services = new ServiceCollection();
		services.AddSingleton<TestEntityCache>();

		var sp = services.BuildServiceProvider();
		var cache = sp.GetRequiredService<TestEntityCache>();

		// Act & Assert
		Assert.Throws<InvalidOperationException>(() => CacheMarshall.Delete(cache, "some-key"),
			"Should throw when producer is not configured");
	}

	[Test]
	public async Task RemoveAndProduce_ThenReAdd_WorksCorrectly() {
		// Arrange
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Add entity
		var entity = new TestEntity { Id = "readd-test", Name = "Original", Value = 1 };
		cache.AddOrUpdateAndProduce(entity);

		// Verify added
		Assert.That(cache.TryGet("readd-test", out var added), Is.True);
		Assert.That(added!.Name, Is.EqualTo("Original"));

		// Remove entity
		var removeResult = cache.RemoveAndProduce("readd-test");
		Assert.That(removeResult, Is.True);
		Assert.That(cache.TryGet("readd-test", out _), Is.False);

		// Re-add entity with different value
		var entity2 = new TestEntity { Id = "readd-test", Name = "Re-added", Value = 2 };
		cache.AddOrUpdateAndProduce(entity2);

		// Verify final state in cache
		Assert.That(cache.TryGet("readd-test", out var cachedEntity), Is.True);
		Assert.That(cachedEntity!.Name, Is.EqualTo("Re-added"));
		Assert.That(cachedEntity.Value, Is.EqualTo(2));

		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task RemoveAndProduce_ProducesMessageToKafka() {
		// Arrange - setup a separate consumer to verify message was produced
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Create a separate raw consumer to verify messages
		var rawConsumer = builderProvider.NewConsumerBuilder<byte[], byte[]>(new Dictionary<string, string> {
				{ "group.id", "test-verify-group" }
			})
			.Build();
		rawConsumer.Subscribe(TestTopic);

		// Consume initial EOF
		rawConsumer.Consume(TimeSpan.FromMilliseconds(500));

		// Add and then remove an entity
		var entity = new TestEntity { Id = "verify-produce", Name = "Verify", Value = 123 };
		cache.AddOrUpdate(entity);
		cache.RemoveAndProduce("verify-produce");

		// Act - consume the delete message
		var result = rawConsumer.Consume(TimeSpan.FromMilliseconds(500));

		// Assert
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Message, Is.Not.Null);
		Assert.That(result.Message.Value, Is.Null, "Delete message should have null value (tombstone)");

		// Verify the key is correct
		var key = MessagePackSerializer.Deserialize<string>(result.Message.Key);
		Assert.That(key, Is.EqualTo("verify-produce"));

		rawConsumer.Dispose();
		await hostedService.StopAsync(cts.Token);
	}

	[Test]
	public async Task CacheMarshallDelete_ProducesMessageToKafka() {
		// Arrange - setup a separate consumer to verify message was produced
		var sp = BuildServiceProvider(builder => { builder.AddCache<TestEntityCache, string, TestEntity>(TestTopic); });

		var cache = sp.GetRequiredService<TestEntityCache>();
		var hostedService = sp.GetRequiredService<IHostedService>();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		using var cts = new CancellationTokenSource();
		await hostedService.StartAsync(cts.Token);
		await Task.Delay(100);

		// Create a separate raw consumer to verify messages
		var rawConsumer = builderProvider.NewConsumerBuilder<byte[], byte[]>(new Dictionary<string, string> {
				{ "group.id", "test-verify-group-2" }
			})
			.Build();
		rawConsumer.Subscribe(TestTopic);

		// Consume initial EOF
		rawConsumer.Consume(TimeSpan.FromMilliseconds(500));

		// Act - call CacheMarshall.Delete directly
		CacheMarshall.Delete(cache, "direct-delete-key");

		// Consume the delete message
		var result = rawConsumer.Consume(TimeSpan.FromMilliseconds(500));

		// Assert
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Message, Is.Not.Null);
		Assert.That(result.Message.Value, Is.Null, "Delete message should have null value (tombstone)");

		// Verify the key is correct
		var key = MessagePackSerializer.Deserialize<string>(result.Message.Key);
		Assert.That(key, Is.EqualTo("direct-delete-key"));

		rawConsumer.Dispose();
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
}
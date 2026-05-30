namespace Prague.Kafka.TestAdaptor.Tests.KeyFilters;

using Prague.Kafka;
using Prague.Kafka.TestAdaptor.Tests.TestEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Concurrent;
using Confluent.Kafka;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class KafkaKeyFiltersIntegrationTests {
	private const string TestTopic = "key-filter-test-topic";
	private ServiceCollection _services = null!;
	private IKafkaCacheTestBuilderProvider _provider = null!;

	[OneTimeSetUp]
	public void OneTimeSetUp() {
		MessagePackSerializer.DefaultOptions = ContractlessStandardResolver.Options;
	}

	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

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

	private void ProduceEntity(int id, string name) {
		var keyBytes = MessagePackSerializer.Serialize(id);
		var entity = new TestEntityWithLongTimestamp {
			Id = id,
			Name = name
		};
		var valueBytes = MessagePackSerializer.Serialize(entity);

		KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
			Message = new Message<byte[], byte[]> {
				Key = keyBytes,
				Value = valueBytes,
				Headers = new Headers()
			}
		});
	}

	private void ProduceDelete(int id) {
		var keyBytes = MessagePackSerializer.Serialize(id);

		KafkaCacheTestBuilderProviderMarshall.Produce(_provider, TestTopic, new ConsumeResult<byte[], byte[]> {
			Message = new Message<byte[], byte[]> {
				Key = keyBytes,
				Value = null!,
				Headers = new Headers()
			}
		});
	}

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, TestEntityWithLongTimestamp> {
		public ConcurrentBag<UpdateType> Updates { get; } = new();
		public ConcurrentBag<int> Keys { get; } = new();

		public ValueTask Handle(UpdateType updateType, int key, TestEntityWithLongTimestamp? newValue, TestEntityWithLongTimestamp? oldValue) {
			Updates.Add(updateType);
			Keys.Add(key);
			return ValueTask.CompletedTask;
		}
	}

	[Test]
	public void WithKeyFilter_Builder_AcceptsPredicate_AndReturnsBuilder() {
		var sp = BuildServiceProvider(builder => {
			var b = builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 0);
			Assert.That(b, Is.Not.Null);
		});

		Assert.That(sp.GetRequiredService<KafkaCacheHandlers>(), Is.Not.Null);
	}

	[Test]
	public async Task WithKeyFilter_RejectsKeysFailingPredicate_DuringInitialLoad() {
		for (var i = 1; i <= 5; i++)
			ProduceEntity(i, $"entity-{i}");

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 2);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.False);
		Assert.That(cache.TryGet(2, out _), Is.False);
		Assert.That(cache.TryGet(3, out _), Is.True);
		Assert.That(cache.TryGet(4, out _), Is.True);
		Assert.That(cache.TryGet(5, out _), Is.True);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_RejectsKeysFailingPredicate_LivePhase() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 2);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		for (var i = 1; i <= 5; i++)
			ProduceEntity(i, $"entity-{i}");
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.False);
		Assert.That(cache.TryGet(2, out _), Is.False);
		Assert.That(cache.TryGet(3, out _), Is.True);
		Assert.That(cache.TryGet(4, out _), Is.True);
		Assert.That(cache.TryGet(5, out _), Is.True);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_MultiplePredicates_ComposeWithAnd() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 1)
				.WithKeyFilter(k => k < 4);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		for (var i = 1; i <= 5; i++)
			ProduceEntity(i, $"entity-{i}");
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.False);
		Assert.That(cache.TryGet(2, out _), Is.True);
		Assert.That(cache.TryGet(3, out _), Is.True);
		Assert.That(cache.TryGet(4, out _), Is.False);
		Assert.That(cache.TryGet(5, out _), Is.False);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_FiresAfterHandlerFilter_OnLiveRejection() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 2);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceEntity(1, "rejected");
		ProduceEntity(3, "accepted");
		await Task.Delay(300);

		Assert.That(recording.Updates.Count(u => u == UpdateType.Filtered), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Updates.Count(u => u == UpdateType.Add), Is.GreaterThanOrEqualTo(1));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_TombstoneForFilteredKey_IsDropped() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 2);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceDelete(1);
		await Task.Delay(300);

		Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.EqualTo(0));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_TreatAsDelete_RemovesExistingKey_AndFiresDelete_LivePhase() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		// Mutable allow-set the predicate closes over (thread-safe for cross-thread visibility).
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => allowed.ContainsKey(k), treatAsDelete: true);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		// Admitted while key 3 is allowed.
		ProduceEntity(3, "present");
		await Task.Delay(200);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(3, out _), Is.True);

		// Key 3 is offboarded; the next message for it is rejected -> tombstone.
		allowed.TryRemove(3, out _);
		ProduceEntity(3, "now-rejected");
		await Task.Delay(300);

		Assert.That(cache.TryGet(3, out _), Is.False);
		Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Updates.Count(u => u == UpdateType.Filtered), Is.EqualTo(0));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_DefaultFalse_KeepsExistingValue_OnLiveRejection() {
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => allowed.ContainsKey(k));
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceEntity(3, "present");
		await Task.Delay(200);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(3, out _), Is.True);

		// Rejected, but without treatAsDelete the message is dropped and the cached value remains.
		allowed.TryRemove(3, out _);
		ProduceEntity(3, "now-rejected");
		await Task.Delay(300);

		Assert.That(cache.TryGet(3, out var present), Is.True);
		Assert.That(present!.Name, Is.EqualTo("present"));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_TreatAsDelete_RejectedKeyFromStart_IsNotCached() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => k > 2, treatAsDelete: true);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		// Key 1 is rejected from the start: the delete branch is a safe no-op (nothing to remove).
		ProduceEntity(1, "rejected");
		ProduceEntity(3, "accepted");
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.False);
		Assert.That(cache.TryGet(3, out _), Is.True);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithKeyFilter_PredicateThrows_IsTreatedAsReject_AndLoopKeepsRunning() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithKeyFilter(k => {
					if (k == 7)
						throw new InvalidOperationException("intentional");
					return true;
				});
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceEntity(7, "throws");
		ProduceEntity(8, "ok");
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(7, out _), Is.False);
		Assert.That(cache.TryGet(8, out _), Is.True);

		await hosted.StopAsync(cts.Token);
	}
}

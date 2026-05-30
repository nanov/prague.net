namespace Prague.Kafka.TestAdaptor.Tests.ValueFilters;

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
public class KafkaValueFiltersIntegrationTests {
	private const string TestTopic = "value-filter-test-topic";
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
	public void WithValueFilter_Builder_AcceptsPredicate_AndReturnsBuilder() {
		var sp = BuildServiceProvider(builder => {
			var b = builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Id > 0);
			Assert.That(b, Is.Not.Null);
		});

		Assert.That(sp.GetRequiredService<KafkaCacheHandlers>(), Is.Not.Null);
	}

	[Test]
	public async Task WithValueFilter_RejectsValuesFailingPredicate_DuringInitialLoad() {
		for (var i = 1; i <= 5; i++)
			ProduceEntity(i, $"entity-{i}");

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Id > 2);
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
	public async Task WithValueFilter_RejectsValuesFailingPredicate_LivePhase() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Name.StartsWith("keep"));
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceEntity(1, "drop-1");
		ProduceEntity(2, "keep-2");
		ProduceEntity(3, "drop-3");
		ProduceEntity(4, "keep-4");
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.False);
		Assert.That(cache.TryGet(2, out _), Is.True);
		Assert.That(cache.TryGet(3, out _), Is.False);
		Assert.That(cache.TryGet(4, out _), Is.True);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_MultiplePredicates_ComposeWithAnd() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Id > 1)
				.WithValueFilter(v => v.Id < 4);
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
	public async Task WithValueFilter_FiresAfterHandlerFilter_OnLiveRejection() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Id > 2);
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
	public async Task WithValueFilter_TombstoneSkipsFilter_AndStillDeletes() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => v.Id > 2);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		// Accepted by the filter, lands in the cache.
		ProduceEntity(3, "present");
		await Task.Delay(200);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(3, out _), Is.True);

		// Tombstone: the value filter must NOT run (no value), and the delete must proceed.
		ProduceDelete(3);
		await Task.Delay(300);

		Assert.That(cache.TryGet(3, out _), Is.False);
		Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_TreatAsDelete_RemovesExistingKey_AndFiresDelete_LivePhase() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => !v.Name.StartsWith("deleted"), treatAsDelete: true);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		// Accepted by the filter, lands in the cache.
		ProduceEntity(3, "active");
		await Task.Delay(200);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(3, out _), Is.True);

		// Rejected by the filter — treatAsDelete makes this a tombstone for key 3.
		ProduceEntity(3, "deleted-3");
		await Task.Delay(300);

		Assert.That(cache.TryGet(3, out _), Is.False);
		Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Updates.Count(u => u == UpdateType.Filtered), Is.EqualTo(0));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_DefaultFalse_KeepsExistingValue_OnLiveRejection() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => !v.Name.StartsWith("deleted"));
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		ProduceEntity(3, "active");
		await Task.Delay(200);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(3, out _), Is.True);

		// Rejected, but without treatAsDelete the message is dropped and the cached value remains.
		ProduceEntity(3, "deleted-3");
		await Task.Delay(300);

		Assert.That(cache.TryGet(3, out var present), Is.True);
		Assert.That(present!.Name, Is.EqualTo("active"));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_TreatAsDelete_RejectedDuringInitialLoad_IsNotCached() {
		ProduceEntity(1, "active");
		ProduceEntity(2, "deleted-2");

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => !v.Name.StartsWith("deleted"), treatAsDelete: true);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(300);

		var cache = sp.GetRequiredService<TestEntityWithLongTimestampCache>();
		Assert.That(cache.TryGet(1, out _), Is.True);
		Assert.That(cache.TryGet(2, out _), Is.False);

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_FirstRejectWins_PlainFilterBeforeDeleteFilter_DoesNotDelete() {
		var recording = new RecordingAfterHandler();
		_services.AddSingleton<ICacheAfterHandler<int, TestEntityWithLongTimestamp>>(recording);

		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				// First filter (plain) rejects Id <= 0; second filter (treatAsDelete) rejects "deleted".
				// A value failing the FIRST filter must Skip, not Delete.
				.WithValueFilter(v => v.Id > 0)
				.WithValueFilter(v => !v.Name.StartsWith("deleted"), treatAsDelete: true);
		});

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource();
		await hosted.StartAsync(cts.Token);
		await Task.Delay(150);

		// Seed key 5 so the later delete-filter rejection has something to remove.
		ProduceEntity(5, "active");
		await Task.Delay(200);

		// Fails the first (plain) filter only -> Skip (Filtered), never reaches the delete filter.
		ProduceEntity(0, "active");
		// Fails the second (treatAsDelete) filter -> Delete.
		ProduceEntity(5, "deleted-5");
		await Task.Delay(300);

		Assert.That(recording.Updates.Count(u => u == UpdateType.Filtered), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Updates.Count(u => u == UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await hosted.StopAsync(cts.Token);
	}

	[Test]
	public async Task WithValueFilter_PredicateThrows_IsTreatedAsReject_AndLoopKeepsRunning() {
		var sp = BuildServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithValueFilter(v => {
					if (v.Id == 7)
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

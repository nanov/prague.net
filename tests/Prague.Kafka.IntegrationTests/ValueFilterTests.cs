namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class ValueFilterTests {
	private const string TopicPrefix = "it-value-filter";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task WithValueFilter_RejectsValuesFailingPredicate_DuringInitialLoad() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var i = 1; i <= 5; i++)
			Produce(producer, i, $"entity-{i}", i);
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, cache) = await StartAsync(b => b.WithValueFilter(v => v.Value > 2));

		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.False);
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);
		Assert.That(cache.Cache.TryGet(4, out _), Is.True);
		Assert.That(cache.Cache.TryGet(5, out _), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithValueFilter_LivePhase_AcceptedPresent_RejectedAbsent() {
		var (sp, cache) = await StartAsync(b => b.WithValueFilter(v => v.Name.StartsWith("keep")));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "drop-1", 1);
		Produce(producer, 2, "keep-2", 2);
		Produce(producer, 3, "drop-3", 3);
		Produce(producer, 4, "keep-4", 4);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(4, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.True);
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
		Assert.That(cache.Cache.TryGet(4, out _), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithValueFilter_TombstoneSkipsFilter_AndStillDeletes() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(
			b => b.WithValueFilter(v => v.Value > 2),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		// Accepted by the filter, lands in the cache.
		Produce(producer, 3, "present", 3);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);

		// Tombstone: the value filter must NOT run (no value), and the delete must proceed.
		ProduceTombstone(producer, 3);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => !cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
		await WaitUntil(() => recording.Count(UpdateType.Delete) >= 1);
		Assert.That(recording.Count(UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithValueFilter_TreatAsDelete_RemovesExistingKey_AndFiresDelete_LivePhase() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(
			b => b.WithValueFilter(v => !v.Name.StartsWith("deleted"), treatAsDelete: true),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 3, "active", 3);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);

		// Rejected by the filter — treatAsDelete makes this a tombstone for key 3.
		Produce(producer, 3, "deleted-3", 3);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => !cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
		await WaitUntil(() => recording.Count(UpdateType.Delete) >= 1);
		Assert.That(recording.Count(UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithValueFilter_DefaultFalse_KeepsExistingValue_OnLiveRejection() {
		var (sp, cache) = await StartAsync(b => b.WithValueFilter(v => !v.Name.StartsWith("deleted")));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 3, "active", 3);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));

		// Rejected, but without treatAsDelete the message is dropped and the cached value remains.
		Produce(producer, 3, "deleted-3", 3);
		producer.Flush(TimeSpan.FromSeconds(10));

		await Task.Delay(1000);
		Assert.That(cache.Cache.TryGet(3, out var present), Is.True);
		Assert.That(present!.Name, Is.EqualTo("active"));

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, int value) {
		var entity = new FilterEntity { Id = id, Name = name, Value = value };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	private void ProduceTombstone(IProducer<byte[], byte[]> producer, int id) {
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = null!,
			Headers = new Headers()
		});
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>> configure,
		Action<IServiceCollection>? extra = null) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		extra?.Invoke(services);
		services.AddKafkaCaches("KafkaConfig", b => {
			configure(b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));
		});

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private static async Task StopAsync(IServiceProvider sp) {
		var hosted = sp.GetRequiredService<IHostedService>();
		await hosted.StopAsync(CancellationToken.None);
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition()) {
			if (cts.IsCancellationRequested)
				return;
			await Task.Delay(50);
		}
	}

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentBag<UpdateType> _updates = new();

		public int Count(UpdateType type) => _updates.Count(u => u == type);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_updates.Add(updateType);
			return ValueTask.CompletedTask;
		}
	}
}

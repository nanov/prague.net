namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class KeyFilterTests {
	private const string TopicPrefix = "it-key-filter";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task WithKeyFilter_RejectsKeysFailingPredicate_DuringInitialLoad() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var i = 1; i <= 5; i++)
			Produce(producer, i, $"entity-{i}");
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, cache) = await StartAsync(b => b.WithKeyFilter(k => k > 2));

		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.False);
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);
		Assert.That(cache.Cache.TryGet(4, out _), Is.True);
		Assert.That(cache.Cache.TryGet(5, out _), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_MultiplePredicates_ComposeWithAnd_DuringInitialLoad() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var i = 1; i <= 5; i++)
			Produce(producer, i, $"entity-{i}");
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, cache) = await StartAsync(b => b
			.WithKeyFilter(k => k > 1)
			.WithKeyFilter(k => k < 4));

		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.True);
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);
		Assert.That(cache.Cache.TryGet(4, out _), Is.False);
		Assert.That(cache.Cache.TryGet(5, out _), Is.False);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_LivePhase_AcceptedPresent_RejectedAbsent_AndFiresFiltered() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => k > 2),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "rejected");
		Produce(producer, 3, "accepted");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(3, out _));

		Assert.That(cache.Cache.TryGet(3, out _), Is.True, "Accepted key should be present");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Rejected key should be absent");
		await WaitUntil(() => recording.Count(UpdateType.Filtered) >= 1);
		Assert.That(recording.Count(UpdateType.Filtered), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Count(UpdateType.Add), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_TreatAsDelete_RemovesExistingKey_AndFiresDelete_LivePhase() {
		var recording = new RecordingAfterHandler();
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => allowed.ContainsKey(k), treatAsDelete: true),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		// Admitted while key 3 is allowed.
		Produce(producer, 3, "present");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);

		// Key 3 is offboarded; the next message for it is rejected -> tombstone.
		allowed.TryRemove(3, out _);
		Produce(producer, 3, "now-rejected");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => !cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
		await WaitUntil(() => recording.Count(UpdateType.Delete) >= 1);
		Assert.That(recording.Count(UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_DefaultFalse_KeepsExistingValue_OnLiveRejection() {
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var (sp, cache) = await StartAsync(b => b.WithKeyFilter(k => allowed.ContainsKey(k)));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 3, "present");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));

		// Rejected, but without treatAsDelete the message is dropped and the cached value remains.
		allowed.TryRemove(3, out _);
		Produce(producer, 3, "now-rejected");
		producer.Flush(TimeSpan.FromSeconds(10));

		// Give the rejected message time to be processed (and ignored).
		await Task.Delay(1000);
		Assert.That(cache.Cache.TryGet(3, out var present), Is.True);
		Assert.That(present!.Name, Is.EqualTo("present"));

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
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

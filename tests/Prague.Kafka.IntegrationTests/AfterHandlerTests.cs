namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class AfterHandlerTests {
	private const string TopicPrefix = "it-after-handler";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task AfterHandler_OnAdd_ReceivesAddWithNewValue() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "Added", 42);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == 1));
		var add = recording.Invocations.First(i => i.UpdateType == UpdateType.Add && i.Key == 1);
		Assert.That(add.NewValue, Is.Not.Null);
		Assert.That(add.NewValue!.Name, Is.EqualTo("Added"));
		Assert.That(add.OldValue, Is.Null);
		Assert.That(cache.Cache.TryGet(1, out _), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task AfterHandler_OnUpdate_ReceivesOldAndNewValues() {
		var recording = new RecordingAfterHandler();
		var (sp, _) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "Original", 1);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == 1));

		Produce(producer, 1, "Updated", 2);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == 1));

		var update = recording.Invocations.Last(i => i.UpdateType == UpdateType.Update && i.Key == 1);
		Assert.That(update.NewValue!.Name, Is.EqualTo("Updated"));
		Assert.That(update.OldValue!.Name, Is.EqualTo("Original"));

		await StopAsync(sp);
	}

	[Test]
	public async Task AfterHandler_OnSameValue_ReceivesSame() {
		var recording = new RecordingAfterHandler();
		var (sp, _) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "Same", 1);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == 1));

		// Identical payload again -> Same.
		Produce(producer, 1, "Same", 1);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1));

		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task AfterHandler_OnDelete_ReceivesDelete() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "ToDelete", 1);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(1, out _));

		ProduceTombstone(producer, 1);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Delete && i.Key == 1));

		var del = recording.Invocations.Last(i => i.UpdateType == UpdateType.Delete && i.Key == 1);
		Assert.That(del.NewValue, Is.Null);
		Assert.That(cache.Cache.TryGet(1, out _), Is.False);

		await StopAsync(sp);
	}

	[Test]
	public async Task AfterHandler_DuringInitialLoad_NotInvoked_ButLivePhaseIs() {
		// Pre-populate before consumer starts: these must NOT fire the after-handler.
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var i = 1; i <= 5; i++)
				Produce(seeder, i, $"preload-{i}", i);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		// After the loader completed, the preloaded keys are cached but never went through the after-handler.
		Assert.That(cache.Cache.Count, Is.EqualTo(5));
		Assert.That(recording.Invocations.Count, Is.EqualTo(0), "Initial-load records must not invoke the after-handler");

		// A live message after load completion must invoke it.
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 99, "after-load", 99);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => recording.Any(i => i.Key == 99));
		Assert.That(recording.Any(i => i.Key == 99 && i.UpdateType == UpdateType.Add), Is.True);

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

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(RecordingAfterHandler recording) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording);
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
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

	private sealed record Invocation(UpdateType UpdateType, int Key, FilterEntity? NewValue, FilterEntity? OldValue);

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<Invocation> _invocations = new();

		public IReadOnlyCollection<Invocation> Invocations => _invocations;

		public bool Any(Func<Invocation, bool> predicate) => _invocations.Any(predicate);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_invocations.Enqueue(new Invocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}
}

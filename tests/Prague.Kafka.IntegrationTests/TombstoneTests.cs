namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prague.Kafka.IO;

[TestFixture]
public class TombstoneTests {
	private const string TopicPrefix = "it-tombstone";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task NullValueTombstone_RemovesKeyFromCache_InLivePhase() {
		var services = BuildServices();
		using var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(1),
			Value = MessagePackSerializer.Serialize(new FilterEntity { Id = 1, Name = "present", Value = 1 }),
			Headers = new Headers()
		});
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.True);

		// Tombstone: null value for the same key removes it.
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(1),
			Value = null!,
			Headers = new Headers()
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => !cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Tombstone must remove the key");

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	[Test]
	public async Task TombstoneDuringInitialLoad_LeavesKeyAbsent() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			seeder.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(1),
				Value = MessagePackSerializer.Serialize(new FilterEntity { Id = 1, Name = "present", Value = 1 }),
				Headers = new Headers()
			});
			seeder.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(1),
				Value = null!,
				Headers = new Headers()
			});
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		using var sp = BuildServices().BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Value then tombstone during load -> absent");

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	[Test]
	public async Task TombstoneAfterBufferFlushDuringInitialLoad_LeavesKeyAbsent() {
		var lastSpacer = 100 + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, "present");

			// Enough values for other keys to cross a compacting-buffer flush, so key 1 is already in the cache.
			for (var i = 100; i <= lastSpacer; i++)
				Produce(seeder, i, $"spacer-{i}");

			seeder.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(1),
				Value = null!,
				Headers = new Headers()
			});
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		using var sp = BuildServices().BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True, "Spacer keys must be loaded");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Tombstone after a buffer flush during load -> absent");

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name) {
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(new FilterEntity { Id = id, Name = name, Value = id }),
			Headers = new Headers()
		});
	}

	private ServiceCollection BuildServices() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
				// Own group per provider: sharing one group.id across tests means each teardown
				// rebalances the group and can stall a neighbouring test's initial load.
				{ "KafkaConfig:ClientSettings:group.id", Guid.NewGuid().ToString() }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
		});
		return services;
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition()) {
			if (cts.IsCancellationRequested)
				return;
			await Task.Delay(50);
		}
	}
}

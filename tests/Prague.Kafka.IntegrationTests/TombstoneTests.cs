namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
		var sp = services.BuildServiceProvider();
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

		var sp = BuildServices().BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Value then tombstone during load -> absent");

		await hosted.StopAsync(CancellationToken.None);
	}

	private ServiceCollection BuildServices() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
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

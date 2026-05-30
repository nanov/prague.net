namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using IO;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class SelfConsumeTests {
	private const string TopicPrefix = "it-self-consume";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task OwnProducerWrites_AreFilteredOut_ButForeignWritesAreIngested() {
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

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();

		// Write via Prague's own producer: it stamps the producer-instance header, so the
		// consumer must NOT ingest this record (self-consume guard).
		var pragueProducer = sp.GetRequiredKeyedService<KafkaCacheProducer>("KafkaConfig");
		pragueProducer.Produce(_topic, 1, new FilterEntity { Id = 1, Name = "self", Value = 1 });

		// Write via a foreign raw producer (no instance header): the consumer must ingest it.
		using (var foreign = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			var entity = new FilterEntity { Id = 2, Name = "foreign", Value = 2 };
			foreign.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(2),
				Value = MessagePackSerializer.Serialize(entity),
				Headers = new Headers()
			});
			foreign.Flush(TimeSpan.FromSeconds(10));
		}

		// Wait for the foreign record to arrive, then assert the self-written one never did.
		await WaitUntil(() => cache.Cache.TryGet(2, out _));
		Assert.That(cache.Cache.TryGet(2, out var foreignEntity), Is.True, "Foreign write should be ingested");
		Assert.That(foreignEntity!.Name, Is.EqualTo("foreign"));
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Self-produced write must be filtered out");

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
}

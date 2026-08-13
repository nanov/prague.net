namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   Pins that stopping the hosted service actually stops consuming. `StopAsync` used to call
///   `KafkaCachesLoader.StartAsync`, which returns the memoised (already completed) loading task —
///   so shutdown reported success while the consume loop kept running.
/// </summary>
[TestFixture]
public class ShutdownTests {
	private const string TopicPrefix = "it-shutdown";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task StopAsync_StopsConsuming() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(cts.Token);
		var cache = sp.GetRequiredService<FilterEntityCache>();

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		// Positive control: the live path must be working, otherwise the assertion below is vacuous.
		Produce(producer, 1, "before-stop");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.True, "Precondition: live consumption must work before stopping");

		await hosted.StopAsync(CancellationToken.None);

		Produce(producer, 2, "after-stop");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(2, out _), 3000);

		Assert.That(cache.Cache.TryGet(2, out _), Is.False,
			"After StopAsync the consume loop must be down — a message produced afterwards must not reach the cache");
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
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

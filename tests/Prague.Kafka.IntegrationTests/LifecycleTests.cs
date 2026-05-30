namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class LifecycleTests {
	private const string TopicPrefix = "it-lifecycle";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task EmptyTopic_LoadsCleanly_AndCacheIsEmpty() {
		var sp = BuildServices().BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.Count, Is.EqualTo(0), "Empty topic should load to an empty cache");

		await hosted.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task PopulatedTopic_LoadsToCompletion() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var i = 1; i <= 10; i++)
				seeder.Produce(_topic, new Message<byte[], byte[]> {
					Key = MessagePackSerializer.Serialize(i),
					Value = MessagePackSerializer.Serialize(new FilterEntity { Id = i, Name = $"e-{i}", Value = i }),
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
		Assert.That(cache.Cache.Count, Is.EqualTo(10), "All seeded records must be present after the loader completes");
		Assert.That(cache.Cache.TryGet(5, out var e5), Is.True);
		Assert.That(e5!.Name, Is.EqualTo("e-5"));

		await hosted.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task HostedService_StartStop_Completes() {
		var sp = BuildServices().BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		// A clean stop must complete without throwing.
		Assert.DoesNotThrowAsync(async () => await hosted.StopAsync(CancellationToken.None));
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
}

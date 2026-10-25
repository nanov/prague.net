namespace Prague.Kafka.IntegrationTests;

using Nanov.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   <c>ForceProduceAll()</c> re-emits every resident record onto the topic, unconditionally. The records
///   are written locally without producing, so the topic starts empty and whatever it holds afterwards came
///   from the forced pass alone. The topic is read back with a raw consumer: a Prague consumer in this
///   process would skip the records as self-produced (one instance id per process).
/// </summary>
[TestFixture]
public class ForceProduceAllTests {
	private const string TopicPrefix = "it-force-produce-all";

	private readonly List<IServiceProvider> _providers = new();
	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[TearDown]
	public async Task TearDownProviders() {
		foreach (var provider in _providers)
			try {
				await provider.GetRequiredService<IHostedService>().StopAsync(CancellationToken.None);
			}
			catch {
				// teardown must not mask the test's own failure
			}
			finally {
				(provider as IDisposable)?.Dispose();
			}

		_providers.Clear();
	}

	[Test]
	public async Task ForceProduceAll_PublishesEveryResidentRecord_AndReturnsTheirCount() {
		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();

		// Resident without ever having been produced: the topic knows nothing about these three.
		for (var id = 1; id <= 3; id++)
			cache.AddOrUpdate(new FilterEntity { Id = id, Name = $"n{id}", Value = id * 10 });

		var produced = cache.ForceProduceAll();

		Assert.That(produced, Is.EqualTo(3), "one record per resident key");

		var onTopic = ReadTopic(expected: 3);
		Assert.That(onTopic.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }), "every resident key was produced exactly once");
		Assert.That(onTopic[2].Value, Is.EqualTo(20), "the produced record is the resident document");

		// A second forced pass is a plain re-emit: nothing changed locally, the records still go out.
		Assert.That(cache.ForceProduceAll(), Is.EqualTo(3));

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	[Test]
	public async Task ForceProduceAll_EmptyCache_ProducesNothing() {
		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.ForceProduceAll(), Is.Zero);
		Assert.That(ReadTopic(expected: 0), Is.Empty);

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	/// <summary>
	///   Reads partition 0 from the beginning to EOF, keyed by document id. The producer has no flush
	///   surface, so the read waits for the expected count (EOF alone could race a delivery still in flight).
	///   Compaction is not on, so a key seen twice is a real double produce.
	/// </summary>
	private Dictionary<int, FilterEntity> ReadTopic(int expected) {
		using var consumer = DualKafkaClusterFixture.NewConsumer(DualKafkaClusterFixture.BootstrapServersA);
		consumer.Assign(new TopicPartitionOffset(_topic, 0, Offset.Beginning));
		var records = new Dictionary<int, FilterEntity>();
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline) {
			var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
			if (result is null)
				continue;
			if (result.IsPartitionEOF) {
				if (records.Count >= expected)
					break;
				continue;
			}
			var key = MessagePackSerializer.Deserialize<int>(result.Message.Key);
			Assert.That(records.ContainsKey(key), Is.False, $"key {key} was produced twice in one pass");
			records[key] = MessagePackSerializer.Deserialize<FilterEntity>(result.Message.Value);
		}

		return records;
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
}

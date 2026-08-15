namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   Pins the load-phase failure contract: a failed <b>cache mutation</b> faults the load instead of
///   being logged and stepped over. Bad input stays skippable (a failed deserialization drops one
///   message and the load continues) — this covers the other half of that split.
/// </summary>
[TestFixture]
public class LoadFailureTests {
	private const string TopicPrefix = "it-load-failure";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task CacheMutationFailureDuringLoad_FaultsTheLoad() {
		using (var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			var entity = new FilterEntity { Id = 1, Name = "one", Value = 1 };
			producer.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(1),
				Value = MessagePackSerializer.Serialize(entity),
				Headers = new Headers()
			});
			producer.Flush(TimeSpan.FromSeconds(10));
		}

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
		services.AddKafkaCaches("KafkaConfig", b => b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));

		using var sp = services.BuildServiceProvider();

		// A secondary index whose selector throws is the only user code on the cache-mutation path,
		// so it is how a mutation failure is provoked without a fake cache.
		sp.GetRequiredService<FilterEntityCache>().Cache
			.AddKeyValueIndex<int>((_, _) => throw new InvalidOperationException("index selector boom"));

		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		// The buffer flush hits AddOrUpdate -> index.Add -> the throwing selector. That must reach
		// ConsumeRawLoop, latch fatal and fault the load — not be swallowed per value, which would
		// bring the cache up with a store/index divergence and no signal to the caller.
		Assert.ThrowsAsync<InvalidOperationException>(async () => await hosted.StartAsync(cts.Token));

		var consumerStats = sp.GetRequiredService<KafkaCachesStatistics>().Consumers.Values.Single();
		Assert.That(consumerStats.IsFatalLatchedUnsafe, Is.True,
			"A failed cache mutation during load must latch fatal so the liveness check reports it");
	}
}

namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Nanov.Kafka;
using Entities;
using Health;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   A cache topic with several partitions loads only when every assigned partition has reached the high
///   watermark it had at assignment. The backlog is deliberately lopsided — one empty partition, one short,
///   one long — because a single shared watermark let the empty partition's EOF declare the whole cache
///   loaded while the long one was still behind, pushing its backlog through the live path.
/// </summary>
[TestFixture]
public class MultiPartitionLoadTests {
	private const string TopicPrefix = "it-multi-partition";
	private const int Partitions = 3;
	private const int LongBacklog = 3000;

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic, Partitions);
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
	public async Task Load_WaitsForEveryPartition_AndAfterHandlersSeeOnlyLiveData() {
		// Partition 0 stays empty, partition 1 gets one record, partition 2 the long backlog.
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, 1, "short");
			for (var id = 1000; id < 1000 + LongBacklog; id++)
				Produce(seeder, 2, id, "long");
			seeder.Flush(TimeSpan.FromSeconds(30));
		}

		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		Assert.That(cache.Cache.Count, Is.EqualTo(1 + LongBacklog), "load returned before every partition was consumed");
		Assert.That(recording.Count, Is.EqualTo(0), "load-phase records went through the live path");

		var statistics = sp.GetRequiredService<KafkaCachesStatistics>();
		var consumerStats = statistics.Consumers.Values.Single();
		Assert.That(consumerStats.CachesLoadingCount, Is.EqualTo(0));
		Assert.That(consumerStats.Caches[_topic].AssignedPartitionCountUnsafe, Is.EqualTo(Partitions));
		Assert.That(consumerStats.Caches[_topic].PartitionCountUnsafe, Is.EqualTo(Partitions));

		// Live writes on every partition, including the one that was empty at load, reach the after-handler.
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var partition = 0; partition < Partitions; partition++)
			Produce(producer, partition, 90 + partition, "live");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => recording.Count >= Partitions);
		Assert.That(recording.Keys, Is.EquivalentTo(new[] { 90, 91, 92 }));
		Assert.That(cache.Cache.Count, Is.EqualTo(1 + LongBacklog + Partitions));
	}

	[Test]
	public async Task Load_AllPartitionsEmpty_Completes() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		Assert.That(cache.Cache.Count, Is.EqualTo(0));
		Assert.That(sp.GetRequiredService<KafkaCachesStatistics>().Consumers.Values.Single().CachesLoadingCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Load_KeyedRecordsAcrossPartitions_LastWritePerKeyWins() {
		// Default keyed partitioning keeps every write for a key on one partition, so per-key order holds
		// across a multi-partition load even though partitions interleave.
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var round = 0; round < 5; round++)
				for (var id = 0; id < 200; id++)
					ProduceKeyed(seeder, id, $"v{round}");
			for (var id = 0; id < 200; id += 2)
				ProduceKeyedTombstone(seeder, id);
			seeder.Flush(TimeSpan.FromSeconds(30));
		}

		var (_, cache) = await StartAsync(new RecordingAfterHandler());

		Assert.That(cache.Cache.Count, Is.EqualTo(100));
		for (var id = 1; id < 200; id += 2) {
			Assert.That(cache.Cache.TryGet(id, out var value), Is.True);
			Assert.That(value!.Name, Is.EqualTo("v4"));
		}
	}

	private void Produce(IProducer<byte[], byte[]> producer, int partition, int id, string name) =>
		producer.Produce(new TopicPartition(_topic, new Partition(partition)), Message(id, name));

	private void ProduceKeyed(IProducer<byte[], byte[]> producer, int id, string name) =>
		producer.Produce(_topic, Message(id, name));

	private void ProduceKeyedTombstone(IProducer<byte[], byte[]> producer, int id) =>
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id), Value = null!, Headers = new Headers()
		});

	private static Message<byte[], byte[]> Message(int id, string name) => new() {
		Key = MessagePackSerializer.Serialize(id),
		Value = MessagePackSerializer.Serialize(new FilterEntity { Id = id, Name = name, Value = id }),
		Headers = new Headers()
	};

	[Test]
	public async Task SharedGroup_InstanceAssignedPartOfTheTopic_DoesNotGoLive() {
		// The load target is the topic's partition count from metadata, not what one assign callback delivered.
		// A shared group.id splits the partitions, so the second instance holds one of two and must stay loading
		// rather than go live on half the topic.
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic + "-shared", 2);
		_topic += "-shared";
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 0, 1, "p0");
			Produce(seeder, 1, 2, "p1");
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var groupId = Guid.NewGuid().ToString();
		var (_, first) = await StartAsync(new RecordingAfterHandler(), groupId);
		Assert.That(first.Cache.Count, Is.EqualTo(2), "the sole member is assigned both partitions");

		await Assert.ThrowsAsync<TimeoutException>(() => StartAsync(new RecordingAfterHandler(), groupId, TimeSpan.FromSeconds(15)));

		var second = _providers[^1];
		var consumerStats = second.GetRequiredService<KafkaCachesStatistics>().Consumers.Values.Single();
		Assert.That(consumerStats.Caches[_topic].AssignedPartitionCountUnsafe, Is.EqualTo(1), "the group split the topic");
		Assert.That(consumerStats.CachesLoadingCount, Is.EqualTo(1));
		Assert.That(KafkaCachesHealthPredicates.HasNoPartitionAssigned(consumerStats.Caches[_topic]), Is.True,
			"readiness must see the missing partition");
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(RecordingAfterHandler recording,
		string? groupId = null, TimeSpan? loadTimeout = null) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
				{ "KafkaConfig:ClientSettings:group.id", groupId ?? Guid.NewGuid().ToString() }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording);
		services.AddKafkaCaches("KafkaConfig", b => b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		// The hosted service is the loader: StartAsync returns once the initial load completes.
		using var cts = new CancellationTokenSource(loadTimeout ?? TimeSpan.FromSeconds(60));
		await sp.GetRequiredService<IHostedService>().StartAsync(cts.Token);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition() && !cts.IsCancellationRequested)
			await Task.Delay(50);
	}

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<int> _keys = new();

		public int Count => _keys.Count;
		public IEnumerable<int> Keys => _keys;

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_keys.Enqueue(key);
			return ValueTask.CompletedTask;
		}
	}
}

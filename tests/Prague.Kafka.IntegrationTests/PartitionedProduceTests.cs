namespace Prague.Kafka.IntegrationTests;

using Nanov.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   Where the generated <c>…AndProduce</c> methods send a write on a multi-partition topic: an update or delete
///   goes to the partition the record is stored on, an explicit partition must match it, and a new key without
///   one is left to the producer's partitioner. The topic is read back with a raw consumer, since a Prague
///   consumer in this process skips its own writes.
/// </summary>
[TestFixture]
public class PartitionedProduceTests {
	private const string TopicPrefix = "it-partitioned-produce";
	private const int Partitions = 3;

	private readonly List<IServiceProvider> _providers = new();
	private string _topic = "";

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
	public async Task Consumed_record_knows_its_partition_and_the_cache_knows_the_count() {
		Seed((2, 7, "seed"));
		var cache = await StartAsync();

		Assert.That(cache.PartitionCount, Is.EqualTo(Partitions));
		Assert.That(cache.Cache.TryGet(7, out var resident), Is.True);
		Assert.That(Metadata(resident!).Partition, Is.EqualTo(2));
	}

	// The stored partition is chosen as one the partitioner would NOT pick for the key, so a write that fell back to
	// the partitioner lands elsewhere and fails the test, instead of passing by coincidence one time in three.

	[Test]
	public async Task Update_without_partition_goes_to_the_stored_partition() {
		var stored = await NotThePartitionersChoiceAsync(7);
		Seed((stored, 7, "seed"));
		var cache = await StartAsync();

		cache.AddOrUpdateAndProduce(new FilterEntity { Id = 7, Name = "updated", Value = 1 });

		var update = ReadTopic(expected: 2).Single(r => r.Name == "updated");
		Assert.That(update.Partition, Is.EqualTo(stored));
	}

	[Test]
	public async Task Explicit_partition_on_a_new_key_is_kept_by_later_writes() {
		var explicitPartition = await NotThePartitionersChoiceAsync(50);
		var cache = await StartAsync();

		cache.AddOrUpdateAndProduce(explicitPartition, new FilterEntity { Id = 50, Name = "created", Value = 1 });
		cache.AddOrUpdateAndProduce(new FilterEntity { Id = 50, Name = "updated", Value = 2 });
		cache.AddOrUpdateAndProduce(explicitPartition, new FilterEntity { Id = 50, Name = "updated", Value = 2 }, force: true);
		cache.RemoveAndProduce(50);

		var records = ReadTopic(expected: 4);
		Assert.That(records.Select(r => r.Partition), Is.All.EqualTo(explicitPartition),
			"every write of the key went to the partition it was created on");
		Assert.That(records.Count(r => r.IsTombstone), Is.EqualTo(1));
	}

	[Test]
	public async Task Delete_without_partition_goes_to_the_stored_partition() {
		var stored = await NotThePartitionersChoiceAsync(7);
		Seed((stored, 7, "seed"));
		var cache = await StartAsync();

		Assert.That(cache.RemoveAndProduce(7), Is.True);

		var tombstone = ReadTopic(expected: 2).Single(r => r.IsTombstone);
		Assert.That(tombstone.Partition, Is.EqualTo(stored));
	}

	[Test]
	public async Task Explicit_partition_differing_from_the_stored_one_throws_before_any_write() {
		Seed((2, 7, "seed"));
		var cache = await StartAsync();

		Assert.Throws<InvalidOperationException>(() =>
			cache.AddOrUpdateAndProduce(0, new FilterEntity { Id = 7, Name = "moved", Value = 1 }));
		Assert.Throws<InvalidOperationException>(() =>
			cache.AddOrUpdateAndProduce(0, new FilterEntity { Id = 7, Name = "moved", Value = 1 }, force: true));
		Assert.Throws<InvalidOperationException>(() => cache.RemoveAndProduce(0, 7));

		Assert.That(cache.Cache.TryGet(7, out var resident), Is.True, "the rejected delete removed nothing");
		Assert.That(resident!.Name, Is.EqualTo("seed"), "the rejected update wrote nothing");
		Assert.That(ReadTopic(expected: 1), Has.Count.EqualTo(1), "nothing was produced");
	}

	[Test]
	public async Task Partition_outside_the_topic_throws_before_any_write() {
		var cache = await StartAsync();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			cache.AddOrUpdateAndProduce(Partitions, new FilterEntity { Id = 9, Name = "x", Value = 1 }));
		Assert.Throws<ArgumentOutOfRangeException>(() => cache.RemoveAndProduce(-2, 9));
		Assert.That(cache.Cache.TryGet(9, out _), Is.False);
	}

	[Test]
	public async Task ForceProduceAll_reproduces_each_record_to_its_stored_partition() {
		Seed((0, 1, "a"), (1, 2, "b"), (2, 3, "c"));
		var cache = await StartAsync();

		Assert.That(cache.ForceProduceAll(), Is.EqualTo(3));

		var records = ReadTopic(expected: 6);
		foreach (var group in records.GroupBy(r => r.Key))
			Assert.That(group.Select(r => r.Partition).Distinct().Count(), Is.EqualTo(1),
				$"key {group.Key} was re-produced to a different partition");
	}

	[Test]
	public async Task OnWrite_mutation_is_what_gets_produced_and_an_unchanged_write_produces_nothing() {
		var cache = await StartAsync();

		cache.AddOrUpdateAndProduce(new FilterEntity { Id = 60, Name = "raw", Value = 1 },
			static (next, resident) => next.Name = resident is null ? "created" : "updated");
		cache.AddOrUpdateAndProduce(new FilterEntity { Id = 60, Name = "raw", Value = 2 }, "stamped",
			static (next, _, name) => next.Name = name);
		cache.AddOrUpdateAndProduce(new FilterEntity { Id = 60, Name = "stamped", Value = 2 },
			static (next, _) => next.Name = "never");

		var records = ReadTopic(expected: 2);
		Assert.That(records.Select(r => r.Name), Is.EqualTo(new[] { "created", "stamped" }),
			"each produced record carries the callback's mutation; the cache-equal third write produced nothing");
		Assert.That(cache.Cache.TryGet(60, out var resident) && resident.Name == "stamped");
	}

	/// <summary>
	///   A partition the producer's partitioner does not choose for <paramref name="id" />: probes the choice with an
	///   unpartitioned produce to a scratch topic of the same partition count, then takes the next partition.
	/// </summary>
	private static async Task<int> NotThePartitionersChoiceAsync(int id) {
		var probeTopic = $"{TopicPrefix}-probe-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, probeTopic, Partitions);
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		var delivery = await producer.ProduceAsync(probeTopic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id), Value = [1]
		});
		return (delivery.Partition.Value + 1) % Partitions;
	}

	private static Core.PersistenceInfo Metadata(FilterEntity value) =>
		((Core.IPragueMetadataSettable)value).__PragueMetadata__;

	private void Seed(params (int Partition, int Id, string Name)[] records) {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		foreach (var (partition, id, name) in records)
			producer.Produce(new TopicPartition(_topic, new Partition(partition)), new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(id),
				Value = MessagePackSerializer.Serialize(new FilterEntity { Id = id, Name = name, Value = id }),
				Headers = new Headers()
			});
		producer.Flush(TimeSpan.FromSeconds(10));
	}

	private sealed record Record(int Partition, int Key, string? Name, bool IsTombstone);

	/// <summary>
	///   Reads every partition from the beginning until <paramref name="expected" /> records arrived. The producer
	///   has no flush surface, so the read waits for the count; a short settle afterwards catches an extra record.
	/// </summary>
	private List<Record> ReadTopic(int expected) {
		using var consumer = DualKafkaClusterFixture.NewConsumer(DualKafkaClusterFixture.BootstrapServersA);
		var assignment = new List<TopicPartitionOffset>();
		for (var p = 0; p < Partitions; p++)
			assignment.Add(new TopicPartitionOffset(_topic, p, Offset.Beginning));
		consumer.Assign(assignment);

		var records = new List<Record>();
		var deadline = DateTime.UtcNow.AddSeconds(15);
		var settle = DateTime.MaxValue;
		while (DateTime.UtcNow < deadline && DateTime.UtcNow < settle) {
			var result = consumer.Consume(TimeSpan.FromMilliseconds(200));
			if (result is null || result.IsPartitionEOF)
				continue;
			var key = MessagePackSerializer.Deserialize<int>(result.Message.Key);
			var tombstone = result.Message.Value is null || result.Message.Value.Length == 0;
			var name = tombstone ? null : MessagePackSerializer.Deserialize<FilterEntity>(result.Message.Value).Name;
			records.Add(new Record(result.Partition.Value, key, name, tombstone));
			if (records.Count >= expected && settle == DateTime.MaxValue)
				settle = DateTime.UtcNow.AddSeconds(1);
		}

		return records;
	}

	private async Task<FilterEntityCache> StartAsync() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
				{ "KafkaConfig:ClientSettings:group.id", Guid.NewGuid().ToString() }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		await sp.GetRequiredService<IHostedService>().StartAsync(cts.Token);
		return sp.GetRequiredService<FilterEntityCache>();
	}
}

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

	private readonly List<IServiceProvider> _providers = new();

	/// <summary>
	///   Runs whatever the test did. A failing test never reaches its own StopAsync call, and a consumer
	///   left alive stays a member of the group — from then on every later test's join has to rebalance
	///   around a zombie, which is what makes initial loads stall.
	/// </summary>
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
	public async Task OwnProducerWrites_AreFilteredOut_ButForeignWritesAreIngested() {
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

		using var sp = services.BuildServiceProvider();
		_providers.Add(sp);
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
		(sp as IDisposable)?.Dispose();
	}

	/// <summary>
	///   The self-filter is absolute — a tombstone does not buy an exemption from it. Only the
	///   "a required header never appeared" reason is waived for a delete; being our own write is a fact about
	///   who produced the message, and a producer must not re-consume its own writes whatever their shape.
	/// </summary>
	[Test]
	public async Task OwnProducerDelete_IsStillFilteredOut() {
		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();

		using var foreign = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		ProduceForeign(foreign, 1, "present");
		foreign.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.True,
			"Precondition: the key must be cached before the self-produced delete");

		// A real tombstone, carrying this process's instance id and nothing else.
		var pragueProducer = sp.GetRequiredKeyedService<KafkaCacheProducer>("KafkaConfig");
		pragueProducer.Delete(_topic, 1);

		ProduceForeign(foreign, 2, "sentinel");
		foreign.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(2, out _));

		// The sentinel goes through a different producer, so it orders the consumer's work but not the two
		// producers' writes against each other. The settle window is what makes a late self-delete visible;
		// without it a delete still in flight would leave the assertion below passing for the wrong reason.
		await Task.Delay(1000);
		Assert.That(cache.Cache.TryGet(1, out var kept), Is.True, "A self-produced delete must still be self-filtered");
		Assert.That(kept!.Name, Is.EqualTo("present"));

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	private void ProduceForeign(IProducer<byte[], byte[]> producer, int id, string name) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	/// <summary>
	///   Used only by <see cref="OwnProducerDelete_IsStillFilteredOut" /> — the test above builds its own
	///   collection inline and is left exactly as it was.
	/// </summary>
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

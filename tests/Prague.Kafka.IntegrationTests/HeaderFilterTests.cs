namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class HeaderFilterTests {
	private const string TopicPrefix = "it-header-filter";

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
	public async Task WithHeaderEqualsFilter_String_AcceptsMatch_RejectsNonMatch() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "accepted", new Headers { { "event-type", "user-created"u8.ToArray() } });
		Produce(producer, 2, "rejected", new Headers { { "event-type", "user-updated"u8.ToArray() } });
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("event-type", "user-created"));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True, "Matching header should be admitted");
		Assert.That(cache.Cache.TryGet(2, out _), Is.False, "Non-matching header should be filtered");
	}

	[Test]
	public async Task WithHeaderEqualsFilter_String_MultiValue_AcceptsAnyOf() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "a", new Headers { { "event-type", "created"u8.ToArray() } });
		Produce(producer, 2, "b", new Headers { { "event-type", "updated"u8.ToArray() } });
		Produce(producer, 3, "c", new Headers { { "event-type", "deleted"u8.ToArray() } });
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("event-type", "created", "updated"));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True);
		Assert.That(cache.Cache.TryGet(2, out _), Is.True);
		Assert.That(cache.Cache.TryGet(3, out _), Is.False, "Value outside the OR-set should be filtered");
	}

	[Test]
	public async Task WithHeaderEqualsFilter_Int_AcceptsMatch_RejectsNonMatch() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "match", new Headers { { "region", MessagePackSerializer.Serialize(7) } });
		Produce(producer, 2, "miss", new Headers { { "region", MessagePackSerializer.Serialize(9) } });
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("region", 7));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True);
		Assert.That(cache.Cache.TryGet(2, out _), Is.False);
	}

	[Test]
	public async Task WithHeaderEqualsFilter_Long_AcceptsMatch_RejectsNonMatch() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "match", new Headers { { "epoch", MessagePackSerializer.Serialize(9876543210L) } });
		Produce(producer, 2, "miss", new Headers { { "epoch", MessagePackSerializer.Serialize(1L) } });
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("epoch", 9876543210L));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True);
		Assert.That(cache.Cache.TryGet(2, out _), Is.False);
	}

	[Test]
	public async Task WithHeaderEqualsFilter_Int_MultiValue_AcceptsAnyOf() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "a", new Headers { { "region", MessagePackSerializer.Serialize(1) } });
		Produce(producer, 2, "b", new Headers { { "region", MessagePackSerializer.Serialize(2) } });
		Produce(producer, 3, "c", new Headers { { "region", MessagePackSerializer.Serialize(3) } });
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("region", 1, 2));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True);
		Assert.That(cache.Cache.TryGet(2, out _), Is.True);
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, Headers headers) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = headers
		});
	}

	private async Task<FilterEntityCache> LoadAsync(
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>> configure) {
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
			configure(b.AddCache<FilterEntityCache, int, FilterEntity>(_topic));
		});

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return sp.GetRequiredService<FilterEntityCache>();
	}
}

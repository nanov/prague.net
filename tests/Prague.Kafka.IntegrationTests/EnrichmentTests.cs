namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class EnrichmentTests {
	private const string TopicPrefix = "it-enrichment";

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
	public async Task HeaderProperties_RoundTrip_ViaCodegenDericherAndEnricher() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new HeaderEnrichEntity {
			Id = 1,
			Name = "first",
			TenantId = 12345,
			EventType = "UserCreated",
			CustomValue = "ABC123"
		};
		var headers = new Headers();
		HeaderEnrichEntity.Derich(sent, headers);

		// Sanity: Derich emits the string header under its custom name.
		Assert.That(headers.Any(h => h.Key == "custom-header"), Is.True,
			"Custom-named header property must be deriched under its attribute name");

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = headers
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync();

		Assert.That(cache.Cache.TryGet(1, out var received), Is.True);
		Assert.That(received!.TenantId, Is.EqualTo(12345), "int header round-trip");
		Assert.That(received.EventType, Is.EqualTo("UserCreated"), "string header round-trip");
		Assert.That(received.CustomValue, Is.EqualTo("ABC123"), "custom-named string header round-trip");
	}

	[Test]
	public async Task FromTimestamp_IsPopulatedFromKafkaMessageTimestamp() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new HeaderEnrichEntity { Id = 2, Name = "ts", EventType = "X" };
		var headers = new Headers();
		HeaderEnrichEntity.Derich(sent, headers);

		var expectedMs = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = headers,
			Timestamp = new Timestamp(expectedMs, TimestampType.CreateTime)
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync();

		Assert.That(cache.Cache.TryGet(2, out var received), Is.True);
		Assert.That(received!.CreatedAt, Is.EqualTo(expectedMs),
			"[DataCacheFromTimestamp] must be populated from the Kafka message timestamp");
	}

	private async Task<HeaderEnrichEntityCache> LoadAsync() {
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
			b.AddCache<HeaderEnrichEntityCache, int, HeaderEnrichEntity>(_topic);
		});

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return sp.GetRequiredService<HeaderEnrichEntityCache>();
	}
}

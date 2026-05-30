namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class NumericHeaderRoundtripTests {
	private const string TopicPrefix = "integration-tests-numeric-headers";

	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}".Substring(0, 50);
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	[Test]
	public async Task IntAndLongHeaderProperties_RoundTrip_ViaCodegenDericherAndEnricher() {
		// Produce: build entity, use codegen Derich(...) to populate headers, then write to real Kafka.
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new EventWithNumericHeaders {
			Id = 1,
			Name = "first",
			TenantId = 12345,
			EventTimestamp = long.MaxValue
		};
		var headers = new Headers();
		EventWithNumericHeaders.Derich(sent, headers);

		// Sanity: Derich should write MessagePack, not raw BitConverter, for both int and long.
		var tenantIdHeader = headers.FirstOrDefault(h => h.Key == "TenantId");
		Assert.That(tenantIdHeader, Is.Not.Null);
		Assert.That(tenantIdHeader!.GetValueBytes(), Is.Not.EqualTo(BitConverter.GetBytes(12345)),
			"Dericher must emit MessagePack, not raw BitConverter");
		var eventTimestampHeader = headers.FirstOrDefault(h => h.Key == "EventTimestamp");
		Assert.That(eventTimestampHeader, Is.Not.Null);
		Assert.That(eventTimestampHeader!.GetValueBytes(), Is.Not.EqualTo(BitConverter.GetBytes(long.MaxValue)),
			"Dericher must emit MessagePack for long, not raw BitConverter");

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = headers
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		// Consume via the cache.
		var (sp, hosted) = BuildServiceProvider();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EventWithNumericHeadersCache>();
		Assert.That(cache.Cache.TryGet(1, out var received), Is.True, "Entity should be in the cache after initial load");
		Assert.That(received!.TenantId, Is.EqualTo(12345), "MessagePack-encoded int header round-trip");
		Assert.That(received.EventTimestamp, Is.EqualTo(long.MaxValue), "MessagePack-encoded long header round-trip");

		await hosted.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task LegacyRawHeaderBytes_StillDecodeViaFallback() {
		// Build raw headers the old way (BitConverter) and verify the Enricher's legacy fallback still reads them.
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new EventWithNumericHeaders { Id = 2, Name = "legacy", TenantId = 999, EventTimestamp = 9876543210L };
		var legacyHeaders = new Headers {
			{ "TenantId", BitConverter.GetBytes(999) },
			{ "EventTimestamp", BitConverter.GetBytes(9876543210L) }
		};

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = legacyHeaders
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, hosted) = BuildServiceProvider();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EventWithNumericHeadersCache>();
		Assert.That(cache.Cache.TryGet(2, out var received), Is.True);
		Assert.That(received!.TenantId, Is.EqualTo(999), "Legacy raw 4-byte int header must decode via fallback");
		Assert.That(received.EventTimestamp, Is.EqualTo(9876543210L), "Legacy raw 8-byte long header must decode via fallback");

		await hosted.StopAsync(CancellationToken.None);
	}

	private (IServiceProvider sp, IHostedService hosted) BuildServiceProvider() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<EventWithNumericHeadersCache, int, EventWithNumericHeaders>(_topic);
		});

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		return (sp, hosted);
	}
}

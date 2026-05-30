namespace Prague.Kafka.IntegrationTests;

using Prague.Kafka;
using Prague.Kafka.SerDe;
using Confluent.Kafka;
using Entities;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

[TestFixture]
public class MessagePackIsolationTests {
	private const string TopicPrefix = "integration-tests-mp-isolation";
	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}".Substring(0, 50);
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
		PragueMessagePack.ResetForTests();
	}

	[TearDown]
	public void ResetAfter() => PragueMessagePack.ResetForTests();

	[Test]
	public async Task EntityWithDateTime_RoundTrips_NativeIntFormat() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new EntityWithDateTime {
			Id = 1,
			Name = "native",
			CreatedAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc),
			UpdatedAt = new DateTime(2026, 5, 17, 12, 30, 0, DateTimeKind.Utc),
			CorrelationId = Guid.NewGuid()
		};

		var bytes = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options);

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
			Value = bytes
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, hosted) = BuildServiceProvider();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
		Assert.That(cache.Cache.TryGet(1, out var received), Is.True);
		Assert.That(received!.CreatedAt, Is.EqualTo(sent.CreatedAt));
		Assert.That(received.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
		Assert.That(received.UpdatedAt, Is.EqualTo(sent.UpdatedAt));
		Assert.That(received.CorrelationId, Is.EqualTo(sent.CorrelationId));

		await hosted.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task HostMutatedDefaultOptions_DoesNotBreakProduction() {
		// Simulate the host snippet that historically broke things.
		var original = MessagePackSerializer.DefaultOptions;
		MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
			.WithResolver(CompositeResolver.Create(
				TypelessContractlessStandardResolver.Instance,
				StandardResolver.Instance));

		try {
			using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
			var sent = new EntityWithDateTime {
				Id = 2,
				Name = "mutated-default",
				CreatedAt = DateTime.UtcNow,
				CorrelationId = Guid.NewGuid()
			};

			producer.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
				Value = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options)
			});
			producer.Flush(TimeSpan.FromSeconds(10));

			var (sp, hosted) = BuildServiceProvider();
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			await hosted.StartAsync(cts.Token);
			var loader = sp.GetRequiredService<KafkaCachesLoader>();
			await loader.StartAsync(cts.Token);

			var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
			Assert.That(cache.Cache.TryGet(2, out var received), Is.True,
				"Cache must work even when DefaultOptions has been mutated by host");
			Assert.That(received!.CreatedAt, Is.EqualTo(sent.CreatedAt).Within(TimeSpan.FromMilliseconds(1)));

			await hosted.StopAsync(CancellationToken.None);
		} finally {
			MessagePackSerializer.DefaultOptions = original;
		}
	}

	[Test]
	public async Task LegacyTopicData_StandardTimestampExt_StillDecodes() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var instantUtc = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

		// Use plain StandardResolver to force DateTime → standard ext format on the wire.
		var pureStandard = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

		var sent = new EntityWithDateTime {
			Id = 3,
			Name = "legacy-ext",
			CreatedAt = instantUtc,
			CorrelationId = Guid.NewGuid()
		};
		var bytesWithExtDate = MessagePackSerializer.Serialize(sent, pureStandard);

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
			Value = bytesWithExtDate
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, hosted) = BuildServiceProvider();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
		Assert.That(cache.Cache.TryGet(3, out var received), Is.True);
		Assert.That(received!.CreatedAt.ToUniversalTime(), Is.EqualTo(instantUtc));

		await hosted.StopAsync(CancellationToken.None);
	}

	private (IServiceProvider sp, IHostedService hosted) BuildServiceProvider() {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			}).Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<EntityWithDateTimeCache, int, EntityWithDateTime>(_topic);
		});

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		return (sp, hosted);
	}
}

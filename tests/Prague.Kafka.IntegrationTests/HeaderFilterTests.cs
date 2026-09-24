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

	/// <summary>
	///   Baseline for <c>WithHeaderExistsFilter</c>, which had no coverage at all: a message carrying the
	///   required header is admitted, one without it is dropped. The tombstone waiver below is an exception
	///   carved out of exactly this rule, so the rule itself has to be pinned first.
	/// </summary>
	[Test]
	public async Task WithHeaderExistsFilter_AdmitsMessagesCarryingTheHeader_DropsThoseWithout() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		Produce(producer, 1, "has-header", new Headers { { "tenant", "A"u8.ToArray() } });
		Produce(producer, 2, "no-header", new Headers());
		producer.Flush(TimeSpan.FromSeconds(10));

		var cache = await LoadAsync(b => b
			.WithHeaderExistsFilter("tenant"));

		Assert.That(cache.Cache.TryGet(1, out _), Is.True, "A message carrying the required header is admitted");
		Assert.That(cache.Cache.TryGet(2, out _), Is.False, "A message without the required header is dropped");
	}

	/// <summary>
	///   A delete carries no user headers — <c>KafkaCacheProducer.Delete</c> stamps only the producer instance id —
	///   so a required header can never be satisfied by one. Requiring it anyway pinned the key permanently, against
	///   Prague's own producer included. The gate therefore waives <c>MissingRequiredHeader</c>, and only that reason,
	///   for a tombstone.
	/// </summary>
	[Test]
	public async Task WithHeaderExistsFilter_HeaderlessTombstone_StillRemovesTheKey_DuringInitialLoad() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, "present", new Headers { { "tenant", "A"u8.ToArray() } });
			ProduceTombstone(seeder, 1, new Headers());
			Produce(seeder, 2, "after", new Headers { { "tenant", "A"u8.ToArray() } });
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var cache = await LoadAsync(b => b
			.WithHeaderExistsFilter("tenant"));

		Assert.That(cache.Cache.TryGet(2, out _), Is.True,
			"The record after the tombstone must be loaded — otherwise the log was never read that far");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "A headerless tombstone must still remove the key");
	}

	/// <summary>Live-phase twin of the case above — the gate runs before either phase's dispatch.</summary>
	[Test]
	public async Task WithHeaderExistsFilter_HeaderlessTombstone_StillRemovesTheKey_LivePhase() {
		var cache = await LoadAsync(b => b
			.WithHeaderExistsFilter("tenant"));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "present", new Headers { { "tenant", "A"u8.ToArray() } });
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.True, "Precondition: the key must be cached before the tombstone");

		// No user headers at all — the shape KafkaCacheProducer.Delete puts on the wire. Prague's own producer
		// cannot stand in here: KafkaCaches.InstanceId is process-wide, so this consumer would self-filter its
		// delete and the test would pass for the wrong reason.
		ProduceTombstone(producer, 1, new Headers());
		Produce(producer, 999, "sentinel", new Headers { { "tenant", "A"u8.ToArray() } });
		producer.Flush(TimeSpan.FromSeconds(10));

		// One partition and one FIFO live worker, so the sentinel landing proves the tombstone ahead of it has
		// already been applied.
		await WaitUntil(() => cache.Cache.TryGet(999, out _));
		Assert.That(cache.Cache.TryGet(999, out _), Is.True,
			"Sentinel must arrive — without it nothing proves the tombstone was processed");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "A headerless tombstone must still remove the key");
	}

	/// <summary>
	///   Cross-stream guard: an <i>explicit</i> header rejection still drops a tombstone. Selecting a sub-stream of a
	///   shared topic is a judgement about the message's content, not its shape, so it is not waived — otherwise any
	///   producer could evict another stream's key.
	///   <para>
	///     The rejecting header must be present and non-matching. An equals filter never runs for an <i>absent</i>
	///     header, so a tombstone with no headers at all is admitted by one — writing this case with an empty
	///     <c>Headers</c> would make it vacuous.
	///   </para>
	/// </summary>
	[Test]
	public async Task WithHeaderEqualsFilter_ExplicitlyRejectedTombstone_DoesNotRemoveTheKey_DuringInitialLoad() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, "present", new Headers { { "tenant", "A"u8.ToArray() } });
			ProduceTombstone(seeder, 1, new Headers { { "tenant", "B"u8.ToArray() } });
			Produce(seeder, 2, "after", new Headers { { "tenant", "A"u8.ToArray() } });
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var cache = await LoadAsync(b => b
			.WithHeaderEqualsFilter("tenant", "A"));

		Assert.That(cache.Cache.TryGet(2, out _), Is.True,
			"The record after the tombstone must be loaded — otherwise the log was never read that far");
		Assert.That(cache.Cache.TryGet(1, out var kept), Is.True,
			"A tombstone from another stream must not evict this stream's key");
		Assert.That(kept!.Name, Is.EqualTo("present"));
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, Headers headers) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = headers
		});
	}

	private void ProduceTombstone(IProducer<byte[], byte[]> producer, int id, Headers headers) {
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = null!,
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

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition()) {
			if (cts.IsCancellationRequested)
				return;
			await Task.Delay(50);
		}
	}
}

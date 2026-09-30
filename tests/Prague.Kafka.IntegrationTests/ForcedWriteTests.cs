namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using IO;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
///   A forced write — the <c>X-Prague-Force</c> header — is applied unconditionally: the resident value is
///   replaced even when cache-equal, in both phases, and after-handlers see <c>Update</c>, never <c>Same</c>.
///   It does not override ingress filters. Prague's own producer stamps the header when asked to.
/// </summary>
[TestFixture]
public class ForcedWriteTests {
	private const string TopicPrefix = "it-forced-write";
	private const string ForceHeaderName = "X-Prague-Force";
	private const int Key = 1;
	private const int SpacerFirstKey = 100;

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

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
	public async Task LivePhase_ForcedEqualValue_IsAppliedAsUpdate() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, Key, "same", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == Key));

		// Control: the identical payload, unforced, is Same.
		Produce(producer, Key, "same", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == Key));
		Assert.That(cache.Cache.TryGet(Key, out var beforeForce), Is.True);

		Produce(producer, Key, "same", forced: true);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == Key));

		var update = recording.Invocations.LastOrDefault(i => i.UpdateType == UpdateType.Update && i.Key == Key);
		Assert.That(update, Is.Not.Null, "a forced equal value must surface as Update, not Same");
		Assert.Multiple(() => {
			Assert.That(update!.NewValue!.Name, Is.EqualTo("same"));
			Assert.That(update.OldValue!.Name, Is.EqualTo("same"));
			Assert.That(update.OldValue, Is.SameAs(beforeForce), "old is the previously resident instance");
			Assert.That(update.NewValue.__PragueMetadata__.Offset, Is.GreaterThan(update.OldValue.__PragueMetadata__.Offset),
				"the resident object now carries the forced record's offset");
		});
		Assert.That(cache.Cache.TryGet(Key, out var afterForce), Is.True);
		Assert.That(afterForce, Is.SameAs(update!.NewValue), "the store swapped the reference");

		await StopAsync(sp);
	}

	/// <summary>
	///   Load/live parity across the compacting-buffer flush: the first value for the key is in the cache,
	///   not merely buffered, when the repeat arrives. An unforced repeat leaves the earlier record resident;
	///   a forced one replaces it — visible through the persisted offset, since the payloads are identical.
	/// </summary>
	[TestCase(false)]
	[TestCase(true)]
	public async Task LoadPhase_EqualRepeat_ReplacesOnlyWhenForced(bool forced) {
		var lastSpacer = SpacerFirstKey + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		long firstOffset, repeatOffset;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			firstOffset = (await ProduceAsync(seeder, Key, "same", forced: false)).Offset.Value;
			for (var i = SpacerFirstKey; i <= lastSpacer; i++)
				Produce(seeder, i, $"spacer-{i}", forced: false);
			repeatOffset = (await ProduceAsync(seeder, Key, "same", forced)).Offset.Value;
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);

		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True,
			"Spacer keys must be loaded — otherwise the flush boundary was never crossed and the case is vacuous");
		Assert.That(cache.Cache.TryGet(Key, out var resident), Is.True);
		Assert.Multiple(() => {
			Assert.That(resident!.Name, Is.EqualTo("same"));
			Assert.That(resident.__PragueMetadata__.Offset, Is.EqualTo(forced ? repeatOffset : firstOffset));
			Assert.That(recording.Invocations, Is.Empty, "the initial load never fires after-handlers, forced or not");
		});

		await StopAsync(sp);
	}

	[Test]
	public async Task ForcedWrite_DoesNotOverrideAValueFilter() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording,
			static handler => handler.WithValueFilter(static v => !v.Name.StartsWith("reject", StringComparison.Ordinal), treatAsDelete: true));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, Key, "keep", forced: false);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == Key));

		Produce(producer, Key, "reject-me", forced: true);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Delete && i.Key == Key));

		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Delete && i.Key == Key), Is.True,
			"treatAsDelete must win over the force marker");
		Assert.That(cache.Cache.TryGet(Key, out _), Is.False);

		await StopAsync(sp);
	}

	/// <summary>
	///   Prague's own producer stamps the marker when asked, and its own consumer still drops the record: the
	///   self-filter runs before the marker is read, so a forced write never re-applies on the process that wrote
	///   it. An in-process A→B pair cannot be tested — <c>KafkaCaches.InstanceId</c> is static per process, so B
	///   would self-filter A's record too; the wire header plus the foreign-producer tests above cover B's side.
	/// </summary>
	[Test]
	public async Task PragueProducer_Force_StampsTheHeader_AndIsStillSelfFiltered() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(recording);
		var pragueProducer = sp.GetRequiredKeyedService<KafkaCacheProducer>("KafkaConfig");

		pragueProducer.Produce(_topic, 1, new FilterEntity { Id = 1, Name = "plain", Value = 1 });
		pragueProducer.Produce(_topic, 2, new FilterEntity { Id = 2, Name = "forced", Value = 2 }, force: true);

		using var consumer = DualKafkaClusterFixture.NewConsumer(DualKafkaClusterFixture.BootstrapServersA);
		consumer.Subscribe(_topic);
		var records = new Dictionary<int, Headers>();
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (records.Count < 2 && DateTime.UtcNow < deadline) {
			var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
			if (result is null || result.IsPartitionEOF)
				continue;
			records[MessagePackSerializer.Deserialize<int>(result.Message.Key)] = result.Message.Headers;
		}

		Assert.That(records, Has.Count.EqualTo(2), "both records must reach the topic");
		Assert.Multiple(() => {
			Assert.That(records[1].TryGetLastBytes(ForceHeaderName, out _), Is.False, "an unforced produce carries no marker");
			Assert.That(records[2].TryGetLastBytes(ForceHeaderName, out var value), Is.True, "a forced produce carries the marker");
			Assert.That(value, Is.EqualTo("1"u8.ToArray()));
		});

		// Both records have landed (the raw consumer read them), so the settle only covers the Prague consumer's
		// own poll. A self-produced record reaches the after-handler as Filtered at most — never as Add/Update.
		await Task.Delay(1000);
		Assert.Multiple(() => {
			Assert.That(cache.Cache.TryGet(1, out _), Is.False, "a self-produced write must be self-filtered");
			Assert.That(cache.Cache.TryGet(2, out _), Is.False, "a self-produced forced write must be self-filtered too");
			Assert.That(recording.Invocations.All(i => i.UpdateType == UpdateType.Filtered), Is.True,
				"the marker must not let a self-produced record through as Add or Update");
		});

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, bool forced) {
		producer.Produce(_topic, Message(id, name, forced));
	}

	private Task<DeliveryResult<byte[], byte[]>> ProduceAsync(IProducer<byte[], byte[]> producer, int id, string name, bool forced) {
		return producer.ProduceAsync(_topic, Message(id, name, forced));
	}

	private static Message<byte[], byte[]> Message(int id, string name, bool forced) {
		var headers = new Headers();
		if (forced)
			headers.Add(ForceHeaderName, "1"u8.ToArray());
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		return new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = headers
		};
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(
		RecordingAfterHandler recording,
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>>? configure = null) {
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
		services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording);
		services.AddKafkaCaches("KafkaConfig", b => {
			var handler = b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
			configure?.Invoke(handler);
		});

		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private static async Task StopAsync(IServiceProvider sp) {
		var hosted = sp.GetRequiredService<IHostedService>();
		await hosted.StopAsync(CancellationToken.None);
		// Every cache in the process shares one group.id (KafkaCaches.InstanceId is static), so a consumer
		// left alive here stays a group member: it delays the rebalance the next test's join triggers, and
		// that test then waits on an initial load that cannot complete. Disposing cancels the consume loop,
		// whose finally closes the consumer and leaves the group.
		(sp as IDisposable)?.Dispose();
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000) {
		using var cts = new CancellationTokenSource(timeoutMs);
		while (!condition()) {
			if (cts.IsCancellationRequested)
				return;
			await Task.Delay(50);
		}
	}

	private sealed record Invocation(UpdateType UpdateType, int Key, FilterEntity? NewValue, FilterEntity? OldValue);

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<Invocation> _invocations = new();

		public IReadOnlyCollection<Invocation> Invocations => _invocations;

		public bool Any(Func<Invocation, bool> predicate) => _invocations.Any(predicate);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_invocations.Enqueue(new Invocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}
}

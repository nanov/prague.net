namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prague.Kafka.IO;

[TestFixture]
public class KeyFilterTests {
	private const string TopicPrefix = "it-key-filter";

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
	public async Task WithKeyFilter_RejectsKeysFailingPredicate_DuringInitialLoad() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var i = 1; i <= 5; i++)
			Produce(producer, i, $"entity-{i}");
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, cache) = await StartAsync(b => b.WithKeyFilter(k => k > 2));

		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.False);
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);
		Assert.That(cache.Cache.TryGet(4, out _), Is.True);
		Assert.That(cache.Cache.TryGet(5, out _), Is.True);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_MultiplePredicates_ComposeWithAnd_DuringInitialLoad() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		for (var i = 1; i <= 5; i++)
			Produce(producer, i, $"entity-{i}");
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, cache) = await StartAsync(b => b
			.WithKeyFilter(k => k > 1)
			.WithKeyFilter(k => k < 4));

		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(cache.Cache.TryGet(2, out _), Is.True);
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);
		Assert.That(cache.Cache.TryGet(4, out _), Is.False);
		Assert.That(cache.Cache.TryGet(5, out _), Is.False);

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_LivePhase_AcceptedPresent_RejectedAbsent_AndFiresFiltered() {
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => k > 2),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "rejected");
		Produce(producer, 3, "accepted");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(3, out _));

		Assert.That(cache.Cache.TryGet(3, out _), Is.True, "Accepted key should be present");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Rejected key should be absent");
		await WaitUntil(() => recording.Count(UpdateType.Filtered) >= 1);
		Assert.That(recording.Count(UpdateType.Filtered), Is.GreaterThanOrEqualTo(1));
		Assert.That(recording.Count(UpdateType.Add), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_TreatAsDelete_RemovesExistingKey_AndFiresDelete_LivePhase() {
		var recording = new RecordingAfterHandler();
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => allowed.ContainsKey(k), treatAsDelete: true),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		// Admitted while key 3 is allowed.
		Produce(producer, 3, "present");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.True);

		// Key 3 is offboarded; the next message for it is rejected -> tombstone.
		allowed.TryRemove(3, out _);
		Produce(producer, 3, "now-rejected");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => !cache.Cache.TryGet(3, out _));
		Assert.That(cache.Cache.TryGet(3, out _), Is.False);
		await WaitUntil(() => recording.Count(UpdateType.Delete) >= 1);
		Assert.That(recording.Count(UpdateType.Delete), Is.GreaterThanOrEqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_DefaultFalse_KeepsExistingValue_OnLiveRejection() {
		var allowed = new ConcurrentDictionary<int, byte>();
		allowed[3] = 0;

		var (sp, cache) = await StartAsync(b => b.WithKeyFilter(k => allowed.ContainsKey(k)));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 3, "present");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(3, out _));

		// Rejected, but without treatAsDelete the message is dropped and the cached value remains.
		allowed.TryRemove(3, out _);
		Produce(producer, 3, "now-rejected");
		producer.Flush(TimeSpan.FromSeconds(10));

		// Give the rejected message time to be processed (and ignored).
		await Task.Delay(1000);
		Assert.That(cache.Cache.TryGet(3, out var present), Is.True);
		Assert.That(present!.Name, Is.EqualTo("present"));

		await StopAsync(sp);
	}

	[Test]
	public async Task WithKeyFilter_TreatAsDelete_AfterBufferFlush_RemovesKey_DuringInitialLoad() {
		var lastSpacer = 100 + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, "present");

			// Enough values for other keys to cross a compacting-buffer flush, so key 1 is already in the cache.
			for (var i = 100; i <= lastSpacer; i++)
				Produce(seeder, i, $"spacer-{i}");

			Produce(seeder, 1, "now-rejected");
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		// Key 1 is admitted on its first message and offboarded before its second one.
		var seenKeyOne = 0;
		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => k != 1 || Interlocked.Increment(ref seenKeyOne) == 1, treatAsDelete: true));

		// Guards the predicate above: a re-read would admit/reject the wrong message and make the test vacuous.
		Assert.That(seenKeyOne, Is.EqualTo(2), "Key 1 must be filtered exactly twice");
		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True, "Spacer keys must be loaded");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False, "Key-filter delete after a buffer flush during load -> absent");

		await StopAsync(sp);
	}

	/// <summary>
	///   A key filter that rejects with the default <c>treatAsDelete: false</c> must not swallow a tombstone.
	///   The empty-value check sits above the key gate, so the predicate never judges a delete at all: the log's
	///   statement that the key is gone outranks a predicate that can only say "I am not interested in this key".
	///   Suppressing it pinned an entry that not even the producer could remove.
	/// </summary>
	[Test]
	public async Task WithKeyFilter_Skip_DoesNotSwallowTombstone_LivePhase() {
		const int key = 3;
		const int sentinelKey = 4;
		var seenKey = 0;
		var recording = new RecordingAfterHandler();

		// Admits `key` on its first message only; every other key always passes. A tombstone that reached the
		// predicate would be its second evaluation — and would be rejected.
		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => k != key || Interlocked.Increment(ref seenKey) == 1),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, key, "present");
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => cache.Cache.TryGet(key, out _));
		Assert.That(cache.Cache.TryGet(key, out _), Is.True, "Precondition: the key must be cached before the tombstone");

		ProduceTombstone(producer, key);
		Produce(producer, sentinelKey, "sentinel");
		producer.Flush(TimeSpan.FromSeconds(10));

		// One partition and one FIFO live worker, whose items are processed one at a time with each item's
		// after-handlers awaited before the next is dispatched. So the sentinel being in the cache proves the
		// tombstone ahead of it was fully applied — after-handlers included, which is what the counts below rest on.
		await WaitUntil(() => cache.Cache.TryGet(sentinelKey, out _));
		Assert.That(cache.Cache.TryGet(sentinelKey, out _), Is.True,
			"Sentinel must arrive — without it nothing proves the tombstone was processed");

		Assert.That(cache.Cache.TryGet(key, out _), Is.False, "A rejecting key filter must not swallow a tombstone");
		Assert.That(seenKey, Is.EqualTo(1), "The key predicate must not be invoked for a tombstone");
		Assert.That(recording.Count(UpdateType.Delete), Is.GreaterThanOrEqualTo(1), "The tombstone must fire Delete");
		Assert.That(recording.Count(UpdateType.Filtered), Is.Zero, "A tombstone is never a filtered message");

		await StopAsync(sp);
	}

	/// <summary>
	///   Load-phase twin of the case above, across a compacting-buffer flush so the superseded value is really
	///   in the cache rather than still pending in the buffer. The two phases are separate branches of
	///   <c>DispatchRaw</c>; before the hoist they each carried their own copy of this decision.
	/// </summary>
	[Test]
	public async Task WithKeyFilter_Skip_DoesNotSwallowTombstone_AfterBufferFlush_DuringInitialLoad() {
		var lastSpacer = 100 + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			Produce(seeder, 1, "present");

			// Enough values for other keys to cross a compacting-buffer flush, so key 1 is already in the cache.
			for (var i = 100; i <= lastSpacer; i++)
				Produce(seeder, i, $"spacer-{i}");

			ProduceTombstone(seeder, 1);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		// Key 1 is admitted on its first message; a second evaluation would reject it.
		var seenKeyOne = 0;
		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => k != 1 || Interlocked.Increment(ref seenKeyOne) == 1));

		Assert.That(seenKeyOne, Is.EqualTo(1), "The key predicate must not be invoked for a tombstone");
		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True, "Spacer keys must be loaded");
		Assert.That(cache.Cache.TryGet(1, out _), Is.False,
			"A rejecting key filter must not swallow a tombstone during load");

		await StopAsync(sp);
	}

	/// <summary>
	///   A tombstone for a key the filter never admitted is a total no-op: the key is not in the cache, so there
	///   is nothing to delete — and it is not a <c>Filtered</c> message either, because the key gate never judged it.
	/// </summary>
	[Test]
	public async Task WithKeyFilter_TombstoneForNeverAdmittedKey_FiresNoAfterHandler_LivePhase() {
		const int rejectedKey = 7;
		const int sentinelKey = 1;
		var seenRejectedKey = 0;
		var recording = new RecordingAfterHandler();

		var (sp, cache) = await StartAsync(
			b => b.WithKeyFilter(k => {
				if (k == rejectedKey)
					Interlocked.Increment(ref seenRejectedKey);
				return k != rejectedKey;
			}),
			services => services.AddSingleton<ICacheAfterHandler<int, FilterEntity>>(recording));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		ProduceTombstone(producer, rejectedKey);
		Produce(producer, sentinelKey, "sentinel");
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(sentinelKey, out _));
		Assert.That(cache.Cache.TryGet(sentinelKey, out _), Is.True,
			"Sentinel must arrive — without it nothing proves the tombstone was processed");

		Assert.That(cache.Cache.TryGet(rejectedKey, out _), Is.False);
		Assert.That(seenRejectedKey, Is.Zero, "The key predicate must not be invoked for a tombstone");
		Assert.That(recording.Count(UpdateType.Delete), Is.Zero, "Removing a key that was never cached is not a Delete");
		Assert.That(recording.Count(UpdateType.Filtered), Is.Zero, "...and it is not a Filtered message either");
		Assert.That(recording.Count(UpdateType.Add), Is.EqualTo(1), "Only the sentinel should have been added");

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	private void ProduceTombstone(IProducer<byte[], byte[]> producer, int id) {
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = null!,
			Headers = new Headers()
		});
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>> configure,
		Action<IServiceCollection>? extra = null) {
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
		extra?.Invoke(services);
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

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentBag<UpdateType> _updates = new();

		public int Count(UpdateType type) => _updates.Count(u => u == type);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_updates.Add(updateType);
			return ValueTask.CompletedTask;
		}
	}
}

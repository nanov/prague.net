namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prague.Kafka.IO;

/// <summary>
///   Load/live parity for every filter outcome that can supersede an already-cached value.
///   <para>
///     Issue #35 existed because the load and live branches of <c>DispatchRaw</c> implement the same
///     decisions twice and the test matrix was asymmetric in exactly the same shape: every
///     <c>treatAsDelete</c> assertion was live-phase only. So the load branch could quietly disagree
///     with the live branch and nothing failed.
///   </para>
///   <para>
///     Each case is asserted in both phases against the same expected cache state. The load variant
///     always crosses a compacting-buffer flush, so the earlier value is really in the cache rather
///     than sitting in the buffer — that is the boundary #35 was lost across.
///   </para>
/// </summary>
[TestFixture]
public class LoadLivePhaseParityTests {
	/// <summary>The message that supersedes an already-cached value for <see cref="Key"/>.</summary>
	public enum Supersede {
		/// <summary>Empty value span.</summary>
		Tombstone,

		/// <summary>Key filter rejects with <c>treatAsDelete: true</c>.</summary>
		KeyFilterDelete,

		/// <summary>Value filter rejects with <c>treatAsDelete: true</c>.</summary>
		ValueFilterDelete,

		/// <summary>Key filter rejects with <c>treatAsDelete: false</c> — a skip, not a delete.</summary>
		KeyFilterSkip,

		/// <summary>Value filter rejects with <c>treatAsDelete: false</c> — a skip, not a delete.</summary>
		ValueFilterSkip
	}

	private const string TopicPrefix = "it-phase-parity";
	private const int Key = 1;
	private const int SentinelKey = 999;
	private const int SpacerFirstKey = 100;
	private const string KeptName = "keep";

	private string _topic = "";
	private int _keyEvaluations;

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		_keyEvaluations = 0;
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

	[TestCase(Supersede.Tombstone)]
	[TestCase(Supersede.KeyFilterDelete)]
	[TestCase(Supersede.ValueFilterDelete)]
	[TestCase(Supersede.KeyFilterSkip)]
	[TestCase(Supersede.ValueFilterSkip)]
	public async Task LoadPhase(Supersede supersede) {
		var lastSpacer = SpacerFirstKey + KafkaCacheHandler.COMPACTING_BUFFER_CAPACITY;
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			ProduceValue(seeder, Key, KeptName);

			// Enough values for other keys to force a mid-load FlushRawLoadBufferToCache, so the value
			// for Key is in the cache — not merely pending in the compacting buffer — when the
			// superseding message arrives.
			for (var i = SpacerFirstKey; i <= lastSpacer; i++)
				ProduceValue(seeder, i, $"spacer-{i}");

			ProduceSuperseding(seeder, supersede);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var (sp, cache) = await StartAsync(supersede);

		Assert.That(cache.Cache.TryGet(lastSpacer, out _), Is.True,
			"Spacer keys must be loaded — otherwise the flush boundary was never crossed and the case is vacuous");
		AssertOutcome(cache, supersede, "load");
		AssertKeyFilterSaw(supersede);

		await StopAsync(sp);
	}

	[TestCase(Supersede.Tombstone)]
	[TestCase(Supersede.KeyFilterDelete)]
	[TestCase(Supersede.ValueFilterDelete)]
	[TestCase(Supersede.KeyFilterSkip)]
	[TestCase(Supersede.ValueFilterSkip)]
	public async Task LivePhase(Supersede supersede) {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			ProduceValue(seeder, Key, KeptName);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var (sp, cache) = await StartAsync(supersede);
		Assert.That(cache.Cache.TryGet(Key, out _), Is.True, "Precondition: the key must be loaded before going live");

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		ProduceSuperseding(producer, supersede);
		ProduceValue(producer, SentinelKey, "sentinel");
		producer.Flush(TimeSpan.FromSeconds(10));

		// One partition and one FIFO live worker, so the sentinel landing proves the superseding
		// message ahead of it has already been applied. Deterministic, unlike sleeping.
		await WaitUntil(() => cache.Cache.TryGet(SentinelKey, out _));
		Assert.That(cache.Cache.TryGet(SentinelKey, out _), Is.True,
			"Sentinel must arrive — without it nothing proves the superseding message was processed");
		AssertOutcome(cache, supersede, "live");
		AssertKeyFilterSaw(supersede);

		await StopAsync(sp);
	}

	/// <summary>`Delete` evicts; `Skip` drops the message and leaves the cached value alone.</summary>
	private static bool ExpectsRemoval(Supersede supersede)
		=> supersede is Supersede.Tombstone or Supersede.KeyFilterDelete or Supersede.ValueFilterDelete;

	private static void AssertOutcome(FilterEntityCache cache, Supersede supersede, string phase) {
		if (ExpectsRemoval(supersede)) {
			Assert.That(cache.Cache.TryGet(Key, out _), Is.False, $"{supersede} during {phase} must remove the key");
			return;
		}

		Assert.That(cache.Cache.TryGet(Key, out var kept), Is.True, $"{supersede} during {phase} must keep the key");
		Assert.That(kept!.Name, Is.EqualTo(KeptName), $"{supersede} during {phase} must keep the previous value");
	}

	/// <summary>
	///   Guards the stateful key predicate: a key is immutable, so key-filter rejection can only be
	///   driven by evaluation order. If the count is not 2 the wrong message was judged and the
	///   assertions above prove nothing.
	/// </summary>
	private void AssertKeyFilterSaw(Supersede supersede) {
		if (supersede is not (Supersede.KeyFilterDelete or Supersede.KeyFilterSkip))
			return;

		Assert.That(_keyEvaluations, Is.EqualTo(2), "The key filter must have judged exactly two messages for the key");
	}

	private void ProduceValue(IProducer<byte[], byte[]> producer, int id, string name) {
		var entity = new FilterEntity { Id = id, Name = name, Value = id };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	private void ProduceSuperseding(IProducer<byte[], byte[]> producer, Supersede supersede) {
		switch (supersede) {
			case Supersede.Tombstone:
				producer.Produce(_topic, new Message<byte[], byte[]> {
					Key = MessagePackSerializer.Serialize(Key),
					Value = null!,
					Headers = new Headers()
				});
				return;
			case Supersede.ValueFilterDelete:
			case Supersede.ValueFilterSkip:
				ProduceValue(producer, Key, "reject-me");
				return;
			default:
				// Key filters never see the value; the second evaluation of this key is the rejection.
				ProduceValue(producer, Key, "second");
				return;
		}
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartAsync(Supersede supersede) {
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
			var handler = b.AddCache<FilterEntityCache, int, FilterEntity>(_topic);
			switch (supersede) {
				case Supersede.KeyFilterDelete:
					handler.WithKeyFilter(AcceptKeyOnce, treatAsDelete: true);
					break;
				case Supersede.KeyFilterSkip:
					handler.WithKeyFilter(AcceptKeyOnce);
					break;
				case Supersede.ValueFilterDelete:
					handler.WithValueFilter(RejectMarkedValue, treatAsDelete: true);
					break;
				case Supersede.ValueFilterSkip:
					handler.WithValueFilter(RejectMarkedValue);
					break;
				case Supersede.Tombstone:
				default:
					break;
			}
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

	private bool AcceptKeyOnce(int key)
		=> key != Key || Interlocked.Increment(ref _keyEvaluations) == 1;

	private static bool RejectMarkedValue(FilterEntity value)
		=> !value.Name.StartsWith("reject", StringComparison.Ordinal);

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
}

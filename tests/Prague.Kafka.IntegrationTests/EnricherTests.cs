namespace Prague.Kafka.IntegrationTests;

using System.Collections.Concurrent;
using Nanov.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
///   <c>WithEnricher</c> against a broker: the hook runs in both phases, after header enrichment, after the value
///   filters, in registration order, and a throw keeps the value as enriched so far. The live phase compares the
///   enriched value with the resident one, so a re-emitted message whose enrichment is unchanged reports Same and
///   one whose enrichment changed reports Update.
/// </summary>
[TestFixture]
public class EnricherTests {
	private const string TopicPrefix = "it-enricher";

	private string _topic = "";
	private readonly List<IServiceProvider> _providers = new();

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}";
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
	}

	/// <summary>
	///   Runs whatever the test did. A failing test never reaches its own StopAsync call, and a consumer
	///   left alive stays a member of the group, which stalls later tests' initial loads.
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
	public async Task Enricher_RunsDuringTheInitialLoad() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var i = 1; i <= 3; i++)
				Produce(seeder, i, $"preload-{i}", i);
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher<SuffixEnricher>());

		Assert.That(cache.Cache.Count, Is.EqualTo(3));
		for (var i = 1; i <= 3; i++) {
			Assert.That(cache.Cache.TryGet(i, out var value), Is.True);
			Assert.That(value!.Name, Is.EqualTo($"preload-{i}!"), "the load branch stores the enriched value");
		}

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_RunsInTheLivePhase() {
		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher<SuffixEnricher>());

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 7, "live", 7);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(7, out _));
		Assert.That(cache.Cache.TryGet(7, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("live!"), "the live branch stores the enriched value");

		await StopAsync(sp);
	}

	[Test]
	public async Task Enrichers_RunInRegistrationOrder_EachSeeingThePreviousChange() {
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithEnricher(_ => new AppendEnricher("A"))
			.WithEnricher(_ => new AppendEnricher("B")));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "x", 1);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("xAB"));

		await StopAsync(sp);
	}

	[Test]
	public async Task ThrowingEnricher_IsLogged_KeepsTheValueAsEnrichedSoFar_AndDoesNotStopTheOthers() {
		var logs = new CapturingLoggerProvider();
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithEnricher(_ => new AppendEnricher("0"))
			.WithEnricher(_ => new AppendEnricher("1", throwForKey: 2))
			.WithEnricher(_ => new AppendEnricher("2")), logs);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "x", 1);
		Produce(producer, 2, "y", 2);
		Produce(producer, 3, "z", 3);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.Count == 3);
		Assert.That(cache.Cache.TryGet(1, out var first), Is.True);
		Assert.That(first!.Name, Is.EqualTo("x012"));
		Assert.That(cache.Cache.TryGet(2, out var second), Is.True);
		Assert.That(second!.Name, Is.EqualTo("y02"),
			"the change made before the throw survives; the throwing enricher appended nothing; the next one still ran");
		Assert.That(cache.Cache.TryGet(3, out var third), Is.True);
		Assert.That(third!.Name, Is.EqualTo("z012"), "the next message is unaffected");
		Assert.That(logs.Errors.Count(m => m.Contains("Enricher threw")), Is.EqualTo(1));

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_SeesTheHeaderEnrichedValue() {
		var (sp, cache) = await StartHeaderEntityAsync(b => b.WithEnricher<TenantIntoNameEnricher>());

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		var sent = new HeaderEnrichEntity { Id = 1, Name = "first", TenantId = 12345, EventType = "UserCreated" };
		var headers = new Headers();
		HeaderEnrichEntity.Derich(sent, headers);
		// The body serializes TenantId as well; zero it there so only the header can supply 12345.
		sent.TenantId = 0;
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id),
			Value = MessagePackSerializer.Serialize(sent),
			Headers = headers
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(1, out _));
		Assert.That(cache.Cache.TryGet(1, out var value), Is.True);
		Assert.That(value!.Name, Is.EqualTo("first@12345"), "the [DataCacheHeader] property is already set when the enricher runs");

		await StopAsync(sp);
	}

	[Test]
	public async Task Enricher_DoesNotRunForAValueTheFilterRejected() {
		var seen = new SeenKeysEnricher();
		var (sp, cache) = await StartFilterEntityAsync(b => b
			.WithValueFilter(static v => v.Value >= 0)
			.WithEnricher(_ => seen));

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "rejected", -1);
		Produce(producer, 2, "accepted", 1);
		producer.Flush(TimeSpan.FromSeconds(10));

		await WaitUntil(() => cache.Cache.TryGet(2, out _));
		Assert.That(cache.Cache.TryGet(1, out _), Is.False);
		Assert.That(seen.Keys, Is.EquivalentTo(new[] { 2 }));

		await StopAsync(sp);
	}

	[Test]
	public async Task UnchangedEnrichment_IsSame_ChangedIsUpdate() {
		var enricher = new ValueFromIdEnricher();
		var recording = new RecordingAfterHandler();
		var (sp, cache) = await StartFilterEntityAsync(b => b.WithEnricher(_ => enricher), afterHandler: recording);

		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		Produce(producer, 1, "same", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Add && i.Key == 1));
		Assert.That(cache.Cache.TryGet(1, out var stored), Is.True);
		Assert.That(stored!.Value, Is.EqualTo(10), "Value comes from the enricher");

		Produce(producer, 1, "same", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1));

		// The input stays byte-identical and only the enrichment changes, so an Update can only come from
		// comparing the enriched value with the resident one.
		enricher.Factor = 20;
		Produce(producer, 1, "same", 0);
		producer.Flush(TimeSpan.FromSeconds(10));
		await WaitUntil(() => recording.Any(i => i.UpdateType == UpdateType.Update && i.Key == 1));

		Assert.That(recording.Any(i => i.UpdateType == UpdateType.Same && i.Key == 1), Is.True,
			"an enrichment that reproduces the resident value reports Same");
		Assert.That(cache.Cache.TryGet(1, out var updated), Is.True);
		Assert.That(updated!.Value, Is.EqualTo(20), "the changed enrichment is stored");
		var update = recording.FirstOrDefault(i => i.UpdateType == UpdateType.Update && i.Key == 1);
		Assert.That(update, Is.Not.Null, "an enrichment that changes the resident value reports Update");
		Assert.That(update!.OldValue!.Value, Is.EqualTo(10));
		Assert.That(update.NewValue!.Value, Is.EqualTo(20));

		await StopAsync(sp);
	}

	private void Produce(IProducer<byte[], byte[]> producer, int id, string name, int value) {
		var entity = new FilterEntity { Id = id, Name = name, Value = value };
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(entity),
			Headers = new Headers()
		});
	}

	private async Task<(IServiceProvider sp, FilterEntityCache cache)> StartFilterEntityAsync(
		Action<KafkaCacheHandlerBuilder<FilterEntityCache, int, FilterEntity>> configure,
		CapturingLoggerProvider? logs = null,
		ICacheAfterHandler<int, FilterEntity>? afterHandler = null) {
		var services = NewServices(logs);
		if (afterHandler is not null)
			services.AddSingleton(afterHandler);
		services.AddKafkaCaches("KafkaConfig", b => configure(b.AddCache<FilterEntityCache, int, FilterEntity>(_topic)));
		var sp = await StartAsync(services);
		return (sp, sp.GetRequiredService<FilterEntityCache>());
	}

	private async Task<(IServiceProvider sp, HeaderEnrichEntityCache cache)> StartHeaderEntityAsync(
		Action<KafkaCacheHandlerBuilder<HeaderEnrichEntityCache, int, HeaderEnrichEntity>> configure) {
		var services = NewServices(null);
		services.AddKafkaCaches("KafkaConfig", b => configure(b.AddCache<HeaderEnrichEntityCache, int, HeaderEnrichEntity>(_topic)));
		var sp = await StartAsync(services);
		return (sp, sp.GetRequiredService<HeaderEnrichEntityCache>());
	}

	private static ServiceCollection NewServices(CapturingLoggerProvider? logs) {
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
		services.AddLogging(logging => {
			if (logs is not null)
				logging.AddProvider(logs);
		});
		return services;
	}

	private async Task<IServiceProvider> StartAsync(ServiceCollection services) {
		var sp = services.BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);
		return sp;
	}

	private static async Task StopAsync(IServiceProvider sp) {
		var hosted = sp.GetRequiredService<IHostedService>();
		await hosted.StopAsync(CancellationToken.None);
		// Disposing cancels the consume loop, whose finally closes the consumer and leaves the group.
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

	private sealed class SuffixEnricher : ICacheEnricher<int, FilterEntity> {
		public void Enrich(int key, FilterEntity value) => value.Name += "!";
	}

	private sealed class AppendEnricher(string suffix, int? throwForKey = null) : ICacheEnricher<int, FilterEntity> {
		public void Enrich(int key, FilterEntity value) {
			if (key == throwForKey)
				throw new InvalidOperationException($"enricher {suffix} refuses key {key}");
			value.Name += suffix;
		}
	}

	private sealed class TenantIntoNameEnricher : ICacheEnricher<int, HeaderEnrichEntity> {
		public void Enrich(int key, HeaderEnrichEntity value) => value.Name = $"{value.Name}@{value.TenantId}";
	}

	private sealed class SeenKeysEnricher : ICacheEnricher<int, FilterEntity> {
		private readonly ConcurrentQueue<int> _keys = new();

		public IReadOnlyCollection<int> Keys => _keys;

		public void Enrich(int key, FilterEntity value) => _keys.Enqueue(key);
	}

	private sealed class ValueFromIdEnricher : ICacheEnricher<int, FilterEntity> {
		// Written by the test thread, read on the consume thread.
		private volatile int _factor = 10;

		public int Factor {
			get => _factor;
			set => _factor = value;
		}

		public void Enrich(int key, FilterEntity value) => value.Value = key * _factor;
	}

	private sealed record Invocation(UpdateType UpdateType, int Key, FilterEntity? NewValue, FilterEntity? OldValue);

	private sealed class RecordingAfterHandler : ICacheAfterHandler<int, FilterEntity> {
		private readonly ConcurrentQueue<Invocation> _invocations = new();

		public bool Any(Func<Invocation, bool> predicate) => _invocations.Any(predicate);

		public Invocation? FirstOrDefault(Func<Invocation, bool> predicate) => _invocations.FirstOrDefault(predicate);

		public ValueTask Handle(UpdateType updateType, int key, FilterEntity? newValue, FilterEntity? oldValue) {
			_invocations.Enqueue(new Invocation(updateType, key, newValue, oldValue));
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>Collects Error-level log lines so a test can assert that a throw was reported exactly once.</summary>
	private sealed class CapturingLoggerProvider : ILoggerProvider {
		private readonly ConcurrentQueue<string> _errors = new();

		public IReadOnlyCollection<string> Errors => _errors;

		public ILogger CreateLogger(string categoryName) => new Logger(_errors);

		public void Dispose() { }

		private sealed class Logger(ConcurrentQueue<string> errors) : ILogger {
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
				if (logLevel >= LogLevel.Error)
					errors.Enqueue(formatter(state, exception));
			}
		}
	}
}

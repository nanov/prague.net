namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Entities;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class LifecycleTests {
	private const string TopicPrefix = "it-lifecycle";

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
	public async Task EmptyTopic_LoadsCleanly_AndCacheIsEmpty() {
		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.Count, Is.EqualTo(0), "Empty topic should load to an empty cache");

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	[Test]
	public async Task PopulatedTopic_LoadsToCompletion() {
		using (var seeder = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA)) {
			for (var i = 1; i <= 10; i++)
				seeder.Produce(_topic, new Message<byte[], byte[]> {
					Key = MessagePackSerializer.Serialize(i),
					Value = MessagePackSerializer.Serialize(new FilterEntity { Id = i, Name = $"e-{i}", Value = i }),
					Headers = new Headers()
				});
			seeder.Flush(TimeSpan.FromSeconds(10));
		}

		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.Count, Is.EqualTo(10), "All seeded records must be present after the loader completes");
		Assert.That(cache.Cache.TryGet(5, out var e5), Is.True);
		Assert.That(e5!.Name, Is.EqualTo("e-5"));

		await hosted.StopAsync(CancellationToken.None);
		(sp as IDisposable)?.Dispose();
	}

	[Test]
	public async Task HostedService_StartStop_Completes() {
		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		// A clean stop must complete without throwing.
		Assert.DoesNotThrowAsync(async () => await hosted.StopAsync(CancellationToken.None));
	}

	/// <summary>
	///   Regression for #43: StopAsync called the loader's StartAsync, so a graceful stop left the raw
	///   consume loop polling and the live workers running. A stopped host must apply nothing more.
	/// </summary>
	[Test]
	public async Task HostedService_Stop_HaltsLiveConsumption() {
		Produce(1);

		using var sp = BuildServices().BuildServiceProvider();
		_providers.Add(sp);
		var hosted = sp.GetRequiredService<IHostedService>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		await hosted.StartAsync(cts.Token);
		await sp.GetRequiredService<KafkaCachesLoader>().StartAsync(cts.Token);

		var cache = sp.GetRequiredService<FilterEntityCache>();
		Assert.That(cache.Cache.Count, Is.EqualTo(1), "Seeded record must be loaded before the stop");

		await hosted.StopAsync(CancellationToken.None);

		// Published strictly after the stop returned: a stopped consumer must never observe it.
		Produce(2);
		for (var i = 0; i < 20; i++) {
			Assert.That(cache.Cache.TryGet(2, out _), Is.False,
				"Stopped consumer applied a record published after StopAsync returned");
			await Task.Delay(100);
		}
	}

	private void Produce(int id) {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(id),
			Value = MessagePackSerializer.Serialize(new FilterEntity { Id = id, Name = $"e-{id}", Value = id }),
			Headers = new Headers()
		});
		producer.Flush(TimeSpan.FromSeconds(10));
	}

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
}

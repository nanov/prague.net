namespace Prague.Baseline.Harness.Real;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prague.Baseline.Scenario;
using Prague.Kafka;
using Testcontainers.Kafka;

/// <summary>
///   full-real ingest driver: spins up a real Kafka broker via Testcontainers, produces the
///   pre-encoded payloads as plain <c>Message&lt;byte[],byte[]&gt;</c> (no <c>X-Producer-Id</c> header,
///   so the Prague consumer ingests them), then wires the real Prague consumer through
///   <c>AddKafkaCaches</c> and starts the load. Modeled on
///   <c>DualKafkaClusterFixture</c> + <c>SelfConsumeTests</c> in the Kafka integration tests.
/// </summary>
internal sealed class RealIngestPipeline : IAsyncDisposable {
	private const string ProductsTopic = "baseline-products";
	private const string InfosTopic = "baseline-product-infos";
	private const string OffersTopic = "baseline-offers";

	private KafkaContainer _broker = null!;
	private ServiceProvider _sp = null!;

	/// <summary>Starts the broker, creates the topics, produces every payload, and returns the bootstrap address.</summary>
	public async Task<string> StartBrokerAndProduceAsync(EncodedSet enc) {
		_broker = new KafkaBuilder("confluentinc/cp-kafka:7.5.0")
			.WithImage("confluentinc/cp-kafka:7.5.0")
			.Build();
		await _broker.StartAsync();
		var bootstrap = _broker.GetBootstrapAddress();

		await CreateTopicAsync(bootstrap, ProductsTopic);
		await CreateTopicAsync(bootstrap, InfosTopic);
		await CreateTopicAsync(bootstrap, OffersTopic);

		using var producer = new ProducerBuilder<byte[], byte[]>(
			new ProducerConfig { BootstrapServers = bootstrap, Acks = Acks.All }).Build();
		Produce(producer, ProductsTopic, enc.Products);
		Produce(producer, InfosTopic, enc.Infos);
		Produce(producer, OffersTopic, enc.Offers);
		producer.Flush(TimeSpan.FromSeconds(30));
		return bootstrap;
	}

	private static void Produce(IProducer<byte[], byte[]> p, string topic, EncodedEntity[] items) {
		for (var i = 0; i < items.Length; i++) {
			var e = items[i];
			// Plain record with EMPTY headers: no producer-instance header => the Prague consumer's
			// self-consume guard does not filter it, so the caches ingest every record.
			p.Produce(topic, new Message<byte[], byte[]> { Key = e.Key, Value = e.Value, Headers = new Headers() });
		}
	}

	private static async Task CreateTopicAsync(string bootstrap, string name) {
		using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
		try {
			await admin.CreateTopicsAsync([
				new TopicSpecification { Name = name, NumPartitions = 1, ReplicationFactor = 1 }
			]);
		}
		catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists)) {
		}
	}

	/// <summary>Wires the Prague consumer against the broker and starts the load, returning the live caches.</summary>
	public async Task<(BaselineProductCache Products, BaselineProductInfoCache Infos, BaselineOfferCache Offers)>
		StartConsumerAsync(string bootstrap, CancellationToken ct) {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				["KafkaConfig:BootstrapServers"] = bootstrap,
			})
			.Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IConfiguration>(config);
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<BaselineProductCache, int, BaselineProduct>(ProductsTopic);
			b.AddCache<BaselineProductInfoCache, int, BaselineProductInfo>(InfosTopic);
			b.AddCache<BaselineOfferCache, int, BaselineOffer>(OffersTopic);
		});
		_sp = services.BuildServiceProvider();
		await _sp.GetRequiredService<IHostedService>().StartAsync(ct);
		await _sp.GetRequiredService<KafkaCachesLoader>().StartAsync(ct);
		return (
			_sp.GetRequiredService<BaselineProductCache>(),
			_sp.GetRequiredService<BaselineProductInfoCache>(),
			_sp.GetRequiredService<BaselineOfferCache>());
	}

	public async ValueTask DisposeAsync() {
		if (_sp is not null)
			await _sp.DisposeAsync();
		if (_broker is not null)
			await _broker.DisposeAsync();
	}
}

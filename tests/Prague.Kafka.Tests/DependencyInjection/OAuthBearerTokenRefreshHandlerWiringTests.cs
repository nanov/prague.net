namespace Prague.Kafka.Tests.DependencyInjection;

using Nanov.Kafka;
using Prague.Kafka;
using Prague.Kafka.IO;
using Prague.Kafka.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class OAuthBearerTokenRefreshHandlerWiringTests {
	private const string CLUSTER_NAME = "KafkaConfig";

	[Test]
	public async Task AddKafkaCaches_WithOAuthBearerHandler_AttachesItToTheCacheConsumer() {
		Action<IClient, string> handler = (_, _) => { };
		var provider = new CapturingBuilderProvider();
		await using var sp = BuildServices(provider, o => o.OAuthBearerTokenRefreshHandler = handler);

		await BuildAndCloseConsumer(sp);

		Assert.That(provider.Consumer, Is.Not.Null, "The cache consumer must be built through the provider.");
		Assert.That(provider.Consumer!.RefreshHandler, Is.SameAs(handler),
			"The configured handler must reach the raw consumer builder unchanged.");
	}

	[Test]
	public async Task AddKafkaCaches_WithoutOAuthBearerHandler_LeavesTheCacheConsumerWithoutOne() {
		var provider = new CapturingBuilderProvider();
		await using var sp = BuildServices(provider, _ => { });

		await BuildAndCloseConsumer(sp);

		Assert.That(provider.Consumer, Is.Not.Null, "The cache consumer must be built through the provider.");
		Assert.That(provider.Consumer!.RefreshHandler, Is.Null,
			"Without a configured handler the raw consumer builder must not get one.");
	}

	[Test]
	public async Task AddKafkaCaches_WithOAuthBearerHandler_AttachesItToTheCacheProducer() {
		Action<IClient, string> handler = (_, _) => { };
		var provider = new CapturingBuilderProvider();
		await using var sp = BuildServices(provider, o => o.OAuthBearerTokenRefreshHandler = handler);

		sp.GetRequiredKeyedService<KafkaCacheProducer>(CLUSTER_NAME);

		Assert.That(provider.Producer, Is.Not.Null, "The cache producer must be built through the provider.");
		Assert.That(provider.Producer!.RefreshHandler, Is.SameAs(handler),
			"The configured handler must reach the raw producer builder unchanged.");
	}

	[Test]
	public async Task AddKafkaCaches_WithoutOAuthBearerHandler_LeavesTheCacheProducerWithoutOne() {
		var provider = new CapturingBuilderProvider();
		await using var sp = BuildServices(provider, _ => { });

		sp.GetRequiredKeyedService<KafkaCacheProducer>(CLUSTER_NAME);

		Assert.That(provider.Producer, Is.Not.Null, "The cache producer must be built through the provider.");
		Assert.That(provider.Producer!.RefreshHandler, Is.Null,
			"Without a configured handler the raw producer builder must not get one.");
	}

	private static ServiceProvider BuildServices(CapturingBuilderProvider provider, Action<KafkaCachesOptions> configure) {
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ $"{CLUSTER_NAME}:BootstrapServers", "127.0.0.1:1" }
			}).Build());
		services.AddLogging();
		services.AddSingleton<IKafkaCacheBuilderProvider>(provider);

		services.AddKafkaCaches(CLUSTER_NAME, configure, _ => { });

		return services.BuildServiceProvider();
	}

	private static async Task BuildAndCloseConsumer(IServiceProvider sp) {
		var consumer = sp.GetRequiredKeyedService<KafkaCacheConsumer>(CLUSTER_NAME);
		await consumer.ExecuteAsync(new CancellationToken(true));
		await consumer.WaitForCompletionAsync();
	}

	private sealed class CapturingBuilderProvider : IKafkaCacheBuilderProvider {
		public CapturingConsumerBuilder? Consumer { get; private set; }
		public CapturingProducerBuilder? Producer { get; private set; }

		public RawConsumerBuilder NewRawConsumerBuilder(IEnumerable<KeyValuePair<string, string>> config)
			=> Consumer = new CapturingConsumerBuilder(config);

		public RawProducerBuilder NewRawProducerBuilder(IEnumerable<KeyValuePair<string, string>> config)
			=> Producer = new CapturingProducerBuilder(config);

		public ProducerBuilder<TKey, TValue> NewProducerBuilder<TKey, TValue>(IEnumerable<KeyValuePair<string, string>> config)
			=> new(config);
	}

	private sealed class CapturingConsumerBuilder(IEnumerable<KeyValuePair<string, string>> config)
		: RawConsumerBuilder(config) {
		public Action<IConsumer<Ignore, Ignore>, string>? RefreshHandler => OAuthBearerTokenRefreshHandler;
	}

	private sealed class CapturingProducerBuilder(IEnumerable<KeyValuePair<string, string>> config)
		: RawProducerBuilder(config) {
		public Action<IProducer<Ignore, Ignore>, string>? RefreshHandler => OAuthBearerTokenRefreshHandler;
	}
}

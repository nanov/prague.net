namespace Prague.Kafka.Tests.DependencyInjection;

using Prague.Kafka;
using Prague.Kafka.Options;
using MessagePack;
using MessagePack.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class AddKafkaCachesOptionsWiringTests {
	[SetUp]
	public void Reset() => PragueMessagePack.ResetForTests();

	[TearDown]
	public void ResetAfter() => PragueMessagePack.ResetForTests();

	[Test]
	public void AddKafkaCaches_WithOptionsCallback_ConfiguresPragueMessagePack() {
		var probe = new ProbeResolver();
		var services = new ServiceCollection();
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "localhost:9092" }
			}).Build();
		services.AddSingleton<IConfiguration>(config);
		services.AddLogging();

		services.AddKafkaCaches("KafkaConfig",
			(Action<KafkaCachesOptions, IServiceProvider>?)null,
			_ => { /* no caches */ },
			options: o => o.WithMessagePackResolver(_ => probe));

		// PragueMessagePack.Configure is called inline by AddKafkaCaches when an options callback
		// is supplied — no BuildServiceProvider needed.
		Assert.That(PragueMessagePack.Options.Resolver, Is.SameAs(probe),
			"AddKafkaCaches options callback must configure PragueMessagePack.Options so the user's resolver becomes active.");
	}

	[Test]
	public void AddKafkaCaches_WithoutOptionsCallback_LeavesDefaultOptions() {
		var services = new ServiceCollection();
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "localhost:9092" }
			}).Build();
		services.AddSingleton<IConfiguration>(config);
		services.AddLogging();

		services.AddKafkaCaches("KafkaConfig",
			(Action<KafkaCachesOptions, IServiceProvider>?)null,
			_ => { });

		Assert.That(PragueMessagePack.Options, Is.SameAs(PragueMessagePack.DefaultOptions()),
			"Without an options callback, AddKafkaCaches must leave PragueMessagePack at the default singleton.");
	}

	/// <summary>Test resolver used as a sentinel to verify the compose return value becomes the active resolver.</summary>
	private sealed class ProbeResolver : IFormatterResolver {
		public IMessagePackFormatter<T>? GetFormatter<T>() => null;
	}
}

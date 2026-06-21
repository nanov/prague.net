namespace Prague.Kafka.Tests.DependencyInjection;

using Prague.Kafka;
using Prague.Kafka.Options;
using MessagePack;
using MessagePack.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
	public void AddKafkaCaches_WithDelegate_BindsConfigurationThenAppliesDelegate() {
		var services = new ServiceCollection();
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", "from-config" },
				{ "KafkaConfig:ClientSettings:foo", "bar" }
			}).Build();
		services.AddSingleton<IConfiguration>(config);
		services.AddLogging();

		services.AddKafkaCaches("KafkaConfig",
			(o, _) => o.BootstrapServers = "from-code",
			_ => { });

		var sp = services.BuildServiceProvider();
		var opts = sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get("KafkaConfig");

		Assert.That(opts.BootstrapServers, Is.EqualTo("from-code"),
			"The user delegate must run after the IConfiguration bind and override the bound value.");
		Assert.That(opts.ClientSettings.GetValueOrDefault("foo"), Is.EqualTo("bar"),
			"Values bound from IConfiguration must survive — proving the section is still bound when a delegate is supplied.");
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

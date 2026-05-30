namespace Prague.Kafka.Tests.DependencyInjection;

using Prague.Kafka;
using Prague.Kafka.Options;
using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class WithMessagePackResolverTests {
	[SetUp]
	public void Reset() => PragueMessagePack.ResetForTests();

	[TearDown]
	public void ResetAfter() => PragueMessagePack.ResetForTests();

	[Test]
	public void Build_NoCallback_ReturnsDefaultOptions() {
		var builder = new KafkaCachesGlobalOptionsBuilder();
		var opts = builder.Build();
		Assert.That(opts, Is.SameAs(PragueMessagePack.DefaultOptions()));
	}

	[Test]
	public void WithMessagePackResolver_ReceivesPragueComposite() {
		IFormatterResolver? captured = null;
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackResolver(defaultResolver => {
			captured = defaultResolver;
			return defaultResolver;
		});
		_ = builder.Build();

		Assert.That(captured, Is.Not.Null);
		// Composite resolver wraps PragueDateTimeResolver first → resolver answers DateTime via our formatter.
		var dtFormatter = captured!.GetFormatter<DateTime>();
		Assert.That(dtFormatter, Is.SameAs(PragueDateTimeFormatter.Instance));
		// Falls through for everything else — Guid resolves via TypelessContractlessStandardResolver path.
		var guidFormatter = captured.GetFormatter<Guid>();
		Assert.That(guidFormatter, Is.Not.Null, "Guid formatter must resolve via Typeless composite fallback");
	}

	[Test]
	public void WithMessagePackResolver_ReturnValueBecomesActiveResolver() {
		var probe = new ProbeResolver();
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackResolver(_ => probe);
		var opts = builder.Build();

		Assert.That(opts.Resolver, Is.SameAs(probe));
	}

	[Test]
	public void Build_ComposeReturnsNull_Throws() {
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackResolver(_ => null!);

		var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
		Assert.That(ex!.Message, Does.Contain("null"));
	}

	/// <summary>Test resolver used as a sentinel to verify the compose return value becomes the active resolver.</summary>
	private sealed class ProbeResolver : IFormatterResolver {
		public IMessagePackFormatter<T>? GetFormatter<T>() => null;
	}
}

namespace Prague.Kafka.Tests.DependencyInjection;

using Prague.Kafka;
using Prague.Kafka.Options;
using MessagePack;
using NUnit.Framework;

[TestFixture]
public class WithMessagePackFormatterFactoryTests {
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
	public void WithMessagePackFormatterFactory_ReceivesPragueChain() {
		MessagePackFormatterFactory? captured = null;
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackFormatterFactory(defaultFactory => {
			captured = defaultFactory;
			return defaultFactory;
		});
		_ = builder.Build();

		Assert.That(captured, Is.Not.Null);
		var opts = PragueMessagePack.CreateOptions(captured!);
		// Prague's DateTime factory sits ahead of the default chain → legacy int64 (0xd3) on the wire.
		var dtBytes = MessagePackSerializer.Serialize(new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc), opts);
		Assert.That(dtBytes[0], Is.EqualTo(0xd3));
		// Falls through for everything else — Guid resolves via the typeless default chain.
		var guid = Guid.NewGuid();
		Assert.That(MessagePackSerializer.Deserialize<Guid>(MessagePackSerializer.Serialize(guid, opts), opts), Is.EqualTo(guid));
	}

	[Test]
	public void WithMessagePackFormatterFactory_ReturnValueBecomesActiveFactory() {
		var probe = new ProbeFormatterFactory();
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackFormatterFactory(defaultFactory => MessagePackFormatterFactory.Combine(probe, defaultFactory));
		var opts = builder.Build();

		_ = MessagePackSerializer.Serialize(42, opts);
		Assert.That(probe.Calls, Is.GreaterThan(0));
	}

	[Test]
	public void Build_ComposeReturnsNull_Throws() {
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackFormatterFactory(_ => null!);

		var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
		Assert.That(ex!.Message, Does.Contain("null"));
	}
}

/// <summary>Sentinel factory: serves nothing, counts lookups so a test can prove it sits in the active chain.</summary>
internal sealed partial class ProbeFormatterFactory : MessagePackFormatterFactory {
	public int Calls;

	public override object? CreateFormatter<TWriteBuffer, TReadBuffer>(Type type) {
		Calls++;
		return null;
	}
}

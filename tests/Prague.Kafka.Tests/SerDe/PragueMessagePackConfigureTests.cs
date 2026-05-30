namespace Prague.Kafka.Tests.SerDe;

using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class PragueMessagePackConfigureTests {
	[SetUp]
	public void ResetBeforeTest() => PragueMessagePack.ResetForTests();

	[TearDown]
	public void ResetAfterTest() => PragueMessagePack.ResetForTests();

	[Test]
	public void DefaultOptions_UsesPragueDateTimeResolverAndTypelessContractless() {
		var opts = PragueMessagePack.Options;

		Assert.That(opts, Is.Not.Null);
		Assert.That(opts.Resolver, Is.Not.Null);
		// Sanity: DateTime via default options must round-trip through the native int64 path.
		var dt = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);
		var bytes = MessagePackSerializer.Serialize(dt, opts);
		Assert.That(bytes[0], Is.EqualTo(0xd3), "DateTime must be int64-encoded (0xd3 prefix)");
	}

	[Test]
	public void Configure_FirstCall_SetsOptions() {
		var custom = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		PragueMessagePack.Configure(custom);

		Assert.That(PragueMessagePack.Options, Is.SameAs(custom));
	}

	[Test]
	public void Configure_SameReferenceTwice_IsNoOp() {
		var custom = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		PragueMessagePack.Configure(custom);

		Assert.DoesNotThrow(() => PragueMessagePack.Configure(custom));
		Assert.That(PragueMessagePack.Options, Is.SameAs(custom));
	}

	[Test]
	public void Configure_ConflictingSecondCall_Throws() {
		var a = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		var b = MessagePackSerializerOptions.Standard.WithResolver(TypelessContractlessStandardResolver.Instance);
		PragueMessagePack.Configure(a);

		var ex = Assert.Throws<InvalidOperationException>(() => PragueMessagePack.Configure(b));
		Assert.That(ex!.Message, Does.Contain("conflicting"));
	}
}

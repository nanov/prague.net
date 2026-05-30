namespace Prague.Kafka.Tests.SerDe;

using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class PragueDateTimeFormatterTests {
	private static readonly MessagePackSerializerOptions Options =
		MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(PragueDateTimeResolver.Instance, StandardResolver.Instance));

	[Test]
	public void Serialize_ProducesLegacyInt64Encoding() {
		var value = new DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Utc);
		var bytes = MessagePackSerializer.Serialize(value, Options);

		// MessagePack int64 prefix is 0xd3 followed by 8 big-endian bytes — 9 bytes total.
		Assert.That(bytes.Length, Is.EqualTo(9));
		Assert.That(bytes[0], Is.EqualTo(0xd3));
	}

	[TestCase(DateTimeKind.Utc)]
	[TestCase(DateTimeKind.Local)]
	[TestCase(DateTimeKind.Unspecified)]
	public void Serialize_Deserialize_RoundTripsTicksAndKind(DateTimeKind kind) {
		var value = DateTime.SpecifyKind(new DateTime(2026, 5, 17, 12, 34, 56, 789), kind);

		var bytes = MessagePackSerializer.Serialize(value, Options);
		var back = MessagePackSerializer.Deserialize<DateTime>(bytes, Options);

		Assert.That(back.Ticks, Is.EqualTo(value.Ticks));
		Assert.That(back.Kind, Is.EqualTo(kind));
	}

	[Test]
	public void Deserialize_FromStandardTimestampExt_ProducesEquivalentInstant() {
		// MessagePack timestamp ext writes seconds-since-epoch UTC.
		var instantUtc = new DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Utc);

		// Encode via MessagePack's built-in DateTime writer (writes standard ext timestamp).
		// We use NativeDateTimeResolver-less options so DateTime goes through the default ext writer.
		var standardExtOptions = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		var extBytes = MessagePackSerializer.Serialize(instantUtc, standardExtOptions);

		// Sanity: first byte should be one of the ext type prefixes (0xd6, 0xd7, 0xc7).
		Assert.That(extBytes[0], Is.EqualTo(0xd6).Or.EqualTo(0xd7).Or.EqualTo(0xc7));

		// Now read via our dual-format formatter.
		var back = MessagePackSerializer.Deserialize<DateTime>(extBytes, Options);

		// Standard timestamp ext is UTC by spec; we accept that Kind comes back as Utc/Unspecified.
		Assert.That(back.ToUniversalTime(), Is.EqualTo(instantUtc));
	}

	[Test]
	public void Deserialize_UnexpectedToken_Throws() {
		// Encode a string where a DateTime is expected.
		var bytes = MessagePackSerializer.Serialize("not a datetime", Options);

		Assert.Throws<MessagePackSerializationException>(() =>
			MessagePackSerializer.Deserialize<DateTime>(bytes, Options));
	}
}

[TestFixture]
public class PragueNullableDateTimeFormatterTests {
	private static readonly MessagePackSerializerOptions Options =
		MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(PragueDateTimeResolver.Instance, StandardResolver.Instance));

	[Test]
	public void Nullable_Null_RoundTrips() {
		DateTime? value = null;
		var bytes = MessagePackSerializer.Serialize(value, Options);
		var back = MessagePackSerializer.Deserialize<DateTime?>(bytes, Options);
		Assert.That(back, Is.Null);
	}

	[TestCase(DateTimeKind.Utc)]
	[TestCase(DateTimeKind.Local)]
	[TestCase(DateTimeKind.Unspecified)]
	public void Nullable_HasValue_RoundTripsTicksAndKind(DateTimeKind kind) {
		DateTime? value = DateTime.SpecifyKind(new DateTime(2026, 5, 17, 12, 0, 0), kind);
		var bytes = MessagePackSerializer.Serialize(value, Options);
		var back = MessagePackSerializer.Deserialize<DateTime?>(bytes, Options);
		Assert.That(back, Is.Not.Null);
		Assert.That(back!.Value.Ticks, Is.EqualTo(value.Value.Ticks));
		Assert.That(back.Value.Kind, Is.EqualTo(kind));
	}

	[Test]
	public void Nullable_FromStandardTimestampExt_StillDecodes() {
		var instantUtc = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
		var standardExtOptions = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		var extBytes = MessagePackSerializer.Serialize<DateTime?>(instantUtc, standardExtOptions);

		// Sanity: should be FixExt prefix.
		Assert.That(extBytes[0], Is.EqualTo(0xd6).Or.EqualTo(0xd7).Or.EqualTo(0xc7));

		var back = MessagePackSerializer.Deserialize<DateTime?>(extBytes, Options);
		Assert.That(back, Is.Not.Null);
		Assert.That(back!.Value.ToUniversalTime(), Is.EqualTo(instantUtc));
	}
}

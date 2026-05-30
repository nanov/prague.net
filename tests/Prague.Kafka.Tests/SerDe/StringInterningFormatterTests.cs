namespace Prague.Kafka.Tests.SerDe;

using Prague.Kafka;
using MessagePack;
using NUnit.Framework;

[TestFixture]
public class StringInterningTests {
	[Test]
	public void Deserialize_RepeatedStrings_ShareSameInstance() {
		var content = new string(new[] { 'p', 'r', 'a', 'g', 'u', 'e', '-', 'r', 'e', 'p', 'e', 'a', 't' });
		var bytes1 = MessagePackSerializer.Serialize(content, PragueMessagePack.Options);
		var bytes2 = MessagePackSerializer.Serialize(content, PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<string>(bytes1, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<string>(bytes2, PragueMessagePack.Options);

		Assert.That(ReferenceEquals(a, b), Is.True,
			"Repeated wire strings must dedupe to the same CLR instance via MessagePack's StringInterningFormatter.");
	}

	[Test]
	public void Deserialize_NullString_ReturnsNull() {
		var bytes = MessagePackSerializer.Serialize<string?>(null, PragueMessagePack.Options);
		var back = MessagePackSerializer.Deserialize<string?>(bytes, PragueMessagePack.Options);
		Assert.That(back, Is.Null);
	}

	[Test]
	public void Serialize_RoundTripsContent() {
		var s = "hello-prague-string";
		var bytes = MessagePackSerializer.Serialize(s, PragueMessagePack.Options);
		var back = MessagePackSerializer.Deserialize<string>(bytes, PragueMessagePack.Options);
		Assert.That(back, Is.EqualTo(s));
	}
}

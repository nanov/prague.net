namespace Prague.Kafka.SerDe;

using System.Text;
using MessagePack;
using SerializerFoundation;

/// <summary>
///   String formatter that interns every deserialized string through a <see cref="Utf8WeakInternPool"/>:
///   repeated values share one instance, a repeat costs a hash and a byte compare (no decode, no
///   allocation), and nothing is retained once the cache stops referencing it.
/// </summary>
// MsgPack104: this formatter IS the string formatter, so writing the string directly is intended.
#pragma warning disable MsgPack104
internal sealed partial class PragueStringInterningFormatter<TWriteBuffer, TReadBuffer> : IMessagePackFormatter<TWriteBuffer, TReadBuffer, string?> {
	// Long strings are rarely repeated values; interning them would only pin a byte[] copy per entry.
	private const int MaxInternedBytes = 4096;

	private readonly Utf8WeakInternPool _pool;

	public PragueStringInterningFormatter(Utf8WeakInternPool pool) => _pool = pool;

	public void Initialize(MessagePackFormatterResolver resolver) { }

	public void Serialize(ref TWriteBuffer buffer, ref SerializeState state, string? value) {
		if (value is null)
			buffer.WriteNil();
		else
			buffer.WriteString(value);
	}

	public void Deserialize(ref TReadBuffer buffer, ref DeserializeState state, ref string? value) {
		if (buffer.TryReadNil()) {
			value = null;
			return;
		}

		var length = buffer.ReadStringHeader();
		if (length == 0) {
			value = string.Empty;
			return;
		}

		if (!buffer.TryGetSpan(length, out var utf8))
			throw new MessagePackSerializationException("Unexpected end of data while reading a string");
		utf8 = utf8[..length];
		value = length <= MaxInternedBytes ? _pool.GetOrAdd(utf8) : Encoding.UTF8.GetString(utf8);
		buffer.Advance(length);
	}
}
#pragma warning restore MsgPack104

/// <summary>Serves <see cref="PragueStringInterningFormatter{TWriteBuffer, TReadBuffer}"/> for <see cref="string"/>, sharing one pool.</summary>
internal sealed partial class PragueStringInterningFormatterFactory : MessagePackFormatterFactory {
	private readonly Utf8WeakInternPool _pool = new();

	internal Utf8WeakInternPool Pool => _pool;

	public override object? CreateFormatter<TWriteBuffer, TReadBuffer>(Type type)
		=> type == typeof(string) ? new PragueStringInterningFormatter<TWriteBuffer, TReadBuffer>(_pool) : null;
}

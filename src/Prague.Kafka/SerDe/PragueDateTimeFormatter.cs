namespace Prague.Kafka.SerDe;

using MessagePack;
using SerializerFoundation;

/// <summary>
///   Prague's DateTime formatter: writes the legacy native int64 encoding
///   (DateTime.ToBinary) for byte-for-byte compatibility with topics produced
///   under the historical host-side NativeDateTimeResolver configuration.
///   Reads both that legacy int64 encoding AND the standard MessagePack
///   timestamp ext format, dispatched by inspecting the next token.
///   <c>DateTime?</c> is served by MessagePack's generic nullable formatter,
///   which resolves this one through the resolver chain.
/// </summary>
public sealed partial class PragueDateTimeFormatter<TWriteBuffer, TReadBuffer> : IMessagePackFormatter<TWriteBuffer, TReadBuffer, DateTime> {
	public void Initialize(MessagePackFormatterResolver resolver) { }

	// v4's WriteInt64 picks the smallest encoding (uint64 0xcf for most ToBinary values);
	// the forced form keeps v3's fixed int64 0xd3 bytes.
	public void Serialize(ref TWriteBuffer buffer, ref SerializeState state, DateTime value)
		=> buffer.WriteForcedInt64(value.ToBinary());

	// MsgPack104: this formatter IS the DateTime formatter, so reading the timestamp directly is intended.
#pragma warning disable MsgPack104
	public void Deserialize(ref TReadBuffer buffer, ref DeserializeState state, ref DateTime value) {
		if (!buffer.TryPeek(out var code))
			throw new MessagePackSerializationException("Unexpected end of data while reading DateTime");
		value = MessagePackCode.ToMessagePackType(code) switch {
			MessagePackType.Integer   => DateTime.FromBinary(buffer.ReadInt64()),
			MessagePackType.Extension => buffer.ReadTimestamp(),
			var t => throw new MessagePackSerializationException(
				$"Unexpected MessagePack token while reading DateTime: {t}")
		};
	}
#pragma warning restore MsgPack104
}

/// <summary>
///   Serves <see cref="PragueDateTimeFormatter{TWriteBuffer, TReadBuffer}"/> for <see cref="DateTime"/>
///   and falls through (returns null) for everything else so it can sit ahead of the default chain.
/// </summary>
public sealed partial class PragueDateTimeFormatterFactory : MessagePackFormatterFactory {
	public static readonly PragueDateTimeFormatterFactory Instance = new();

	private PragueDateTimeFormatterFactory() { }

	public override object? CreateFormatter<TWriteBuffer, TReadBuffer>(Type type)
		=> type == typeof(DateTime) ? new PragueDateTimeFormatter<TWriteBuffer, TReadBuffer>() : null;
}

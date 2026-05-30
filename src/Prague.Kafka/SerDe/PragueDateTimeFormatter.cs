namespace Prague.Kafka.SerDe;

using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Prague's DateTime formatter: writes the legacy native int64 encoding
///   (DateTime.ToBinary) for byte-for-byte compatibility with topics produced
///   under the historical host-side NativeDateTimeResolver configuration.
///   Reads both that legacy int64 encoding AND the standard MessagePack
///   timestamp ext format, dispatched by inspecting the next token.
/// </summary>
public sealed class PragueDateTimeFormatter : IMessagePackFormatter<DateTime> {
	public static readonly PragueDateTimeFormatter Instance = new();

	private PragueDateTimeFormatter() { }

	public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options)
		=> writer.WriteInt64(value.ToBinary());

	public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
		reader.NextMessagePackType switch {
			MessagePackType.Integer   => DateTime.FromBinary(reader.ReadInt64()),
			MessagePackType.Extension => reader.ReadDateTime(),
			var t => throw new MessagePackSerializationException(
				$"Unexpected MessagePack token while reading DateTime: {t}")
		};
}

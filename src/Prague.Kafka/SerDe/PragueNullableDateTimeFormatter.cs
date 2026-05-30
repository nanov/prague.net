namespace Prague.Kafka.SerDe;

using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Nullable wrapper around <see cref="PragueDateTimeFormatter"/>. Needed
///   because MessagePack-CSharp's standard nullable formatters bind to the
///   underlying formatter at construction time and bypass <c>options.Resolver</c>
///   at deserialize time, so without this an ext-encoded <c>DateTime?</c> hits
///   <c>NativeDateTimeFormatter</c> (int64-only) and throws.
/// </summary>
public sealed class PragueNullableDateTimeFormatter : IMessagePackFormatter<DateTime?> {
	public static readonly PragueNullableDateTimeFormatter Instance = new();

	private PragueNullableDateTimeFormatter() { }

	public void Serialize(ref MessagePackWriter writer, DateTime? value, MessagePackSerializerOptions options) {
		if (value.HasValue) {
			PragueDateTimeFormatter.Instance.Serialize(ref writer, value.Value, options);
		} else {
			writer.WriteNil();
		}
	}

	public DateTime? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
		if (reader.TryReadNil()) return null;
		return PragueDateTimeFormatter.Instance.Deserialize(ref reader, options);
	}
}

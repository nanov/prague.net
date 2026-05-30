namespace Prague.Kafka.SerDe;

using MessagePack;

public static class CacheSerde<T> {
	/// <summary>
	///   Zero-copy deserialize straight off a consume-path native span (see <see cref="SpanMessagePackDeserializer"/>).
	///   The span must stay alive for the duration of the call.
	/// </summary>
	internal static T DeserializeFromSpan(ReadOnlySpan<byte> data)
		=> SpanMessagePackDeserializer.Deserialize<T>(data);

	public static byte[] Serialize(T value)
		=> MessagePackSerializer.Serialize<T>(value, PragueMessagePack.Options);
}

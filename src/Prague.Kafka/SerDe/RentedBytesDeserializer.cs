namespace Prague.Kafka.SerDe;

using System.Buffers;
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

	/// <summary>
	///   Serialize into a reusable <see cref="IBufferWriter{T}"/> instead of allocating a fresh
	///   <c>byte[]</c> — the produce path rents a pooled writer and hands its span to RawProduce
	///   (which copies synchronously).
	/// </summary>
	internal static void SerializeInto(T value, IBufferWriter<byte> writer) {
		var mpWriter = new MessagePackWriter(writer);
		MessagePackSerializer.Serialize(ref mpWriter, value, PragueMessagePack.Options);
		mpWriter.Flush();
	}
}

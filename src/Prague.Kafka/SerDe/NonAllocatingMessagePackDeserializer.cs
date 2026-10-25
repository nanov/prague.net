namespace Prague.Kafka.SerDe;

using System.Runtime.CompilerServices;
using MessagePack;
using SerializerFoundation;

/// <summary>
///   Zero-allocation MessagePack deserialization straight off the consume-path native span.
///   MessagePack v4 reads a <see cref="ReadOnlySpan{T}"/> directly, so the span pointing into
///   librdkafka's buffer is fed in as-is. The span MUST stay alive for the (synchronous)
///   duration of the deserialize call.
/// </summary>
internal static class SpanMessagePackDeserializer {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Deserialize<T>(ReadOnlySpan<byte> data) {
		if (data.IsEmpty)
			return default!;
		return MessagePackSerializer.Deserialize<T>(data, PragueMessagePack.Options);
	}

	/// <summary>
	///   Zero-copy deserialize that also reports how many bytes the MessagePack reader consumed
	///   (used for the "exact" header path that rejects trailing bytes).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Deserialize<T>(ReadOnlySpan<byte> data, out int bytesRead) {
		if (data.IsEmpty) {
			bytesRead = 0;
			return default!;
		}

		var buffer = new ReadOnlySpanReadBuffer(data);
		var value = MessagePackSerializer.Deserialize<ReadOnlySpanReadBuffer, T>(ref buffer, PragueMessagePack.Options);
		bytesRead = (int)buffer.BytesConsumed;
		return value;
	}
}

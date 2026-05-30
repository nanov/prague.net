namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MessagePack;

/// <summary>
///   Zero-allocation MessagePack deserialization straight off the consume-path native span.
///
///   MessagePack's public <see cref="MessagePackSerializer.Deserialize{T}(System.ReadOnlyMemory{byte}, MessagePackSerializerOptions, System.Threading.CancellationToken)"/>
///   needs a <see cref="System.ReadOnlyMemory{T}"/>, not a <see cref="ReadOnlySpan{T}"/> — so a raw span
///   pointing into librdkafka's buffer cannot be fed in directly. <see cref="ReusableByteMemoryManager"/>
///   is re-pointed at each incoming span and hands back its <see cref="MemoryManager{T}.Memory"/>, letting
///   MessagePack read the native buffer with zero copy and zero allocation (no pooled byte copy).
///
///   Safety rests on invariants that hold here but not in general:
///   the source span MUST stay alive for the (synchronous) duration of the deserialize call, and the
///   manager is NOT thread-safe — one instance per consume thread (kept <c>[ThreadStatic]</c>).
/// </summary>
internal static class SpanMessagePackDeserializer {
	[ThreadStatic] private static ReusableByteMemoryManager? _manager;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Deserialize<T>(ReadOnlySpan<byte> data) {
		if (data.IsEmpty)
			return default!;
		var manager = _manager ??= new ReusableByteMemoryManager();
		return MessagePackSerializer.Deserialize<T>(manager.SetSpan(data), PragueMessagePack.Options);
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

		var manager = _manager ??= new ReusableByteMemoryManager();
		return MessagePackSerializer.Deserialize<T>(manager.SetSpan(data), PragueMessagePack.Options, out bytesRead);
	}

	/// <summary>
	///   A <see cref="MemoryManager{T}"/> whose backing pointer is re-aimed at an arbitrary span per call.
	///   Holds mutable pointer state — never share across threads, never store the returned memory.
	/// </summary>
	private sealed unsafe class ReusableByteMemoryManager : MemoryManager<byte> {
		private byte* _pointer;
		private int _length;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Memory<byte> SetSpan(ReadOnlySpan<byte> source) {
			_pointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source));
			_length = source.Length;
			return Memory;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override Span<byte> GetSpan() => new(_pointer, _length);

		public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

		public override void Unpin() { }

		protected override void Dispose(bool disposing) { }
	}
}

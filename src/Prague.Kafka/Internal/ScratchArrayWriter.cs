namespace Prague.Kafka.Internal;

using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
///   A minimal <see cref="IBufferWriter{T}"/> over a single managed <c>byte[]</c>, grown in place by
///   doubling. Intended for small copy-out payloads (Kafka keys/values) where the bytes are read
///   synchronously (librdkafka MSG_F_COPY) and the writer is reused across <see cref="Reset"/> cycles
///   — zero allocations on the Nth use. Ported (BCL-only) from the reference Kafka lib.
/// </summary>
internal sealed class ScratchArrayWriter : IBufferWriter<byte> {
	private const int DefaultInitialCapacity = 128;

	private byte[] _buffer;
	private int _written;

	public ScratchArrayWriter(int initialCapacity = DefaultInitialCapacity) {
		if (initialCapacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(initialCapacity));
		_buffer = new byte[initialCapacity];
		_written = 0;
	}

	/// <summary>Bytes written since the last <see cref="Reset"/>.</summary>
	public int Length => _written;

	/// <summary>The bytes written. Valid until the next write/Reset (a grow may relocate the array).</summary>
	public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<byte> GetSpan(int sizeHint = 0) {
		if (sizeHint < 0)
			throw new ArgumentOutOfRangeException(nameof(sizeHint));
		EnsureCapacity(sizeHint == 0 ? 1 : sizeHint);
		return _buffer.AsSpan(_written);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Memory<byte> GetMemory(int sizeHint = 0) {
		if (sizeHint < 0)
			throw new ArgumentOutOfRangeException(nameof(sizeHint));
		EnsureCapacity(sizeHint == 0 ? 1 : sizeHint);
		return _buffer.AsMemory(_written);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Advance(int count) {
		if (count == 0)
			return;
		if ((uint)count > (uint)(_buffer.Length - _written))
			throw new InvalidOperationException($"Advance({count}) exceeds remaining capacity {_buffer.Length - _written}.");
		_written += count;
	}

	/// <summary>Clears the written-length counter; keeps the buffer for reuse.</summary>
	public void Reset() => _written = 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void EnsureCapacity(int needed) {
		if (_buffer.Length - _written < needed)
			Grow(needed);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow(int needed) {
		var newSize = _buffer.Length;
		var required = _written + needed;
		while (newSize < required) {
			if (newSize >= int.MaxValue / 2) {
				newSize = required;
				break;
			}

			newSize *= 2;
		}

		Array.Resize(ref _buffer, newSize);
	}
}

/// <summary>
///   Per-type, per-thread cache of one <see cref="ScratchArrayWriter"/>, with a monotonic size hint.
///   Keyed by <typeparamref name="T"/> so a key writer and a value writer (different T) draw from
///   independent thread-local slots and can be held simultaneously.
/// </summary>
internal static class ScratchArrayWriterManager<T> {
	private const int DefaultInitialHint = 128;
	private const int MaxHint = 1 << 20; // 1 MiB safety cap

	private static int _sizeHint = DefaultInitialHint;
	[ThreadStatic] private static ScratchArrayWriter? _cached;

	public static ScratchArrayWriter Rent() {
		var writer = _cached;
		if (writer is not null) {
			_cached = null;
			return writer;
		}

		return new ScratchArrayWriter(_sizeHint);
	}

	public static void Return(ScratchArrayWriter writer) {
		var observed = writer.Length;
		if (observed > _sizeHint) // advisory; a lost race just under-sizes the next fresh writer
			_sizeHint = observed > MaxHint ? MaxHint : observed;
		writer.Reset();
		_cached ??= writer;
	}
}

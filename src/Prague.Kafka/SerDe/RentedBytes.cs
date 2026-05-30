namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Runtime.CompilerServices;
using IO;

public struct RentedBytes {
	private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
	private readonly byte[]? _bytes;
	private readonly uint _length;

	public bool IsNull => _bytes is null;

	public static RentedBytes Irrelevant()
		=> new(true, ReadOnlySpan<byte>.Empty);

	public ReadOnlySpan<byte> AsSpan()
		=> _bytes is null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_bytes, 0, (int)_length);

	public ReadOnlyMemory<byte> AsMemory()
		=> _bytes is null ? Memory<byte>.Empty : new Memory<byte>(_bytes, 0, (int)_length);

	public RentedBytes(bool isNUll, ReadOnlySpan<byte> bytes) {
		if (isNUll) {
			_bytes = null;
			_length = 0;
			return;
		}

		_bytes = _pool.Rent(bytes.Length);
		bytes.CopyTo(_bytes);
		_length = (uint)bytes.Length;
	}

	public void Dispose() {
		if (_bytes is not null)
			_pool.Return(_bytes);
	}
}

internal struct RentedBytesWithHandler {
	public readonly KafkaCacheHandler? Handler;
	public readonly bool IsFiltered;

	private readonly ReadOnlyMemory<byte> _memory;
	private readonly bool _pooled;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsFound()
		=> Handler is not null;

	public bool IsNull => _memory.IsEmpty;

	internal static RentedBytesWithHandler NotFound()
		=> new(null!, true, default, true);

	internal static RentedBytesWithHandler Filtered()
		=> new(null!, true, default, true);

	public ReadOnlyMemory<byte> AsMemory() => _memory;

	public RentedBytesWithHandler(KafkaCacheHandler handler, bool isNUll, ReadOnlySpan<byte> bytes, bool isFiltered) {
		IsFiltered = isFiltered;
		Handler = handler;
		_pooled = false;

		if (isNUll || isFiltered) {
			_memory = default;
			return;
		}

		var memory = handler.KeyRingBuffer.Rent(bytes.Length, out _pooled);
		bytes.CopyTo(memory.Span);
		_memory = memory;
	}

	public void Dispose() {
		if (_pooled)
#if DETECT_CORRUPTION
			Handler!.KeyRingBuffer.Return(_memory);
#else
			Handler!.KeyRingBuffer.Return(_memory.Length);
#endif
	}
}

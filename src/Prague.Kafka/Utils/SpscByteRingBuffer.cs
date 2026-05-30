namespace Prague.Kafka.Utils;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Single-producer single-consumer ring buffer for byte allocations.
/// Producer (consumer thread) calls Rent, consumer (channel loop thread) calls Return.
/// All allocations and frees happen in strict FIFO order.
/// Backed by unmanaged memory — zero GC pressure.
/// </summary>
internal sealed unsafe class SpscByteRingBuffer : IDisposable {
	private readonly byte* _buffer;
	private readonly int _capacity;
	private readonly UnmanagedRegionMemoryManager _manager;

	private volatile int _head; // write cursor — only producer advances
	private volatile int _tail; // free cursor — only consumer advances

	private const int GcPressureThreshold = 85_000; // LOH threshold

	public SpscByteRingBuffer(int capacity = 65536) {
		_capacity = capacity;

		_buffer = (byte*)NativeMemory.AllocZeroed((nuint)_capacity);
		_manager = new UnmanagedRegionMemoryManager(_buffer, _capacity);

		if (_capacity >= 85_000)
			GC.AddMemoryPressure(_capacity);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Memory<byte> Rent(int size, out bool pooled) {
		var head = _head;
		var tail = _tail;

		if (head >= tail) {
			var endSpace = _capacity - head;
			if (endSpace >= size) {
				var newHead = head + size;
				if (newHead < _capacity || tail > 0) {
					_head = newHead < _capacity ? newHead : 0;
					pooled = true;
					return _manager.Memory.Slice(head, size);
				}
			}

			if (tail > size) {
				_head = size;
				pooled = true;
				return _manager.Memory.Slice(0, size);
			}
		}
		else {
			if (tail - head > size) {
				_head = head + size;
				pooled = true;
				return _manager.Memory.Slice(head, size);
			}
		}

		pooled = false;
		return new byte[size];
	}

	/// <summary>
	/// Free the oldest allocation of the given size. Must be called in FIFO order.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Return(int size) {
		var tail = _tail;
		var newTail = tail + size;

		if (newTail > _capacity) {
			_tail = size;
			return;
		}

		_tail = newTail < _capacity ? newTail : 0;
	}

	public void Return(ReadOnlyMemory<byte> memory) {
#if DETECT_CORRUPTION
		fixed (byte* ptr = memory.Span) {
			// Check if this pointer actually resides within our unmanaged block
			if (ptr >= _buffer && ptr < _buffer + _capacity) {
				var tail = _tail;
				var expectedOffset = tail + memory.Length > _capacity ? 0 : tail;
				Debug.Assert(ptr == _buffer + expectedOffset,
					$"FIFO Violation: Memory returned out of order. Expected offset {expectedOffset}, got {ptr - _buffer}");
			} else
				Debug.Assert(false, "Returning memory that is not backed by our unmanaged block");
		}
#endif
		Return(memory.Length);
	}

	public void Dispose() {
		NativeMemory.Free(_buffer);
		if (_capacity >= GcPressureThreshold)
			GC.RemoveMemoryPressure(_capacity);
	}

	private sealed class UnmanagedRegionMemoryManager : MemoryManager<byte> {
		private readonly byte* _pointer;
		private readonly int _length;

		public UnmanagedRegionMemoryManager(byte* pointer, int length) {
			_pointer = pointer;
			_length = length;
		}

		public override Span<byte> GetSpan() => new(_pointer, _length);

		public override MemoryHandle Pin(int elementIndex = 0) =>
			new(_pointer + elementIndex);

		public override void Unpin() { }

		protected override void Dispose(bool disposing) { }
	}
}

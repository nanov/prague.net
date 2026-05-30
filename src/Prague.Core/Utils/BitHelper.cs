namespace Prague.Core.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal ref struct BitHelper {
	private const int IntSize = sizeof(int) * 8;
	private readonly Span<int> _span;

	[UnscopedRef]
	public ReadOnlySpan<int> Bits => _span;   // expose the bitmap

	internal BitHelper(Span<int> span, bool clear) {
		if (clear) span.Clear();

		_span = span;
	}

	internal void MarkBit(int bitPosition) {
		var bitArrayIndex = bitPosition / IntSize;
		if ((uint)bitArrayIndex < (uint)_span.Length) _span[bitArrayIndex] |= 1 << (bitPosition % IntSize);
	}

	internal void UnmarkBit(int bitPosition) {
		var bitArrayIndex = bitPosition / IntSize;
		if ((uint)bitArrayIndex < (uint)_span.Length) _span[bitArrayIndex] &= ~(1 << (bitPosition % IntSize));
	}

	internal bool IsMarked(int bitPosition) {
		var bitArrayIndex = bitPosition / IntSize;
		return
			(uint)bitArrayIndex < (uint)_span.Length &&
			(_span[bitArrayIndex] & (1 << (bitPosition % IntSize))) != 0;
	}

	internal int FindFirstUnmarked(int startPosition = 0) {
		var i = startPosition;
		for (var bi = i / IntSize; (uint)bi < (uint)_span.Length; bi = ++i / IntSize)
			if ((_span[bi] & (1 << (i % IntSize))) == 0)
				return i;

		return -1;
	}

	internal int FindFirstMarked(int startPosition = 0) {
		var i = startPosition;
		for (var bi = i / IntSize; (uint)bi < (uint)_span.Length; bi = ++i / IntSize)
			if ((_span[bi] & (1 << (i % IntSize))) != 0)
				return i;

		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Intersect(scoped ref BitHelper other)
		=> Intersect(other._span);

	internal void Intersect(scoped ReadOnlySpan<int> other) {
		var bits = _span;
		var len = Math.Min(bits.Length, other.Length);
		var i = 0;

		if (Vector.IsHardwareAccelerated) {
			ref var bRef = ref MemoryMarshal.GetReference(bits);
			ref var oRef = ref MemoryMarshal.GetReference(other);
			var lastVec = len - Vector<int>.Count;
			for (; i <= lastVec; i += Vector<int>.Count) {
				var vb = Vector.LoadUnsafe(ref Unsafe.Add(ref bRef, i));
				var vo = Vector.LoadUnsafe(ref Unsafe.Add(ref oRef, i));
				(vb & vo).StoreUnsafe(ref Unsafe.Add(ref bRef, i));
			}
		}
		for (; i < len; i++)
			bits[i] &= other[i];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Union(scoped ref BitHelper other)
		=> Union(other._span);

	internal void Union(scoped ReadOnlySpan<int> span) {
		var bits = _span;
		var otherBits = span;
		var len = bits.Length;
		var i = 0;

		if (Vector.IsHardwareAccelerated && len >= Vector<int>.Count) {
			ref var bRef = ref MemoryMarshal.GetReference(bits);
			ref var oRef = ref MemoryMarshal.GetReference(otherBits);
			var lastVec = len - Vector<int>.Count;
			for (; i <= lastVec; i += Vector<int>.Count) {
				var vb = Vector.LoadUnsafe(ref Unsafe.Add(ref bRef, i));
				var vo = Vector.LoadUnsafe(ref Unsafe.Add(ref oRef, i));
				(vb | vo).StoreUnsafe(ref Unsafe.Add(ref bRef, i));
			}
		}
		for (; i < len; i++)
			bits[i] |= otherBits[i];
	}

	/// <summary>How many ints must be allocated to represent n bits. Returns (n+31)/32, but avoids overflow.</summary>
	internal static int ToIntArrayLength(int n)
		=> n > 0 ? (n - 1) / IntSize + 1 : 0;

	internal void Clear() => _span.Clear();
}
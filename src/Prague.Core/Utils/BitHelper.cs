namespace Prague.Core.Utils;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal ref struct BitHelper {
	private const int IntSize = sizeof(int) * 8;
	// Word addressing for the scans: position >> WordShift is the word, position & WordMask the
	// bit. Signed / and % by 32 are NOT free — the JIT emits sign-correction around them — and
	// the scans have already established the position is non-negative.
	private const int WordShift = 5;
	private const int WordMask = IntSize - 1;
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

	/// <summary>
	///   Index of the first CLEAR bit at or after <paramref name="startPosition"/>, or -1 if
	///   there is none. Scanned a word at a time: an all-marked word is rejected by a single
	///   compare instead of 32 bit tests, and the hit index comes from one TrailingZeroCount.
	///   The scan is bounded by the word count only — bits in the tail of the final word past
	///   the caller's logical element count read as clear and ARE returned. The ValueSet
	///   intersect sweeps depend on that and apply their own upper bound.
	/// </summary>
	internal int FindFirstUnmarked(int startPosition = 0) {
		Debug.Assert(startPosition >= 0, "scan positions are produced by the caller's previous hit + 1");

		var length = _span.Length;
		var wordIndex = startPosition >> WordShift;
		if ((uint)wordIndex >= (uint)length)
			return -1;

		ref var words = ref MemoryMarshal.GetReference(_span);
		var current = (uint)Unsafe.Add(ref words, wordIndex);
		var bitOffset = startPosition & WordMask;

		// Fast path: startPosition itself is clear. When clear bits are dense — a strongly
		// narrowing intersect, where few slots survive — this is the overwhelmingly common
		// outcome, and skipping the mask/complement/TZCNT chain keeps this case at parity
		// with the old bit-at-a-time scan instead of 1.5x slower than it.
		if ((current & (1u << bitOffset)) == 0)
			return startPosition;

		// Set the bits below startPosition so they can never be reported as clear.
		var word = current | ((1u << bitOffset) - 1u);

		while (word == uint.MaxValue) {
			if (++wordIndex >= length)
				return -1;

			word = (uint)Unsafe.Add(ref words, wordIndex);
		}

		return (wordIndex << WordShift) + BitOperations.TrailingZeroCount(~word);
	}

	/// <summary>
	///   Index of the first MARKED bit at or after <paramref name="startPosition"/>, or -1 if
	///   there is none. Word-at-a-time counterpart of <see cref="FindFirstUnmarked"/>.
	/// </summary>
	internal int FindFirstMarked(int startPosition = 0) {
		Debug.Assert(startPosition >= 0, "scan positions are produced by the caller's previous hit + 1");

		var length = _span.Length;
		var wordIndex = startPosition >> WordShift;
		if ((uint)wordIndex >= (uint)length)
			return -1;

		ref var words = ref MemoryMarshal.GetReference(_span);
		var current = (uint)Unsafe.Add(ref words, wordIndex);
		var bitOffset = startPosition & WordMask;

		// Fast path — see FindFirstUnmarked.
		if ((current & (1u << bitOffset)) != 0)
			return startPosition;

		// Clear the bits below startPosition so they can never be reported as marked.
		var word = current & ~((1u << bitOffset) - 1u);

		while (word == 0) {
			if (++wordIndex >= length)
				return -1;

			word = (uint)Unsafe.Add(ref words, wordIndex);
		}

		return (wordIndex << WordShift) + BitOperations.TrailingZeroCount(word);
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
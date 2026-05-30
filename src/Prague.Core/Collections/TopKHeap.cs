namespace Prague.Core.Collections;

using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
///   An ephemeral max-heap of fixed capacity K that keeps only the top-K smallest items.
///   Used for heap-select: iterate M candidates, push each, the heap evicts the worst
///   (largest) automatically. After all candidates are pushed, call <see cref="DrainSorted"/>
///   to extract results in ascending order.
///
///   Max-heap property: root = largest score among the K best. Any new candidate with a
///   score &lt; root evicts the root and sifts down. Candidates &gt;= root are discarded in O(1).
///
///   Complexity: O(M log K) for M pushes into a heap of size K, O(K log K) final drain.
///   Memory: single pooled array of K elements, zero per-push allocation.
///
///   Thread safety: not thread-safe. Intended for single-query ephemeral use.
/// </summary>
[SkipLocalsInit]
internal struct TopKHeap<TKey, TScore> : IDisposable
	where TScore : IComparable<TScore> {
	private (TScore score, TKey key)[] _buffer;
	private readonly int _capacity;
	private int _count;
	private bool _heapified;

	/// <summary>
	///   Creates a heap that will keep the top <paramref name="k"/> smallest items.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TopKHeap(int k) {
		_capacity = k;
		_buffer = ArrayPool<(TScore, TKey)>.Shared.Rent(k);
		_count = 0;
		_heapified = false;
	}

	public readonly int Count {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	/// <summary>
	///   Push a candidate. If the heap is not yet full, the item is appended.
	///   Once full, the heap is built (if not already) and items worse than the
	///   root (i.e. score &gt;= root score) are discarded in O(1).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Push(TKey key, TScore score) {
		if (_count < _capacity) {
			_buffer[_count++] = (score, key);
			if (_count == _capacity) {
				BuildMaxHeap();
				_heapified = true;
			}

			return;
		}

		// Heap is full — only accept if strictly better (smaller) than the worst kept item
		if (score.CompareTo(_buffer[0].score) >= 0)
			return;

		_buffer[0] = (score, key);
		SiftDown(0);
	}

	/// <summary>
	///   Drain all collected items into <paramref name="destination"/> in ascending score order.
	///   Returns the number of items written. The heap is empty after this call.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int DrainSorted(TKey[] destination) {
		var n = _count;
		if (n == 0) return 0;

		// If we never filled to capacity, the buffer is unordered — heapify first
		if (!_heapified)
			BuildMaxHeap();

		// Heapsort: repeatedly extract max to the end, shrink heap, sift down
		for (var i = n - 1; i > 0; i--) {
			(_buffer[0], _buffer[i]) = (_buffer[i], _buffer[0]);
			SiftDownRange(0, i);
		}

		// Copy keys in sorted order (ascending)
		for (var i = 0; i < n; i++)
			destination[i] = _buffer[i].key;

		_count = 0;
		_heapified = false;
		return n;
	}

	/// <summary>
	///   Drain all collected items into <paramref name="destination"/> in ascending score order,
	///   applying a skip offset first.
	///   Returns the number of items written.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int DrainSorted(TKey[] destination, int skip) {
		var n = _count;
		if (n == 0 || skip >= n) return 0;

		if (!_heapified)
			BuildMaxHeap();

		for (var i = n - 1; i > 0; i--) {
			(_buffer[0], _buffer[i]) = (_buffer[i], _buffer[0]);
			SiftDownRange(0, i);
		}

		var written = n - skip;
		for (var i = 0; i < written; i++)
			destination[i] = _buffer[i + skip].key;

		_count = 0;
		_heapified = false;
		return written;
	}

	public void Dispose() {
		if (_buffer != null!) {
			ArrayPool<(TScore, TKey)>.Shared.Return(_buffer);
			_buffer = null!;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void BuildMaxHeap() {
		for (var i = _count / 2 - 1; i >= 0; i--)
			SiftDown(i);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SiftDown(int i) {
		var buf = _buffer;
		var size = _count;
		while (true) {
			var largest = i;
			var left = 2 * i + 1;
			var right = 2 * i + 2;

			if (left < size && buf[left].score.CompareTo(buf[largest].score) > 0)
				largest = left;
			if (right < size && buf[right].score.CompareTo(buf[largest].score) > 0)
				largest = right;

			if (largest == i) return;

			(buf[i], buf[largest]) = (buf[largest], buf[i]);
			i = largest;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SiftDownRange(int i, int size) {
		var buf = _buffer;
		while (true) {
			var largest = i;
			var left = 2 * i + 1;
			var right = 2 * i + 2;

			if (left < size && buf[left].score.CompareTo(buf[largest].score) > 0)
				largest = left;
			if (right < size && buf[right].score.CompareTo(buf[largest].score) > 0)
				largest = right;

			if (largest == i) return;

			(buf[i], buf[largest]) = (buf[largest], buf[i]);
			i = largest;
		}
	}
}

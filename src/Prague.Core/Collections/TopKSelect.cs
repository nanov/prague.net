namespace Prague.Core.Collections;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///   Zero-allocation selection primitives over caller-owned buffers, shared by the bounded
///   sorted-paging containers. Two plans:
///   <list type="bullet">
///     <item>
///       <b>Small prefix</b> (K = skip + take far below N): <see cref="Push{T,TComparer}"/> keeps the K
///       smallest items per <c>TComparer</c> in a max-heap (root = worst kept item).
///       Candidates not better than the root are rejected in O(1); accepted candidates replace the
///       root and sift down in O(log K). <see cref="DrainAscending{T,TComparer}"/> heapsorts the kept
///       items in place, ascending. O(M log K) for M candidates, O(K log K) drain.
///     </item>
///     <item>
///       <b>Large prefix</b> (every matched row collected): <see cref="SelectPage{T,TComparer}(Span{T},int,int,TComparer)"/>
///       partitions the buffer around the page bounds with introselect and sorts only the page:
///       O(N + take log take) expected, O(N log N) worst case, instead of a full O(N log N) sort.
///     </item>
///   </list>
///   Pure static helpers over caller-owned state — no rents, no ownership, so the struct-copy
///   double-return hazard that killed the old TopKHeap cannot occur. Inside the algorithms elements
///   are reached by reference (<see cref="Unsafe.Add{T}(ref T, int)"/>) since every index is bounded by
///   the routine's own arguments; the buffer writes at the container boundary stay bounds-checked.
/// </summary>
internal static class TopKSelect {
	// Below this length partitioning stops paying for itself; insertion sort finishes the range.
	private const int InsertionSortThreshold = 16;

	// ── Small prefix: bounded max-heap ────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Push<T, TComparer>(T[] buffer, ref int count, ref bool heapified, int k, T item, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		if (k <= 0)
			return;

		if (count < k) {
			buffer[count++] = item;
			if (count == k) {
				BuildMaxHeap(ref MemoryMarshal.GetArrayDataReference(buffer), count, comparer);
				heapified = true;
			}

			return;
		}

		// Heap is full — accept only items strictly better (smaller) than the current worst.
		ref var root = ref MemoryMarshal.GetArrayDataReference(buffer);
		if (comparer.Compare(item, root) >= 0)
			return;

		root = item;
		SiftDown(ref root, 0, count, comparer);
	}

	/// <summary>Heapsorts the kept items in place (ascending). Returns the row count; resets state.</summary>
	internal static int DrainAscending<T, TComparer>(T[] buffer, ref int count, ref bool heapified, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		var n = count;
		if (n == 0)
			return 0;

		Debug.Assert(n <= buffer.Length, "kept count exceeds the buffer");
		ref var root = ref MemoryMarshal.GetArrayDataReference(buffer);
		if (!heapified)
			BuildMaxHeap(ref root, n, comparer);

		SortBuiltHeap(ref root, n, comparer);
		count = 0;
		heapified = false;
		return n;
	}

	// ── Large prefix: introselect the page bounds, sort only the page ─────────

	/// <summary>
	///   Rearranges <paramref name="values"/> so that <c>values[skip .. skip + page)</c> holds the rows of
	///   rank <c>[skip, skip + take)</c> in ascending <typeparamref name="TComparer"/> order, where
	///   <c>page = min(take, values.Length - skip)</c> is returned (0 when the page lies past the end).
	///   Rows outside the page end up in unspecified order. Expected O(N + page log page).
	/// </summary>
	internal static int SelectPage<T, TComparer>(Span<T> values, int skip, int take, TComparer comparer)
		where TComparer : struct, IComparer<T>
		=> SelectPage(values, skip, take, comparer, DepthLimit(values.Length));

	/// <summary>Test seam: <paramref name="depthLimit"/> bounds the partitioning depth (0 forces the fallbacks).</summary>
	internal static int SelectPage<T, TComparer>(Span<T> values, int skip, int take, TComparer comparer, int depthLimit)
		where TComparer : struct, IComparer<T> {
		var length = values.Length;
		if (skip < 0 || take <= 0 || skip >= length)
			return 0;

		var page = Math.Min(take, length - skip);
		ref var first = ref MemoryMarshal.GetReference(values);
		if (skip > 0) {
			// Ranks [0, skip) settle before index skip; the page and everything after it settle behind.
			Select(ref first, length, skip, depthLimit, comparer);
		}

		ref var pageStart = ref Unsafe.Add(ref first, skip);
		var tail = length - skip;
		if (page < tail) {
			// Of the tail, bring the page smallest to the front: rank page - 1 is the page's last row.
			Select(ref pageStart, tail, page - 1, depthLimit, comparer);
		}

		IntroSort(ref pageStart, page, depthLimit, comparer);
		return page;
	}

	/// <summary>In-place introsort, ascending; constrained comparer calls avoid boxing.</summary>
	internal static void SortAscending<T, TComparer>(Span<T> values, TComparer comparer)
		where TComparer : struct, IComparer<T>
		=> SortAscending(values, comparer, DepthLimit(values.Length));

	/// <summary>Test seam: <paramref name="depthLimit"/> bounds the partitioning depth (0 forces the heapsort fallback).</summary>
	internal static void SortAscending<T, TComparer>(Span<T> values, TComparer comparer, int depthLimit)
		where TComparer : struct, IComparer<T> {
		if (values.Length > 1)
			IntroSort(ref MemoryMarshal.GetReference(values), values.Length, depthLimit, comparer);
	}

	// Same budget as the framework introsort: past it, partitioning is assumed adversarial.
	private static int DepthLimit(int length) => 2 * (BitOperations.Log2((uint)length) + 1);

	/// <summary>
	///   Introselect: leaves the item of rank <paramref name="rank"/> at index <paramref name="rank"/>,
	///   every item before it ordered not after it and every item after it ordered not before it.
	/// </summary>
	private static void Select<T, TComparer>(ref T first, int length, int rank, int depth, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		Debug.Assert((uint)rank < (uint)length, "rank out of range");
		while (length > InsertionSortThreshold) {
			if (depth == 0) {
				// Adversarial partitioning: sorting the range establishes the property in bounded time.
				IntroSort(ref first, length, DepthLimit(length), comparer);
				return;
			}

			depth--;
			Partition(ref first, length, comparer, out var lessCount, out var greaterStart);
			if (rank < lessCount) {
				length = lessCount;
			} else if (rank >= greaterStart) {
				first = ref Unsafe.Add(ref first, greaterStart);
				rank -= greaterStart;
				length -= greaterStart;
			} else {
				// The rank falls into the run of pivot-equal items between the parts: already final.
				return;
			}
		}

		InsertionSort(ref first, length, comparer);
	}

	private static void IntroSort<T, TComparer>(ref T first, int length, int depth, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		while (length > InsertionSortThreshold) {
			if (depth == 0) {
				// Adversarial partitioning: bounded O(N log N) fallback without another buffer.
				HeapSort(ref first, length, comparer);
				return;
			}

			depth--;
			Partition(ref first, length, comparer, out var lessCount, out var greaterStart);
			// Recurse into the smaller part; the larger one reuses this frame (stack depth O(log N)).
			var greaterCount = length - greaterStart;
			if (lessCount < greaterCount) {
				IntroSort(ref first, lessCount, depth, comparer);
				first = ref Unsafe.Add(ref first, greaterStart);
				length = greaterCount;
			} else {
				IntroSort(ref Unsafe.Add(ref first, greaterStart), greaterCount, depth, comparer);
				length = lessCount;
			}
		}

		InsertionSort(ref first, length, comparer);
	}

	/// <summary>
	///   Hoare partition around a median-of-three pivot. On return items <c>[0, lessCount)</c> compare not
	///   after the pivot, items <c>[greaterStart, length)</c> not before it, and any items in between equal
	///   it. Both parts are strictly shorter than the input, so callers always make progress.
	/// </summary>
	private static void Partition<T, TComparer>(ref T first, int length, TComparer comparer, out int lessCount, out int greaterStart)
		where TComparer : struct, IComparer<T> {
		var last = length - 1;
		ref var middle = ref Unsafe.Add(ref first, length >> 1);
		ref var end = ref Unsafe.Add(ref first, last);
		SwapIfGreater(ref first, ref middle, comparer);
		SwapIfGreater(ref first, ref end, comparer);
		SwapIfGreater(ref middle, ref end, comparer);
		var pivot = middle;

		// first <= pivot <= end bound both scans: lo stops on the middle item at the latest, hi on the first.
		var lo = 0;
		var hi = last;
		while (lo <= hi) {
			while (lo <= last && comparer.Compare(Unsafe.Add(ref first, lo), pivot) < 0)
				lo++;

			while (hi >= 0 && comparer.Compare(Unsafe.Add(ref first, hi), pivot) > 0)
				hi--;

			if (lo > hi)
				break;

			Swap(ref Unsafe.Add(ref first, lo), ref Unsafe.Add(ref first, hi));
			lo++;
			hi--;
		}

		lessCount = hi + 1;
		greaterStart = lo;
	}

	private static void InsertionSort<T, TComparer>(ref T first, int length, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		for (var i = 1; i < length; i++) {
			var item = Unsafe.Add(ref first, i);
			var j = i - 1;
			while (j >= 0 && comparer.Compare(Unsafe.Add(ref first, j), item) > 0) {
				Unsafe.Add(ref first, j + 1) = Unsafe.Add(ref first, j);
				j--;
			}

			Unsafe.Add(ref first, j + 1) = item;
		}
	}

	private static void HeapSort<T, TComparer>(ref T first, int length, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		BuildMaxHeap(ref first, length, comparer);
		SortBuiltHeap(ref first, length, comparer);
	}

	// Pops the max to the end repeatedly: a max-heap of n items becomes n items ascending.
	private static void SortBuiltHeap<T, TComparer>(ref T root, int n, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		for (var i = n - 1; i > 0; i--) {
			Swap(ref root, ref Unsafe.Add(ref root, i));
			SiftDown(ref root, 0, i, comparer);
		}
	}

	private static void BuildMaxHeap<T, TComparer>(ref T root, int size, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		for (var i = (size >> 1) - 1; i >= 0; i--)
			SiftDown(ref root, i, size, comparer);
	}

	private static void SiftDown<T, TComparer>(ref T root, int i, int size, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		while (true) {
			var largest = i;
			var left = 2 * i + 1;
			var right = left + 1;

			if (left < size && comparer.Compare(Unsafe.Add(ref root, left), Unsafe.Add(ref root, largest)) > 0)
				largest = left;

			if (right < size && comparer.Compare(Unsafe.Add(ref root, right), Unsafe.Add(ref root, largest)) > 0)
				largest = right;

			if (largest == i)
				return;

			Swap(ref Unsafe.Add(ref root, i), ref Unsafe.Add(ref root, largest));
			i = largest;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SwapIfGreater<T, TComparer>(ref T a, ref T b, TComparer comparer)
		where TComparer : struct, IComparer<T> {
		if (comparer.Compare(a, b) > 0)
			Swap(ref a, ref b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap<T>(ref T a, ref T b) {
		var t = a;
		a = b;
		b = t;
	}
}

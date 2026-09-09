namespace Prague.Core.Collections;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///   Stable ascending sort for the classic (unbounded) sorted pipeline: rows that compare equal keep
///   their encounter order, so a full result, a finite page over the same rows and the concatenation
///   of consecutive pages agree row for row without carrying an ordinal per row.
///   <para>
///   The single-span overload sorts a pooled array of row indices rather than the rows: a three-way
///   introsort partitions the indices by the row key alone, the block of indices whose rows equal the
///   pivot is finished by a plain integer sort (index order is encounter order, so that block is
///   already stable and costs no user comparison), and the two outer blocks recurse. One final pass
///   permutes the rows into place through one pooled buffer. Moving 4-byte indices instead of
///   references keeps the partition loops free of GC write barriers, which is what lets this match the
///   framework's unstable introsort on distinct keys and beat it on heavy ties.
///   </para>
///   <para>
///   The keyed overload — the joined pipeline's row dictionary — sorts the same index array and then
///   permutes the rows and the caller's items through one pooled buffer each. It used to sort the rows
///   in place with a Hoare introsort, carrying an ordinal per row, and repair the ties afterwards; on
///   tie-heavy joined rows that recursed into every run of equal rows — swapping references, a GC write
///   barrier per swap, for log k more levels on a run of k — and then gathered the whole result a second
///   time. Sorting indices instead halved the classic joined sort on the perf baseline (#67).
///   </para>
///   <para>
///   The comparer reaches every comparison as a type parameter, passed by reference through each
///   frame: a struct comparer is a direct, inlinable call per instantiation and nothing is allocated per
///   sort. Neither a <see cref="Comparison{T}"/> (which the framework's single-span sort builds, boxing a
///   struct comparer on the way — 88 B per query) nor the framework's keyed
///   <c>Span&lt;T&gt;.Sort(keys, items, comparer)</c> (which takes an <c>IComparer&lt;TKey&gt;</c> and so
///   boxes a struct — 24 B) is used. A class comparer dispatches through the interface, as the framework
///   sort does.
///   </para>
///   A comparison that throws leaves the rows untouched above <c>DirectInsertionThreshold</c> (the sort
///   orders indices until a final permutation that compares nothing) and in an unspecified permutation
///   below it, like the framework sort.
/// </summary>
internal static class StableSort {
	// Below this many rows the index machinery does not pay for itself: a stable insertion sort on the
	// rows themselves finishes the job.
	private const int DirectInsertionThreshold = 32;

	// Ranges this short are insertion-sorted instead of partitioned (the framework's threshold too).
	private const int InsertionSortThreshold = 16;

	/// <summary>
	///   Sorts <paramref name="values"/> ascending; equal items keep their relative order. A null
	///   <paramref name="comparer"/> means <see cref="Comparer{T}.Default"/>, as it does for
	///   <c>Span&lt;T&gt;.Sort</c>.
	/// </summary>
	internal static void Sort<T, TComparer>(Span<T> values, TComparer comparer)
		where TComparer : IComparer<T> {
		// `comparer is null` on a type parameter boxes a struct comparer to test it (24 B); the
		// Release JIT elides the box, the Debug JIT does not. Only reference comparers can be null.
		if (!typeof(TComparer).IsValueType && comparer is null) {
			Sort(values, Comparer<T>.Default);
			return;
		}

		Sort(values, ref comparer, DepthLimit(values.Length));
	}

	/// <summary>
	///   Sorts <paramref name="values"/> ascending and moves <paramref name="items"/> alongside, so
	///   <c>items[i]</c> still belongs to <c>values[i]</c> afterwards; equal values keep their order. A
	///   null <paramref name="comparer"/> means <see cref="Comparer{T}.Default"/>.
	/// </summary>
	internal static void Sort<T, TItem, TComparer>(Span<T> values, Span<TItem> items, TComparer comparer)
		where TComparer : IComparer<T>
		=> Sort(values, items, comparer, DepthLimit(values.Length));

	/// <summary>Test seam: <paramref name="depthLimit"/> bounds the partitioning depth (0 forces the heapsort fallback).</summary>
	internal static void Sort<T, TItem, TComparer>(Span<T> values, Span<TItem> items, TComparer comparer, int depthLimit)
		where TComparer : IComparer<T> {
		if (values.Length != items.Length)
			throw new ArgumentException("values and items must have the same length", nameof(items));

		var n = values.Length;
		if (n < 2)
			return;

		if (!typeof(TComparer).IsValueType && comparer is null) {
			Sort(values, items, Comparer<T>.Default, depthLimit);
			return;
		}

		if (n <= DirectInsertionThreshold) {
			InsertionSortRows(values, items, ref comparer);
			return;
		}

		var indices = PragueArrayPool<int>.Pool.Rent(n);
		var buffer = PragueArrayPool<T>.Pool.Rent(n);
		var itemBuffer = PragueArrayPool<TItem>.Pool.Rent(n);
		try {
			var order = indices.AsSpan(0, n);
			SortIndices(order, values, ref comparer, depthLimit);
			Permute(values, order, buffer.AsSpan(0, n));
			Permute(items, order, itemBuffer.AsSpan(0, n));
		} finally {
			PragueArrayPool<int>.Pool.Return(indices);
			PragueArrayPool<T>.Pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
			PragueArrayPool<TItem>.Pool.Return(itemBuffer, RuntimeHelpers.IsReferenceOrContainsReferences<TItem>());
		}
	}

	internal static void Sort<T>(Span<T> values, Comparison<T> comparison)
		=> Sort(values, comparison, DepthLimit(values.Length));

	/// <summary>Test seam: <paramref name="depthLimit"/> bounds the partitioning depth (0 forces the heapsort fallback).</summary>
	internal static void Sort<T>(Span<T> values, Comparison<T> comparison, int depthLimit) {
		ArgumentNullException.ThrowIfNull(comparison);
		var comparer = new ComparisonComparer<T>(comparison);
		Sort(values, ref comparer, depthLimit);
	}

	private static void Sort<T, TComparer>(Span<T> values, ref TComparer comparer, int depthLimit)
		where TComparer : IComparer<T> {
		var n = values.Length;
		if (n < 2)
			return;

		if (n <= DirectInsertionThreshold) {
			InsertionSortRows(values, ref comparer);
			return;
		}

		var indices = PragueArrayPool<int>.Pool.Rent(n);
		var buffer = PragueArrayPool<T>.Pool.Rent(n);
		try {
			var order = indices.AsSpan(0, n);
			SortIndices(order, values, ref comparer, depthLimit);
			Permute(values, order, buffer.AsSpan(0, n));
		} finally {
			PragueArrayPool<int>.Pool.Return(indices);
			PragueArrayPool<T>.Pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
		}
	}

	// Same budget as the framework introsort: past it, partitioning is assumed adversarial.
	private static int DepthLimit(int length) => 2 * (BitOperations.Log2((uint)length) + 1);

	// Adapts the Comparison<T> overloads to the comparer-typed core without a per-call allocation: the
	// delegate is the caller's, the wrapper lives on the stack.
	private readonly struct ComparisonComparer<T> : IComparer<T> {
		private readonly Comparison<T> _comparison;

		public ComparisonComparer(Comparison<T> comparison) => _comparison = comparison;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(T? x, T? y) => _comparison(x!, y!);
	}

	// ── Index order ───────────────────────────────────────────────────────────

	private static void SortIndices<T, TComparer>(Span<int> order, Span<T> values, ref TComparer comparer, int depth)
		where TComparer : IComparer<T> {
		for (var i = 0; i < order.Length; i++)
			order[i] = i;

		IntroSort(ref MemoryMarshal.GetReference(order), order.Length, ref MemoryMarshal.GetReference(values), ref comparer, depth);
	}

	// Orders `length` indices at `first` by (row key, index): three-way partition by key, equal block
	// finished by an integer sort, outer blocks recursed (smaller one) / looped (larger one).
	private static void IntroSort<T, TComparer>(ref int first, int length, ref T rows, ref TComparer comparer, int depth)
		where TComparer : IComparer<T> {
		while (length > InsertionSortThreshold) {
			if (depth == 0) {
				// Adversarial partitioning: a heapsort by (key, index) is stable by construction of the order.
				HeapSort(ref first, length, ref rows, ref comparer);
				return;
			}

			depth--;
			Partition(ref first, length, ref rows, ref comparer, out var lessCount, out var greaterStart);

			// Indices whose rows equal the pivot: their encounter order is their numeric order.
			var equalCount = greaterStart - lessCount;
			if (equalCount > 1)
				MemoryMarshal.CreateSpan(ref Unsafe.Add(ref first, lessCount), equalCount).Sort();

			var greaterCount = length - greaterStart;
			if (lessCount < greaterCount) {
				IntroSort(ref first, lessCount, ref rows, ref comparer, depth);
				first = ref Unsafe.Add(ref first, greaterStart);
				length = greaterCount;
			} else {
				IntroSort(ref Unsafe.Add(ref first, greaterStart), greaterCount, ref rows, ref comparer, depth);
				length = lessCount;
			}
		}

		InsertionSort(ref first, length, ref rows, ref comparer);
	}

	// Dutch-flag partition of the indices around the key of a median-of-three pivot row:
	// [0, lessCount) rows < pivot, [lessCount, greaterStart) rows == pivot, [greaterStart, length) rows > pivot.
	// The equal block is never empty (it holds the pivot), so both outer blocks are strictly shorter.
	private static void Partition<T, TComparer>(ref int first, int length, ref T rows, ref TComparer comparer, out int lessCount, out int greaterStart)
		where TComparer : IComparer<T> {
		var last = length - 1;
		var middle = length >> 1;
		ref var a = ref first;
		ref var b = ref Unsafe.Add(ref first, middle);
		ref var c = ref Unsafe.Add(ref first, last);
		if (comparer.Compare(Unsafe.Add(ref rows, a), Unsafe.Add(ref rows, b)) > 0)
			Swap(ref a, ref b);

		if (comparer.Compare(Unsafe.Add(ref rows, a), Unsafe.Add(ref rows, c)) > 0)
			Swap(ref a, ref c);

		if (comparer.Compare(Unsafe.Add(ref rows, b), Unsafe.Add(ref rows, c)) > 0)
			Swap(ref b, ref c);

		var pivot = Unsafe.Add(ref rows, b);
		var less = 0;
		var i = 0;
		var greater = length;
		while (i < greater) {
			ref var index = ref Unsafe.Add(ref first, i);
			var order = comparer.Compare(Unsafe.Add(ref rows, index), pivot);
			if (order < 0) {
				Swap(ref Unsafe.Add(ref first, less), ref index);
				less++;
				i++;
			} else if (order > 0) {
				greater--;
				Swap(ref index, ref Unsafe.Add(ref first, greater));
			} else {
				i++;
			}
		}

		lessCount = less;
		greaterStart = greater;
	}

	// Stable total order on indices: row key first, encounter (index) order on ties.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CompareIndices<T, TComparer>(int x, int y, ref T rows, ref TComparer comparer)
		where TComparer : IComparer<T> {
		var order = comparer.Compare(Unsafe.Add(ref rows, x), Unsafe.Add(ref rows, y));
		return order != 0 ? order : x.CompareTo(y);
	}

	private static void InsertionSort<T, TComparer>(ref int first, int length, ref T rows, ref TComparer comparer)
		where TComparer : IComparer<T> {
		for (var i = 1; i < length; i++) {
			var index = Unsafe.Add(ref first, i);
			var j = i - 1;
			while (j >= 0 && CompareIndices(Unsafe.Add(ref first, j), index, ref rows, ref comparer) > 0) {
				Unsafe.Add(ref first, j + 1) = Unsafe.Add(ref first, j);
				j--;
			}

			Unsafe.Add(ref first, j + 1) = index;
		}
	}

	private static void HeapSort<T, TComparer>(ref int first, int length, ref T rows, ref TComparer comparer)
		where TComparer : IComparer<T> {
		for (var i = (length >> 1) - 1; i >= 0; i--)
			SiftDown(ref first, i, length, ref rows, ref comparer);

		for (var i = length - 1; i > 0; i--) {
			Swap(ref first, ref Unsafe.Add(ref first, i));
			SiftDown(ref first, 0, i, ref rows, ref comparer);
		}
	}

	private static void SiftDown<T, TComparer>(ref int first, int i, int size, ref T rows, ref TComparer comparer)
		where TComparer : IComparer<T> {
		while (true) {
			var largest = i;
			var left = 2 * i + 1;
			var right = left + 1;
			if (left < size && CompareIndices(Unsafe.Add(ref first, left), Unsafe.Add(ref first, largest), ref rows, ref comparer) > 0)
				largest = left;

			if (right < size && CompareIndices(Unsafe.Add(ref first, right), Unsafe.Add(ref first, largest), ref rows, ref comparer) > 0)
				largest = right;

			if (largest == i)
				return;

			Swap(ref Unsafe.Add(ref first, i), ref Unsafe.Add(ref first, largest));
			i = largest;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(ref int a, ref int b) {
		var t = a;
		a = b;
		b = t;
	}

	// ── Rows ──────────────────────────────────────────────────────────────────

	// values[i] = values[order[i]] for every i, through one pooled buffer.
	private static void Permute<T>(Span<T> values, ReadOnlySpan<int> order, Span<T> buffer) {
		ref var source = ref MemoryMarshal.GetReference(values);
		ref var target = ref MemoryMarshal.GetReference(buffer);
		ref var index = ref MemoryMarshal.GetReference(order);
		for (var i = 0; i < order.Length; i++)
			Unsafe.Add(ref target, i) = Unsafe.Add(ref source, Unsafe.Add(ref index, i));

		buffer.CopyTo(values);
	}

	private static void InsertionSortRows<T, TComparer>(Span<T> values, ref TComparer comparer)
		where TComparer : IComparer<T> {
		ref var first = ref MemoryMarshal.GetReference(values);
		for (var i = 1; i < values.Length; i++) {
			var item = Unsafe.Add(ref first, i);
			var j = i - 1;
			// Shift only strictly greater rows: an equal row stays ahead, which keeps the sort stable.
			while (j >= 0 && comparer.Compare(Unsafe.Add(ref first, j), item) > 0) {
				Unsafe.Add(ref first, j + 1) = Unsafe.Add(ref first, j);
				j--;
			}

			Unsafe.Add(ref first, j + 1) = item;
		}
	}

	private static void InsertionSortRows<T, TItem, TComparer>(Span<T> values, Span<TItem> items, ref TComparer comparer)
		where TComparer : IComparer<T> {
		ref var first = ref MemoryMarshal.GetReference(values);
		ref var firstItem = ref MemoryMarshal.GetReference(items);
		for (var i = 1; i < values.Length; i++) {
			var value = Unsafe.Add(ref first, i);
			var item = Unsafe.Add(ref firstItem, i);
			var j = i - 1;
			while (j >= 0 && comparer.Compare(Unsafe.Add(ref first, j), value) > 0) {
				Unsafe.Add(ref first, j + 1) = Unsafe.Add(ref first, j);
				Unsafe.Add(ref firstItem, j + 1) = Unsafe.Add(ref firstItem, j);
				j--;
			}

			Unsafe.Add(ref first, j + 1) = value;
			Unsafe.Add(ref firstItem, j + 1) = item;
		}
	}
}

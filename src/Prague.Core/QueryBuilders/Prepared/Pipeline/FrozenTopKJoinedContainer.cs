namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The frozen twin of <see cref="TopKPairComparer{TKey,TValue,TChain}" />: orders
///   <c>(key, left, ordinal)</c> triples by the left value, then by encounter order, comparing through
///   the sorter <b>itself</b> as a struct type parameter instead of through the resolver chain.
///   <para>
///   The chain hop is not free. <c>IResolvers.CompareLeftValues</c> is a generic method reached once
///   per link — a shared (<c>__Canon</c>) instantiation over the left value, so each link costs a
///   runtime generic lookup and blocks the inline chain down to the user comparer. Measured on the very
///   workload the production shapes run (100 candidates into a heap of 40, then the ascending drain,
///   `BoundedComparerProbeBenchmarks`): 3.86 µs through the sorter, 4.99 µs through a one-link chain,
///   7.44 µs through two links (shape A) and 9.61 µs through three (shape B). That difference is
///   essentially the whole gap between the frozen joined bounded page and its join-free twin.
///   </para>
/// </summary>
internal readonly struct TopKSorterPairComparer<TKey, TValue, TSorter> : IComparer<(TKey Key, TValue Left, int Ordinal)>
	where TSorter : struct, IJoinResolver {
	private readonly TSorter _sorter;

	public TopKSorterPairComparer(TSorter sorter) => _sorter = sorter;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare((TKey Key, TValue Left, int Ordinal) x, (TKey Key, TValue Left, int Ordinal) y) {
		var order = _sorter.CompareLeftValues(x.Left, y.Left);
		return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
	}
}

/// <summary>
///   The frozen pipeline's bounded base container for a joined plan (design §8, §13 step 7): the exact
///   behaviour of <see cref="TopKJoinedBaseContainer{TKey,TValue,TChain}" /> — the same plan choice
///   (a pooled heap of K for a small prefix, collect-and-select-in-place near the full size), the same
///   encounter ordinals stamped by <see cref="Add" />, the same <c>Seal</c> total, the same
///   <see cref="Drain" /> contract and the same "the heap buffer never transfers ownership" rule — with
///   the sorter reaching every comparison as a struct type parameter
///   (<see cref="TopKSorterPairComparer{TKey,TValue,TSorter}" />) rather than through the resolver chain.
///   Reached only from <see cref="PipelineJoinedExecutor{TKey,TValue,TArgs,TResolverChain,TResult}" />,
///   which obtains the sorter through <c>IResolvers.WithSorter</c>; the eager container is untouched and
///   still serves <c>ExecuteCoreJoinedTop</c>.
/// </summary>
internal ref struct FrozenTopKJoinedContainer<TKey, TValue, TSorter>
	: IResultContainerInitializer<TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TSorter : struct, IJoinResolver {
	private readonly int _skip;
	private readonly int _take;
	private readonly int _k;
	private readonly TopKSorterPairComparer<TKey, TValue, TSorter> _comparer;
	private (TKey Key, TValue Left, int Ordinal)[]? _heap;
	private int _seen;
	private bool _collectAll;
	private int _heapCount;
	private bool _heapified;
	private int _totalCount;

	public int TotalCount => _totalCount;

	public FrozenTopKJoinedContainer(TSorter sorter, int skip, int take) {
		_comparer = new TopKSorterPairComparer<TKey, TValue, TSorter>(sorter);
		_skip = skip;
		_take = take;
		_k = skip + take;
	}

	public void Init(int maxCount) {
		if (_heap is not null)
			return;

		// The eager container's plan choice, verbatim: heap for a small prefix, collect + select otherwise.
		_collectAll = _k > 0 && (long)_k * 4 >= maxCount;
		var capacity = _collectAll ? maxCount : Math.Min(_k, maxCount);
		if (capacity > 0)
			_heap = PragueArrayPool<(TKey, TValue, int)>.Pool.Rent(capacity);
	}

	public void Seal(int actualCount) => _totalCount = actualCount;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Add(TKey foreignKey, TValue result) {
		if (_heap is not null) {
			var item = (foreignKey, result, _seen++);
			if (_collectAll) {
				_heap[_heapCount++] = item;
			} else {
				TopKSelect.Push(_heap, ref _heapCount, ref _heapified, _k, item, _comparer);
			}
		}

		return 0;
	}

	/// <summary>Orders the page rows ascending at <c>Buffer[skip ..)</c> and returns the end of the page — the eager <c>Drain</c>.</summary>
	internal int Drain() {
		if (_heap is null)
			return 0;

		if (_collectAll)
			return _skip + TopKSelect.SelectPage(_heap.AsSpan(0, _heapCount), _skip, _take, _comparer);

		return TopKSelect.DrainAscending(_heap, ref _heapCount, ref _heapified, _comparer);
	}

	/// <summary>The kept pairs' backing buffer. Valid only between <see cref="Drain" /> and <see cref="Dispose" />; empty when nothing was ever kept.</summary>
	internal (TKey Key, TValue Left, int Ordinal)[] Buffer => _heap ?? [];

	public void Dispose() {
		if (_heap is not null) {
			PragueArrayPool<(TKey, TValue, int)>.Pool.Return(_heap, RuntimeHelpers.IsReferenceOrContainsReferences<(TKey, TValue, int)>());
			_heap = null;
		}
	}
}

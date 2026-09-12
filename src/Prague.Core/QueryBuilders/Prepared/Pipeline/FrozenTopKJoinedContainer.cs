namespace Prague.Core;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The frozen twin of <see cref="TopKPairComparer{TKey,TValue,TChain}" />: orders
///   <c>(key, left, ordinal)</c> triples by the left value, then by encounter order, calling the
///   <b>user comparer</b> as a struct type parameter instead of reaching it through the resolver chain.
///   <para>
///   Neither hop is free, and the deeper one is not the chain. <c>IResolvers.CompareLeftValues</c> costs
///   a JIT-folded <c>IsSorter</c> test per link; <c>IJoinResolver.CompareLeftValues</c>, at the end of
///   it, is a <b>generic method</b>, so over a reference-type left value it compiles once as
///   <c>CompareLeftValues[__Canon]</c> — reached through a runtime generic dictionary, never inlined,
///   an indirect call per comparison. Measured on shape A's real page path (the ceiling benchmark's
///   level 2, the real container and the real steps): 3,735 ns through the sorter against 2,377 ns with
///   a direct struct comparer, <b>1,359 ns</b> — a third of level 2. The disassembly of
///   <c>TopKSelect.Partition</c> is the proof: five indirect <c>blr</c> calls and four
///   <c>CORINFO_HELP_RUNTIMEHANDLE_METHOD</c> helpers on the sorter instantiation, none at all on the
///   direct one, where the user comparer is inlined into the partition body.
///   </para>
///   <para>
///   <typeparamref name="TResult" /> is what the comparer orders and <typeparamref name="TValue" /> is
///   the left value; the bounded gate (<c>JoinChainShape.BoundedCapable</c> over
///   <c>OrdersByLeftValues</c>) admits this container only when they are the same type, which is what
///   makes the reinterpret sound. A class comparer takes the same path — one interface call, still no
///   generic-dictionary lookup; a struct comparer folds all the way down.
///   </para>
/// </summary>
internal readonly struct TopKLeftPairComparer<TKey, TValue, TResult, TComparer> : IComparer<(TKey Key, TValue Left, int Ordinal)>
	where TComparer : IComparer<TResult> {
	private readonly TComparer _comparer;

	public TopKLeftPairComparer(TComparer comparer) {
		Debug.Assert(typeof(TValue) == typeof(TResult), "the bounded gate admits this container only when the sorter orders the left value");
		_comparer = comparer;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare((TKey Key, TValue Left, int Ordinal) x, (TKey Key, TValue Left, int Ordinal) y) {
		var order = _comparer.Compare(Unsafe.As<TValue, TResult>(ref x.Left), Unsafe.As<TValue, TResult>(ref y.Left));
		return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
	}
}

/// <summary>
///   The frozen pipeline's bounded base container for a joined plan (design §8, §13 step 7): the exact
///   behaviour of <see cref="TopKJoinedBaseContainer{TKey,TValue,TChain}" /> — the same plan choice
///   (a pooled heap of K for a small prefix, collect-and-select-in-place near the full size), the same
///   encounter ordinals stamped by <see cref="Add" />, the same <c>Seal</c> total, the same
///   <see cref="Drain" /> contract and the same "the heap buffer never transfers ownership" rule — with
///   the pair comparer carried as a struct type parameter — in production
///   <see cref="TopKLeftPairComparer{TKey,TValue,TResult,TComparer}" /> over the sorter's own user
///   comparer — rather than reached through the resolver chain.
///   Reached only from <see cref="PipelineJoinedExecutor{TKey,TValue,TArgs,TResolverChain,TResult}" />,
///   which obtains the comparer through <c>IResolvers.WithSorter</c> then
///   <c>IJoinResolver.WithLeftComparer</c>; the eager container is untouched and
///   still serves <c>ExecuteCoreJoinedTop</c>.
/// </summary>
internal ref struct FrozenTopKJoinedContainer<TKey, TValue, TPairComparer>
	: IResultContainerInitializer<TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TPairComparer : struct, IComparer<(TKey Key, TValue Left, int Ordinal)> {
	private readonly int _skip;
	private readonly int _take;
	private readonly int _k;
	private readonly TPairComparer _comparer;
	private (TKey Key, TValue Left, int Ordinal)[]? _heap;
	private int _seen;
	private bool _collectAll;
	private int _heapCount;
	private bool _heapified;
	private int _totalCount;

	public int TotalCount => _totalCount;

	public FrozenTopKJoinedContainer(TPairComparer comparer, int skip, int take) {
		_comparer = comparer;
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

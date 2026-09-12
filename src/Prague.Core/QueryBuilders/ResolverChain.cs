namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Collections;
using Utils;

/*
 *
	internal void UnsafeSortResults<TFullResult>(ref QueryResults<TFullResult> results, int skip, int take)
   	=> throw new InvalidOperationException("Join resolver is not sortable");
 */

public interface ISingleResolver {
	internal bool IsSorter { get; }
	internal void UnsafeSortResults<TFullResult>(ref QueryResults<TFullResult> results, int skip, int take);
}

public interface IResolvers {
	/// <summary>Walks the resolver chain bottom-up (forward order), calling executor.Process for each resolver.</summary>
	int Execute<TExecutor>(ref TExecutor executor)
		where TExecutor : struct, IResolverExecutor, allows ref struct;


	static abstract int Clone<TFullResult>(ref TFullResult fullResult)
		where TFullResult : struct, IJoinResult;

	/// <summary>
	/// Forwards a LEFT-value comparison to the chain's sorter. The dispatch is a JIT-folded
	/// TResolver.IsSorter test per link, so the whole hop collapses to the user comparer's Compare —
	/// which is how the bounded joined plan compares without erasing the comparer to
	/// <see cref="IComparer{T}"/> (a box for a struct comparer) or wrapping it in a delegate.
	/// Callers must have probed that the chain has exactly one sorter and that it orders by the left
	/// value.
	/// </summary>
	int CompareLeftValues<TLeft>(TLeft a, TLeft b)
		=> throw new InvalidOperationException("Resolver chain has no sorter");

	/// <summary>
	///   Hands the chain's sorter to <paramref name="visitor" /> as a struct type parameter, so the work
	///   the visitor does per comparison calls the sorter directly instead of hopping the chain
	///   (<see cref="CompareLeftValues{TLeft}" />) once per link per compare. Walked once per execution;
	///   the frozen bounded joined page (pipeline design §8, step 7) is its only caller, and it must have
	///   probed that the chain has exactly one sorter and that it orders by the left value.
	/// </summary>
	internal void WithSorter<TVisitor>(ref TVisitor visitor)
		where TVisitor : struct, ISorterVisitor, allows ref struct
		=> throw new InvalidOperationException("Resolver chain has no sorter");
}

/// <summary>
///   Receives a resolver chain's sorter statically typed (<see cref="IResolvers.WithSorter{TVisitor}" />).
///   The twin of <see cref="IResolverExecutor" />, which walks every link; this one stops at the sorter.
/// </summary>
internal interface ISorterVisitor {
	void Visit<TSorter>(ref TSorter sorter) where TSorter : struct, IJoinResolver;
}

/// <summary>
///   Receives a sorter's user comparer statically typed, with the type it orders
///   (<see cref="IJoinResolver.WithLeftComparer{TVisitor}" />). One hop further in than
///   <see cref="ISorterVisitor" />, which stops at the resolver: this one carries the comparer itself,
///   so the work the visitor does per comparison calls it through a type parameter rather than through
///   the resolver's generic <c>CompareLeftValues</c>.
/// </summary>
internal interface ILeftComparerVisitor {
	void Visit<TResult, TComparer>(ref TComparer comparer) where TComparer : IComparer<TResult>;
}

public interface IResolverExecutor {
	void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver;
}

/// <summary>Marker interface for resolver chains in forward (flipped) order.</summary>
public interface IFlippedResolvers { }

[StructLayout(LayoutKind.Sequential)]
public struct Resolvers<TResolver> : IResolvers, IFlippedResolvers
	where TResolver : struct, IJoinResolver {
	private TResolver _resolver;

	// readonly: the chain is held in readonly fields (the frozen executors, the prepared query), and a
	// non-readonly member read through one copies the whole chain to a temp before handing back the
	// ref — so the ref would point at the copy, and the copy costs sizeof(chain) per execution. The
	// Unsafe.AsRef is what makes the readonly modifier legal here; it was already doing the laundering.
	internal readonly ref TResolver Resolver => ref Unsafe.AsRef(in _resolver);

	public Resolvers(TResolver resolver) {
		_resolver = resolver;
	}

	/// <summary>Single resolver — just process it.</summary>
	public int Execute<TExecutor>(ref TExecutor executor)
		where TExecutor : struct, IResolverExecutor, allows ref struct {
		executor.Process(0, ref _resolver);
		return 1;
	}

	// Chain base — TResolver is BaseResolver (occupies the JoinResult Left slot,
	// index 0). Return 1 so subsequent chain links clone slot 1 (the first Right).
	public static int Clone<TFullResult>(ref TFullResult fullResult) where TFullResult : struct, IJoinResult
		=> 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CompareLeftValues<TLeft>(TLeft a, TLeft b) => _resolver.CompareLeftValues(a, b);

	// Chain base: position 0, the only place an innermost sorter can sit.
	void IResolvers.WithSorter<TVisitor>(ref TVisitor visitor) => visitor.Visit(ref _resolver);
}

[StructLayout(LayoutKind.Sequential)]
public struct Resolvers<TPrev, TResolver> : IResolvers, IFlippedResolvers
	where TPrev: struct, IResolvers
	where TResolver: struct, IJoinResolver {
	private TPrev _prev;
	private TResolver _resolver;

	public Resolvers(TPrev prev, TResolver resolver) {
		_prev = prev;
		_resolver = resolver;
	}

	/// <summary>Recurse into _prev first (bottom-up), then process this resolver.</summary>
	public int Execute<TExecutor>(ref TExecutor executor)
		where TExecutor : struct, IResolverExecutor, allows ref struct {
		var pos = _prev.Execute(ref executor);
		executor.Process(TResolver.IsSorter ? pos : pos++, ref _resolver);
		return pos;

	}

	public static int Clone<TFullResult>(ref TFullResult fullResult)
		where TFullResult : struct, IJoinResult {
		var pos = TPrev.Clone(ref fullResult);
		if (!TResolver.IsSorter)
			TResolver.Clone(pos++, ref fullResult);
		return pos;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CompareLeftValues<TLeft>(TLeft a, TLeft b)
		=> TResolver.IsSorter ? _resolver.CompareLeftValues(a, b) : _prev.CompareLeftValues(a, b);

	// The same JIT-folded per-link test CompareLeftValues uses, paid once per execution instead of once
	// per link per comparison.
	void IResolvers.WithSorter<TVisitor>(ref TVisitor visitor) {
		if (TResolver.IsSorter)
			visitor.Visit(ref _resolver);
		else
			_prev.WithSorter(ref visitor);
	}
}

internal struct ResolveChainCloner<TResolvers, TLeftValue, TResult> : ICloner<TResult>
	where TResolvers : struct, IResolvers
	where TLeftValue : ICacheClonable<TLeftValue>
	where TResult : struct, IJoinResult<TLeftValue> {
	public void Clone(ref TResult value) {
		// `value.Left` returns a writable ref via the explicit IJoinResult<TLeft>.Left
		// implementation (Unsafe.AsRef over the readonly field). Assignment writes
		// the cloned reference back into the struct's slot.
		value.Left = value.Left.Clone();
		TResolvers.Clone(ref value);
	}
}

internal ref struct JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TResult>
	: IResultContainerInitializer<TLeftKey, TLeftValue>
	where TLeftValue : ICacheClonable<TLeftValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TResult : struct, IJoinResult<TLeftValue>
	where TResolverChain : struct, IResolvers {
	private readonly bool _shouldPool;
	private readonly bool _clone;
	private readonly bool _cloneOnAdd;
	private readonly int _skip;
	private readonly int _take;
	private int _totalCont;
	private bool _handedOff;
	private ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> _results;
	private QueryResultsDisposer _disposer;
	private ref TResolverChain _chainedResolvers;
	public int TotalCount => _totalCont;


	public JoinedResultContaier(ref TResolverChain chainedResolvers, bool pool, bool clone, int skip, int take,
		int manyCount) {
		_chainedResolvers = ref chainedResolvers;
		_shouldPool = pool;
		var shouldSlice = skip > 0 || take < int.MaxValue;
		_cloneOnAdd = clone && !shouldSlice;
		_clone = clone && !_cloneOnAdd;
		_skip = skip;
		_take = take;
		// One disposer slot per Many join in the chain (manyCount), to return their rented child buffers.
		// default is inert (IsActive == false) for non-pooled / no-Many queries.
		_disposer = pool && manyCount > 0 ? new QueryResultsDisposer(manyCount) : default;
	}

	/// <summary>
	///   Bounded top-K constructor. The page is selected upstream (heap over the base walk), so the
	///   container itself neither slices nor sorts: skip/take stay neutral and cloning is always the
	///   deferred kind — only the surviving page rows are cloned, once, in BuildResults.
	/// </summary>
	public JoinedResultContaier(ref TResolverChain chainedResolvers, bool pool, bool clone, int manyCount, bool bounded) {
		_chainedResolvers = ref chainedResolvers;
		_shouldPool = pool;
		_cloneOnAdd = false;
		_clone = clone;
		_skip = 0;
		_take = int.MaxValue;
		_disposer = pool && manyCount > 0 ? new QueryResultsDisposer(manyCount) : default;
	}



	public void PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue> {

		var e = new PrepareIndexedInnerProcessor<TLeftKey, TLeftValue, TExecutor>(ref leftQuery,  _cloneOnAdd, _shouldPool, ref _disposer);
		_chainedResolvers.Execute(ref e);
		if (e._hadHit)
			Init(leftQuery.Candidates.Count);
		_totalCont = _results.Count;
	}

	public void ExecuteIndexedInner<TExecutor>(ref TExecutor leftQuery) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue> {
		var e = new ExecuteIndexedInnerProcessor<TLeftKey, TLeftValue, TResult, TExecutor>(ref leftQuery, ref _results,  _cloneOnAdd, ref _disposer);
		_chainedResolvers.Execute(ref e);
	}

	public void Init(int maxCount) {
		if (!_results.IsInitialized)
			_results = new ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>>(_shouldPool, maxCount);
	}

	public void Seal(int actualCount) => _totalCont = actualCount;

	public int Add(TLeftKey foreignKey, TLeftValue result) {
		ref var v = ref _results.GetValueRefOrAddDefault(foreignKey, out var exists);
		if (!exists) _totalCont++;
		Unsafe.AsRef(in v.Left) = _cloneOnAdd ? result.Clone() : result;
		return 0;
	}

	/// <summary>
	///   The frozen pipeline's fused <c>JoinOne</c> fill (design §7.1): every resolver in
	///   <paramref name="fusedMask" /> writes its right slot of every row with one point lookup per row
	///   (<see cref="IJoinResolver.UnsafeFillFusedRows{TAccessor}" />); an inner one drops the rows without
	///   a right. <paramref name="recount" />: the total becomes the surviving row count (the classic flow,
	///   where the total is the rows added so far); the bounded flow keeps its narrowing's total.
	///   A fused <c>JoinMany</c> in the mask fills through its frozen per-left fill (<paramref name="fillHints" />:
	///   its buffer size hint per chain position, the executor's); <paramref name="innerMany" /> (design §7.2 as
	///   implemented) makes the same walk also run every <i>unfused</i> inner <c>JoinMany</c>'s fan-out over the
	///   rows and drop the rows whose slot stayed empty, in chain order with the fused inner fills; unfused
	///   outer <c>JoinMany</c>s run in <see cref="ExecuteJoins" /> like any unfused resolver.
	/// </summary>
	internal void FillFused(int fusedMask, bool recount, int[] fillHints, bool innerMany = false) {
		var p = new ExecuteWithAccessorProcessor<TLeftKey, TResult>(
			ref _results, _skip, _take, _cloneOnAdd, _shouldPool, ref _disposer, fillInner: false, skipSorter: true, fusedMask, fillFused: true, fillInnerMany: innerMany, fillHints: fillHints);
		_chainedResolvers.Execute(ref p);
		if (p.Pruned && recount)
			_totalCont = _results.Count;
	}

	public readonly ReadOnlySpan<TLeftKey> Keys => _results.Keys;

	/// <summary>Execute joins for resolver 1 (reverse joins only — forward joins resolved in Add when active). <paramref name="fusedMask" />: a bit per chain position whose slot the frozen pipeline filled in its pass — skipped here; the sorter always runs.</summary>
	public void ExecuteJoins(int fusedMask = 0) {
		var p = new ExecuteWithAccessorProcessor<TLeftKey, TResult>(
			ref _results, _skip, _take, _cloneOnAdd, _shouldPool, ref _disposer, fillInner: false, skipSorter: false, fusedMask);
		_chainedResolvers.Execute(ref p);
		if (!p.DidSort && (_skip > 0 || _take < int.MaxValue))
			_results.Crop(_skip, _take);
	}

	/// <summary>Finalize results — placeholder for post-execute work; currently a no-op.</summary>
	public void FinalizeResults() {
	}

	// ── Bounded top-K path ───────────────────────────────────────────────────────

	/// <summary>
	///   Bounded-path Prepare: triggers candidate auto-population exactly like
	///   <see cref="PrepareIndexedInner{TExecutor}"/>, but does NOT size the results dictionary to
	///   the candidate count — the dictionary is created later, sized to the selected page.
	/// </summary>
	public void PrepareIndexedInnerBounded<TExecutor>(ref TExecutor leftQuery)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue> {
		var e = new PrepareIndexedInnerProcessor<TLeftKey, TLeftValue, TExecutor>(ref leftQuery, _cloneOnAdd, _shouldPool, ref _disposer);
		_chainedResolvers.Execute(ref e);
	}

	/// <summary>Runs every inner resolver in narrow-only mode (candidate narrowing, no dictionary writes).</summary>
	public void NarrowIndexedInner<TExecutor>(ref TExecutor leftQuery)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue> {
		var e = new NarrowIndexedInnerProcessor<TExecutor>(ref leftQuery);
		_chainedResolvers.Execute(ref e);
	}

	/// <summary>
	///   Materializes the selected top-K rows into the results dictionary: page rows are inserted in
	///   ascending (final) order, keys are unique (they come from a candidate set), and metadata built
	///   by plain appends stays lookup-consistent for the join fill pass that follows.
	/// </summary>
	public void MaterializeTopK((TLeftKey Key, TLeftValue Left, int Ordinal)[] pairs, int start, int count, int totalCount) {
		// ValueDictionary is fixed-capacity; clamp to at least 1 so the rent path stays on
		// well-trodden ground. An empty page is handled by BuildResults' Count == 0 guard.
		Init(Math.Max(count, 1));
		for (var i = 0; i < count; i++) {
			ref readonly var pair = ref pairs[start + i];
			ref var v = ref _results.GetValueRefOrAddDefault(pair.Key, out _);
			Unsafe.AsRef(in v.Left) = pair.Left;
		}

		_totalCont = totalCount;
	}

	/// <summary>
	///   Join fill for the bounded path: fills inner AND reverse slots, for the page rows only.
	///   The sorter is skipped (rows are already ordered and cropped) and no fallback Crop runs.
	/// </summary>
	public void ExecuteJoinsBounded(int fusedMask = 0) {
		var p = new ExecuteWithAccessorProcessor<TLeftKey, TResult>(
			ref _results, 0, int.MaxValue, _cloneOnAdd, _shouldPool, ref _disposer, fillInner: true, skipSorter: true, fusedMask);
		_chainedResolvers.Execute(ref p);
	}

	public QueryResults<TResult> BuildResults() {
		// Empty result carries no buffer slices — ownership of the values array and the
		// disposer stays here, and the pipeline's finally Dispose() returns both.
		if (_results.Count == 0)
			return QueryResults<TResult>.EmptyWithTotalCount(TotalCount);

		var offset = _results.Offset;
		var allResults = QueryResults<TResult>.FromArray(
			_results.ValuesArray ?? [], offset, _results.Count, TotalCount, _shouldPool, in _disposer);

		// Clone after slicing if needed. Hand the buffer off only once nothing left here can throw: a
		// user Clone() that throws leaves the values array (and the disposer's child buffers) with this
		// container, whose Dispose returns them.
		if (_clone)
			allResults.CloneElements(new ResolveChainCloner<TResolverChain, TLeftValue, TResult>());
		_handedOff = true;

		return allResults;
	}

	// Hands the keyed result map and its disposer to the caller, transferring ownership:
	// our own Dispose/HardDispose become no-ops for these (set to default). Used by the
	// nested-join path, which needs key→row lookups (not the flattened QueryResults) to
	// scatter inner rows back to their outer parent, and must keep any pooled inner-Many
	// buffers (held by the disposer) alive until the OUTER result is disposed.
	internal void ExtractKeyedResults(
		out ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> results,
		out QueryResultsDisposer disposer) {
		results = _results;
		disposer = _disposer;
		_results = default;
		_disposer = default;
	}

	// Exactly-one-owner cleanup: once BuildResults hands the values array and disposer to
	// the returned QueryResults, only metadata/keys are still ours. On every other exit —
	// empty result, Count, or a user callback throwing mid-pipeline — the disposer's child
	// buffers and a pooled values array must go back to the pool here.
	public void Dispose() {
		if (!_handedOff)
			_disposer.Dispose();
		_results.Dispose(withValues: !_handedOff);
	}
}

internal ref struct SimpleResultContainer<TKey, TValue, TResolver>
	: IResultContainerInitializer<TKey, TValue>
	where TResolver : struct, IJoinResolver
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly bool _shouldPool;
	private readonly bool _clone;
	private readonly bool _cloneOnAdd;
	private readonly int _skip;
	private readonly int _take;
	private int _totalCount;
	private bool _handedOff;
	private QueryResults<TValue> _results;
	private TResolver _chain;

	public int TotalCount => _totalCount;

	public SimpleResultContainer(TResolver chain, bool pool, bool clone, int skip, int take) {
		_chain = chain;
		_shouldPool = pool;
		var shouldSlice = skip > 0 || take < int.MaxValue;
		_cloneOnAdd = clone && !shouldSlice;
		_clone = clone && !_cloneOnAdd;
		_skip = skip;
		_take = take;
	}

	public void Init(int maxCount) {
		_results = new QueryResults<TValue>(maxCount, _shouldPool);
	}

	public void Seal(int actualCount) => _totalCount = actualCount;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Add(TKey foreignKey, TValue result) => _results.UnsafeAdd(_cloneOnAdd ? result.Clone() : result);

	public QueryResults<TValue> BuildResults() {
		// Empty / skip-past-end: the rented buffer (if Init ran) stays ours and the
		// pipeline's finally Dispose() returns it.
		if (_totalCount == 0 || _skip > _totalCount)
			return QueryResults<TValue>
				.EmptyWithTotalCount(
					_totalCount);

		var allResults = _results;
		if (TResolver.IsSorter)
			_chain.UnsafeSortResults(ref allResults, _skip, _take);
		else if (_skip > 0 || _take < int.MaxValue)
			allResults.SliceLeaveTotalCount(_skip, Math.Min(_take, _totalCount - _skip));

		// Hand the buffer off only once nothing left here can throw: a user comparer or Clone() that
		// throws above leaves the rented array with this container, whose Dispose returns it.
		if (_clone)
			allResults.CloneInPlace();
		_handedOff = true;
		return allResults;
	}

	public void Dispose() {
		if (!_handedOff)
			_results.Dispose();
	}
}

/// <summary>
///   Bounded counterpart of <see cref="SimpleResultContainer{TKey,TValue,TResolver}"/>: instead of
///   materializing every matched row for a small page, keeps the top (skip+take) rows in a heap.
///   Large prefixes are collected and the page is selected in place (introselect), sorting only the
///   page; both plans break ties by encounter order. BuildResults drops
///   the skip prefix and produces a page-sized QueryResults whose
///   TotalCount is the full matched count (via UnsafeSetTotal — its first caller).
///   Clone semantics mirror the sliced classic path: survivors cloned once, after selection.
///   The heap buffer never transfers ownership — Dispose always returns it; the page rows are
///   copied into the result's own buffer.
/// </summary>
internal ref struct TopKSimpleResultContainer<TKey, TValue, TResolver>
	: IResultContainerInitializer<TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TResolver : struct, IJoinResolver
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly bool _shouldPool;
	private readonly bool _clone;
	private readonly int _skip;
	private readonly int _take;
	private readonly int _k;
	private readonly TopKValueComparer<TValue, TResolver> _comparer;
	private (TValue Left, int Ordinal)[]? _heap;
	private int _seen;
	private bool _collectAll;
	private int _heapCount;
	private bool _heapified;
	private int _totalCount;

	public int TotalCount => _totalCount;

	public TopKSimpleResultContainer(TResolver resolver, bool pool, bool clone, int skip, int take) {
		_comparer = new TopKValueComparer<TValue, TResolver>(resolver);
		_shouldPool = pool;
		// Bounded selection always slices, so cloning is always deferred to the survivors.
		_clone = clone;
		_skip = skip;
		_take = take;
		_k = skip + take;
	}

	public void Init(int maxCount) {
		if (_heap is not null)
			return;

		// A small prefix is cheapest through the heap. Near the full result size, collect every row and
		// select the page in place (introselect + page sort): fewer compares than a heap of size K or a
		// full sort, for a buffer sized to the result instead of to K. Decided once, with the rent.
		_collectAll = _k > 0 && (long)_k * 4 >= maxCount;
		var capacity = _collectAll ? maxCount : Math.Min(_k, maxCount);
		if (capacity > 0)
			_heap = PragueArrayPool<(TValue, int)>.Pool.Rent(capacity);
	}

	public void Seal(int actualCount) => _totalCount = actualCount;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Add(TKey foreignKey, TValue result) {
		if (_heap is not null) {
			var item = (result, _seen++);
			if (_collectAll) {
				_heap[_heapCount++] = item;
			} else {
				TopKSelect.Push(_heap, ref _heapCount, ref _heapified, _k, item, _comparer);
			}
		}

		return 0;
	}

	public QueryResults<TValue> BuildResults() {
		if (_totalCount == 0 || _skip > _totalCount)
			return QueryResults<TValue>.EmptyWithTotalCount(_totalCount);

		var page = 0;
		if (_heap is not null) {
			if (_collectAll) {
				page = TopKSelect.SelectPage(_heap.AsSpan(0, _heapCount), _skip, _take, _comparer);
			} else {
				// The heap held at most K = skip + take rows, so the page is whatever lies past skip.
				var kept = TopKSelect.DrainAscending(_heap, ref _heapCount, ref _heapified, _comparer);
				page = Math.Max(kept - _skip, 0);
			}
		}

		if (page == 0)
			return QueryResults<TValue>.EmptyWithTotalCount(_totalCount);

		var results = new QueryResults<TValue>(page, _shouldPool);
		for (var i = 0; i < page; i++)
			results.UnsafeAdd(_heap![_skip + i].Left);

		results.UnsafeSetTotal(_totalCount);
		if (!_clone)
			return results;

		try {
			return results.CloneInPlace();
		} catch {
			// User Clone() threw: the page buffer was never handed to a caller, so it is still ours.
			results.Dispose();
			throw;
		}
	}

	public void Dispose() {
		if (_heap is not null) {
			PragueArrayPool<(TValue, int)>.Pool.Return(_heap, RuntimeHelpers.IsReferenceOrContainsReferences<(TValue, int)>());
			_heap = null;
		}
	}
}

/// <summary>
///   Orders by the left value, then by encounter order so that ties resolve independently of the page
///   size. The user comparer is invoked through a <see cref="Comparison{T}"/> delegate created once per
///   query: the JIT devirtualizes and inlines a hot delegate target, while the same call through
///   <see cref="IComparer{T}"/> stays an interface dispatch — measured 1.7× on the page sort and 1.8× on
///   the heap loop for a class comparer. One 64-byte delegate per bounded query is the price, the same
///   allocation the classic full-sort path pays inside the framework sort.
/// </summary>
internal readonly struct TopKValueComparer<TValue, TResolver> : IComparer<(TValue Left, int Ordinal)>
	where TResolver : struct, IJoinResolver {
	private readonly TResolver _resolver;

	public TopKValueComparer(TResolver resolver) => _resolver = resolver;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare((TValue Left, int Ordinal) x, (TValue Left, int Ordinal) y) {
		var order = _resolver.CompareLeftValues(x.Left, y.Left);
		return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
	}
}

/// <summary>
///   Joined-path twin of <see cref="TopKValueComparer{TValue,TResolver}"/>: orders (key, left, ordinal)
///   triples by the left value, then by encounter order, comparing through the resolver chain so
///   nothing is boxed and no delegate is created.
/// </summary>
internal readonly unsafe struct TopKPairComparer<TKey, TValue, TChain> : IComparer<(TKey Key, TValue Left, int Ordinal)>
	where TChain : struct, IResolvers {
	// A pointer, not the chain by value: this comparer is copied into every TopKSelect sift and
	// partition frame, and a chain with several joins is a fat struct. The chain lives in the query
	// builder for the whole query, which outlives every use here.
	private readonly void* _chain;

	public TopKPairComparer(ref TChain chain) => _chain = Unsafe.AsPointer(ref chain);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare((TKey Key, TValue Left, int Ordinal) x, (TKey Key, TValue Left, int Ordinal) y) {
		var order = Unsafe.AsRef<TChain>(_chain).CompareLeftValues(x.Left, y.Left);
		return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
	}
}

/// <summary>
///   Bounded base-walk container for the joined top-K path: receives every (key, left) match from the
///   base execution (post-Where, post-inner-narrowing), selects a small prefix with a pooled heap
///   or collects a large prefix and selects the page in place, and records the authoritative total
///   via Seal. Pairs are drained into the
///   real results dictionary. The heap buffer never transfers ownership — Dispose always returns it.
/// </summary>
internal ref struct TopKJoinedBaseContainer<TKey, TValue, TChain>
	: IResultContainerInitializer<TKey, TValue>
	where TChain : struct, IResolvers
	where TKey : notnull, IEquatable<TKey> {
	private readonly int _skip;
	private readonly int _take;
	private readonly int _k;
	private readonly TopKPairComparer<TKey, TValue, TChain> _comparer;
	private (TKey Key, TValue Left, int Ordinal)[]? _heap;
	private int _seen;
	private bool _collectAll;
	private int _heapCount;
	private bool _heapified;
	private int _totalCount;

	public int TotalCount => _totalCount;

	public TopKJoinedBaseContainer(ref TChain chain, int skip, int take) {
		_comparer = new TopKPairComparer<TKey, TValue, TChain>(ref chain);
		_skip = skip;
		_take = take;
		_k = skip + take;
	}

	public void Init(int maxCount) {
		if (_heap is not null)
			return;

		// Same plan choice as TopKSimpleResultContainer: heap for a small prefix, collect + select otherwise.
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

	/// <summary>
	///   Orders the page rows ascending at <c>Buffer[skip ..)</c>: heapsorts the kept prefix, or selects
	///   the page in place when every row was collected. Returns the end of the page, so the caller
	///   materializes <c>Buffer[skip .. result)</c>.
	/// </summary>
	internal int Drain() {
		if (_heap is null)
			return 0;

		if (_collectAll)
			return _skip + TopKSelect.SelectPage(_heap.AsSpan(0, _heapCount), _skip, _take, _comparer);

		return TopKSelect.DrainAscending(_heap, ref _heapCount, ref _heapified, _comparer);
	}

	/// <summary>
	///   The kept pairs' backing buffer. Valid only between <see cref="Drain"/> and
	///   <see cref="Dispose"/>; empty when nothing was ever kept.
	/// </summary>
	internal (TKey Key, TValue Left, int Ordinal)[] Buffer => _heap ?? [];

	public void Dispose() {
		if (_heap is not null) {
			PragueArrayPool<(TKey, TValue, int)>.Pool.Return(_heap, RuntimeHelpers.IsReferenceOrContainsReferences<(TKey, TValue, int)>());
			_heap = null;
		}
	}
}

/// <summary>
///   Pre-execution probe for the bounded top-K path. Walks the resolver chain and answers: is there
///   exactly one sorter, is it the innermost resolver (chain base, position 0 — only the pre-join Sort
///   overload produces that), does it order by the left value (comparer extractable), and does every
///   inner resolver support narrow-only execution.
/// </summary>
internal ref struct TopKProbeProcessor<TLeftValue> : IResolverExecutor {
	internal int SorterCount;
	internal bool SorterInnermost;
	internal bool SorterAllowsBounded;
	internal bool SorterOrdersByLeftValues;
	internal bool AllInnerNarrowable;

	public static TopKProbeProcessor<TLeftValue> Create() => new() { AllInnerNarrowable = true };

	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
		if (TResolver.IsSorter) {
			SorterCount++;
			SorterInnermost = position == 0;
			SorterAllowsBounded = resolver.AllowsBounded;
			SorterOrdersByLeftValues = resolver.OrdersByLeftValues<TLeftValue>();
			return;
		}

		if (resolver.Inner && !TResolver.SupportsNarrowOnly)
			AllInnerNarrowable = false;
	}
}

/// <summary>
///   Chain walker for the bounded top-K path: runs every inner resolver in narrow-only mode
///   (candidates intersected with survivors, no dictionary writes). Callers must have verified
///   via the probe that every inner resolver in the chain has SupportsNarrowOnly == true.
///   Hand-written (unlike the generated processors) because it needs no per-position accessor.
/// </summary>
internal ref struct NarrowIndexedInnerProcessor<TExecutor> : IResolverExecutor
	where TExecutor : struct, IUnsafeCandidatesExecutor {
	private ref TExecutor _leftQuery;

	public NarrowIndexedInnerProcessor(ref TExecutor leftQuery) {
		_leftQuery = ref leftQuery;
	}

	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
		if (!resolver.Inner)
			return;

		resolver.UnsafeNarrowIndexedInner(ref _leftQuery);
	}
}

internal struct ReleaseNarrowedInnerProcessor : IResolverExecutor {
	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
		if (TResolver.SupportsNarrowOnly)
			resolver.ReleaseNarrowedInner();
	}
}

	// internal ref struct PrepareIndexedInnerProcessor<TLeftKey, TLeftValue, TExecutor>: IResolverExecutor
	// 	where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue> where TLeftKey : notnull {
	// 	private ref TExecutor _leftQuery;
	// 	private readonly bool _cloneOnAdd;
	// 	private readonly bool _shouldPool;
	// 	internal bool _hadHit;
	// 	private QueryResultsDisposer? _disposer;
	//
	// 	public PrepareIndexedInnerProcessor(ref TExecutor leftQuery,  bool cloneOnAdd, bool shouldPool, QueryResultsDisposer? disposer) {
	// 		_leftQuery = ref leftQuery;
	// 		_cloneOnAdd = cloneOnAdd;
	// 		_shouldPool = shouldPool;
	// 		_disposer = disposer;
	// 	}
	//
	//
	// 	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
	// 		if (!resolver.IndexedInner)
	// 			return;
	// 		_hadHit = true;
	// 		resolver.PrepareIndexedInner(ref _leftQuery, _cloneOnAdd, _shouldPool, _disposer);
	// 	}
	// }
	//
	// internal ref struct ExecuteIndexedInnerProcessor<TLeftKey, TResult>: IResolverExecutor
	// 	where TLeftKey : notnull, IEquatable<TLeftKey>
	// 	where TResult : IJoinResult {
	// 	private readonly bool _cloneOnAdd;
	// 	private readonly bool _shouldPool;
	// 	private ref ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> _results;
	// 	private QueryResultsDisposer? _disposer;
	//
	// 	public ExecuteIndexedInnerProcessor(ref ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> results, bool cloneOnAdd, bool shouldPool, QueryResultsDisposer? disposer) {
	// 		_cloneOnAdd = cloneOnAdd;
	// 		_shouldPool = shouldPool;
	// 		_disposer = disposer;
	// 		_results = ref results;
	// 	}
	//
	//
	// 	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
	// 		if (!resolver.IndexedInner)
	// 			return;
	// 		switch (position) {
	// 			case 1: {
	// 				var a = new UnsafeRightAccessor<TLeftKey, TResult>(ref _results);
	// 				resolver.UnsafeExecuteIndexedInner(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 			}
	// 				break;
	// 			case 2: {
	// 				var a = new UnsafeRight2Accessor<TLeftKey, TResult>(ref _results);
	// 				resolver.UnsafeExecuteIndexedInner(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 			}
	// 				break;
	// 			case 3: {
	// 				var a = new UnsafeRight3Accessor<TLeftKey, TResult>(ref _results);
	// 				resolver.UnsafeExecuteIndexedInner(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 			}
	// 				break;
	// 			case 4: {
	// 				var a = new UnsafeRight4Accessor<TLeftKey, TResult>(ref _results);
	// 				resolver.UnsafeExecuteIndexedInner(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 			}
	// 				break;
	// 			case 5: {
	// 				var a = new UnsafeRight5Accessor<TLeftKey, TResult>(ref _results);
	// 				resolver.UnsafeExecuteIndexedInner(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 			}
	// 				break;
	// 		}
	// 	}
	// }
	// internal ref struct ExecuteWithAccessorProcessor<TLeftKey, TResult> : IResolverExecutor
	// 	where TLeftKey : notnull, IEquatable<TLeftKey> where TResult : struct, IJoinResult {
	// 	private readonly bool _cloneOnAdd;
	// 	private readonly bool _shouldPool;
	// 	private readonly int _skip;
	// 	private readonly int _take;
	// 	private ref ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> _results;
	// 	private QueryResultsDisposer? _disposer;
	//
	// 	internal bool DidSort = false;
	//
	// 	public ExecuteWithAccessorProcessor(ref ValueDictionary<TLeftKey, TResult, DefaultKeyComparer<TLeftKey>> results,
	// 		int skip, int take,
	// 		bool cloneOnAdd,
	// 		bool shouldPool, QueryResultsDisposer? disposer) {
	// 		_skip = skip;
	// 		_take = take;
	// 		_cloneOnAdd = cloneOnAdd;
	// 		_shouldPool = shouldPool;
	// 		_disposer = disposer;
	// 		_results = ref results;
	// 	}
	//
	//
	// 	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
	// 		if (resolver.IsSorter) {
	// 			resolver.UnsafeSortResults(ref _results, _skip, _take);
	// 			DidSort = true;
	// 			return;
	// 		}
	//
	// 		switch (position) {
	// 			case 0: {
	// 				// handle only sorting
	//
	// 			}
	// 				break;
	// 			case 1: {
	// 				if (resolver is { IndexedInner: false, IsForward: false }) {
	// 					var a = new UnsafeRightAccessor<TLeftKey, TResult>(ref _results);
	// 					resolver.UnsafeExecuteWithAccessor(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 				}
	//
	// 				break;
	// 			}
	// 			case 2: {
	// 				if (resolver is { IndexedInner: false, IsForward: false }) {
	// 					var a = new UnsafeRight2Accessor<TLeftKey, TResult>(ref _results);
	// 					resolver.UnsafeExecuteWithAccessor(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 				}
	//
	// 				break;
	//
	// 			}
	// 			case 3: {
	// 				if (resolver is { IndexedInner: false, IsForward: false }) {
	// 					var a = new UnsafeRight3Accessor<TLeftKey, TResult>(ref _results);
	// 					resolver.UnsafeExecuteWithAccessor(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 				}
	//
	// 				break;
	// 			}
	// 			case 4: {
	// 				if (resolver is { IndexedInner: false, IsForward: false }) {
	// 					var a = new UnsafeRight4Accessor<TLeftKey, TResult>(ref _results);
	// 					resolver.UnsafeExecuteWithAccessor(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 				}
	//
	// 				break;
	//
	// 			}
	// 			case 5: {
	// 				if (resolver is { IndexedInner: false, IsForward: false }) {
	// 					var a = new UnsafeRight5Accessor<TLeftKey, TResult>(ref _results);
	// 					resolver.UnsafeExecuteWithAccessor(ref a, _cloneOnAdd, _shouldPool, _disposer);
	// 				}
	//
	// 				break;
	//
	// 			}
	// 		}
	// 	}
	// }

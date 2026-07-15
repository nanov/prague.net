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
	internal ref TResolver Resolver => ref Unsafe.AsRef(in _resolver);

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

	public readonly ReadOnlySpan<TLeftKey> Keys => _results.Keys;

	/// <summary>Execute joins for resolver 1 (reverse joins only — forward joins resolved in Add when active).</summary>
	public void ExecuteJoins() {
		var p = new ExecuteWithAccessorProcessor<TLeftKey, TResult>(ref _results, _skip, _take, _cloneOnAdd, _shouldPool, ref _disposer);
		_chainedResolvers.Execute(ref p);
		if (!p.DidSort && (_skip > 0 || _take < int.MaxValue))
			_results.Crop(_skip, _take);
	}

	/// <summary>Finalize results — placeholder for post-execute work; currently a no-op.</summary>
	public void FinalizeResults() {
	}

	public QueryResults<TResult> BuildResults() {
		if (_results.Count == 0) {
			// Empty result carries no buffer slices — return any pooled child buffers rented by
			// inner joins now, since the empty QueryResults won't own the disposer to do it.
			_disposer.Dispose();
			_disposer = default;
			return QueryResults<TResult>.EmptyWithTotalCount(TotalCount);
		}

		var offset = _results.Offset;
		var allResults = QueryResults<TResult>.FromArray(
			_results.ValuesArray ?? [], offset, _results.Count, TotalCount, _shouldPool, in _disposer);

		// Ownership of the values array and the child-buffer disposer moved into allResults —
		// the executor's finally HardDispose must not return them a second time.
		_results.UnsafeReleaseValues();
		_disposer = default;

		// Clone after slicing if needed
		if (_clone)
			allResults.CloneElements(new ResolveChainCloner<TResolverChain, TLeftValue, TResult>());

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

	// Cleanup for every non-hand-off exit (exception, Count, empty result): returns whatever
	// BuildResults/ExtractKeyedResults did not transfer — registered child buffers plus the
	// dict's metadata/keys/values. No-op after a successful hand-off (fields are defaulted).
	public void HardDispose() {
		_disposer.Dispose();
		_disposer = default;
		_results.Dispose(withValues: _shouldPool);
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
		var allResults = _results;
		_results = default; // ownership moves to the caller (or is disposed just below)

		if (_totalCount == 0 || _skip > _totalCount) {
			// Init already rented (before the predicate walk) — return it, no rows survived.
			allResults.Dispose();
			return QueryResults<TValue>
				.EmptyWithTotalCount(
					_totalCount);
		}

		if (TResolver.IsSorter)
			_chain.UnsafeSortResults(ref allResults, _skip, _take);
		else if (_skip > 0 || _take < int.MaxValue)
			allResults.SliceLeaveTotalCount(_skip, Math.Min(_take, _totalCount - _skip));

		return _clone ? allResults.CloneInPlace() : allResults;
	}

	/// <summary>Exception-path cleanup — no-op after BuildResults handed the results off.</summary>
	public void Dispose() => _results.Dispose();
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

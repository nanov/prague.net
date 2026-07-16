namespace Prague.Core;

using System.Buffers;
using System.Runtime.CompilerServices;
using Prague.Core.Collections;
using Utils;


public struct SortResolver<TLeftKey, TLeftValue, TResult, TComparer> : IJoinResolver<TLeftKey, TLeftValue>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TComparer : IComparer<TResult> {
	public static bool IsSorter { get; } = true;
	public bool Inner { get; } = false;

	private readonly TComparer _comparer;
	private readonly bool _isLeftValue;

	public SortResolver(TComparer comparer) {
		_comparer = comparer;
		_isLeftValue = typeof(TResult) == typeof(TLeftValue);
	}


	public void UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor executor, bool cloneOnAdd, bool isFirst,
		ref QueryResultsDisposer disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct where TExecutor : struct, IUnsafeCandidatesExecutor =>
		throw new NotImplementedException();

	public void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct =>
		throw new NotImplementedException();

	public void PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, IUnsafeCandidatesExecutor =>
		throw new NotImplementedException();

	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult
		=> throw new NotImplementedException();


	void IJoinResolver.UnsafeSortResults<TFullResult>(ref QueryResults<TFullResult> results, int skip, int take) {
		ref var res = ref Unsafe.As<QueryResults<TFullResult>, QueryResults<TResult>>(ref results);
		res.Sort(_comparer);
		if (skip > 0 || take < int.MaxValue)
			results.SliceLeaveTotalCount(skip, Math.Min(take, results.Count - skip));

	}

	void IJoinResolver.UnsafeSortResults<TKey, TFullResult>(ref ValueDictionary<TKey, TFullResult, DefaultKeyComparer<TKey>> results, int skip, int take){
		if (_isLeftValue)
			results.SortAndCrop(new LeftSortComparerWrapper<TFullResult>(_comparer), skip, take);
		else
			results.SortAndCrop(new JoinSortComparerWrapper<TFullResult>(_comparer), skip, take);
	}

	private struct LeftSortComparerWrapper<TFullResult> : IComparer<TFullResult>
		where TFullResult : struct, IJoinResult {
		private TComparer _comparer;

		public LeftSortComparerWrapper(TComparer comparer) => _comparer = comparer;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(TFullResult x, TFullResult y) {
			var a = x.UnsafeGetLeft<TResult>(); // Unsafe.As<TFullResult, TResult>(ref x);
			var b = y.UnsafeGetLeft<TResult>(); // Unsafe.As<TFullResult, TResult>(ref x);
			return _comparer.Compare(a, b);
		}
	}
	private struct JoinSortComparerWrapper<TFullResult> : IComparer<TFullResult>
		where TFullResult : struct, IJoinResult {
		private TComparer _comparer;

		public JoinSortComparerWrapper(TComparer comparer) => _comparer = comparer;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(TFullResult x, TFullResult y) {
			var a = Unsafe.As<TFullResult, TResult>(ref x);
			var b = Unsafe.As<TFullResult, TResult>(ref y);
			return _comparer.Compare(a, b);
		}
	}

}


public struct ResolveChainCloner<TLeftCloner, TResultChain, TLeftKey, TLeftValue, T1> : ICloner<JoinResult<TLeftValue, T1>>
	where TLeftCloner : struct, ICloner<TLeftValue>
	where TResultChain : IResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TLeftCloner _leftCloner;

	public ResolveChainCloner(TLeftCloner leftCloner) {
		_leftCloner = leftCloner;
	}

	public void Clone(ref JoinResult<TLeftValue, T1> value)
		=> TResultChain.CloneValue(_leftCloner, ref Unsafe.AsRef(in value.Left), ref Unsafe.AsRef(in value.Right)!);
}

public struct ResolveChainCloner<TLeftCloner, TResultChain, TLeftKey, TLeftValue, T1, T2> : ICloner<JoinResult<TLeftValue, T1, T2>>
	where TLeftCloner : struct, ICloner<TLeftValue>
	where TResultChain : IResolverChain<TLeftKey, TLeftValue, T1, T2>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TLeftCloner _leftCloner;

	public ResolveChainCloner(TLeftCloner leftCloner) {
		_leftCloner = leftCloner;
	}

	public void Clone(ref JoinResult<TLeftValue, T1, T2> value)
		=> TResultChain.CloneValue(_leftCloner, ref Unsafe.AsRef(in value.Left), ref Unsafe.AsRef(in value.Right)!, ref Unsafe.AsRef(in value.Right2)!);
}

public interface ISortableResolverChain {
	const int NoSort = -1;
}

public interface ISortableResolverChain<TLeftKey, TLeftValue>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	int SortLevel => ISortableResolverChain.NoSort;
	void SortResults(ref QueryResults<TLeftValue> results, int skip, int take);

	internal void SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		where TFullResult : IJoinResult<TLeftValue>;
}


public interface IResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {

	void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>;

	public abstract static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1)
		where TLeftCloner : struct, ICloner<TLeftValue>;
}

public interface ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	int SortLevel { get; }
	internal void SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		where TFullResult : IJoinResult<TLeftValue, T1>;
}

public interface ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	int SortLevel { get; }
	internal void SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		where TFullResult : IJoinResult<TLeftValue, T1, T2>;
}

public interface IResolverChain<TLeftKey, TLeftValue, T1, T2>
	: IResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {

	void PrepareIndexedInner2<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>;

	public abstract static void CloneValue<TLeftCloner>(
		TLeftCloner cloner, ref TLeftValue left, ref T1 val1, ref T2 val2) where TLeftCloner : struct, ICloner<TLeftValue>;
}

public struct ResolverChain<TLeftKey, TLeftValue, TResolver, T1>
	: IResolverChain<TLeftKey, TLeftValue, T1>,
	  ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TResolver : IJoinResolver<TLeftKey, TLeftValue, T1>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TResolver _resolver;
	public int SortLevel => ISortableResolverChain.NoSort;

	public ResolverChain(TResolver resolver) {
		_resolver = resolver;
	}

	public void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _resolver.PrepareIndexedInner(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	void ISortableResolverChain<TLeftKey, TLeftValue, T1>.SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take) { }

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1)
		where TLeftCloner : struct, ICloner<TLeftValue> {
		cloner.Clone(ref left);
		TResolver.CloneValue(ref val1);
	}

}

public struct ResolverChain<TLeftKey, TLeftValue, TZeroResolver, TResolver, T1>
	: IResolverChain<TLeftKey, TLeftValue, T1>,
	  ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TZeroResolver : ISortableResolverChain<TLeftKey, TLeftValue>
	where TResolver : IJoinResolver<TLeftKey, TLeftValue, T1>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private readonly TZeroResolver _zeroResolver;
	private TResolver _resolver;
	public int SortLevel => _zeroResolver.SortLevel;

	public ResolverChain(TZeroResolver zeroResolver, TResolver resolver) {
		_zeroResolver = zeroResolver;
		_resolver = resolver;
	}

	public void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _resolver.PrepareIndexedInner(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	void ISortableResolverChain<TLeftKey, TLeftValue, T1>.SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		=> _zeroResolver.SortResults(ref results, skip, take);

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1)
		where TLeftCloner : struct, ICloner<TLeftValue> {
		cloner.Clone(ref left);
		TResolver.CloneValue(ref val1);
	}

}


public struct ResolverChain<TLeftKey, TLeftValue, TPrev, TResolver, T1, T2> : IResolverChain<TLeftKey, TLeftValue, T1, T2>,
	ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TPrev : IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TResolver : IJoinResolver<TLeftKey, TLeftValue, T2>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TPrev _prev;
	private TResolver _resolver;

	public int SortLevel => _prev.SortLevel;
	public ResolverChain(TPrev prev, TResolver resolver) {
		_prev = prev;
		_resolver = resolver;
	}

	public void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _prev.PrepareIndexedInner1(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue missing_name, ref T1 val1) where TLeftCloner : struct, ICloner<TLeftValue> => throw new NotImplementedException();

	public void PrepareIndexedInner2<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _resolver.PrepareIndexedInner(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	void ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>.SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		=> _prev.SortResults(ref results, skip, take);

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1, ref T2 val2)
		where TLeftCloner : struct, ICloner<TLeftValue> {
		TPrev.CloneValue(cloner, ref left, ref val1);
		TResolver.CloneValue(ref val2);
	}

}


public struct SortComparerWrapper<TFullResult, TLeftValue,TComparer> : IComparer<TFullResult>
	where TFullResult : IJoinResult<TLeftValue>
	where TComparer : IComparer<TLeftValue> {
	private TComparer _comparer;

	public SortComparerWrapper(TComparer comparer) => _comparer = comparer;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare(TFullResult? x, TFullResult? y)
		=> _comparer.Compare(x!.Left, y!.Left);
}

public struct SortComparerWrapper<TFullResult, TLeftValue, T1, TComparer> : IComparer<TFullResult>
	where TFullResult : IJoinResult<TLeftValue, T1>
	where TComparer : IComparer<JoinResult<TLeftValue, T1>> {
	private TComparer _comparer;

	public SortComparerWrapper(TComparer comparer) => _comparer = comparer;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare(TFullResult? x, TFullResult? y)
		=> _comparer.Compare(new JoinResult<TLeftValue, T1>(x!.Left, x!.Right), new JoinResult<TLeftValue, T1>(y!.Left, y!.Right));
}

public struct SortedResolverChain<TLeftKey, TLeftValue, TPrev, TComparer, T1>
	: IResolverChain<TLeftKey, TLeftValue, T1>,
	  ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TPrev : IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
	where TComparer : IComparer<JoinResult<TLeftValue, T1>>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TPrev _prev;
	private TComparer _comparer;

	public int SortLevel => 1;

	public SortedResolverChain(TPrev prev, TComparer comparer) {
		_prev = prev;
		_comparer = comparer;
	}

	public void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _prev.PrepareIndexedInner1(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	void ISortableResolverChain<TLeftKey, TLeftValue, T1>.SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		=> results.SortAndCrop(new SortComparerWrapper<TFullResult, TLeftValue, T1, TComparer>(_comparer), skip, take);

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1)
		where TLeftCloner : struct, ICloner<TLeftValue>
		=> TPrev.CloneValue(cloner, ref left, ref val1);
}

public struct SortComparerWrapper<TFullResult, TLeftValue, T1, T2, TComparer> : IComparer<TFullResult>
	where TFullResult : IJoinResult<TLeftValue, T1, T2>
	where TComparer : IComparer<JoinResult<TLeftValue, T1, T2>> {
	private TComparer _comparer;

	public SortComparerWrapper(TComparer comparer) => _comparer = comparer;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare(TFullResult? x, TFullResult? y)
		=> _comparer.Compare(new JoinResult<TLeftValue, T1, T2>(x!.Left, x!.Right, x!.Right2), new JoinResult<TLeftValue, T1, T2>(y!.Left, y!.Right, y!.Right2));
}

public struct SortedResolverChain<TLeftKey, TLeftValue, TPrev, TComparer, T1, T2>
	: IResolverChain<TLeftKey, TLeftValue, T1, T2>,
	  ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>
	where TLeftKey : IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TPrev : IResolverChain<TLeftKey, TLeftValue, T1, T2>, ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>
	where TComparer : IComparer<JoinResult<TLeftValue, T1, T2>>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	private TPrev _prev;
	private TComparer _comparer;

	public int SortLevel => 2;

	public SortedResolverChain(TPrev prev, TComparer comparer) {
		_prev = prev;
		_comparer = comparer;
	}

	public void PrepareIndexedInner1<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _prev.PrepareIndexedInner1(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);
	public void PrepareIndexedInner2<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		=> _prev.PrepareIndexedInner2(ref leftQuery, cloneOnAdd, shouldPool, ref disposer);

	void ISortableResolverChain<TLeftKey, TLeftValue, T1, T2>.SortResults<TFullResult>(ref ValueDictionary<TLeftKey, TFullResult, DefaultKeyComparer<TLeftKey>> results, int skip, int take)
		=> results.SortAndCrop(new SortComparerWrapper<TFullResult, TLeftValue, T1, T2, TComparer>(_comparer), skip, take);

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1)
		where TLeftCloner : struct, ICloner<TLeftValue> => throw new NotImplementedException();

	public static void CloneValue<TLeftCloner>(TLeftCloner cloner, ref TLeftValue left, ref T1 val1, ref T2 val2)
		where TLeftCloner : struct, ICloner<TLeftValue>
		=> TPrev.CloneValue(cloner, ref left, ref val1, ref val2);
}



public struct BaseResolver<TKey, TValue> : IJoinResolver {
	public static bool IsSorter { get; } = false;
	public bool Inner { get; }

	public void UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor executor, bool cloneOnAdd, bool isFirst,
		ref QueryResultsDisposer disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct where TExecutor : struct, IUnsafeCandidatesExecutor =>
		throw new NotImplementedException();

	public void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct =>
		throw new NotImplementedException();

	public void PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, IUnsafeCandidatesExecutor =>
		throw new NotImplementedException();

	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		throw new NotImplementedException();
	}
}

/// <summary>
/// Zero-join resolver chain. Used by CacheQueryBuilderNew for non-join queries.
/// </summary>
public struct NoResolver<TKey, TValue> : ISortableResolverChain<TKey, TValue>
	where TKey : IEquatable<TKey>, IComparable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	public int SortLevel => ISortableResolverChain.NoSort;
	public void SortResults(ref QueryResults<TValue> results, int skip, int take) { }
	void ISortableResolverChain<TKey, TValue>.SortResults<TFullResult>(ref ValueDictionary<TKey, TFullResult, DefaultKeyComparer<TKey>> results, int skip, int take) { }
}

/// <summary>
/// Sorted zero-join resolver chain. Wraps a comparer for sorting non-join query results.
/// </summary>
public struct SortedNoResolver<TKey, TValue, TComparer> : ISortableResolverChain<TKey, TValue>
	where TKey : IEquatable<TKey>, IComparable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TComparer : IComparer<TValue> {
	private TComparer _comparer;
	public int SortLevel => 0;
	public SortedNoResolver(TComparer comparer) => _comparer = comparer;
	public void SortResults(ref QueryResults<TValue> results, int skip, int take) {
		results.Sort(_comparer);
		if (skip > 0 || take < int.MaxValue)
			results.SliceLeaveTotalCount(skip, Math.Min(take, results.Count - skip));
	}
	void ISortableResolverChain<TKey, TValue>.SortResults<TFullResult>(ref ValueDictionary<TKey, TFullResult, DefaultKeyComparer<TKey>> results, int skip, int take)
		=> results.SortAndCrop(new SortComparerWrapper<TFullResult, TValue, TComparer>(_comparer), skip, take);
}

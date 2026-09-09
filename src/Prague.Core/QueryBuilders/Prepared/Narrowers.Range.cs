namespace Prague.Core;

using System.Runtime.CompilerServices;
using QueryBuilders;

/// <summary>Range index narrowing with the bounds fixed at build time: the range lambda is invoked at replay, as eager.</summary>
public readonly struct RangeNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey>
	where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> _rangeBuilder;

	public RangeNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder) {
		_index = index;
		_rangeBuilder = rangeBuilder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _rangeBuilder);
}

/// <summary>
///   Range index narrowing with bounds taken from the execution arguments. The lambda has the eager
///   <c>(rb, args) =&gt; …</c> shape and the query arguments are passed straight through to the eager
///   args overload, so no adaptor delegate or closure exists on the execution path.
/// </summary>
public readonly struct RangeArgNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey>
	where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> _rangeBuilder;

	public RangeArgNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder) {
		_index = index;
		_rangeBuilder = rangeBuilder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _rangeBuilder, args);
}

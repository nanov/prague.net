namespace Prague.Core;

using TypeSystem;

/// <summary>Range-index narrowing of the prepared builder; see <see cref="PreparedQueryBuilderExtensions" />.</summary>
public static class PreparedQueryBuilderRangeExtensions {
	/// <summary>Range index narrowing with bounds fixed now, e.g. <c>rb =&gt; rb.Gte(10).Lt(20)</c>.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey, TQueryBuilder>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : IComparable<TIndexKey>
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		=> PreparedQueryBuilderExtensions.Link(in builder, new RangeNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs>(index, rangeBuilder));

	/// <summary>Range index narrowing with bounds taken from the execution arguments, the eager <c>(rb, args) =&gt; rb.Gte(args.min)</c> shape (use a static lambda).</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeArgNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey, TQueryBuilder>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : IComparable<TIndexKey>
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		=> PreparedQueryBuilderExtensions.Link(in builder, new RangeArgNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs>(index, rangeBuilder));
}

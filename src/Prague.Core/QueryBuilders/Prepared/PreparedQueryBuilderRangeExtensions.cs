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

/// <summary>
///   Optional-bounds range narrowing: "from and/or to" in one step, which the type-state
///   <see cref="RangeQueryBuilder{TIndexKey}" /> cannot spell in one lambda. A <c>null</c> bound is
///   the open side; both <c>null</c> is no narrowing at all (the core is not called — see
///   <see cref="OptionalRange{TIndexKey}" />). Value-type and reference-type keys are separate
///   overloads because <c>TIndexKey?</c> means <see cref="Nullable{T}" /> for the one and an annotated
///   reference for the other; overload resolution picks by constraint, so callers see one <c>UseIndex</c>.
/// </summary>
public static class PreparedQueryBuilderOptionalRangeExtensions {
	/// <summary>Optional bounds selected from the execution arguments (use static lambdas), value-type key.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeOptionalArgNarrower<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, TIndexKey?> from,
			Func<TArgs, TIndexKey?> to,
			bool fromInclusive = true,
			bool toInclusive = true)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : struct, IComparable<TIndexKey> {
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		return PreparedQueryBuilderExtensions.Link(in builder, new RangeOptionalArgNarrower<TKey, TValue, TIndexKey, TArgs>(index, from, to, fromInclusive, toInclusive));
	}

	/// <summary>Optional bounds selected from the execution arguments (use static lambdas), reference-type key.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeOptionalRefArgNarrower<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, TIndexKey?> from,
			Func<TArgs, TIndexKey?> to,
			bool fromInclusive = true,
			bool toInclusive = true)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : class, IComparable<TIndexKey> {
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		return PreparedQueryBuilderExtensions.Link(in builder, new RangeOptionalRefArgNarrower<TKey, TValue, TIndexKey, TArgs>(index, from, to, fromInclusive, toInclusive));
	}

	/// <summary>Optional bounds fixed at build time, value-type key. Both <c>null</c> records a no-op step.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeOptionalNarrower<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			TIndexKey? from,
			TIndexKey? to,
			bool fromInclusive = true,
			bool toInclusive = true)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : struct, IComparable<TIndexKey>
		=> PreparedQueryBuilderExtensions.Link(in builder, new RangeOptionalNarrower<TKey, TValue, TIndexKey, TArgs>(index,
			new OptionalRange<TIndexKey>(OptionalRange<TIndexKey>.Bound(from.HasValue, from.GetValueOrDefault(), fromInclusive), OptionalRange<TIndexKey>.Bound(to.HasValue, to.GetValueOrDefault(), toInclusive))));

	/// <summary>Optional bounds fixed at build time, reference-type key. Both <c>null</c> records a no-op step.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, RangeOptionalNarrower<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			TIndexKey? from,
			TIndexKey? to,
			bool fromInclusive = true,
			bool toInclusive = true)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : class, IComparable<TIndexKey>
		=> PreparedQueryBuilderExtensions.Link(in builder, new RangeOptionalNarrower<TKey, TValue, TIndexKey, TArgs>(index,
			new OptionalRange<TIndexKey>(OptionalRange<TIndexKey>.Bound(from is not null, from!, fromInclusive), OptionalRange<TIndexKey>.Bound(to is not null, to!, toInclusive))));
}

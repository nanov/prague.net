namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>
///   <c>Sort</c> / <c>SortBounded</c> on the prepared builder. The eager overloads are constrained on
///   <see cref="ICandidatesFilterer{TKey,TValue}" />, which the recorder deliberately lacks, so these
///   rebind them on the prepared discriminator and left query. Each constructs the very
///   <see cref="SortResolver{TLeftKey,TLeftValue,TResult,TComparer}" /> its eager twin constructs
///   (same <c>allowBounded</c> flag) and appends it the same way, so the resolver chain a sorted
///   prepared query replays is byte-for-byte the eager one. The result is discriminated by
///   <see cref="SortedQuery{T}" />, which admits only joins and <c>Build()</c>: narrowing or filtering
///   after a sort is a compile error here exactly as it is on the eager builder. Generic over the
///   carrier type <c>TCache</c> (the raw cache, or a generated wrapper), which the sorted discriminator
///   keeps so the generated <c>Sort → JoinWith{T}</c> overloads still see their <c>ICacheCarrier</c>.
/// </summary>
public static class PreparedQueryBuilderSortExtensions {
	/// <summary>
	///   Classic sort: the matched rows are materialized and fully sorted, then sliced if the execution
	///   was given a page. Mirrors the eager <c>Sort</c>; see it for when to prefer <c>SortBounded</c>.
	/// </summary>
	public static
		CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue,
			Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		Sort<TCache, TKey, TValue, TArgs, TChain, TResult, TComparer>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TResult> builder,
			TComparer comparer)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TComparer : IComparer<TResult> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<PreparedQueryDiscriminator<TCache>>(builder._discriminator),
			new Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>(resolver));
	}

	/// <summary>Classic sort appended after a join chain. Mirrors the eager chained <c>Sort</c>.</summary>
	public static
		CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue,
			Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		Sort<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TComparer>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			TComparer comparer)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TComparer : IComparer<TResult> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<PreparedQueryDiscriminator<TCache>>(builder._discriminator),
			new Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>(builder._resolverChain, resolver));
	}

	/// <summary>
	///   Sort opting in to the bounded top-K plan for finite pages; ties resolve by encounter order, so
	///   consecutive pages of an unchanged result partition it. Mirrors the eager <c>SortBounded</c> —
	///   see it for the plan's cost profile and when the classic <c>Sort</c> is the better choice.
	/// </summary>
	public static
		CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue,
			Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		SortBounded<TCache, TKey, TValue, TArgs, TChain, TResult, TComparer>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TResult> builder,
			TComparer comparer)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TComparer : IComparer<TResult> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer, allowBounded: true);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<PreparedQueryDiscriminator<TCache>>(builder._discriminator),
			new Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>(resolver));
	}

	/// <summary>Bounded sort appended after a join chain. Mirrors the eager chained <c>SortBounded</c>.</summary>
	public static
		CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue,
			Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		SortBounded<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TComparer>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			TComparer comparer)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TComparer : IComparer<TResult> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer, allowBounded: true);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<PreparedQueryDiscriminator<TCache>>(builder._discriminator),
			new Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>(builder._resolverChain, resolver));
	}

	// ── Terminal ─────────────────────────────────────────────────────────────────

	/// <summary>
	///   Freezes a sorted simple (no-join) description into a reusable command. Executions route
	///   through the eager sorted terminals' core (<c>ExecuteCoreSimpleTop</c>), so a <c>SortBounded</c>
	///   page is bounded and a <c>Sort</c> or unbounded page runs the classic pipeline, exactly as eager.
	/// </summary>
	public static PreparedQuery<TArgs, TValue> Build<TCache, TKey, TValue, TArgs, TChain, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<TResolver>, TValue> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		=> new PreparedSimpleQuery<TKey, TValue, TArgs, TChain, TResolver, TopSimplePlan>(builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain);

	/// <summary>
	///   Freezes a sorted joined description into a reusable command. Executions route through the
	///   eager sorted joined terminals' core (<c>ExecuteCoreJoinedTop</c>): a <c>SortBounded</c> placed
	///   before the joins bounds a finite page, every other shape runs the classic joined pipeline,
	///   exactly as eager. Overload selection and the join-filter limitation are as described on the
	///   unsorted joined <c>Build()</c>.
	/// </summary>
	public static PreparedQuery<TArgs, TResult> Build<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> new PreparedJoinedQuery<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TopJoinedPlan>(
			builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, builder._manyCount);

	// ── Frozen terminal ──────────────────────────────────────────────────────────

	/// <summary>The sorted simple <c>Build()</c> with plan metadata; sorted shapes always replay in stage 1.</summary>
	public static FrozenQuery<TArgs, TValue> BuildFrozen<TCache, TKey, TValue, TArgs, TChain, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<TResolver>, TValue> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		=> FrozenPlanner.Simple<TKey, TValue, TArgs, TChain, TResolver, TopSimplePlan>(builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, true);

	/// <summary>The sorted joined <c>Build()</c> with plan metadata; sorted joined shapes always replay in stage 1.</summary>
	public static FrozenQuery<TArgs, TResult> BuildFrozen<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<PreparedQueryDiscriminator<TCache>>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> FrozenPlanner.Joined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TopJoinedPlan>(
			builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, builder._manyCount, true);
}

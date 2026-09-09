namespace Prague.Core;

using TypeSystem;

/// <summary>
///   <c>Or</c> on the prepared builder: <c>outer ∩ (b1 ∪ b2)</c>, or just <c>b1 ∪ b2</c> when nothing
///   was narrowed before, exactly as eager. Each branch lambda receives a fresh prepared branch builder
///   (discriminated by <see cref="PreparedNarrowOnly{TCache}" />, so only <c>UseIndex</c> and a nested
///   <c>Or</c> bind) and returns it with its own recorded chain; the lambdas run once, here, and the two
///   chains are frozen into one <see cref="OrNarrower{TKey,TValue,TArgs,TBranch1,TBranch2}" /> link. A
///   branch may read the execution arguments through the parameterized <c>UseIndex</c> overloads, so
///   the disjunction itself can be parameterized; a branch returned unchanged (<c>b =&gt; b</c>) is the
///   eager no-op branch and is excluded from the union by the core.
/// </summary>
public static class PreparedQueryBuilderOrExtensions {
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Or<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TBranch1, TBranch2>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch1>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b1,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch2>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b2)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs> {
		ArgumentNullException.ThrowIfNull(b1);
		ArgumentNullException.ThrowIfNull(b2);
		var cache = builder._leftQuery._cache;
		var seed = new CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>(
			new PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>(cache),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(default),
			0);
		var branch1 = b1(seed)._leftQuery._chain;
		var branch2 = b2(seed)._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>(cache, in branch1, in branch2));
	}
}

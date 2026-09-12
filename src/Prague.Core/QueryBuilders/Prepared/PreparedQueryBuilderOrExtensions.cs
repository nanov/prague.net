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
///   <para>
///   The branch discriminator carries the enclosing query's <c>TCache</c> and carrier value (the raw
///   cache, or the generated wrapper), so the generated <c>WithXxx</c> extensions — scoped by
///   <see cref="ICacheCarrier{TCache}" /> of the wrapper — bind inside a branch exactly as at top level.
///   C# does not infer a type argument from a constraint, so the receiver's discriminator is spelled
///   out per placement (top level, inside an <c>Or</c> branch, inside an <c>If</c> branch) rather than
///   bound through <c>TDiscriminator : ICacheCarrier&lt;TCache&gt;</c>; all three forward to one core.
///   </para>
/// </summary>
public static class PreparedQueryBuilderOrExtensions {
	/// <summary>Disjunctive narrowing on the top-level prepared builder.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Or<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TBranch1, TBranch2>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch1>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b1,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch2>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b2)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> OrCore(in builder, builder._discriminator.Cache, b1, b2);

	/// <summary>Nested disjunction inside an <c>Or</c> branch.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Or<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TBranch1, TBranch2>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch1>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b1,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch2>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b2)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> OrCore(in builder, builder._discriminator.Cache, b1, b2);

	/// <summary>Disjunction inside an <c>If</c> / <c>IfElse</c> branch.</summary>
	public static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Or<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TBranch1, TBranch2>(
			this in CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch1>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b1,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch2>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b2)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> OrCore(in builder, builder._discriminator.Cache, b1, b2);

	// ── Core ─────────────────────────────────────────────────────────────────────
	//
	// The branch lambdas run once, at build time, against an empty narrow-only recorder over the
	// enclosing query's cache, discriminated by the enclosing carrier; the resolver chain and result
	// type only type the seed (a branch cannot join or execute).

	private static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		OrCore<TDiscriminator, TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TBranch1, TBranch2>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			TCache carrier,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch1>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b1,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TBranch2>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> b2)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(b1);
		ArgumentNullException.ThrowIfNull(b2);
		var cache = builder._leftQuery._cache;
		var seed = PreparedBranchSeeds.NarrowOnly<TCache, TKey, TValue, TArgs>(carrier, cache);
		var branch1 = b1(seed)._leftQuery._chain;
		var branch2 = b2(seed)._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2>(cache, in branch1, in branch2));
	}
}

/// <summary>
///   Empty branch recorders handed to <c>Or</c> / <c>If</c> branch lambdas. Each carries the enclosing
///   query's carrier in its discriminator and the enclosing query's cache in its recorder, so a
///   branch narrows the same cache under the same <see cref="ICacheCarrier{TCache}" /> scope.
/// </summary>
internal static class PreparedBranchSeeds {
	internal static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>
		NarrowOnly<TCache, TKey, TValue, TArgs>(TCache carrier, InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TArgs : struct
		=> new(new PreparedNarrowOnly<TCache>(carrier),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(default),
			0);

	internal static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>
		Conditional<TCache, TKey, TValue, TArgs>(TCache carrier, InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TArgs : struct
		=> new(new PreparedConditionalBranch<TCache>(carrier),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(default),
			0);
}

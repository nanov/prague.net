namespace Prague.Core;

using TypeSystem;

/// <summary>
///   <c>If</c> / <c>IfElse</c> on the prepared builder: conditional narrowing decided per execution
///   from the arguments, the prepared twin of an eager query built with a C# <c>if</c> around a
///   type-preserving <c>UseIndex</c> / <c>Where</c> reassignment. The branch lambda receives a fresh
///   prepared branch builder over the same cache, runs once here, and the chain it returns is frozen
///   into one <see cref="IfNarrower{TKey,TValue,TArgs,TSub}" /> (or
///   <see cref="IfElseNarrower{TKey,TValue,TArgs,TThen,TElse}" />) link; the returned builder keeps the
///   enclosing discriminator and resolver chain because a conditional never changes the query's shape.
///   <para>
///   Two overload families, selected by the receiver's discriminator. On a top-level builder or inside
///   another conditional (<see cref="IBaseFilterable" />) the branch is discriminated by
///   <see cref="PreparedConditionalBranch{TCache}" />, so <c>UseIndex</c>, <c>Where</c>, <c>Or</c> and a
///   nested <c>If</c> bind. Inside an <c>Or</c> branch (<see cref="PreparedNarrowOnly{TCache}" />, which
///   is not <see cref="IBaseFilterable" />, so the first family cannot bind) the conditional branch is
///   itself narrow-only: an <c>Or</c> branch may never filter, conditionally or not.
///   </para>
/// </summary>
public static class PreparedQueryBuilderIfExtensions {
	// ── Top-level / nested-conditional receivers ─────────────────────────────────

	/// <summary>Replays <paramref name="branch" /> only when <paramref name="condition" /> holds for the execution arguments.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, IfNarrower<TKey, TValue, TArgs, TSub>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		If<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TDiscriminator : struct, IBaseFilterable
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs> {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(branch);
		var cache = builder._leftQuery._cache;
		var sub = branch(FilterableSeed<TKey, TValue, TArgs>(cache))._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new IfNarrower<TKey, TValue, TArgs, TSub>(condition, in sub));
	}

	/// <summary>Replays <paramref name="then" /> when <paramref name="condition" /> holds for the execution arguments, <paramref name="otherwise" /> when it does not.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, IfElseNarrower<TKey, TValue, TArgs, TThen, TElse>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElse<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TDiscriminator : struct, IBaseFilterable
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs> {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(then);
		ArgumentNullException.ThrowIfNull(otherwise);
		var cache = builder._leftQuery._cache;
		var seed = FilterableSeed<TKey, TValue, TArgs>(cache);
		var thenChain = then(seed)._leftQuery._chain;
		var elseChain = otherwise(seed)._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new IfElseNarrower<TKey, TValue, TArgs, TThen, TElse>(condition, in thenChain, in elseChain));
	}

	// ── Or-branch receivers (narrow-only) ────────────────────────────────────────

	/// <summary>Conditional narrowing inside an <c>Or</c> branch: the branch is narrow-only, as the enclosing <c>Or</c> branch is.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, IfNarrower<TKey, TValue, TArgs, TSub>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		If<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs> {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(branch);
		var cache = builder._leftQuery._cache;
		var sub = branch(NarrowOnlySeed<TKey, TValue, TArgs>(cache))._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new IfNarrower<TKey, TValue, TArgs, TSub>(condition, in sub));
	}

	/// <summary>Two-way conditional narrowing inside an <c>Or</c> branch: both branches are narrow-only, as the enclosing <c>Or</c> branch is.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, IfElseNarrower<TKey, TValue, TArgs, TThen, TElse>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElse<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs> {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(then);
		ArgumentNullException.ThrowIfNull(otherwise);
		var cache = builder._leftQuery._cache;
		var seed = NarrowOnlySeed<TKey, TValue, TArgs>(cache);
		var thenChain = then(seed)._leftQuery._chain;
		var elseChain = otherwise(seed)._leftQuery._chain;
		return PreparedQueryBuilderExtensions.Link(in builder, new IfElseNarrower<TKey, TValue, TArgs, TThen, TElse>(condition, in thenChain, in elseChain));
	}

	// ── Seeds ────────────────────────────────────────────────────────────────────
	//
	// The branch lambdas run once, at build time, against an empty recorder over the enclosing query's
	// cache; the resolver chain and result type only type the builder (a branch cannot join or execute).

	private static CacheQueryBuilderCombined<PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>
		FilterableSeed<TKey, TValue, TArgs>(InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> new(new PreparedConditionalBranch<InMemoryDataCache<TKey, TValue>>(cache),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(default),
			0);

	private static CacheQueryBuilderCombined<PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>
		NarrowOnlySeed<TKey, TValue, TArgs>(InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> new(new PreparedNarrowOnly<InMemoryDataCache<TKey, TValue>>(cache),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(default),
			0);
}

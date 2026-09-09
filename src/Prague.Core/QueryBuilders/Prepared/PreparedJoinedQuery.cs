namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>
///   Which joined execution core a prepared joined query hands its replayed builder to. The same
///   static abstract strategy as <see cref="IPreparedSimplePlan" />: the routing is folded per closed
///   type, so the unsorted command is a direct <c>ExecuteCoreJoined</c> call with no runtime flag.
/// </summary>
internal interface IPreparedJoinedPlan {
	static abstract QueryResults<TResult> Execute<TKey, TValue, TResolverChain, TResult>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>;
}

/// <summary>Unsorted joined shape: the eager joined terminals' <c>ExecuteCoreJoined</c>.</summary>
internal readonly struct ClassicJoinedPlan : IPreparedJoinedPlan {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<TResult> Execute<TKey, TValue, TResolverChain, TResult>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> builder.ExecuteCoreJoined<TResult>(pool, clone, skip, take);
}

/// <summary>
///   Sorted joined shape: the eager sorted joined terminals' <c>ExecuteCoreJoinedTop</c>, which bounds
///   a finite page when the chain shape allows it and itself falls back to <c>ExecuteCoreJoined</c>
///   otherwise — the eager routing, reproduced rather than re-decided here.
/// </summary>
internal readonly struct TopJoinedPlan : IPreparedJoinedPlan {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<TResult> Execute<TKey, TValue, TResolverChain, TResult>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> builder.ExecuteCoreJoinedTop<TResult>(pool, clone, skip, take);
}

/// <summary>
///   Joined prepared query. Each execution replays the recorded chain into a fresh, stack-local eager
///   core FIRST, then wraps that core in the ordinary combined builder together with a copy of the
///   recorded resolver chain, and hands the builder to the eager joined core chosen by
///   <typeparamref name="TPlan" />. The ordering is load-bearing: an inner join's
///   <c>PrepareIndexedInner</c> reads the executor's candidates before base execution, so the executor
///   the joined core sees must already be the replayed eager core — never the recorder. The resolver
///   chain is copied by value per execution because resolvers carry per-execution scratch; the stored
///   chain, resolvers and many-count are never mutated.
/// </summary>
internal sealed class PreparedJoinedQuery<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan> : PreparedQuery<TArgs, TResult>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
	where TResolverChain : struct, IResolvers
	where TResult : struct, IJoinResult<TValue>
	where TPlan : struct, IPreparedJoinedPlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TChain _chain;
	private readonly TResolverChain _resolvers;
	// What the eager join extensions accumulated through AddResolver(isMany): the joined container
	// sizes its pooled 1:N slots from it, so it travels with the chain it describes.
	private readonly int _manyCount;

	internal PreparedJoinedQuery(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
		_manyCount = manyCount;
	}

	public override QueryResults<TResult> Execute(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, false, false, skip, take);

	public override QueryResults<TResult> ExecuteCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, false, true, skip, take);

	public override QueryResults<TResult> ExecutePooled(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, true, false, skip, take);

	public override QueryResults<TResult> ExecutePooledCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, true, true, skip, take);

	public override int Count(in TArgs args)
		=> PreparedReplay.CountJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(_cache, in _chain, in _resolvers, _manyCount, in args);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private QueryResults<TResult> Run(in TArgs args, bool pool, bool clone, int skip, int take)
		=> PreparedReplay.RunJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(_cache, in _chain, in _resolvers, _manyCount, in args, pool, clone, skip, take);
}

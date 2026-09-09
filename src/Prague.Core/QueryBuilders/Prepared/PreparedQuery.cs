namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>
///   A query described once and executed on demand: the command form of a cache query. Immutable
///   after <c>Build()</c>, safe to store in a field and to execute concurrently from any thread.
///   Reads keep the cache's usual semantics (lock-free, documented staleness); the results are the
///   same <see cref="QueryResults{T}" /> the eager builder returns and follow the same
///   <c>Dispose()</c> contract on pooled paths.
///   The abstract base exists only to give the unnameable closed builder type a storable name — the
///   single allocation of a prepared query is the derived instance created by <c>Build()</c>.
/// </summary>
public abstract class PreparedQuery<TArgs, TResult> {
	public abstract QueryResults<TResult> Execute(in TArgs args, int skip = 0, int take = int.MaxValue);

	public abstract QueryResults<TResult> ExecuteCloned(in TArgs args, int skip = 0, int take = int.MaxValue);

	public abstract QueryResults<TResult> ExecutePooled(in TArgs args, int skip = 0, int take = int.MaxValue);

	public abstract QueryResults<TResult> ExecutePooledCloned(in TArgs args, int skip = 0, int take = int.MaxValue);

	public abstract int Count(in TArgs args);
}

public static class PreparedQueryNoArgsExtensions {
	public static QueryResults<TResult> Execute<TResult>(this PreparedQuery<NoArgs, TResult> query, int skip = 0, int take = int.MaxValue)
		=> query.Execute(default, skip, take);

	public static QueryResults<TResult> ExecuteCloned<TResult>(this PreparedQuery<NoArgs, TResult> query, int skip = 0, int take = int.MaxValue)
		=> query.ExecuteCloned(default, skip, take);

	public static QueryResults<TResult> ExecutePooled<TResult>(this PreparedQuery<NoArgs, TResult> query, int skip = 0, int take = int.MaxValue)
		=> query.ExecutePooled(default, skip, take);

	public static QueryResults<TResult> ExecutePooledCloned<TResult>(this PreparedQuery<NoArgs, TResult> query, int skip = 0, int take = int.MaxValue)
		=> query.ExecutePooledCloned(default, skip, take);

	public static int Count<TResult>(this PreparedQuery<NoArgs, TResult> query) => query.Count(default);
}

/// <summary>
///   Which simple execution core a prepared simple query hands its replayed builder to. A static
///   abstract strategy rather than a flag so the choice is folded per closed type: the unsorted
///   command compiles to the same direct <c>ExecuteCoreSimple</c> call it made before sorting existed.
/// </summary>
internal interface IPreparedSimplePlan {
	static abstract QueryResults<TValue> Execute<TKey, TValue, TResolver>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolver : struct, IJoinResolver;
}

/// <summary>Unsorted shape: the eager unsorted terminals' <c>ExecuteCoreSimple</c>.</summary>
internal readonly struct ClassicSimplePlan : IPreparedSimplePlan {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<TValue> Execute<TKey, TValue, TResolver>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolver : struct, IJoinResolver
		=> builder.ExecuteCoreSimple(ref builder._resolverChain.Resolver, pool, clone, skip, take);
}

/// <summary>
///   Sorted shape: the eager sorted terminals' <c>ExecuteCoreSimpleTop</c>, which bounds a finite page
///   of a <c>SortBounded</c> query and itself falls back to <c>ExecuteCoreSimple</c> for <c>Sort</c>
///   and for unbounded pages — the eager routing, reproduced rather than re-decided here.
/// </summary>
internal readonly struct TopSimplePlan : IPreparedSimplePlan {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<TValue> Execute<TKey, TValue, TResolver>(
		ref CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue> builder,
		bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResolver : struct, IJoinResolver
		=> builder.ExecuteCoreSimpleTop(ref builder._resolverChain.Resolver, pool, clone, skip, take);
}

/// <summary>
///   Simple (no-join) prepared query. Each execution replays the recorded chain into a fresh,
///   stack-local eager core and hands that core — wrapped in the ordinary combined builder — to the
///   ordinary execution core chosen by <typeparamref name="TPlan" />, so there is no second query
///   engine: results are the eager builder's by construction. The stored chain and resolver are
///   never mutated.
/// </summary>
internal sealed class PreparedSimpleQuery<TKey, TValue, TArgs, TChain, TResolver, TPlan> : PreparedQuery<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
	where TResolver : struct, IJoinResolver
	where TPlan : struct, IPreparedSimplePlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TChain _chain;
	private readonly Resolvers<TResolver> _resolvers;

	internal PreparedSimpleQuery(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
	}

	public override QueryResults<TValue> Execute(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, false, false, skip, take);

	public override QueryResults<TValue> ExecuteCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, false, true, skip, take);

	public override QueryResults<TValue> ExecutePooled(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, true, false, skip, take);

	public override QueryResults<TValue> ExecutePooledCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> Run(in args, true, true, skip, take);

	public override int Count(in TArgs args) => PreparedReplay.CountSimple(_cache, in _chain, in args);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private QueryResults<TValue> Run(in TArgs args, bool pool, bool clone, int skip, int take)
		=> PreparedReplay.RunSimple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(_cache, in _chain, in _resolvers, in args, pool, clone, skip, take);
}

/// <summary>
///   The one replay routine every prepared shape (simple and joined) runs: a fresh eager core, the
///   recorded chain applied to it in build order, and the core's rented candidates released if a
///   narrower throws mid-replay so a throwing selector or index never strands them. The <c>Run*</c> /
///   <c>Count*</c> routines wrap it in the execution the eager terminals run, shared by the
///   <c>PreparedQuery</c> classes and the frozen replay executors so there is one replay path.
/// </summary>
internal static class PreparedReplay {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static CacheQueryBuilderCoreCombined<TKey, TValue> Into<TKey, TValue, TArgs, TChain>(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs> {
		var core = new CacheQueryBuilderCoreCombined<TKey, TValue>(cache);
		try {
			chain.Replay(ref core, in args);
		} catch {
			core.Dispose();
			throw;
		}

		return core;
	}

	// The predicate-pool mark/reset brackets the whole execution, not just the replay: the core
	// applies its filter while counting / executing, so a rented arg-predicate must outlive replay.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static QueryResults<TValue> RunSimple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers, in TArgs args, bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		where TPlan : struct, IPreparedSimplePlan {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		try {
			// The eager path's Query() also copies the core into the combined builder; the copy inside
			// `builder` is the one executed and disposed, the replayed core is not touched again.
			var builder = new CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
				CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue>(
				new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(cache), Into(cache, in chain, in args), resolvers, 0);
			return TPlan.Execute(ref builder, pool, clone, skip, take);
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	// Both the unsorted and the sorted eager Count terminals route to CountCoreSimple, which is the
	// core's Count(): a sorter never changes how many rows match.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int CountSimple<TKey, TValue, TArgs, TChain>(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs> {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		try {
			var core = Into(cache, in chain, in args);
			return core.Count();
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	// Joined: the chain is replayed into the eager core FIRST, then wrapped together with a copy of the
	// resolver chain — an inner join's PrepareIndexedInner reads the executor's candidates before base
	// execution, so the executor the joined core sees must already be the replayed core. The mark /
	// reset brackets the whole execution because that inner pass applies the filter too.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static QueryResults<TResult> RunJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, in TArgs args, bool pool, bool clone, int skip, int take)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TPlan : struct, IPreparedJoinedPlan {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		try {
			var builder = WrapJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(cache, in chain, in resolvers, manyCount, in args);
			return TPlan.Execute(ref builder, pool, clone, skip, take);
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	// Both the unsorted and the sorted eager joined Count terminals route to CountCoreJoined, which
	// runs the indexed-inner narrowing so inner joins count matched lefts only.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int CountJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue> {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		try {
			var builder = WrapJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(cache, in chain, in resolvers, manyCount, in args);
			return builder.CountCoreJoined<TResult>();
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult>
		WrapJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> new(new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(cache), Into(cache, in chain, in args), resolvers, manyCount);
}

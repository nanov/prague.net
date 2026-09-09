namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;
using TypeSystem;

/// <summary>
///   A prepared query whose recorded chain was flattened at build time into inspectable plan
///   metadata (<see cref="PlanInfo" />) and, when the plan shape allows it, bound to a specialized
///   executor instead of the general replay. A drop-in for <see cref="PreparedQuery{TArgs,TResult}" />:
///   same terminals, same results, same thread-safety. <see cref="Explain" /> is the cold-path
///   inspection surface; the typed chain is kept alongside the metadata so the replay fallback (and
///   future specialized executors that still want JIT-specialized sub-steps) have it.
/// </summary>
public abstract class FrozenQuery<TArgs, TResult> : PreparedQuery<TArgs, TResult> {
	internal abstract PlanInfo Plan { get; }

	/// <summary>The flattened ops and the executor <c>BuildFrozen()</c> chose. Cold path; allocates.</summary>
	public string Explain() {
		var sb = new StringBuilder();
		sb.Append("FrozenQuery<").Append(typeof(TArgs).Name).Append(", ").Append(typeof(TResult).Name).AppendLine(">");
		sb.Append(Plan.Explain());
		return sb.ToString();
	}
}

/// <summary>
///   One execution strategy of a frozen query. A static-abstract strategy carried as a struct type
///   parameter (the <see cref="IPreparedSimplePlan" /> pattern) so the frozen class's single virtual
///   <c>Execute</c> lands in a body that is fully specialized per executor — the constrained call on
///   the struct field devirtualizes and inlines.
/// </summary>
internal interface IFrozenExecutor<TArgs, TResult> {
	static abstract string Name { get; }

	QueryResults<TResult> Execute(in TArgs args, bool pool, bool clone, int skip, int take);

	int Count(in TArgs args);
}

internal sealed class FrozenQuery<TArgs, TResult, TExecutor> : FrozenQuery<TArgs, TResult>
	where TExecutor : struct, IFrozenExecutor<TArgs, TResult> {
	// Not readonly on purpose: an interface call on a readonly field whose type is a struct type
	// parameter makes the compiler copy the whole executor (chain included) per execution. The field is
	// never written after construction.
	private TExecutor _executor;
	private readonly PlanInfo _plan;

	internal FrozenQuery(in TExecutor executor, IReadOnlyList<NarrowerDescriptor> narrowers, bool hasResolvers, bool isSorted) {
		_executor = executor;
		_plan = new PlanInfo(narrowers, hasResolvers, isSorted, TExecutor.Name);
	}

	internal override PlanInfo Plan => _plan;

	public override QueryResults<TResult> Execute(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> _executor.Execute(in args, false, false, skip, take);

	public override QueryResults<TResult> ExecuteCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> _executor.Execute(in args, false, true, skip, take);

	public override QueryResults<TResult> ExecutePooled(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> _executor.Execute(in args, true, false, skip, take);

	public override QueryResults<TResult> ExecutePooledCloned(in TArgs args, int skip = 0, int take = int.MaxValue)
		=> _executor.Execute(in args, true, true, skip, take);

	public override int Count(in TArgs args) => _executor.Count(in args);
}

/// <summary>The general executor of a simple (no-join) shape: exactly <see cref="PreparedSimpleQuery{TKey,TValue,TArgs,TChain,TResolver,TPlan}" />'s replay.</summary>
internal readonly struct ReplaySimpleExecutor<TKey, TValue, TArgs, TChain, TResolver, TPlan> : IFrozenExecutor<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
	where TResolver : struct, IJoinResolver
	where TPlan : struct, IPreparedSimplePlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TChain _chain;
	private readonly Resolvers<TResolver> _resolvers;

	internal ReplaySimpleExecutor(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
	}

	public static string Name => "Replay";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TValue> Execute(in TArgs args, bool pool, bool clone, int skip, int take)
		=> PreparedReplay.RunSimple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(_cache, in _chain, in _resolvers, in args, pool, clone, skip, take);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) => PreparedReplay.CountSimple(_cache, in _chain, in args);
}

/// <summary>The general executor of a joined shape: exactly <see cref="PreparedJoinedQuery{TKey,TValue,TArgs,TChain,TResolverChain,TResult,TPlan}" />'s replay.</summary>
internal readonly struct ReplayJoinedExecutor<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan> : IFrozenExecutor<TArgs, TResult>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
	where TResolverChain : struct, IResolvers
	where TResult : struct, IJoinResult<TValue>
	where TPlan : struct, IPreparedJoinedPlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TChain _chain;
	private readonly TResolverChain _resolvers;
	private readonly int _manyCount;

	internal ReplayJoinedExecutor(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
		_manyCount = manyCount;
	}

	public static string Name => "Replay";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TResult> Execute(in TArgs args, bool pool, bool clone, int skip, int take)
		=> PreparedReplay.RunJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(_cache, in _chain, in _resolvers, _manyCount, in args, pool, clone, skip, take);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) => PreparedReplay.CountJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(_cache, in _chain, in _resolvers, _manyCount, in args);
}

/// <summary>
///   <c>BuildFrozen()</c>'s planner. Flattens the typed chain through <c>Describe</c>, checks the one
///   optimization stage 1 ships — the point-lookup shape — and otherwise binds the replay executor.
///   Everything here runs once per build; nothing is reached from an execution.
/// </summary>
internal static class FrozenPlanner {
	/// <summary>
	///   Point-lookup eligibility: a simple, unsorted query whose first op is a unique-index equality
	///   (bound or parameterized) followed by nothing but filters. Anything else — a list index, a
	///   narrower before the unique step, a range (fixed or optional-bounds) or multi-value step after
	///   it, any composite (<c>Or</c> / <c>If</c> / <c>IfElse</c> / <c>Match</c>) — replays.
	/// </summary>
	internal static bool IsPointLookup(IReadOnlyList<NarrowerDescriptor> narrowers, bool hasResolvers, bool isSorted) {
		if (hasResolvers || isSorted || narrowers.Count == 0 || narrowers[0].Kind != NarrowerKind.UniqueEq)
			return false;
		for (var i = 1; i < narrowers.Count; i++)
			if (narrowers[i].Kind is not (NarrowerKind.Filter or NarrowerKind.FilterArg))
				return false;
		return true;
	}

	internal static FrozenQuery<TArgs, TValue> Simple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers, bool isSorted)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		where TPlan : struct, IPreparedSimplePlan {
		var narrowers = new List<NarrowerDescriptor>();
		chain.Describe(narrowers);
		var hasResolvers = typeof(TResolver) != typeof(BaseResolver<TKey, TValue>);
		// The unique-equality narrower knows its TIndexKey; it constructs the closed executor, so no
		// reflection is needed to recover the type argument from the descriptor.
		if (IsPointLookup(narrowers, hasResolvers, isSorted) && narrowers[0].Source is IPointLookupSource<TKey, TValue, TArgs> source)
			return source.CreateFrozen(cache, FilterSteps<TValue, TArgs>(narrowers), narrowers);
		return new FrozenQuery<TArgs, TValue, ReplaySimpleExecutor<TKey, TValue, TArgs, TChain, TResolver, TPlan>>(
			new(cache, in chain, in resolvers), narrowers, hasResolvers, isSorted);
	}

	internal static FrozenQuery<TArgs, TResult> Joined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, bool isSorted)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TPlan : struct, IPreparedJoinedPlan {
		var narrowers = new List<NarrowerDescriptor>();
		chain.Describe(narrowers);
		return new FrozenQuery<TArgs, TResult, ReplayJoinedExecutor<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>>(
			new(cache, in chain, in resolvers, manyCount), narrowers, true, isSorted);
	}

	// The filters after the unique step, unboxed once into typed steps the executor applies in order.
	private static FilterStep<TValue, TArgs>[] FilterSteps<TValue, TArgs>(IReadOnlyList<NarrowerDescriptor> narrowers) {
		if (narrowers.Count == 1)
			return [];
		var steps = new FilterStep<TValue, TArgs>[narrowers.Count - 1];
		for (var i = 1; i < narrowers.Count; i++) {
			var d = narrowers[i];
			steps[i - 1] = d.Kind == NarrowerKind.Filter
				? new FilterStep<TValue, TArgs>((Predicate<TValue>)d.Filter!)
				: new FilterStep<TValue, TArgs>((Func<TValue, TArgs, bool>)d.Filter!);
		}

		return steps;
	}
}

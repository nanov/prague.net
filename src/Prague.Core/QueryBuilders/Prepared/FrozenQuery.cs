namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;
using Collections;
using QueryBuilders;
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

	/// <summary>The flattened ops, the executor <c>BuildFrozen()</c> chose and the optimizations active on the plan (with their live state). Cold path; allocates.</summary>
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

	internal FrozenQuery(in TExecutor executor, IReadOnlyList<NarrowerDescriptor> narrowers, bool hasResolvers, bool isSorted, FrozenPlanner.Optimizations optimizations) {
		_executor = executor;
		_plan = new PlanInfo(narrowers, hasResolvers, isSorted, TExecutor.Name, optimizations.Names, optimizations.Live);
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

/// <summary>The general executor of a simple (no-join) shape: exactly <see cref="PreparedSimpleQuery{TKey,TValue,TArgs,TChain,TResolver,TPlan}" />'s replay. Bound when no stage-2 optimization applies to the plan, so that plan's body is byte for byte the <c>Build()</c> one.</summary>
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

/// <summary>
///   The simple-shape replay with the stage-2 additions the planner attached — a fused filter (applied
///   in one step instead of the chain's <c>Where</c> links) and / or a capacity hint for the candidate
///   set. A separate executor from <see cref="ReplaySimpleExecutor{TKey,TValue,TArgs,TChain,TResolver,TPlan}" />
///   so a plan that gets neither keeps the stage-1 body: the replay inlines the whole narrower chain, and
///   extra parameters and branches in that frame measured as a few percent on a range walk.
/// </summary>
internal readonly struct OptimizedReplaySimpleExecutor<TKey, TValue, TArgs, TChain, TResolver, TPlan> : IFrozenExecutor<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
	where TResolver : struct, IJoinResolver
	where TPlan : struct, IPreparedSimplePlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TChain _chain;
	private readonly Resolvers<TResolver> _resolvers;
	private readonly FusedFilter<TValue, TArgs>? _fused;
	private readonly FrozenHints? _hints;

	internal OptimizedReplaySimpleExecutor(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers, FusedFilter<TValue, TArgs>? fused, FrozenHints? hints) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
		_fused = fused;
		_hints = hints;
	}

	public static string Name => "Replay";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TValue> Execute(in TArgs args, bool pool, bool clone, int skip, int take)
		=> FrozenReplay.RunSimple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(_cache, in _chain, in _resolvers, in args, pool, clone, skip, take, _fused, _hints);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) => FrozenReplay.CountSimple(_cache, in _chain, in args, _fused, _hints);
}

/// <summary>The general executor of a joined shape: exactly <see cref="PreparedJoinedQuery{TKey,TValue,TArgs,TChain,TResolverChain,TResult,TPlan}" />'s replay; bound when no stage-2 optimization applies.</summary>
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

/// <summary>The joined-shape replay with the stage-2 fused filter and / or capacity hint; see <see cref="OptimizedReplaySimpleExecutor{TKey,TValue,TArgs,TChain,TResolver,TPlan}" />.</summary>
internal readonly struct OptimizedReplayJoinedExecutor<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan> : IFrozenExecutor<TArgs, TResult>
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
	private readonly FusedFilter<TValue, TArgs>? _fused;
	private readonly FrozenHints? _hints;

	internal OptimizedReplayJoinedExecutor(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, FusedFilter<TValue, TArgs>? fused, FrozenHints? hints) {
		_cache = cache;
		_chain = chain;
		_resolvers = resolvers;
		_manyCount = manyCount;
		_fused = fused;
		_hints = hints;
	}

	public static string Name => "Replay";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TResult> Execute(in TArgs args, bool pool, bool clone, int skip, int take)
		=> FrozenReplay.RunJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(_cache, in _chain, in _resolvers, _manyCount, in args, pool, clone, skip, take, _fused, _hints);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) => FrozenReplay.CountJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(_cache, in _chain, in _resolvers, _manyCount, in args, _fused, _hints);
}

/// <summary>
///   The frozen replay: <see cref="PreparedReplay" />'s routines with the stage-2 hooks. The fused
///   predicate goes into the core <i>before</i> the chain replays — the core only reads its filter at
///   execution (and when an <c>Or</c> auto-seeds, where an earlier filter means a smaller seed, exactly
///   as an eager <c>Where(..).Or(..)</c> would) — and the chain replays through
///   <see cref="INarrowerChain{TKey,TValue,TArgs}.ReplayIndexOnly{TCore}" /> so the fused <c>Where</c>s
///   do not run twice. The capacity hint is written into the fresh core and the set's high-water mark
///   is read back after the replay. Sampled executions close with the fused filter's re-ranking.
/// </summary>
internal static class FrozenReplay {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void ApplyFused<TKey, TValue, TArgs, TCore>(ref TCore core, FusedFilter<TValue, TArgs> fused, bool sampled, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TCore : struct, ICandidatesFilterer<TKey, TValue> {
		if (fused.HasArgs)
			core.WhereInternal(ArgPredicatePool<TValue, TArgs>.RentFused(fused, fused.Ordering, sampled, in args));
		else
			core.WhereInternal(sampled ? fused.ConstSampledPredicate! : fused.ConstPredicate!);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void ApplyFused<TKey, TValue, TArgs>(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, FusedFilter<TValue, TArgs> fused, bool sampled, in TArgs args)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> ApplyFused<TKey, TValue, TArgs, CacheQueryBuilderCoreCombined<TKey, TValue>>(ref core, fused, sampled, in args);

	// The hint pre-creates the candidate set the eager core would create lazily on its first index
	// step (`if (!Candidates.IsInitlized) Candidates = new()`), at the remembered size. Hinted plans are
	// equality-seeded, so that step always runs and only ever unions into / prunes the set: a pre-sized
	// empty set is the same state the lazy path reaches, minus the rehashes. The eager core is untouched.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void PreSize<TKey, TValue>(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, FrozenHints hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		var capacity = hints.CandidateCapacity;
		if (capacity > 0)
			core.Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>(capacity);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static CacheQueryBuilderCoreCombined<TKey, TValue> Into<TKey, TValue, TArgs, TChain>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TArgs args, FusedFilter<TValue, TArgs>? fused, bool sampled, FrozenHints? hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs> {
		var core = new CacheQueryBuilderCoreCombined<TKey, TValue>(cache);
		if (hints is not null)
			PreSize(ref core, hints);
		try {
			if (fused is null) {
				chain.Replay(ref core, in args);
			} else {
				ApplyFused(ref core, fused, sampled, in args);
				chain.ReplayIndexOnly(ref core, in args);
			}
		} catch {
			core.Dispose();
			throw;
		}

		hints?.Observe(core.Candidates.IsInitlized ? core.Candidates.HighWaterMark : 0);
		return core;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static QueryResults<TValue> RunSimple<TKey, TValue, TArgs, TChain, TResolver, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers, in TArgs args, bool pool, bool clone, int skip, int take,
		FusedFilter<TValue, TArgs>? fused, FrozenHints? hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		where TPlan : struct, IPreparedSimplePlan {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = fused is not null && fused.BeginExecution();
		try {
			var builder = new CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
				CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue>(
				new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(cache), Into(cache, in chain, in args, fused, sampled, hints), resolvers, 0);
			var results = TPlan.Execute(ref builder, pool, clone, skip, take);
			if (sampled)
				fused!.EndSampled();
			return results;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int CountSimple<TKey, TValue, TArgs, TChain>(InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TArgs args, FusedFilter<TValue, TArgs>? fused, FrozenHints? hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs> {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = fused is not null && fused.BeginExecution();
		try {
			var core = Into(cache, in chain, in args, fused, sampled, hints);
			var count = core.Count();
			if (sampled)
				fused!.EndSampled();
			return count;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static QueryResults<TResult> RunJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, in TArgs args, bool pool, bool clone, int skip, int take,
		FusedFilter<TValue, TArgs>? fused, FrozenHints? hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TPlan : struct, IPreparedJoinedPlan {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = fused is not null && fused.BeginExecution();
		try {
			var builder = new CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
				CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
				new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(cache), Into(cache, in chain, in args, fused, sampled, hints), resolvers, manyCount);
			var results = TPlan.Execute(ref builder, pool, clone, skip, take);
			if (sampled)
				fused!.EndSampled();
			return results;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int CountJoined<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, in TArgs args, FusedFilter<TValue, TArgs>? fused, FrozenHints? hints)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue> {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = fused is not null && fused.BeginExecution();
		try {
			var builder = new CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
				CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
				new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(cache), Into(cache, in chain, in args, fused, sampled, hints), resolvers, manyCount);
			var count = builder.CountCoreJoined<TResult>();
			if (sampled)
				fused!.EndSampled();
			return count;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}
}

/// <summary>
///   <c>BuildFrozen()</c>'s planner. Flattens the typed chain through <c>Describe</c>, picks the
///   executor — point lookup, the pipeline (every simple plan of non-composite index steps, unsorted
///   or under a <c>Sort</c> / <c>SortBounded</c>; a joined plan of the same steps whose joins are
///   fusable <c>JoinOne</c>s, with or without one sorter) or the replay — and attaches the optimizations the plan qualifies
///   for: a fused filter for two or more top-level <c>Where</c>s and, for replayed plans, a capacity
///   hint when they are equality-seeded. Everything here runs once per build; nothing is reached from
///   an execution.
/// </summary>
internal static class FrozenPlanner {
	/// <summary>The optimizations a plan ended up with: their names for <see cref="PlanInfo" /> and their live state for <c>Explain()</c>.</summary>
	internal readonly struct Optimizations(IReadOnlyList<string> names, IReadOnlyList<IPlanExplainable> live) {
		public static readonly Optimizations None = new([], []);

		public IReadOnlyList<string> Names { get; } = names;

		public IReadOnlyList<IPlanExplainable> Live { get; } = live;
	}

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
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in Resolvers<TResolver> resolvers, bool isSorted, FrozenOptions options)
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

		var names = new List<string>();
		var live = new List<IPlanExplainable>();
		// Stage 3: the pipeline takes every simple plan of non-composite index steps — unsorted, under a
		// classic Sort (the sorter runs inside the eager container the pipeline drives) or under
		// SortBounded (a finite page drives the eager top-k container behind the ExecuteCoreSimpleTop
		// gate, an unbounded one the classic container — design §8).
		var sorter = isSorted && TResolver.IsSorter;
		var sort = !sorter ? PipelineSort.None : resolvers.Resolver.AllowsBounded ? PipelineSort.Bounded : PipelineSort.Classic;
		if (options.Pipeline && (!hasResolvers || sorter) && TryPipeline<TKey, TValue, TArgs>(narrowers, options, out var pipelineSteps)) {
			var filters = TopLevelFilterSteps<TValue, TArgs>(narrowers);
			var pipelineFused = Fuse<TValue, TArgs>(narrowers, options, names, live);
			if (options.ReorderIndexNarrowers)
				names.Add("ReorderIndexNarrowers");
			// Free seed: Count always; Execute when the rows are fully sorted afterwards (classic Sort —
			// design §8) or the caller opted out of the eager encounter order. Never on its own for
			// SortBounded: the bounded container breaks ties by encounter ordinal, which must be eager's.
			var plan = new PipelinePlan<TKey, TValue, TArgs>(pipelineSteps, filters.Length, pipelineFused is not null, options.ReorderIndexNarrowers || sort == PipelineSort.Classic, sort, 0);
			live.Insert(0, plan);
			return new FrozenQuery<TArgs, TValue, PipelineExecutor<TKey, TValue, TArgs, TResolver>>(
				new(cache, pipelineSteps, in resolvers, filters, pipelineFused, plan), narrowers, hasResolvers, isSorted, new(names, live));
		}

		var fused = Fuse<TValue, TArgs>(narrowers, options, names, live);
		var hints = Hints(narrowers, options, names, live);
		if (fused is null && hints is null)
			return new FrozenQuery<TArgs, TValue, ReplaySimpleExecutor<TKey, TValue, TArgs, TChain, TResolver, TPlan>>(
				new(cache, in chain, in resolvers), narrowers, hasResolvers, isSorted);
		return new FrozenQuery<TArgs, TValue, OptimizedReplaySimpleExecutor<TKey, TValue, TArgs, TChain, TResolver, TPlan>>(
			new(cache, in chain, in resolvers, fused, hints), narrowers, hasResolvers, isSorted, new(names, live));
	}

	internal static FrozenQuery<TArgs, TResult> Joined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>(
		InMemoryDataCache<TKey, TValue> cache, in TChain chain, in TResolverChain resolvers, int manyCount, bool isSorted, FrozenOptions options)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		where TPlan : struct, IPreparedJoinedPlan {
		var narrowers = new List<NarrowerDescriptor>();
		chain.Describe(narrowers);
		var names = new List<string>();
		var live = new List<IPlanExplainable>();
		// Stage 3, steps 5 and 6: a chain of fusable JoinOnes (outer / inner, identity / selector, the four
		// families, chained), optionally one classic Sort or SortBounded before or after them, takes the
		// joined pipeline — the pass fills the fused rights per row; a SortBounded innermost feeds the
		// eager bounded base container. The step-6 shape (SortBounded → outer joins) also admits
		// resolvers that cannot fuse (a filter callback): they keep their paired read. Any JoinMany (its
		// two-pass fan-out, design §7.2), an unfusable resolver elsewhere, or an inner left-symmetric join
		// (its fan-out regroups the rows — opt in with FrozenOptions.FuseSymmetricInnerJoins) replays.
		if (options.Pipeline && manyCount == 0 && PipelineJoinedExecutor<TKey, TValue, TArgs, TResolverChain, TResult>.Accepts(in resolvers, options.FuseSymmetricInnerJoins, out var shape)
		    && TryPipeline<TKey, TValue, TArgs>(narrowers, options, out var pipelineSteps)) {
			var filters = TopLevelFilterSteps<TValue, TArgs>(narrowers);
			var pipelineFused = Fuse<TValue, TArgs>(narrowers, options, names, live);
			if (options.ReorderIndexNarrowers)
				names.Add("ReorderIndexNarrowers");
			// Free seed: Count always; Execute under a classic Sort (the rows are fully sorted afterwards —
			// design §8) or the caller's opt-in. Never on its own for a SortBounded (its tie-breaking
			// ordinals must be eager's) or an unsorted chain (the encounter order is eager's).
			var plan = new PipelinePlan<TKey, TValue, TArgs>(pipelineSteps, filters.Length, pipelineFused is not null, options.ReorderIndexNarrowers || shape.ClassicSort, shape.Sort, shape.Joins, shape.FusedJoins);
			live.Insert(0, plan);
			return new FrozenQuery<TArgs, TResult, PipelineJoinedExecutor<TKey, TValue, TArgs, TResolverChain, TResult>>(
				new(cache, pipelineSteps, in resolvers, manyCount, filters, pipelineFused, plan, in shape), narrowers, true, isSorted, new(names, live));
		}

		var fused = Fuse<TValue, TArgs>(narrowers, options, names, live);
		var hints = Hints(narrowers, options, names, live);
		if (fused is null && hints is null)
			return new FrozenQuery<TArgs, TResult, ReplayJoinedExecutor<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>>(
				new(cache, in chain, in resolvers, manyCount), narrowers, true, isSorted);
		return new FrozenQuery<TArgs, TResult, OptimizedReplayJoinedExecutor<TKey, TValue, TArgs, TChain, TResolverChain, TResult, TPlan>>(
			new(cache, in chain, in resolvers, manyCount, fused, hints), narrowers, true, isSorted, new(names, live));
	}

	// Two or more top-level filters fuse; a single one is already one delegate in the eager core and
	// gains nothing from a wrapper. Filters inside composite branches stay in their sub-chains.
	private static FusedFilter<TValue, TArgs>? Fuse<TValue, TArgs>(IReadOnlyList<NarrowerDescriptor> narrowers, FrozenOptions options, List<string> names, List<IPlanExplainable> live) {
		if (!options.FuseFilters)
			return null;
		var count = 0;
		var hasArgs = false;
		for (var i = 0; i < narrowers.Count; i++) {
			if (narrowers[i].Kind is not (NarrowerKind.Filter or NarrowerKind.FilterArg))
				continue;
			count++;
			hasArgs |= narrowers[i].Kind == NarrowerKind.FilterArg;
		}

		if (count < 2)
			return null;
		var steps = new FilterStep<TValue, TArgs>[count];
		var n = 0;
		for (var i = 0; i < narrowers.Count; i++) {
			var d = narrowers[i];
			if (d.Kind == NarrowerKind.Filter)
				steps[n++] = new FilterStep<TValue, TArgs>((Predicate<TValue>)d.Filter!);
			else if (d.Kind == NarrowerKind.FilterArg)
				steps[n++] = new FilterStep<TValue, TArgs>((Func<TValue, TArgs, bool>)d.Filter!);
		}

		var fused = new FusedFilter<TValue, TArgs>(steps, hasArgs, options.AdaptiveFilterOrdering);
		names.Add("FusedFilters");
		if (options.AdaptiveFilterOrdering)
			names.Add("AdaptiveFilterOrder");
		live.Add(fused);
		return fused;
	}

	// A hint helps a plan whose candidate set is seeded by an equality / membership step and only
	// pruned afterwards: the high-water mark read back is then the seed size, which is what the next
	// execution should pre-size to. A range seed's size is the range's, unrelated to the next
	// execution's bounds, and composites may or may not seed at all, so those plans get no hint; a
	// filter-only plan walks the store and never rents a set.
	private static FrozenHints? Hints(IReadOnlyList<NarrowerDescriptor> narrowers, FrozenOptions options, List<string> names, List<IPlanExplainable> live) {
		if (!options.CapacityHints || !IsEqualitySeeded(narrowers))
			return null;
		var hints = new FrozenHints();
		names.Add("CapacityHints");
		live.Add(hints);
		return hints;
	}

	private static bool IsEqualitySeeded(IReadOnlyList<NarrowerDescriptor> narrowers) {
		var steps = 0;
		for (var i = 0; i < narrowers.Count; i++) {
			switch (narrowers[i].Kind) {
				case NarrowerKind.UniqueEq:
				case NarrowerKind.UniqueIn:
				case NarrowerKind.ListEq:
				case NarrowerKind.ListIn:
				case NarrowerKind.ListInProjected:
				case NarrowerKind.KeySet:
					steps++;
					break;
				case NarrowerKind.Filter:
				case NarrowerKind.FilterArg:
					break;
				default:
					return false;
			}
		}

		return steps > 0;
	}

	/// <summary>
	///   Pipeline eligibility (design §9): one to <see cref="PipelineLimits.MaxSteps" /> index steps, every
	///   one a non-composite narrower that can build its step under the options (a key the binding can
	///   hold; no range step when probes must be index-side), plus any number of top-level filters. A
	///   filter-only plan has no seed source and replays; composites arrive in the design's step 4.
	///   Shared by the simple and the joined planner rules.
	/// </summary>
	private static bool TryPipeline<TKey, TValue, TArgs>(IReadOnlyList<NarrowerDescriptor> narrowers, FrozenOptions options, out IPipelineStep<TKey, TValue, TArgs>[] steps)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		var list = new List<IPipelineStep<TKey, TValue, TArgs>>();
		for (var i = 0; i < narrowers.Count; i++) {
			var d = narrowers[i];
			if (d.Kind is NarrowerKind.Filter or NarrowerKind.FilterArg)
				continue;
			if (d.Source is not IPipelineStepSource<TKey, TValue, TArgs> source || source.CreatePipelineStep(options) is not { } step) {
				steps = [];
				return false;
			}

			list.Add(step);
		}

		if (list.Count is 0 or > PipelineLimits.MaxSteps) {
			steps = [];
			return false;
		}

		steps = list.ToArray();
		return true;
	}

	// Every top-level filter in build order, unboxed once; the pipeline applies them directly.
	private static FilterStep<TValue, TArgs>[] TopLevelFilterSteps<TValue, TArgs>(IReadOnlyList<NarrowerDescriptor> narrowers) {
		var list = new List<FilterStep<TValue, TArgs>>();
		for (var i = 0; i < narrowers.Count; i++) {
			var d = narrowers[i];
			if (d.Kind == NarrowerKind.Filter)
				list.Add(new FilterStep<TValue, TArgs>((Predicate<TValue>)d.Filter!));
			else if (d.Kind == NarrowerKind.FilterArg)
				list.Add(new FilterStep<TValue, TArgs>((Func<TValue, TArgs, bool>)d.Filter!));
		}

		return list.ToArray();
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

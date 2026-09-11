namespace Prague.Core;

using System.Runtime.CompilerServices;
using QueryBuilders;
using TypeSystem;

/// <summary>
///   Disjunction of two recorded sub-chains. Replays through the very <c>OrWith</c> the eager
///   <c>Or(b1, b2)</c> extension calls, handing it two <see cref="PreparedOrBranch{TKey,TValue,TArgs,TBranch1,TBranch2,TCore}" />
///   values that replay one sub-chain each into the branch cores <c>OrWith</c> creates. Auto-seeding
///   when the Or is the first narrowing, the two child intersecters, their union into the outer
///   candidates, no-op branch detection and disposal are all the eager core's — nothing is re-decided
///   here. Nested Ors are ordinary links inside a sub-chain.
/// </summary>
public readonly struct OrNarrower<TKey, TValue, TArgs, TBranch1, TBranch2> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
	where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
	where TArgs : struct {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly TBranch1 _branch1;
	private readonly TBranch2 _branch2;

	public OrNarrower(InMemoryDataCache<TKey, TValue> cache, in TBranch1 branch1, in TBranch2 branch2) {
		_cache = cache;
		_branch1 = branch1;
		_branch2 = branch2;
	}

	// OrWith's resolver chain and result type only type the branch builders it constructs (a branch
	// cannot join or execute, so nothing reads them); the base chain and the cache value are passed
	// whatever the outer query's chain is, which keeps the closed OrWith instantiation independent of
	// where in the query the Or sits.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		var first = new PreparedOrBranch<TKey, TValue, TArgs, TBranch1, TBranch2, TCore>(in _branch1, in _branch2, in args, false);
		var second = new PreparedOrBranch<TKey, TValue, TArgs, TBranch1, TBranch2, TCore>(in _branch1, in _branch2, in args, true);
		core.OrWith<InMemoryDataCache<TKey, TValue>, Resolvers<BaseResolver<TKey, TValue>>, TValue, PreparedOrBranch<TKey, TValue, TArgs, TBranch1, TBranch2, TCore>>(
			new NarrowOnlyQuery<InMemoryDataCache<TKey, TValue>>(_cache), new Resolvers<BaseResolver<TKey, TValue>>(default), in first, in second);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		var branch1 = new List<NarrowerDescriptor>();
		var branch2 = new List<NarrowerDescriptor>();
		_branch1.Describe(branch1);
		_branch2.Describe(branch2);
		plan.Add(NarrowerDescriptor.ForComposite(NarrowerKind.Or, null, branch1, branch2));
	}
}

/// <summary>
///   The branch strategy a prepared Or hands to <c>OrWith</c>. <c>OrWith</c> takes both branches as
///   one <c>TBranch</c> type while the two sub-chains have different types, so a single struct carries
///   both chains plus the execution arguments and a selector: the instance for <c>b1</c> replays the
///   first chain, the one for <c>b2</c> the second. The arguments travel by value (a struct copy per
///   Or per execution, never a closure) because the eager branch core runs only inside <c>OrWith</c>,
///   after the narrower's own <c>in args</c> frame is no longer reachable by reference.
/// </summary>
public readonly struct PreparedOrBranch<TKey, TValue, TArgs, TBranch1, TBranch2, TCore>
	: IOrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<TKey, TValue>>, TCore, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TBranch1 : struct, INarrowerChain<TKey, TValue, TArgs>
	where TBranch2 : struct, INarrowerChain<TKey, TValue, TArgs>
	where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
	where TArgs : struct {
	private readonly TBranch1 _branch1;
	private readonly TBranch2 _branch2;
	private readonly TArgs _args;
	private readonly bool _second;

	public PreparedOrBranch(in TBranch1 branch1, in TBranch2 branch2, in TArgs args, bool second) {
		_branch1 = branch1;
		_branch2 = branch2;
		_args = args;
		_second = second;
	}

	// `b` is OrWith's branch builder whose left query is a fresh eager core in intersecter mode; the
	// sub-chain narrows that core exactly as the eager branch lambda's UseIndex calls would, and the
	// mutated copy goes back to OrWith, which reads its `_first` to detect a no-op branch.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<TKey, TValue>>, TCore, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>
		Apply(CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<TKey, TValue>>, TCore, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> b) {
		if (_second)
			_branch2.Replay(ref b._leftQuery, in _args);
		else
			_branch1.Replay(ref b._leftQuery, in _args);
		return b;
	}
}

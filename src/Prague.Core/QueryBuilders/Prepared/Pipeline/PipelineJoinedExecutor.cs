namespace Prague.Core;

using System.Runtime.CompilerServices;

/// <summary>
///   The stage-3 executor for joined plans whose narrowing is a pipeline plan and whose resolver chain
///   is a <c>SortBounded</c> followed by outer joins (design §7.3 / §8, step 6): the
///   <see cref="PipelineCore{TKey,TValue,TArgs}" /> pass replaces the eager base walk of
///   <c>ExecuteCoreJoinedTop</c> — it feeds the eager <see cref="TopKJoinedBaseContainer{TKey,TValue,TChain}" />,
///   whose page is materialized into the eager <see cref="JoinedResultContaier{TLeftKey,TLeftValue,TResolverChain,TResult}" />
///   and filled by the join resolvers as they run today (<c>ExecuteJoinsBounded</c>: unfused, one
///   paired read per resolver over the page rows only). The eager gate is applied per call, so an
///   unbounded or negative page drives the classic joined container (<c>ExecuteCoreJoined</c>: the
///   pass into the dictionary, then <c>ExecuteJoins</c> runs the sorter and the joins over every
///   row) exactly as eager does. Inner joins need the eager candidate set (their narrowing pass reads
///   it before the base walk), and a <c>JoinMany</c> its two-pass fan-out, so the planner sends those
///   chains to the replay (§7.2, §9). The resolver chain is copied onto the stack per execution
///   because resolvers carry per-execution scratch, as the prepared joined query does.
/// </summary>
internal readonly struct PipelineJoinedExecutor<TKey, TValue, TArgs, TResolverChain, TResult> : IFrozenExecutor<TArgs, TResult>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TResolverChain : struct, IResolvers
	where TResult : struct, IJoinResult<TValue> {
	private readonly PipelineCore<TKey, TValue, TArgs> _core;
	private readonly TResolverChain _resolvers;
	private readonly int _manyCount;

	internal PipelineJoinedExecutor(InMemoryDataCache<TKey, TValue> cache, IPipelineStep<TKey, TValue, TArgs>[] steps, in TResolverChain resolvers, int manyCount,
		FilterStep<TValue, TArgs>[] filters, FusedFilter<TValue, TArgs>? fused, PipelinePlan<TKey, TValue, TArgs> plan) {
		_core = new(cache, steps, filters, fused, plan);
		_resolvers = resolvers;
		_manyCount = manyCount;
	}

	public static string Name => "Pipeline";

	/// <summary>
	///   The chain shape this executor drives: exactly one sorter, innermost, a <c>SortBounded</c> that
	///   orders by the left value, and no inner join — the eager <c>ExecuteCoreJoinedTop</c> probe plus
	///   the pipeline's own "no candidate set" condition. Decided once at build.
	/// </summary>
	internal static bool Accepts(in TResolverChain resolvers) {
		var chain = resolvers;
		var probe = new ChainProbe();
		chain.Execute(ref probe);
		return probe.Sorters == 1 && probe.SorterInnermost && probe.SorterAllowsBounded && probe.SorterOrdersByLeftValues && !probe.HasInner;
	}

	// SkipLocalsInit: the seed's stack buffer is written before it is read; the frame's constructor
	// zeroes the rest (its bindings hold references and an activation state the steps read back).
	[SkipLocalsInit]
	public QueryResults<TResult> Execute(in TArgs args, bool pool, bool clone, int skip, int take) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var frame = new PipelineFrame<TKey>(SeedKeys<TKey>.Over(stack));
		try {
			// The eager base walk adds nothing to an empty candidate set; both joined cores then build
			// an empty result with TotalCount 0 — the shared Empty.
			if (!_core.Open(in args, ref frame))
				return QueryResults<TResult>.Empty;
			var chain = _resolvers;
			return PipelineCore<TKey, TValue, TArgs>.IsBoundedPage(skip, take)
				? ExecuteTop(in args, ref frame, ref chain, pool, clone, skip, take)
				: ExecuteClassic(in args, ref frame, ref chain, pool, clone, skip, take);
		} finally {
			_core.Release(ref frame);
		}
	}

	// CountCoreJoined narrows the candidates by the inner joins and counts the survivors; with outer
	// joins only, that is the base count — the pipeline's own.
	public int Count(in TArgs args) => _core.Count(in args);

	// ExecuteCoreJoined with the pass in place of the base walk: the container is sized to the seed,
	// filled, sealed with the number of rows added, and ExecuteJoins runs the sorter (stable, then the
	// page crop) and the joins over every row.
	private QueryResults<TResult> ExecuteClassic(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, skip, take, _manyCount);
		try {
			container.Init(frame.Seed.Count);
			container.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref container));
			container.ExecuteJoins();
			var results = container.BuildResults();
			_core.EndSampling(sampled);
			return results;
		} finally {
			container.Dispose();
		}
	}

	// ExecuteCoreJoinedTop with the pass in place of the base walk: the bounded base container keeps
	// the [skip, skip + take) page in a heap of size skip + take (or collects and selects in place near
	// the full size), the page is materialized into the joined container in final order, and the joins
	// fill only those rows. No inner narrowing or release passes: the planner admits outer joins only.
	private QueryResults<TResult> ExecuteTop(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, _manyCount, bounded: true);
		var topK = new TopKJoinedBaseContainer<TKey, TValue, TResolverChain>(ref chain, skip, take);
		try {
			topK.Init(frame.Seed.Count);
			topK.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref topK));
			var kept = topK.Drain();
			container.MaterializeTopK(topK.Buffer, skip, Math.Max(kept - skip, 0), topK.TotalCount);
			container.ExecuteJoinsBounded();
			var results = container.BuildResults();
			_core.EndSampling(sampled);
			return results;
		} finally {
			// Both own pooled memory; each must run even if the other throws (the eager core's nesting).
			try {
				topK.Dispose();
			} finally {
				container.Dispose();
			}
		}
	}

	/// <summary>The eager <see cref="TopKProbeProcessor{TLeftValue}" /> plus "is any join inner".</summary>
	private ref struct ChainProbe : IResolverExecutor {
		internal int Sorters;
		internal bool SorterInnermost;
		internal bool SorterAllowsBounded;
		internal bool SorterOrdersByLeftValues;
		internal bool HasInner;

		public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
			if (TResolver.IsSorter) {
				Sorters++;
				SorterInnermost = position == 0;
				SorterAllowsBounded = resolver.AllowsBounded;
				SorterOrdersByLeftValues = resolver.OrdersByLeftValues<TValue>();
				return;
			}

			HasInner |= resolver.Inner;
		}
	}
}

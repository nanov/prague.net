namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

/// <summary>
///   An equality index step the planner may apply out of build order: unique / list equality and the
///   key-set index. Implemented explicitly by those narrowers and boxed once at build, so the
///   <see cref="IndexStepsExecutor{TKey,TValue,TArgs,TResolver,TPlan}" /> can pick the seed per
///   execution — one interface call per step per execution, against the JIT-specialized chain the
///   replay executor keeps for plans that do not reorder.
/// </summary>
internal interface IIndexStep<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	/// <summary>Rows this step would seed for this execution's key: 0 / 1 for a unique step, the live bucket size for a list step, the key count for a key-set. One probe.</summary>
	int Cardinality(in TArgs args);

	void Apply(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, in TArgs args);
}

/// <summary>
///   Simple-shape executor for plans of equality index steps plus filters when the options ask for a
///   data-dependent step order (<see cref="FrozenOptions.ReorderIndexNarrowers" />) or an adaptive
///   intersection (<see cref="FrozenOptions.AdaptiveIntersection" />). The eager core is still the
///   engine: the seed step goes through the same <c>UseIndexInternal</c> the eager builder calls, and
///   the other steps intersect into it one of two ways. The eager way walks the candidate set once per
///   step, probing the step's bucket and removing misses by hash — O(|candidates|) per step. The
///   bitmap way — the Or branch mechanism used at the top level — marks the candidates that a step's
///   bucket contains by walking the <i>bucket</i> and probing the set (O(|bucket|)), prunes the marks
///   with the remaining steps (O(|marked|)), and compacts the set once by slot when the intersecter is
///   disposed. Adaptive intersection reads each remaining step's bucket size and takes the bitmap way
///   when the smallest bucket is smaller than the seeded set, with that step first; otherwise it takes
///   the eager way. Either way removes by slot, so survivors keep the seed's encounter order. Filters
///   are the fused predicate, applied before the steps like the replay executor does.
/// </summary>
internal readonly struct IndexStepsExecutor<TKey, TValue, TArgs, TResolver, TPlan> : IFrozenExecutor<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TResolver : struct, IJoinResolver
	where TPlan : struct, IPreparedSimplePlan {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly IIndexStep<TKey, TValue, TArgs>[] _steps;
	private readonly Resolvers<TResolver> _resolvers;
	private readonly FusedFilter<TValue, TArgs>? _fused;
	private readonly FrozenHints? _hints;
	private readonly bool _reorder;
	private readonly bool _adaptiveIntersection;

	internal IndexStepsExecutor(InMemoryDataCache<TKey, TValue> cache, IIndexStep<TKey, TValue, TArgs>[] steps, in Resolvers<TResolver> resolvers,
		FusedFilter<TValue, TArgs>? fused, FrozenHints? hints, bool reorder, bool adaptiveIntersection) {
		_cache = cache;
		_steps = steps;
		_resolvers = resolvers;
		_fused = fused;
		_hints = hints;
		_reorder = reorder;
		_adaptiveIntersection = adaptiveIntersection;
	}

	public static string Name => "IndexSteps";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TValue> Execute(in TArgs args, bool pool, bool clone, int skip, int take) {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = _fused is not null && _fused.BeginExecution();
		try {
			var builder = new CacheQueryBuilderCombined<ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
				CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<TResolver>, TValue>(
				new ExecutableQuery<InMemoryDataCache<TKey, TValue>>(_cache), Into(in args, sampled), _resolvers, 0);
			var results = TPlan.Execute(ref builder, pool, clone, skip, take);
			if (sampled)
				_fused!.EndSampled();
			return results;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) {
		var mark = ArgPredicatePool<TValue, TArgs>.Mark();
		var sampled = _fused is not null && _fused.BeginExecution();
		try {
			var core = Into(in args, sampled);
			var count = core.Count();
			if (sampled)
				_fused!.EndSampled();
			return count;
		} finally {
			ArgPredicatePool<TValue, TArgs>.Reset(mark);
		}
	}

	[SkipLocalsInit]
	private CacheQueryBuilderCoreCombined<TKey, TValue> Into(in TArgs args, bool sampled) {
		var core = new CacheQueryBuilderCoreCombined<TKey, TValue>(_cache);
		if (_hints is not null)
			FrozenReplay.PreSize(ref core, _hints);
		try {
			if (_fused is not null)
				FrozenReplay.ApplyFused(ref core, _fused, sampled, in args);
			var steps = _steps;
			// Bucket sizes for this execution's keys; one probe per step, read once for both decisions.
			// The planner caps the step count, so the frame stays small.
			Span<int> cardinalities = stackalloc int[steps.Length];
			var seed = 0;
			if (_reorder || _adaptiveIntersection)
				for (var i = 0; i < steps.Length; i++)
					cardinalities[i] = steps[i].Cardinality(in args);
			if (_reorder) {
				// Smallest bucket seeds. A unique step reports 0 or 1 and so always wins; a missing key
				// reports 0 and seeds an empty set, which every later step then skips.
				var best = int.MaxValue;
				for (var i = 0; i < steps.Length; i++) {
					if (cardinalities[i] >= best)
						continue;
					best = cardinalities[i];
					seed = i;
				}
			}

			steps[seed].Apply(ref core, in args);
			var count = core.Candidates.Count;
			if (count > 0) {
				var first = -1;
				if (_adaptiveIntersection) {
					var smallest = int.MaxValue;
					for (var i = 0; i < steps.Length; i++) {
						if (i == seed || cardinalities[i] >= smallest)
							continue;
						smallest = cardinalities[i];
						first = i;
					}

					if (smallest >= count)
						first = -1;
				}

				if (first >= 0)
					IntersectOnce(ref core, steps, seed, first, in args);
				else
					for (var i = 0; i < steps.Length; i++)
						if (i != seed)
							steps[i].Apply(ref core, in args);
			}
		} catch {
			core.Dispose();
			throw;
		}

		_hints?.Observe(core.Candidates.IsInitlized ? core.Candidates.HighWaterMark : 0);
		return core;
	}

	// The remaining steps mark-and-prune a bitmap over the seeded set through a child core in
	// intersecter mode (the same construction OrWith uses for its branches): the smallest bucket
	// marks first (the child's `_first` step walks its bucket), the others prune the marks, and the
	// intersecter's dispose removes the unmarked slots in one pass.
	// No SkipLocalsInit here: the intersecter takes the buffer as an already-zeroed bitmap (as OrWith's
	// stackalloc is), and a stale bit would be a phantom mark — a row kept that no step matched.
	private void IntersectOnce(ref CacheQueryBuilderCoreCombined<TKey, TValue> core, IIndexStep<TKey, TValue, TArgs>[] steps, int seed, int first, in TArgs args) {
		Span<int> buffer = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
		var intersecter = new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref core.Candidates, buffer);
		try {
			var child = new CacheQueryBuilderCoreCombined<TKey, TValue>(_cache, ref intersecter);
			steps[first].Apply(ref child, in args);
			for (var i = 0; i < steps.Length; i++)
				if (i != seed && i != first)
					steps[i].Apply(ref child, in args);
		} finally {
			intersecter.Dispose();
		}
	}
}

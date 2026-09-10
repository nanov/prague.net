namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The stage-3 executor for simple plans of non-composite index steps — unsorted, or under a
///   classic <c>Sort</c> (design §2, §3, §6, §8): bind every step to the arguments once, choose the
///   seed, copy its keys out under one gate pin, then one pass — key-side probes, one store lookup,
///   value-side probes, the predicates — driving the eager
///   <see cref="SimpleResultContainer{TKey,TValue,TResolver}" /> exactly as the eager core does
///   (<c>Init(seedCount)</c>, <c>Add</c>, <c>Seal</c>, <c>BuildResults</c> — which also runs the sorter),
///   so <c>TotalCount</c>, paging, clone timing and pooling are the eager ones by construction. No
///   candidate set, no bitmap, no compaction; filters are called directly (no
///   <see cref="ArgPredicatePool{TValue,TArgs}" />).
///   Seed selection (§3.3 / §3.4): in fixed mode the first active index step seeds — the eager
///   <c>_first</c> rule, so the encounter order is eager's — and when that step is a
///   <see cref="PooledSet{T,TKeyComparer}" /> (list equality, key-set) and another active equality step
///   has a signal at most half its own, the small step is walked instead, each survivor is located in
///   the first step's set with <c>TryGetSlot</c> and the survivors are sorted by that slot: the same
///   sequence at the small step's cost. In free mode (<c>Count</c>; <c>Execute*</c> of a classic
///   <c>Sort</c> or under <see cref="FrozenOptions.ReorderIndexNarrowers" />) the smallest signal seeds
///   — a unique step always wins, an exact count of zero is the empty result with no walk, a B+tree
///   estimate replaces an exact count only when twice the estimate is still smaller — and the row
///   order follows the seeding source. Immutable after build; every execution's state is a stack
///   frame (design §10).
/// </summary>
internal readonly struct PipelineExecutor<TKey, TValue, TArgs, TResolver> : IFrozenExecutor<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TResolver : struct, IJoinResolver {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly IPipelineStep<TKey, TValue, TArgs>[] _steps;
	private readonly Resolvers<TResolver> _resolvers;
	private readonly FilterStep<TValue, TArgs>[] _filters;
	private readonly FusedFilter<TValue, TArgs>? _fused;
	private readonly PipelinePlan<TKey, TValue, TArgs> _plan;
	private readonly bool _freeSeed;
	private readonly bool _needsRelease;

	internal PipelineExecutor(InMemoryDataCache<TKey, TValue> cache, IPipelineStep<TKey, TValue, TArgs>[] steps, in Resolvers<TResolver> resolvers,
		FilterStep<TValue, TArgs>[] filters, FusedFilter<TValue, TArgs>? fused, PipelinePlan<TKey, TValue, TArgs> plan) {
		_cache = cache;
		_steps = steps;
		_resolvers = resolvers;
		_filters = filters;
		_fused = fused;
		_plan = plan;
		_freeSeed = plan.FreeSeed;
		for (var i = 0; i < steps.Length; i++)
			_needsRelease |= steps[i].NeedsRelease;
	}

	public static string Name => "Pipeline";

	// SkipLocalsInit: the seed's stack buffer is written before it is read; the frame's constructor
	// zeroes the rest (its bindings hold references and an activation state the steps read back).
	[SkipLocalsInit]
	public QueryResults<TValue> Execute(in TArgs args, bool pool, bool clone, int skip, int take) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var frame = new PipelineFrame<TKey>(SeedKeys<TKey>.Over(stack));
		var steps = _steps;
		try {
			// The eager core returns before Init when the candidate set is empty; BuildResults then
			// yields the shared Empty.
			if (!Bind(steps, in args, ref frame) || !ChooseSeed(steps, ref frame, _freeSeed) || !Seed(steps, ref frame))
				return QueryResults<TValue>.Empty;

			var sampled = _fused is not null && _fused.BeginExecution();
			var container = new SimpleResultContainer<TKey, TValue, TResolver>(_resolvers.Resolver, pool, clone, skip, take);
			try {
				container.Init(frame.Seed.Count);
				container.Seal(Walk(steps, in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref container));
				var results = container.BuildResults();
				if (sampled)
					_fused!.EndSampled();
				return results;
			} finally {
				container.Dispose();
			}
		} finally {
			Release(steps, ref frame);
		}
	}

	[SkipLocalsInit]
	public int Count(in TArgs args) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var frame = new PipelineFrame<TKey>(SeedKeys<TKey>.Over(stack));
		var steps = _steps;
		try {
			if (!Bind(steps, in args, ref frame) || !ChooseSeed(steps, ref frame, true) || !Seed(steps, ref frame))
				return 0;
			var sampled = _fused is not null && _fused.BeginExecution();
			var counter = new CountContainer();
			var count = Walk(steps, in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref counter);
			if (sampled)
				_fused!.EndSampled();
			return count;
		} finally {
			Release(steps, ref frame);
		}
	}

	// Binds every step in plan order and records the active ones. False when a step empties the query
	// (an empty unique In span): no seed walk, no store lookups, the eager zero-row result.
	private static bool Bind(IPipelineStep<TKey, TValue, TArgs>[] steps, in TArgs args, scoped ref PipelineFrame<TKey> frame) {
		var active = 0;
		for (var i = 0; i < steps.Length; i++) {
			ref var binding = ref frame.Bindings[i];
			var activation = steps[i].Bind(in args, ref binding);
			binding.Activation = activation;
			if (activation == StepActivation.Empty)
				return false;
			if (activation == StepActivation.Active)
				frame.Active[active++] = (byte)i;
		}

		frame.ActiveCount = active;
		return true;
	}

	// Picks the seed (design §3.3 / §3.4) and splits the remaining active steps into the key-side and
	// value-side probe lists in plan order. False when an exact signal of zero proves the result empty.
	private bool ChooseSeed(IPipelineStep<TKey, TValue, TArgs>[] steps, scoped ref PipelineFrame<TKey> frame, bool free) {
		var active = frame.ActiveList;
		var seed = -1;
		var small = -1;
		var seedSignal = 0;
		var otherSignal = 0;
		var mode = SeedMode.AllRows;
		if (active.Length > 0) {
			seed = active[0];
			mode = SeedMode.Fixed;
			if (active.Length > 1) {
				if (free) {
					mode = SeedMode.Free;
					if (!ChooseSmallest(steps, active, frame.Bindings, out seed, out seedSignal)) {
						_plan.Record(mode, seed, -1, seedSignal, 0);
						return false;
					}
				} else if (steps[seed].SlotAddressable) {
					// Fixed order, small walk: the survivors of the smallest equality step, sorted by
					// their slot in the first step's set, are the first step's sequence.
					seedSignal = steps[seed].Signal(in frame.Bindings[seed]);
					if (ChooseSmallEquality(steps, active[1..], frame.Bindings, out small, out otherSignal) && 2L * otherSignal <= seedSignal)
						mode = SeedMode.SmallProbe;
					else
						small = -1;
				}
			}
		}

		frame.SeedStep = seed;
		frame.SmallStep = small;
		frame.Mode = mode;
		_plan.Record(mode, seed, small, seedSignal, otherSignal);

		// A walked step is judged by its index. The step whose walk stands in for the first step's (the
		// small step; a free seed moved off the first step) keeps its value-side probe when it has one,
		// so the rows it admits are judged as the fixed walk would judge them — by the value returned
		// (§14.1) — at one field compare per survivor. A key-side walk is its own probe.
		var keyProbes = 0;
		var valueProbes = 0;
		for (var i = 0; i < active.Length; i++) {
			var s = active[i];
			var side = steps[s].Side;
			if (s == seed && (mode != SeedMode.Free || seed == active[0] || side == ProbeSide.Key))
				continue;
			if (s == small && side == ProbeSide.Key)
				continue;
			if (side == ProbeSide.Key)
				frame.KeyProbes[keyProbes++] = s;
			else
				frame.ValueProbes[valueProbes++] = s;
		}

		frame.KeyProbeCount = keyProbes;
		frame.ValueProbeCount = valueProbes;
		return true;
	}

	// Free seed: the smallest exact signal (ties → the earliest, so a unique step's 0 / 1 always wins and
	// the declared order breaks ties); an estimate (range, last-updated) replaces it only when twice the
	// estimate is still smaller (§14.4: a skewed tree must not lose to a small bucket). An exact zero is
	// the empty result; an estimate is never trusted for emptiness.
	private static bool ChooseSmallest(IPipelineStep<TKey, TValue, TArgs>[] steps, ReadOnlySpan<byte> active, scoped ReadOnlySpan<StepBinding> bindings, out int seed, out int signal) {
		seed = -1;
		signal = int.MaxValue;
		var exact = false;
		for (var i = 0; i < active.Length; i++) {
			var s = active[i];
			var step = steps[s];
			var value = step.Signal(in bindings[s]);
			if (step.ExactSignal) {
				if (!exact || value < signal) {
					seed = s;
					signal = value;
					exact = true;
				}
			} else if (exact ? 2L * value < signal : value < signal) {
				seed = s;
				signal = value;
			}
		}

		return !exact || signal > 0;
	}

	private static bool ChooseSmallEquality(IPipelineStep<TKey, TValue, TArgs>[] steps, ReadOnlySpan<byte> candidates, scoped ReadOnlySpan<StepBinding> bindings, out int small, out int signal) {
		small = -1;
		signal = int.MaxValue;
		for (var i = 0; i < candidates.Length; i++) {
			var s = candidates[i];
			var step = steps[s];
			if (!step.ExactSignal)
				continue;
			var value = step.Signal(in bindings[s]);
			if (value < signal) {
				small = s;
				signal = value;
			}
		}

		return small >= 0;
	}

	// Copies the seed keys out; false when nothing was copied (the eager empty candidate set).
	private bool Seed(IPipelineStep<TKey, TValue, TArgs>[] steps, scoped ref PipelineFrame<TKey> frame) {
		var seedStep = frame.SeedStep;
		if (seedStep < 0) {
			AllRows(ref frame.Seed);
			return frame.Seed.Count > 0;
		}

		// Multi-bucket seeds dedupe through a set the eager UnionWith would also have inserted into;
		// created on demand by the step, released here whatever happens in the walk.
		var dedupe = default(ValueSet<TKey, DefaultKeyComparer<TKey>>);
		try {
			if (frame.Mode == SeedMode.SmallProbe) {
				var small = frame.SmallStep;
				steps[small].Seed(in frame.Bindings[small], ref frame.Seed, ref dedupe);
				if (frame.Seed.Count > 0)
					SortBySlot(steps[seedStep], in frame.Bindings[seedStep], ref frame.Seed);
			} else {
				steps[seedStep].Seed(in frame.Bindings[seedStep], ref frame.Seed, ref dedupe);
			}
		} finally {
			if (dedupe.IsInitlized)
				dedupe.Dispose();
		}

		return frame.Seed.Count > 0;
	}

	// The small-probe seed's second half (§3.4): keep the keys the first step's set contains, remember
	// the slot each occupies, and sort the survivors by slot — the order the first step's own walk
	// would have yielded them in (the eager UnionWith fills a fresh set in that order, IntersectWith
	// removes by slot, the enumerator yields slot order). Slots are distinct, so the sort has no ties
	// to keep stable. The scratch is a stack span up to 256 survivors, the pool above.
	[SkipLocalsInit]
	private static void SortBySlot(IPipelineStep<TKey, TValue, TArgs> first, in StepBinding binding, ref SeedKeys<TKey> seed) {
		var keys = seed.MutableKeys;
		Span<int> stack = stackalloc int[PipelineLimits.SlotStackInts];
		var rented = keys.Length > stack.Length ? PragueArrayPool<int>.Pool.Rent(keys.Length) : null;
		try {
			var slots = rented is null ? stack[..keys.Length] : rented.AsSpan(0, keys.Length);
			var n = 0;
			for (var i = 0; i < keys.Length; i++) {
				if (!first.TryGetSlot(keys[i], in binding, out var slot))
					continue;
				keys[n] = keys[i];
				slots[n] = slot;
				n++;
			}

			seed.Truncate(n);
			if (n > 1)
				slots[..n].Sort(keys[..n]);
		} finally {
			if (rented is not null)
				PragueArrayPool<int>.Pool.Return(rented);
		}
	}

	// Every index step inactive (an empty list In span, an unbounded optional range): the eager core
	// never seeded and walks the whole store — same walk, same order, the keys copied out.
	private void AllRows(ref SeedKeys<TKey> seed) {
		var collector = new KeyCollector(ref seed);
		_cache.EnumerateAllValuesInit(ref collector, null);
	}

	// The frame's spans are passed `scoped` so the compiler knows nothing stack-bound can flow into the
	// container, which is the one ref-struct argument passed by reference.
	private int Walk<TContainer>(IPipelineStep<TKey, TValue, TArgs>[] steps, in TArgs args, scoped ReadOnlySpan<TKey> keys, scoped ReadOnlySpan<byte> keyProbes,
		scoped ReadOnlySpan<byte> valueProbes, scoped ReadOnlySpan<StepBinding> bindings, bool sampled, ref TContainer container)
		where TContainer : struct, IJoinedResultContainer<TKey, TValue>, allows ref struct {
		var filters = _filters;
		var fused = _fused;
		var ordering = fused?.Ordering;
		var actual = 0;
		for (var i = 0; i < keys.Length; i++) {
			var key = keys[i];
			if (keyProbes.Length > 0 && !PassesKeyProbes(steps, key, keyProbes, bindings))
				continue;
			if (!_cache.TryGet(key, out var value))
				continue;
			if (valueProbes.Length > 0 && !PassesValueProbes(steps, key, value, valueProbes, bindings))
				continue;
			if (ordering is null) {
				if (!PassesDirect(filters, value, in args))
					continue;
			} else if (sampled ? !fused!.PassesSampled(value, in args, ordering) : !FusedFilter<TValue, TArgs>.Passes(value, in args, ordering)) {
				continue;
			}

			container.Add(key, value);
			actual++;
		}

		return actual;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesKeyProbes(IPipelineStep<TKey, TValue, TArgs>[] steps, TKey key, scoped ReadOnlySpan<byte> probes, scoped ReadOnlySpan<StepBinding> bindings) {
		for (var p = 0; p < probes.Length; p++) {
			var s = probes[p];
			if (!steps[s].ProbeKey(key, in bindings[s]))
				return false;
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesValueProbes(IPipelineStep<TKey, TValue, TArgs>[] steps, TKey key, TValue value, scoped ReadOnlySpan<byte> probes, scoped ReadOnlySpan<StepBinding> bindings) {
		for (var p = 0; p < probes.Length; p++) {
			var s = probes[p];
			if (!steps[s].ProbeValue(key, value, in bindings[s]))
				return false;
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesDirect(FilterStep<TValue, TArgs>[] filters, TValue value, in TArgs args) {
		for (var i = 0; i < filters.Length; i++)
			if (!filters[i].Passes(value, in args))
				return false;
		return true;
	}

	private void Release(IPipelineStep<TKey, TValue, TArgs>[] steps, scoped ref PipelineFrame<TKey> frame) {
		frame.Seed.Dispose();
		if (!_needsRelease)
			return;
		for (var i = 0; i < steps.Length; i++)
			steps[i].Release(ref frame.Bindings[i]);
	}

	// The seed is a ref struct, so the collector keeps a laundered pointer to it (see SeedAggregators).
	private unsafe ref struct KeyCollector(ref SeedKeys<TKey> seed) : IResultContainerInitializer<TKey, TValue> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);

		public void Init(int maxCount) { }

		public void Seal(int actualCount) { }

		public int Add(TKey foreignKey, TValue result) {
			Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(foreignKey);
			return 0;
		}

		public int TotalCount => 0;
	}

	private ref struct CountContainer : IJoinedResultContainer<TKey, TValue> {
		public int Add(TKey foreignKey, TValue result) => 0;

		public int TotalCount => 0;
	}
}

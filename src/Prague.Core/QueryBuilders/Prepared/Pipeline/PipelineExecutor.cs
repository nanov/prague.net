namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The stage-3 executor for simple, unsorted plans of non-composite index steps (design §2, §6):
///   bind every step to the arguments once, copy the first active step's keys out under one gate pin,
///   then one pass — key-side probes, one store lookup, value-side probes, the predicates — driving the
///   eager <see cref="SimpleResultContainer{TKey,TValue,TResolver}" /> exactly as the eager core does
///   (<c>Init(seedCount)</c>, <c>Add</c>, <c>Seal</c>, <c>BuildResults</c>), so <c>TotalCount</c>,
///   paging, clone timing and pooling are the eager ones by construction. No candidate set, no bitmap,
///   no compaction; filters are called directly (no <see cref="ArgPredicatePool{TValue,TArgs}" />).
///   Immutable after build; every execution's state is a stack frame (design §10).
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
	private readonly bool _needsRelease;

	internal PipelineExecutor(InMemoryDataCache<TKey, TValue> cache, IPipelineStep<TKey, TValue, TArgs>[] steps, in Resolvers<TResolver> resolvers,
		FilterStep<TValue, TArgs>[] filters, FusedFilter<TValue, TArgs>? fused) {
		_cache = cache;
		_steps = steps;
		_resolvers = resolvers;
		_filters = filters;
		_fused = fused;
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
			if (!Bind(steps, in args, ref frame))
				return QueryResults<TValue>.Empty;
			Seed(steps, ref frame);
			// The eager core returns before Init when the candidate set is empty; BuildResults then
			// yields the shared Empty.
			if (frame.Seed.Count == 0)
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
			if (!Bind(steps, in args, ref frame))
				return 0;
			Seed(steps, ref frame);
			if (frame.Seed.Count == 0)
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

	// Binds every step in plan order and records the seed (the first active step — the eager `_first`
	// rule, so the encounter order is the eager one) and the probe lists. False when a step empties the
	// query (an empty unique In span): no seed walk, no store lookups, the eager zero-row result.
	private static bool Bind(IPipelineStep<TKey, TValue, TArgs>[] steps, in TArgs args, scoped ref PipelineFrame<TKey> frame) {
		var seed = -1;
		var keyProbes = 0;
		var valueProbes = 0;
		for (var i = 0; i < steps.Length; i++) {
			var step = steps[i];
			ref var binding = ref frame.Bindings[i];
			var activation = step.Bind(in args, ref binding);
			binding.Activation = activation;
			if (activation == StepActivation.Empty)
				return false;
			if (activation == StepActivation.Inactive)
				continue;
			if (seed < 0) {
				seed = i;
				continue;
			}

			if (step.Side == ProbeSide.Key)
				frame.KeyProbes[keyProbes++] = (byte)i;
			else
				frame.ValueProbes[valueProbes++] = (byte)i;
		}

		frame.SeedStep = seed;
		frame.KeyProbeCount = keyProbes;
		frame.ValueProbeCount = valueProbes;
		return true;
	}

	private void Seed(IPipelineStep<TKey, TValue, TArgs>[] steps, scoped ref PipelineFrame<TKey> frame) {
		var seedStep = frame.SeedStep;
		if (seedStep < 0) {
			AllRows(ref frame.Seed);
			return;
		}

		// Multi-bucket seeds dedupe through a set the eager UnionWith would also have inserted into;
		// created on demand by the step, released here whatever happens in the walk.
		var dedupe = default(ValueSet<TKey, DefaultKeyComparer<TKey>>);
		try {
			steps[seedStep].Seed(in frame.Bindings[seedStep], ref frame.Seed, ref dedupe);
		} finally {
			if (dedupe.IsInitlized)
				dedupe.Dispose();
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

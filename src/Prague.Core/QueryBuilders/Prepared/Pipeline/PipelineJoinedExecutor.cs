namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The stage-3 executor for joined plans whose narrowing is a pipeline plan (design §7, §8, steps 5 and
///   6). The <see cref="PipelineCore{TKey,TValue,TArgs}" /> pass replaces the eager base walk of
///   <c>ExecuteCoreJoined</c> / <c>ExecuteCoreJoinedTop</c> and drives the eager joined containers, and the
///   <c>JoinOne</c> resolvers that can fuse (<see cref="IJoinResolver.SupportsFusedLookup" /> and
///   <see cref="IJoinResolver.CanFuse" />: the four families, identity or selector, outer or inner, a
///   filter callback only once the build probe reduced it to a per-right check) answer their join with
///   one point lookup per row instead of a pair-set build plus a paired bulk read. The fill is driven
///   <b>per resolver</b>, not per row (<c>JoinedResultContaier.FillFused</c> →
///   <see cref="IJoinResolver.UnsafeFillFusedRows{TAccessor}" /> over every row the container holds): a
///   per-row walk of the chain costs a generic-dictionary lookup per link and measured slower than the
///   paired read it replaces, so the chain is walked once per resolver and the per-row work is the
///   resolver's own lookups and one slot write. An inner fused join's left without a right is neither
///   emitted nor counted (the eager <c>CountCoreJoined</c> narrowing).
///   <para>
///   <b>Classic flow</b> (unsorted, a classic <c>Sort</c> anywhere, or an unbounded / negative page): the
///   pass fills <see cref="JoinedResultContaier{TLeftKey,TLeftValue,TResolverChain,TResult}" /> with the
///   lefts, the fused resolvers write their slots and prune, then <c>ExecuteJoins(fusedMask)</c> runs the
///   sorter, the crop and any unfused resolver — the fill first, because an inner join's rows must be gone
///   before the sorter sees them. <b>Bounded flow</b> (an innermost <c>SortBounded</c> over the left value
///   and a finite page, the eager gate): the pass feeds <see cref="FrozenTopKJoinedContainer{TKey,TValue,TSorter}" />
///   — through a pooled <c>RowBuffer</c> that every inner fused resolver compacts first, so the dropped
///   lefts never reach the heap and the survivors keep eager's encounter ordinals — and the page rows are
///   materialized before the fused resolvers fill them, so only page rows are looked up (§7.3). That
///   container is the eager <see cref="TopKJoinedBaseContainer{TKey,TValue,TChain}" /> row for row, but it
///   is handed the chain's sorter as a struct type parameter (<c>IResolvers.WithSorter</c>, step 7) instead
///   of comparing through the chain, which costs a shared-generic hop per link per comparison.
///   </para>
///   The step-6 shape (an innermost <c>SortBounded</c> followed by outer <c>JoinOne</c>s) also admits
///   resolvers that cannot fuse (a filter callback): the mask leaves them their paired read over the page
///   rows.
///   <para>
///   <b><c>JoinMany</c></b> (step 8; design §7.2 as implemented): never fused — its rows are slots of one
///   shared buffer that the fan-out partitions once every left's right count is known, a two-pass shape —
///   but admitted, so the <i>narrowing</i> is the pipeline pass. Without a filter callback the join is
///   <b>fused</b> like a <c>JoinOne</c>: <c>JoinManyFusedFill</c> fills its slots in the fill walk with one
///   pass over the rows — per left the bucket, per right one store lookup, appended into one append-only
///   buffer in row order so each slot is a contiguous run, the slots given the buffer once the fill is done
///   — no pair set, no partitioning up front, no dictionary lookup per delivery; an inner one drops the rows
///   whose slot stayed empty, narrows a bounded page before the heap and a count by a bucket probe. A sorter
///   declared before the join keeps it unfused only when it is not the innermost one or does not order by
///   the left value (the container then crops before the ordinary walk fills only the page); an innermost
///   sorter over the left value fuses it, a classic <c>Sort</c> as much as a <c>SortBounded</c>. A filter
///   callback keeps it unfused too (the callback is a builder lambda over the paired core). An <b>unfused</b> outer <c>JoinMany</c> is an unfused resolver of the ordinary walk
///   (<c>ExecuteJoins</c> / <c>ExecuteJoinsBounded</c>): its <c>UnsafeExecuteWithAccessor</c> runs over the
///   rows the pass formed (or the page rows in the bounded flow), exactly as over the eager base walk's rows;
///   an unfused inner one runs in the fill walk (<c>FillFused(…, innerMany: true)</c>), in chain order with
///   the fused inner fills, drops the rows whose slot stayed empty — the eager <c>RetainNonEmptyManySlots</c>
///   narrowing — keeps the plan on the classic flow (eager's <c>AllInnerNarrowable</c> gate) and makes
///   <c>Count</c> form the rows and fill them (eager's <c>CountCoreJoined</c> runs the whole inner phase
///   too). An unfusable inner <c>JoinOne</c>, an unfusable <c>JoinOne</c> outside the step-6 shape, or a
///   nested <c>JoinMany</c> replays (§9). The resolver chain is copied onto the stack per execution because
///   resolvers carry per-execution scratch, as the prepared joined query does.
///   </para>
/// </summary>
internal readonly struct PipelineJoinedExecutor<TKey, TValue, TArgs, TResolverChain, TResult> : IFrozenExecutor<TArgs, TResult>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TResolverChain : struct, IResolvers
	where TResult : struct, IJoinResult<TValue>
	where TArgs : struct {
	private readonly PipelineCore<TKey, TValue, TArgs> _core;
	private readonly TResolverChain _resolvers;
	private readonly int _manyCount;
	// A bit per chain position filled in the pass; the rest (step-6 shape only) run after it.
	private readonly int _fusedMask;
	private readonly bool _allFused;
	private readonly bool _hasInner;
	// An UNFUSED inner JoinMany in the chain (step 8: a filter callback, or a classic Sort before it): the
	// fill walk runs its fan-out and prunes, and Count has to form the rows.
	private readonly bool _hasUnfusedInnerMany;
	// The fused INNER positions alone: the count that forms rows fills only what narrows.
	private readonly int _innerFusedMask;
	// Per chain position, a fused JoinMany's buffer size hint (the last execution's total; advisory).
	private readonly int[] _manyHints;
	// The resolver half of the eager bounded gate, decided once: the chain is immutable after build.
	private readonly bool _bounded;

	internal PipelineJoinedExecutor(InMemoryDataCache<TKey, TValue> cache, IPipelineStep<TKey, TValue, TArgs>[] steps, in TResolverChain resolvers, int manyCount,
		FilterStep<TValue, TArgs>[] filters, FusedFilter<TValue, TArgs>? fused, PipelinePlan<TKey, TValue, TArgs> plan, in JoinChainShape<TValue> shape,
		FilterStep<TValue, TArgs>[]? branchFilters = null) {
		_core = new(cache, steps, filters, fused, plan, branchFilters);
		_resolvers = resolvers;
		_manyCount = manyCount;
		_fusedMask = shape.FusedMask;
		_allFused = shape.Joins == shape.FusedJoins;
		_hasInner = shape.HasInner;
		_hasUnfusedInnerMany = shape.HasUnfusedInnerMany;
		_innerFusedMask = shape.InnerFusedMask;
		_manyHints = new int[JoinChainShape<TValue>.MaxPositions];
		_bounded = shape.BoundedCapable;
	}

	public static string Name => "Pipeline";

	/// <summary>
	///   The chain shapes this executor drives, decided once at build: at most one sorter, and every join
	///   either fusable (then fused), a <c>JoinMany</c> (its fan-out runs after the pass, step 8) or — only in
	///   the step-6 shape, an innermost bounded left-value sorter followed by outer joins — a <c>JoinOne</c>
	///   left to its paired read after the pass.
	/// </summary>
	internal static bool Accepts(in TResolverChain resolvers, bool fuseRegroupingInner, out JoinChainShape<TValue> shape) {
		var chain = resolvers;
		shape = new JoinChainShape<TValue> { AllowRegroupingInner = fuseRegroupingInner };
		chain.Execute(ref shape);
		// Arity: the fill walk indexes the hint array by chain position, so a chain deeper than the array
		// goes to the replay instead of reading past it. Unreachable while MaxPositions tracks the generated
		// arity — which is the point: raising MaxJoinResults without re-running T4 costs a plan, not a throw.
		if (shape.MaxPosition >= JoinChainShape<TValue>.MaxPositions)
			return false;
		if (shape.Sorters > 1 || shape.HasUnfusable || shape.HasUnfusedInner)
			return false;
		return shape.Joins == shape.FusedJoins + shape.ManyJoins - shape.FusedManyJoins || (shape.BoundedCapable && !shape.HasInner);
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
			return _bounded && PipelineCore<TKey, TValue, TArgs>.IsBoundedPage(skip, take)
				? ExecuteTop(in args, ref frame, ref chain, pool, clone, skip, take)
				: ExecuteClassic(in args, ref frame, ref chain, pool, clone, skip, take);
		} finally {
			_core.Release(ref frame);
		}
	}

	// CountCoreJoined narrows the candidates by the inner joins and counts the survivors: with outer joins
	// only that is the pipeline's own count, and with inner fused joins the counting pass collects the
	// matched keys and each inner resolver keeps the ones that have a right (no values, no container). An
	// inner JoinMany's answer per left is its fan-out's (the filter callback decides too), so that count
	// forms the rows and runs the inner fills — what eager's CountCoreJoined does when it runs the inner phase.
	public int Count(in TArgs args) {
		if (!_hasInner)
			return _core.Count(in args);
		if (_hasUnfusedInnerMany)
			return CountThroughFill(in args);
		var chain = _resolvers;
		var rows = new RowBuffer(withValues: false);
		try {
			_core.Count(in args, ref rows);
			return Narrow(ref chain, ref rows);
		} finally {
			rows.Dispose();
		}
	}

	// The classic flow without a result (step 8): the free-seeded pass forms the rows into a pooled container,
	// the fill walk runs the fused INNER fills and every inner JoinMany's fan-out in chain order and drops the
	// lefts without a right, and the survivors are the count. Outer joins are not run — they never change the
	// count — and nothing is built; the container's Dispose returns the rows and the fan-outs' buffers.
	[SkipLocalsInit]
	private int CountThroughFill(in TArgs args) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var frame = new PipelineFrame<TKey>(SeedKeys<TKey>.Over(stack));
		try {
			if (!_core.Open(in args, ref frame, freeSeed: true))
				return 0;
			var chain = _resolvers;
			var sampled = _core.BeginSampling();
			var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, true, false, 0, int.MaxValue, _manyCount);
			try {
				container.Init(frame.Seed.Count);
				container.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.ActiveFilterList, frame.Bindings, sampled, ref container));
				container.FillFused(_innerFusedMask, recount: true, _manyHints, innerMany: true);
				_core.EndSampling(sampled);
				return container.TotalCount;
			} finally {
				container.Dispose();
			}
		} finally {
			_core.Release(ref frame);
		}
	}

	// ExecuteCoreJoined with the pass in place of the base walk: the container is sized to the seed and
	// filled with the lefts, then each fused resolver writes its right slot of every row with one point
	// lookup per row (an inner one drops the rows without a right and the total becomes the survivors' —
	// the eager narrowing) and each inner JoinMany runs its fan-out and drops its empty rows (step 8), and
	// ExecuteJoins runs the sorter (stable, then the page crop) and any unfused resolver — an outer
	// JoinMany among them. The fill precedes the sorter because an inner join's rows must be gone before it sorts.
	private QueryResults<TResult> ExecuteClassic(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, skip, take, _manyCount);
		try {
			container.Init(frame.Seed.Count);
			container.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.ActiveFilterList, frame.Bindings, sampled, ref container));
			if (_fusedMask != 0 || _hasUnfusedInnerMany)
				container.FillFused(_fusedMask, recount: true, _manyHints, _hasUnfusedInnerMany);
			// Runs whatever the mask left: the sorter and the page crop always, an unfused resolver when there is one.
			container.ExecuteJoins(_fusedMask);
			var results = container.BuildResults();
			_core.EndSampling(sampled);
			return results;
		} finally {
			container.Dispose();
		}
	}

	// ExecuteCoreJoinedTop with the pass in place of the base walk: the bounded base container keeps the
	// [skip, skip + take) page in a heap of size skip + take (or collects and selects in place near the
	// full size), the page is materialized into the joined container in final order and the fused
	// resolvers fill only those rows (design §7.3) — the per-left lookup in place of the pair-set build.
	// An inner fused join narrows first, as the eager bounded core's narrow pass does: the lefts without a
	// right never reach the heap, so neither the page nor the total counts them.
	// Step 7: the heap and the page selection run inside BoundedFeeder, which the chain hands its sorter
	// statically typed, so every comparison calls the user comparer directly instead of walking the chain.
	private QueryResults<TResult> ExecuteTop(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, _manyCount, bounded: true);
		try {
			var feeder = new BoundedFeeder(in _core, in args, ref chain, ref container, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList,
				frame.ActiveFilterList, frame.Bindings, sampled, _hasInner, skip, take);
			chain.WithSorter(ref feeder);
			if (_fusedMask != 0)
				container.FillFused(_fusedMask, recount: false, _manyHints);
			if (!_allFused)
				container.ExecuteJoinsBounded(_fusedMask);
			var results = container.BuildResults();
			_core.EndSampling(sampled);
			return results;
		} finally {
			container.Dispose();
		}
	}

	/// <summary>
	///   The bounded flow's heap feed, run once the chain has handed over its sorter as a struct type
	///   parameter (<c>IResolvers.WithSorter</c>, design §13 step 7): fills
	///   <see cref="FrozenTopKJoinedContainer{TKey,TValue,TSorter}" /> from the pass, drains the page and
	///   materializes it into the joined container — everything whose cost is a comparison. The chain walk
	///   that finds the sorter is paid once per execution, not once per link per comparison. The joined
	///   container is a ref struct, so it travels as a laundered pointer (the codebase's ref-struct-in-a-
	///   ref-struct pattern); the frame's spans travel by value, so nothing stack-bound escapes.
	/// </summary>
	private unsafe ref struct BoundedFeeder : ISorterVisitor {
		private readonly PipelineCore<TKey, TValue, TArgs> _core;
		private readonly ref readonly TArgs _args;
		private readonly ref TResolverChain _chain;
		private readonly void* _container;
		private readonly ReadOnlySpan<TKey> _keys;
		private readonly ReadOnlySpan<byte> _keyProbes;
		private readonly ReadOnlySpan<byte> _valueProbes;
		private readonly ReadOnlySpan<byte> _branchFilters;
		private readonly ReadOnlySpan<StepBinding> _bindings;
		private readonly bool _sampled;
		private readonly bool _hasInner;
		private readonly int _skip;
		private readonly int _take;

		internal BoundedFeeder(in PipelineCore<TKey, TValue, TArgs> core, in TArgs args, ref TResolverChain chain,
			ref JoinedResultContaier<TKey, TValue, TResolverChain, TResult> container, ReadOnlySpan<TKey> keys, ReadOnlySpan<byte> keyProbes,
			ReadOnlySpan<byte> valueProbes, ReadOnlySpan<byte> branchFilters, ReadOnlySpan<StepBinding> bindings, bool sampled, bool hasInner, int skip, int take) {
			_core = core;
			_args = ref args;
			_chain = ref chain;
			_container = Unsafe.AsPointer(ref container);
			_keys = keys;
			_keyProbes = keyProbes;
			_valueProbes = valueProbes;
			_branchFilters = branchFilters;
			_bindings = bindings;
			_sampled = sampled;
			_hasInner = hasInner;
			_skip = skip;
			_take = take;
		}

		public void Visit<TSorter>(ref TSorter sorter) where TSorter : struct, IJoinResolver {
			var topK = new FrozenTopKJoinedContainer<TKey, TValue, TSorter>(sorter, _skip, _take);
			try {
				topK.Init(_keys.Length);
				if (_hasInner) {
					// The bounded flow's inner narrowing: the pass collects the matched (left key, left value)
					// pairs in its own order, each inner fused resolver keeps the ones that have a right (one
					// point lookup per row, compacted in place), and the survivors enter the heap in that order —
					// so their encounter ordinals, the bounded tie-breaker, are the ones eager's narrowed base
					// walk stamps.
					var rows = new RowBuffer(withValues: true);
					try {
						_core.Walk(in _args, _keys, _keyProbes, _valueProbes, _branchFilters, _bindings, _sampled, ref rows);
						var narrowed = Narrow(ref _chain, ref rows);
						var keys = rows.Keys;
						var values = rows.Values;
						for (var i = 0; i < narrowed; i++)
							topK.Add(keys[i], values[i]);
						topK.Seal(narrowed);
					} finally {
						rows.Dispose();
					}
				} else {
					topK.Seal(_core.Walk(in _args, _keys, _keyProbes, _valueProbes, _branchFilters, _bindings, _sampled, ref topK));
				}

				var kept = topK.Drain();
				ref var container = ref Unsafe.AsRef<JoinedResultContaier<TKey, TValue, TResolverChain, TResult>>(_container);
				container.MaterializeTopK(topK.Buffer, _skip, Math.Max(kept - _skip, 0), topK.TotalCount);
			} finally {
				topK.Dispose();
			}
		}
	}

	// Runs every inner fused resolver's narrowing over the collected rows, compacting them in place.
	private static int Narrow(scoped ref TResolverChain chain, scoped ref RowBuffer rows) {
		var narrower = new FusedNarrower(ref rows);
		chain.Execute(ref narrower);
		rows.Truncate(narrower.Count);
		return narrower.Count;
	}

	/// <summary>
	///   The rows one pass matched, in its order: pooled parallel key and value buffers the inner fused
	///   narrowing compacts in place. Rented on the first row, returned by <see cref="Dispose" />.
	/// </summary>
	private ref struct RowBuffer : IJoinedResultContainer<TKey, TValue> {
		private readonly bool _withValues;
		private TKey[]? _keys;
		private TValue[]? _values;
		private int _count;

		internal RowBuffer(bool withValues) => _withValues = withValues;

		internal int Count => _count;

		internal Span<TKey> Keys => _keys is null ? default : _keys.AsSpan(0, _count);

		internal Span<TValue> Values => _values is null ? default : _values.AsSpan(0, _count);

		public int TotalCount => 0;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TKey key, TValue value) {
			if (_keys is null || _count == _keys.Length)
				Grow();
			_keys![_count] = key;
			if (_withValues)
				_values![_count] = value;
			_count++;
			return 0;
		}

		internal void Truncate(int count) => _count = count;

		[MethodImpl(MethodImplOptions.NoInlining)]
		private void Grow() {
			var capacity = _keys is null ? 256 : _keys.Length * 2;
			var keys = PragueArrayPool<TKey>.Pool.Rent(capacity);
			var values = _withValues ? PragueArrayPool<TValue>.Pool.Rent(capacity) : null;
			if (_keys is not null) {
				_keys.AsSpan(0, _count).CopyTo(keys);
				PragueArrayPool<TKey>.Pool.Return(_keys, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
			}

			if (_values is not null) {
				_values.AsSpan(0, _count).CopyTo(values!);
				PragueArrayPool<TValue>.Pool.Return(_values, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
			}

			_keys = keys;
			_values = values;
		}

		public void Dispose() {
			var keys = _keys;
			var values = _values;
			_keys = null;
			_values = null;
			_count = 0;
			if (keys is not null)
				PragueArrayPool<TKey>.Pool.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
			if (values is not null)
				PragueArrayPool<TValue>.Pool.Return(values, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
		}
	}

	/// <summary>
	///   Walks the chain once and lets every inner fused resolver compact the collected rows to the ones
	///   that have a right (design §7.1: a left without a right is neither emitted nor counted). The
	///   static tests fold per resolver type, so a chain without inner joins walks to nothing.
	/// </summary>
	private unsafe ref struct FusedNarrower : IResolverExecutor {
		private readonly void* _rows;
		internal int Count;

		internal FusedNarrower(ref RowBuffer rows) {
			_rows = Unsafe.AsPointer(ref rows);
			Count = rows.Count;
		}

		public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
			// A JoinMany reaches this walk only when it is fused (the executor's gate): an unfused inner one keeps the chain on CountThroughFill and the classic flow.
			if (TResolver.IsSorter || !(TResolver.SupportsFusedLookup || TResolver.IsMany) || !resolver.Inner || Count == 0)
				return;
			ref var rows = ref Unsafe.AsRef<RowBuffer>(_rows);
			Count = resolver.UnsafeNarrowFused(rows.Keys[..Count], rows.Values.Length == 0 ? default : rows.Values[..Count]);
		}
	}
}

/// <summary>
///   Runs every resolver's build-time filter compilation (<see cref="IJoinResolver.CompileFusedFilter" />)
///   once over the chain the planner is about to freeze, so <see cref="IJoinResolver.CanFuse" /> can answer
///   for a filtered <c>JoinOne</c> when the shape walk asks. It must run on the chain the executor stores:
///   the shape walk takes a copy, and a copy's compiled check is lost.
/// </summary>
internal struct FusedFilterCompiler : IResolverExecutor {
	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver
		=> resolver.CompileFusedFilter();
}

/// <summary>
///   What a resolver chain looks like to the joined pipeline planner (design §7.1, §7.2, §9): the sorter's
///   place and kind (the eager <see cref="TopKProbeProcessor{TLeftValue}" /> questions), and per join
///   whether it fuses (<see cref="IJoinResolver.SupportsFusedLookup" /> and <see cref="IJoinResolver.CanFuse" />),
///   is a <c>JoinMany</c> (<see cref="IJoinResolver.IsMany" />: its fan-out runs after the pass), is inner, or
///   is something else. Walked once at build.
/// </summary>
internal struct JoinChainShape<TLeftValue> : IResolverExecutor {
	/// <summary>Fuse an inner join whose family regroups its rows, accepting the encounter-order change: true unless <see cref="FrozenOptions.PreserveEagerOrder" /> is set.</summary>
	internal bool AllowRegroupingInner;

	internal int Sorters;
	internal bool SorterInnermost;
	internal bool SorterAllowsBounded;
	internal bool SorterOrdersByLeftValues;
	internal int Joins;
	internal int FusedJoins;
	internal int FusedMask;
	/// <summary>
	///   One past the highest chain position a join can occupy: positions are 1-based and the generated
	///   <c>FillFused</c> walks them up to <see cref="JoinResultLimits.MaxJoinResults" />, so the hint array
	///   the executor hands it is sized to this. It was 8 against a 15-position walk — a chain of more than
	///   seven joins read past the array once per execution. <see cref="MaxPosition" /> guards the bound
	///   rather than trusting it.
	/// </summary>
	internal const int MaxPositions = JoinResultLimits.MaxJoinResults + 1;

	/// <summary>The fused positions that are inner: what a count has to fill.</summary>
	internal int InnerFusedMask;
	/// <summary>The <c>JoinMany</c>s (step 8): admitted always; fused (the frozen per-left fill, counted in <see cref="FusedJoins" /> too) without a filter callback and no classic sorter before them.</summary>
	internal int ManyJoins;
	internal int FusedManyJoins;
	internal bool HasInner;
	/// <summary>An inner <c>JoinMany</c> that runs its own fan-out: the chain forms rows to count and stays on the classic flow.</summary>
	internal bool HasUnfusedInnerMany;
	internal bool HasUnfusedInner;
	internal bool HasUnfusable;
	/// <summary>The highest position the chain reported: the planner rejects a chain that would index past the hint array.</summary>
	internal int MaxPosition;

	/// <summary>
	///   The eager <c>ExecuteCoreJoinedTop</c> resolver gate: one innermost sorter over the left value and
	///   every inner resolver able to narrow before the heap — a fused <c>JoinMany</c> can (its bucket
	///   probe), an unfused one cannot (its answer per left is the fan-out's), so that chain runs the classic
	///   flow as eager's <c>AllInnerNarrowable</c> gate does for every inner <c>JoinMany</c>. Unlike eager, a
	///   classic <c>Sort</c> qualifies too (step 8): the bounded container's encounter-ordinal ties are the
	///   stable sort's for the same input sequence, and comparer-equal rows have no specified order — so a
	///   finite page costs a heap of its size instead of the full sort. It keeps its free seed.
	/// </summary>
	internal bool BoundedCapable => Sorters == 1 && SorterInnermost && SorterOrdersByLeftValues && !HasUnfusedInnerMany;

	/// <summary>For <c>Explain()</c>: the bounded page flow when the gate can pass, else the classic container's sort (a classic <c>Sort</c> anywhere, a <c>SortBounded</c> after a join).</summary>
	internal PipelineSort Sort => Sorters == 0 ? PipelineSort.None : BoundedCapable ? PipelineSort.Bounded : PipelineSort.Classic;

	/// <summary>
	///   A classic <c>Sort</c>: the rows are fully sorted after the pass, so the seed may move (design §8). A
	///   <c>SortBounded</c> keeps the fixed seed — innermost, its tie-breaking ordinals are eager's; after a
	///   join, the classic container sorts the same input sequence eager sorts.
	/// </summary>
	internal bool ClassicSort => Sorters == 1 && !SorterAllowsBounded;

	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
		if (position > MaxPosition)
			MaxPosition = position;
		if (TResolver.IsSorter) {
			Sorters++;
			SorterInnermost = position == 0;
			SorterAllowsBounded = resolver.AllowsBounded;
			SorterOrdersByLeftValues = resolver.OrdersByLeftValues<TLeftValue>();
			return;
		}

		// Position 0 without a sorter is the chain base (BaseResolver): the left slot, not a join.
		if (position == 0)
			return;

		Joins++;
		var inner = resolver.Inner;
		HasInner |= inner;
		// A JoinMany (design §7.2 as implemented, step 8), admitted always. Without a filter callback it takes
		// the frozen per-left fill (JoinManyFusedFill) in the fill walk — unless a sorter over the joined row
		// precedes it, whose container sorts and crops before the ordinary walk fills only the page: an
		// innermost sorter over the left value is fine (the bounded flow materializes the page first; its
		// unbounded fallback needs every row anyway), a sorter after it needs every row filled regardless. Otherwise its own two-pass
		// fan-out runs after the pass over the rows the pass formed, an inner one dropping the lefts without
		// a right there. Folded per instantiation.
		if (TResolver.IsMany) {
			ManyJoins++;
			if (resolver.CanFuse && (Sorters == 0 || (SorterInnermost && SorterOrdersByLeftValues))) {
				FusedJoins++;
				FusedManyJoins++;
				FusedMask |= 1 << position;
				if (inner)
					InnerFusedMask |= 1 << position;
				return;
			}

			HasUnfusedInnerMany |= inner;
			return;
		}

		// An inner join whose family emits its rows regrouped (left-symmetric) fuses only when the caller
		// accepted the order change; otherwise the chain replays and keeps eager's sequence byte for byte.
		var regroups = inner && TResolver.FusedInnerRegroups && !AllowRegroupingInner;
		if (TResolver.SupportsFusedLookup && resolver.CanFuse && !regroups) {
			FusedJoins++;
			FusedMask |= 1 << position;
			if (inner)
				InnerFusedMask |= 1 << position;
			return;
		}

		HasUnfusable |= !TResolver.SupportsFusedLookup;
		HasUnfusedInner |= inner;
	}
}

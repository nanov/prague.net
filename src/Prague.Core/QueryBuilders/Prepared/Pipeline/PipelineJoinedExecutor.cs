namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The stage-3 executor for joined plans whose narrowing is a pipeline plan (design §7, §8, steps 5 and
///   6). The <see cref="PipelineCore{TKey,TValue,TArgs}" /> pass replaces the eager base walk of
///   <c>ExecuteCoreJoined</c> / <c>ExecuteCoreJoinedTop</c> and drives the eager joined containers, and the
///   <c>JoinOne</c> resolvers that can fuse (<see cref="IFusableJoinOne{TLeftKey,TLeftValue,TRightValue}" />:
///   the four families, identity or selector, outer or inner, no filter callback) answer their join with
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
///   and a finite page, the eager gate): the pass feeds <see cref="TopKJoinedBaseContainer{TKey,TValue,TChain}" />
///   — through a pooled <c>RowBuffer</c> that every inner fused resolver compacts first, so the dropped
///   lefts never reach the heap and the survivors keep eager's encounter ordinals — and the page rows are
///   materialized before the fused resolvers fill them, so only page rows are looked up (§7.3).
///   </para>
///   The step-6 shape (an innermost <c>SortBounded</c> followed by outer <c>JoinOne</c>s) also admits
///   resolvers that cannot fuse (a filter callback): the mask leaves them their paired read over the page
///   rows. Any <c>JoinMany</c>, an unfusable inner join, or an unfusable join outside that shape replays
///   (§7.2, §9). The resolver chain is copied onto the stack per execution because resolvers carry
///   per-execution scratch, as the prepared joined query does.
/// </summary>
internal readonly struct PipelineJoinedExecutor<TKey, TValue, TArgs, TResolverChain, TResult> : IFrozenExecutor<TArgs, TResult>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TResolverChain : struct, IResolvers
	where TResult : struct, IJoinResult<TValue> {
	private readonly PipelineCore<TKey, TValue, TArgs> _core;
	private readonly TResolverChain _resolvers;
	private readonly int _manyCount;
	// A bit per chain position filled in the pass; the rest (step-6 shape only) run after it.
	private readonly int _fusedMask;
	private readonly bool _allFused;
	private readonly bool _hasInner;
	// The resolver half of the eager bounded gate, decided once: the chain is immutable after build.
	private readonly bool _bounded;

	internal PipelineJoinedExecutor(InMemoryDataCache<TKey, TValue> cache, IPipelineStep<TKey, TValue, TArgs>[] steps, in TResolverChain resolvers, int manyCount,
		FilterStep<TValue, TArgs>[] filters, FusedFilter<TValue, TArgs>? fused, PipelinePlan<TKey, TValue, TArgs> plan, in JoinChainShape<TValue> shape) {
		_core = new(cache, steps, filters, fused, plan);
		_resolvers = resolvers;
		_manyCount = manyCount;
		_fusedMask = shape.FusedMask;
		_allFused = shape.Joins == shape.FusedJoins;
		_hasInner = shape.HasInner;
		_bounded = shape.BoundedCapable;
	}

	public static string Name => "Pipeline";

	/// <summary>
	///   The chain shapes this executor drives, decided once at build: at most one sorter, no
	///   <c>JoinMany</c>, and every join either fusable (then fused) or — only in the step-6 shape, an
	///   innermost bounded left-value sorter followed by outer joins — left to its paired read after the pass.
	/// </summary>
	internal static bool Accepts(in TResolverChain resolvers, bool fuseRegroupingInner, out JoinChainShape<TValue> shape) {
		var chain = resolvers;
		shape = new JoinChainShape<TValue> { AllowRegroupingInner = fuseRegroupingInner };
		chain.Execute(ref shape);
		if (shape.Sorters > 1 || shape.HasUnfusable || shape.HasUnfusedInner)
			return false;
		return shape.Joins == shape.FusedJoins || (shape.BoundedCapable && !shape.HasInner);
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
	// matched keys and each inner resolver keeps the ones that have a right (no values, no container).
	public int Count(in TArgs args) {
		if (!_hasInner)
			return _core.Count(in args);
		var chain = _resolvers;
		var rows = new RowBuffer(withValues: false);
		try {
			_core.Count(in args, ref rows);
			return Narrow(ref chain, ref rows);
		} finally {
			rows.Dispose();
		}
	}

	// ExecuteCoreJoined with the pass in place of the base walk: the container is sized to the seed and
	// filled with the lefts, then each fused resolver writes its right slot of every row with one point
	// lookup per row (an inner one drops the rows without a right and the total becomes the survivors' —
	// the eager narrowing), and ExecuteJoins runs the sorter (stable, then the page crop) and any unfused
	// resolver. The fill precedes the sorter because an inner join's rows must be gone before it sorts.
	private QueryResults<TResult> ExecuteClassic(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, skip, take, _manyCount);
		try {
			container.Init(frame.Seed.Count);
			container.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref container));
			if (_fusedMask != 0)
				container.FillFused(_fusedMask, recount: true);
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
	private QueryResults<TResult> ExecuteTop(in TArgs args, scoped ref PipelineFrame<TKey> frame, scoped ref TResolverChain chain, bool pool, bool clone, int skip, int take) {
		var sampled = _core.BeginSampling();
		var container = new JoinedResultContaier<TKey, TValue, TResolverChain, TResult>(ref chain, pool, clone, _manyCount, bounded: true);
		var topK = new TopKJoinedBaseContainer<TKey, TValue, TResolverChain>(ref chain, skip, take);
		try {
			topK.Init(frame.Seed.Count);
			if (_hasInner) {
				// The bounded flow's inner narrowing, inline because the heap holds a pointer to this
				// execution's chain copy and may not travel through another frame: the pass collects the
				// matched (left key, left value) pairs in its own order, each inner fused resolver keeps the
				// ones that have a right (one point lookup per row, compacted in place), and the survivors
				// enter the heap in that order — so their encounter ordinals, the bounded tie-breaker, are
				// the ones eager's narrowed base walk stamps.
				var rows = new RowBuffer(withValues: true);
				try {
					_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref rows);
					var narrowed = Narrow(ref chain, ref rows);
					var keys = rows.Keys;
					var values = rows.Values;
					for (var i = 0; i < narrowed; i++)
						topK.Add(keys[i], values[i]);
					topK.Seal(narrowed);
				} finally {
					rows.Dispose();
				}
			} else {
				topK.Seal(_core.Walk(in args, frame.Seed.Keys, frame.KeyProbeList, frame.ValueProbeList, frame.Bindings, sampled, ref topK));
			}

			var kept = topK.Drain();
			container.MaterializeTopK(topK.Buffer, skip, Math.Max(kept - skip, 0), topK.TotalCount);
			if (_fusedMask != 0)
				container.FillFused(_fusedMask, recount: false);
			if (!_allFused)
				container.ExecuteJoinsBounded(_fusedMask);
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
			if (TResolver.IsSorter || !TResolver.SupportsFusedLookup || !resolver.Inner || Count == 0)
				return;
			ref var rows = ref Unsafe.AsRef<RowBuffer>(_rows);
			Count = resolver.UnsafeNarrowFused(rows.Keys[..Count], rows.Values.Length == 0 ? default : rows.Values[..Count]);
		}
	}
}

/// <summary>
///   What a resolver chain looks like to the joined pipeline planner (design §7.1, §9): the sorter's
///   place and kind (the eager <see cref="TopKProbeProcessor{TLeftValue}" /> questions), and per join
///   whether it fuses (<see cref="IJoinResolver.SupportsFusedLookup" /> and <see cref="IJoinResolver.CanFuse" />),
///   is inner, or is something else (a <c>JoinMany</c>). Walked once at build.
/// </summary>
internal struct JoinChainShape<TLeftValue> : IResolverExecutor {
	/// <summary><see cref="FrozenOptions.FuseSymmetricInnerJoins" />: fuse an inner join whose family regroups its rows, accepting the encounter-order change.</summary>
	internal bool AllowRegroupingInner;

	internal int Sorters;
	internal bool SorterInnermost;
	internal bool SorterAllowsBounded;
	internal bool SorterOrdersByLeftValues;
	internal int Joins;
	internal int FusedJoins;
	internal int FusedMask;
	internal bool HasInner;
	internal bool HasUnfusedInner;
	internal bool HasUnfusable;

	/// <summary>The eager <c>ExecuteCoreJoinedTop</c> resolver gate: one innermost bounded sorter over the left value.</summary>
	internal bool BoundedCapable => Sorters == 1 && SorterInnermost && SorterAllowsBounded && SorterOrdersByLeftValues;

	/// <summary>For <c>Explain()</c>: the bounded page flow when the gate can pass, else the classic container's sort (a classic <c>Sort</c> anywhere, a <c>SortBounded</c> after a join).</summary>
	internal PipelineSort Sort => Sorters == 0 ? PipelineSort.None : BoundedCapable ? PipelineSort.Bounded : PipelineSort.Classic;

	/// <summary>
	///   A classic <c>Sort</c>: the rows are fully sorted after the pass, so the seed may move (design §8). A
	///   <c>SortBounded</c> keeps the fixed seed — innermost, its tie-breaking ordinals are eager's; after a
	///   join, the classic container sorts the same input sequence eager sorts.
	/// </summary>
	internal bool ClassicSort => Sorters == 1 && !SorterAllowsBounded;

	public void Process<TResolver>(int position, ref TResolver resolver) where TResolver : struct, IJoinResolver {
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
		// An inner join whose family emits its rows regrouped (left-symmetric) fuses only when the caller
		// accepted the order change; otherwise the chain replays and keeps eager's sequence byte for byte.
		var regroups = inner && TResolver.FusedInnerRegroups && !AllowRegroupingInner;
		if (TResolver.SupportsFusedLookup && resolver.CanFuse && !regroups) {
			FusedJoins++;
			FusedMask |= 1 << position;
			return;
		}

		HasUnfusable |= !TResolver.SupportsFusedLookup;
		HasUnfusedInner |= inner;
	}
}

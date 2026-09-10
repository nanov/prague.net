namespace Prague.Core;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Collections;

/// <summary>Limits shared by the pipeline's inline per-execution storage (a non-generic home so <c>[InlineArray]</c> can name them).</summary>
internal static class PipelineLimits {
	/// <summary>The most index steps a pipeline plan may have: its per-execution bindings live on the stack.</summary>
	internal const int MaxSteps = 16;

	/// <summary>Stack longs the seed key buffer starts with (1 KB); larger seeds spill to the array pool.</summary>
	internal const int SeedStackLongs = 128;

	/// <summary>Stack ints the small-probe seed's slot scratch starts with (1 KB); larger survivor sets spill to the array pool.</summary>
	internal const int SlotStackInts = 256;

	/// <summary>The most branches one <c>Or</c> step may have after flattening nested Ors: one first-leaf byte per branch in the step's binding, one bit per branch in its mask.</summary>
	internal const int MaxOrBranches = 8;
}

/// <summary>How one execution chose its seed (design §3.3 / §3.4).</summary>
internal enum SeedMode : byte {
	/// <summary>No active index step: the store walk.</summary>
	AllRows,

	/// <summary>The first active index step, walked in its own order — the eager <c>_first</c> rule.</summary>
	Fixed,

	/// <summary>
	///   The first active step still decides the order, but a smaller equality step is walked instead and
	///   its survivors are sorted by the slot they occupy in the first step's set — the eager sequence at
	///   the small step's cost.
	/// </summary>
	SmallProbe,

	/// <summary>The step with the smallest cardinality signal; encounter order follows it.</summary>
	Free,
}

/// <summary>The sorter a pipeline plan carries, for <c>Explain()</c> (design §8).</summary>
internal enum PipelineSort : byte {
	None,

	/// <summary>A classic <c>Sort</c>: the eager container sorts every row after the pass; the seed is free.</summary>
	Classic,

	/// <summary>A <c>SortBounded</c>: a finite page drives the eager top-k container, an unbounded one the classic container; the seed stays fixed so the tie-breaking ordinals are eager's.</summary>
	Bounded,
}

/// <summary>Which side of the store a step probes (design §4 / §14.1).</summary>
internal enum ProbeSide : byte {
	/// <summary>Consults the index before the store lookup — the eager step's own read, its staleness window.</summary>
	Key,

	/// <summary>Compares the value the pipeline already fetched — consistent with the row it returns.</summary>
	Value,
}

/// <summary>What one step's binding to this execution's arguments came to.</summary>
internal enum StepActivation : byte {
	/// <summary>Not bound yet (the frame's zero state).</summary>
	Unbound,

	/// <summary>Seeds or probes this execution.</summary>
	Active,

	/// <summary>The eager step would have been a no-op (an empty <c>In</c> span on a list index, an unbounded optional range): skipped.</summary>
	Inactive,

	/// <summary>The eager step would have emptied the candidates (an empty <c>In</c> span on a unique index): the query has no rows.</summary>
	Empty,
}

/// <summary>Two 16-byte slots for unmanaged index keys, contiguous by construction.</summary>
[InlineArray(4)]
internal struct BindingBits {
	private long _element0;
}

/// <summary>
///   One step's per-execution state, type-erased so the frame can hold every step's binding inline:
///   two 16-byte slots for unmanaged index keys (written and read with <see cref="Unsafe" />), two
///   reference slots for reference-type keys or a rented buffer, two ints for a span's offset and
///   length or the range bound kinds. A step whose key fits neither (an unmanaged struct over 16 bytes,
///   a struct with references) is rejected at build (<see cref="CanHold{T}" />) and the plan replays.
/// </summary>
internal struct StepBinding {
	internal BindingBits Bits;
	internal object? Ref0;
	internal object? Ref1;
	internal int Int0;
	internal int Int1;
	internal StepActivation Activation;

	internal static bool CanHold<T>() => !typeof(T).IsValueType || (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Unsafe.SizeOf<T>() <= 16);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Write<T>(int slot, T value) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
			// A reference type: CanHold rejected reference-carrying structs, so the box is a no-op cast.
			if (slot == 0) Ref0 = value;
			else Ref1 = value;
			return;
		}

		Unsafe.WriteUnaligned(ref Unsafe.Add(ref Unsafe.As<BindingBits, byte>(ref Bits), slot * 16), value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal readonly T Read<T>(int slot) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
			var boxed = slot == 0 ? Ref0 : Ref1;
			return Unsafe.As<object?, T>(ref boxed);
		}

		ref var bits = ref Unsafe.AsRef(in Bits);
		return Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref Unsafe.As<BindingBits, byte>(ref bits), slot * 16));
	}
}

/// <summary>
///   A parameterized multi-value step's span for this execution, stored as the memory's backing
///   object plus offset and length so the binding stays type-erased and nothing is boxed per execution.
/// </summary>
internal static class BindingMemory {
	internal static void Write<T>(ref StepBinding binding, ReadOnlyMemory<T> memory) {
		if (memory.Length == 0) {
			binding.Ref0 = null;
			binding.Int0 = 0;
			binding.Int1 = 0;
			return;
		}

		if (MemoryMarshal.TryGetArray(memory, out var segment)) {
			binding.Ref0 = segment.Array;
			binding.Int0 = segment.Offset;
			binding.Int1 = segment.Count;
			return;
		}

		if (MemoryMarshal.TryGetMemoryManager<T, MemoryManager<T>>(memory, out var manager, out var start, out var length)) {
			binding.Ref0 = manager;
			binding.Int0 = start;
			binding.Int1 = length;
			return;
		}

		ThrowUnsupported();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static ReadOnlySpan<T> Read<T>(in StepBinding binding) => binding.Ref0 switch {
		T[] array => new ReadOnlySpan<T>(array, binding.Int0, binding.Int1),
		MemoryManager<T> manager => manager.GetSpan().Slice(binding.Int0, binding.Int1),
		_ => default,
	};

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowUnsupported()
		=> throw new NotSupportedException("A frozen pipeline step accepts array- or MemoryManager-backed ReadOnlyMemory values.");
}

/// <summary>
///   The seed's flat key buffer: a stack span first (unmanaged keys of at most 8 bytes), the array pool
///   above it, grown by doubling, returned by <see cref="Dispose" />. Filled under the seed source's one
///   gate pin and read after it; the count is the exact result-buffer size (design §2.2).
/// </summary>
internal ref struct SeedKeys<TKey> : IKeySink<TKey> {
	private Span<TKey> _buffer;
	private TKey[]? _rented;
	private int _count;

	private SeedKeys(Span<TKey> initial) => _buffer = initial;

	/// <summary>A buffer starting on <paramref name="stack" /> when <typeparamref name="TKey" /> is an unmanaged type of at most 8 bytes; otherwise one that rents on the first key.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static SeedKeys<TKey> Over(Span<long> stack) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>() || Unsafe.SizeOf<TKey>() > sizeof(long))
			return default;
		var length = stack.Length * sizeof(long) / Unsafe.SizeOf<TKey>();
		return new(MemoryMarshal.CreateSpan(ref Unsafe.As<long, TKey>(ref MemoryMarshal.GetReference(stack)), length));
	}

	internal int Count => _count;

	internal ReadOnlySpan<TKey> Keys => _buffer[.._count];

	/// <summary>The copied keys, writable: the small-probe seed compacts and reorders them in place.</summary>
	internal Span<TKey> MutableKeys => _buffer[.._count];

	/// <summary>Drops every key past <paramref name="count" /> (after an in-place compaction).</summary>
	internal void Truncate(int count) => _count = count;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(TKey key) {
		var count = _count;
		if ((uint)count >= (uint)_buffer.Length)
			Grow();
		_buffer[count] = key;
		_count = count + 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow() {
		var next = PragueArrayPool<TKey>.Pool.Rent(Math.Max(_buffer.Length * 2, 256));
		_buffer[.._count].CopyTo(next);
		var previous = _rented;
		_rented = next;
		_buffer = next;
		if (previous is not null)
			PragueArrayPool<TKey>.Pool.Return(previous, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
	}

	internal void Dispose() {
		var rented = _rented;
		_rented = null;
		_buffer = default;
		_count = 0;
		if (rented is not null)
			PragueArrayPool<TKey>.Pool.Return(rented, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
	}
}

[InlineArray(PipelineLimits.MaxSteps)]
internal struct StepBindings {
	private StepBinding _element0;
}

[InlineArray(PipelineLimits.MaxSteps)]
internal struct StepIndices {
	private byte _element0;
}

/// <summary>
///   One execution's state: every step's binding, the seed step chosen (-1 = all rows), the probe
///   steps split by side in plan order, the branch filters the taken <c>If</c> / <c>Match</c> arms
///   activated, and the seed buffer. Lives on the executing thread's stack; nothing here is shared
///   (design §10).
/// </summary>
internal ref struct PipelineFrame<TKey> {
	internal PipelineFrame(SeedKeys<TKey> seed) => Seed = seed;

	internal StepBindings Bindings;
	internal StepIndices Active;
	internal StepIndices KeyProbes;
	internal StepIndices ValueProbes;
	internal StepIndices ActiveFilters;
	internal int ActiveCount;
	internal int KeyProbeCount;
	internal int ValueProbeCount;
	internal int ActiveFilterCount;
	internal int SeedStep;
	internal int SmallStep;
	internal SeedMode Mode;
	internal SeedKeys<TKey> Seed;

	internal ReadOnlySpan<byte> ActiveList {
		[UnscopedRef] get => ((ReadOnlySpan<byte>)Active)[..ActiveCount];
	}

	internal ReadOnlySpan<byte> ActiveFilterList {
		[UnscopedRef] get => ((ReadOnlySpan<byte>)ActiveFilters)[..ActiveFilterCount];
	}

	internal ReadOnlySpan<byte> KeyProbeList {
		[UnscopedRef] get => ((ReadOnlySpan<byte>)KeyProbes)[..KeyProbeCount];
	}

	internal ReadOnlySpan<byte> ValueProbeList {
		[UnscopedRef] get => ((ReadOnlySpan<byte>)ValueProbes)[..ValueProbeCount];
	}
}

/// <summary>
///   One index step of a pipeline plan (design §4): bound once per execution, then either the seed —
///   its keys copied out under one gate pin in the index's enumeration order — or a probe on the key
///   (before the store lookup) or on the fetched value. Immutable after build; boxed once.
/// </summary>
internal interface IPipelineStep<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	NarrowerKind Kind { get; }

	/// <summary>Which of <see cref="ProbeKey" /> / <see cref="ProbeValue" /> the pipeline calls when the step is not the seed.</summary>
	ProbeSide Side { get; }

	/// <summary>True when <see cref="Bind" /> may rent and <see cref="Release" /> must run.</summary>
	bool NeedsRelease { get; }

	/// <summary>Resolves the execution arguments into <paramref name="binding" /> once (selectors, range bounds).</summary>
	StepActivation Bind(in TArgs args, ref StepBinding binding);

	/// <summary>
	///   True when <see cref="Signal" /> is an exact live count (unique, list, key-set — the equality steps,
	///   which may also walk as the small side of a small-probe seed); false for the B+tree estimates
	///   (range, last-updated), which only ever pick a seed and never decide emptiness.
	/// </summary>
	bool ExactSignal { get; }

	/// <summary>True when the step is backed by one <see cref="PooledSet{T,TKeyComparer}" /> whose slot order is the step's seed order, so <see cref="TryGetSlot" /> can reproduce it.</summary>
	bool SlotAddressable { get; }

	/// <summary>The rows this binding would seed — a live count or a B+tree estimate (design §3.1); read only when a seed has to be chosen.</summary>
	int Signal(in StepBinding binding);

	/// <summary>Copies the keys the step's index holds for the binding into <paramref name="seed" />; <paramref name="dedupe" /> is created on demand by multi-bucket seeds.</summary>
	void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe);

	/// <summary>For a <see cref="SlotAddressable" /> step: the slot <paramref name="key" /> occupies in the bound set — its position in the step's seed order — or false when absent.</summary>
	bool TryGetSlot(TKey key, in StepBinding binding, out int slot);

	bool ProbeKey(TKey key, in StepBinding binding);

	bool ProbeValue(TKey key, TValue value, in StepBinding binding);

	void Release(ref StepBinding binding);
}

/// <summary>
///   A narrower that can become a pipeline step. Implemented explicitly by the non-composite narrowers
///   (the <see cref="IPointLookupSource{TKey,TValue,TArgs}" /> pattern): the narrower knows its
///   <c>TIndexKey</c>, so it constructs the closed step; the planner type-tests the descriptor's source.
/// </summary>
internal interface IPipelineStepSource<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	/// <summary>The step, or <c>null</c> when this narrower cannot run in the pipeline under <paramref name="options" /> (a key the binding cannot hold, a range probe with <see cref="FrozenOptions.IndexSideProbes" />): the plan replays.</summary>
	IPipelineStep<TKey, TValue, TArgs>? CreatePipelineStep(FrozenOptions options);
}

/// <summary>Defaults shared by the steps: no rental, the unused probe side answers true.</summary>
internal abstract class PipelineStepBase<TKey, TValue, TArgs> : IPipelineStep<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	public abstract NarrowerKind Kind { get; }

	public abstract ProbeSide Side { get; }

	public virtual bool NeedsRelease => false;

	public virtual bool ExactSignal => true;

	public virtual bool SlotAddressable => false;

	public abstract StepActivation Bind(in TArgs args, ref StepBinding binding);

	public abstract int Signal(in StepBinding binding);

	public abstract void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe);

	public virtual bool TryGetSlot(TKey key, in StepBinding binding, out int slot) {
		slot = -1;
		return false;
	}

	public virtual bool ProbeKey(TKey key, in StepBinding binding) => true;

	public virtual bool ProbeValue(TKey key, TValue value, in StepBinding binding) => true;

	public virtual void Release(ref StepBinding binding) { }

	/// <summary>Copies one list bucket into the seed (the bulk <see cref="PooledSet{T,TKeyComparer}.CopyKeysTo{TSink}" />), deduplicating through <paramref name="dedupe" /> when asked (the eager <c>UnionWith</c> into a set).</summary>
	protected static void SeedBucket(PooledSet<TKey, DefaultKeyComparer<TKey>> bucket, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe, bool dedupeKeys) {
		if (!dedupeKeys) {
			bucket.CopyKeysTo(ref seed);
			return;
		}

		if (!dedupe.IsInitlized)
			dedupe = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		var sink = new DedupeSink<TKey>(ref seed, ref dedupe);
		bucket.CopyKeysTo(ref sink);
	}
}

/// <summary>
///   A seed sink that admits each key once through a <see cref="ValueSet{T,TKeyComparer}" /> — the
///   eager <c>UnionWith</c> chain's first-occurrence rule for overlapping buckets. The seed is a ref
///   struct, which a ref field cannot name (CS9050); both pointers are laundered through <c>void*</c>
///   and the sink never outlives the copy it is passed to.
/// </summary>
internal unsafe ref struct DedupeSink<TKey>(ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) : IKeySink<TKey>
	where TKey : IEquatable<TKey> {
	private readonly void* _seed = Unsafe.AsPointer(ref seed);
	private readonly void* _dedupe = Unsafe.AsPointer(ref dedupe);

	public void Add(TKey key) {
		if (Unsafe.AsRef<ValueSet<TKey, DefaultKeyComparer<TKey>>>(_dedupe).Add(key))
			Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(key);
	}
}

/// <summary>
///   B+tree walk aggregators that copy the walked keys into the seed — plain, minus one excluded index
///   key, minus two (the eager <c>IndexSkip</c> / <c>IndecesSkip</c> semantics). The seed is a ref struct,
///   which a ref field cannot name (CS9050); the pointer is laundered through <c>void*</c> and recovered
///   with <see cref="Unsafe.AsRef{T}(void*)" /> — the aggregator never outlives the walk it is passed to.
/// </summary>
internal static unsafe class SeedAggregators<TKey, TIndexKey>
	where TKey : IEquatable<TKey>
	where TIndexKey : IComparable<TIndexKey> {
	internal ref struct Plain(ref SeedKeys<TKey> seed) : PooledBTree<TIndexKey, TKey>.IResultAggregator {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);

		public void Add(TIndexKey index, TKey value) => Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(value);

		public void Dispose() { }
	}

	internal ref struct SkipOne(ref SeedKeys<TKey> seed, TIndexKey skip) : PooledBTree<TIndexKey, TKey>.IResultAggregator {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly TIndexKey _skip = skip;

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_skip) != 0)
				Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(value);
		}

		public void Dispose() { }
	}

	internal ref struct SkipTwo(ref SeedKeys<TKey> seed, TIndexKey skip1, TIndexKey skip2) : PooledBTree<TIndexKey, TKey>.IResultAggregator {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly TIndexKey _skip1 = skip1;
		private readonly TIndexKey _skip2 = skip2;

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_skip1) != 0 && index.CompareTo(_skip2) != 0)
				Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(value);
		}

		public void Dispose() { }
	}
}

/// <summary>
///   Store-walk sinks that copy walked keys into the seed — every row (the eager all-rows seed) or the
///   rows a set admits (the eager Or-first result: the store order, kept to the branch union). The
///   seed and the set are ref / plain structs on the caller's stack, laundered through <c>void*</c> as
///   the aggregators are; the walk never outlives the frame.
/// </summary>
internal static unsafe class SeedCollectors<TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	internal ref struct All(ref SeedKeys<TKey> seed) : IResultContainerInitializer<TKey, TValue> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);

		public void Init(int maxCount) { }

		public void Seal(int actualCount) { }

		public int Add(TKey foreignKey, TValue result) {
			Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(foreignKey);
			return 0;
		}

		public int TotalCount => 0;
	}

	internal ref struct Filtered(ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> admit) : IResultContainerInitializer<TKey, TValue> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly void* _admit = Unsafe.AsPointer(ref admit);

		public void Init(int maxCount) { }

		public void Seal(int actualCount) { }

		public int Add(TKey foreignKey, TValue result) {
			if (Unsafe.AsRef<ValueSet<TKey, DefaultKeyComparer<TKey>>>(_admit).Contains(foreignKey))
				Unsafe.AsRef<SeedKeys<TKey>>(_seed).Add(foreignKey);
			return 0;
		}

		public int TotalCount => 0;
	}
}

/// <summary>
///   What <c>Explain()</c> prints for a pipeline plan: the seed rule, every step's probe side, the
///   composite shape, and the last execution's seed decision and arm selections. The decision is
///   advisory shared state — plain int fields written only when the decision changes, so steady-state
///   executions of one plan read a line they never write.
/// </summary>
internal sealed class PipelinePlan<TKey, TValue, TArgs> : IPlanExplainable
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly IPipelineStep<TKey, TValue, TArgs>[] _steps;
	private readonly int _filters;
	private readonly bool _fused;
	private readonly bool _freeSeed;
	private readonly PipelineSort _sort;
	private readonly int _joins;
	private readonly int _fusedJoins;
	private readonly PipelineTree<TArgs>? _tree;
	private readonly int _branchFilters;
	private readonly bool _orSeed;
	private readonly int[] _lastArms;
	private readonly int[] _lastOrMasks;
	private int _lastDecision = -1;
	private int _lastSeedSignal;
	private int _lastOtherSignal;

	internal PipelinePlan(IPipelineStep<TKey, TValue, TArgs>[] steps, int filters, bool fused, bool freeSeed, PipelineSort sort, int joins, int fusedJoins = 0,
		PipelineTree<TArgs>? tree = null, int branchFilters = 0, bool orSeed = false) {
		_steps = steps;
		_filters = filters;
		_fused = fused;
		_freeSeed = freeSeed;
		_sort = sort;
		_joins = joins;
		_fusedJoins = fusedJoins;
		_tree = tree;
		_branchFilters = branchFilters;
		_orSeed = orSeed;
		_lastArms = new int[tree?.SelectCount ?? 0];
		_lastOrMasks = new int[tree is null ? 0 : steps.Length];
		Array.Fill(_lastArms, -2);
		Array.Fill(_lastOrMasks, -1);
	}

	/// <summary>True when <c>Execute*</c> seeds from the smallest signal (a classic <c>Sort</c>, or <see cref="FrozenOptions.ReorderIndexNarrowers" />); <c>Count</c> always does.</summary>
	internal bool FreeSeed => _freeSeed;

	/// <summary>The composite shape, or <c>null</c> for a flat plan.</summary>
	internal PipelineTree<TArgs>? Tree => _tree;

	/// <summary><see cref="FrozenOptions.OrSeed" /> as bound.</summary>
	internal bool OrSeed => _orSeed;

	/// <summary>The arm one select node took (-1: none); written only when it changes.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void RecordArm(int node, int arm) {
		if (_lastArms[node] != arm)
			_lastArms[node] = arm;
	}

	/// <summary>The active branch mask of one Or step (bit 31: the Or was first); written only when it changes.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void RecordOr(int step, int mask, bool first) {
		var packed = mask | (first ? int.MinValue : 0);
		if (_lastOrMasks[step] != packed)
			_lastOrMasks[step] = packed;
	}

	// Written only when the (mode, seed, small) decision changes, so the signals printed are those of the
	// execution that last changed it — a plan executed with one set of arguments never writes here again.
	internal void Record(SeedMode mode, int seed, int small, int seedSignal, int otherSignal, bool orUnion = false) {
		var packed = (int)mode | (seed + 1) << 8 | (small + 1) << 16 | (orUnion ? 1 << 24 : 0);
		if (packed == _lastDecision)
			return;
		_lastSeedSignal = seedSignal;
		_lastOtherSignal = otherSignal;
		_lastDecision = packed;
	}

	public void Explain(StringBuilder sb) {
		sb.Append("pipeline: seed = ").Append(_freeSeed ? "free" : "fixed").Append(" for Execute, free for Count (fixed: the first active index step, walking a smaller equality step instead when 2 × its signal ≤ the first's and sorting the survivors by slot; free: the smallest signal, unique first, an estimate only when estimate × 2 < the best exact count), steps: [");
		for (var i = 0; i < _steps.Length; i++) {
			if (i > 0)
				sb.Append(", ");
			sb.Append(i).Append(' ').Append(_steps[i].Kind).Append(" probe: ").Append(_steps[i].Side == ProbeSide.Key ? "key-side" : "value-side");
		}

		sb.Append("], filters: ").Append(_filters).Append(_fused ? " (fused, order below)" : " (direct)");
		if (_branchFilters > 0)
			sb.Append(", branch filters: ").Append(_branchFilters).Append(" (direct, after the top-level ones, when their arm is taken)");
		if (_tree is not null) {
			sb.Append(", shape: ");
			PipelineNode<TArgs>.Explain(sb, _tree.Top);
			sb.Append(" (an if / match arm is chosen once at bind and its steps spliced in place; an Or probes as the OR of its branches' ANDs, seeds ")
				.Append(_orSeed ? "from the union of its branches in branch order (OrSeed, the default)" : "the store walk in store order kept to the union of its branches (OrSeed = false: the eager sequence; the union itself for Count / Sort)")
				.Append(')');
		}

		switch (_sort) {
			case PipelineSort.Classic:
				sb.Append(", sort: classic (the container sorts every row after the pass)");
				break;
			case PipelineSort.Bounded:
				sb.Append(", sort: bounded (a finite page feeds the top-k container, ties by encounter ordinal; take = int.MaxValue or a negative page feeds the classic container)");
				break;
		}

		if (_joins > 0)
			sb.Append(", joins: ").Append(_joins).Append(" (fused: ").Append(_fusedJoins).Append(", unfused: ").Append(_joins - _fusedJoins)
				.Append("; fused: one right lookup per row in the pass, an inner miss drops the row; unfused: the resolver's paired read after the pass)");
		sb.AppendLine();
		ExplainLastBind(sb);
		var decision = _lastDecision;
		if (decision < 0)
			return;
		var mode = (SeedMode)(decision & 0xFF);
		var seed = ((decision >> 8) & 0xFF) - 1;
		var small = ((decision >> 16) & 0xFF) - 1;
		var orUnion = (decision & 1 << 24) != 0;
		sb.Append("  last seed: ");
		switch (mode) {
			case SeedMode.AllRows:
				sb.AppendLine("all rows (no active index step)");
				break;
			case SeedMode.SmallProbe:
				sb.Append("step ").Append(small).Append(' ').Append(_steps[small].Kind).Append(" (signal ").Append(_lastOtherSignal)
					.Append("), probe: slot-sorted into step ").Append(seed).Append(' ').Append(_steps[seed].Kind).Append(" (signal ").Append(_lastSeedSignal).AppendLine(")");
				break;
			default:
				sb.Append("step ").Append(seed).Append(' ').Append(_steps[seed].Kind).Append(" (signal ").Append(_lastSeedSignal).Append("), ")
					.Append(mode == SeedMode.Fixed ? "fixed: first active step" : "free: smallest signal");
				if (_steps[seed].Kind == NarrowerKind.Or && seed < _lastOrMasks.Length && _lastOrMasks[seed] != -1)
					sb.Append((_lastOrMasks[seed] & int.MaxValue) == 0
						? " — the Or narrowed nothing: the store walk"
						: orUnion ? " — the union of the branches" : " — the store walk kept to the branch union");
				sb.AppendLine();
				break;
		}
	}

	// The arms the last bind took and the branches of every Or that narrowed, in plan order.
	private void ExplainLastBind(StringBuilder sb) {
		if (_tree is null)
			return;
		var any = false;
		for (var i = 0; i < _lastArms.Length; i++) {
			if (_lastArms[i] == -2)
				continue;
			sb.Append(any ? ", " : "  last bind: ").Append("select#").Append(i).Append(" → ").Append(_lastArms[i] < 0 ? "no arm" : "arm " + _lastArms[i]);
			any = true;
		}

		for (var s = 0; s < _lastOrMasks.Length; s++) {
			var packed = _lastOrMasks[s];
			if (packed == -1)
				continue;
			sb.Append(any ? ", " : "  last bind: ").Append("step ").Append(s).Append(" Or → ");
			var mask = packed & int.MaxValue;
			if (mask == 0) {
				sb.Append(packed < 0 ? "no branch narrowed (first: admits every row)" : "no branch narrowed (inactive)");
			} else {
				sb.Append("branches {");
				var first = true;
				for (var b = 0; mask != 0; b++, mask >>= 1) {
					if ((mask & 1) == 0)
						continue;
					sb.Append(first ? "" : ", ").Append(b + 1);
					first = false;
				}

				sb.Append('}');
			}

			any = true;
		}

		if (any)
			sb.AppendLine();
	}
}

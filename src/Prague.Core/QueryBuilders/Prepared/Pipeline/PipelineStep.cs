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
///   steps split by side in plan order, and the seed buffer. Lives on the executing thread's stack;
///   nothing here is shared (design §10).
/// </summary>
internal ref struct PipelineFrame<TKey> {
	internal PipelineFrame(SeedKeys<TKey> seed) => Seed = seed;

	internal StepBindings Bindings;
	internal StepIndices KeyProbes;
	internal StepIndices ValueProbes;
	internal int KeyProbeCount;
	internal int ValueProbeCount;
	internal int SeedStep;
	internal SeedKeys<TKey> Seed;

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

	/// <summary>Copies the keys the step's index holds for the binding into <paramref name="seed" />; <paramref name="dedupe" /> is created on demand by multi-bucket seeds.</summary>
	void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe);

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

	public abstract StepActivation Bind(in TArgs args, ref StepBinding binding);

	public abstract void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe);

	public virtual bool ProbeKey(TKey key, in StepBinding binding) => true;

	public virtual bool ProbeValue(TKey key, TValue value, in StepBinding binding) => true;

	public virtual void Release(ref StepBinding binding) { }

	/// <summary>Cold-path helper: walks one list bucket into the seed, deduplicating through <paramref name="dedupe" /> when asked (the eager <c>UnionWith</c> into a set).</summary>
	protected static void SeedBucket(PooledSet<TKey, DefaultKeyComparer<TKey>> bucket, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe, bool dedupeKeys) {
		if (!dedupeKeys) {
			foreach (var key in bucket)
				seed.Add(key);
			return;
		}

		if (!dedupe.IsInitlized)
			dedupe = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		foreach (var key in bucket)
			if (dedupe.Add(key))
				seed.Add(key);
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

/// <summary>What <c>Explain()</c> prints for a pipeline plan: the seed rule and every step's probe side.</summary>
internal sealed class PipelinePlan<TKey, TValue, TArgs> : IPlanExplainable
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly IPipelineStep<TKey, TValue, TArgs>[] _steps;
	private readonly int _filters;
	private readonly bool _fused;

	internal PipelinePlan(IPipelineStep<TKey, TValue, TArgs>[] steps, int filters, bool fused) {
		_steps = steps;
		_filters = filters;
		_fused = fused;
	}

	public void Explain(StringBuilder sb) {
		sb.Append("pipeline: seed = first active index step (fixed order), steps: [");
		for (var i = 0; i < _steps.Length; i++) {
			if (i > 0)
				sb.Append(", ");
			sb.Append(i).Append(' ').Append(_steps[i].Kind).Append(" probe: ").Append(_steps[i].Side == ProbeSide.Key ? "key-side" : "value-side");
		}

		sb.Append("], filters: ").Append(_filters).Append(_fused ? " (fused, order below)" : " (direct)").AppendLine();
	}
}

namespace Prague.Core;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Collections;

/// <summary>
///   An <c>Or</c> as a pipeline step (design §5.1). Its branches' leaf steps sit in the plan's step
///   array like every other step; the core binds them in <i>branch mode</i> (see
///   <c>PipelineCore.BindOr</c>) and writes the outcome into this step's binding: which branches
///   narrowed, which of those are empty, each branch's first active leaf, and whether the Or is the
///   first narrowing of the execution. From there:
///   <para>
///   <b>As a probe</b> — a candidate passes when some active branch admits it, a branch being the AND
///   of its active leaves' own probes (key-side leaves on the key, value-side on the fetched value; the
///   eager bitmap marks then prunes, a pure set operation, so the per-row disjunction of conjunctions
///   is result-identical). A branch that narrowed to nothing (an empty <c>In</c> span, the eager
///   intersecter's <c>Clear</c>) admits nothing; an Or none of whose branches narrowed is a no-op —
///   inactive when it is not first, and when it <i>is</i> first an active step that admits every row,
///   because the eager <c>OrWith</c> auto-seeds the whole store and clears <c>_first</c> before it looks
///   at the branches, so a following index step intersects the store walk instead of seeding.
///   </para>
///   <para>
///   <b>As the seed</b> — the eager Or-first result is the store walk, in store order, kept to the
///   branch union; so by default the step builds the union once (each active branch's first leaf,
///   admitted once across branches through the seed's dedupe set — the eager <c>UnionWith</c> rule)
///   and copies the store walk out <i>filtered by that set</i>: the eager sequence byte for byte, at
///   the cost of one hash probe per store row instead of one set insert — what
///   <see cref="FrozenOptions.PreserveEagerOrder" /> asks for. By default, and for <c>Count</c> and a
///   classic <c>Sort</c> regardless, the union itself is the seed, in branch order. Either way the Or
///   stays a probe on the walk when a branch has more than one leaf (the union is that branch's first
///   leaf, a superset) — <see cref="ProbeAfterSeed" />.
///   </para>
///   The step reads its leaves' bindings through a pointer to the frame's binding array written at bind
///   (the frame is a ref struct on the executing thread's stack, alive for the execution — the
///   <c>SeedAggregators</c> laundering pattern).
/// </summary>
internal sealed unsafe class OrStep<TKey, TValue, TArgs> : IPipelineStep<TKey, TValue, TArgs>, IOrStepExplain
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	/// <summary>One branch: its nodes (leaves and narrow-only <c>If</c> / <c>Match</c>), every leaf reachable in it, and whether it counts as a narrowing of the Or (a branch flattened out of a nested Or does not — the eager nested <c>OrWith</c> unions its bits into the enclosing branch without touching its <c>_first</c>).</summary>
	internal sealed class Branch(PipelineNode<TArgs>[] nodes, byte[] leaves, bool narrows) {
		internal readonly PipelineNode<TArgs>[] Nodes = nodes;
		internal readonly byte[] Leaves = leaves;
		internal readonly bool Narrows = narrows;
		internal bool[] KeySide = [];
	}

	private const int FlagNarrowed = 1;
	private const int FlagNeedsProbe = 2;
	private const int FlagFirst = 4;
	private const int FlagUnionSeed = 8;
	private const byte NoLeaf = 0xFF;

	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly Branch[] _branches;
	private IPipelineStep<TKey, TValue, TArgs>[] _steps = [];
	private bool _exactSignal;

	internal OrStep(InMemoryDataCache<TKey, TValue> cache, Branch[] branches) {
		Debug.Assert(branches.Length <= PipelineLimits.MaxOrBranches);
		_cache = cache;
		_branches = branches;
	}

	internal Branch[] Branches => _branches;

	/// <summary>Called once by the planner when the step array is complete: resolves each leaf's probe side once, so the per-row conjunction reads a bool instead of a virtual property.</summary>
	internal void Attach(IPipelineStep<TKey, TValue, TArgs>[] steps) {
		_steps = steps;
		var exact = true;
		for (var b = 0; b < _branches.Length; b++) {
			var branch = _branches[b];
			var leaves = branch.Leaves;
			branch.KeySide = new bool[leaves.Length];
			for (var i = 0; i < leaves.Length; i++) {
				var step = steps[leaves[i]];
				branch.KeySide[i] = step.Side == ProbeSide.Key;
				exact &= step.ExactSignal;
			}
		}

		_exactSignal = exact;
	}

	public NarrowerKind Kind => NarrowerKind.Or;

	public ProbeSide Side => ProbeSide.Value;

	public bool NeedsRelease => false;

	// Conservative: every leaf of every branch is an exact signal (unique, list, key-set). A range or
	// last-updated leaf anywhere makes the sum an estimate.
	public bool ExactSignal => _exactSignal;

	/// <summary>The core binds an Or through <c>BindOr</c>, never through this.</summary>
	public StepActivation Bind(in TArgs args, ref StepBinding binding) => throw new UnreachableException();

	// ── The binding: slot 0 = the frame's binding array, slot 1 = one first-leaf byte per branch,
	// Int0 = the active (narrowed and non-empty) branch mask, Int1 = the flags above. ─────────────

	private static ref byte FirstLeafSlot(ref StepBinding binding, int branch)
		=> ref Unsafe.Add(ref Unsafe.As<BindingBits, byte>(ref binding.Bits), 16 + branch);

	private static byte FirstLeaf(in StepBinding binding, int branch)
		=> Unsafe.Add(ref Unsafe.As<BindingBits, byte>(ref Unsafe.AsRef(in binding.Bits)), 16 + branch);

	private static ReadOnlySpan<StepBinding> Bindings(in StepBinding binding) {
		ref var all = ref Unsafe.AsRef<StepBindings>((void*)binding.Read<long>(0));
		return all;
	}

	/// <summary>
	///   Writes one execution's outcome (from <c>PipelineCore.BindOr</c>): the frame's binding array, each
	///   branch's first active leaf (<see cref="NoLeaf" /> for none), the active mask and whether the Or
	///   narrowed, needs the probe after seeding, or is the execution's first narrowing. Returns the
	///   Or's own activation.
	/// </summary>
	internal static StepActivation Complete(ref StepBinding binding, ref StepBindings bindings, ReadOnlySpan<byte> firstLeaves, int activeMask, bool narrowed, bool needsProbe, bool first) {
		binding.Write(0, (long)Unsafe.AsPointer(ref bindings));
		for (var b = 0; b < firstLeaves.Length; b++)
			FirstLeafSlot(ref binding, b) = firstLeaves[b];
		// Nothing narrowed (every bound branch came out of a nested Or, or none bound): the eager Or
		// leaves the candidates alone, so no branch may probe — the mask is cleared whatever bound.
		binding.Int0 = narrowed ? activeMask : 0;
		binding.Int1 = (narrowed ? FlagNarrowed : 0) | (needsProbe && narrowed ? FlagNeedsProbe : 0) | (first ? FlagFirst : 0);
		if (!narrowed)
			return first ? StepActivation.Active : StepActivation.Inactive;
		return activeMask == 0 ? StepActivation.Empty : StepActivation.Active;
	}

	/// <summary>Chosen after the seed decision: the union is the seed (the default, and always for <c>Count</c> / a classic <c>Sort</c>) rather than the filtered store walk.</summary>
	internal static void SetUnionSeed(ref StepBinding binding, bool union) {
		if (union)
			binding.Int1 |= FlagUnionSeed;
		else
			binding.Int1 &= ~FlagUnionSeed;
	}

	/// <summary>True when the Or must still probe the rows its own seed walked: a branch has more than one active leaf, so the seeded union (first leaves) is a superset.</summary>
	internal static bool ProbeAfterSeed(in StepBinding binding) => (binding.Int1 & FlagNeedsProbe) != 0;

	/// <summary>The active branch mask, for the last-bind record.</summary>
	internal static int ActiveMask(in StepBinding binding) => binding.Int0;

	internal static bool IsFirst(in StepBinding binding) => (binding.Int1 & FlagFirst) != 0;

	// ── Signal / seed ────────────────────────────────────────────────────────────────────────────────

	// The sum of the active branches' first-leaf signals — an upper bound on the union. An Or-first
	// that did not narrow seeds the whole store: the largest signal there is, so a free seed prefers
	// any other active step.
	public int Signal(in StepBinding binding) {
		var mask = binding.Int0;
		if (mask == 0)
			return int.MaxValue;
		var bindings = Bindings(in binding);
		var steps = _steps;
		var total = 0L;
		for (var b = 0; mask != 0; b++, mask >>= 1) {
			if ((mask & 1) == 0)
				continue;
			var leaf = FirstLeaf(in binding, b);
			total += steps[leaf].Signal(in bindings[leaf]);
		}

		return (int)Math.Min(total, int.MaxValue);
	}

	public void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		var flags = binding.Int1;
		if ((flags & FlagFirst) != 0 && binding.Int0 == 0) {
			// An Or-first none of whose branches narrowed: the eager auto-seed, every store row.
			var all = new SeedCollectors<TKey, TValue>.All(ref seed);
			_cache.EnumerateAllValuesInit(ref all, null);
			return;
		}

		if ((flags & FlagFirst) != 0 && (flags & FlagUnionSeed) == 0) {
			// The eager Or-first: every store row in store order, kept to the union. The union is built
			// into the dedupe set first (its keys are discarded from the buffer), then the walk copies
			// the rows the set admits.
			SeedUnion(in binding, ref seed, ref dedupe, collectAll: true);
			seed.Truncate(0);
			var filtered = new SeedCollectors<TKey, TValue>.Filtered(ref seed, ref dedupe);
			_cache.EnumerateAllValuesInit(ref filtered, null);
			return;
		}

		SeedUnion(in binding, ref seed, ref dedupe, collectAll: false);
	}

	// Each active branch's first leaf copied into the seed in branch order, every key admitted once
	// across branches through the dedupe set — the eager UnionWith into one set. A leaf's own
	// multi-bucket dedupe (a list In) is its own set, released here, so the cross-branch set sees each
	// branch's keys exactly once. With collectAll the set receives every key even for a single branch
	// (the filtered store walk needs the whole union).
	private void SeedUnion(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe, bool collectAll) {
		var mask = binding.Int0;
		var bindings = Bindings(in binding);
		var steps = _steps;
		var needSet = collectAll || (mask & (mask - 1)) != 0;
		if (needSet && !dedupe.IsInitlized)
			dedupe = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		for (var b = 0; mask != 0; b++, mask >>= 1) {
			if ((mask & 1) == 0)
				continue;
			var leaf = FirstLeaf(in binding, b);
			var start = seed.Count;
			var own = default(ValueSet<TKey, DefaultKeyComparer<TKey>>);
			try {
				steps[leaf].Seed(in bindings[leaf], ref seed, ref own);
			} finally {
				if (own.IsInitlized)
					own.Dispose();
			}

			if (!needSet)
				continue;
			var keys = seed.MutableKeys;
			var n = start;
			for (var i = start; i < keys.Length; i++)
				if (dedupe.Add(keys[i]))
					keys[n++] = keys[i];
			seed.Truncate(n);
		}
	}

	// ── Probe ────────────────────────────────────────────────────────────────────────────────────────

	public bool ProbeKey(TKey key, in StepBinding binding) => true;

	public bool ProbeValue(TKey key, TValue value, in StepBinding binding) {
		var mask = binding.Int0;
		if (mask == 0)
			return true;
		var bindings = Bindings(in binding);
		var steps = _steps;
		var branches = _branches;
		for (var b = 0; mask != 0; b++, mask >>= 1) {
			if ((mask & 1) == 0)
				continue;
			var branch = branches[b];
			var leaves = branch.Leaves;
			var keySide = branch.KeySide;
			var pass = true;
			for (var i = 0; i < leaves.Length; i++) {
				var s = leaves[i];
				ref readonly var leaf = ref bindings[s];
				if (leaf.Activation != StepActivation.Active)
					continue;
				if (keySide[i] ? !steps[s].ProbeKey(key, in leaf) : !steps[s].ProbeValue(key, value, in leaf)) {
					pass = false;
					break;
				}
			}

			if (pass)
				return true;
		}

		return false;
	}

	public void Release(ref StepBinding binding) { }

	public void ExplainBranches(StringBuilder sb) {
		sb.Append('{');
		for (var b = 0; b < _branches.Length; b++) {
			if (b > 0)
				sb.Append("; ");
			sb.Append("branch ").Append(b + 1).Append(_branches[b].Narrows ? ": " : " (from a nested Or): ");
			PipelineNode<TArgs>.Explain(sb, _branches[b].Nodes);
		}

		sb.Append('}');
	}
}

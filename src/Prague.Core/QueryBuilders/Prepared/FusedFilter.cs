namespace Prague.Core;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
///   The plan's top-level <c>Where</c>s folded into one predicate, with an order that adapts to the
///   data. Built once per frozen query and shared by every execution on every thread.
///   <para>
///   <b>Fusion.</b> The eager core keeps one <c>Predicate&lt;TValue&gt;</c>; a second <c>Where</c>
///   composes it through a <c>&amp;&amp;</c> closure allocated per execution, and every parameterized
///   filter rents its own pooled box. A fused filter is one step: all-constant plans hand the core a
///   delegate cached on this object; plans with an argument filter bind one pooled box per execution
///   (<see cref="ArgPredicatePool{TValue,TArgs}.RentFused" />) whose predicate loops the steps with
///   the execution's arguments. Results are identical to the eager composition — every step is
///   still evaluated left to right with short-circuit on the first rejection.
///   </para>
///   <para>
///   <b>Adaptive order.</b> Predicates are pure, so their order never changes the rows returned; it
///   changes how many predicate calls a rejected row costs. Every <see cref="SampleEvery" />-th
///   execution (the first included) runs sampled: each step's calls and rejections are counted, its
///   first <see cref="MaxTimedCalls" /> sampled calls are timed with <see cref="Stopwatch" /> for a
///   per-call cost, and at the end of that execution the steps are re-ranked by rejection rate per
///   unit of cost — cheap rejectors first — and a changed order is published as a fresh, immutable
///   <see cref="FusedOrdering{TValue,TArgs}" /> (the steps permuted into evaluation order) through
///   <see cref="Volatile.Write{T}(ref T, T)" />. Unsampled executions read the current ordering once
///   (the box path) or once per row (the shared constant delegate) and write nothing, so the steady
///   state has no shared writes and no false sharing. The counters are advisory: plain increments,
///   lost updates between concurrent sampled executions are acceptable.
///   </para>
///   <para>
///   The cost is the timed mean minus the measured overhead of two <see cref="Stopwatch" /> reads,
///   clamped to a floor of about one delegate call: on this hardware a field read and an integer
///   modulo are indistinguishable under the ~10 ns read overhead, while a string operation is not, so
///   cost separates "expensive" from "cheap" and selectivity decides among the cheap. A step is only
///   promoted past another when it has seen <see cref="MinCalls" /> sampled calls and its score beats
///   the other's by <see cref="PromoteMargin" /> — a swap between predicates of similar selectivity
///   saves about <c>N·Δreject</c> calls and can lose as much to branch prediction, so it is not worth
///   taking. The margin also keeps the order from flapping; each change allocates one small object and
///   the order converges after a few samples. Sampled executions short-circuit like unsampled ones, so
///   no predicate is ever evaluated on a row an earlier one rejected in the current order.
///   </para>
///   <para>
///   <b>Exceptions.</b> Because the order may differ from the eager one, a throwing predicate may
///   throw earlier or later than it would under the eager builder, and a predicate that is not total —
///   one that relies on an earlier <c>Where</c> as a guard — may be called on rows it never saw. Such
///   plans should build with <see cref="FrozenOptions.AdaptiveFilterOrdering" /> off.
///   </para>
/// </summary>
internal sealed class FusedFilter<TValue, TArgs> : IPlanExplainable {
	internal const int SampleEvery = 256;
	internal const int MinCalls = 32;
	internal const int MaxTimedCalls = 4096;
	private const double PromoteMargin = 1.5;
	private const int DecayAt = 1 << 24;

	// Two back-to-back timestamps, averaged once per process; subtracted from every timed call.
	private static readonly double TimerOverheadTicks = MeasureTimerOverhead();

	// About one delegate call — the least a predicate can cost, and the resolution floor below which
	// two cheap predicates read as equal.
	private static readonly double CostFloorTicks = Math.Max(Stopwatch.Frequency * 2e-9, 1e-3);

	private readonly FilterStep<TValue, TArgs>[] _steps;
	private readonly bool _adaptive;
	private readonly int[] _calls;
	private readonly int[] _rejects;
	private readonly int[] _timed;
	private readonly long[] _ticks;
	private readonly double[] _scores;
	private FusedOrdering<TValue, TArgs> _ordering;
	private int _executions;
	private int _reordering;
	private int _reorders;
	private int _samples;

	// Constant-only plans: the core takes one of these directly, no box rented per execution.
	internal readonly Predicate<TValue>? ConstPredicate;
	internal readonly Predicate<TValue>? ConstSampledPredicate;

	// Every step's Passes takes `in TArgs`; the constant sampled delegate passes this so the loop is one body.
	private static readonly TArgs NoArgs = default!;

	internal FusedFilter(FilterStep<TValue, TArgs>[] steps, bool hasArgs, bool adaptive) {
		_steps = steps;
		HasArgs = hasArgs;
		_adaptive = adaptive;
		_calls = new int[steps.Length];
		_rejects = new int[steps.Length];
		_timed = new int[steps.Length];
		_ticks = new long[steps.Length];
		_scores = new double[steps.Length];
		var order = new int[steps.Length];
		for (var i = 0; i < order.Length; i++)
			order[i] = i;
		_ordering = new FusedOrdering<TValue, TArgs>(steps, order, !hasArgs);
		if (!hasArgs) {
			ConstPredicate = InvokeConst;
			ConstSampledPredicate = InvokeConstSampled;
		}
	}

	private static double MeasureTimerOverhead() {
		long ticks = 0;
		for (var i = 0; i < 4096; i++) {
			var start = Stopwatch.GetTimestamp();
			ticks += Stopwatch.GetTimestamp() - start;
		}

		return ticks / 4096.0;
	}

	internal bool HasArgs { get; }

	internal int Count => _steps.Length;

	/// <summary>The published ordering; read once per execution by the box path.</summary>
	internal FusedOrdering<TValue, TArgs> Ordering => Volatile.Read(ref _ordering);

	/// <summary>Called once per execution; true when this execution should sample. Plain counter — advisory.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool BeginExecution() => _adaptive && (_executions++ & (SampleEvery - 1)) == 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool Passes(TValue value, in TArgs args, FusedOrdering<TValue, TArgs> ordering) {
		var steps = ordering.Steps;
		for (var i = 0; i < steps.Length; i++)
			if (!steps[i].Passes(value, in args))
				return false;
		return true;
	}

	// Timing is the expensive part of a sample (two timestamps per call, ~10 ns against predicates
	// that cost 1–5 ns), so each step is timed for its first MaxTimedCalls sampled calls only — enough
	// for a stable per-call mean — and merely counted afterwards. Decay re-opens the timing window.
	internal bool PassesSampled(TValue value, in TArgs args, FusedOrdering<TValue, TArgs> ordering) {
		var steps = ordering.Steps;
		var order = ordering.Order;
		for (var i = 0; i < steps.Length; i++) {
			var s = order[i];
			bool pass;
			if (_timed[s] < MaxTimedCalls) {
				var start = Stopwatch.GetTimestamp();
				pass = steps[i].Passes(value, in args);
				_ticks[s] += Stopwatch.GetTimestamp() - start;
				_timed[s]++;
			} else {
				pass = steps[i].Passes(value, in args);
			}

			_calls[s]++;
			if (pass)
				continue;
			_rejects[s]++;
			return false;
		}

		return true;
	}

	// The constant-only loop: the permuted Predicate<TValue>s called directly, no step branch, no args.
	private bool InvokeConst(TValue value) {
		var constants = Volatile.Read(ref _ordering).Constants!;
		for (var i = 0; i < constants.Length; i++)
			if (!constants[i](value))
				return false;
		return true;
	}

	private bool InvokeConstSampled(TValue value) => PassesSampled(value, in NoArgs, Volatile.Read(ref _ordering));

	/// <summary>Closes a sampled execution: one thread at a time re-ranks the steps from the accumulated counters.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void EndSampled() {
		if (Interlocked.CompareExchange(ref _reordering, 1, 0) != 0)
			return;
		try {
			_samples++;
			Reorder();
		} finally {
			Volatile.Write(ref _reordering, 0);
		}
	}

	// Stable insertion sort of the current order by score, descending. A step moves ahead of the one
	// before it only when both have enough samples and it wins by the margin; a step without enough
	// samples is a barrier nothing crosses (it saw few rows because the steps before it reject well,
	// which is the order working). Scores are rejection rate per tick of cost — the eager order's
	// left-to-right conditional rates, which is what the next execution will see under this order.
	private void Reorder() {
		var current = _ordering.Order;
		var n = current.Length;
		var scores = _scores;
		var decay = false;
		for (var i = 0; i < n; i++) {
			var calls = _calls[i];
			if (calls >= DecayAt)
				decay = true;
			if (calls < MinCalls) {
				scores[i] = -1;
				continue;
			}

			var timed = _timed[i];
			var cost = timed == 0 ? CostFloorTicks : Math.Max((double)_ticks[i] / timed - TimerOverheadTicks, CostFloorTicks);
			scores[i] = (double)_rejects[i] / calls / cost;
		}

		if (decay)
			for (var i = 0; i < n; i++) {
				_calls[i] >>= 1;
				_rejects[i] >>= 1;
				_timed[i] >>= 1;
				_ticks[i] >>= 1;
			}

		Span<int> next = stackalloc int[Math.Min(n, 64)];
		if (n > 64)
			return;
		current.AsSpan().CopyTo(next);
		var changed = false;
		for (var p = 1; p < n; p++) {
			var e = next[p];
			if (scores[e] < 0)
				continue;
			var q = p - 1;
			while (q >= 0 && scores[next[q]] >= 0 && scores[e] > scores[next[q]] * PromoteMargin) {
				next[q + 1] = next[q];
				q--;
			}

			if (q + 1 == p)
				continue;
			next[q + 1] = e;
			changed = true;
		}

		if (!changed)
			return;
		_reorders++;
		Volatile.Write(ref _ordering, new FusedOrdering<TValue, TArgs>(_steps, next.ToArray(), !HasArgs));
	}

	internal int ReordersForTests => _reorders;

	internal int SampledCallsForTests(int step) => _calls[step];

	public void Explain(StringBuilder sb) {
		var order = Volatile.Read(ref _ordering).Order;
		sb.Append("fused filters: ").Append(_steps.Length).Append(", adaptive: ").Append(_adaptive ? "on" : "off").Append(", order: [");
		for (var i = 0; i < order.Length; i++) {
			if (i > 0)
				sb.Append(", ");
			sb.Append(order[i]);
		}

		sb.Append("], samples: ").Append(_samples).Append(", reorders: ").Append(_reorders).AppendLine();
	}
}

/// <summary>
///   One immutable evaluation order of a <see cref="FusedFilter{TValue,TArgs}" />: the step indices in
///   evaluation order (for the sample counters and <c>Explain</c>) and the steps themselves permuted
///   into that order, so the per-row loop indexes one array. Constant-only filters also carry the bare
///   predicates for the tightest loop. A new instance is published per order change; an execution
///   that captured the previous one keeps evaluating a complete, valid permutation.
/// </summary>
internal sealed class FusedOrdering<TValue, TArgs> {
	internal readonly int[] Order;
	internal readonly FilterStep<TValue, TArgs>[] Steps;
	internal readonly Predicate<TValue>[]? Constants;

	internal FusedOrdering(FilterStep<TValue, TArgs>[] steps, int[] order, bool allConstant) {
		Order = order;
		Steps = new FilterStep<TValue, TArgs>[order.Length];
		for (var i = 0; i < order.Length; i++)
			Steps[i] = steps[order[i]];
		if (!allConstant)
			return;
		Constants = new Predicate<TValue>[order.Length];
		for (var i = 0; i < order.Length; i++)
			Constants[i] = Steps[i].Constant!;
	}
}

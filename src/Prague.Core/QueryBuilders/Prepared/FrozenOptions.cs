namespace Prague.Core;

/// <summary>
///   What <c>BuildFrozen()</c> may do beyond binding the fastest executor for the plan shape. The
///   defaults are the result-preserving optimizations (same rows, same order as the eager builder);
///   the ones that change encounter order are opt-in. Read once at build; nothing on the execute
///   path consults the options object.
/// </summary>
public sealed class FrozenOptions {
	public static FrozenOptions Default { get; } = new();

	/// <summary>
	///   Fold the plan's top-level <c>Where</c>s (two or more) into one fused predicate applied to the
	///   eager core in a single step: no per-execution <c>&amp;&amp;</c> closure, one pooled box per
	///   execution for parameterized filters instead of one per filter. Results are identical.
	/// </summary>
	public bool FuseFilters { get; init; } = true;

	/// <summary>
	///   Let a fused filter learn which predicate to evaluate first. Predicates are pure, so the order
	///   only changes which ones a rejected row is shown — never the rows returned — but a predicate
	///   that is not total (one that relies on an earlier <c>Where</c> to guard it, e.g. a null check)
	///   may now be called on rows it never saw under the eager order, and a throwing predicate may
	///   throw earlier or later. Turn off when a filter depends on another one having run.
	/// </summary>
	public bool AdaptiveFilterOrdering { get; init; } = true;

	/// <summary>
	///   Remember how large the candidate set grew on previous executions and pre-size the next one,
	///   so a large index bucket does not rehash its way up from the inline storage. Bounded and
	///   self-shrinking; results are unaffected.
	/// </summary>
	public bool CapacityHints { get; init; } = true;

	/// <summary>
	///   Opt-in, changes encounter order. Let an unsorted <c>Execute*</c> of a pipeline plan seed from
	///   the index step with the smallest cardinality signal for the execution's arguments (a unique
	///   step's 0 / 1, a list bucket's live count, a key-set's count, a B+tree estimate for a range or
	///   last-updated window — the estimate wins only when twice it is still smaller than the best exact
	///   count), probing the other steps on each candidate. The rows returned are the same set with the
	///   same <c>Count</c>; their order follows the seeding source instead of the first-declared step,
	///   so a page of an unsorted query may differ from the eager builder's. <c>Count</c> and a classic
	///   <c>Sort</c> (whose rows are fully sorted afterwards) seed this way regardless.
	/// </summary>
	public bool ReorderIndexNarrowers { get; init; }

	/// <summary>
	///   Bind a simple plan — unsorted, under <c>Sort</c> or under <c>SortBounded</c>; its index steps
	///   unique / list equality and membership, range, key-set, last-updated, and the composites
	///   <c>Or</c> / <c>If</c> / <c>IfElse</c> / <c>Match</c> over them — and a joined plan of the same
	///   steps whose joins are fusable <c>JoinOne</c>s (or a <c>SortBounded</c> followed by outer
	///   <c>JoinOne</c>s), to the pipeline executor: one index step's keys are copied out once and every
	///   other step is an O(1) probe on the key or the fetched value — no candidate set, no intersection,
	///   one store lookup per candidate. An <c>If</c> / <c>Match</c> arm is chosen once per execution at
	///   bind and its steps take part like top-level ones; an <c>Or</c> probes as the OR of its branches'
	///   ANDs. Same rows in the same order as the eager builder for unsorted plans (the first declared
	///   step seeds; a smaller equality step is walked instead and its survivors put back in the first
	///   step's order when that is cheaper) — except an <c>Or</c> that is the first narrowing, whose
	///   union order is the default (<see cref="OrSeed" />). Off → the replay.
	/// </summary>
	public bool Pipeline { get; init; } = true;

	/// <summary>
	///   Opt-in, changes encounter order. Let an <b>inner</b> <c>JoinOne</c> driven by a symmetric list
	///   index on the left (<c>JoinOneLeftSymResolver</c>) fuse into the pipeline pass. The eager resolver
	///   creates that join's rows through its fan-out — one pair per right key, every left of the bucket
	///   emitted together — so an unsorted result comes out grouped by right; the fused pass looks the
	///   right up per left and keeps the seed's order instead. Same rows, same <c>Count</c>, different
	///   sequence, so a page of an unsorted query may differ from the eager builder's. Off (the default) a
	///   chain containing such a join replays and stays byte-identical. Outer left-symmetric joins are
	///   unaffected either way — their rows already exist and the fan-out only fills them.
	/// </summary>
	public bool FuseSymmetricInnerJoins { get; init; }

	/// <summary>
	///   On by default; the one default that changes encounter order. An <c>Or</c> that is the query's
	///   first narrowing seeds the pipeline from the <b>union of its branches</b> — branch 1's keys in
	///   that branch's index order, then branch 2's new keys, and so on. The eager <c>OrWith</c>
	///   auto-seeds an Or-first from every row under the store's locks and returns the union in
	///   <i>store</i> order — an order the store's own resizes change and no caller can rely on — at the
	///   cost of a full store walk (two 1k buckets in a 100k store: ~1 ms eager, ~27 µs here). Same rows,
	///   same <c>Count</c>, the union's order. Set <c>false</c> to reproduce the eager sequence byte for
	///   byte: the store walk in store order, kept to the union built once per execution (one hash probe
	///   per store row instead of a set insert; ~1.4× eager). Only an <c>Or</c> that is the first
	///   narrowing is affected — an <c>Or</c> after another step is a probe and keeps that step's order
	///   either way. <c>Count</c> and a classic <c>Sort</c> seed from the union regardless, as their rows
	///   carry no encounter order.
	/// </summary>
	public bool OrSeed { get; init; } = true;

	/// <summary>
	///   Make the pipeline's probes read the index instead of the fetched value: a list step probes its
	///   bucket, a key-set step its key set. That is the eager step's own read and reproduces the eager
	///   staleness window exactly (a row whose store write has landed but whose index write has not yet
	///   is judged by the index); the default value-side probes judge the row by the value they return.
	///   Both are inside the documented contract. A plan with a range step replays under this option
	///   (its key-side twin is a window walk).
	/// </summary>
	public bool IndexSideProbes { get; init; }
}

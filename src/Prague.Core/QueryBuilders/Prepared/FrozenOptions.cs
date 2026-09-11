namespace Prague.Core;

/// <summary>
///   What <c>BuildFrozen()</c> may do beyond binding the fastest executor for the plan shape. Read once
///   at build; nothing on the execute path consults the options object.
///   <para>
///     The ordering contract: a frozen result holds the <b>same rows</b> as the eager builder's, with the
///     same <c>Count</c> / <c>TotalCount</c> / <c>Truncated</c>, the same clone timing, pooling and leak
///     safety, and pages that are slices of the same whole. An <b>unsorted</b> result carries <b>no
///     row-order guarantee</b>, and <b>ties</b> inside a sorted one are unspecified as well — the comparer
///     does not order them. A <c>Sort</c> / <c>SortBounded</c> with a <b>total</b> comparer is byte-identical
///     to eager. Set <see cref="PreserveEagerOrder" /> to give up the freedom and get eager's sequence back.
///   </para>
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
	///   Bind a simple plan — unsorted, under <c>Sort</c> or under <c>SortBounded</c>; its index steps
	///   unique / list equality and membership, range, key-set, last-updated, and the composites
	///   <c>Or</c> / <c>If</c> / <c>IfElse</c> / <c>Match</c> over them — and a joined plan of the same
	///   steps whose joins are fusable <c>JoinOne</c>s and / or <c>JoinMany</c>s (or a <c>SortBounded</c>
	///   followed by outer <c>JoinOne</c>s), to the pipeline executor: one index step's keys are copied out
	///   once and every other step is an O(1) probe on the key or the fetched value — no candidate set, no
	///   intersection, one store lookup per candidate. An <c>If</c> / <c>Match</c> arm is chosen once per
	///   execution at bind and its steps take part like top-level ones; an <c>Or</c> probes as the OR of its
	///   branches' ANDs. A <c>JoinMany</c> is never fused past its fan-out: its own two-pass fan-out runs
	///   after the pass over the rows the pass formed, so only the narrowing is pipelined (an inner one
	///   drops the lefts without a right there). Off → the replay.
	/// </summary>
	public bool Pipeline { get; init; } = true;

	/// <summary>
	///   Make the pipeline's probes read the index instead of the fetched value: a list step probes its
	///   bucket, a key-set step its key set. That is the eager step's own read and reproduces the eager
	///   staleness window exactly (a row whose store write has landed but whose index write has not yet
	///   is judged by the index); the default value-side probes judge the row by the value they return.
	///   Both are inside the documented contract. A plan with a range step replays under this option
	///   (its key-side twin is a window walk).
	/// </summary>
	public bool IndexSideProbes { get; init; }

	/// <summary>
	///   Opt out of the ordering freedom and reproduce the eager builder's row sequence byte for byte,
	///   at the cost of the plans it forbids. Three things change when it is set:
	///   <list type="bullet">
	///     <item>
	///       <b>The seed is the first declared index step</b>, not the smallest one. By default an
	///       <c>Execute*</c> seeds from the step with the smallest cardinality signal for the execution's
	///       arguments (a unique step's 0 / 1, a list bucket's live count, a key-set's count, a B+tree
	///       estimate for a range or last-updated window — the estimate wins only when twice it is still
	///       smaller than the best exact count) and probes the rest per candidate, so the rows come out in
	///       the seeding step's order and the walk is as short as the narrowest step allows.
	///     </item>
	///     <item>
	///       <b>An <c>Or</c> that is the query's first narrowing walks the store</b>, as the eager
	///       <c>OrWith</c> auto-seed does, instead of seeding from the union of its branches. The eager
	///       sequence is the store's hash enumeration order — an order the store's own resizes change and
	///       no caller can rely on — and reproducing it costs a full store walk (two 1k buckets in a 100k
	///       store: ~1 ms, against ~27 µs for the union).
	///     </item>
	///     <item>
	///       <b>An inner <c>JoinOne</c> driven by a symmetric list index on the left does not fuse</b>
	///       (<c>JoinOneLeftSymResolver</c>), so a chain containing one replays. The eager resolver creates
	///       that join's rows through its fan-out — one pair per right key, every left of the bucket emitted
	///       together — so an unsorted result comes out grouped by right; the fused pass looks the right up
	///       per left and keeps the seed's order instead. Outer left-symmetric joins are unaffected either
	///       way — their rows already exist and the fan-out only fills them.
	///     </item>
	///   </list>
	///   <c>Count</c> is unaffected — its rows carry no order — and neither is a sorted result whose
	///   comparer is total.
	/// </summary>
	public bool PreserveEagerOrder { get; init; }
}

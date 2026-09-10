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
	///   Bind a simple plan whose index steps are all non-composite (unique / list equality and
	///   membership, range, key-set, last-updated), unsorted or under a classic <c>Sort</c>, to the
	///   pipeline executor: one index step's keys are copied out once and every other step is an O(1)
	///   probe on the key or the fetched value — no candidate set, no intersection, one store lookup
	///   per candidate. Same rows in the same order as the eager builder for unsorted plans (the first
	///   declared step seeds; a smaller equality step is walked instead and its survivors put back in
	///   the first step's order when that is cheaper). Off → the replay.
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
}

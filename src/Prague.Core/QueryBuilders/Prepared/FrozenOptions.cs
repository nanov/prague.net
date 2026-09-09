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
	///   For a plan of two or more equality index steps (unique, list; not key-set), intersect the
	///   steps after the seed by walking whichever side is smaller: when a remaining step's bucket is
	///   smaller than the seeded candidate set, mark the candidates that bucket contains on a bitmap,
	///   prune the marks with the other steps and compact the set once; otherwise walk the candidate
	///   set per step as the eager builder does. Bucket sizes are read per execution. Order
	///   preserving: both ways remove by slot, so survivors keep the seed's encounter order.
	/// </summary>
	public bool AdaptiveIntersection { get; init; } = true;

	/// <summary>
	///   Opt-in, changes encounter order. For a plan whose index steps are all equality lookups
	///   (unique, list, key-set), read each step's live bucket size at execute time and seed the
	///   candidates from the smallest one, intersecting the others into it. The rows returned are the
	///   same set with the same <c>Count</c>; their order follows the seeding bucket instead of the
	///   first-declared one, so a page of an unsorted query may differ from the eager builder's.
	/// </summary>
	public bool ReorderIndexNarrowers { get; init; }

	/// <summary>
	///   Bind a simple, unsorted plan whose index steps are all non-composite (unique / list equality
	///   and membership, range, key-set, last-updated) to the pipeline executor: the first index step's
	///   keys are copied out once and every later step is an O(1) probe on the key or the fetched value
	///   — no candidate set, no intersection, one store lookup per candidate. Same rows in the same
	///   order as the eager builder. Off → the stage-2 executor selection.
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

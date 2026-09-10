namespace Prague.Core;

/// <summary>
///   Builds a pipeline plan's step array and shape from the flattened descriptors (design §5, §9).
///   Every non-composite narrower becomes one leaf step through <see cref="IPipelineStepSource{TKey,TValue,TArgs}" />
///   wherever it sits — top level or inside an arm or branch — so an execution binds only the steps its
///   taken arms reach. Composites become nodes: an <c>If</c> / <c>IfElse</c> / <c>Match</c> a
///   <see cref="SelectNode{TArgs}" /> (a <c>Where</c> inside its arm a branch filter), an <c>Or</c> an
///   <see cref="OrStep{TKey,TValue,TArgs}" /> holding its branches. Rejected (the plan replays): more
///   than <see cref="PipelineLimits.MaxSteps" /> steps or branch filters, an <c>Or</c> with more than
///   <see cref="PipelineLimits.MaxOrBranches" /> branches after flattening, a narrower that cannot build
///   its step under the options, an <c>Or</c> nested inside an <c>Or</c> branch that is not that
///   branch's only narrower — the eager nested <c>OrWith</c> unions its bits into the enclosing branch
///   without marking it narrowed, which flattens exactly only for a branch that holds nothing else —
///   and a range / key-set / last-updated step anywhere but first inside an <c>Or</c> branch: the eager
///   branch core <i>marks</i> those (unions them into the branch's bitmap, and drops them when the
///   branch was already cleared) where it intersects a unique / list step, a set-level rule a per-row
///   probe cannot reproduce. Runs once per build.
/// </summary>
internal sealed class PipelinePlanner<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly FrozenOptions _options;
	private readonly List<IPipelineStep<TKey, TValue, TArgs>> _steps = [];
	private readonly List<FilterStep<TValue, TArgs>> _branchFilters = [];
	private int _selects;
	private bool _composites;

	private PipelinePlanner(InMemoryDataCache<TKey, TValue> cache, FrozenOptions options) {
		_cache = cache;
		_options = options;
	}

	/// <summary>
	///   Pipeline eligibility: at least one and at most <see cref="PipelineLimits.MaxSteps" /> steps (an
	///   <c>Or</c> counts as one plus its leaves), every one buildable under the options, plus any number
	///   of top-level filters (applied by the executor directly) and up to <see cref="PipelineLimits.MaxSteps" />
	///   branch filters. A filter-only plan has no seed source and replays. <paramref name="tree" /> is
	///   <c>null</c> for a plan without composites, which binds its steps flat.
	/// </summary>
	internal static bool TryPlan(InMemoryDataCache<TKey, TValue> cache, IReadOnlyList<NarrowerDescriptor> narrowers, FrozenOptions options,
		out IPipelineStep<TKey, TValue, TArgs>[] steps, out PipelineTree<TArgs>? tree, out FilterStep<TValue, TArgs>[] branchFilters) {
		var planner = new PipelinePlanner<TKey, TValue, TArgs>(cache, options);
		steps = [];
		tree = null;
		branchFilters = [];
		if (!planner.TryNodes(narrowers, top: true, inOr: false, leftmost: false, out var top) || planner._steps.Count is 0 or > PipelineLimits.MaxSteps
		    || planner._branchFilters.Count > PipelineLimits.MaxSteps)
			return false;
		steps = planner._steps.ToArray();
		for (var i = 0; i < steps.Length; i++)
			if (steps[i] is OrStep<TKey, TValue, TArgs> or)
				or.Attach(steps);
		if (planner._composites)
			tree = new PipelineTree<TArgs>(top, planner._selects);
		branchFilters = planner._branchFilters.ToArray();
		return true;
	}

	// One descriptor list into nodes. Top-level filters are the executor's own (FilterSteps), not
	// nodes; a filter inside an arm is a branch filter; a filter inside an Or branch cannot occur
	// (PreparedNarrowOnly admits none). Inside an Or branch `leftmost` tracks whether a node is the
	// branch's first (through nested If / Match arms), the one position a marking step may take.
	private bool TryNodes(IReadOnlyList<NarrowerDescriptor> narrowers, bool top, bool inOr, bool leftmost, out PipelineNode<TArgs>[] nodes) {
		var list = new List<PipelineNode<TArgs>>(narrowers.Count);
		nodes = [];
		for (var i = 0; i < narrowers.Count; i++) {
			var d = narrowers[i];
			var first = leftmost && i == 0;
			switch (d.Kind) {
				case NarrowerKind.Filter:
				case NarrowerKind.FilterArg:
					if (top)
						continue;
					if (inOr || _branchFilters.Count >= PipelineLimits.MaxSteps)
						return false;
					list.Add(new FilterNode<TArgs>((byte)_branchFilters.Count));
					_branchFilters.Add(d.Kind == NarrowerKind.Filter
						? new FilterStep<TValue, TArgs>((Predicate<TValue>)d.Filter!)
						: new FilterStep<TValue, TArgs>((Func<TValue, TArgs, bool>)d.Filter!));
					break;
				case NarrowerKind.If:
				case NarrowerKind.IfElse:
				case NarrowerKind.Match:
					if (!TrySelect(d, inOr, first, out var select))
						return false;
					list.Add(select);
					break;
				case NarrowerKind.Or:
					if (inOr || !TryOr(d, out var or))
						return false;
					list.Add(or);
					break;
				default:
					if (inOr && !first && Marks(d.Kind))
						return false;
					if (!TryLeaf(d, out var leaf))
						return false;
					list.Add(leaf);
					break;
			}
		}

		nodes = list.ToArray();
		return true;
	}

	// The kinds the eager branch core always marks (design §4 / CacheQueryBuilder's intersecter paths).
	private static bool Marks(NarrowerKind kind)
		=> kind is NarrowerKind.Range or NarrowerKind.KeySet or NarrowerKind.LastUpdatedAfter or NarrowerKind.LastUpdatedBetween;

	private bool TryLeaf(NarrowerDescriptor d, out PipelineNode<TArgs> node) {
		node = null!;
		if (_steps.Count >= PipelineLimits.MaxSteps || d.Source is not IPipelineStepSource<TKey, TValue, TArgs> source || source.CreatePipelineStep(_options) is not { } step)
			return false;
		node = new LeafNode<TArgs>((byte)_steps.Count, d.Kind);
		_steps.Add(step);
		return true;
	}

	private bool TrySelect(NarrowerDescriptor d, bool inOr, bool leftmost, out PipelineNode<TArgs> node) {
		node = null!;
		_composites = true;
		IBranchSelector<TArgs> selector;
		string[] labels;
		if (d.Kind == NarrowerKind.Match) {
			if (d.Source is not IBranchSelectorSource<TArgs> source || d.Value is not MatchArmTags tags)
				return false;
			selector = source.CreateSelector(tags);
			labels = new string[d.Children.Count];
			for (var a = 0; a < labels.Length; a++)
				labels[a] = a < tags.Tags.Count ? "case " + tags.Tags[a] : "default";
		} else {
			if (d.Selector is not Func<TArgs, bool> condition)
				return false;
			selector = new IfSelector<TArgs>(condition, d.Kind == NarrowerKind.IfElse);
			labels = d.Kind == NarrowerKind.IfElse ? ["then", "else"] : ["then"];
		}

		var arms = new PipelineNode<TArgs>[d.Children.Count][];
		for (var a = 0; a < arms.Length; a++)
			if (!TryNodes(d.Children[a], top: false, inOr, leftmost, out arms[a]))
				return false;
		node = new SelectNode<TArgs>(_selects++, d.Kind, selector, arms, labels);
		return true;
	}

	// An Or: each child list a branch, except a child that is exactly one nested Or, whose own branches
	// are flattened in as branches that do not count as narrowing (see the class doc). The step is
	// added before its leaves so the seed rule's "first active step" is the Or itself.
	private bool TryOr(NarrowerDescriptor d, out PipelineNode<TArgs> node) {
		node = null!;
		_composites = true;
		if (_steps.Count >= PipelineLimits.MaxSteps)
			return false;
		var index = (byte)_steps.Count;
		_steps.Add(null!);
		var branches = new List<OrStep<TKey, TValue, TArgs>.Branch>();
		if (!TryBranches(d, branches, narrows: true) || branches.Count > PipelineLimits.MaxOrBranches)
			return false;
		var or = new OrStep<TKey, TValue, TArgs>(_cache, branches.ToArray());
		_steps[index] = or;
		node = new OrNode<TArgs>(index, or);
		return true;
	}

	private bool TryBranches(NarrowerDescriptor or, List<OrStep<TKey, TValue, TArgs>.Branch> branches, bool narrows) {
		for (var b = 0; b < or.Children.Count; b++) {
			var child = or.Children[b];
			if (child.Count == 1 && child[0].Kind == NarrowerKind.Or) {
				if (!TryBranches(child[0], branches, narrows: false))
					return false;
				continue;
			}

			if (!TryNodes(child, top: false, inOr: true, leftmost: true, out var nodes))
				return false;
			var leaves = new List<byte>();
			CollectLeaves(nodes, leaves);
			branches.Add(new OrStep<TKey, TValue, TArgs>.Branch(nodes, leaves.ToArray(), narrows));
		}

		return true;
	}

	private static void CollectLeaves(PipelineNode<TArgs>[] nodes, List<byte> leaves) {
		for (var i = 0; i < nodes.Length; i++) {
			switch (nodes[i]) {
				case LeafNode<TArgs> leaf:
					leaves.Add(leaf.Step);
					break;
				case SelectNode<TArgs> select:
					for (var a = 0; a < select.Arms.Length; a++)
						CollectLeaves(select.Arms[a], leaves);
					break;
			}
		}
	}
}

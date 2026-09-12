namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;

/// <summary>The kind of one <see cref="PipelineNode{TArgs}" />, a byte the binder switches on.</summary>
internal enum PipelineNodeKind : byte {
	/// <summary>A non-composite index step: its index into the plan's step array.</summary>
	Leaf,

	/// <summary>A <c>Where</c> inside an <c>If</c> / <c>Match</c> arm: its index into the plan's branch filters, active only when its arm is.</summary>
	Filter,

	/// <summary>A <c>Match</c> — and so an <c>If</c> / <c>IfElse</c>, which is one: one arm is chosen per execution at bind and walked in place of the node.</summary>
	Select,

	/// <summary>An <c>Or</c>: the step (an <see cref="OrStep{TKey,TValue,TArgs}" />) carries its branches and binds them itself.</summary>
	Or,
}

/// <summary>
///   One node of a pipeline plan's shape (design §5): what the binder walks per execution to decide
///   which steps and branch filters are active. Built once; a plan without composites has no tree and
///   binds its steps flat. The leaf steps of every node — taken arms or not — live in the plan's one
///   step array, so an untaken arm's steps simply stay unbound and cost nothing in the pass.
/// </summary>
internal abstract class PipelineNode<TArgs>
	where TArgs : struct {
	// A plain field, not a virtual property: BindNodes / BindBranch switch on this once per node per
	// execution, and every concrete node type is sealed — a field read beats a virtual dispatch through
	// the (necessarily unsealed, since every node kind shares it) base-typed array element with no
	// downside, since the kind never changes after construction.
	internal readonly PipelineNodeKind NodeKind;

	private protected PipelineNode(PipelineNodeKind kind) => NodeKind = kind;

	internal abstract void Explain(StringBuilder sb);

	internal static void Explain(StringBuilder sb, PipelineNode<TArgs>[] nodes) {
		sb.Append('[');
		for (var i = 0; i < nodes.Length; i++) {
			if (i > 0)
				sb.Append(", ");
			nodes[i].Explain(sb);
		}

		sb.Append(']');
	}
}

internal sealed class LeafNode<TArgs> : PipelineNode<TArgs>
	where TArgs : struct {
	internal readonly byte Step;
	private readonly NarrowerKind _kind;

	internal LeafNode(byte step, NarrowerKind kind) : base(PipelineNodeKind.Leaf) {
		Step = step;
		_kind = kind;
	}

	internal override void Explain(StringBuilder sb) => sb.Append("step ").Append(Step).Append(' ').Append(_kind);
}

internal sealed class FilterNode<TArgs> : PipelineNode<TArgs>
	where TArgs : struct {
	internal readonly byte Filter;

	internal FilterNode(byte filter) : base(PipelineNodeKind.Filter) => Filter = filter;

	internal override void Explain(StringBuilder sb) => sb.Append("branch filter ").Append(Filter);
}

/// <summary>Picks the arm of a <c>Match</c> for one execution's arguments. Always an arm index, never "none": every arm chain is closed by a <c>Default</c>, which is the last arm.</summary>
internal interface IBranchSelector<TArgs>
	where TArgs : struct {
	int Select(in TArgs args);
}

/// <summary>
///   The guard-form <c>Match</c> dispatch: the first arm whose predicate holds — declaration order,
///   later guards never called, the replay's own rule — else the default arm, which is always the last
///   and always there.
/// </summary>
internal sealed class GuardSelector<TArgs>(Func<TArgs, bool>[] guards) : IBranchSelector<TArgs>
	where TArgs : struct {
	public int Select(in TArgs args) {
		for (var i = 0; i < guards.Length; i++)
			if (guards[i](args))
				return i;
		return guards.Length;
	}
}

/// <summary>
///   The one-guard case of the above — which is every <c>If</c> and <c>IfElse</c>, both being a
///   <c>Match</c> with a single <c>Case</c> — as the ternary it is: no array, no bounds check, no loop.
///   <see cref="SelectNode{TArgs}" /> holds it in a typed field, so the call is direct and inlinable;
///   this is the shape the retired <c>IfSelector</c> had, kept because one <c>Case</c> is the common
///   guard <c>Match</c> by far and the node can devirtualize to exactly one sealed type.
///   <para>
///   Kept on structure, not on a measured win: on the <c>IfTaken</c> shape (a bind plus a one-row walk)
///   the loop form was indistinguishable from this one. That row is **bimodal** on this machine —
///   ~103 ns or ~124 ns depending on the process, with the tight 0.4 ns error bars of a stable state in
///   both modes — and an alternating three-round A/B found both modes on the base commit, on the guard
///   form alone, and on the sugar (base 103.3 / 124.7 / 125.3, sugar 123.6 / 103.1 / 102.8). Anyone
///   chasing a ~20 ns delta on a composite pipeline plan should sample it three times per side before
///   believing it.
///   </para>
/// </summary>
internal sealed class SingleGuardSelector<TArgs>(Func<TArgs, bool> guard) : IBranchSelector<TArgs>
	where TArgs : struct {
	public int Select(in TArgs args) => guard(args) ? 0 : 1;
}

/// <summary>
///   The tag-form <c>Match</c> dispatch: the first arm whose tag equals the selected one under
///   <see cref="EqualityComparer{T}.Default" /> (the replay's rule — a tag declared twice resolves to its
///   first arm), else the default arm, which is always the last and always there.
/// </summary>
internal sealed class MatchSelector<TArgs, TTag>(Func<TArgs, TTag> selector, TTag[] tags) : IBranchSelector<TArgs>
	where TArgs : struct {
	public int Select(in TArgs args) {
		var tag = selector(args);
		for (var i = 0; i < tags.Length; i++)
			if (EqualityComparer<TTag>.Default.Equals(tags[i], tag))
				return i;
		return tags.Length;
	}
}

/// <summary>
///   A narrower that can build the typed <see cref="IBranchSelector{TArgs}" /> for its descriptor: the
///   two <c>Match</c> narrowers, which alone know their <c>TTag</c> / their guards' argument type. The
///   argument is the descriptor's own <see cref="NarrowerDescriptor.Value" /> — a
///   <see cref="MatchArmTags" /> for the tag form, a <see cref="MatchArmGuards" /> for the guard one —
///   which each implementation casts back to what it wrote there.
/// </summary>
internal interface IBranchSelectorSource<TArgs>
	where TArgs : struct {
	IBranchSelector<TArgs> CreateSelector(object arms);
}

/// <summary>A <c>Match</c> node: the selector and one node list per arm, the <c>Default</c> last.</summary>
internal sealed class SelectNode<TArgs> : PipelineNode<TArgs>
	where TArgs : struct {
	/// <summary>The node's index among the plan's select nodes, for the last-bind record.</summary>
	internal readonly int Id;
	internal readonly IBranchSelector<TArgs> Selector;
	// Resolved once at build, not per execution: a one-guard Match — every If and IfElse — selects
	// through SingleGuardSelector<TArgs>, a sealed class closed over TArgs alone, so a call through this
	// field is a direct (non-virtual) call the JIT inlines, its body being one ternary. Everything else
	// (a multi-guard Match, and the tag form, whose MatchSelector<TArgs,TTag> is generic over a TTag
	// this node never names) goes through the interface: there is no sealed reference to devirtualize
	// to, and those plans do more per execution than the dispatch costs.
	private readonly SingleGuardSelector<TArgs>? _singleGuard;
	internal readonly PipelineNode<TArgs>[][] Arms;
	internal readonly string[] Labels;

	internal SelectNode(int id, IBranchSelector<TArgs> selector, PipelineNode<TArgs>[][] arms, string[] labels) : base(PipelineNodeKind.Select) {
		Id = id;
		Selector = selector;
		_singleGuard = selector as SingleGuardSelector<TArgs>;
		Arms = arms;
		Labels = labels;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal int Select(in TArgs args) => _singleGuard is not null ? _singleGuard.Select(in args) : Selector.Select(in args);

	internal override void Explain(StringBuilder sb) {
		sb.Append("match#").Append(Id).Append(" {");
		for (var a = 0; a < Arms.Length; a++) {
			if (a > 0)
				sb.Append("; ");
			sb.Append(Labels[a]).Append(": ");
			Explain(sb, Arms[a]);
		}

		sb.Append('}');
	}
}

/// <summary>An <c>Or</c> node: the index of its <see cref="OrStep{TKey,TValue,TArgs}" /> in the plan's step array, which carries the branches.</summary>
internal sealed class OrNode<TArgs> : PipelineNode<TArgs>
	where TArgs : struct {
	internal readonly byte Step;
	private readonly IOrStepExplain _or;

	internal OrNode(byte step, IOrStepExplain or) : base(PipelineNodeKind.Or) {
		Step = step;
		_or = or;
	}

	internal override void Explain(StringBuilder sb) {
		sb.Append("step ").Append(Step).Append(" Or ");
		_or.ExplainBranches(sb);
	}
}

/// <summary>What <c>Explain()</c> needs of an Or step without its type arguments.</summary>
internal interface IOrStepExplain {
	void ExplainBranches(StringBuilder sb);
}

/// <summary>A pipeline plan's shape when it has composites: the top-level nodes and how many select nodes they hold (for the last-bind record).</summary>
internal sealed class PipelineTree<TArgs>(PipelineNode<TArgs>[] top, int selectCount)
	where TArgs : struct {
	internal readonly PipelineNode<TArgs>[] Top = top;
	internal readonly int SelectCount = selectCount;
}

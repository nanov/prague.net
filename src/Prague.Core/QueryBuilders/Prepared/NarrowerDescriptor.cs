namespace Prague.Core;

using System.Text;

/// <summary>The kind of one recorded narrowing step, as seen by <c>BuildFrozen()</c>'s planner.</summary>
public enum NarrowerKind {
	UniqueEq,
	UniqueIn,
	ListEq,
	ListIn,
	ListInProjected,
	Range,
	KeySet,
	LastUpdatedAfter,
	LastUpdatedBetween,
	Filter,
	FilterArg,
	Or,
	If,
	IfElse,
}

/// <summary>
///   Build-time description of one narrower: the flattened, inspectable twin of a link in the typed
///   chain. Produced once by <c>INarrower.Describe</c> when a query is frozen; never touched at execute
///   time, so it holds the index as an <see cref="object" /> (for identity), the bound value boxed, and
///   the delegates as <see cref="Delegate" />. Composite narrowers (<c>Or</c>, <c>If</c>, <c>IfElse</c>)
///   describe their sub-chains recursively in <see cref="Children" />.
/// </summary>
public sealed class NarrowerDescriptor {
	private static readonly IReadOnlyList<IReadOnlyList<NarrowerDescriptor>> NoChildren = [];

	public NarrowerKind Kind { get; }

	/// <summary>True when the step reads the execution arguments (a selector, an arg predicate or a condition).</summary>
	public bool IsParameterized { get; }

	/// <summary>The index object the step narrows through, for identity; <c>null</c> for filters and composites.</summary>
	public object? Index { get; }

	/// <summary>The bound value(s), boxed; <c>null</c> when the step is parameterized or has no value.</summary>
	public object? Value { get; }

	/// <summary>The argument selector, range builder, key projection or branch condition; <c>null</c> when none.</summary>
	public Delegate? Selector { get; }

	/// <summary>The predicate of a <see cref="NarrowerKind.Filter" /> / <see cref="NarrowerKind.FilterArg" /> step.</summary>
	public Delegate? Filter { get; }

	/// <summary>Sub-chains of a composite step, in branch order (<c>Or</c>: two, <c>If</c>: one, <c>IfElse</c>: then/else).</summary>
	public IReadOnlyList<IReadOnlyList<NarrowerDescriptor>> Children { get; }

	// The narrower that produced the descriptor, when it can seed a specialized executor (a unique
	// equality step). Typed as object so the descriptor stays non-generic; the planner type-tests it.
	internal object? Source { get; }

	private NarrowerDescriptor(NarrowerKind kind, bool isParameterized, object? index, object? value, Delegate? selector, Delegate? filter,
		IReadOnlyList<IReadOnlyList<NarrowerDescriptor>> children, object? source) {
		Kind = kind;
		IsParameterized = isParameterized;
		Index = index;
		Value = value;
		Selector = selector;
		Filter = filter;
		Children = children;
		Source = source;
	}

	public static NarrowerDescriptor ForIndex(NarrowerKind kind, object index, object? value = null, Delegate? selector = null, bool isParameterized = false)
		=> new(kind, isParameterized, index, value, selector, null, NoChildren, null);

	internal static NarrowerDescriptor ForIndex(NarrowerKind kind, object index, object? value, Delegate? selector, bool isParameterized, object source)
		=> new(kind, isParameterized, index, value, selector, null, NoChildren, source);

	public static NarrowerDescriptor ForFilter(Delegate predicate, bool isParameterized)
		=> new(isParameterized ? NarrowerKind.FilterArg : NarrowerKind.Filter, isParameterized, null, null, null, predicate, NoChildren, null);

	public static NarrowerDescriptor ForComposite(NarrowerKind kind, Delegate? condition, params IReadOnlyList<NarrowerDescriptor>[] children)
		=> new(kind, condition is not null, null, null, condition, null, children, null);

	public override string ToString() {
		var sb = new StringBuilder();
		Write(sb, 0);
		return sb.ToString();
	}

	internal void Write(StringBuilder sb, int depth) {
		sb.Append(' ', depth * 2).Append(Kind).Append(IsParameterized ? " (arg)" : " (bound)");
		if (Index is not null)
			sb.Append(" index=").Append(Index.GetType().Name);
		if (Value is not null)
			sb.Append(" value=").Append(Value);
		sb.AppendLine();
		for (var b = 0; b < Children.Count; b++) {
			sb.Append(' ', depth * 2 + 2).Append("branch ").Append(b + 1).AppendLine(":");
			var branch = Children[b];
			if (branch.Count == 0)
				sb.Append(' ', depth * 2 + 4).AppendLine("(empty)");
			for (var i = 0; i < branch.Count; i++)
				branch[i].Write(sb, depth + 2);
		}
	}
}

/// <summary>
///   What <c>BuildFrozen()</c> decided: the flattened narrowers, a summary of the resolver chain and
///   the name of the executor it picked. Immutable, allocated once at build; read by
///   <see cref="FrozenQuery{TArgs,TResult}.Explain" /> and by tests.
/// </summary>
public sealed class PlanInfo {
	public IReadOnlyList<NarrowerDescriptor> Narrowers { get; }

	/// <summary>True when the chain carries joins or a sort (anything beyond the base resolver).</summary>
	public bool HasResolvers { get; }

	public bool IsSorted { get; }

	/// <summary>The executor <c>BuildFrozen()</c> selected: <c>PointLookup</c> or <c>Replay</c>.</summary>
	public string Executor { get; }

	internal PlanInfo(IReadOnlyList<NarrowerDescriptor> narrowers, bool hasResolvers, bool isSorted, string executor) {
		Narrowers = narrowers;
		HasResolvers = hasResolvers;
		IsSorted = isSorted;
		Executor = executor;
	}

	public string Explain() {
		var sb = new StringBuilder();
		sb.Append("executor: ").AppendLine(Executor);
		sb.Append("narrowers: ").Append(Narrowers.Count).AppendLine();
		for (var i = 0; i < Narrowers.Count; i++)
			Narrowers[i].Write(sb, 1);
		sb.Append("resolvers: ").Append(HasResolvers ? "yes" : "none").Append(", sorted: ").AppendLine(IsSorted ? "yes" : "no");
		return sb.ToString();
	}
}

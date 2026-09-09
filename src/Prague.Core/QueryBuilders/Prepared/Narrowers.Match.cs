namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;
using QueryBuilders;

/// <summary>
///   A recorded chain of <c>Match</c> arms: tag + frozen sub-chain per <c>Case</c>, an optional
///   <c>Default</c> sub-chain at the tail. The same type-level shape as
///   <see cref="INarrowerChain{TKey,TValue,TArgs}" /> (a closed generic per arm, so the JIT
///   specializes the replay and the chain is a plain value). <see cref="TryReplay{TCore}" /> walks
///   the arms in declaration order and replays the first whose tag equals the selected one — so a
///   tag declared twice resolves to its first arm — then stops; it reports whether an arm ran.
/// </summary>
public interface IMatchArms<TKey, TValue, TArgs, TTag>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull {
	bool TryReplay<TCore>(ref TCore core, in TArgs args, in TTag tag)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>;

	/// <summary>Appends one described sub-chain per arm, in declaration order (the default last), and its tag (boxed) per <c>Case</c>.</summary>
	void Describe(List<IReadOnlyList<NarrowerDescriptor>> arms, List<object> tags, out bool hasDefault);
}

/// <summary>
///   An arm chain that has no <c>Default</c> yet. <c>Case</c> and <c>Default</c> bind only on these,
///   so an arm after <c>Default</c> — which could never run — is a compile error.
/// </summary>
public interface IOpenMatchArms<TKey, TValue, TArgs, TTag> : IMatchArms<TKey, TValue, TArgs, TTag>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull;

/// <summary>Arm-chain head: no arm recorded yet. Never matches.</summary>
public readonly struct EmptyArms<TKey, TValue, TArgs, TTag> : IOpenMatchArms<TKey, TValue, TArgs, TTag>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReplay<TCore>(ref TCore core, in TArgs args, in TTag tag)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> false;

	public void Describe(List<IReadOnlyList<NarrowerDescriptor>> arms, List<object> tags, out bool hasDefault) => hasDefault = false;
}

/// <summary>
///   One <c>Case</c>: every arm declared before, then this tag and its recorded sub-chain. The tag
///   compare is <see cref="EqualityComparer{T}.Default" />, which the JIT devirtualizes and inlines
///   for value-type tags (enums do not implement <see cref="IEquatable{T}" />, so a constrained
///   call is not an option); no boxing on the execution path.
/// </summary>
public readonly struct MatchArms<TPrev, TArm, TKey, TValue, TArgs, TTag> : IOpenMatchArms<TKey, TValue, TArgs, TTag>
	where TPrev : struct, IOpenMatchArms<TKey, TValue, TArgs, TTag>
	where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull {
	private readonly TPrev _prev;
	private readonly TTag _tag;
	private readonly TArm _arm;

	public MatchArms(in TPrev prev, TTag tag, in TArm arm) {
		_prev = prev;
		_tag = tag;
		_arm = arm;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReplay<TCore>(ref TCore core, in TArgs args, in TTag tag)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (_prev.TryReplay(ref core, in args, in tag))
			return true;
		if (!EqualityComparer<TTag>.Default.Equals(_tag, tag))
			return false;
		_arm.Replay(ref core, in args);
		return true;
	}

	public void Describe(List<IReadOnlyList<NarrowerDescriptor>> arms, List<object> tags, out bool hasDefault) {
		_prev.Describe(arms, tags, out hasDefault);
		var arm = new List<NarrowerDescriptor>();
		_arm.Describe(arm);
		arms.Add(arm);
		tags.Add(_tag);
	}
}

/// <summary>The <c>Default</c> arm: replays when no <c>Case</c> before it matched. Closes the chain.</summary>
public readonly struct DefaultArm<TPrev, TArm, TKey, TValue, TArgs, TTag> : IMatchArms<TKey, TValue, TArgs, TTag>
	where TPrev : struct, IOpenMatchArms<TKey, TValue, TArgs, TTag>
	where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull {
	private readonly TPrev _prev;
	private readonly TArm _arm;

	public DefaultArm(in TPrev prev, in TArm arm) {
		_prev = prev;
		_arm = arm;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReplay<TCore>(ref TCore core, in TArgs args, in TTag tag)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (!_prev.TryReplay(ref core, in args, in tag))
			_arm.Replay(ref core, in args);
		return true;
	}

	public void Describe(List<IReadOnlyList<NarrowerDescriptor>> arms, List<object> tags, out bool hasDefault) {
		_prev.Describe(arms, tags, out _);
		var arm = new List<NarrowerDescriptor>();
		_arm.Describe(arm);
		arms.Add(arm);
		hasDefault = true;
	}
}

/// <summary>
///   Tag-dispatched narrowing: one delegate call selects the tag from the execution arguments, the
///   arm chain replays the first <c>Case</c> whose tag equals it, else the <c>Default</c> when there
///   is one, else nothing. The arms were recorded once at build (the prepared twin of a C#
///   <c>switch</c> over type-preserving builder reassignments), so the plan stays analyzable — which
///   is why an opaque per-execution <c>Eval</c> callback was rejected in its favour. Seeding is the
///   eager core's, as for <see cref="IfNarrower{TKey,TValue,TArgs,TSub}" />: a <c>Match</c> whose
///   selected arm is a no-op leaves <c>_first</c> untouched and the next narrower seeds.
/// </summary>
public readonly struct MatchNarrower<TKey, TValue, TArgs, TTag, TArms> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull
	where TArms : struct, IMatchArms<TKey, TValue, TArgs, TTag> {
	private readonly Func<TArgs, TTag> _selector;
	private readonly TArms _arms;

	public MatchNarrower(Func<TArgs, TTag> selector, in TArms arms) {
		_selector = selector;
		_arms = arms;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		var tag = _selector(args);
		_arms.TryReplay(ref core, in args, in tag);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		var arms = new List<IReadOnlyList<NarrowerDescriptor>>();
		var tags = new List<object>();
		_arms.Describe(arms, tags, out var hasDefault);
		plan.Add(NarrowerDescriptor.ForMatch(_selector, new MatchArmTags(tags, hasDefault), arms));
	}
}

/// <summary>
///   The <see cref="NarrowerDescriptor.Value" /> of a <see cref="NarrowerKind.Match" /> step: the
///   boxed tag of every <c>Case</c> in declaration order, and whether a <c>Default</c> closes the
///   chain. <c>Children[i]</c> is the sub-chain of <c>Tags[i]</c>; the default, when present, is the
///   last child.
/// </summary>
public sealed class MatchArmTags {
	public IReadOnlyList<object> Tags { get; }

	public bool HasDefault { get; }

	public MatchArmTags(IReadOnlyList<object> tags, bool hasDefault) {
		Tags = tags;
		HasDefault = hasDefault;
	}

	public override string ToString() {
		var sb = new StringBuilder();
		sb.Append('[');
		for (var i = 0; i < Tags.Count; i++) {
			if (i > 0) sb.Append(", ");
			sb.Append(Tags[i]);
		}

		if (HasDefault) sb.Append(Tags.Count > 0 ? ", default" : "default");
		return sb.Append(']').ToString();
	}
}

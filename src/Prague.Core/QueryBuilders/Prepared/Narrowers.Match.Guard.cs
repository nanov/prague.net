namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;
using QueryBuilders;

/// <summary>
///   The tag type of the guard form: there is none. A guard arm decides from the execution arguments
///   alone, so the arm chain carries no selected value — but it is the very
///   <see cref="IOpenMatchArms{TKey,TValue,TArgs,TTag}" /> chain the tag form uses, closed by the very
///   <see cref="DefaultArm{TPrev,TArm,TKey,TValue,TArgs,TTag}" />, and this empty struct is what stands
///   in its <c>TTag</c> slot so both forms share one type-state, one <c>Default</c> and one replay shape.
/// </summary>
public readonly struct NoTag;

/// <summary>
///   One guard <c>Case</c>: every arm declared before, then this predicate and its recorded sub-chain.
///   <see cref="TryReplay{TCore}" /> asks the earlier arms first, so guards are evaluated in
///   declaration order and the first one that holds wins — the later guards are never called, exactly
///   as a C# <c>if / else if</c> chain short-circuits. The tag parameter is ignored: a guard reads the
///   arguments itself.
/// </summary>
public readonly struct GuardArms<TPrev, TArm, TKey, TValue, TArgs> : IOpenMatchArms<TKey, TValue, TArgs, NoTag>
	where TPrev : struct, IOpenMatchArms<TKey, TValue, TArgs, NoTag>
	where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TArgs : struct {
	private readonly TPrev _prev;
	private readonly Func<TArgs, bool> _guard;
	private readonly TArm _arm;

	public GuardArms(in TPrev prev, Func<TArgs, bool> guard, in TArm arm) {
		_prev = prev;
		_guard = guard;
		_arm = arm;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReplay<TCore>(ref TCore core, in TArgs args, in NoTag tag)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (_prev.TryReplay(ref core, in args, in tag))
			return true;
		if (!_guard(args))
			return false;
		_arm.Replay(ref core, in args);
		return true;
	}

	public void Describe(List<IReadOnlyList<NarrowerDescriptor>> arms, List<object> tags) {
		_prev.Describe(arms, tags);
		var arm = new List<NarrowerDescriptor>();
		_arm.Describe(arm);
		arms.Add(arm);
		tags.Add(_guard);
	}
}

/// <summary>
///   Guard-dispatched narrowing: no selector, one predicate per <c>Case</c>, and the first arm whose
///   guard holds for this execution's arguments replays — else the <c>Default</c>, which
///   <typeparamref name="TArms" />'s <see cref="IClosedMatchArms{TKey,TValue,TArgs,TTag}" /> constraint
///   guarantees is there, so exactly one arm always runs. The arms were recorded once at build (the
///   prepared twin of a C# <c>if / else if / else</c> chain over type-preserving builder
///   reassignments), so the plan stays analyzable and the pipeline can choose the arm at bind rather
///   than replay. Seeding is the eager core's, as for the tag form: a guard <c>Match</c> whose selected
///   arm is a no-op (an empty <c>Default()</c>, say) leaves <c>_first</c> untouched and the next
///   narrower seeds — which is what makes <c>If</c> a one-<c>Case</c> guard <c>Match</c> and nothing more.
/// </summary>
public readonly struct GuardMatchNarrower<TKey, TValue, TArgs, TArms> : INarrower<TKey, TValue, TArgs>, IBranchSelectorSource<TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, NoTag>
	where TArgs : struct {
	private readonly TArms _arms;

	public GuardMatchNarrower(in TArms arms) => _arms = arms;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		var none = default(NoTag);
		_arms.TryReplay(ref core, in args, in none);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		var arms = new List<IReadOnlyList<NarrowerDescriptor>>();
		var recorded = new List<object>();
		_arms.Describe(arms, recorded);
		var guards = new Delegate[recorded.Count];
		for (var i = 0; i < guards.Length; i++)
			guards[i] = (Delegate)recorded[i];
		plan.Add(NarrowerDescriptor.ForGuardMatch(new MatchArmGuards(guards), arms, this));
	}

	// The pipeline's bind-time dispatch (design §5.2): the same guards, typed once at build.
	IBranchSelector<TArgs> IBranchSelectorSource<TArgs>.CreateSelector(object arms) {
		var guards = ((MatchArmGuards)arms).Guards;
		var typed = new Func<TArgs, bool>[guards.Count];
		for (var i = 0; i < typed.Length; i++)
			typed[i] = (Func<TArgs, bool>)guards[i];
		return new GuardSelector<TArgs>(typed);
	}
}

/// <summary>
///   The <see cref="NarrowerDescriptor.Value" /> of a guard-form <see cref="NarrowerKind.Match" />
///   step: the predicate of every <c>Case</c> in declaration order. <c>Children[i]</c> is the sub-chain
///   of <c>Guards[i]</c>; the <c>Default</c> — always present — is the last child. A tag-form
///   <c>Match</c> carries a <see cref="MatchArmTags" /> here instead, which is how the plan tells the
///   two apart.
/// </summary>
public sealed class MatchArmGuards {
	public IReadOnlyList<Delegate> Guards { get; }

	public MatchArmGuards(IReadOnlyList<Delegate> guards) => Guards = guards;

	public override string ToString() {
		var sb = new StringBuilder();
		sb.Append('[');
		for (var i = 0; i < Guards.Count; i++)
			sb.Append("guard#").Append(i).Append(", ");
		return sb.Append("default]").ToString();
	}
}

namespace Prague.Core;

using System.Runtime.CompilerServices;
using QueryBuilders;

/// <summary>
///   Conditional narrowing: the recorded sub-chain replays only when the condition holds for this
///   execution's arguments. The condition delegate is created once at build time (a capturing lambda
///   costs nothing per execution) and the sub-chain is a plain value, so a skipped branch costs one
///   delegate call. Seeding is the eager core's: a skipped first <c>If</c> leaves <c>_first</c> set,
///   so the next narrower seeds the candidates exactly as if the eager caller's <c>if</c> had been
///   false; when every narrower sits inside a skipped <c>If</c> the query is an all-rows scan.
/// </summary>
public readonly struct IfNarrower<TKey, TValue, TArgs, TSub> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TSub : struct, INarrowerChain<TKey, TValue, TArgs>
	where TArgs : struct {
	private readonly Func<TArgs, bool> _condition;
	private readonly TSub _sub;

	public IfNarrower(Func<TArgs, bool> condition, in TSub sub) {
		_condition = condition;
		_sub = sub;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (_condition(args))
			_sub.Replay(ref core, in args);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		var sub = new List<NarrowerDescriptor>();
		_sub.Describe(sub);
		plan.Add(NarrowerDescriptor.ForComposite(NarrowerKind.If, _condition, sub));
	}
}

/// <summary>Two-way conditional narrowing: exactly one of the two recorded sub-chains replays per execution.</summary>
public readonly struct IfElseNarrower<TKey, TValue, TArgs, TThen, TElse> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
	where TElse : struct, INarrowerChain<TKey, TValue, TArgs>
	where TArgs : struct {
	private readonly Func<TArgs, bool> _condition;
	private readonly TThen _then;
	private readonly TElse _else;

	public IfElseNarrower(Func<TArgs, bool> condition, in TThen then, in TElse otherwise) {
		_condition = condition;
		_then = then;
		_else = otherwise;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args)
		where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (_condition(args))
			_then.Replay(ref core, in args);
		else
			_else.Replay(ref core, in args);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		var then = new List<NarrowerDescriptor>();
		var otherwise = new List<NarrowerDescriptor>();
		_then.Describe(then);
		_else.Describe(otherwise);
		plan.Add(NarrowerDescriptor.ForComposite(NarrowerKind.IfElse, _condition, then, otherwise));
	}
}

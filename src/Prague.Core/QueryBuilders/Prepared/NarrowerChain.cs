namespace Prague.Core;

using System.Runtime.CompilerServices;

/// <summary>Chain head: no narrowing recorded yet. Replaying it leaves the core untouched (all-rows query).</summary>
public readonly struct EmptyNarrowers<TKey, TValue, TArgs> : INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesFilterer<TKey, TValue> { }
}

/// <summary>One link: everything recorded before, then this narrower. Replay order is build order.</summary>
public readonly struct NarrowerLink<TPrev, TNarrower, TKey, TValue, TArgs> : INarrowerChain<TKey, TValue, TArgs>
	where TPrev : struct, INarrowerChain<TKey, TValue, TArgs>
	where TNarrower : struct, INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey> {
	private readonly TPrev _prev;
	private readonly TNarrower _narrower;

	public NarrowerLink(in TPrev prev, in TNarrower narrower) {
		_prev = prev;
		_narrower = narrower;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesFilterer<TKey, TValue> {
		_prev.Replay(ref core, in args);
		_narrower.Apply(ref core, in args);
	}
}

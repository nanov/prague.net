namespace Prague.Core;

using System.Runtime.CompilerServices;
using QueryBuilders;

/// <summary>Chain head: no narrowing recorded yet. Replaying it leaves the core untouched (all-rows query).</summary>
public readonly struct EmptyNarrowers<TKey, TValue, TArgs> : INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ReplayIndexOnly<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> { }

	public void Describe(List<NarrowerDescriptor> plan) { }
}

/// <summary>One link: everything recorded before, then this narrower. Replay order is build order.</summary>
public readonly struct NarrowerLink<TPrev, TNarrower, TKey, TValue, TArgs> : INarrowerChain<TKey, TValue, TArgs>
	where TPrev : struct, INarrowerChain<TKey, TValue, TArgs>
	where TNarrower : struct, INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly TPrev _prev;
	private readonly TNarrower _narrower;

	public NarrowerLink(in TPrev prev, in TNarrower narrower) {
		_prev = prev;
		_narrower = narrower;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		_prev.Replay(ref core, in args);
		_narrower.Apply(ref core, in args);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ReplayIndexOnly<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		_prev.ReplayIndexOnly(ref core, in args);
		if (typeof(TNarrower) != typeof(FilterNarrower<TKey, TValue, TArgs>) && typeof(TNarrower) != typeof(FilterArgNarrower<TKey, TValue, TArgs>))
			_narrower.Apply(ref core, in args);
	}

	public void Describe(List<NarrowerDescriptor> plan) {
		_prev.Describe(plan);
		_narrower.Describe(plan);
	}
}

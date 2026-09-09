namespace Prague.Core;

using QueryBuilders;

/// <summary>
///   One recorded narrowing step of a prepared query. <see cref="Apply{TCore}" /> replays it into an
///   eager core by calling the very <see cref="ICandidatesFilterer{TKey,TValue}" /> method the eager
///   builder's extension would have called, so a replayed core is byte-for-byte the state the eager
///   builder would have reached. Generic over the core (rather than taking the concrete core type)
///   so the call is a constrained call on a struct — no boxing through the explicitly implemented
///   interface — and so the same narrower can later replay into Or-branch cores.
/// </summary>
public interface INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>;

	/// <summary>
	///   Appends this step's build-time description to <paramref name="plan" />. Called once by
	///   <c>BuildFrozen()</c>; allocation is fine here, nothing on the execute path reads the result.
	/// </summary>
	void Describe(List<NarrowerDescriptor> plan);
}

/// <summary>
///   A recorded sequence of narrowing steps, as a type-level chain (the same shape as
///   <see cref="Resolvers{TPrev,TResolver}" />): every link is a closed generic, so the JIT
///   specializes and inlines <see cref="Replay{TCore}" /> per query shape and the chain is a plain
///   value with no per-step heap object.
/// </summary>
public interface INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>;

	/// <summary>
	///   <see cref="Replay{TCore}" /> without the chain's own top-level <c>Where</c> links — the frozen
	///   replay applies those as one fused predicate (<see cref="FrozenOptions.FuseFilters" />) before
	///   calling this. Filters inside composite branches (<c>If</c>, <c>Match</c>) are part of their
	///   sub-chains and still replay. The link test is a <c>typeof</c> comparison the JIT folds per
	///   closed chain, so this costs nothing over <c>Replay</c>.
	/// </summary>
	void ReplayIndexOnly<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>;

	/// <summary>Describes every recorded step in build (= replay) order.</summary>
	void Describe(List<NarrowerDescriptor> plan);
}

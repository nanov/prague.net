namespace Prague.Core;

/// <summary>
///   One recorded narrowing step of a prepared query. <see cref="Apply{TCore}" /> replays it into an
///   eager core by calling the very <see cref="ICandidatesFilterer{TKey,TValue}" /> method the eager
///   builder's extension would have called, so a replayed core is byte-for-byte the state the eager
///   builder would have reached. Generic over the core (rather than taking the concrete core type)
///   so the call is a constrained call on a struct — no boxing through the explicitly implemented
///   interface — and so the same narrower can later replay into Or-branch cores.
/// </summary>
public interface INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey> {
	void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesFilterer<TKey, TValue>;
}

/// <summary>
///   A recorded sequence of narrowing steps, as a type-level chain (the same shape as
///   <see cref="Resolvers{TPrev,TResolver}" />): every link is a closed generic, so the JIT
///   specializes and inlines <see cref="Replay{TCore}" /> per query shape and the chain is a plain
///   value with no per-step heap object.
/// </summary>
public interface INarrowerChain<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey> {
	void Replay<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesFilterer<TKey, TValue>;
}

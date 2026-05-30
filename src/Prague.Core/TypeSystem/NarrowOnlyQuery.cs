namespace Prague.Core.TypeSystem;

/// <summary>
/// Discriminator used inside Or-branch builders. Implements only the
/// markers that admit candidate-narrowing extensions (UseIndex, WithXxx,
/// nested Or). Deliberately does NOT implement IBaseFilterable, IBaseJoinable,
/// IInnerJoinable, ISortable, IExecutableQuery — so Where, Join, Sort, and
/// Execute* are compile-unreachable inside an Or branch.
/// </summary>
public readonly struct NarrowOnlyQuery<TCache> : IIndexNarrower, ICacheCarrier<TCache> {
	public TCache Cache { get; }

	public NarrowOnlyQuery(TCache cache) => Cache = cache;
}

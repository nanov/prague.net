namespace Prague.Core.TypeSystem;

/// <summary>
///   Discriminator of a prepared <c>Or</c> branch builder — the prepared twin of
///   <see cref="NarrowOnlyQuery{TCache}" />. Implements only <see cref="IIndexNarrower" />, so inside a
///   branch <c>UseIndex</c> and a nested <c>Or</c> bind while <c>Where</c>, joins, sorting and
///   <c>Build()</c> are compile errors: a branch only ever narrows candidates, exactly as eager.
/// </summary>
public readonly struct PreparedNarrowOnly<TCache> : IIndexNarrower, ICacheCarrier<TCache> {
	public TCache Cache { get; }

	public PreparedNarrowOnly(TCache cache) => Cache = cache;
}

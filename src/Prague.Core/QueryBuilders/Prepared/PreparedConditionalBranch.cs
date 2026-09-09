namespace Prague.Core.TypeSystem;

/// <summary>
///   Discriminator of a prepared <c>If</c> / <c>IfElse</c> branch builder. Implements
///   <see cref="IBaseFilterable" /> (and through it <see cref="IIndexNarrower" />), so inside a
///   conditional branch every <c>UseIndex</c>, constant and parameterized <c>Where</c>, a nested
///   <c>Or</c> and a nested <c>If</c> bind, while joins, sorting and <c>Build()</c> — resolver-chain
///   operations that would change the query's shape — are compile errors. A branch conditionally
///   narrows the same candidate set the enclosing query narrows; it never re-shapes the result.
/// </summary>
public readonly struct PreparedConditionalBranch<TCache> : IBaseFilterable, ICacheCarrier<TCache> {
	public TCache Cache { get; }

	public PreparedConditionalBranch(TCache cache) => Cache = cache;
}

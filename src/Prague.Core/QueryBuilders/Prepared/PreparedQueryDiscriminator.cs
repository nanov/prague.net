namespace Prague.Core.TypeSystem;

/// <summary>
///   Discriminator of the prepared (build-once / execute-on-demand) builder. Admits narrowing,
///   filtering, joining and sorting exactly like <see cref="ExecutableQuery{TCache}" />, but
///   deliberately does NOT implement <see cref="IExecutableQuery" />: the eager
///   <c>Execute*</c> / <c>Count</c> terminals are compile-unreachable on a prepared builder, whose
///   only terminal is <c>Build()</c>.
/// </summary>
public readonly struct PreparedQueryDiscriminator<TCache> : IBaseFilterable, IBaseJoinable, IInnerJoinable, ISortable, ICacheCarrier<TCache> {
	public TCache Cache { get; }

	public PreparedQueryDiscriminator(TCache cache) => Cache = cache;
}

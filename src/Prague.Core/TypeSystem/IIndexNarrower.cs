namespace Prague.Core.TypeSystem;

/// <summary>
/// Marker for discriminators that admit candidate-narrowing extensions
/// (UseIndex, WithXxx, Or). Base of <see cref="IBaseFilterable"/> — every
/// IBaseFilterable discriminator is also an IIndexNarrower, so existing
/// call sites keep working transitively.
/// </summary>
public interface IIndexNarrower { }

namespace Prague.Core.Collections;

using System.Buffers;

/// <summary>
///   Process-wide pool source for every pooled collection. Production leaves
///   <see cref="Provider"/> null, so each closed <see cref="PragueArrayPool{T}.Pool"/>
///   is a static readonly reference to <see cref="ArrayPool{T}.Shared"/> — same JIT
///   shape as referencing Shared directly. Tests install a tracking provider before
///   any pooled type is touched (the field is read once per closed generic, at type
///   init) to account every Rent/Return.
/// </summary>
internal interface IArrayPoolProvider {
	ArrayPool<T> Get<T>();
}

internal static class PragueArrayPool {
	internal static IArrayPoolProvider? Provider;
}

internal static class PragueArrayPool<T> {
	internal static readonly ArrayPool<T> Pool = PragueArrayPool.Provider?.Get<T>() ?? ArrayPool<T>.Shared;
}

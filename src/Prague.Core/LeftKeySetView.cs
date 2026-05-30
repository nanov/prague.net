namespace Prague.Core;

using Collections;

/// <summary>
/// Opaque public wrapper around an internal <see cref="PooledSet{T}"/>. Exists solely so
/// that <see cref="JoinOneLeftSymResolver{TLeftKey,TLeftValue,TRightCache,TLookupKey,TRightIndexKey,TRightKey,TRightValue,TFilter,TSelector}"/>
/// can mention this type in its filter constraint without forcing <c>PooledSet</c> public.
/// <para>
/// Inside Core, fan-out containers reinterpret the view back to <see cref="PooledSet{T}"/>
/// via <c>Unsafe.As&lt;LeftKeySetView&lt;T&gt;, PooledSet&lt;T&gt;&gt;</c> — valid because
/// the struct is a single-reference-field wrapper with identical layout.
/// </para>
/// </summary>
public readonly struct LeftKeySetView<T> {
	internal readonly PooledSet<T, DefaultKeyComparer<T>> Set;

	internal LeftKeySetView(PooledSet<T, DefaultKeyComparer<T>> set) {
		Set = set;
	}
}

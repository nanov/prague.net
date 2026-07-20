namespace Prague.Core.Collections;

using System.Runtime.CompilerServices;
using Prague.Core.Utils;

/// <summary>
/// Zero-virtual-dispatch equality-comparer strategy for hash-based collections
/// (<see cref="ValueDictionary{TKey,TValue,TComparer}"/>, <c>PooledSet</c>, <c>ValueSet</c>).
/// Each collection carries a <c>TComparer : struct, IKeyComparer&lt;TKey&gt;</c> generic parameter;
/// the JIT specializes <see cref="Equals"/> / <see cref="GetHashCode"/> per closed generic, so the
/// dispatch folds to a direct call (or an elided no-op for identity comparers).
/// Mirrors the <c>IKeySelector&lt;TIn,TOut&gt;</c> + <c>IdentitySelector&lt;T&gt;</c> pattern.
/// </summary>
public interface IKeyComparer<T> {
	/// <summary>
	/// <c>true</c> if this comparer is the default (<c>T.Equals</c> / <c>T.GetHashCode</c>).
	/// JIT-specialized per closed generic — callers may branch on this flag and the dead path is folded.
	/// </summary>
	static abstract bool IsDefault { get; }

	bool Equals(T x, T y);
	int GetHashCode(T value);
}

/// <summary>
/// Default key comparer — dispatches to <c>T.Equals</c> / <c>T.GetHashCode</c> via the
/// <see cref="IEquatable{T}"/> contract. Zero-size struct: occupies no field space, so collections
/// parameterized by it stay layout-compatible with their pre-generic predecessors
/// (a load-bearing invariant for <c>Unsafe.As</c> reinterprets in <c>LeftKeySetView</c> and the
/// fan-out containers).
/// </summary>
public readonly struct DefaultKeyComparer<T> : IKeyComparer<T> {
	public static bool IsDefault => true;

	// typeof(T) == typeof(string) is JIT-folded to a compile-time constant per closed generic;
	// the dead branch is dropped. Unsafe.As<T,string>(ref x) is a zero-cost reinterpret —
	// no castclass IL, no runtime type check. The fall-through path uses EqualityComparer<T>.Default
	// which is a JIT intrinsic: for value types implementing IEquatable<T> it folds to a direct
	// call (since .NET 7); for ref types it dispatches to the runtime-cached singleton.
	// Cross-collection hash consistency (ValueSet.IntersectWithPooledSet) is preserved because
	// PooledSet and ValueSet both route hashing through this same struct.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(T x, T y) {
		if (typeof(T) == typeof(string)) {
			ref var sx = ref Unsafe.As<T, string>(ref x);
			ref var sy = ref Unsafe.As<T, string>(ref y);
			return string.Equals(sx, sy);
		}
		return EqualityComparer<T>.Default.Equals(x, y);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetHashCode(T value) {
		if (typeof(T) == typeof(string)) {
			ref var s = ref Unsafe.As<T, string>(ref value);
			return s is null ? 0 : s.GetHashCode();  // Marvin32 — already well-mixed
		}
		if (typeof(T).IsValueType) {
			// Fibonacci hashing (Knuth) — see HashMixing for the constant and its inverse.
			// `int.GetHashCode()` returns the int identity, which is catastrophic for power-of-2 +
			// linear-probe tables (e.g. ValueDictionary) on sequential IDs. The Fibonacci mix spreads
			// bits across the full 32-bit range for ~1 extra cycle per probe. Harmless on prime-sized
			// tables (PooledSet, ValueSet, ConcurrentCacheStore) where FastMod already diffuses bits.
			return HashMixing.Mix(value!.GetHashCode());
		}
		return value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value);
	}
}

/// <summary>
/// Comparer adapter that wraps an arbitrary <see cref="IEqualityComparer{T}"/>. Use only when the
/// caller genuinely needs a non-default comparison (e.g. <see cref="StringComparer.Ordinal"/>) —
/// the inner virtual dispatch is preserved here, so this defeats the JIT-devirtualization win.
/// </summary>
public readonly struct CustomKeyComparer<T> : IKeyComparer<T> {
	private readonly IEqualityComparer<T> _inner;

	public CustomKeyComparer(IEqualityComparer<T> inner) => _inner = inner;

	public static bool IsDefault => false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(T x, T y) => _inner.Equals(x, y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetHashCode(T value) => _inner.GetHashCode(value!);
}

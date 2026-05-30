namespace Prague.Core.Utils;

using System.Runtime.CompilerServices;

/// <summary>
/// Universal struct cloner that implements ICloner&lt;T&gt;.
/// Handles reference types (ICacheClonable), QueryResults (clone elements), and value types (no-op).
/// All branches are JIT constants — dead code is eliminated.
/// </summary>
public struct AutoCloner<T> : ICloner<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref T value) => Cloner<T>.Clone(ref value);
}

internal static class Cloner<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone(ref T s) {
		// Reference-type ICacheClonable path: replace ref with a deep clone.
		// JIT-folds the typeof checks per closed generic.
		if (!typeof(T).IsValueType && typeof(T).IsAssignableTo(typeof(ICacheClonable<T>))) {
			if (s is null) return;
			UnsafeCacheClonableCloner<T>.CloneInPlace(ref s);
			return;
		}
		// QueryResults<X> value-type slots: dispatched via SlotCloner<T>.
		// Default action is a no-op; resolvers that produce QueryResults slots
		// (JoinManyRightListIndexResolver, JoinManyLeftSymResolver) register a
		// CloneElements action in their static ctor. Other slot types (plain
		// value POCOs, etc.) hit the no-op delegate.
		SlotCloner<T>.Clone(ref s);
	}
}

internal delegate void RefAction<T>(ref T value);

/// <summary>
/// Per-T slot-clone registry. Default <see cref="_action"/> is a no-op closure
/// over a static cached lambda — zero allocation, single delegate invoke.
/// Resolvers register via <see cref="Register"/> in their static ctor; the
/// resolver type is always referenced before any matching <c>JoinResult&lt;...&gt;</c>
/// is produced, so registration runs before <c>Cloner&lt;T&gt;.Clone</c>.
/// Multiple registrations of the same closed generic are idempotent.
/// </summary>
internal static class SlotCloner<T> {
	private static RefAction<T> _action = static (ref T _) => { };

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Register(RefAction<T> action) => _action = action;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone(ref T value) => _action(ref value);
}

internal static class QueryResultsCloner<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone(ref QueryResults<T> results) {
		if (!typeof(T).IsAssignableTo(typeof(ICacheClonable<T>)))
			return;
		var span = results.AsSpan();
		for (var i = 0; i < span.Length; i++)
			span[i] = Unsafe.As<T, ICacheClonable<T>>(ref span[i]).Clone();
	}
}

internal struct QueryResultsElementCloner<T> : ICloner<QueryResults<T>> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref QueryResults<T> source) => source.CloneElements();
}

internal struct NoCloner<T> : ICloner<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref T value) { }
}

internal struct CacheClonableCloner<T> : ICloner<T> where T : ICacheClonable<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref T source) => source = source.Clone();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CloneInPlace(ref T source) => source = source.Clone();
}

internal struct UnsafeCacheClonableCloner<T> : ICloner<T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref T source) => source = Unsafe.As<T, ICacheClonable<T>>(ref source).Clone();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CloneInPlace(ref T source) => source = Unsafe.As<T, ICacheClonable<T>>(ref source).Clone();
}

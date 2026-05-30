namespace Prague.Core.Utils;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Utility methods for comparing collections.
/// </summary>
public static class CompareUtils {
	/// <summary>
	/// Optimized dictionary comparison using a custom IValueComparer for deep equality.
	/// Uses CollectionsMarshal for zero-allocation lookups.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool CompareDictionaries<TKey, TValue, TComparer>(Dictionary<TKey, TValue> left, Dictionary<TKey, TValue> right)
		where TKey : notnull
		where TComparer : IValueComparer<TValue> {
		if (left.Count != right.Count) return false;

		foreach (var kvp in left) {
			ref readonly var rightValue = ref CollectionsMarshal.GetValueRefOrNullRef(right, kvp.Key);
			if (Unsafe.IsNullRef(in rightValue)) return false;
			if (!TComparer.Equals(kvp.Value, rightValue)) return false;
		}
		return true;
	}

	/// <summary>
	/// Optimized dictionary comparison using CollectionsMarshal for zero-allocation lookups.
	/// Uses vectorized byte comparison for large unmanaged value types.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool CompareDictionaries<TKey, TValue>(Dictionary<TKey, TValue> left, Dictionary<TKey, TValue> right)
		where TKey : notnull {
		if (left.Count != right.Count) return false;

		// For unmanaged types, check if we should use vectorized byte comparison
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() == false) {
			var size = Unsafe.SizeOf<TValue>();
			// For large unmanaged types (>16 bytes), use vectorized byte comparison
			// This is faster than EqualityComparer for large structs
			if (size > 16) {
				foreach (var kvp in left) {
					ref readonly var rightValue = ref CollectionsMarshal.GetValueRefOrNullRef(right, kvp.Key);
					if (Unsafe.IsNullRef(in rightValue)) return false;

					// Vectorized byte-wise comparison using SIMD when available
					// Copy left value to local to get a ref
					var leftValue = kvp.Value;
					var bytesLeft = MemoryMarshal.CreateReadOnlySpan(
						ref Unsafe.As<TValue, byte>(ref leftValue), size);
					var bytesRight = MemoryMarshal.CreateReadOnlySpan(
						ref Unsafe.As<TValue, byte>(ref Unsafe.AsRef(in rightValue)), size);
					if (!bytesLeft.SequenceEqual(bytesRight))
						return false;
				}
				return true;
			}
		}

		// Standard path: use EqualityComparer (optimized by JIT for primitives and strings)
		var comparer = EqualityComparer<TValue>.Default;
		foreach (var kvp in left) {
			ref readonly var rightValue = ref CollectionsMarshal.GetValueRefOrNullRef(right, kvp.Key);
			if (Unsafe.IsNullRef(in rightValue)) return false;
			if (!comparer.Equals(kvp.Value, rightValue)) return false;
		}
		return true;
	}
}

namespace Prague.Core.Utils;

/// <summary>
/// Interface for static value comparison. Used for optimized dictionary comparisons
/// with JIT-inlined static abstract calls.
/// </summary>
public interface IValueComparer<T> {
	static abstract bool Equals(T? left, T? right);

	/// <summary>
	/// Structural comparison that, when <paramref name="forceDeep"/> is true, skips the
	/// reference-equality fast path at every level. Lets <c>forceDeep</c> propagate across the
	/// dictionary boundary in <see cref="CompareUtils"/>.
	/// </summary>
	static abstract bool Equals(T? left, T? right, bool forceDeep);
}

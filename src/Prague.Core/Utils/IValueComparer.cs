namespace Prague.Core.Utils;

/// <summary>
/// Interface for static value comparison. Used for optimized dictionary comparisons
/// with JIT-inlined static abstract calls.
/// </summary>
public interface IValueComparer<T> {
	static abstract bool Equals(T? left, T? right);
}

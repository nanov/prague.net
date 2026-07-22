namespace Prague.Core.Utils;

internal static class Constants {
	/// <summary>
	///   Default initial capacity of index buckets — a prime that still fits a
	///   128-slot pooled array.
	/// </summary>
	public const int DefaultInitialCapacity = 107;

	/// <summary>
	///   "Capacity not specified" sentinel. Carried through the attribute and index
	///   plumbing as-is; resolved to <see cref="DefaultInitialCapacity"/> in one place —
	///   the PooledSet constructor.
	/// </summary>
	public const int NonSetCapacity = -1;
}
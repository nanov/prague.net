// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Library-wide data-cache constants.
/// </summary>
public static class DataCacheConstants {
	/// <summary>
	///   Default value of <see cref="DataCacheIndexAttribute.InitialCapacity"/> — a prime
	///   that still fits a 128-slot pooled array.
	/// </summary>
	public const int DefaultInitialCapacity = 107;
}

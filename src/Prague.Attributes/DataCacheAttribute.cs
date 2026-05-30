// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a class as a data cache item. The generator will create a cache class for this type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataCacheAttribute : Attribute {
	public DataCacheAttribute(string? cacheClassName = null) {
		CacheClassName = cacheClassName;
	}

	public string? CacheClassName { get; set; }
}

/// <summary>
///   Marks a class as a cache for an external type that you don't own.
///   Use this when you cannot add [DataCache] to the cache item type directly.
///   The generator will create the cache implementation and generate Clone/Equality methods for the external type.
/// </summary>
/// <typeparam name="TCacheItem">The external cache item type</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataCacheAttribute<TCacheItem> : Attribute where TCacheItem : class {
	/// <summary>
	///   Creates a cache for an external type with the specified key property.
	/// </summary>
	/// <param name="keyProperty">The name of the property to use as the cache key. Use nameof() for compile-time safety.</param>
	public DataCacheAttribute(string keyProperty) {
		KeyProperty = keyProperty;
	}

	/// <summary>
	///   Creates a cache for an external type. KeyProperty must be set.
	/// </summary>
	public DataCacheAttribute() {
	}

	/// <summary>
	///   The name of the property to use as the cache key.
	/// </summary>
	public string? KeyProperty { get; set; }
}
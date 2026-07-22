// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Creates an index on a property for fast lookup.
///   Can be applied directly to a property, or to a class when using DataCache&lt;T&gt; for external types.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class DataCacheIndexAttribute : Attribute {
	/// <summary>
	///   Creates an index on the property this attribute is attached to.
	/// </summary>
	public DataCacheIndexAttribute() {
	}

	/// <summary>
	///   Creates an index on the property this attribute is attached to with the specified index type.
	/// </summary>
	/// <param name="indexType">The type of index to create.</param>
	public DataCacheIndexAttribute(DataCacheIndexType indexType) {
		IndexType = indexType;
	}

	/// <summary>
	///   Creates an index on a property of an external type (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to index. Use nameof() for compile-time safety.</param>
	/// <param name="indexType">The type of index to create.</param>
	public DataCacheIndexAttribute(string propertyName, DataCacheIndexType indexType) {
		PropertyName = propertyName;
		IndexType = indexType;
	}

	/// <summary>
	///   Creates a named index on a property of an external type (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to index. Use nameof() for compile-time safety.</param>
	/// <param name="indexName">The name of the index.</param>
	/// <param name="indexType">The type of index to create.</param>
	public DataCacheIndexAttribute(string propertyName, string indexName, DataCacheIndexType indexType) {
		PropertyName = propertyName;
		IndexName = indexName;
		IndexType = indexType;
	}

	/// <summary>
	///   The name of the property to index (for class-level usage with external types).
	/// </summary>
	public string? PropertyName { get; set; }

	/// <summary>
	///   The name of the index. If not specified, defaults to the property name.
	/// </summary>
	public string? IndexName { get; set; }

	/// <summary>
	///   The type of index to create.
	/// </summary>
	public DataCacheIndexType IndexType { get; set; } = DataCacheIndexType.Many;

	/// <summary>
	///   When <c>true</c> on a <see cref="DataCacheIndexType.Many"/> index, emit a
	///   <c>CacheSymmetricKeyValueListIndex</c> instead of <c>CacheKeyValueListIndex</c>.
	///   The symmetric variant supports reverse lookup (TKey → TIndexKey), required for
	///   index-driven joins like <c>JoinOne(leftIndex, rightCache)</c>.
	/// </summary>
	public bool Symmetric { get; set; } = false;

	/// <summary>
	///   Initial capacity of each per-key value collection of a
	///   <see cref="DataCacheIndexType.Many"/> index. A hint, not a limit — collections
	///   grow on demand. Set a small value for indexes where a key maps to only a few
	///   values to avoid over-allocation; leave unset for the default. Ignored by other
	///   index types.
	/// </summary>
	public int InitialCapacity { get; set; } = 0;
}
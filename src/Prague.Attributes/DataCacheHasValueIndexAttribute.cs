// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Creates a key set index that tracks which keys have a non-null value for this property.
///   Can be applied to reference types, nullable value types, or boolean properties.
///   For nullable types: indexes keys where the property value is not null.
///   For boolean types: indexes keys where the property value is true.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class DataCacheHasValueIndexAttribute : Attribute {
	/// <summary>
	///   Creates a has-value index on the property this attribute is attached to.
	/// </summary>
	public DataCacheHasValueIndexAttribute() {
	}

	/// <summary>
	///   Creates a has-value index on a property of an external type (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to index. Use nameof() for compile-time safety.</param>
	public DataCacheHasValueIndexAttribute(string propertyName) {
		PropertyName = propertyName;
	}

	/// <summary>
	///   Creates a named has-value index on a property of an external type (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to index. Use nameof() for compile-time safety.</param>
	/// <param name="indexName">The name of the index.</param>
	public DataCacheHasValueIndexAttribute(string propertyName, string indexName) {
		PropertyName = propertyName;
		IndexName = indexName;
	}

	/// <summary>
	///   The name of the property to index (for class-level usage with external types).
	/// </summary>
	public string? PropertyName { get; set; }

	/// <summary>
	///   The name of the index. If not specified, defaults to "Has{PropertyName}Index".
	/// </summary>
	public string? IndexName { get; set; }
}

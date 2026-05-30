// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a property to be excluded from equality comparison and hash code generation.
///   Properties with this attribute will not be included in the generated Equals() and GetHashCode() methods.
///   Can be applied directly to a property, or to a class when using DataCache&lt;T&gt; for external types.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class DataCacheIgnoreEqualityAttribute : Attribute {
	/// <summary>
	///   Marks the property this attribute is attached to be ignored in equality comparison.
	/// </summary>
	public DataCacheIgnoreEqualityAttribute() {
	}

	/// <summary>
	///   Marks a property on an external type to be ignored in equality comparison (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to ignore. Use nameof() for compile-time safety.</param>
	public DataCacheIgnoreEqualityAttribute(string propertyName) {
		PropertyName = propertyName;
	}

	/// <summary>
	///   The name of the property to ignore (for class-level usage with external types).
	/// </summary>
	public string? PropertyName { get; set; }
}
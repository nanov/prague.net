// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Creates a key set index that tracks which keys match a comparison condition.
///   The index stores keys where the property value satisfies the comparison with the specified value.
///   Uses CacheKeySetIndex under the hood for O(1) lookups.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
public sealed class DataCacheValueIndexAttribute : Attribute {
	/// <summary>
	///   Creates a value index on the property this attribute is attached to.
	/// </summary>
	/// <param name="op">The comparison operation to use.</param>
	/// <param name="value">The value to compare against. Must be a compile-time constant (number, string, enum, etc.).</param>
	public DataCacheValueIndexAttribute(CompareOp op, object value) {
		Operation = op;
		Value = value;
	}

	/// <summary>
	///   Creates a value index on a property of an external type (for use with DataCache&lt;T&gt;).
	/// </summary>
	/// <param name="propertyName">The name of the property to index. Use nameof() for compile-time safety.</param>
	/// <param name="op">The comparison operation to use.</param>
	/// <param name="value">The value to compare against. Must be a compile-time constant (number, string, enum, etc.).</param>
	public DataCacheValueIndexAttribute(string propertyName, CompareOp op, object value) {
		PropertyName = propertyName;
		Operation = op;
		Value = value;
	}

	/// <summary>
	///   The name of the property to index (for class-level usage with external types).
	/// </summary>
	public string? PropertyName { get; set; }

	/// <summary>
	///   The comparison operation to use.
	/// </summary>
	public CompareOp Operation { get; }

	/// <summary>
	///   The value to compare against.
	/// </summary>
	public object Value { get; }

	/// <summary>
	///   Optional custom name for the index. If not specified, auto-generated based on operation and value.
	/// </summary>
	public string? IndexName { get; set; }
}

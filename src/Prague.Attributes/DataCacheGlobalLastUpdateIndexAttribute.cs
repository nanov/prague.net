// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a property as the group key for a global LastUpdatedIndex.
///   The property type must match the TGroupKey of the IDataCacheGlobalLastUpdateIndex&lt;TGroupKey&gt; interface
///   implemented by TIndex.
/// </summary>
/// <typeparam name="TIndex">The partial class type that implements IDataCacheGlobalLastUpdateIndex&lt;TGroupKey&gt;.</typeparam>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class DataCacheGlobalLastUpdateIndexAttribute<TIndex> : Attribute
	where TIndex : class {
	/// <summary>
	///   Creates a global last update index using the timestamp from AddOrUpdate method calls.
	/// </summary>
	public DataCacheGlobalLastUpdateIndexAttribute() {
	}

	/// <summary>
	///   Creates a global last update index using the specified timestamp property.
	/// </summary>
	/// <param name="timestampPropertyName">
	///   The name of the property containing the timestamp. Use nameof() for compile-time
	///   safety.
	/// </param>
	public DataCacheGlobalLastUpdateIndexAttribute(string timestampPropertyName) {
		TimestampPropertyName = timestampPropertyName;
	}

	/// <summary>
	///   The name of the property containing the timestamp.
	///   If null, the timestamp from AddOrUpdate method calls will be used.
	/// </summary>
	public string? TimestampPropertyName { get; }
}
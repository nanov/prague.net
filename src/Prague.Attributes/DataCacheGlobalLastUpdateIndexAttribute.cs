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

/// <summary>
///   Filtered variant of <see cref="DataCacheGlobalLastUpdateIndexAttribute{TIndex}"/>. Only entities
///   for which <typeparamref name="TFilter"/> returns <c>true</c> contribute to the index. Membership
///   is dynamic — the filter is re-evaluated on every update, so an entity that transitions out of the
///   set (e.g. <c>IsEnabled</c> goes <c>true → false</c>) is removed, and one that transitions in is added.
/// </summary>
/// <typeparam name="TIndex">The partial class type that implements IDataCacheGlobalLastUpdateIndex&lt;TGroupKey&gt;.</typeparam>
/// <typeparam name="TFilter">
///   A <c>readonly struct</c> implementing <see cref="IDataCacheGlobalLastUpdateFilter{TValue}"/> for the
///   owning <c>[DataCache]</c> entity type. Codegen validates the type argument lines up (CACHE032/033).
/// </typeparam>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class DataCacheGlobalLastUpdateIndexAttribute<TIndex, TFilter> : Attribute
	where TIndex : class
	where TFilter : struct {
	// Note: TFilter is NOT constrained to IDataCacheGlobalLastUpdateFilter<> at the C# level (we'd need the
	// entity type as a type param to express that). Codegen validates that TFilter implements
	// IDataCacheGlobalLastUpdateFilter<TEntity> for the owning entity (CACHE032/033).
	/// <summary>
	///   Creates a filtered global last update index using the timestamp from AddOrUpdate method calls.
	/// </summary>
	public DataCacheGlobalLastUpdateIndexAttribute() {
	}

	/// <summary>
	///   Creates a filtered global last update index using the specified timestamp property.
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

/// <summary>
///   Static-abstract membership predicate for a filtered global last update index. Implementations should
///   be <c>readonly struct</c>s so the JIT devirtualizes the <see cref="Include"/> call per closed generic.
/// </summary>
/// <typeparam name="TValue">The owning <c>[DataCache]</c> entity type.</typeparam>
public interface IDataCacheGlobalLastUpdateFilter<TValue> {
	/// <summary>Returns <c>true</c> if <paramref name="value"/> should be tracked by the index.</summary>
	static abstract bool Include(TValue value);
}
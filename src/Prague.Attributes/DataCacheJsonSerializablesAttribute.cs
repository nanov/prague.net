namespace Prague.Core;

/// <summary>
///   Marks a partial JsonSerializerContext class to have JSON serialization registrations
///   generated for all QueryResults&lt;T&gt; types in the project.
///   The generator will add [JsonSerializable] attributes for each cache document type.
/// </summary>
/// <example>
///   <code>
/// [DataCacheJsonSerializables]
/// public partial class MyJsonContext : JsonSerializerContext
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataCacheJsonSerializablesAttribute : Attribute {
}
// ReSharper disable once CheckNamespace
namespace Prague.Core;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DataCacheSortAttribute : Attribute {
	/// <summary>
	///   Defines a custom sort comparer for the cache.
	/// </summary>
	/// <param name="name">Name of the comparer (e.g., "ByDate")</param>
	/// <param name="sortProperties">
	///   Property names with direction: "PropertyName:1" for ascending, "PropertyName:-1" for
	///   descending
	/// </param>
	public DataCacheSortAttribute(string name, params string[] sortProperties) {
		Name = name;
		SortProperties = sortProperties;
	}

	public string Name { get; }
	public string[] SortProperties { get; }
}
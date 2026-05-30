// ReSharper disable once CheckNamespace
namespace Prague.Core;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DataCacheHeaderAttribute : Attribute {
	public DataCacheHeaderAttribute() {
	}

	public DataCacheHeaderAttribute(string headerName) {
		HeaderName = headerName;
	}

	public string? HeaderName { get; }
}
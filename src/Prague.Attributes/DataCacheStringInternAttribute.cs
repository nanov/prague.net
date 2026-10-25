// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Chooses whether a string property's deserialized values are interned. Prague interns every string it
///   reads from Kafka by default; <c>[DataCacheStringIntern(false)]</c> opts a property out. Use it for
///   high-cardinality values (ids, free text) that would only churn the intern pool.
/// </summary>
/// <remarks>
///   An opted-out property must be declared <c>partial</c> (C# 13+): Prague's source generator emits the
///   implementing part and attaches the non-interning formatter to it. <c>[DataCacheStringIntern(true)]</c>
///   is the default and changes nothing.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DataCacheStringInternAttribute : Attribute {
	/// <param name="intern">Whether the property's strings are interned; <see langword="true"/> by default.</param>
	public DataCacheStringInternAttribute(bool intern = true) {
		Intern = intern;
	}

	/// <summary>Whether the property's strings are interned.</summary>
	public bool Intern { get; }
}

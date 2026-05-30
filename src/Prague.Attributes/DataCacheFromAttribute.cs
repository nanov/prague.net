// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a class to copy properties from a source type.
///   Use with the code refactoring (lightbulb) to generate properties once, then own them.
///   The hash is auto-generated to detect when the source type changes.
/// </summary>
/// <typeparam name="TSource">The source type to copy properties from</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataCacheFromAttribute<TSource> : Attribute where TSource : class {
	/// <summary>
	///   Creates a data cache from marker for the specified source type (no hash yet - needs generation).
	/// </summary>
	public DataCacheFromAttribute() {
		SourceHash = null;
	}

	/// <summary>
	///   Creates a data cache from marker with a hash of the source type.
	///   Auto-generated when properties are copied. Used to detect source changes.
	/// </summary>
	/// <param name="sourceHash">GUID hash of the source type's properties</param>
	public DataCacheFromAttribute(string sourceHash) {
		SourceHash = sourceHash;
	}

	/// <summary>
	///   GUID hash of the source type's properties. Used to detect when source changes.
	/// </summary>
	public string? SourceHash { get; }

	/// <summary>
	///   Property names to exclude from copying. Use nameof() for compile-time safety.
	/// </summary>
	public string[]? Exclude { get; set; }

	/// <summary>
	///   If set, only these properties will be copied. Use nameof() for compile-time safety.
	/// </summary>
	public string[]? Only { get; set; }

	/// <summary>
	///   Whether to copy attributes from the source properties. Default is true.
	/// </summary>
	public bool CopyAttributes { get; set; } = true;
}
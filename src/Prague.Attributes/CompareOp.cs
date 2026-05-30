// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Comparison operation for value index filtering.
/// </summary>
public enum CompareOp {
	/// <summary>Value equals the specified value.</summary>
	Equal,

	/// <summary>Value does not equal the specified value.</summary>
	NotEqual,

	/// <summary>Value is greater than the specified value.</summary>
	GreaterThan,

	/// <summary>Value is greater than or equal to the specified value.</summary>
	GreaterThanOrEqual,

	/// <summary>Value is less than the specified value.</summary>
	LessThan,

	/// <summary>Value is less than or equal to the specified value.</summary>
	LessThanOrEqual
}

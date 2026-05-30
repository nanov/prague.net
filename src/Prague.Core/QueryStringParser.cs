namespace Prague.Core;

/// <summary>
///   Comparison operator for a query parameter.
/// </summary>
public enum QueryComparisonOp {
	/// <summary>Equality comparison (=)</summary>
	Eq,
	/// <summary>Greater than (&gt;)</summary>
	Gt,
	/// <summary>Greater than or equal (&gt;=)</summary>
	Gte,
	/// <summary>Less than (&lt;)</summary>
	Lt,
	/// <summary>Less than or equal (&lt;=)</summary>
	Lte
}

/// <summary>
///   Represents a parsed query parameter with field name, operator, and value(s).
/// </summary>
public readonly ref struct QueryParam {
	/// <summary>The field name</summary>
	public readonly ReadOnlySpan<char> Field;
	/// <summary>The comparison operator</summary>
	public readonly QueryComparisonOp Op;
	/// <summary>The value (for single value or range start)</summary>
	public readonly ReadOnlySpan<char> Value;
	/// <summary>The range end value (only set for range syntax like 10..100)</summary>
	public readonly ReadOnlySpan<char> RangeEnd;
	/// <summary>Whether this is a range query (10..100 syntax)</summary>
	public readonly bool IsRange;
	/// <summary>Whether the range start is inclusive ([ = inclusive, ( = exclusive)</summary>
	public readonly bool RangeStartInclusive;
	/// <summary>Whether the range end is inclusive (] = inclusive, ) = exclusive)</summary>
	public readonly bool RangeEndInclusive;
	/// <summary>Whether this is an array value ([1,2,3] syntax)</summary>
	public readonly bool IsArray;

	public QueryParam(ReadOnlySpan<char> field, QueryComparisonOp op, ReadOnlySpan<char> value) {
		Field = field;
		Op = op;
		Value = value;
		RangeEnd = default;
		IsRange = false;
		RangeStartInclusive = true;
		RangeEndInclusive = true;
		IsArray = false;
	}

	public QueryParam(ReadOnlySpan<char> field, ReadOnlySpan<char> rangeStart, ReadOnlySpan<char> rangeEnd,
		bool startInclusive = true, bool endInclusive = true) {
		Field = field;
		Op = QueryComparisonOp.Eq; // Not used for ranges, inclusivity is tracked separately
		Value = rangeStart;
		RangeEnd = rangeEnd;
		IsRange = true;
		RangeStartInclusive = startInclusive;
		RangeEndInclusive = endInclusive;
		IsArray = false;
	}

	public QueryParam(ReadOnlySpan<char> field, ReadOnlySpan<char> arrayValue, bool isArray) {
		Field = field;
		Op = QueryComparisonOp.Eq;
		Value = arrayValue;
		RangeEnd = default;
		IsRange = false;
		RangeStartInclusive = true;
		RangeEndInclusive = true;
		IsArray = isArray;
	}
}

/// <summary>
///   Non-allocating query string parser that supports:
///   - Equality: field=value
///   - Comparison operators: field&gt;=10, field&gt;10, field&lt;=10, field&lt;10
///   - Suffix operators: field.gt=10, field.gte=10, field.lt=10, field.lte=10
///   - Range syntax: field=10..100 (inclusive), field=[10..100] (inclusive),
///     field=(10..100) (exclusive), field=[10..100) (start inclusive, end exclusive),
///     field=(10..100] (start exclusive, end inclusive)
///   - Array syntax: field=[1,2,3]
///   - Comma-separated values: field=1,2,3
/// </summary>
public ref struct QueryStringParser {
	private ReadOnlySpan<char> _remaining;

	public QueryStringParser(ReadOnlySpan<char> queryString) {
		// Skip leading '?' if present
		_remaining = queryString.Length > 0 && queryString[0] == '?'
			? queryString.Slice(1)
			: queryString;
	}

	/// <summary>
	///   Tries to get the next query parameter.
	/// </summary>
	/// <param name="param">The parsed parameter</param>
	/// <returns>True if a parameter was found, false if end of string</returns>
	public bool TryGetNext(out QueryParam param) {
		param = default;

		// Skip empty segments
		while (_remaining.Length > 0 && _remaining[0] == '&')
			_remaining = _remaining.Slice(1);

		if (_remaining.Length == 0)
			return false;

		// Find the end of this key=value pair
		var ampIndex = _remaining.IndexOf('&');
		var segment = ampIndex < 0 ? _remaining : _remaining.Slice(0, ampIndex);
		_remaining = ampIndex < 0 ? ReadOnlySpan<char>.Empty : _remaining.Slice(ampIndex + 1);

		// Try to find operator: >=, <=, >, <, or = (in that order of precedence)
		// We need to find the first occurrence of any operator
		var (opIndex, opLength, op) = FindOperator(segment);

		if (opIndex < 0) {
			// No operator found, treat entire segment as field with empty value
			param = new QueryParam(segment, QueryComparisonOp.Eq, ReadOnlySpan<char>.Empty);
			return true;
		}

		var field = segment.Slice(0, opIndex);
		var value = segment.Slice(opIndex + opLength);

		// Check for suffix operator on field name (.gt, .gte, .lt, .lte) - only if we used '=' as separator
		if (op == QueryComparisonOp.Eq) {
			var lastDot = field.LastIndexOf('.');
			if (lastDot > 0 && lastDot < field.Length - 1) {
				var suffix = field.Slice(lastDot + 1);
				if (suffix.Equals("gt".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
					field = field.Slice(0, lastDot);
					op = QueryComparisonOp.Gt;
				}
				else if (suffix.Equals("gte".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
					field = field.Slice(0, lastDot);
					op = QueryComparisonOp.Gte;
				}
				else if (suffix.Equals("lt".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
					field = field.Slice(0, lastDot);
					op = QueryComparisonOp.Lt;
				}
				else if (suffix.Equals("lte".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
					field = field.Slice(0, lastDot);
					op = QueryComparisonOp.Lte;
				}
			}
		}

		// Check for range syntax - only if equality operator
		// Supports: 10..100, [10..100], (10..100), [10..100), (10..100]
		if (op == QueryComparisonOp.Eq && value.Length > 2) {
			// Check for bracket-wrapped range first
			var startChar = value[0];
			var endChar = value[value.Length - 1];
			var hasBrackets = (startChar == '[' || startChar == '(') && (endChar == ']' || endChar == ')');

			var rangeValue = hasBrackets ? value.Slice(1, value.Length - 2) : value;
			var rangeIndex = IndexOfRange(rangeValue);

			if (rangeIndex > 0 && rangeIndex < rangeValue.Length - 2) {
				var rangeStart = rangeValue.Slice(0, rangeIndex);
				var rangeEnd = rangeValue.Slice(rangeIndex + 2);

				// Determine inclusivity from brackets (default to inclusive)
				var startInclusive = !hasBrackets || startChar == '[';
				var endInclusive = !hasBrackets || endChar == ']';

				param = new QueryParam(field, rangeStart, rangeEnd, startInclusive, endInclusive);
				return true;
			}
		}

		// Check for array syntax [1,2,3] - must not contain ".." (which would be a range)
		if (op == QueryComparisonOp.Eq && value.Length >= 2 && value[0] == '[' && value[value.Length - 1] == ']') {
			var innerContent = value.Slice(1, value.Length - 2);
			// Only treat as array if it doesn't contain ".." (range indicator)
			if (IndexOfRange(innerContent) < 0) {
				param = new QueryParam(field, innerContent, isArray: true);
				return true;
			}
		}

		param = new QueryParam(field, op, value);
		return true;
	}

	/// <summary>
	///   Finds the first operator in the segment.
	///   Returns (index, length, operator) or (-1, 0, Eq) if not found.
	///   Supports: >=, =>, <=, =<, >, <, =
	/// </summary>
	private static (int Index, int Length, QueryComparisonOp Op) FindOperator(ReadOnlySpan<char> segment) {
		for (var i = 0; i < segment.Length; i++) {
			var c = segment[i];

			// Check for two-character operators first
			if (i + 1 < segment.Length) {
				var next = segment[i + 1];

				// >= or =>
				if ((c == '>' && next == '=') || (c == '=' && next == '>'))
					return (i, 2, QueryComparisonOp.Gte);

				// <= or =<
				if ((c == '<' && next == '=') || (c == '=' && next == '<'))
					return (i, 2, QueryComparisonOp.Lte);
			}

			// Single character operators
			if (c == '>')
				return (i, 1, QueryComparisonOp.Gt);

			if (c == '<')
				return (i, 1, QueryComparisonOp.Lt);

			if (c == '=')
				return (i, 1, QueryComparisonOp.Eq);
		}

		return (-1, 0, QueryComparisonOp.Eq);
	}

	/// <summary>
	///   Finds the index of ".." in the span.
	/// </summary>
	private static int IndexOfRange(ReadOnlySpan<char> span) {
		for (var i = 0; i < span.Length - 1; i++) {
			if (span[i] == '.' && span[i + 1] == '.')
				return i;
		}
		return -1;
	}
}

/// <summary>
///   Helper for enumerating array values without allocation.
/// </summary>
public ref struct ArrayValueEnumerator {
	private ReadOnlySpan<char> _remaining;

	public ArrayValueEnumerator(ReadOnlySpan<char> arrayContent) {
		_remaining = arrayContent;
		Current = default;
	}

	public ReadOnlySpan<char> Current { get; private set; }

	public bool MoveNext() {
		if (_remaining.Length == 0)
			return false;

		var commaIndex = _remaining.IndexOf(',');
		if (commaIndex < 0) {
			Current = _remaining.Trim();
			_remaining = ReadOnlySpan<char>.Empty;
		}
		else {
			Current = _remaining.Slice(0, commaIndex).Trim();
			_remaining = _remaining.Slice(commaIndex + 1);
		}

		return Current.Length > 0 || _remaining.Length > 0;
	}

	public ArrayValueEnumerator GetEnumerator() => this;
}

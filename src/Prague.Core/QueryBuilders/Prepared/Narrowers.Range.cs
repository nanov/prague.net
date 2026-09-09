namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Text;
using QueryBuilders;

/// <summary>Range index narrowing with the bounds fixed at build time: the range lambda is invoked at replay, as eager.</summary>
public readonly struct RangeNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey>
	where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> _rangeBuilder;

	public RangeNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder) {
		_index = index;
		_rangeBuilder = rangeBuilder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _rangeBuilder);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.Range, _index, selector: _rangeBuilder));
}

/// <summary>
///   Range index narrowing with bounds taken from the execution arguments. The lambda has the eager
///   <c>(rb, args) =&gt; …</c> shape and the query arguments are passed straight through to the eager
///   args overload, so no adaptor delegate or closure exists on the execution path.
/// </summary>
public readonly struct RangeArgNarrower<TKey, TValue, TIndexKey, TQueryBuilder, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey>
	where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> _rangeBuilder;

	public RangeArgNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder) {
		_index = index;
		_rangeBuilder = rangeBuilder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _rangeBuilder, args);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.Range, _index, selector: _rangeBuilder, isParameterized: true));
}

/// <summary>
///   A range whose bounds are each present or absent, handed to the eager core's range dispatch
///   directly (through the <c>(rb, args) =&gt; args</c> pass-through) instead of through the
///   type-state <see cref="RangeQueryBuilder{TIndexKey}" />, which cannot spell "from and/or to" in
///   one lambda. Both bounds absent is <see cref="IsUnbounded" />: the core has no arm for
///   <c>(None, None)</c> (it throws <see cref="System.Diagnostics.UnreachableException" />), so the
///   optional-range narrowers never hand it one — they skip the core call, which is exactly what an
///   eager query that never called the range index does (<c>_first</c> untouched).
/// </summary>
public readonly struct OptionalRange<TIndexKey> : IRangeQueryBuilder<TIndexKey> where TIndexKey : IComparable<TIndexKey> {
	// Cached pass-through: the core's args overload builds the range from (rb, args); here args IS the range.
	internal static readonly Func<RangeQueryBuilder<TIndexKey>, OptionalRange<TIndexKey>, OptionalRange<TIndexKey>> PassThrough = static (_, range) => range;

	private readonly RangeValue<TIndexKey> _from;
	private readonly RangeValue<TIndexKey> _to;

	public OptionalRange(RangeValue<TIndexKey> from, RangeValue<TIndexKey> to) {
		_from = from;
		_to = to;
	}

	public bool IsUnbounded => _from.Type == RangeValueType.None && _to.Type == RangeValueType.None;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static RangeValue<TIndexKey> Bound(bool present, TIndexKey value, bool inclusive)
		=> present ? new(inclusive ? RangeValueType.ThanOrEqual : RangeValueType.Than, value) : default;

	public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l) => (g, l) = (_from, _to);

	public override string ToString() {
		var sb = new StringBuilder();
		sb.Append(_from.Type == RangeValueType.ThanOrEqual ? '[' : '(');
		if (_from.Type == RangeValueType.None) sb.Append("-inf"); else sb.Append(_from.Value);
		sb.Append(", ");
		if (_to.Type == RangeValueType.None) sb.Append("+inf"); else sb.Append(_to.Value);
		return sb.Append(_to.Type == RangeValueType.ThanOrEqual ? ']' : ')').ToString();
	}
}

/// <summary>Optional-bounds range narrowing with the bounds fixed at build time; an unbounded range is a recorded no-op.</summary>
public readonly struct RangeOptionalNarrower<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly OptionalRange<TIndexKey> _range;

	public RangeOptionalNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, in OptionalRange<TIndexKey> range) {
		_index = index;
		_range = range;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (!_range.IsUnbounded)
			core.UseIndexInternal(_index, OptionalRange<TIndexKey>.PassThrough, _range);
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.Range, _index, value: _range));
}

/// <summary>
///   Optional-bounds range narrowing over a value-type key, each bound selected from the execution
///   arguments as a <see cref="Nullable{T}" /> (<c>null</c> = open side). Two delegate calls per
///   execution; both <c>null</c> skips the core (see <see cref="OptionalRange{TIndexKey}" />).
/// </summary>
public readonly struct RangeOptionalArgNarrower<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : struct, IComparable<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, TIndexKey?> _from;
	private readonly Func<TArgs, TIndexKey?> _to;
	private readonly bool _fromInclusive;
	private readonly bool _toInclusive;

	public RangeOptionalArgNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<TArgs, TIndexKey?> from, Func<TArgs, TIndexKey?> to, bool fromInclusive, bool toInclusive) {
		_index = index;
		_from = from;
		_to = to;
		_fromInclusive = fromInclusive;
		_toInclusive = toInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		var from = _from(args);
		var to = _to(args);
		if (!from.HasValue && !to.HasValue)
			return;
		core.UseIndexInternal(_index, OptionalRange<TIndexKey>.PassThrough,
			new OptionalRange<TIndexKey>(OptionalRange<TIndexKey>.Bound(from.HasValue, from.GetValueOrDefault(), _fromInclusive), OptionalRange<TIndexKey>.Bound(to.HasValue, to.GetValueOrDefault(), _toInclusive)));
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.Range, _index, selector: _from, isParameterized: true));
}

/// <summary>The reference-type-key twin of <see cref="RangeOptionalArgNarrower{TKey,TValue,TIndexKey,TArgs}" />: a <c>null</c> reference is the open side.</summary>
public readonly struct RangeOptionalRefArgNarrower<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : class, IComparable<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, TIndexKey?> _from;
	private readonly Func<TArgs, TIndexKey?> _to;
	private readonly bool _fromInclusive;
	private readonly bool _toInclusive;

	public RangeOptionalRefArgNarrower(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<TArgs, TIndexKey?> from, Func<TArgs, TIndexKey?> to, bool fromInclusive, bool toInclusive) {
		_index = index;
		_from = from;
		_to = to;
		_fromInclusive = fromInclusive;
		_toInclusive = toInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		var from = _from(args);
		var to = _to(args);
		if (from is null && to is null)
			return;
		core.UseIndexInternal(_index, OptionalRange<TIndexKey>.PassThrough,
			new OptionalRange<TIndexKey>(OptionalRange<TIndexKey>.Bound(from is not null, from!, _fromInclusive), OptionalRange<TIndexKey>.Bound(to is not null, to!, _toInclusive)));
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.Range, _index, selector: _from, isParameterized: true));
}

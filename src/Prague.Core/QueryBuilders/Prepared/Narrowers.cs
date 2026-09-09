namespace Prague.Core;

using System.Runtime.CompilerServices;
using QueryBuilders;

// Every narrower mirrors one eager entry point and replays through the same
// ICandidatesFilterer method. Selector-based variants hold a delegate created once at build time
// (a static lambda is a cached delegate), so an execution costs one delegate call and no allocation.

/// <summary>Unique (1:1) index equality with a value bound at build time.</summary>
public readonly struct UniqueIndexEq<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>, IPointLookupSource<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly TIndexKey _value;

	public UniqueIndexEq(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, TIndexKey value) {
		_index = index;
		_value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _value);

	public void Describe(List<NarrowerDescriptor> plan)
		=> plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.UniqueEq, _index, _value, null, false, this));

	FrozenQuery<TArgs, TValue> IPointLookupSource<TKey, TValue, TArgs>.CreateFrozen(InMemoryDataCache<TKey, TValue> cache, FilterStep<TValue, TArgs>[] filters, IReadOnlyList<NarrowerDescriptor> narrowers)
		=> new FrozenQuery<TArgs, TValue, PointLookupExecutor<TKey, TValue, TArgs, TIndexKey>>(new(cache, _index, _value, null, filters), narrowers, false, false);
}

/// <summary>Unique (1:1) index equality with the value selected from the execution arguments.</summary>
public readonly struct UniqueIndexEqArg<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>, IPointLookupSource<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, TIndexKey> _selector;

	public UniqueIndexEqArg(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, Func<TArgs, TIndexKey> selector) {
		_index = index;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _selector(args));

	public void Describe(List<NarrowerDescriptor> plan)
		=> plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.UniqueEq, _index, null, _selector, true, this));

	FrozenQuery<TArgs, TValue> IPointLookupSource<TKey, TValue, TArgs>.CreateFrozen(InMemoryDataCache<TKey, TValue> cache, FilterStep<TValue, TArgs>[] filters, IReadOnlyList<NarrowerDescriptor> narrowers)
		=> new FrozenQuery<TArgs, TValue, PointLookupExecutor<TKey, TValue, TArgs, TIndexKey>>(new(cache, _index, default!, _selector, filters), narrowers, false, false);
}

/// <summary>List (1:N) index equality with a value bound at build time.</summary>
public readonly struct ListIndexEq<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly TIndexKey _value;

	public ListIndexEq(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, TIndexKey value) {
		_index = index;
		_value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _value);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.ListEq, _index, _value));
}

/// <summary>List (1:N) index equality with the value selected from the execution arguments.</summary>
public readonly struct ListIndexEqArg<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, TIndexKey> _selector;

	public ListIndexEqArg(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, Func<TArgs, TIndexKey> selector) {
		_index = index;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _selector(args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.ListEq, _index, selector: _selector, isParameterized: true));
}

// Multi-value narrowers hold a ReadOnlyMemory rather than a span because a narrower lives inside the
// heap-resident prepared query. The memory is rooted by that object and only ever read synchronously
// during Replay, so `.Span` at replay time is the same span the eager span overload receives.

/// <summary>Unique (1:1) index membership in a value set bound at build time (empty set = no rows, as eager).</summary>
public readonly struct UniqueIndexIn<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly ReadOnlyMemory<TIndexKey> _values;

	public UniqueIndexIn(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TIndexKey> values) {
		_index = index;
		_values = values;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _values.Span);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.UniqueIn, _index, _values));
}

/// <summary>Unique (1:1) index membership in a value set selected from the execution arguments.</summary>
public readonly struct UniqueIndexInArg<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, ReadOnlyMemory<TIndexKey>> _selector;

	public UniqueIndexInArg(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, Func<TArgs, ReadOnlyMemory<TIndexKey>> selector) {
		_index = index;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _selector(args).Span);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.UniqueIn, _index, selector: _selector, isParameterized: true));
}

/// <summary>List (1:N) index membership in a value set bound at build time (empty set = no rows, as eager).</summary>
public readonly struct ListIndexIn<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly ReadOnlyMemory<TIndexKey> _values;

	public ListIndexIn(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TIndexKey> values) {
		_index = index;
		_values = values;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _values.Span);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.ListIn, _index, _values));
}

/// <summary>List (1:N) index membership in a value set selected from the execution arguments.</summary>
public readonly struct ListIndexInArg<TKey, TValue, TIndexKey, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, ReadOnlyMemory<TIndexKey>> _selector;

	public ListIndexInArg(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, Func<TArgs, ReadOnlyMemory<TIndexKey>> selector) {
		_index = index;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _selector(args).Span);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.ListIn, _index, selector: _selector, isParameterized: true));
}

/// <summary>
///   List (1:N) index membership over a bound set of foreign values, each projected to an index key at
///   replay by <c>keySelector</c> — the eager <c>UseIndex(listIndex, ReadOnlySpan&lt;TOtherValue&gt;, keySelector)</c> shape.
/// </summary>
public readonly struct ListIndexInProjected<TKey, TValue, TIndexKey, TOtherValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly ReadOnlyMemory<TOtherValue> _values;
	private readonly Func<TOtherValue, TIndexKey> _keySelector;

	public ListIndexInProjected(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TOtherValue> values, Func<TOtherValue, TIndexKey> keySelector) {
		_index = index;
		_values = values;
		_keySelector = keySelector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _values.Span, _keySelector);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.ListInProjected, _index, _values, _keySelector));
}

/// <summary>Key-set (predicate) index: the index itself is the whole description, nothing to bind.</summary>
public readonly struct KeySetNarrower<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly CacheKeySetIndex<TKey, TValue> _index;

	public KeySetNarrower(CacheKeySetIndex<TKey, TValue> index) => _index = index;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.KeySet, _index));
}

/// <summary>
///   Value predicate bound at build time; replays through the eager <c>WhereInternal</c> unchanged.
///   For a predicate over the execution arguments see <see cref="FilterArgNarrower{TKey,TValue,TArgs}" />.
/// </summary>
public readonly struct FilterNarrower<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly Predicate<TValue> _predicate;

	public FilterNarrower(Predicate<TValue> predicate) => _predicate = predicate;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.WhereInternal(_predicate);

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForFilter(_predicate, false));
}

/// <summary>
///   Value predicate over the row and the execution arguments. The eager core's filter slot is a
///   <see cref="Predicate{T}" />, so <c>args</c> has to be bound into one per execution; binding it
///   through a closure would allocate on every run. Instead the narrower rents a per-thread pooled
///   <see cref="ArgPredicate{TValue,TArgs}" /> box whose predicate was created once, so an execution
///   costs one extra delegate hop and no allocation. The box stays bound until the owning execution
///   resets the pool, which is after the core has finished executing (see <see cref="ArgPredicatePool{TValue,TArgs}" />).
/// </summary>
public readonly struct FilterArgNarrower<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly Func<TValue, TArgs, bool> _predicate;

	public FilterArgNarrower(Func<TValue, TArgs, bool> predicate) => _predicate = predicate;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.WhereInternal(ArgPredicatePool<TValue, TArgs>.Rent(_predicate, in args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForFilter(_predicate, true));
}

namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Utils;

/// <summary>
///   A narrower that can seed the point-lookup executor: the unique-index equality steps. The
///   narrower knows its <c>TIndexKey</c>, so it — not the planner — constructs the closed executor and
///   frozen query; the planner only type-tests the descriptor's source against this interface.
/// </summary>
internal interface IPointLookupSource<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TArgs : struct {
	FrozenQuery<TArgs, TValue> CreateFrozen(InMemoryDataCache<TKey, TValue> cache, FilterStep<TValue, TArgs>[] filters, IReadOnlyList<NarrowerDescriptor> narrowers);
}

/// <summary>
///   One filter after the unique step, unboxed at build. A constant predicate is called directly; a
///   parameterized one is called with the execution arguments directly — no <see cref="ArgPredicatePool{TValue,TArgs}" />
///   box on this path, because the executor applies its own filters instead of handing a
///   <see cref="Predicate{T}" /> to the eager core.
/// </summary>
internal readonly struct FilterStep<TValue, TArgs>
	where TArgs : struct {
	private readonly Predicate<TValue>? _constant;
	private readonly ArgFilter<TValue, TArgs>? _arg;

	internal FilterStep(Predicate<TValue> constant) => _constant = constant;

	internal FilterStep(ArgFilter<TValue, TArgs> arg) => _arg = arg;

	internal Predicate<TValue>? Constant => _constant;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool Passes(TValue value, in TArgs args) => _constant is not null ? _constant(value) : _arg!(value, args);
}

/// <summary>
///   The point-lookup fast path: <c>UseIndex(unique, key)</c> followed by zero or more <c>Where</c>s.
///   Two dictionary probes (index → entity key → value) and the filters in order, then a
///   <see cref="QueryResults{T}" /> of 0 or 1 rows built exactly as the eager
///   <c>SimpleResultContainer</c> would build it from a one-candidate set: same <c>TotalCount</c>,
///   same <c>skip</c> / <c>take</c> slicing, same pooled array source, same clone timing. What it
///   skips is the eager core itself — no rented <c>ValueSet</c>, no candidate insert, no set walk, no
///   composed filter closure, no per-execution predicate box.
/// </summary>
internal readonly struct PointLookupExecutor<TKey, TValue, TArgs, TIndexKey> : IFrozenExecutor<TArgs, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull
	where TArgs : struct {
	private readonly InMemoryDataCache<TKey, TValue> _cache;
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly TIndexKey _value;
	private readonly Func<TArgs, TIndexKey>? _selector;
	private readonly FilterStep<TValue, TArgs>[] _filters;

	internal PointLookupExecutor(InMemoryDataCache<TKey, TValue> cache, CacheKeyValueIndex<TKey, TValue, TIndexKey> index, TIndexKey value,
		Func<TArgs, TIndexKey>? selector, FilterStep<TValue, TArgs>[] filters) {
		_cache = cache;
		_index = index;
		_value = value;
		_selector = selector;
		_filters = filters;
	}

	public static string Name => "PointLookup";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool TryLookup(in TArgs args, [MaybeNullWhen(false)] out TValue value) {
		var key = _selector is null ? _value : _selector(args);
		if (!_index.TryGetValue(key, out var entityKey) || !_cache.TryGet(entityKey, out value)) {
			value = default;
			return false;
		}

		var filters = _filters;
		for (var i = 0; i < filters.Length; i++)
			if (!filters[i].Passes(value, in args))
				return false;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<TValue> Execute(in TArgs args, bool pool, bool clone, int skip, int take)
		=> TryLookup(in args, out var value) ? SingleRow(value, pool, clone, skip, take) : QueryResults<TValue>.Empty;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Count(in TArgs args) => TryLookup(in args, out _) ? 1 : 0;

	// SimpleResultContainer for a one-row match, step for step: Init(1) rents / allocates a
	// one-slot buffer from the same source, Add clones on the way in when no slice is requested,
	// BuildResults returns the total-only empty result when skip is past the end, slices with the
	// total kept otherwise, and clones the surviving page after slicing.
	private static QueryResults<TValue> SingleRow(TValue value, bool pool, bool clone, int skip, int take) {
		if (skip > 1)
			return QueryResults<TValue>.EmptyWithTotalCount(1);
		var shouldSlice = skip > 0 || take < int.MaxValue;
		var results = new QueryResults<TValue>(1, pool);
		results.UnsafeAdd(clone && !shouldSlice ? value.Clone() : value);
		if (!shouldSlice)
			return results;
		results.SliceLeaveTotalCount(skip, Math.Min(take, 1 - skip));
		return clone ? results.CloneInPlace() : results;
	}
}

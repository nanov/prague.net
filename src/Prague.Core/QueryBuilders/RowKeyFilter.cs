namespace Prague.Core;

using System.Runtime.CompilerServices;
using Prague.Core.Collections;

// ── Inner post-walk by key ────────────────────────────────────────────────────
//
// An inner JoinOne drops the rows it did not match. The slot-based post-walks (RetainNonNullSlots,
// PruneNullSlots) read "not matched" as a null slot, which a struct right value never has: a default struct
// is not null, so a chained inner join or the fused frozen fill kept every row of a struct right. A struct
// right's inner join therefore records what it matched (or missed) by left key and filters the rows by key —
// IUnsafeValueAccessor.RetainRows / PruneRows with one of the predicates below. The resolvers pick it with
// typeof(TRightValue).IsValueType, a JIT constant: a reference right keeps the slot test and pays nothing.

/// <summary>Decides, by its left key, whether a result row survives an inner post-walk.</summary>
internal interface IRowKeyPredicate<in TKey> {
	bool Keep(TKey key);
}

/// <summary>Adapts an <see cref="IRowKeyPredicate{TKey}"/> to the result map's filter (the accessor reinterprets the key type).</summary>
internal struct RowKeyFilter<TKey, TJoinResult, TKey1, TKeep>
	: ValueDictionary<TKey, TJoinResult, DefaultKeyComparer<TKey>>.IValueDictionaryFilter<TKey, TJoinResult>
	where TKey : IEquatable<TKey>
	where TKeep : struct, IRowKeyPredicate<TKey1> {
	// Mutable on purpose: Keep is called on the filter the map holds by value, so a non-readonly field
	// keeps the predicate (a map or set copy) from being copied defensively per row.
	private TKeep _keep;

	public RowKeyFilter(TKeep keep) {
		_keep = keep;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Keep(TKey key, ref TJoinResult value) => _keep.Keep(Unsafe.As<TKey, TKey1>(ref key));
}

/// <summary>No row survives: an inner join that matched nothing.</summary>
internal readonly struct KeepNoRows<TKey> : IRowKeyPredicate<TKey> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Keep(TKey key) => false;
}

/// <summary>The rows whose key is in the match map survive — the lefts an inner execute delivered to.</summary>
internal struct KeepRowsInMap<TKey, TValue> : IRowKeyPredicate<TKey>
	where TKey : notnull, IEquatable<TKey> {
	// A copy of the map shares its storage; it is only read here and never disposed.
	private ValueDictionary<TKey, TValue, DefaultKeyComparer<TKey>> _map;

	public KeepRowsInMap(ValueDictionary<TKey, TValue, DefaultKeyComparer<TKey>> map) {
		_map = map;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Keep(TKey key) => _map.TryGetValue(key, out _);
}

/// <summary>The rows whose key is in the miss set are dropped — the fused fill's lefts without a right.</summary>
internal struct DropRowsInSet<TKey> : IRowKeyPredicate<TKey>
	where TKey : notnull, IEquatable<TKey> {
	// A copy of the set shares its storage; it is only read here and never disposed.
	private ValueSet<TKey, DefaultKeyComparer<TKey>> _set;

	public DropRowsInSet(ValueSet<TKey, DefaultKeyComparer<TKey>> set) {
		_set = set;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Keep(TKey key) => !_set.Contains(key);
}

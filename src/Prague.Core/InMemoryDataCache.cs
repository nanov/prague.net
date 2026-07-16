namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

public interface ICountableCacheIndex {
	public ulong GetCounters(out ulong values);
}

public interface ICacheIndex<in TKey, in TValue> {
	internal void Add(TKey key, TValue value, long timestampMs);
	internal void Remove(TKey key, TValue value, long timestampMs);
	internal void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs);
}

public abstract class CacheKeyValueIndex<TKey, TValue, TIndexKey>
	where TIndexKey : notnull
	where TKey : notnull {
	public abstract bool TryGetValue(TIndexKey key, [MaybeNullWhen(false)] out TKey value);

	/// <summary>
	///   Checks if the index contains a value for the given key.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(TIndexKey key) => TryGetValue(key, out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue<TJoinedKey>(TJoinedKey joinedKey, TIndexKey key,
		ValueSet<JoinedKeyPair<TJoinedKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TJoinedKey, TKey>>> target, bool add)
		where TJoinedKey : notnull {
		if (!TryGetValue(key, out var value)) {
			target.Clear();
			return;
		}

		if (add)
			target.Add(JoinedKeyPair.Create(joinedKey, value));
		else
			target.IntersectWith(JoinedKeyPair.Create(joinedKey, value));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue(TIndexKey key, ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target, bool add) {
		if (!TryGetValue(key, out var value)) {
			target.Clear();
			return;
		}

		if (add)
			target.Add(JoinedKeyPair.Create(key, value));
		else
			target.IntersectWith(JoinedKeyPair.Create(key, value));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue<TJoinedKey>(TIndexKey key, ref ValueSet<JoinedKeyPair<TJoinedKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TJoinedKey, TKey>>> target)
		where TJoinedKey : notnull {
		if (!TryGetValue(key, out var value)) {
			target.Clear();
			return;
		}

		target.IntersectWith(JoinedKeyPair.Create<TJoinedKey, TKey>(value));
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		if (!TryGetValue(key, out var value)) {
			target.Clear();
			return;
		}

		if (add)
			target.Add(value);
		else
			target.IntersectWith(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal void IntersectValues(ICollection<TIndexKey> keys, ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target,
		bool add) {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var key in keys)
				if (TryGetValue(key, out var value))
					target.Add(JoinedKeyPair.Create(key, value));
			return;
		}

		// When add=false, we need to intersect with existing candidates
		using var intersecter =
			new ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>>.IncrementalIntersecter(ref target,
				stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
		foreach (var key in keys)
			if (TryGetValue(key, out var value))
				intersecter.IntersectWith(JoinedKeyPair.Create(key, value));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		if (intersecter.IsCleared) return;
		var found = false;
		foreach (var key in keys)
			if (TryGetValue(key, out var value)) {
				intersecter.IntersectWith(value);
				found = true;
			}
		if (!found)
			intersecter.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var key in keys)
				if (TryGetValue(key, out var value))
					target.Add(value);
			return;
		}

		// When add=false, we need to intersect with existing candidates
		using var intersecter =
			new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref target, stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
		foreach (var key in keys)
			if (TryGetValue(key, out var value))
				intersecter.IntersectWith(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal virtual void IntersectValues<TContainer>(
		ReadOnlySpan<TIndexKey> values,
		ref TContainer container,
		bool add
	) where TContainer : struct, IJoinedResultContainer<TIndexKey, TKey>, allows ref struct {
		if (add) {
			foreach (var val in values) {
				if (TryGetValue(val, out var value))
					container.Add(val, value);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal virtual void IntersectValues<TContainer>(
		ref ValueSet<TIndexKey, DefaultKeyComparer<TIndexKey>> values,
		ref TContainer container,
		bool add
	) where TContainer : struct, IJoinedResultContainer<TIndexKey, TKey>, allows ref struct {
		if (add) {
			foreach (var val in values) {
				if (TryGetValue(val, out var value))
					container.Add(val, value);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal virtual void IntersectValues(
		ref ValueSet<TIndexKey, DefaultKeyComparer<TIndexKey>> values,
		ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target,
		bool add) {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var val in values) {
				if (TryGetValue(val, out var value))
					target.Add(JoinedKeyPair.Create<TIndexKey, TKey>(val, value));
			}
		}
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal virtual void IntersectValues<TOtherKey>(ReadOnlySpan<TOtherKey> values, Func<TOtherKey, TIndexKey> keySelector,
		ref ValueSet<JoinedKeyPair<TOtherKey, TIndexKey>, DefaultKeyComparer<JoinedKeyPair<TOtherKey, TIndexKey>>> target,
		bool add) where TOtherKey : notnull {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var val in values) {
				var key = keySelector(val);
				if (TryGetValue(key, out var value))
					target.Add(JoinedKeyPair.Create<TOtherKey, TIndexKey>(val, key));
			}
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal virtual void IntersectValues<TOtherKey>(ReadOnlySpan<TIndexKey> values, Func<TKey, TOtherKey> keySelector,
		ref ValueSet<JoinedKeyPair<TIndexKey, TOtherKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TOtherKey>>> target,
		bool add) where TOtherKey : notnull {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var val in values) {
				if (TryGetValue(val, out var value))
					target.Add(JoinedKeyPair.Create<TIndexKey, TOtherKey>(val, keySelector(value)));
			}
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TOtherValue>(ReadOnlySpan<TOtherValue> values, Func<TOtherValue, TIndexKey> keySelector,
		ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target,
		bool add) {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var val in values) {
				var key = keySelector(val);
				if (TryGetValue(key, out var value))
					target.Add(JoinedKeyPair.Create<TIndexKey, TKey>(key, value));
			}

			return;
		}
	}

	/// <summary>
	/// Bulk intersect with a struct-dispatched key selector. The selector transforms each
	/// source value to a <typeparamref name="TIndexKey"/>; resulting pairs store the
	/// <b>original source</b> as <c>JoinedKey</c> (not the transformed index key), preserving
	/// caller identity through the join. JIT devirtualizes per closed generic — the per-key
	/// delegate call is inlined away; <see cref="IdentitySelector{T}"/> overloads fold to the
	/// no-selector identity path.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TOtherValue, TSelector>(
		ReadOnlySpan<TOtherValue> values,
		TSelector selector,
		ref ValueSet<JoinedKeyPair<TOtherValue, TKey>, DefaultKeyComparer<JoinedKeyPair<TOtherValue, TKey>>> target,
		bool add)
		where TOtherValue : notnull
		where TSelector : struct, IKeySelector<TOtherValue, TIndexKey> {
		if (add) {
			foreach (var val in values) {
				var indexKey = selector.Select(val);
				if (TryGetValue(indexKey, out var value))
					target.Add(JoinedKeyPair.Create<TOtherValue, TKey>(val, value));
			}
		}
	}

	/// <summary>
	/// Bulk intersect with a <b>tail key selector</b> applied to the index's lookup result.
	/// Two-step chain: <c>(input) → TryGetValue → TKey → tailSelector.Select → TOutKey</c>.
	/// Pair: <c>(input, TOutKey)</c> — input is preserved as <c>JoinedKey</c>.
	/// Used by resolvers where the index returns an intermediate key that the caller's selector
	/// then transforms into the final right-side key (e.g. <c>JoinOneLeftUniqueIndexResolver</c>'s
	/// <c>Reverse[leftKey] → idx → Selector.Select(idx) → rightKey</c>).
	/// JIT-devirtualizes the tail selector per closed generic.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesChain<TOutKey, TSelector>(
		ReadOnlySpan<TIndexKey> values,
		TSelector tailSelector,
		ref ValueSet<JoinedKeyPair<TIndexKey, TOutKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TOutKey>>> target,
		bool add)
		where TOutKey : notnull
		where TSelector : struct, IKeySelector<TKey, TOutKey> {
		if (add) {
			foreach (var val in values) {
				if (TryGetValue(val, out var inner))
					target.Add(JoinedKeyPair.Create<TIndexKey, TOutKey>(val, tailSelector.Select(inner)));
			}
		}
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target,
		bool add) {
		if (add) {
			// When add=true, we're building the initial set, just add all values
			foreach (var key in keys)
				if (TryGetValue(key, out var value))
					target.Add(JoinedKeyPair.Create<TIndexKey, TKey>(key, value));
			return;
		}

		// When add=false, we need to intersect with existing candidates
		using var intersecter =
			new ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>>.IncrementalIntersecter(ref target,
				stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
		foreach (var key in keys)
			if (TryGetValue(key, out var value))
				intersecter.IntersectWith(JoinedKeyPair.Create(key, value));
	}

	/// <summary>
	/// <see cref="ValueSet{T}"/>-sourced struct-selector overload. Companion to the
	/// identity <c>IntersectValues(ref ValueSet&lt;TIndexKey&gt;, ...)</c> already defined
	/// above; struct-dispatched selector transforms each source value to
	/// <typeparamref name="TIndexKey"/>, then probes the index. Used by
	/// <c>InnerJoinOne</c> selector paths to avoid resolver-side iteration —
	/// the loop body lives here, JIT-devirtualized per closed generic.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TOtherValue, TSelector>(
		ref ValueSet<TOtherValue, DefaultKeyComparer<TOtherValue>> values,
		TSelector selector,
		ref ValueSet<JoinedKeyPair<TOtherValue, TKey>, DefaultKeyComparer<JoinedKeyPair<TOtherValue, TKey>>> target,
		bool add)
		where TOtherValue : notnull
		where TSelector : struct, IKeySelector<TOtherValue, TIndexKey> {
		if (add) {
			foreach (var val in values) {
				var indexKey = selector.Select(val);
				if (TryGetValue(indexKey, out var value))
					target.Add(JoinedKeyPair.Create<TOtherValue, TKey>(val, value));
			}
		}
	}

	/// <summary>
	/// <see cref="ValueSet{T}"/>-sourced tail-selector chain overload — companion to
	/// the <see cref="ReadOnlySpan{T}"/> <c>IntersectValuesChain</c> above. Used by
	/// <c>InnerJoinOne</c> on left-unique-index resolvers where the input candidate
	/// set is a <see cref="ValueSet{T}"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesChain<TOutKey, TSelector>(
		ref ValueSet<TIndexKey, DefaultKeyComparer<TIndexKey>> values,
		TSelector tailSelector,
		ref ValueSet<JoinedKeyPair<TIndexKey, TOutKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TOutKey>>> target,
		bool add)
		where TOutKey : notnull
		where TSelector : struct, IKeySelector<TKey, TOutKey> {
		if (add) {
			foreach (var val in values) {
				if (TryGetValue(val, out var inner))
					target.Add(JoinedKeyPair.Create<TIndexKey, TOutKey>(val, tailSelector.Select(inner)));
			}
		}
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		if (!TryGetValue(key, out var value)) {
			intersecter.Clear();
			return;
		}

		intersecter.IntersectWith(value);
	}

	// Prune-phase counterpart of IntersectValue for chained UseIndex within an Or branch:
	// keeps only bits whose slot's key == this index's value for `key`. 1:1 index → at most one survivor.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void RetainOnly(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		if (!TryGetValue(key, out var value)) {
			intersecter.Clear();
			return;
		}

		intersecter.RetainOnly(value);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TJoinKey>(ReadOnlySpan<TIndexKey> keys,
		ref ValueSet<JoinedKeyPair<TJoinKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TJoinKey, TKey>>> target)
		where TJoinKey : notnull {
		using var intersecter =
			new ValueSet<JoinedKeyPair<TJoinKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TJoinKey, TKey>>>.IncrementalIntersecter(ref target,
				stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
		foreach (var key in keys)
			if (TryGetValue(key, out var value))
				intersecter.IntersectWith(JoinedKeyPair.Create<TJoinKey, TKey>(value));
	}
}

public sealed class CacheSymmetricUniqueIndex<TKey, TValue, TIndexKey> : CacheUniqueIndex<TKey, TValue, TIndexKey>
	where TIndexKey : notnull
	where TKey : notnull {
	public CacheUniqueIndex<TIndexKey, TKey, TKey> Reverse => _cacheReverse;
	public CacheSymmetricUniqueIndex(Func<TKey, TValue, TIndexKey> keySelector) : base(keySelector, true) {
	}
}

public class CacheUniqueIndex<TKey, TValue, TIndexKey> : CacheKeyValueIndex<TKey, TValue, TIndexKey>,
	ICacheIndex<TKey, TValue>, ICountableCacheIndex
	where TIndexKey : notnull
	where TKey : notnull {
	private readonly ConcurrentCacheStore<TIndexKey, TKey> _cache = new();
	private readonly Func<TKey, TValue, TIndexKey> _keySelector;

	protected readonly CacheUniqueIndex<TIndexKey, TKey, TKey> _cacheReverse = null!;

	protected CacheUniqueIndex(Func<TKey, TValue, TIndexKey> keySelector, bool reverse = false) {
		_keySelector = keySelector;
		if (reverse) {
			_cacheReverse = new CacheUniqueIndex<TIndexKey, TKey, TKey>(static (_, value) => value);
		}
	}

	public CacheUniqueIndex(Func<TKey, TValue, TIndexKey> keySelector):this(keySelector, false) {
	}

	/// <summary>
	///   Gets the approximate count of items in this index. This is an O(1) operation.
	///   Note: Due to concurrent operations, this count may be slightly inaccurate at any given moment.
	/// </summary>
	public ulong ApproximateCount { get; private set; }

	public void Update(TKey key, TValue orginialValue, TValue newValue, long timestampMs) {
		var oldIndexKey = _keySelector(key, orginialValue);
		var indexKey = _keySelector(key, newValue);
		// same key, no work
		if (oldIndexKey.Equals(indexKey))
			return;

		// Add to the new index first to ensure the key is always visible
		var r = _cache.AddOrUpdate(indexKey, key, static (_, _, _) => true);
		_cacheReverse?.Add(indexKey, key, timestampMs);

		var sizeDelta = 0UL;
		if (r.Operation is AddOrUpdateOperation.Add)
			sizeDelta = 1;

		// Then remove from the old index — but only if the slot still belongs to THIS
		// entity. A blind remove drops a live mapping when another entity has meanwhile
		// taken the old index key (key-swap between two entities).
		if (_cache.TryRemove(new KeyValuePair<TIndexKey, TKey>(oldIndexKey, key)))
			sizeDelta--;

		ApproximateCount += sizeDelta;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		var indexKey = _keySelector(key, value);
		var r = _cache.AddOrUpdate(indexKey, key, static (_, _, _) => true);
		_cacheReverse?.Add(indexKey, key, timestampMs);

		var sizeDelta = 0UL;
		if (r.Operation is AddOrUpdateOperation.Add)
			sizeDelta = 1;

		ApproximateCount += sizeDelta;
	}

	public void Remove(TKey key, TValue orginialValue, long timestampMs) {
		var indexKey = _keySelector(key, orginialValue);
		_cacheReverse?.Remove(indexKey, key, timestampMs); //TryRemove(key, out _);
		// Value-conditional: if another entity has meanwhile taken this index key
		// (forward slot clobbered by its Add/Update), its live mapping must survive.
		if (_cache.TryRemove(new KeyValuePair<TIndexKey, TKey>(indexKey, key)))
			ApproximateCount--;
	}

	public ulong GetCounters(out ulong vlaues) {
		vlaues = ApproximateCount;
		return ApproximateCount;
	}

	public override bool TryGetValue(TIndexKey key, [MaybeNullWhen(false)] out TKey value) {
		return _cache.TryGetValue(key, out value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal override void IntersectValues<TContainer>(
		ReadOnlySpan<TIndexKey> values,
		ref TContainer container,
		bool add
	) {
		if (add) {
			_cache.TryGetValues(ref container, values);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal override void IntersectValues<TContainer>(
		ref ValueSet<TIndexKey, DefaultKeyComparer<TIndexKey>> values,
		ref TContainer container,
		bool add
		) {
		if (add) {
			_cache.TryGetValues(ref container, ref values);
		}
	}


	/// <summary>
	///   Checks if the index contains a value for the given key.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public new bool ContainsKey(TIndexKey key) => _cache.TryGetValue(key, out _);
}

public sealed class CachePrimaryKeyIndex<TKey, TValue> : CacheKeyValueIndex<TKey, TValue, TKey>
	where TKey : notnull, IEquatable<TKey>, IComparable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly InMemoryDataCache<TKey, TValue> _cache;

	internal CachePrimaryKeyIndex(InMemoryDataCache<TKey, TValue> cache) {
		_cache = cache;
	}


	public override bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TKey value) {
		// For primary key index, if the key exists in cache, return the key itself
		if (_cache.TryGet(key, out _)) {
			value = key;
			return true;
		}

		value = default;
		return false;
	}
}



public sealed class CacheRangeIndex<TKey, TValue, TIndexKey> : ICacheIndex<TKey, TValue>, ICountableCacheIndex
	where TIndexKey : IComparable<TIndexKey>
	where TKey : IEquatable<TKey>, IComparable<TKey> {
	private readonly PooledBTree<TIndexKey, TKey> _index;
	private readonly Func<TKey, TValue, TIndexKey> _keySelector;

	public CacheRangeIndex(Func<TKey, TValue, TIndexKey> keySelector) {
		_index = new PooledBTree<TIndexKey, TKey>();
		_keySelector = keySelector;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		_index.Add(_keySelector(key, value), key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key, TValue value, long timestamp) {
		_index.Remove(_keySelector(key, value), key);
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var oldIndexKey = _keySelector(key, originalValue);
		var newIndexKey = _keySelector(key, newValue);
		// CompareTo instead of Equals: on a constrained type parameter Equals binds to
		// object.Equals and boxes the argument on every cache update; the constrained
		// CompareTo call devirtualizes and is allocation-free.
		if (oldIndexKey.CompareTo(newIndexKey) == 0)
			return;

		// Single locked move (insert-before-remove inside): one write-lock round-trip
		// instead of two. Size doesn't change on update (removing one, adding one).
		_index.Update(oldIndexKey, newIndexKey, key);
	}

	public ulong GetCounters(out ulong vlaues) {
		// Report the real B-tree size, not a logically-maintained counter: a divergence
		// between the two is exactly how stale (leaked) index entries manifest.
		vlaues = (ulong)_index.Length;
		return (ulong)_index.Length;
	}

	public IReadOnlyCollection<TKey> GetValuesGte(TIndexKey lowerValue) {
		var agg = new ListResultsAggregator();
		_index.RangeFrom(lowerValue, ref agg);
		return agg.Values;
	}

	public IReadOnlyCollection<TKey> GetValuesLte(TIndexKey upperValue) {
		var agg = new ListResultsAggregator();
		_index.RangeTo(upperValue, ref agg);
		return agg.Values;
	}

	public IReadOnlyCollection<TKey> GetValuesGt(TIndexKey lowerValue) {
		var agg = new ListResultsAggregator();
		_index.RangeFromExclusive(lowerValue, ref agg);
		return agg.Values;
	}

	public IReadOnlyCollection<TKey> GetValuesLt(TIndexKey upperValue) {
		var agg = new ListResultsAggregator();
		_index.RangeToExclusive(upperValue, ref agg);
		return agg.Values;
	}

	public IReadOnlyCollection<TKey> GetValuesBetween(TIndexKey lowerValue, TIndexKey upperValue) {
		var agg = new ListResultsAggregator();
		_index.Range(lowerValue, upperValue, ref agg);
		return agg.Values;
	}

	public IReadOnlyCollection<TKey> GetValuesBetween(TIndexKey lowerValue, TIndexKey upperValue, bool includeFrom,
		bool includeTo) {
		var agg = new ListResultsAggregator();
		_index.RangeCustom(lowerValue, upperValue, includeFrom, includeTo, ref agg);
		return agg.Values;
	}

	internal void GetValuesBetween<TAgg>(TIndexKey lowerValue, TIndexKey upperValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct {
		_index.Range(lowerValue, upperValue, ref agg);
	}

	internal void GetValuesLte<TAgg>(TIndexKey upperValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct {
		_index.RangeTo(upperValue, ref agg);
	}

	internal void GetValuesGte<TAgg>(TIndexKey lowerValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct {
		_index.RangeFrom(lowerValue, ref agg);
	}

	internal void GetValuesGt<TAgg>(TIndexKey lowerValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct {
		_index.RangeFromExclusive(lowerValue, ref agg);
	}

	internal void GetValuesLt<TAgg>(TIndexKey upperValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct {
		_index.RangeToExclusive(upperValue, ref agg);
	}

	/// <summary>
	///   Gets the minimum index key and its associated value key.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetMin(out TIndexKey indexKey, out TKey valueKey) {
		return _index.TryGetMin(out indexKey, out valueKey);
	}

	/// <summary>
	///   Gets the maximum index key and its associated value key.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetMax(out TIndexKey indexKey, out TKey valueKey) {
		return _index.TryGetMax(out indexKey, out valueKey);
	}

	private readonly struct ListResultsAggregator : PooledBTree<TIndexKey, TKey>.IResultAggregator {
		public readonly List<TKey> Values;

		public ListResultsAggregator() {
			Values = new List<TKey>(10);
		}

		public void Add(TIndexKey index, TKey value) {
			Values.Add(value);
		}

		public void Dispose() {
		}
	}
}

/// <summary>
///   A key set index that tracks which keys satisfy a predicate (typically "has value" / "is not null").
///   Only keys where the predicate returns true are stored in the index.
/// </summary>
public sealed class CacheKeySetIndex<TKey, TValue> : ICacheIndex<TKey, TValue>, ICountableCacheIndex
	where TKey : notnull, IEquatable<TKey>, IComparable<TKey> {
	private readonly PooledSet<TKey, DefaultKeyComparer<TKey>> _keys = new();
	private readonly Func<TKey, TValue, bool> _predicate;
	private readonly object _lock = new();

	public CacheKeySetIndex(Func<TKey, TValue, bool> predicate) {
		_predicate = predicate;
	}

	/// <summary>
	///   Gets the approximate count of keys in this index.
	/// </summary>
	public ulong ApproximateCount => (ulong)_keys.Count;

	/// <summary>
	///   Checks if the given key is in the index (i.e., satisfies the predicate).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(TKey key) {
		lock (_lock) {
			return _keys.Contains(key);
		}
	}

	internal void  AddKeyTo(ref ValueSet<TKey, DefaultKeyComparer<TKey>> target) {
		lock (_lock) {
			foreach (var key in _keys) target.Add(key);  // ref-struct enumerator, no alloc
		}
	}

	/// <summary>
	/// Intersects a paired <see cref="ValueSet{T}"/> of <see cref="JoinedKeyPair{TLeft,TKey}"/>
	/// directly against the index's stored key set — no temp allocation. Caller's pair set
	/// is mutated in place; pairs whose <c>.Key</c> is not in this index are removed.
	/// Lock-protected because the underlying <see cref="PooledSet{T}"/> may be concurrently mutated.
	/// </summary>
	internal void IntersectWithPaired<TLeft>(ref ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>> target)
		where TLeft : notnull {
		lock (_lock) {
			target.IntersectWith<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
				JoinedKeyPair<TLeft, TKey>.Into, _keys);
		}
	}

	internal void IntersectKeysWith(ref ValueSet<TKey, DefaultKeyComparer<TKey>> target) {
		lock (_lock) {
			target.IntersectWith(_keys);                 // uses PooledSet<T> overload
		}
	}

	internal void IntersectKeysWith(ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		lock (_lock) {
			intersecter.IntersectWith(_keys);            // marks slots in intersecter's _self
		}
	}

	internal void IntersectKeysWithPaired<TLeft>(ref ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> intersecter)
		where TLeft : notnull {
		lock (_lock) {
			intersecter.IntersectWith(_keys);            // marks slots whose .Key ∈ _keys
		}
	}
	/// <summary>
	///   Gets all keys currently in the index.
	/// </summary>
	public IReadOnlyCollection<TKey> GetKeys() {
		lock (_lock) {
			return _keys.ToArray();
		}
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		if (_predicate(key, value)) {
			lock (_lock) {
				_keys.Add(key);
			}
		}
	}

	public void Remove(TKey key, TValue originalValue, long timestampMs) {
		// Only remove if the predicate was true (key might be in the set)
		if (_predicate(key, originalValue)) {
			lock (_lock) {
				_keys.Remove(key);
			}
		}
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var wasInSet = _predicate(key, originalValue);
		var shouldBeInSet = _predicate(key, newValue);

		if (wasInSet == shouldBeInSet)
			return;

		lock (_lock) {
			if (shouldBeInSet)
				_keys.Add(key);
			else
				_keys.Remove(key);
		}
	}

	public ulong GetCounters(out ulong values) {
		values = ApproximateCount;
		return ApproximateCount;
	}
}

public sealed class LastUpdatedIndex<TKey> where TKey : IEquatable<TKey>, IComparable<TKey> {
	private readonly ConcurrentCacheStore<TKey, (long Value, int Count)> _cache = new();
	private readonly CacheRangeIndex<TKey, (long Value, int Count), long> _rangeIndex;

	public LastUpdatedIndex() {
		_rangeIndex = new CacheRangeIndex<TKey, (long Value, int Count), long>(static (k, v) => v.Value);
	}

	// Conversion helpers
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long ToUnixMs(DateTimeOffset dt) {
		return dt.ToUnixTimeMilliseconds();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long ToUnixMs(DateTime dt) {
		return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
	}

	public void Add(TKey key, long timestampUpdateMs) {
		Add(key, timestampUpdateMs, timestampUpdateMs);
	}

	public void Add(TKey key, long timestampMs, long timestampUpdateMs) {
		var r = _cache.AddOrUpdate(key,
			static (_, v) => (v, 1), // factory: new entry with count=1
			static (_, existing, v) => v > existing.Value
				? (v, existing.Count + 1) // update value, increment count
				: (existing.Value, existing.Count + 1), // keep value, increment count
			timestampUpdateMs);

		switch (r.Operation) {
			case AddOrUpdateOperation.Add:
				_rangeIndex.Add(key, r.Value, timestampUpdateMs);
				return;
			case AddOrUpdateOperation.Update:
				_rangeIndex.Update(key, r.OldValue!, r.Value, timestampUpdateMs);
				return;
			default:
				return;
		}
	}


	public void Update(TKey key, long timestampMs) {
		Update(key, timestampMs, timestampMs);
	}

	public void Update(TKey key, long timestampMs, long timestampUpdateMs) {
		var r = _cache.AddOrUpdate(key,
			static (_, v) => (v, 1), // factory: new entry with count=1 (shouldn't happen normally)
			static (_, existing, v) => v > existing.Value
				? (v, existing.Count) // update value, keep count
				: (existing.Value, existing.Count), // keep value, keep count
			timestampMs);

		switch (r.Operation) {
			case AddOrUpdateOperation.Add:
				_rangeIndex.Add(key, r.Value, timestampUpdateMs);
				return;
			case AddOrUpdateOperation.Update:
				_rangeIndex.Update(key, r.OldValue!, r.Value, timestampUpdateMs);
				return;
			default:
				return;
		}
	}

	public void Remove(TKey key, long updateTimestampMs) {
		var result = _cache.UpdateOrRemove(key,
			static (_, existing, newTs) => existing.Count > 1
				? (true, (newTs, existing.Count - 1)) // decrement count, update timestamp
				: (false, default), // signal removal
			updateTimestampMs);

		switch (result.Operation) {
			case UpdateOrRemoveOperation.Update:
				_rangeIndex.Update(key, result.OldValue, result.NewValue, updateTimestampMs);
				break;
			case UpdateOrRemoveOperation.Remove:
				_rangeIndex.Remove(key, result.OldValue, updateTimestampMs);
				break;
		}
	}

	public int GetEntitiesCount(TKey key) {
		return _cache.TryGetValue(key, out var entry)
			? entry.Count
			: 0;
	}

	public bool TryGetLastUpdated(TKey key, out long timestampMs) {
		if (_cache.TryGetValue(key, out var entry)) {
			timestampMs = entry.Value;
			return true;
		}

		timestampMs = default;
		return false;
	}

	/// <summary>
	///   Gets the maximum (newest) last-updated timestamp across the given group keys. Keys absent from the
	///   index are skipped. Returns <c>false</c> (and <paramref name="timestampMs"/> = default) if none of
	///   the keys are present.
	/// </summary>
	public bool TryGetMax(ReadOnlySpan<TKey> keys, out long timestampMs) {
		var found = false;
		var max = long.MinValue;
		foreach (var key in keys)
			if (_cache.TryGetValue(key, out var entry)) {
				found = true;
				if (entry.Value > max)
					max = entry.Value;
			}

		timestampMs = found ? max : default;
		return found;
	}

	// Range query interface - long (native)

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGte(long thresholdMs)
		=> _rangeIndex.GetValuesGte(thresholdMs);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void GetValuesGte<TAgg>(long lowerValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<long, TKey>.IResultAggregator, allows ref struct =>
		_rangeIndex.GetValuesGte(lowerValue, ref agg);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGt(long thresholdMs)
		=> _rangeIndex.GetValuesGt(thresholdMs);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void GetValuesGt<TAgg>(long lowerValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<long, TKey>.IResultAggregator, allows ref struct {
		_rangeIndex.GetValuesGt(lowerValue, ref agg);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLte(long thresholdMs) {
		return _rangeIndex.GetValuesLte(thresholdMs);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLt(long thresholdMs) {
		return _rangeIndex.GetValuesLt(thresholdMs);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(long fromMs, long toMs) {
		return _rangeIndex.GetValuesBetween(fromMs, toMs);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void GetValuesBetween<TAgg>(long lowerValue, long upperValue, ref TAgg agg)
		where TAgg : struct, PooledBTree<long, TKey>.IResultAggregator, allows ref struct {
		_rangeIndex.GetValuesBetween(lowerValue, upperValue, ref agg);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(long fromMs, long toMs, bool includeFrom, bool includeTo) {
		return _rangeIndex.GetValuesBetween(fromMs, toMs, includeFrom, includeTo);
	}

	// Range query interface - DateTimeOffset

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGte(DateTimeOffset threshold) {
		return GetValuesGte(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGt(DateTimeOffset threshold) {
		return GetValuesGt(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLte(DateTimeOffset threshold) {
		return GetValuesLte(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLt(DateTimeOffset threshold) {
		return GetValuesLt(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(DateTimeOffset from, DateTimeOffset to) {
		return GetValuesBetween(ToUnixMs(from), ToUnixMs(to));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(DateTimeOffset from, DateTimeOffset to, bool includeFrom,
		bool includeTo) {
		return GetValuesBetween(ToUnixMs(from), ToUnixMs(to), includeFrom, includeTo);
	}

	// Range query interface - DateTime

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGte(DateTime threshold) {
		return GetValuesGte(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesGt(DateTime threshold) {
		return GetValuesGt(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLte(DateTime threshold) {
		return GetValuesLte(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesLt(DateTime threshold) {
		return GetValuesLt(ToUnixMs(threshold));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(DateTime from, DateTime to) {
		return GetValuesBetween(ToUnixMs(from), ToUnixMs(to));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValuesBetween(DateTime from, DateTime to, bool includeFrom, bool includeTo) {
		return GetValuesBetween(ToUnixMs(from), ToUnixMs(to), includeFrom, includeTo);
	}

	// Min/Max methods

	/// <summary>
	///   Gets the key with the minimum (oldest) timestamp.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetMin(out long timestampMs, out TKey key) {
		return _rangeIndex.TryGetMin(out timestampMs, out key);
	}

	/// <summary>
	///   Gets the key with the maximum (newest) timestamp.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetMax(out long timestampMs, out TKey key) {
		return _rangeIndex.TryGetMax(out timestampMs, out key);
	}
}

public sealed class LastUpdatedCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey> : ICacheIndex<TKey, TValue>
	where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey> {
	private readonly Func<TKey, TValue, TGroupKey> _groupKeySelector;

	private readonly LastUpdatedIndex<TGroupKey> _index;
	private readonly Func<TKey, TValue, long> _timestampSelector;

	public LastUpdatedCustomTimeStampIndexAdapter(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector,
		Func<TKey, TValue, long> timestampSelector) {
		_index = index;
		_groupKeySelector = groupKeySelector;
		_timestampSelector = timestampSelector;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		var groupKey = _groupKeySelector(key, value);
		var timestamp = _timestampSelector(key, value);
		_index.Add(groupKey, timestamp, timestamp);
	}

	public void Remove(TKey key, TValue value, long timestampMs) {
		var groupKey = _groupKeySelector(key, value);
		_index.Remove(groupKey, timestampMs);
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var oldGroupKey = _groupKeySelector(key, originalValue);
		var newGroupKey = _groupKeySelector(key, newValue);
		var timestamp = _timestampSelector(key, newValue);

		if (oldGroupKey.Equals(newGroupKey)) {
			// Same group key - just update the timestamp
			_index.Update(newGroupKey, timestamp, timestamp);
		}
		else {
			// Group key changed - remove from old, add to new
			_index.Remove(oldGroupKey, timestampMs);
			_index.Add(newGroupKey, timestamp, timestamp);
		}
	}
}

public sealed class LastUpdatedIndexAdapter<TKey, TValue, TGroupKey> : ICacheIndex<TKey, TValue>
	where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey> {
	private readonly Func<TKey, TValue, TGroupKey> _groupKeySelector;

	private readonly LastUpdatedIndex<TGroupKey> _index;

	public LastUpdatedIndexAdapter(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector) {
		_index = index;
		_groupKeySelector = groupKeySelector;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		var groupKey = _groupKeySelector(key, value);
		_index.Add(groupKey, timestampMs, timestampMs);
	}

	public void Remove(TKey key, TValue value, long timestampMs) {
		var groupKey = _groupKeySelector(key, value);
		_index.Remove(groupKey, timestampMs);
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var oldGroupKey = _groupKeySelector(key, originalValue);
		var newGroupKey = _groupKeySelector(key, newValue);

		if (oldGroupKey.Equals(newGroupKey)) {
			// Same group key - just update the timestamp
			_index.Update(newGroupKey, timestampMs, timestampMs);
		}
		else {
			// Group key changed - remove from old, add to new
			_index.Remove(oldGroupKey, timestampMs);
			_index.Add(newGroupKey, timestampMs, timestampMs);
		}
	}
}

/// <summary>
///   Filtered variant of <see cref="LastUpdatedIndexAdapter{TKey,TValue,TGroupKey}"/>. Only entities for
///   which <typeparamref name="TFilter"/> returns <c>true</c> are tracked. Membership is dynamic: the
///   filter is re-evaluated on every update so entities crossing the filter boundary enter or leave the
///   index. <typeparamref name="TFilter"/> is a <c>readonly struct</c>, so the static-abstract call
///   devirtualizes per closed generic.
/// </summary>
public sealed class LastUpdatedFilteredIndexAdapter<TKey, TValue, TGroupKey, TFilter> : ICacheIndex<TKey, TValue>
	where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey>
	where TFilter : struct, IDataCacheGlobalLastUpdateFilter<TValue> {
	private readonly Func<TKey, TValue, TGroupKey> _groupKeySelector;

	private readonly LastUpdatedIndex<TGroupKey> _index;

	public LastUpdatedFilteredIndexAdapter(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector) {
		_index = index;
		_groupKeySelector = groupKeySelector;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		if (!TFilter.Include(value))
			return;
		var groupKey = _groupKeySelector(key, value);
		_index.Add(groupKey, timestampMs, timestampMs);
	}

	public void Remove(TKey key, TValue value, long timestampMs) {
		if (!TFilter.Include(value))
			return;
		var groupKey = _groupKeySelector(key, value);
		_index.Remove(groupKey, timestampMs);
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var oldIncluded = TFilter.Include(originalValue);
		var newIncluded = TFilter.Include(newValue);

		if (!oldIncluded && !newIncluded)
			return;

		if (oldIncluded && !newIncluded) {
			// Left the set - remove from its old group.
			_index.Remove(_groupKeySelector(key, originalValue), timestampMs);
			return;
		}

		if (!oldIncluded && newIncluded) {
			// Entered the set - add to its new group.
			_index.Add(_groupKeySelector(key, newValue), timestampMs, timestampMs);
			return;
		}

		// In the set before and after.
		var oldGroupKey = _groupKeySelector(key, originalValue);
		var newGroupKey = _groupKeySelector(key, newValue);

		if (oldGroupKey.Equals(newGroupKey)) {
			// Same group key - just update the timestamp
			_index.Update(newGroupKey, timestampMs, timestampMs);
		}
		else {
			// Group key changed - remove from old, add to new
			_index.Remove(oldGroupKey, timestampMs);
			_index.Add(newGroupKey, timestampMs, timestampMs);
		}
	}
}

/// <summary>
///   Filtered variant of <see cref="LastUpdatedCustomTimeStampIndexAdapter{TKey,TValue,TGroupKey}"/>.
///   See <see cref="LastUpdatedFilteredIndexAdapter{TKey,TValue,TGroupKey,TFilter}"/> for the membership
///   semantics; the timestamp is taken from <c>timestampSelector</c> instead of the AddOrUpdate timestamp.
/// </summary>
public sealed class LastUpdatedFilteredCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey, TFilter>
	: ICacheIndex<TKey, TValue>
	where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey>
	where TFilter : struct, IDataCacheGlobalLastUpdateFilter<TValue> {
	private readonly Func<TKey, TValue, TGroupKey> _groupKeySelector;

	private readonly LastUpdatedIndex<TGroupKey> _index;
	private readonly Func<TKey, TValue, long> _timestampSelector;

	public LastUpdatedFilteredCustomTimeStampIndexAdapter(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector,
		Func<TKey, TValue, long> timestampSelector) {
		_index = index;
		_groupKeySelector = groupKeySelector;
		_timestampSelector = timestampSelector;
	}

	public void Add(TKey key, TValue value, long timestampMs) {
		if (!TFilter.Include(value))
			return;
		var groupKey = _groupKeySelector(key, value);
		var timestamp = _timestampSelector(key, value);
		_index.Add(groupKey, timestamp, timestamp);
	}

	public void Remove(TKey key, TValue value, long timestampMs) {
		if (!TFilter.Include(value))
			return;
		var groupKey = _groupKeySelector(key, value);
		_index.Remove(groupKey, timestampMs);
	}

	public void Update(TKey key, TValue originalValue, TValue newValue, long timestampMs) {
		var oldIncluded = TFilter.Include(originalValue);
		var newIncluded = TFilter.Include(newValue);

		if (!oldIncluded && !newIncluded)
			return;

		if (oldIncluded && !newIncluded) {
			// Left the set - remove from its old group.
			_index.Remove(_groupKeySelector(key, originalValue), timestampMs);
			return;
		}

		var newTimestamp = _timestampSelector(key, newValue);

		if (!oldIncluded && newIncluded) {
			// Entered the set - add to its new group.
			_index.Add(_groupKeySelector(key, newValue), newTimestamp, newTimestamp);
			return;
		}

		// In the set before and after.
		var oldGroupKey = _groupKeySelector(key, originalValue);
		var newGroupKey = _groupKeySelector(key, newValue);

		if (oldGroupKey.Equals(newGroupKey)) {
			// Same group key - just update the timestamp
			_index.Update(newGroupKey, newTimestamp, newTimestamp);
		}
		else {
			// Group key changed - remove from old, add to new
			_index.Remove(oldGroupKey, timestampMs);
			_index.Add(newGroupKey, newTimestamp, newTimestamp);
		}
	}
}

internal static class InMemoryDataCache {
	internal static readonly DataCacheIndexType InternalIndex = (DataCacheIndexType)1982;
}

public sealed class InMemoryDataCache<TKey, TValue>
	: IDataCache<InMemoryDataCache<TKey, TValue>, TKey, TValue>
	where TKey : notnull, IEquatable<TKey>, IComparable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly ConcurrentCacheStore<TKey, TValue> _cache = new();
	private ICacheIndex<TKey, TValue>[] _indeces = Array.Empty<ICacheIndex<TKey, TValue>>();
	private CachePrimaryKeyIndex<TKey, TValue>? _keyIndex;

	public InMemoryDataCache() : this(DataCacheNoOpStatisticsCollector.Default) {
	}

	public InMemoryDataCache(DataCacheStatisticsCollector statisticsCollector) {
		StatisticsCollector = statisticsCollector;
	}

	/// <summary>
	/// Self-reference to satisfy <see cref="IDataCache{TCache, TKey, TValue}.Cache"/>. The
	/// codegen-emitted wrapper classes have a separate <c>Cache</c> field pointing to an
	/// <see cref="InMemoryDataCache{TKey,TValue}"/>; for the raw cache itself, <c>Cache</c>
	/// is just <c>this</c>.
	/// </summary>
	public InMemoryDataCache<TKey, TValue> Cache => this;

	/// <summary>
	/// Entry point matching <see cref="IDataCache{TCache, TKey, TValue}.Query"/>. Lets the
	/// raw cache participate directly in <c>JoinOne</c> chains without a codegen wrapper —
	/// useful for tests and minimal setups. Codegen-emitted wrappers hide this with their
	/// own <c>Query()</c> that uses the wrapper type as the discriminator carrier; this
	/// implementation uses the cache itself.
	/// Note: <c>WithXxx</c> extensions are codegen-emitted on wrapper types, so filter
	/// callbacks against a raw <c>InMemoryDataCache</c> are limited to <c>.Where(predicate)</c>
	/// and <c>.UseIndex(...)</c>.
	/// </summary>
	public CacheQueryBuilderCombined<TypeSystem.ExecutableQuery<InMemoryDataCache<TKey, TValue>>,
		CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue,
		Resolvers<BaseResolver<TKey, TValue>>, TValue> Query() =>
		new(new TypeSystem.ExecutableQuery<InMemoryDataCache<TKey, TValue>>(this),
			new CacheQueryBuilderCoreCombined<TKey, TValue>(this),
			new Resolvers<BaseResolver<TKey, TValue>>(new BaseResolver<TKey, TValue>()),
			0);

	public CachePrimaryKeyIndex<TKey, TValue> KeyIndex => _keyIndex ??= new CachePrimaryKeyIndex<TKey, TValue>(this);

	internal int Count => _cache.Count;

	internal DataCacheStatisticsCollector StatisticsCollector { get; }

	public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value) {
		return _cache.TryGetValue(key, out value);
	}

	internal int TryCount(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, Predicate<TValue>? predicate = null) {
		return predicate is null
			? _cache.TryCountValues(ref keys)
			: _cache.TryCountValues(ref keys, predicate);
	}

	internal int TryGet<TContainer>(ref TContainer container, ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys,
		Predicate<TValue>? predicate = null)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		return predicate is null
			? _cache.TryGetValues(ref container, ref keys)
			: _cache.TryGetValues(ref container, ref keys, predicate);
	}

	internal int TryGet<TForeignKey, TContainer>(ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys, Predicate<TValue>? predicate = null)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TValue>, allows ref struct {
		return predicate is null
			? _cache.TryGetValues(ref container, ref keys)
			: _cache.TryGetValues(ref container, ref keys, predicate);
	}



	internal int TryGetJoined<TForeignKey, TContainer>(ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys, Predicate<TValue>? predicate = null)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TKey ,TValue>, allows ref struct {
		return predicate is null
			? _cache.TryGetValuesJoined(ref container, ref keys)
			: _cache.TryGetValuesJoined(ref container, ref keys, predicate);
	}

	public List<TValue> TryGet(ICollection<TKey> keys, Predicate<TValue>? predicate = null) {
		return predicate is null ? _cache.TryGetValues(keys) : _cache.TryGetValues(keys, predicate!);
	}

	internal List<TValue> TryGet(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, Predicate<TValue>? predicate = null) {
		return predicate is null ? _cache.TryGetValues(ref keys) : _cache.TryGetValues(ref keys, predicate!);
	}

	public int TryGet(ICollection<TKey> keys, Span<TValue> results, Predicate<TValue>? predicate = null) {
		return predicate is null ? _cache.TryGetValues(keys, results) : _cache.TryGetValues(keys, results, predicate);
	}

	internal ArraySegment<TValue> EnumerateAllValues(Predicate<TValue>? predicate) {
		return predicate is null ? _cache.GetValues() : _cache.GetValues(predicate);
	}

	internal void EnumerateAllValuesInit<TContainer>(ref TContainer container, Predicate<TValue>? predicate)
		where TContainer : IResultContainerInitializer<TKey, TValue>, allows ref struct {
		if (predicate is null)
			_cache.GetValuesInit(ref container);
		else
			_cache.GetValuesInit(ref container, predicate);
	}

	internal int TryGetMapWhere<TMapped, TMapper, TContainer>(ref TContainer container,
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TContainer : IJoinedResultContainer<TKey, TMapped>, allows ref struct {
		return _cache.TryGetValuesMapWhere<TMapped, TMapper, TContainer>(ref container, ref keys, mapper);
	}

	internal void EnumerateAllValuesInitMapWhere<TMapped, TMapper, TContainer>(ref TContainer container, TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TContainer : IResultContainerInitializer<TKey, TMapped>, allows ref struct {
		_cache.GetValuesInitMapWhere<TMapped, TMapper, TContainer>(ref container, mapper);
	}

	internal void EnumerateAllValues<TContainer>(ref TContainer container, Predicate<TValue>? predicate)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (predicate is null)
			_cache.GetValues(ref container);
		else
			_cache.GetValues(ref container, predicate);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal int CountAllValues(Predicate<TValue> predicate) {
		return _cache.CountValues(predicate);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool AddOrUpdate(TKey key, TValue value, out TValue? oldValue) {
		return AddOrUpdate(key, value, DateTimeOffset.Now.ToUnixTimeMilliseconds(), out oldValue);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value, long timestamp, out TValue? oldValue) {
		var r = _cache.AddOrUpdate(key,
			value,
			static (_, ov, nv) => !ov!.CacheEquals(nv));

		if (r.Operation is AddOrUpdateOperation.Same) {
			oldValue = default;
			return false;
		}

		StatisticsCollector.Performed(r.Operation);

		foreach (var index in _indeces)
			if (r.Operation is AddOrUpdateOperation.Update) {
				// Only update if OldValue is not null
				if (r.OldValue is not null)
					index.Update(key, r.OldValue, r.Value, timestamp);
				else
					index.Add(key, r.Value, timestamp);
			}
			else {
				index.Add(key, r.Value, timestamp);
			}

		oldValue = r.OldValue;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value) {
		return AddOrUpdate(key, value, DateTimeOffset.Now.ToUnixTimeMilliseconds());
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool AddOrUpdate(TKey key, TValue value, long timestamp) {
		var r = _cache.AddOrUpdate(key,
			value,
			static (_, ov, nv) => !ov!.CacheEquals(nv));

		if (r.Operation is AddOrUpdateOperation.Same)
			return false;

		StatisticsCollector.Performed(r.Operation);

		foreach (var index in _indeces)
			if (r.Operation is AddOrUpdateOperation.Update) {
				// Only update if OldValue is not null
				if (r.OldValue is not null)
					index.Update(key, r.OldValue, r.Value, timestamp);
				else
					index.Add(key, r.Value, timestamp);
			}
			else {
				index.Add(key, r.Value, timestamp);
			}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key) {
		Remove(key, DateTimeOffset.Now.ToUnixTimeMilliseconds(), out _);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key, long timestamp) {
		Remove(key, timestamp, out _);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value) {
		return Remove(key, DateTimeOffset.Now.ToUnixTimeMilliseconds(), out value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(TKey key, long timestampMs, [MaybeNullWhen(false)] out TValue value) {
		if (!_cache.TryRemove(key, out value))
			return false;

		foreach (var index in _indeces)
			index.Remove(key, value, timestampMs);

		StatisticsCollector.Removed();

		return true;
	}

	public CacheUniqueIndex<TKey, TValue, TIndexKey> AddKeyValueIndex<TIndexKey>(Func<TKey, TValue, TIndexKey> indexer)
		where TIndexKey : IEquatable<TIndexKey> {
		var index = new CacheUniqueIndex<TKey, TValue, TIndexKey>(indexer);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	public CacheSymmetricUniqueIndex<TKey, TValue, TIndexKey> AddSymmetricKeyValueIndex<TIndexKey>(Func<TKey, TValue, TIndexKey> indexer)
		where TIndexKey : IEquatable<TIndexKey> {
		var index = new CacheSymmetricUniqueIndex<TKey, TValue, TIndexKey>(indexer);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	public CacheSymmetricKeyValueListIndex<TKey, TValue, TIndexKey> CacheSymmetricKeyValueListIndex<TIndexKey>(
		Func<TKey, TValue, TIndexKey> indexer)
		where TIndexKey : notnull {
		var index = new CacheSymmetricKeyValueListIndex<TKey, TValue, TIndexKey>(indexer);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	public CacheKeyValueListIndex<TKey, TValue, TIndexKey> CacheKeyValueListIndex<TIndexKey>(
		Func<TKey, TValue, TIndexKey> indexer)
		where TIndexKey : notnull {
		var index = new CacheKeyValueListIndex<TKey, TValue, TIndexKey>(indexer);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	/// <summary>
	///   Creates a collection-backed Many index: each element of the per-entity collection becomes an index
	///   key pointing back to the entity (inverted index). The returned index is a plain
	///   <see cref="CacheKeyValueListIndex{TKey,TValue,TIndexKey}" /> in collection mode, so it composes with
	///   the existing query (<c>UseIndex</c>) and <c>JoinMany</c> machinery unchanged.
	/// </summary>
	public CacheKeyValueListIndex<TKey, TValue, TIndexKey> CacheCollectionKeyValueListIndex<TIndexKey>(
		Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector)
		where TIndexKey : notnull {
		var index = new CacheKeyValueListIndex<TKey, TValue, TIndexKey>(collectionSelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	/// <summary>
	///   Creates a symmetric collection-backed index (forward element → {owners} + reverse owner → {elements}).
	///   The reverse half lets the M:N collection-join resolver fan a single right value out to its many lefts.
	/// </summary>
	public CacheCollectionSymmetricKeyValueListIndex<TKey, TValue, TIndexKey> CacheCollectionSymmetricKeyValueListIndex<TIndexKey>(
		Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector)
		where TIndexKey : notnull {
		var index = new CacheCollectionSymmetricKeyValueListIndex<TKey, TValue, TIndexKey>(collectionSelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	public CacheRangeIndex<TKey, TValue, TIndexKey> CacheRangeIndex<TIndexKey>(Func<TKey, TValue, TIndexKey> indexer)
		where TIndexKey : IComparable<TIndexKey> {
		var index = new CacheRangeIndex<TKey, TValue, TIndexKey>(indexer);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}

	public CacheKeySetIndex<TKey, TValue> AddKeySetIndex(Func<TKey, TValue, bool> predicate) {
		var index = new CacheKeySetIndex<TKey, TValue>(predicate);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = index;
		return index;
	}


	public LastUpdatedIndexAdapter<TKey, TValue, TGroupKey> CacheLastUpdatedIndex<TGroupKey>(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector)
		where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey> {
		var adapter = new LastUpdatedIndexAdapter<TKey, TValue, TGroupKey>(
			index,
			groupKeySelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = adapter;
		return adapter;
	}

	public LastUpdatedCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey> CacheLastUpdatedIndex<TGroupKey>(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector,
		Func<TKey, TValue, long> timestampSelector)
		where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey> {
		var adapter = new LastUpdatedCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey>(
			index,
			groupKeySelector, timestampSelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = adapter;
		return adapter;
	}

	public LastUpdatedFilteredIndexAdapter<TKey, TValue, TGroupKey, TFilter> CacheLastUpdatedIndex<TGroupKey, TFilter>(
		LastUpdatedIndex<TGroupKey> index,
		Func<TKey, TValue, TGroupKey> groupKeySelector)
		where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey>
		where TFilter : struct, IDataCacheGlobalLastUpdateFilter<TValue> {
		var adapter = new LastUpdatedFilteredIndexAdapter<TKey, TValue, TGroupKey, TFilter>(
			index,
			groupKeySelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = adapter;
		return adapter;
	}

	public LastUpdatedFilteredCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey, TFilter>
		CacheLastUpdatedIndex<TGroupKey, TFilter>(
			LastUpdatedIndex<TGroupKey> index,
			Func<TKey, TValue, TGroupKey> groupKeySelector,
			Func<TKey, TValue, long> timestampSelector)
		where TGroupKey : IEquatable<TGroupKey>, IComparable<TGroupKey>
		where TFilter : struct, IDataCacheGlobalLastUpdateFilter<TValue> {
		var adapter = new LastUpdatedFilteredCustomTimeStampIndexAdapter<TKey, TValue, TGroupKey, TFilter>(
			index,
			groupKeySelector, timestampSelector);
		var len = _indeces.Length;
		Array.Resize(ref _indeces, len + 1);
		_indeces[len] = adapter;
		return adapter;
	}
}

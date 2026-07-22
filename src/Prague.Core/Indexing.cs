namespace Prague.Core;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Collections;
using Prague.Core.Utils;

public sealed class
	CacheSymmetricKeyValueListIndex<TKey, TValue, TIndexKey> : CacheKeyValueListIndex<TKey, TValue, TIndexKey>
	where TIndexKey : notnull
	where TKey : notnull {

	public CacheUniqueIndex<TIndexKey, TKey, TKey> Reverse => _cacheReverse;

	public CacheSymmetricKeyValueListIndex(Func<TKey, TValue, TIndexKey> keySelector)
		: this(keySelector, Constants.NonSetCapacity) {
	}

	public CacheSymmetricKeyValueListIndex(Func<TKey, TValue, TIndexKey> keySelector, int initialBucketCapacity)
		: base(keySelector, true, initialBucketCapacity) {
	}

	// ── Bulk primitives for the LeftSym join resolver ────────────────────────
	//
	// These build pairs of (PooledSet<TKey, DefaultKeyComparer<TKey>> = all-lefts-sharing-this-lookupKey,
	// TIndexKey = lookupKey) by iterating the candidate left-keys. The PooledSet
	// is borrowed directly from this index's storage — no allocation.
	//
	// Pair set dedups on .Key (the lookupKey), so multiple lefts that share the
	// same lookupKey collapse into a single pair. FanOutContainer at emission
	// time expands the JoinedKey set back to individual lefts.

	/// <summary>
	/// Shape A: build (set-of-lefts, lookupKey) pairs from a candidate
	/// <see cref="ValueSet{TKey}"/>. Used by <c>JoinOneLeftSymResolver</c>
	/// when <c>TLookupKey == TRightKey</c> (identity).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>>> target,
		bool add) {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>(new LeftKeySetView<TKey>(lefts), lku));
			}
		}
	}

	/// <summary>Shape A, <see cref="ReadOnlySpan{TKey}"/> input.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(
		ReadOnlySpan<TKey> candidates,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>>> target,
		bool add) {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TIndexKey>(new LeftKeySetView<TKey>(lefts), lku));
			}
		}
	}

	/// <summary>
	/// Shape B: translates <typeparamref name="TIndexKey"/> → TRightKey via
	/// <paramref name="rightIndex"/>; pair stores TRightKey as <c>.Key</c>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesVia<TRightKey, TRightValue>(
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates,
		CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>>> target,
		bool add)
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				if (!rightIndex.TryGetValue(lku, out var rk))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>(new LeftKeySetView<TKey>(lefts), rk));
			}
		}
	}

	/// <summary>Shape B, <see cref="ReadOnlySpan{TKey}"/> input.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesVia<TRightKey, TRightValue>(
		ReadOnlySpan<TKey> candidates,
		CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>>> target,
		bool add)
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				if (!rightIndex.TryGetValue(lku, out var rk))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>(new LeftKeySetView<TKey>(lefts), rk));
			}
		}
	}

	/// <summary>
	/// Shape A with a struct selector that translates <typeparamref name="TIndexKey"/>
	/// to the user's right key. JIT-devirtualizes per closed generic.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TOutKey, TSelector>(
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates,
		TSelector selector,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>>> target,
		bool add)
		where TOutKey : notnull
		where TSelector : struct, IKeySelector<TIndexKey, TOutKey> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var ok = selector.Select(lku);
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>(new LeftKeySetView<TKey>(lefts), ok));
			}
		}
	}

	/// <summary>Shape A + selector, <see cref="ReadOnlySpan{TKey}"/> input.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TOutKey, TSelector>(
		ReadOnlySpan<TKey> candidates,
		TSelector selector,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>>> target,
		bool add)
		where TOutKey : notnull
		where TSelector : struct, IKeySelector<TIndexKey, TOutKey> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var ok = selector.Select(lku);
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TOutKey>(new LeftKeySetView<TKey>(lefts), ok));
			}
		}
	}

	/// <summary>
	/// Shape B with a struct selector that translates <typeparamref name="TIndexKey"/>
	/// to <typeparamref name="TIntermediateKey"/>, then <paramref name="rightIndex"/>
	/// resolves it to TRightKey.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesVia<TIntermediateKey, TSelector, TRightKey, TRightValue>(
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates,
		TSelector selector,
		CacheKeyValueIndex<TRightKey, TRightValue, TIntermediateKey> rightIndex,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>>> target,
		bool add)
		where TIntermediateKey : notnull, IEquatable<TIntermediateKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TSelector : struct, IKeySelector<TIndexKey, TIntermediateKey> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var imk = selector.Select(lku);
				if (!rightIndex.TryGetValue(imk, out var rk))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>(new LeftKeySetView<TKey>(lefts), rk));
			}
		}
	}

	/// <summary>Shape B + selector, <see cref="ReadOnlySpan{TKey}"/> input.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesVia<TIntermediateKey, TSelector, TRightKey, TRightValue>(
		ReadOnlySpan<TKey> candidates,
		TSelector selector,
		CacheKeyValueIndex<TRightKey, TRightValue, TIntermediateKey> rightIndex,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>>> target,
		bool add)
		where TIntermediateKey : notnull, IEquatable<TIntermediateKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TSelector : struct, IKeySelector<TIndexKey, TIntermediateKey> {
		if (add) {
			foreach (var lk in candidates) {
				if (!_cacheReverse.TryGetValue(lk, out var lku))
					continue;
				var imk = selector.Select(lku);
				if (!rightIndex.TryGetValue(imk, out var rk))
					continue;
				var lefts = GetValuesUnsafe(lku);
				target.Add(new JoinedKeyPair<LeftKeySetView<TKey>, TRightKey>(new LeftKeySetView<TKey>(lefts), rk));
			}
		}
	}
}

public class CacheKeyValueListIndex<TKey, TValue, TIndexKey> : ICacheIndex<TKey, TValue>, ICountableCacheIndex
	where TIndexKey : notnull
	where TKey : notnull {
	private readonly ConcurrentCacheStore<TIndexKey, PooledSet<TKey, DefaultKeyComparer<TKey>>> _cache = new();
	private ulong _keysSize;

	// First-generation slot capacity for new per-key buckets. Plumbed from
	// [DataCacheIndex(InitialCapacity = ...)] via the cache factory;
	// Constants.NonSetCapacity when no hint is given — PooledSet's constructor
	// resolves the sentinel to the library default.
	private readonly int _bucketInitialCapacity;

	internal readonly CacheUniqueIndex<TIndexKey, TKey, TKey> _cacheReverse = null!;

	// Set for collection-backed indexes: yields the per-entity list of index keys, so the entity is
	// registered under each element. Null for scalar indexes (the common case), which use KeySelector.
	private readonly Func<TKey, TValue, IReadOnlyList<TIndexKey>>? _collectionSelector;

	protected CacheKeyValueListIndex(Func<TKey, TValue, TIndexKey> keySelector, bool reverse,
		int initialBucketCapacity) {
		KeySelector = keySelector;
		_bucketInitialCapacity = initialBucketCapacity;
		if (reverse) {
			_cacheReverse = new CacheUniqueIndex<TIndexKey, TKey, TKey>(static (_, value) => value);
		}
	}

	public CacheKeyValueListIndex(Func<TKey, TValue, TIndexKey> keySelector)
		: this(keySelector, false, Constants.NonSetCapacity) {
	}

	public CacheKeyValueListIndex(Func<TKey, TValue, TIndexKey> keySelector, int initialBucketCapacity)
		: this(keySelector, false, initialBucketCapacity) {
	}

	public CacheKeyValueListIndex(Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector)
		: this(collectionSelector, Constants.NonSetCapacity) {
	}

	public CacheKeyValueListIndex(Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector,
		int initialBucketCapacity) {
		_collectionSelector = collectionSelector;
		_bucketInitialCapacity = initialBucketCapacity;
		KeySelector = null!;
	}

	// Raw storage with no selector: never registered for cache maintenance (Add/Remove/Update are not
	// called via ICacheIndex); populated externally through AddUnderKey/RemoveUnderKey. Used as the
	// forward/reverse half of a CacheCollectionSymmetricKeyValueListIndex.
	internal CacheKeyValueListIndex() : this(Constants.NonSetCapacity) {
	}

	internal CacheKeyValueListIndex(int initialBucketCapacity) {
		_bucketInitialCapacity = initialBucketCapacity;
		KeySelector = null!;
	}

	internal Func<TKey, TValue, TIndexKey> KeySelector { get; }

	/// <summary>
	///   Gets the approximate count of items in this index. This is an O(1) operation.
	///   Note: Due to concurrent operations, this count may be slightly inaccurate at any given moment.
	/// </summary>
	public ulong ApproximateCount { get; private set; }

	/// <summary>
	///   Checks if the index contains any values for the given key.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(TIndexKey key) => _cache.TryGetValue(key, out _);

	public void Update(TKey key, int keyHash, TValue orginialValue, TValue newValue, long timestampMs) {
		if (_collectionSelector is not null) {
			UpdateCollection(key, keyHash, orginialValue, newValue, timestampMs);
			return;
		}

		var oldIndexKey = KeySelector(key, orginialValue);
		var newIndexKey = KeySelector(key, newValue);

		// Same index key - do nothing
		if (oldIndexKey.Equals(newIndexKey))
			return;

		// Different index keys - add to new first, then remove from old
		// This ensures the key is always visible (may temporarily duplicate, but won't disappear)
		var r = _cache.AddOrUpdate(newIndexKey,
			static (_, a) => {
				var s = new PooledSet<TKey, DefaultKeyComparer<TKey>>(default, a.Capacity);
				s.Add(a.Key, a.Hash);
				return s;
			},
			static (_, hs, a) => { hs.Add(a.Key, a.Hash); return hs; },
			(Key: key, Hash: keyHash, Capacity: _bucketInitialCapacity));

		var sizeDelta = (ulong)r.Value.Count;
		if (r.OldValue is not null)
			sizeDelta -= (ulong)r.OldValue.Count;
		ApproximateCount += sizeDelta;

		var rr = _cache.UpdateOrRemove(oldIndexKey, static (_, hs, a) => {
			hs.Remove(a.Key, a.Hash);
			if (hs.Count > 0)
				return (true, hs);
			hs.Dispose();
			return (false, null);
		}, (Key: key, Hash: keyHash));

		sizeDelta = 0UL;
		if (rr.OldValue is not null)
			sizeDelta -= (ulong)rr.OldValue.Count;

		if (rr.NewValue is not null)
			sizeDelta += (ulong)rr.NewValue.Count;

		ApproximateCount += sizeDelta;
		_cacheReverse?.Add(newIndexKey, keyHash, key, timestampMs);
	}

	// Population is unified onto a span path: scalar feeds a zero-alloc 1-item span over the computed
	// key; collection feeds a span over the concrete List<T>/array. Both loop the per-key helpers.

	public void Add(TKey key, int keyHash, TValue value, long timestampMs) {
		if (_collectionSelector is not null) {
			AddCollection(key, keyHash, value, timestampMs);
			return;
		}

		var indexKey = KeySelector(key, value);
		AddKeys(MemoryMarshal.CreateReadOnlySpan(ref indexKey, 1), key, keyHash, timestampMs);
	}

	public void Remove(TKey key, int keyHash, TValue orginialValue, long timestampMs) {
		if (_collectionSelector is not null) {
			RemoveCollection(key, keyHash, orginialValue, timestampMs);
			return;
		}

		var indexKey = KeySelector(key, orginialValue);
		RemoveKeys(MemoryMarshal.CreateReadOnlySpan(ref indexKey, 1), key, keyHash, timestampMs);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AddKeys(ReadOnlySpan<TIndexKey> indexKeys, TKey key, int keyHash, long timestampMs) {
		foreach (var indexKey in indexKeys)
			AddUnderKey(indexKey, key, keyHash, timestampMs);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void RemoveKeys(ReadOnlySpan<TIndexKey> indexKeys, TKey key, int keyHash, long timestampMs) {
		foreach (var indexKey in indexKeys)
			RemoveUnderKey(indexKey, key, keyHash, timestampMs);
	}

	// Registers/unregisters one (indexKey -> key) edge. Idempotent on Add; a no-op on Remove once the
	// edge is gone (so duplicate elements in a collection cannot mis-count _keysSize/ApproximateCount).
	internal void AddUnderKey(TIndexKey indexKey, TKey key, int keyHash, long timestampMs) {
		var r = _cache.AddOrUpdate(indexKey,
			static (_, a) => {
				var s = new PooledSet<TKey, DefaultKeyComparer<TKey>>(default, a.Capacity);
				s.Add(a.Key, a.Hash);
				return s;
			},
			static (_, hs, a) => { hs.Add(a.Key, a.Hash); return hs; },
			(Key: key, Hash: keyHash, Capacity: _bucketInitialCapacity));

		var sizeDelta = (ulong)r.Value.Count;
		var keyDelta = 1UL;
		if (r.OldValue is not null) {
			sizeDelta -= (ulong)r.OldValue.Count;
			keyDelta = 0;
		}

		_keysSize += keyDelta;
		ApproximateCount += sizeDelta;
		_cacheReverse?.Add(indexKey, keyHash, key, timestampMs);
	}

	internal void RemoveUnderKey(TIndexKey indexKey, TKey key, int keyHash, long timestampMs) {
		_cacheReverse?.Remove(indexKey, keyHash, key, timestampMs);
		var r = _cache.UpdateOrRemove(indexKey,
			static (_, hs, a) => {
				hs.Remove(a.Key, a.Hash);
				if (hs.Count > 0)
					return (true, hs);
				hs.Dispose();
				return (false, null);
			}, (Key: key, Hash: keyHash));

		var sizeDelta = 0UL;
		if (r.OldValue is not null)
			sizeDelta -= (ulong)r.OldValue.Count;

		if (r.NewValue is not null)
			sizeDelta += (ulong)r.NewValue.Count;
		else if (r.OldValue is not null)
			_keysSize--;

		ApproximateCount += sizeDelta;
	}

	// ── Collection-backed population (register the owner under each element) ──

	private void AddCollection(TKey key, int keyHash, TValue value, long timestampMs) {
		var coll = _collectionSelector!(key, value);
		switch (coll) {
			case null:
				return;
			case TIndexKey[] arr:
				AddKeys(arr, key, keyHash, timestampMs);
				return;
			case List<TIndexKey> list:
				AddKeys(CollectionsMarshal.AsSpan(list), key, keyHash, timestampMs);
				return;
			default:
				for (var i = 0; i < coll.Count; i++)
					AddUnderKey(coll[i], key, keyHash, timestampMs);
				return;
		}
	}

	private void RemoveCollection(TKey key, int keyHash, TValue value, long timestampMs) {
		var coll = _collectionSelector!(key, value);
		switch (coll) {
			case null:
				return;
			case TIndexKey[] arr:
				RemoveKeys(arr, key, keyHash, timestampMs);
				return;
			case List<TIndexKey> list:
				RemoveKeys(CollectionsMarshal.AsSpan(list), key, keyHash, timestampMs);
				return;
			default:
				for (var i = 0; i < coll.Count; i++)
					RemoveUnderKey(coll[i], key, keyHash, timestampMs);
				return;
		}
	}

	// Element-set diff: add (new \ old) first for reader visibility, then remove (old \ new). Runs only on
	// value changes (the cold update path), so a plain membership scan over IReadOnlyList is fine here.
	private void UpdateCollection(TKey key, int keyHash, TValue originalValue, TValue newValue, long timestampMs) {
		var oldColl = _collectionSelector!(key, originalValue);
		var newColl = _collectionSelector!(key, newValue);
		var oldCount = oldColl?.Count ?? 0;
		var newCount = newColl?.Count ?? 0;
		var cmp = EqualityComparer<TIndexKey>.Default;

		for (var i = 0; i < newCount; i++) {
			var e = newColl![i];
			if (!ListContains(oldColl, oldCount, e, cmp))
				AddUnderKey(e, key, keyHash, timestampMs);
		}

		for (var i = 0; i < oldCount; i++) {
			var e = oldColl![i];
			if (!ListContains(newColl, newCount, e, cmp))
				RemoveUnderKey(e, key, keyHash, timestampMs);
		}
	}

	private static bool ListContains(IReadOnlyList<TIndexKey>? list, int count, TIndexKey value,
		EqualityComparer<TIndexKey> cmp) {
		for (var i = 0; i < count; i++)
			if (cmp.Equals(list![i], value))
				return true;
		return false;
	}

	public ulong GetCounters(out ulong values) {
		values = ApproximateCount;
		return _keysSize;
	}

	public IReadOnlyCollection<TKey> GetValues(TIndexKey key) {
		if (_cache.TryGetValue(key, out var value))
			return value;
		return Array.Empty<TKey>();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValues(List<TIndexKey> keys) {
		return GetValues(CollectionsMarshal.AsSpan(keys));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IReadOnlyCollection<TKey> GetValues(ReadOnlySpan<TIndexKey> keys) {
		var target = new HashSet<TKey>();
		foreach (var key in keys)
			if (_cache.TryGetValue(key, out var values))
				foreach (var item in values)
					target.Add(item);

		return target;
	}

	internal PooledSet<TKey, DefaultKeyComparer<TKey>> GetValuesUnsafe(TIndexKey key)
		=> _cache.TryGetValue(key, out var value) ? value : PooledSet<TKey, DefaultKeyComparer<TKey>>.Empty;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(List<TIndexKey> keys, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		IntersectValues(CollectionsMarshal.AsSpan(keys), ref target, add);
	}

	internal void IntersectValues<TOtherValue>(ReadOnlySpan<TOtherValue> otherValues, Func<TOtherValue, TIndexKey> keySelector, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		if (add) {
			foreach (var otherValue in otherValues)
				if (_cache.TryGetValue(keySelector(otherValue), out var values))
					target.UnionWith(values);
		}
		else {
			using var intersecter =
				new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref target, stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
			foreach (var otherValue in otherValues)
				if (_cache.TryGetValue(keySelector(otherValue), out var values))
					intersecter.IntersectWith(values);
		}
	}

	internal void IntersectValues<TOtherValue>(ReadOnlySpan<TOtherValue> otherValues, Func<TOtherValue, TIndexKey> keySelector, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		foreach (var otherValue in otherValues)
			if (_cache.TryGetValue(keySelector(otherValue), out var values))
				intersecter.IntersectWith(values);
	}

	internal void IntersectValues<TOtherValue>(ReadOnlySpan<TOtherValue> otherValues, Func<TOtherValue, TIndexKey> keySelector, ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target, bool add) {
		if (add) {
			foreach (var otherValue in otherValues) {
				var key = keySelector(otherValue);
				if (_cache.TryGetValue(key, out var values))
					target.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);
			}
		}
	}
/*
 *approxSize
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
 */

	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
		foreach (var key in keys)
			if (_cache.TryGetValue(key, out var values))
				intersecter.IntersectWith(values);
	}

	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		if (add) {
			foreach (var key in keys)
				if (_cache.TryGetValue(key, out var values))
					target.UnionWith(values);
		}
		else {
			using var intersecter =
				new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref target, stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold]);
			foreach (var key in keys)
				if (_cache.TryGetValue(key, out var values))
					intersecter.IntersectWith(values);
		}
	}


	internal void IntersectValuesInit<TContainer>(ref TContainer container, ReadOnlySpan<TIndexKey> keys,
		ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target)
		where TContainer : struct, IJoinedKeyedResultContainerInitializer<TIndexKey>, allows ref struct {
		foreach (var key in keys)
			if (_cache.TryGetValue(key, out var values)) {
				container.Init(key, values.Count);
				target.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);
			}
	}

	/// <summary>
	/// IntersectValuesInit with a source span and key selector - extracts keys from source items.
	/// </summary>
	internal void IntersectValuesInit<TContainer, TSource>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TIndexKey> keySelector,
		ref ValueSet<JoinedKeyPair<TSource, TKey>, DefaultKeyComparer<JoinedKeyPair<TSource, TKey>>> target)
		where TContainer : struct, IJoinedKeyedResultContainerInitializer<TSource>, allows ref struct where TSource : notnull {
		foreach (var source in sources) {
			var key = keySelector(source);
			if (_cache.TryGetValue(key, out var values)) {
				container.Init(source, values.Count);
				target.UnionWith(JoinedKeyPair<TSource, TKey>.IntoKeyed(source), values);
			}
		}
	}
	/// <summary>
	/// IntersectValuesInit with a source span and key selector - extracts keys from source items.
	/// </summary>
	internal void IntersectValuesInit<TContainer, TSource>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TIndexKey> keySelector,
		ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target)
		where TContainer : struct, IJoinedKeyedResultContainerInitializer<TIndexKey>, allows ref struct {
		foreach (var source in sources) {
			var key = keySelector(source);
			if (_cache.TryGetValue(key, out var values)) {
				container.Init(key, values.Count);
				target.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);
			}
		}
	}

	/// <summary>
	/// Gets primary keys for each source item and calls container.Add(indexKey, source, primaryKey) for each.
	/// Used for QueryResults continuation joins - maps sources to their related primary keys.
	/// </summary>
	internal void GetKeysForSources<TContainer, TSource>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TIndexKey> keySelector)
		where TContainer : struct, IJoinedSourceResultContainer<TIndexKey, TSource, TKey>, allows ref struct {
		foreach (var source in sources) {
			var indexKey = keySelector(source);
			if (_cache.TryGetValue(indexKey, out var primaryKeys)) {
				foreach (var primaryKey in primaryKeys) {
					container.Add(indexKey, source, primaryKey);
				}
			}
		}
	}

	internal void IntersectValues(ReadOnlySpan<TIndexKey> keys, ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target,
		bool add) {
		if (add) {
			// Union straight into the target: pairs deduplicate on insert, so the old
			// intermediate set bought nothing here — it was a full extra copy of every
			// bucket, sized by a keys*10 guess on top.
			foreach (var key in keys)
				if (_cache.TryGetValue(key, out var values)) {
					target.EnsureCapacity(target.Count + values.Count);
					target.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);
				}

			return;
		}

		// Intersect needs the full right-hand set up front; size it from the actual
		// bucket counts (a cheap O(1) probe per key) instead of the keys*10 guess,
		// which both over-allocated small lookups and under-allocated big buckets.
		// Buckets may mutate concurrently, so the sum is a hint — the set still
		// grows on demand.
		var capacity = 0;
		foreach (var key in keys)
			if (_cache.TryGetValue(key, out var values))
				capacity += values.Count;

		var tempSet = new ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>>(capacity);
		try {
			foreach (var key in keys)
				if (_cache.TryGetValue(key, out var values))
					tempSet.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);

			target.IntersectWith(ref tempSet);
		}
		finally {
			tempSet.Dispose();
		}
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValue(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter target) {
		if (!_cache.TryGetValue(key, out var values)) {
			target.Clear();
			return;
		}

		target.IntersectWith(values);
	}

	// Prune-phase counterpart for chained UseIndex within an Or branch:
	// keep only marks whose slot value is in the PooledSet returned for `key`.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void RetainOnly(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter target) {
		if (!_cache.TryGetValue(key, out var values)) {
			target.Clear();
			return;
		}

		target.RetainOnly(values);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues(TIndexKey key, ref ValueSet<TKey, DefaultKeyComparer<TKey>> target, bool add) {
		if (!_cache.TryGetValue(key, out var values)) {
			target.Clear();
			return;
		}

		if (add)
			target.UnionWith(values);
		else
			target.IntersectWith(values);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValuesInit<TContainer>(ref TContainer container, TIndexKey key,
		ref ValueSet<JoinedKeyPair<TIndexKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TKey>>> target)
		where TContainer : IJoinedKeyedResultContainerInitializer<TIndexKey>, allows ref struct {
		if (!_cache.TryGetValue(key, out var values)) {
			target.Clear();
			container.Init(key, 0);
			return;
		}

		container.Init(key, values.Count);
		target.UnionWith(JoinedKeyPair<TIndexKey, TKey>.IntoKeyed(key), values);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void IntersectValues<TJoinKey>(TJoinKey key, ref ValueSet<JoinedKeyPair<TJoinKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TJoinKey, TKey>>> target, bool add)
		where TJoinKey : TIndexKey {
		if (!_cache.TryGetValue(key, out var values)) {
			target.Clear();
			return;
		}

		if (add)
			target.UnionWith(JoinedKeyPair<TJoinKey, TKey>.IntoKeyed(key), values);
		else
			target.IntersectWith(JoinedKeyPair<TJoinKey, TKey>.Into, values);
	}
}

/// <summary>
///   Symmetric collection index: the entity is registered under each element of its collection property in
///   BOTH directions. <see cref="Forward" /> maps element → {owner keys} (e.g. tagId → {bookKeys}), used for
///   queries and for fanning out the owner→referenced join. <see cref="Reverse" /> maps owner key →
///   {element keys} (e.g. bookKey → {tagIds}), used to fan a single right value out to its many lefts in the
///   M:N collection-join resolver. Both halves are plain <see cref="CacheKeyValueListIndex{TKey,TValue,TIndexKey}" />
///   storages populated single-pass via the per-key helpers.
/// </summary>
public sealed class CacheCollectionSymmetricKeyValueListIndex<TKey, TValue, TIndexKey>
	: ICacheIndex<TKey, TValue>, ICountableCacheIndex
	where TKey : notnull
	where TIndexKey : notnull {
	private readonly Func<TKey, TValue, IReadOnlyList<TIndexKey>> _collectionSelector;

	/// <summary>element key → {owner keys} (e.g. tagId → {bookKeys}).</summary>
	public CacheKeyValueListIndex<TKey, TValue, TIndexKey> Forward { get; }

	/// <summary>owner key → {element keys} (e.g. bookKey → {tagIds}).</summary>
	public CacheKeyValueListIndex<TIndexKey, TValue, TKey> Reverse { get; }

	public CacheCollectionSymmetricKeyValueListIndex(Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector)
		: this(collectionSelector, Constants.NonSetCapacity) {
	}

	public CacheCollectionSymmetricKeyValueListIndex(Func<TKey, TValue, IReadOnlyList<TIndexKey>> collectionSelector,
		int initialBucketCapacity) {
		_collectionSelector = collectionSelector;
		Forward = new(initialBucketCapacity);
		Reverse = new(initialBucketCapacity);
	}

	public ulong GetCounters(out ulong values) => Forward.GetCounters(out values);

	public void Add(TKey key, int keyHash, TValue value, long timestampMs) =>
		ApplyAll(_collectionSelector(key, value), key, keyHash, timestampMs, add: true);

	public void Remove(TKey key, int keyHash, TValue originalValue, long timestampMs) =>
		ApplyAll(_collectionSelector(key, originalValue), key, keyHash, timestampMs, add: false);

	public void Update(TKey key, int keyHash, TValue originalValue, TValue newValue, long timestampMs) {
		var oldColl = _collectionSelector(key, originalValue);
		var newColl = _collectionSelector(key, newValue);
		var oldCount = oldColl?.Count ?? 0;
		var newCount = newColl?.Count ?? 0;
		var cmp = EqualityComparer<TIndexKey>.Default;

		for (var i = 0; i < newCount; i++) {
			var e = newColl![i];
			if (!Contains(oldColl, oldCount, e, cmp))
				ApplyOne(e, key, keyHash, timestampMs, add: true);
		}

		for (var i = 0; i < oldCount; i++) {
			var e = oldColl![i];
			if (!Contains(newColl, newCount, e, cmp))
				ApplyOne(e, key, keyHash, timestampMs, add: false);
		}
	}

	private void ApplyAll(IReadOnlyList<TIndexKey>? coll, TKey key, int keyHash, long timestampMs, bool add) {
		switch (coll) {
			case null:
				return;
			case TIndexKey[] arr:
				ApplySpan(arr, key, keyHash, timestampMs, add);
				return;
			case List<TIndexKey> list:
				ApplySpan(CollectionsMarshal.AsSpan(list), key, keyHash, timestampMs, add);
				return;
			default:
				for (var i = 0; i < coll.Count; i++)
					ApplyOne(coll[i], key, keyHash, timestampMs, add);
				return;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ApplySpan(ReadOnlySpan<TIndexKey> elements, TKey key, int keyHash, long timestampMs, bool add) {
		foreach (var element in elements)
			ApplyOne(element, key, keyHash, timestampMs, add);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ApplyOne(TIndexKey element, TKey key, int keyHash, long timestampMs, bool add) {
		// Forward's per-element set stores the MAIN key: thread the store-provided keyHash into the
		// pre-hashed set mutation. Reverse's per-owner set stores the ELEMENT — keyHash covers the
		// main key only (Reverse spends it as its store key, hashed internally as before), so the
		// element's set hash is computed here with the same comparer the set itself uses.
		var elementHash = default(DefaultKeyComparer<TIndexKey>).GetHashCode(element);
		if (add) {
			Forward.AddUnderKey(element, key, keyHash, timestampMs);
			Reverse.AddUnderKey(key, element, elementHash, timestampMs);
		} else {
			Forward.RemoveUnderKey(element, key, keyHash, timestampMs);
			Reverse.RemoveUnderKey(key, element, elementHash, timestampMs);
		}
	}

	private static bool Contains(IReadOnlyList<TIndexKey>? list, int count, TIndexKey value,
		EqualityComparer<TIndexKey> cmp) {
		for (var i = 0; i < count; i++)
			if (cmp.Equals(list![i], value))
				return true;
		return false;
	}
}

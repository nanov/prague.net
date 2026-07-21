namespace Prague.Core.Collections;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[DebuggerTypeProxy(typeof(ConcurrentCacheStore<,>.IDictionaryDebugView))]
[DebuggerDisplay("Count = {Count}")]
internal class ConcurrentCacheStore<TKey, TValue> where TKey : notnull {
	private const int DefaultCapacity = 31;
	private const int MaxLockNumber = 1024;

	private readonly bool _growLockArray;
	private readonly int _initialCapacity;
	private int _budget;
	private volatile Tables _tables;

	public ConcurrentCacheStore()
		: this(DefaultConcurrencyLevel, DefaultCapacity, true) {
	}

	public ConcurrentCacheStore(int concurrencyLevel, int capacity)
		: this(concurrencyLevel, capacity, false) {
	}

	private ConcurrentCacheStore(int concurrencyLevel, int capacity, bool growLockArray) {
		if (capacity < concurrencyLevel)
			capacity = concurrencyLevel;
		capacity = HashHelpers.GetPrime(capacity);
		var locks = new object[concurrencyLevel];
		locks[0] = locks;
		for (var i = 1; i < locks.Length; ++i)
			locks[i] = new object();
		var countPerLock = new int[locks.Length];
		var buckets = new VolatileNode[capacity];
		_tables = new Tables(buckets, locks, countPerLock);
		_growLockArray = growLockArray;
		_initialCapacity = capacity;
		_budget = buckets.Length / locks.Length;
	}

	public int Count {
		get {
			var locksAcquired = 0;
			try {
				var tables = AcquireAllLocks(ref locksAcquired);
				return GetCountNoLocks(tables);
			} finally {
				ReleaseLocks(locksAcquired);
			}
		}
	}

	private static int DefaultConcurrencyLevel => Environment.ProcessorCount;

	// JIT-devirtualized hash/equals via DefaultKeyComparer<TKey> — the comparer choice is baked
	// into the type (struct generic specialization), the single hashing/equality authority for
	// every path in this store. For value types implementing IEquatable<T>,
	// EqualityComparer<T>.Default folds to a direct call; for string, DefaultKeyComparer
	// special-cases via Unsafe.As<T,string>. A table swap can never change the hash function,
	// so retry loops only re-read _tables and never rehash.
	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private static int GetHashCode(TKey key)
		=> default(DefaultKeyComparer<TKey>).GetHashCode(key);

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private static bool KeyEquals(TKey x, TKey y)
		=> default(DefaultKeyComparer<TKey>).Equals(x, y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(TKey key) => TryGetValue(key, out _);

	public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
		=> TryRemoveInternal(key, GetHashCode(key), out value, false, default);

	public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value, out int keyHash) {
		keyHash = GetHashCode(key);
		return TryRemoveInternal(key, keyHash, out value, false, default);
	}

	public bool TryRemove(KeyValuePair<TKey, TValue> item)
		=> TryRemoveInternal(item.Key, GetHashCode(item.Key), out _, true, item.Value);

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private bool TryRemoveInternal(TKey key, int hashCode, [MaybeNullWhen(false)] out TValue value, bool matchValue, TValue? oldValue) {
		var tables = _tables;
		while (true) {
			var locks = tables.Locks;
			ref var local = ref GetBucketAndLock(tables, hashCode, out var lockNo);
			lock (locks[(int)lockNo]) {
				if (tables != _tables) {
					tables = _tables;
				} else {
					Node? prev = null;
					for (var curr = local; curr is not null; curr = curr.Next) {
						if (hashCode == curr.Hashcode && KeyEquals(curr.Key, key)) {
							if (matchValue && !EqualityComparer<TValue>.Default.Equals(oldValue, curr.Value)) {
								value = default;
								return false;
							}

							if (prev is null)
								Volatile.Write(ref local, curr.Next);
							else
								prev.Next = curr.Next;
							value = curr.Value;
							--tables.CountPerLock[(int)lockNo];
							return true;
						}

						prev = curr;
					}

					break;
				}
			}
		}

		value = default;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
		var tables = _tables;
		var hashCode = GetHashCode(key);
		var bucket = GetBucket(tables, hashCode);
		if (bucket is not null) {
			if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
				value = bucket.Value;
				return true;
			}

			for (var next = bucket.Next; next is not null; next = next.Next) {
				if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
					value = next.Value;
					return true;
				}
			}
		}

		value = default;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ReadOnlySpan<TKey> keys, Span<TValue> values, Span<bool> found) {
		if (keys.Length != values.Length || keys.Length != found.Length)
			throw new ArgumentException("Keys, values, and found spans must have the same length");
		if (keys.Length == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		for (var index = 0; index < keys.Length; ++index) {
			var key = keys[index];
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			var matched = false;
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					values[count++] = bucket.Value;
					found[index] = true;
					continue;
				}

				for (var next = bucket.Next; next is not null; next = next.Next) {
					if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
						values[count++] = next.Value;
						found[index] = true;
						matched = true;
						break;
					}
				}
			}

			if (!matched)
				found[index] = false;
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ICollection<TKey> keys, Span<TValue> values) {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					values[count++] = bucket.Value;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							values[count++] = next.Value;
							break;
						}
					}
				}
			}
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ICollection<TKey> keys, Span<TValue> values, Predicate<TValue> predicate) {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key) && predicate(bucket.Value)) {
					values[count++] = bucket.Value;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key) && predicate(next.Value)) {
							values[count++] = next.Value;
							break;
						}
					}
				}
			}
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryCountValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys) {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					++count;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							++count;
							break;
						}
					}
				}
			}
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryCountValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, Predicate<TValue> predicate) {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key) && predicate(bucket.Value)) {
					++count;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key) && predicate(next.Value)) {
							++count;
							break;
						}
					}
				}
			}
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TContainer>(ref TContainer container, ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					container.Add(key, bucket.Value);
					++values;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							container.Add(key, next.Value);
							++values;
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TContainer>(
		ref TContainer container,
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys,
		Predicate<TValue> predicate)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key) && predicate(bucket.Value)) {
					container.Add(key, bucket.Value);
					++values;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key) && predicate(next.Value)) {
							container.Add(key, next.Value);
							++values;
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesMapWhere<TMapped, TMapper, TContainer>(
		ref TContainer container,
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys,
		TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TContainer : IJoinedResultContainer<TKey, TMapped>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var count = 0;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					var result = mapper.MapOrFilter(bucket.Value);
					if (result.Include) {
						container.Add(key, result.Value);
						++count;
					}
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							var result = mapper.MapOrFilter(next.Value);
							if (result.Include) {
								container.Add(key, result.Value);
								++count;
							}

							break;
						}
					}
				}
			}
		}

		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TForeignKey, TContainer>(
		ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var pair in keys) {
			var key = pair.Key;
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					container.Add(pair.JoinedKey, bucket.Value);
					++values;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							container.Add(pair.JoinedKey, next.Value);
							++values;
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TForeignKey, TContainer>(
		ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys,
		Predicate<TValue> predicate)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var pair in keys) {
			var key = pair.Key;
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					if (predicate(bucket.Value)) {
						container.Add(pair.JoinedKey, bucket.Value);
						++values;
					}
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							if (predicate(next.Value)) {
								container.Add(pair.JoinedKey, next.Value);
								++values;
							}

							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesJoined<TForeignKey, TContainer>(
		ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var pair in keys) {
			var key = pair.Key;
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					container.Add(pair.JoinedKey, pair.Key, bucket.Value);
					++values;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							container.Add(pair.JoinedKey, pair.Key, next.Value);
							++values;
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesJoined<TForeignKey, TContainer>(
		ref TContainer container,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys,
		Predicate<TValue> predicate)
		where TForeignKey : notnull
		where TContainer : struct, IJoinedResultContainer<TForeignKey, TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var pair in keys) {
			var key = pair.Key;
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					if (predicate(bucket.Value)) {
						container.Add(pair.JoinedKey, pair.Key, bucket.Value);
						++values;
					}
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							if (predicate(next.Value)) {
								container.Add(pair.JoinedKey, pair.Key, next.Value);
								++values;
							}

							break;
						}
					}
				}
			}
		}

		return values;
	}

	/// <summary>
	/// Gets values for keys extracted from source items using a key selector.
	/// Calls container.Add(source, value) for each found value.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TContainer>(ref TContainer container, ReadOnlySpan<TKey> sources)
		where TContainer : struct, IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (sources.Length == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var key in sources) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is null)
				continue;
			if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
				container.Add(key, bucket.Value);
				++values;
				continue;
			}
			for (var next = bucket.Next; next is not null; next = next.Next) {
				if (hashCode != next.Hashcode || !KeyEquals(next.Key, key))
					continue;
				container.Add(key, next.Value);
				++values;
				break;
			}
		}

		return values;
	}

	/// <summary>
	/// Gets values for keys extracted from source items using a key selector.
	/// Calls container.Add(source, value) for each found value.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TSource, TContainer>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TKey> keySelector)
		where TContainer : struct, IJoinedResultContainer<TSource, TValue>, allows ref struct {
		if (sources.Length == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var source in sources) {
			var key = keySelector(source);
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is null)
				continue;
			if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
				container.Add(source, bucket.Value);
				++values;
				continue;
			}
			for (var next = bucket.Next; next is not null; next = next.Next) {
				if (hashCode != next.Hashcode || !KeyEquals(next.Key, key))
					continue;
				container.Add(source, next.Value);
				++values;
				break;
			}
		}

		return values;
	}

	/// <summary>
	/// Gets values for keys extracted from source items using a key selector.
	/// Calls container.Add(key, source, value) for each found value.
	/// Used for QueryResults continuation joins.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesWithSource<TSource, TContainer>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TKey> keySelector)
		where TContainer : struct, IJoinedSourceResultContainer<TKey, TSource, TValue>, allows ref struct {
		if (sources.Length == 0)
			return 0;
		var tables = _tables;
		var values = 0;
		foreach (var source in sources) {
			var key = keySelector(source);
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					container.Add(key, source, bucket.Value);
					++values;
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							container.Add(key, source, next.Value);
							++values;
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public List<TValue> TryGetValues(ICollection<TKey> keys) {
		var values = new List<TValue>(keys.Count);
		if (keys.Count == 0)
			return values;
		var tables = _tables;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					values.Add(bucket.Value);
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							values.Add(next.Value);
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public List<TValue> TryGetValues(ICollection<TKey> keys, Predicate<TValue> predicate) {
		var values = new List<TValue>(keys.Count);
		if (keys.Count == 0)
			return values;
		var tables = _tables;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key) && predicate(bucket.Value)) {
					values.Add(bucket.Value);
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key) && predicate(next.Value)) {
							values.Add(next.Value);
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal List<TValue> TryGetValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys) {
		var values = new List<TValue>(keys.Count);
		if (keys.Count == 0)
			return values;
		var tables = _tables;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					values.Add(bucket.Value);
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							values.Add(next.Value);
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal List<TValue> TryGetValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, Predicate<TValue> predicate) {
		var values = new List<TValue>(keys.Count);
		if (keys.Count == 0)
			return values;
		var tables = _tables;
		foreach (var key in keys) {
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key) && predicate(bucket.Value)) {
					values.Add(bucket.Value);
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key) && predicate(next.Value)) {
							values.Add(next.Value);
							break;
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public List<TValue> TryGetValues(ReadOnlySpan<TKey> keys) {
		var values = new List<TValue>(keys.Length);
		if (keys.Length == 0)
			return values;
		var tables = _tables;
		for (var index = 0; index < keys.Length; ++index) {
			var key = keys[index];
			var hashCode = GetHashCode(key);
			var bucket = GetBucket(tables, hashCode);
			if (bucket is not null) {
				if (hashCode == bucket.Hashcode && KeyEquals(bucket.Key, key)) {
					values.Add(bucket.Value);
				} else {
					for (var next = bucket.Next; next is not null; next = next.Next) {
						if (hashCode == next.Hashcode && KeyEquals(next.Key, key)) {
							values.Add(next.Value);
							break;
						}
					}
				}
			}
		}

		return values;
	}

	public void Clear() {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			if (AreAllBucketsEmpty(tables))
				return;
			var newTables = new Tables(
				new VolatileNode[HashHelpers.GetPrime(_initialCapacity)],
				tables.Locks,
				new int[tables.CountPerLock.Length]);
			_tables = newTables;
			_budget = Math.Max(1, newTables.Buckets.Length / newTables.Locks.Length);
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private bool UpdateOrIgnoreInternal<TArgs>(
		Tables tables,
		TKey key,
		int hashcode,
		Func<TKey, TValue, TArgs, TValue> updateOperation,
		bool acquireLock,
		TArgs args) {
		while (true) {
			var locks = tables.Locks;
			ref var local = ref GetBucketAndLock(tables, hashcode, out var lockNo);
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != _tables) {
					tables = _tables;
				} else {
					Node? prev = null;
					for (var curr = local; curr is not null; curr = curr.Next) {
						if (hashcode == curr.Hashcode && KeyEquals(curr.Key, key)) {
							var oldValue = curr.Value;
							if (oldValue is not null) {
								var newValue = updateOperation(key, oldValue, args);
								if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
									curr.Value = newValue;
								} else {
									var replacement = new Node(curr.Key, newValue, hashcode, curr.Next);
									if (prev is null)
										Volatile.Write(ref local, replacement);
									else
										prev.Next = replacement;
								}

								return true;
							}
						}

						prev = curr;
					}

					return false;
				}
			} finally {
				if (lockTaken)
					Monitor.Exit(locks[(int)lockNo]);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private UpdateResult TryAddOrUpdateInternal<TArgs>(
		Tables tables,
		TKey key,
		int hashcode,
		Func<TKey, TArgs, TValue> valueFactory,
		Func<TKey, TValue, TArgs, TValue> valueUpdater,
		bool acquireLock,
		TArgs args) {
		var newValue = default(TValue);
		bool resizeDesired;
		while (true) {
			var locks = tables.Locks;
			ref var local = ref GetBucketAndLock(tables, hashcode, out var lockNo);
			resizeDesired = false;
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != _tables) {
					tables = _tables;
				} else {
					var curr = local;
					Node? prev = null;
					while (curr is not null) {
						if (hashcode == curr.Hashcode && KeyEquals(curr.Key, key)) {
							var oldValue = curr.Value;
							if (oldValue is not null) {
								var updated = valueUpdater(key, oldValue, args);
								if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
									curr.Value = updated;
								} else {
									var replacement = new Node(curr.Key, updated, hashcode, curr.Next);
									if (prev is null)
										Volatile.Write(ref local, replacement);
									else
										prev.Next = replacement;
								}

								return new UpdateResult(AddOrUpdateOperation.Update, updated, oldValue, hashcode);
							}
						}

						prev = curr;
						curr = curr.Next;
					}

					newValue = valueFactory(key, args);
					var added = new Node(key, newValue!, hashcode, local);
					Volatile.Write(ref local, added);
					if (++tables.CountPerLock[(int)lockNo] > _budget)
						resizeDesired = true;
					break;
				}
			} finally {
				if (lockTaken)
					Monitor.Exit(locks[(int)lockNo]);
			}
		}

		if (resizeDesired)
			GrowTable(tables);
		return new UpdateResult(AddOrUpdateOperation.Add, newValue!, default, hashcode);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private AddOrUpdateOperation TryAddOrUpdateInternal(
		Tables tables,
		TKey key,
		int hashcode,
		TValue value,
		Func<TKey, TValue, TValue, bool> shouldAdd,
		bool acquireLock,
		out TValue? prevValue) {
		bool resizeDesired;
		while (true) {
			var locks = tables.Locks;
			ref var local = ref GetBucketAndLock(tables, hashcode, out var lockNo);
			resizeDesired = false;
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != _tables) {
					tables = _tables;
				} else {
					var curr = local;
					Node? prev = null;
					while (curr is not null) {
						if (hashcode == curr.Hashcode && KeyEquals(curr.Key, key)) {
							var oldValue = curr.Value;
							if (oldValue is not null) {
								if (shouldAdd(key, value, oldValue)) {
									if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
										curr.Value = value;
									} else {
										var replacement = new Node(curr.Key, value, hashcode, curr.Next);
										if (prev is null)
											Volatile.Write(ref local, replacement);
										else
											prev.Next = replacement;
									}

									prevValue = oldValue;
									return AddOrUpdateOperation.Update;
								}

								prevValue = oldValue;
								return AddOrUpdateOperation.Same;
							}
						}

						prev = curr;
						curr = curr.Next;
					}

					var added = new Node(key, value, hashcode, local);
					Volatile.Write(ref local, added);
					if (++tables.CountPerLock[(int)lockNo] > _budget)
						resizeDesired = true;
					break;
				}
			} finally {
				if (lockTaken)
					Monitor.Exit(locks[(int)lockNo]);
			}
		}

		if (resizeDesired)
			GrowTable(tables);
		prevValue = default;
		return AddOrUpdateOperation.Add;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetCountNoLocks(Tables tables) {
		var count = 0;
		foreach (var perLock in tables.CountPerLock)
			count += perLock;
		return count;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateResult AddOrUpdate<TArgs>(
		TKey key,
		Func<TKey, TArgs, TValue> factory,
		Func<TKey, TValue, TArgs, TValue> updater,
		TArgs args) {
		var tables = _tables;
		var hashCode = GetHashCode(key);
		return TryAddOrUpdateInternal(tables, key, hashCode, factory, updater, true, args);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateResult AddOrUpdate(TKey key, TValue newValue, Func<TKey, TValue, TValue, bool> shouldUpdate) {
		var tables = _tables;
		var hashCode = GetHashCode(key);
		return TryAddOrUpdateInternal(tables, key, hashCode, newValue, shouldUpdate, true, out var prevValue) switch {
			AddOrUpdateOperation.Add => new UpdateResult(AddOrUpdateOperation.Add, newValue, default, hashCode),
			AddOrUpdateOperation.Update => new UpdateResult(AddOrUpdateOperation.Update, newValue, prevValue, hashCode),
			AddOrUpdateOperation.Same => new UpdateResult(AddOrUpdateOperation.Same, prevValue!, default, hashCode),
			_ => throw new UnreachableException(),
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool UpdateOrIgnore<TArgs>(TKey key, Func<TKey, TValue, TArgs, TValue> updateOperation, TArgs args) {
		var tables = _tables;
		var hashCode = GetHashCode(key);
		return UpdateOrIgnoreInternal(tables, key, hashCode, updateOperation, true, args);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateOrRemoveResult<TValue> UpdateOrRemove<TArgs>(
		TKey key,
		Func<TKey, TValue, TArgs, (bool Keep, TValue? NewValue)> updateOrRemove,
		TArgs args) {
		var tables = _tables;
		var hashCode = GetHashCode(key);
		return UpdateOrRemoveInternal(tables, key, hashCode, updateOrRemove, args);
	}

	private UpdateOrRemoveResult<TValue> UpdateOrRemoveInternal<TArgs>(
		Tables tables,
		TKey key,
		int hashcode,
		Func<TKey, TValue, TArgs, (bool Keep, TValue? NewValue)> updateOrRemove,
		TArgs args) {
		while (true) {
			var locks = tables.Locks;
			ref var local = ref GetBucketAndLock(tables, hashcode, out var lockNo);
			lock (locks[(int)lockNo]) {
				if (tables != _tables) {
					tables = _tables;
				} else {
					Node? prev = null;
					for (var curr = local; curr is not null; curr = curr.Next) {
						if (hashcode == curr.Hashcode && KeyEquals(curr.Key, key)) {
							var oldValue = curr.Value;
							var (keep, newValue) = updateOrRemove(key, oldValue, args);
							if (!keep) {
								if (prev is null)
									Volatile.Write(ref local, curr.Next);
								else
									prev.Next = curr.Next;
								--tables.CountPerLock[(int)lockNo];
								return new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.Remove, oldValue, default, hashcode);
							}

							if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
								curr.Value = newValue!;
							} else {
								var replacement = new Node(curr.Key, newValue!, hashcode, curr.Next);
								if (prev is null)
									Volatile.Write(ref local, replacement);
								else
									prev.Next = replacement;
							}

							return new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.Update, oldValue, newValue, hashcode);
						}

						prev = curr;
					}

					break;
				}
			}
		}

		return UpdateOrRemoveResult<TValue>.NotFound(hashcode);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool AreAllBucketsEmpty(Tables tables) => !tables.CountPerLock.AsSpan().ContainsAnyExcept(0);

	internal int CountValues(Predicate<TValue> predicate) {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			if (GetCountNoLocks(tables) == 0)
				return 0;
			var count = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					if (predicate(node.Value))
						++count;
				}
			}

			return count;
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal ArraySegment<TValue> GetValues(Predicate<TValue> predicate) {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return Array.Empty<TValue>();
			var array = new TValue[count];
			var actualCount = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					if (predicate(node.Value))
						array[actualCount++] = node.Value;
				}
			}

			return new ArraySegment<TValue>(array, 0, actualCount);
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInit<TContainer>(ref TContainer container, Predicate<TValue> predicate)
		where TContainer : IResultContainerInitializer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return;
			container.Init(count);
			var actualCount = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					if (predicate(node.Value)) {
						container.Add(node.Key, node.Value);
						++actualCount;
					}
				}
			}

			container.Seal(actualCount);
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInit<TContainer>(ref TContainer container)
		where TContainer : IResultContainerInitializer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return;
			container.Init(count);
			var actualCount = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					++actualCount;
					container.Add(node.Key, node.Value);
				}
			}

			container.Seal(actualCount);
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInitMapWhere<TMapped, TMapper, TContainer>(ref TContainer container, TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TContainer : IResultContainerInitializer<TKey, TMapped>, allows ref struct {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return;
			container.Init(count);
			var actualCount = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					var result = mapper.MapOrFilter(node.Value);
					if (result.Include) {
						container.Add(node.Key, result.Value);
						++actualCount;
					}
				}
			}

			container.Seal(actualCount);
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValues<TContainer>(ref TContainer container)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			if (GetCountNoLocks(tables) == 0)
				return;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next)
					container.Add(node.Key, node.Value);
			}
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValues<TContainer>(ref TContainer container, Predicate<TValue> predicate)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			if (GetCountNoLocks(tables) == 0)
				return;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next) {
					if (predicate(node.Value))
						container.Add(node.Key, node.Value);
				}
			}
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	internal TValue[] GetValues() {
		var locksAcquired = 0;
		var values = Array.Empty<TValue>();
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return values;
			values = new TValue[count];
			var index = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next)
					values[index++] = node.Value;
			}
		} finally {
			ReleaseLocks(locksAcquired);
		}

		return values;
	}

	internal KeyValuePair<TKey, TValue>[] GetKeyValues() {
		var locksAcquired = 0;
		var items = Array.Empty<KeyValuePair<TKey, TValue>>();
		try {
			var tables = AcquireAllLocks(ref locksAcquired);
			var count = GetCountNoLocks(tables);
			if (count == 0)
				return items;
			items = new KeyValuePair<TKey, TValue>[count];
			var index = 0;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node is not null; node = node.Next)
					items[index++] = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
			}
		} finally {
			ReleaseLocks(locksAcquired);
		}

		return items;
	}

	private void GrowTable(Tables tables) {
		var locksAcquired = 0;
		try {
			AcquireFirstLock(out locksAcquired);
			if (tables != _tables)
				return;
			if (GetCountNoLocks(tables) < tables.Buckets.Length / 4) {
				_budget = 2 * _budget;
				if (_budget < 0)
					_budget = int.MaxValue;
				return;
			}

			int newLength;
			var doubled = tables.Buckets.Length * 2;
			if (doubled < 0 || (newLength = HashHelpers.GetPrime(doubled)) > Array.MaxLength) {
				newLength = Array.MaxLength;
				_budget = int.MaxValue;
			}

			var locks = tables.Locks;
			if (_growLockArray && tables.Locks.Length < MaxLockNumber) {
				locks = new object[tables.Locks.Length * 2];
				Array.Copy(tables.Locks, locks, tables.Locks.Length);
				for (var i = tables.Locks.Length; i < locks.Length; ++i)
					locks[i] = new object();
			}

			var buckets = new VolatileNode[newLength];
			var countPerLock = new int[locks.Length];
			var newTables = new Tables(buckets, locks, countPerLock);
			AcquirePostFirstLock(tables, ref locksAcquired);
			foreach (var bucket in tables.Buckets) {
				Node? next;
				for (var node = bucket.Node; node is not null; node = next) {
					next = node.Next;
					ref var local = ref GetBucketAndLock(newTables, node.Hashcode, out var lockNo);
					local = new Node(node.Key, node.Value, node.Hashcode, local);
					++countPerLock[(int)lockNo];
				}
			}

			_budget = Math.Max(1, buckets.Length / locks.Length);
			_tables = newTables;
		} finally {
			ReleaseLocks(locksAcquired);
		}
	}

	// Lock-array growth copies the old lock object references forward (Array.Copy in GrowTable),
	// so element identity is stable across generations for every index a thread has entered.
	// Two consequences these helpers rely on: entering Locks[0] through a fresh _tables read
	// always targets the one true first lock, and once it is held _tables is pinned — GrowTable
	// swaps it only while holding every lock. ReleaseLocks may therefore re-read _tables.Locks
	// and still exit exactly the objects that were entered, even if the array was swapped or
	// acquisition threw midway. Pinned by ConcurrentCacheStoreLockingTests.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Tables AcquireAllLocks(ref int locksAcquired) {
		AcquireFirstLock(out locksAcquired);
		var tables = _tables;
		AcquirePostFirstLock(tables, ref locksAcquired);
		return tables;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AcquireFirstLock(out int locksAcquired) {
		Monitor.Enter(_tables.Locks[0]);
		locksAcquired = 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AcquirePostFirstLock(Tables tables, ref int locksAcquired) {
		var locks = tables.Locks;
		for (var i = 1; i < locks.Length; ++i) {
			Monitor.Enter(locks[i]);
			++locksAcquired;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReleaseLocks(int locksAcquired) {
		var locks = _tables.Locks;
		for (var i = 0; i < locksAcquired; ++i)
			Monitor.Exit(locks[i]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Node? GetBucket(Tables tables, int hashcode) {
		var buckets = tables.Buckets;
		return IntPtr.Size == 8
			? buckets[(int)HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.FastModBucketsMultiplier)].Node
			: buckets[(int)((uint)hashcode % (uint)buckets.Length)].Node;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ref Node? GetBucketAndLock(Tables tables, int hashcode, out uint lockNo) {
		var buckets = tables.Buckets;
		var index = IntPtr.Size != 8
			? (uint)hashcode % (uint)buckets.Length
			: HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.FastModBucketsMultiplier);
		lockNo = index % (uint)tables.Locks.Length;
		// ReSharper disable once ByRefArgumentIsVolatileField
		return ref buckets[(int)index].Node;
	}

	public readonly struct UpdateResult {
		public readonly AddOrUpdateOperation Operation;
		public readonly TValue Value;
		public readonly TValue? OldValue;
		public readonly int KeyHash;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public UpdateResult(AddOrUpdateOperation operation, TValue newValue, TValue? oldValue, int keyHash) {
			Operation = operation;
			Value = newValue;
			OldValue = oldValue;
			KeyHash = keyHash;
		}
	}

	private struct VolatileNode {
		internal volatile Node? Node;
	}

	private sealed class Node {
		internal readonly int Hashcode;
		internal readonly TKey Key;
		internal volatile Node? Next;
		internal TValue Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Node(TKey key, TValue value, int hashcode, Node? next) {
			Key = key;
			Value = value;
			Next = next;
			Hashcode = hashcode;
		}
	}

	private sealed class Tables {
		internal readonly VolatileNode[] Buckets;
		internal readonly int[] CountPerLock;
		internal readonly ulong FastModBucketsMultiplier;
		internal readonly object[] Locks;

		internal Tables(VolatileNode[] buckets, object[] locks, int[] countPerLock) {
			Buckets = buckets;
			Locks = locks;
			CountPerLock = countPerLock;
			if (IntPtr.Size == 8)
				FastModBucketsMultiplier = HashHelpers.GetFastModMultiplier((uint)buckets.Length);
		}
	}

	internal sealed class IDictionaryDebugView {
		private readonly ConcurrentCacheStore<TKey, TValue> _dictionary;

		public IDictionaryDebugView(ConcurrentCacheStore<TKey, TValue> dictionary) {
			ArgumentNullException.ThrowIfNull(dictionary);
			_dictionary = dictionary;
		}

		[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
		public DebugViewDictionaryItem[] Items {
			get {
				var kvps = _dictionary.GetKeyValues();
				var items = new DebugViewDictionaryItem[kvps.Length];
				for (var index = 0; index < kvps.Length; ++index)
					items[index] = new DebugViewDictionaryItem(kvps[index]);
				return items;
			}
		}

		[DebuggerDisplay("{Value}", Name = "[{Key}]")]
		internal readonly struct DebugViewDictionaryItem {
			public DebugViewDictionaryItem(TKey key, TValue value) {
				Key = key;
				Value = value;
			}

			public DebugViewDictionaryItem(KeyValuePair<TKey, TValue> keyValue) {
				Key = keyValue.Key;
				Value = keyValue.Value;
			}

			// ReSharper disable once UnusedAutoPropertyAccessor.Global — used by [DebuggerDisplay] Name template
			[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
			public TKey Key { get; }

			[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
			public TValue Value { get; }
		}
	}
}

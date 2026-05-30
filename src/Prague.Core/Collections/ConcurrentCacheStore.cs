using Prague.Core.Utils;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#nullable enable
namespace Prague.Core.Collections;

[DebuggerTypeProxy(typeof(ConcurrentCacheStore<,>.IDictionaryDebugView))]
[DebuggerDisplay("Count = {Count}")]
public class ConcurrentCacheStore<TKey, TValue> where TKey : notnull {
	private const int DefaultCapacity = 31 /*0x1F*/;
	private const int MaxLockNumber = 1024 /*0x0400*/;
	private readonly bool _comparerIsDefaultForClasses;
	private readonly bool _growLockArray;
	private readonly int _initialCapacity;
	private int _budget;

	private volatile ConcurrentCacheStore<
#nullable disable
		TKey, TValue>.Tables _tables;

	public ConcurrentCacheStore()
		: this(ConcurrentCacheStore<TKey, TValue>.DefaultConcurrencyLevel, 31 /*0x1F*/, true,
			(IEqualityComparer<TKey>)null) {
	}

	public ConcurrentCacheStore(int concurrencyLevel, int capacity)
		: this(concurrencyLevel, capacity, false, (IEqualityComparer<TKey>)null) {
	}

	public ConcurrentCacheStore(int concurrencyLevel, int capacity,
#nullable enable
		IEqualityComparer<TKey>? comparer)
		: this(concurrencyLevel, capacity, false, comparer) {
	}

	private ConcurrentCacheStore(
		int concurrencyLevel,
		int capacity,
		bool growLockArray,
		IEqualityComparer<TKey>? comparer) {
		if (capacity < concurrencyLevel)
			capacity = concurrencyLevel;
		capacity = HashHelpers.GetPrime(capacity);
		var locks = new object[concurrencyLevel];
		locks[0] = (object)locks;
		for (var index = 1; index < locks.Length; ++index)
			locks[index] = new object();
		var countPerLock = new int[locks.Length];
		var buckets =
			new ConcurrentCacheStore<TKey, TValue>.VolatileNode[capacity];
		if (typeof(TKey).IsValueType) {
			if (comparer != null && comparer == EqualityComparer<TKey>.Default)
				comparer = (IEqualityComparer<TKey>)null;
		} else {
			if (comparer == null)
				comparer = (IEqualityComparer<TKey>)EqualityComparer<TKey>.Default;
			comparer = HashCollectionsTools.GetEqualityComparer<TKey>(comparer);
			this._comparerIsDefaultForClasses = comparer == EqualityComparer<TKey>.Default;
		}

		this._tables = new ConcurrentCacheStore<TKey, TValue>.Tables(buckets, locks, countPerLock, comparer);
		this._growLockArray = growLockArray;
		this._initialCapacity = capacity;
		this._budget = buckets.Length / locks.Length;
	}

	public int Count {
		get {
			var locksAcquired = 0;
			try {
				this.AcquireAllLocks(ref locksAcquired);
				return this.GetCountNoLocks();
			} finally {
				this.ReleaseLocks(locksAcquired);
			}
		}
	}

	private static int DefaultConcurrencyLevel => Environment.ProcessorCount;

	// JIT-devirtualized hash/equals via DefaultKeyComparer<TKey>. The IEqualityComparer<TKey>?
	// parameter is preserved for ABI compatibility with the many call sites but ignored —
	// the comparer choice is now baked into the type (struct generic specialization). For
	// value types implementing IEquatable<T>, EqualityComparer<T>.Default folds to a direct
	// call; for string, DefaultKeyComparer special-cases via Unsafe.As<T,string>.
	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private int GetHashCode(IEqualityComparer<TKey>? _, TKey key)
		=> key is null ? 0 : default(DefaultKeyComparer<TKey>).GetHashCode(key);

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private static bool NodeEqualsKey(
		IEqualityComparer<TKey>? _,
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Node node,
#nullable enable
		TKey key)
		=> default(DefaultKeyComparer<TKey>).Equals(node.Key, key);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(TKey key) => this.TryGetValue(key, out var _);

	public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value) {
		return this.TryRemoveInternal(key, out value, false, default(TValue));
	}

	public bool TryRemove(KeyValuePair<TKey, TValue> item) {
		return this.TryRemoveInternal(item.Key, out var _, true, item.Value);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private bool TryRemoveInternal(TKey key, [MaybeNullWhen(false)] out TValue value, bool matchValue, TValue? oldValue) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var hashCode = this.GetHashCode(comparer, key);
		while (true) {
			var locks = tables.Locks;
			uint lockNo;
			ref ConcurrentCacheStore<TKey, TValue>.Node local =
				ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables, hashCode, out lockNo);
			var index = (int)lockNo;
			lock (locks[index]) {
				if (tables != this._tables) {
					tables = this._tables;
					if (comparer != tables.Comparer) {
						comparer = tables.Comparer;
						hashCode = this.GetHashCode(comparer, key);
					}
				} else {
					var node1 = (ConcurrentCacheStore<TKey, TValue>.Node)null;
					for (var node2 = local; node2 != null; node2 = node2.Next) {
						if (hashCode == node2.Hashcode && ConcurrentCacheStore<TKey, TValue>.NodeEqualsKey(comparer, node2, key)) {
							if (matchValue && !EqualityComparer<TValue>.Default.Equals(oldValue, node2.Value)) {
								value = default(TValue);
								return false;
							}

							if (node1 == null)
								Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node2.Next);
							else
								node1.Next = node2.Next;
							value = node2.Value;
							--tables.CountPerLock[(int)lockNo];
							return true;
						}

						node1 = node2;
					}

					break;
				}
			}
		}

		value = default(TValue);
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
			ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
			if (bucket != null) {
				if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
					value = bucket.Value;
					return true;
				}

				for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
						value = next.Value;
						return true;
					}
				}
			}
		} else {
			var hashCode = this.GetHashCode(comparer, key);
			ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
			if (bucket != null) {
				if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
					value = bucket.Value;
					return true;
				}

				for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
						value = next.Value;
						return true;
					}
				}
			}
		}

		value = default(TValue);
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ReadOnlySpan<TKey> keys, Span<TValue> values, Span<bool> found) {
		if (keys.Length != values.Length || keys.Length != found.Length)
			throw new ArgumentException("Keys, values, and found spans must have the same length");
		if (keys.Length == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values1 = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			for (var index = 0; index < keys.Length; ++index) {
				var y = keys[index];
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				var matched = false;
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y)) {
						values[values1++] = bucket.Value;
						found[index] = true;
						continue;
					}

					for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
						if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y)) {
							values[values1++] = next.Value;
							found[index] = true;
							matched = true;
							break;
						}
					}
				}

				if (!matched) {
					values[index] = default(TValue);
					found[index] = false;
				}
			}
		} else {
			for (var index = 0; index < keys.Length; ++index) {
				var key = keys[index];
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				var matched = false;
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						values[values1++] = bucket.Value;
						found[index] = true;
						continue;
					}

					for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
						if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
							values[values1++] = next.Value;
							found[index] = true;
							matched = true;
							break;
						}
					}
				}

				if (!matched) found[index] = false;
			}
		}

		return values1;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ICollection<TKey> keys, Span<TValue> values) {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values1 = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						values[values1++] = bucket.Value;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								values[values1++] = next.Value;
								++values1;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						values[values1++] = bucket.Value;
						++values1;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								values[values1++] = next.Value;
								++values1;
								break;
							}
						}
					}
				}
			}
		}

		return values1;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public int TryGetValues(ICollection<TKey> keys, Span<TValue> values, Predicate<TValue> predicate) {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values1 = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key) &&
					    predicate(bucket.Value)) {
						values[values1++] = bucket.Value;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key) &&
							    predicate(next.Value)) {
								values[values1++] = next.Value;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key) && predicate(bucket.Value)) {
						values[values1++] = bucket.Value;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key) && predicate(next.Value)) {
								values[values1++] = next.Value;
								break;
							}
						}
					}
				}
			}
		}

		return values1;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryCountValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys) {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var num = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var y in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y)) {
						++num;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y)) {
								++num;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						++num;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								++num;
								break;
							}
						}
					}
				}
			}
		}

		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryCountValues(ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys, Predicate<TValue> predicate) {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var num = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var y in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y) &&
					    predicate(bucket.Value)) {
						++num;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y) &&
							    predicate(next.Value)) {
								++num;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key) && predicate(bucket.Value)) {
						++num;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key) && predicate(next.Value)) {
								++num;
								break;
							}
						}
					}
				}
			}
		}

		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TCointainer>(ref TCointainer cointainer, ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys)
		where TCointainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						cointainer.Add(key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								cointainer.Add(key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						cointainer.Add(key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								cointainer.Add(key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys,
		Predicate<TValue> predicate)
		where TCointainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key) &&
					    predicate(bucket.Value)) {
						cointainer.Add(key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key) &&
							    predicate(next.Value)) {
								cointainer.Add(key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key) && predicate(bucket.Value)) {
						cointainer.Add(key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key) && predicate(next.Value)) {
								cointainer.Add(key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesMapWhere<TMapped, TMapper, TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<TKey, DefaultKeyComparer<TKey>> keys,
		TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TCointainer : IJoinedResultContainer<TKey, TMapped>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var valuesMapWhere = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						var cacheMapResult = mapper.MapOrFilter(bucket.Value);
						if (cacheMapResult.Include) {
							cointainer.Add(key, cacheMapResult.Value);
							++valuesMapWhere;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								var cacheMapResult = mapper.MapOrFilter(next.Value);
								if (cacheMapResult.Include) {
									cointainer.Add(key, cacheMapResult.Value);
									++valuesMapWhere;
									break;
								}

								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						var cacheMapResult = mapper.MapOrFilter(bucket.Value);
						if (cacheMapResult.Include) {
							cointainer.Add(key, cacheMapResult.Value);
							++valuesMapWhere;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								var cacheMapResult = mapper.MapOrFilter(next.Value);
								if (cacheMapResult.Include) {
									cointainer.Add(key, cacheMapResult.Value);
									++valuesMapWhere;
									break;
								}

								break;
							}
						}
					}
				}
			}
		}

		return valuesMapWhere;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TForeignKey, TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys)
		where TForeignKey : notnull
		where TCointainer : struct, IJoinedResultContainer<TForeignKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						cointainer.Add(joinedKeyPair.JoinedKey, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								cointainer.Add(joinedKeyPair.JoinedKey, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						cointainer.Add(joinedKeyPair.JoinedKey, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								cointainer.Add(joinedKeyPair.JoinedKey, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValues<TForeignKey, TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys,
		Predicate<TValue> predicate)
		where TForeignKey : notnull
		where TCointainer : struct, IJoinedResultContainer<TForeignKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						if (predicate(bucket.Value)) {
							cointainer.Add(joinedKeyPair.JoinedKey, bucket.Value);
							++values;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								if (predicate(next.Value)) {
									cointainer.Add(joinedKeyPair.JoinedKey, next.Value);
									++values;
									break;
								}

								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						if (predicate(bucket.Value)) {
							cointainer.Add(joinedKeyPair.JoinedKey, bucket.Value);
							++values;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								if (predicate(next.Value)) {
									cointainer.Add(joinedKeyPair.JoinedKey, next.Value);
									++values;
									break;
								}

								break;
							}
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesJoined<TForeignKey, TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys)
		where TForeignKey : notnull
		where TCointainer : struct, IJoinedResultContainer<TForeignKey, TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, bucket.Value);
						++values;
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		}

		return values;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal int TryGetValuesJoined<TForeignKey, TCointainer>(
		ref TCointainer cointainer,
		ref ValueSet<JoinedKeyPair<TForeignKey, TKey>, DefaultKeyComparer<JoinedKeyPair<TForeignKey, TKey>>> keys,
		Predicate<TValue> predicate)
		where TForeignKey : notnull
		where TCointainer : struct, IJoinedResultContainer<TForeignKey, TKey, TValue>, allows ref struct {
		if (keys.Count == 0)
			return 0;
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						if (predicate(bucket.Value)) {
							cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, bucket.Value);
							++values;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								if (predicate(next.Value)) {
									cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, next.Value);
									++values;
									break;
								}

								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var joinedKeyPair in keys) {
				var key = joinedKeyPair.Key;
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						if (predicate(bucket.Value)) {
							cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, bucket.Value);
							++values;
						}
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								if (predicate(next.Value)) {
									cointainer.Add(joinedKeyPair.JoinedKey, joinedKeyPair.Key, next.Value);
									++values;
									break;
								}

								break;
							}
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
	internal int TryGetValues<TContainer>(
		ref TContainer container,
		ReadOnlySpan<TKey> sources)
		where TContainer : struct, IJoinedResultContainer<TKey, TValue>, allows ref struct {

		if (sources.Length == 0)
			return 0;

		Tables tables = _tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in sources) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket is null)
					continue;
				if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
					container.Add(key, bucket.Value);
					++values;
					continue;
				}
				for (Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode != next.Hashcode || !EqualityComparer<TKey>.Default.Equals(next.Key, key))
						continue;
					container.Add(key, next.Value);
					++values;
					break;
				}
			}
		} else {
			foreach (var key in sources) {
				var hashCode = GetHashCode(comparer, key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket == null)
					continue;
				if (hashCode == bucket.Hashcode && comparer!.Equals(bucket.Key, key)) {
					container.Add(key, bucket.Value);
					++values;
					continue;
				}
				for (Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode != next.Hashcode || !comparer!.Equals(next.Key, key))
						continue;
					container.Add(key, next.Value);
					++values;
					break;
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
	internal int TryGetValues<TSource, TContainer>(
		ref TContainer container,
		ReadOnlySpan<TSource> sources,
		Func<TSource, TKey> keySelector)
		where TContainer : struct, IJoinedResultContainer<TSource, TValue>, allows ref struct {

		if (sources.Length == 0)
			return 0;

		Tables tables = _tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var source in sources) {
				var key = keySelector(source);
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket is null)
					continue;
				if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
					container.Add(source, bucket.Value);
					++values;
					continue;
				}
				for (Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode != next.Hashcode || !EqualityComparer<TKey>.Default.Equals(next.Key, key))
						continue;
					container.Add(source, next.Value);
					++values;
					break;
				}
			}
		} else {
			foreach (var source in sources) {
				var key = keySelector(source);
				var hashCode = GetHashCode(comparer, key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket == null)
					continue;
				if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
					container.Add(source, bucket.Value);
					++values;
					continue;
				}
				for (Node next = bucket.Next; next != null; next = next.Next) {
					if (hashCode != next.Hashcode || !comparer.Equals(next.Key, key))
						continue;
					container.Add(source, next.Value);
					++values;
					break;
				}
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
		Tables tables = _tables;
		var comparer = tables.Comparer;
		var values = 0;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var source in sources) {
				var key = keySelector(source);
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						container.Add(key, source, bucket.Value);
						++values;
					} else {
						for (Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								container.Add(key, source, next.Value);
								++values;
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var source in sources) {
				var key = keySelector(source);
				var hashCode = GetHashCode(comparer, key);
				Node bucket = GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						container.Add(key, source, bucket.Value);
						++values;
					} else {
						for (Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								container.Add(key, source, next.Value);
								++values;
								break;
							}
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
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key)) {
								values.Add(next.Value);
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								values.Add(next.Value);
								break;
							}
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
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, key) &&
					    predicate(bucket.Value)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, key) &&
							    predicate(next.Value)) {
								values.Add(next.Value);
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in (IEnumerable<TKey>)keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key) && predicate(bucket.Value)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key) && predicate(next.Value)) {
								values.Add(next.Value);
								break;
							}
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
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var y in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y)) {
								values.Add(next.Value);
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								values.Add(next.Value);
								break;
							}
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
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			foreach (var y in keys) {
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y) &&
					    predicate(bucket.Value)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y) &&
							    predicate(next.Value)) {
								values.Add(next.Value);
								break;
							}
						}
					}
				}
			}
		} else {
			foreach (var key in keys) {
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key) && predicate(bucket.Value)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key) && predicate(next.Value)) {
								values.Add(next.Value);
								break;
							}
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
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var comparer = tables.Comparer;
		if (typeof(TKey).IsValueType && comparer == null) {
			var readOnlySpan = keys;
			for (var index = 0; index < readOnlySpan.Length; ++index) {
				var y = readOnlySpan[index];
				var hashCode = default(DefaultKeyComparer<TKey>).GetHashCode(y);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && EqualityComparer<TKey>.Default.Equals(bucket.Key, y)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && EqualityComparer<TKey>.Default.Equals(next.Key, y)) {
								values.Add(next.Value);
								break;
							}
						}
					}
				}
			}
		} else {
			var readOnlySpan = keys;
			for (var index = 0; index < readOnlySpan.Length; ++index) {
				var key = readOnlySpan[index];
				var hashCode = this.GetHashCode(comparer, key);
				ConcurrentCacheStore<TKey, TValue>.Node bucket = ConcurrentCacheStore<TKey, TValue>.GetBucket(tables, hashCode);
				if (bucket != null) {
					if (hashCode == bucket.Hashcode && comparer.Equals(bucket.Key, key)) {
						values.Add(bucket.Value);
					} else {
						for (ConcurrentCacheStore<TKey, TValue>.Node next = bucket.Next; next != null; next = next.Next) {
							if (hashCode == next.Hashcode && comparer.Equals(next.Key, key)) {
								values.Add(next.Value);
								break;
							}
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
			this.AcquireAllLocks(ref locksAcquired);
			if (this.AreAllBucketsEmpty())
				return;
			ConcurrentCacheStore<TKey, TValue>.Tables tables1 = this._tables;
			var tables2 = new ConcurrentCacheStore<TKey, TValue>.Tables(
				new ConcurrentCacheStore<TKey, TValue>.VolatileNode[HashHelpers.GetPrime(this._initialCapacity)], tables1.Locks,
				new int[tables1.CountPerLock.Length], tables1.Comparer);
			this._tables = tables2;
			this._budget = Math.Max(1, tables2.Buckets.Length / tables2.Locks.Length);
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private bool UpdateOrIgnoreInternal<TArgs>(
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
#nullable enable
		TKey key,
		int? nullableHashcode,
		Func<TKey, TValue, TArgs, TValue> updateOperation,
		bool acquireLock,
		TArgs args) {
		IEqualityComparer<TKey> comparer = tables.Comparer;
		var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
		while (true) {
			var locks = tables.Locks;
			uint lockNo;
			ref ConcurrentCacheStore<TKey, TValue>.Node local =
				ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables, hashcode, out lockNo);
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != this._tables) {
					tables = this._tables;
					if (comparer != tables.Comparer) {
						comparer = tables.Comparer;
						hashcode = this.GetHashCode(comparer, key);
					}
				} else {
					var node1 = local;
					var node2 = (ConcurrentCacheStore<TKey, TValue>.Node)null;
					for (; node1 != null; node1 = node1.Next) {
						if (hashcode == node1.Hashcode && ConcurrentCacheStore<TKey, TValue>.NodeEqualsKey(comparer, node1, key)) {
							var obj1 = node1.Value;
							if ((object)obj1 != null) {
								var obj2 = updateOperation(key, obj1, args);
								if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
									node1.Value = obj2;
								} else {
									var node3 =
										new ConcurrentCacheStore<TKey, TValue>.Node(node1.Key, obj2, hashcode, node1.Next);
									if (node2 == null)
										Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node3);
									else
										node2.Next = node3;
								}

								return true;
							}
						}

						node2 = node1;
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
	private ConcurrentCacheStore<
#nullable disable
		TKey, TValue>.UpdateResult TryAddOrUpdateInternal<TArgs>(
#nullable enable
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
#nullable enable
		TKey key,
		int? nullableHashcode,
		Func<TKey, TArgs, TValue> valueFactory,
		Func<TKey, TValue, TArgs, TValue> valueUpdater,
		bool acquireLock,
		TArgs args) {
		IEqualityComparer<TKey> comparer = tables.Comparer;
		var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
		var flag = false;
		var newValue1 = default(TValue);
		bool resizeDesired;
		bool forceRehashIfNonRandomized;
		while (true) {
			var locks = tables.Locks;
			uint lockNo;
			ref ConcurrentCacheStore<TKey, TValue>.Node local =
				ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables, hashcode, out lockNo);
			resizeDesired = false;
			forceRehashIfNonRandomized = false;
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != this._tables) {
					tables = this._tables;
					if (comparer != tables.Comparer) {
						comparer = tables.Comparer;
						hashcode = this.GetHashCode(comparer, key);
					}
				} else {
					var node1 = local;
					uint num = 0;
					var node2 = (ConcurrentCacheStore<TKey, TValue>.Node)null;
					while (node1 != null) {
						if (hashcode == node1.Hashcode && ConcurrentCacheStore<TKey, TValue>.NodeEqualsKey(comparer, node1, key)) {
							var oldValue = node1.Value;
							if ((object)oldValue != null) {
								var newValue2 = valueUpdater(key, oldValue, args);
								if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
									node1.Value = newValue2;
								} else {
									var node3 =
										new ConcurrentCacheStore<TKey, TValue>.Node(node1.Key, newValue2, hashcode, node1.Next);
									if (node2 == null)
										Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node3);
									else
										node2.Next = node3;
								}

								return new ConcurrentCacheStore<TKey, TValue>.UpdateResult(AddOrUpdateOperation.Update, newValue2,
									oldValue);
							}
						}

						node2 = node1;
						node1 = node1.Next;
						if (!typeof(TKey).IsValueType)
							++num;
					}

					if (!flag) {
						newValue1 = valueFactory(key, args);
						flag = true;
					}

					var node4 =
						new ConcurrentCacheStore<TKey, TValue>.Node(key, newValue1, hashcode, local);
					Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node4);
					if (++tables.CountPerLock[(int)lockNo] > this._budget)
						resizeDesired = true;
					if (!typeof(TKey).IsValueType) {
						if (num > 100U) {
							if (comparer is NonRandomizedStringEqualityComparer) {
								forceRehashIfNonRandomized = true;
								break;
							}

							break;
						}

						break;
					}

					break;
				}
			} finally {
				if (lockTaken)
					Monitor.Exit(locks[(int)lockNo]);
			}
		}

		if (resizeDesired | forceRehashIfNonRandomized)
			this.GrowTable(tables, resizeDesired, forceRehashIfNonRandomized);
		return new ConcurrentCacheStore<TKey, TValue>.UpdateResult(AddOrUpdateOperation.Add, newValue1, default(TValue));
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private AddOrUpdateOperation TryAddOrUpdateInternal(
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
#nullable enable
		TKey key,
		int? nullableHashcode,
		TValue value,
		Func<TKey, TValue, TValue, bool> shouldAdd,
		bool acquireLock,
		out TValue? prevValue) {
		IEqualityComparer<TKey> comparer = tables.Comparer;
		var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
		bool resizeDesired;
		bool forceRehashIfNonRandomized;
		while (true) {
			var locks = tables.Locks;
			uint lockNo;
			ref ConcurrentCacheStore<TKey, TValue>.Node local =
				ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables, hashcode, out lockNo);
			resizeDesired = false;
			forceRehashIfNonRandomized = false;
			var lockTaken = false;
			try {
				if (acquireLock)
					Monitor.Enter(locks[(int)lockNo], ref lockTaken);
				if (tables != this._tables) {
					tables = this._tables;
					if (comparer != tables.Comparer) {
						comparer = tables.Comparer;
						hashcode = this.GetHashCode(comparer, key);
					}
				} else {
					var node1 = local;
					uint num = 0;
					var node2 = (ConcurrentCacheStore<TKey, TValue>.Node)null;
					while (node1 != null) {
						if (hashcode == node1.Hashcode && ConcurrentCacheStore<TKey, TValue>.NodeEqualsKey(comparer, node1, key)) {
							var obj = node1.Value;
							if ((object)obj != null) {
								if (shouldAdd(key, value, obj)) {
									if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
										node1.Value = value;
									} else {
										var node3 =
											new ConcurrentCacheStore<TKey, TValue>.Node(node1.Key, value, hashcode, node1.Next);
										if (node2 == null)
											Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node3);
										else
											node2.Next = node3;
									}

									prevValue = obj;
									return AddOrUpdateOperation.Update;
								}

								prevValue = obj;
								return AddOrUpdateOperation.Same;
							}
						}

						node2 = node1;
						node1 = node1.Next;
						if (!typeof(TKey).IsValueType)
							++num;
					}

					var node4 =
						new ConcurrentCacheStore<TKey, TValue>.Node(key, value, hashcode, local);
					Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node4);
					if (++tables.CountPerLock[(int)lockNo] > this._budget)
						resizeDesired = true;
					if (!typeof(TKey).IsValueType) {
						if (num > 100U) {
							if (comparer is NonRandomizedStringEqualityComparer) {
								forceRehashIfNonRandomized = true;
								break;
							}

							break;
						}

						break;
					}

					break;
				}
			} finally {
				if (lockTaken)
					Monitor.Exit(locks[(int)lockNo]);
			}
		}

		if (resizeDesired | forceRehashIfNonRandomized)
			this.GrowTable(tables, resizeDesired, forceRehashIfNonRandomized);
		prevValue = default(TValue);
		return AddOrUpdateOperation.Add;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetCountNoLocks() => ((IEnumerable<int>)this._tables.CountPerLock).Sum();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ConcurrentCacheStore<
#nullable disable
		TKey, TValue>.UpdateResult AddOrUpdate<TArgs>(
#nullable enable
		TKey key,
		Func<TKey, TArgs, TValue> factory,
		Func<TKey, TValue, TArgs, TValue> updater,
		TArgs args) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var hashCode = this.GetHashCode(tables.Comparer, key);
		return this.TryAddOrUpdateInternal<TArgs>(tables, key, new int?(hashCode), factory, updater, true, args);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ConcurrentCacheStore<
#nullable disable
		TKey, TValue>.UpdateResult AddOrUpdate(
#nullable enable
		TKey key,
		TValue newValue,
		Func<TKey, TValue, TValue, bool> shouldUpdate) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var hashCode = this.GetHashCode(tables.Comparer, key);
		TValue prevValue;
		switch (this.TryAddOrUpdateInternal(tables, key, new int?(hashCode), newValue, shouldUpdate, true, out prevValue)) {
			case AddOrUpdateOperation.Add:
				return new ConcurrentCacheStore<TKey, TValue>.UpdateResult(AddOrUpdateOperation.Add, newValue, default(TValue));
			case AddOrUpdateOperation.Update:
				return new ConcurrentCacheStore<TKey, TValue>.UpdateResult(AddOrUpdateOperation.Update, newValue, prevValue);
			case AddOrUpdateOperation.Same:
				return new ConcurrentCacheStore<TKey, TValue>.UpdateResult(AddOrUpdateOperation.Same, prevValue,
					default(TValue));
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool UpdateOrIgnore<TArgs>(
		TKey key,
		Func<TKey, TValue, TArgs, TValue> updateOperation,
		TArgs args) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var hashCode = this.GetHashCode(tables.Comparer, key);
		return this.UpdateOrIgnoreInternal<TArgs>(tables, key, new int?(hashCode), updateOperation, true, args);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateOrRemoveResult<TValue> UpdateOrRemove<TArgs>(
		TKey key,
		Func<TKey, TValue, TArgs, (bool Keep, TValue? NewValue)> updateOrRemove,
		TArgs args) {
		ConcurrentCacheStore<TKey, TValue>.Tables tables = this._tables;
		var hashCode = this.GetHashCode(tables.Comparer, key);
		return this.UpdateOrRemoveInternal<TArgs>(tables, key, hashCode, updateOrRemove, args);
	}

	private UpdateOrRemoveResult<TValue> UpdateOrRemoveInternal<TArgs>(
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
#nullable enable
		TKey key,
		int hashcode,
		Func<TKey, TValue, TArgs, (bool Keep, TValue? NewValue)> updateOrRemove,
		TArgs args) {
		IEqualityComparer<TKey> comparer = tables.Comparer;
		while (true) {
			var locks = tables.Locks;
			uint lockNo;
			ref ConcurrentCacheStore<TKey, TValue>.Node local =
				ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables, hashcode, out lockNo);
			var index = (int)lockNo;
			lock (locks[index]) {
				if (tables != this._tables) {
					tables = this._tables;
					if (comparer != tables.Comparer) {
						comparer = tables.Comparer;
						hashcode = this.GetHashCode(comparer, key);
					}
				} else {
					var node1 = (ConcurrentCacheStore<TKey, TValue>.Node)null;
					for (var node2 = local; node2 != null; node2 = node2.Next) {
						if (hashcode == node2.Hashcode && ConcurrentCacheStore<TKey, TValue>.NodeEqualsKey(comparer, node2, key)) {
							var oldValue = node2.Value;
							(bool, TValue) valueTuple = updateOrRemove(key, oldValue, args);
							if (!valueTuple.Item1) {
								if (node1 == null)
									Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node2.Next);
								else
									node1.Next = node2.Next;
								--tables.CountPerLock[(int)lockNo];
								return new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.Remove, oldValue, default(TValue));
							}

							var newValue = valueTuple.Item2;
							if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>._isWriteAtomic) {
								node2.Value = newValue;
							} else {
								var node3 =
									new ConcurrentCacheStore<TKey, TValue>.Node(node2.Key, newValue, hashcode, node2.Next);
								if (node1 == null)
									Volatile.Write<ConcurrentCacheStore<TKey, TValue>.Node>(ref local, node3);
								else
									node1.Next = node3;
							}

							return new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.Update, oldValue, newValue);
						}

						node1 = node2;
					}

					break;
				}
			}
		}

		return UpdateOrRemoveResult<TValue>.NotFound;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool AreAllBucketsEmpty() {
		return !this._tables.CountPerLock.AsSpan<int>().ContainsAnyExcept<int>(0);
	}

	internal int CountValues(Predicate<TValue> predicate) {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					if (predicate(node.Value))
						++countNoLocks;
				}
			}

			return countNoLocks;
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal ArraySegment<TValue> GetValues(Predicate<TValue> predicate) {
		var locksAcquired = 0;
		var values = Array.Empty<TValue>();
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return (ArraySegment<TValue>)values;
			var array = new TValue[countNoLocks];
			var count = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					if (predicate(node.Value))
						array[count++] = node.Value;
				}
			}

			return new ArraySegment<TValue>(array, 0, count);
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInit<TContainer>(ref TContainer container, Predicate<TValue> predicate)
		where TContainer : IResultContainerInitializer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return;
			container.Init(countNoLocks);
			var actualCount = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					if (predicate(node.Value)) {
						container.Add(node.Key, node.Value);
						++actualCount;
					}
				}
			}

			container.Seal(actualCount);
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInit<TContainer>(ref TContainer container)
		where TContainer : IResultContainerInitializer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return;
			container.Init(countNoLocks);
			var actualCount = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					++actualCount;
					container.Add(node.Key, node.Value);
				}
			}

			container.Seal(actualCount);
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValuesInitMapWhere<TMapped, TMapper, TContainer>(
		ref TContainer container,
		TMapper mapper)
		where TMapper : struct, ICacheWhereMapper<TValue, TMapped>
		where TContainer : IResultContainerInitializer<TKey, TMapped>, allows ref struct {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return;
			container.Init(countNoLocks);
			var actualCount = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					var cacheMapResult = mapper.MapOrFilter(node.Value);
					if (cacheMapResult.Include) {
						container.Add(node.Key, cacheMapResult.Value);
						++actualCount;
					}
				}
			}

			container.Seal(actualCount);
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValues<TContainer>(ref TContainer container)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			if (this.GetCountNoLocks() == 0)
				return;
			var num = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					++num;
					container.Add(node.Key, node.Value);
				}
			}
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal void GetValues<TContainer>(ref TContainer container, Predicate<TValue> predicate)
		where TContainer : IJoinedResultContainer<TKey, TValue>, allows ref struct {
		var locksAcquired = 0;
		try {
			this.AcquireAllLocks(ref locksAcquired);
			if (this.GetCountNoLocks() == 0)
				return;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next) {
					if (predicate(node.Value))
						container.Add(node.Key, node.Value);
				}
			}
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	internal TValue[] GetValues() {
		var locksAcquired = 0;
		var values = Array.Empty<TValue>();
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return values;
			values = new TValue[countNoLocks];
			var num = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next)
					values[num++] = node.Value;
			}
		} finally {
			this.ReleaseLocks(locksAcquired);
		}

		return values;
	}

	internal KeyValuePair<TKey, TValue>[] GetKeyValues() {
		var locksAcquired = 0;
		var items = Array.Empty<KeyValuePair<TKey, TValue>>();
		try {
			this.AcquireAllLocks(ref locksAcquired);
			var countNoLocks = this.GetCountNoLocks();
			if (countNoLocks == 0)
				return items;
			items = new KeyValuePair<TKey, TValue>[countNoLocks];
			var num = 0;
			foreach (ConcurrentCacheStore<TKey, TValue>.VolatileNode bucket in this._tables.Buckets) {
				for (ConcurrentCacheStore<TKey, TValue>.Node node = bucket.Node; node != null; node = node.Next)
					items[num++] = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
			}
		} finally {
			this.ReleaseLocks(locksAcquired);
		}

		return items;
	}

	private void GrowTable(
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
		bool resizeDesired,
		bool forceRehashIfNonRandomized) {
		var locksAcquired = 0;
		try {
			this.AcquireFirstLock(ref locksAcquired);
			if (tables != this._tables)
				return;
			var length1 = tables.Buckets.Length;
			var equalityComparer = (IEqualityComparer<TKey>)null;
			if (forceRehashIfNonRandomized && tables.Comparer is NonRandomizedStringEqualityComparer comparer)
				equalityComparer = (IEqualityComparer<TKey>)comparer.GetUnderlyingEqualityComparer();
			if (resizeDesired) {
				if (equalityComparer == null && this.GetCountNoLocks() < tables.Buckets.Length / 4) {
					this._budget = 2 * this._budget;
					if (this._budget >= 0)
						return;
					this._budget = int.MaxValue;
					return;
				}

				int min;
				if ((min = tables.Buckets.Length * 2) < 0 || (length1 = HashHelpers.GetPrime(min)) > Array.MaxLength) {
					length1 = Array.MaxLength;
					this._budget = int.MaxValue;
				}
			}

			object[] objArray = tables.Locks;
			if (this._growLockArray && tables.Locks.Length < 1024 /*0x0400*/) {
				objArray = new object[tables.Locks.Length * 2];
				Array.Copy((Array)tables.Locks, (Array)objArray, tables.Locks.Length);
				for (var length2 = tables.Locks.Length; length2 < objArray.Length; ++length2)
					objArray[length2] = new object();
			}

			var buckets =
				new ConcurrentCacheStore<TKey, TValue>.VolatileNode[length1];
			var countPerLock = new int[objArray.Length];
			var tables1 =
				new ConcurrentCacheStore<TKey, TValue>.Tables(buckets, objArray, countPerLock,
					equalityComparer ?? tables.Comparer);
			ConcurrentCacheStore<TKey, TValue>.AcquirePostFirstLock(tables, ref locksAcquired);
			ConcurrentCacheStore<TKey, TValue>.Node next;
			foreach (var bucket in tables.Buckets) {
				for (var node = bucket.Node; node != null; node = next) {
					next = node.Next;
					var hashcode = equalityComparer == null ? node.Hashcode : equalityComparer.GetHashCode(node.Key);
					uint lockNo;
					ref ConcurrentCacheStore<TKey, TValue>.Node local =
						ref ConcurrentCacheStore<TKey, TValue>.GetBucketAndLock(tables1, hashcode, out lockNo);
					local = new ConcurrentCacheStore<TKey, TValue>.Node(node.Key, node.Value, hashcode, local);
					++countPerLock[(int)lockNo];
				}
			}

			this._budget = Math.Max(1, buckets.Length / objArray.Length);
			this._tables = tables1;
		} finally {
			this.ReleaseLocks(locksAcquired);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AcquireAllLocks(ref int locksAcquired) {
		this.AcquireFirstLock(ref locksAcquired);
		ConcurrentCacheStore<TKey, TValue>.AcquirePostFirstLock(this._tables, ref locksAcquired);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AcquireFirstLock(ref int locksAcquired) {
		Monitor.Enter(this._tables.Locks[0]);
		locksAcquired = 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AcquirePostFirstLock(
#nullable enable
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables,
		ref int locksAcquired) {
		object[] locks = tables.Locks;
		for (var index = 1; index < locks.Length; ++index) {
			Monitor.Enter(locks[index]);
			++locksAcquired;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReleaseLocks(int locksAcquired) {
		var locks = this._tables.Locks;
		for (var index = 0; index < locksAcquired; ++index)
			Monitor.Exit(locks[index]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static
#nullable enable
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Node
#nullable enable
		? GetBucket(ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Tables tables, int hashcode) {
		var buckets = tables.Buckets;
		return IntPtr.Size == 8
			? buckets[(int)HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.FastModBucketsMultiplier)].Node
			: buckets[(int)((uint)hashcode % (uint)buckets.Length)].Node;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ref
#nullable enable
		ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.Node
#nullable enable
		? GetBucketAndLock(
			ConcurrentCacheStore<
#nullable disable
				TKey, TValue>.Tables tables,
			int hashcode,
			out uint lockNo) {
		var buckets = tables.Buckets;
		var index = IntPtr.Size != 8
			? (uint)hashcode % (uint)buckets.Length
			: HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.FastModBucketsMultiplier);
		lockNo = index % (uint)tables.Locks.Length;
		return ref buckets[(int)index].Node;
	}

	public readonly struct UpdateResult {
		public readonly AddOrUpdateOperation Operation;

		public readonly
#nullable enable
			TValue Value;

		public readonly TValue? OldValue;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public UpdateResult(AddOrUpdateOperation operation, TValue newValue, TValue? oldValue) {
			var orUpdateOperation = operation;
			var obj1 = newValue;
			var obj2 = oldValue;
			this.Operation = orUpdateOperation;
			this.Value = obj1;
			this.OldValue = obj2;
		}
	}

	private struct VolatileNode {
		internal volatile ConcurrentCacheStore<
#nullable disable
				TKey, TValue>.Node
#nullable enable
			? Node;
	}

	private sealed class Node {
		internal readonly int Hashcode;
		internal readonly TKey Key;

		internal volatile ConcurrentCacheStore<
#nullable disable
				TKey, TValue>.Node
#nullable enable
			? Next;

		internal TValue Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Node(
			TKey key,
			TValue value,
			int hashcode,
			ConcurrentCacheStore<
#nullable disable
					TKey, TValue>.Node
#nullable enable
				? next) {
			this.Key = key;
			this.Value = value;
			this.Next = next;
			this.Hashcode = hashcode;
		}
	}

	private sealed class Tables {
		internal readonly ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.VolatileNode[] Buckets;

		internal readonly
#nullable enable
			IEqualityComparer<TKey>? Comparer;

		internal readonly int[] CountPerLock;
		internal readonly ulong FastModBucketsMultiplier;
		internal readonly object[] Locks;

		internal Tables(
			ConcurrentCacheStore<
#nullable disable
				TKey, TValue>.VolatileNode[] buckets,
#nullable enable
			object[] locks,
			int[] countPerLock,
			IEqualityComparer<TKey>? comparer) {
			this.Buckets = buckets;
			this.Locks = locks;
			this.CountPerLock = countPerLock;
			this.Comparer = comparer;
			if (IntPtr.Size != 8)
				return;
			this.FastModBucketsMultiplier = HashHelpers.GetFastModMultiplier((uint)buckets.Length);
		}
	}

	internal sealed class IDictionaryDebugView {
		private readonly ConcurrentCacheStore<TKey, TValue> _dictionary;

		public IDictionaryDebugView(ConcurrentCacheStore<TKey, TValue> dictionary) {
			ArgumentNullException.ThrowIfNull((object)dictionary, nameof(dictionary));
			this._dictionary = dictionary;
		}

		[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
		public ConcurrentCacheStore<
#nullable disable
			TKey, TValue>.IDictionaryDebugView.DebugViewDictionaryItem[] Items {
			get {
				var kvps = this._dictionary.GetKeyValues();
				var items =
					new ConcurrentCacheStore<TKey, TValue>.IDictionaryDebugView.DebugViewDictionaryItem[kvps.Length];
				for (var index = 0; index < kvps.Length; ++index)
					items[index] =
						new ConcurrentCacheStore<TKey, TValue>.IDictionaryDebugView.DebugViewDictionaryItem(kvps[index]);
				return items;
			}
		}

		[DebuggerDisplay("{Value}", Name = "[{Key}]")]
		internal readonly struct DebugViewDictionaryItem {
			public DebugViewDictionaryItem(
#nullable enable
				TKey key, TValue value) {
				this.Key = key;
				this.Value = value;
			}

			public DebugViewDictionaryItem(KeyValuePair<TKey, TValue> keyValue) {
				this.Key = keyValue.Key;
				this.Value = keyValue.Value;
			}

			[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
			public TKey Key { get; }

			[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
			public TValue Value { get; }
		}
	}
}
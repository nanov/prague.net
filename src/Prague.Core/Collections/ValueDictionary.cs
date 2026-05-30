using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Prague.Core.Utils;

namespace Prague.Core.Collections;

[SkipLocalsInit]
public struct ValueDictionary<TKey, TValue, TKeyComparer> : IDisposable
	where TKey : notnull, IEquatable<TKey>
	where TKeyComparer : struct, IKeyComparer<TKey> {
	private static readonly ArrayPool<int> MetadataPool = ArrayPool<int>.Shared;
	private static readonly ArrayPool<TKey> KeysPool = ArrayPool<TKey>.Shared;
	private static readonly ArrayPool<TValue> ValuesPool = ArrayPool<TValue>.Shared;

	private int[] _metadata;
	private TKey[] _keysArray;

	private Memory<TKey> _keys;      // working window
	private Memory<TValue> _values;  // working window

	private readonly int _capacityMask;
	private readonly TKeyComparer _comparer;

	public int Count { get; private set; }
	public int Offset { get; private set; }
	public readonly bool IsInitialized => _metadata != null;
	public readonly ReadOnlySpan<TKey> Keys => _keys.Span.Slice(0, Count);

	public readonly Span<TValue> Values => _values.Span.Slice(0, Count);
	public TValue[] ValuesArray { get; private set; }
	public readonly Span<TValue> ValuesMutable => _values.Span.Slice(0, Count);


	public ValueDictionary(bool shouldPool, int expectedCount, TKeyComparer comparer = default) {
		var powerOf2Capacity = GetPowerOf2Capacity(expectedCount);
		_capacityMask = powerOf2Capacity - 1;
		Count = 0;
		_comparer = comparer;
		_metadata = MetadataPool.Rent(powerOf2Capacity);
		Array.Fill(_metadata, -1, 0, powerOf2Capacity);
		_keysArray = KeysPool.Rent(expectedCount);
		ValuesArray = (shouldPool ? ValuesPool.Rent(expectedCount) : new TValue[expectedCount]);
		_keys = _keysArray.AsMemory(0, expectedCount);
		_values = ValuesArray.AsMemory(0, expectedCount);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void Add(TKey key, TValue value) {
		Debug.Assert(Count < _values.Length, "ValueDictionary capacity exceeded");
		var hashCode = GetHashCode(key);
		var capacityMask = _capacityMask;
		var num = hashCode & capacityMask;
		var metadata = _metadata;
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(metadata);
		while (Unsafe.Add(ref arrayDataReference, num) >= 0) {
			num = (num + 1) & capacityMask;
		}

		var count = Count;
		var keys = _keys.Span;
		var valuesArray = _values.Span;
		ref var arrayDataReference2 = ref MemoryMarshal.GetReference(keys);
		ref var arrayDataReference3 = ref MemoryMarshal.GetReference(valuesArray);
		Unsafe.Add(ref arrayDataReference2, count) = key;
		Unsafe.Add(ref arrayDataReference3, count) = value;
		Unsafe.Add(ref arrayDataReference, num) = count;
		Count = count + 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public ref TValue GetValueRef(TKey key) {
		var hashCode = GetHashCode(key);
		var capacityMask = _capacityMask;
		var num = hashCode & capacityMask;
		var num2 = num;
		var metadata = _metadata;
		var keys = _keys.Span;
		var valuesArray = _values.Span;
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(metadata);
		ref var arrayDataReference2 = ref MemoryMarshal.GetReference(keys);
		ref var arrayDataReference3 = ref MemoryMarshal.GetReference(valuesArray);
		do {
			var num3 = Unsafe.Add(ref arrayDataReference, num);
			if (num3 < 0) {
				return ref Unsafe.NullRef<TValue>();
			}

			if (KeyEquals(Unsafe.Add(ref arrayDataReference2, num3), key)) {
				return ref Unsafe.Add(ref arrayDataReference3, num3);
			}

			num = (num + 1) & capacityMask;
		} while (num != num2);

		return ref Unsafe.NullRef<TValue>();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public ref TValue GetValueRefOrAddDefault(TKey key, out bool exists) {
		var hashCode = GetHashCode(key);
		var capacityMask = _capacityMask;
		var slot = hashCode & capacityMask;
		var metadata = _metadata;
		var keysSpan = _keys.Span;
		var valuesSpan = _values.Span;
		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(metadata);
		ref var keysRef = ref MemoryMarshal.GetReference(keysSpan);
		ref var valuesRef = ref MemoryMarshal.GetReference(valuesSpan);
		while (true) {
			var index = Unsafe.Add(ref metaRef, slot);
			if (index < 0)
				break;

			if (KeyEquals(Unsafe.Add(ref keysRef, index), key)) {
				exists = true;
				return ref Unsafe.Add(ref valuesRef, index);
			}

			slot = (slot + 1) & capacityMask;
		}

		Debug.Assert(Count < valuesSpan.Length, "ValueDictionary capacity exceeded");
		var count = Count;
		Unsafe.Add(ref keysRef, count) = key;
		Unsafe.Add(ref valuesRef, count) = default(TValue);
		Unsafe.Add(ref metaRef, slot) = count;
		Count = count + 1;
		exists = false;
		return ref Unsafe.Add(ref valuesRef, count);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public readonly bool TryGetValue(TKey key, out TValue value) {
		var hashCode = GetHashCode(key);
		var capacityMask = _capacityMask;
		var num = hashCode & capacityMask;
		var metadata = _metadata;
		var keys = _keys.Span;
		var valuesArray = _values.Span;
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(metadata);
		ref var arrayDataReference2 = ref MemoryMarshal.GetReference(keys);
		ref var arrayDataReference3 = ref MemoryMarshal.GetReference(valuesArray);
		int num2;
		while (true) {
			num2 = Unsafe.Add(ref arrayDataReference, num);
			if (num2 < 0) {
				value = default(TValue);
				return false;
			}

			if (KeyEquals(Unsafe.Add(ref arrayDataReference2, num2), key)) {
				break;
			}

			num = (num + 1) & capacityMask;
		}

		value = Unsafe.Add(ref arrayDataReference3, num2);
		return true;
	}

	public TValue[] ExtractValues() {
		var valuesArray = ValuesArray;
		ValuesArray = null;
		return valuesArray;
	}

	public delegate bool Predicate<in TArg>(TKey key, ref TValue item, TArg arg);

	/// <summary>
	/// Struct-dispatched filter predicate. Companion to the delegate-based
	/// <see cref="Filter{TArg}(Predicate{TArg}, TArg)"/> for zero-alloc, JIT-devirtualizable
	/// filtering — JIT specializes per closed generic so <see cref="Keep"/> inlines into
	/// the compact-and-rebuild loop. Used by inner-join resolvers to drop result-map
	/// entries where THIS resolver's slot is unset (chained-inner stale-slot pruning).
	/// </summary>
	internal interface IValueDictionaryFilter<TKey1, TValue1> {
		bool Keep(TKey1 key, ref TValue1 value);
	}

	/// <summary>
	/// Struct-dispatched filter overload — same compact-and-rebuild semantics as
	/// <see cref="Filter{TArg}(Predicate{TArg}, TArg)"/>, but the predicate is a
	/// <see langword="struct"/> implementing <see cref="IValueDictionaryFilter{TKey1, TValue1}"/>.
	/// </summary>
	internal void Filter<TFilter>(TFilter filter)
		where TFilter : struct, IValueDictionaryFilter<TKey, TValue> {
		var count = Count;
		var keysSpan = _keys.Span;
		var valuesSpan = _values.Span;
		ref var keysRef = ref MemoryMarshal.GetReference(keysSpan);
		ref var valuesRef = ref MemoryMarshal.GetReference(valuesSpan);

		var write = 0;
		for (var read = 0; read < count; read++) {
			if (!filter.Keep(Unsafe.Add(ref keysRef, read), ref Unsafe.Add(ref valuesRef, read))) continue;

			if (write != read) {
				Unsafe.Add(ref keysRef, write) = Unsafe.Add(ref keysRef, read);
				Unsafe.Add(ref valuesRef, write) = Unsafe.Add(ref valuesRef, read);
			}
			write++;
		}

		Count = write;

		var capacityMask = _capacityMask;
		Array.Fill(_metadata, -1, 0, capacityMask + 1);
		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(_metadata);

		for (var i = 0; i < write; i++) {
			var slot = GetHashCode(Unsafe.Add(ref keysRef, i)) & capacityMask;
			while (Unsafe.Add(ref metaRef, slot) >= 0)
				slot = (slot + 1) & capacityMask;
			Unsafe.Add(ref metaRef, slot) = i;
		}
	}

	public void Filter<TArg>(Predicate<TArg> predicate, TArg arg) {
		var count = Count;
		var keysSpan = _keys.Span;
		var valuesSpan = _values.Span;
		ref var keysRef = ref MemoryMarshal.GetReference(keysSpan);
		ref var valuesRef = ref MemoryMarshal.GetReference(valuesSpan);

		var write = 0;
		for (var read = 0; read < count; read++) {
			if (!predicate(Unsafe.Add(ref keysRef, read), ref Unsafe.Add(ref valuesRef, write), arg)) continue; // set.Contains(Unsafe.Add(ref keysRef, read))) continue;

			if (write != read) {
				Unsafe.Add(ref keysRef, write) = Unsafe.Add(ref keysRef, read);
				Unsafe.Add(ref valuesRef, write) = Unsafe.Add(ref valuesRef, read);
			}
			write++;
		}

		Count = write;

		// Rebuild metadata
		var capacityMask = _capacityMask;
		Array.Fill(_metadata, -1, 0, capacityMask + 1);
		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(_metadata);

		for (var i = 0; i < write; i++) {
			var slot = GetHashCode(Unsafe.Add(ref keysRef, i)) & capacityMask;
			while (Unsafe.Add(ref metaRef, slot) >= 0)
				slot = (slot + 1) & capacityMask;
			Unsafe.Add(ref metaRef, slot) = i;
		}
	}

	public void Intersect(HashSet<TKey> set) {
		var count = Count;
		var keysSpan = _keys.Span;
		var valuesSpan = _values.Span;
		ref var keysRef = ref MemoryMarshal.GetReference(keysSpan);
		ref var valuesRef = ref MemoryMarshal.GetReference(valuesSpan);

		var write = 0;
		for (var read = 0; read < count; read++) {
			if (!set.Contains(Unsafe.Add(ref keysRef, read))) continue;

			if (write != read) {
				Unsafe.Add(ref keysRef, write) = Unsafe.Add(ref keysRef, read);
				Unsafe.Add(ref valuesRef, write) = Unsafe.Add(ref valuesRef, read);
			}
			write++;
		}

		Count = write;

		// Rebuild metadata
		var capacityMask = _capacityMask;
		Array.Fill(_metadata, -1, 0, capacityMask + 1);
		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(_metadata);

		for (var i = 0; i < write; i++) {
			var slot = GetHashCode(Unsafe.Add(ref keysRef, i)) & capacityMask;
			while (Unsafe.Add(ref metaRef, slot) >= 0)
				slot = (slot + 1) & capacityMask;
			Unsafe.Add(ref metaRef, slot) = i;
		}
	}

	internal void TrimCount(int newCount) {
		if (newCount < Count) Count = newCount;
	}

	internal void Crop(int skip, int take) {
		var count = Count;
		if (count <= 0) return;

		if (skip >= count) {
			Count = 0;
			return;
		}

		take = Math.Min(take, count - skip);

		if (skip > 0) {
			Offset = skip;
			_keys = _keys.Slice(skip, take);
			_values = _values.Slice(skip, take);
		}

		Count = take;
	}

	internal void SortAndCrop<TComparer>(TComparer comparer, int skip, int take) where TComparer : IComparer<TValue> {
		var count = Count;
		if (count <= 0) return;

		var keysSpan = _keys.Span.Slice(0, count);
		var valuesSpan = _values.Span.Slice(0, count);
		valuesSpan.Sort(keysSpan, comparer);

		if (skip >= count) {
			Count = 0;
			Array.Fill(_metadata, -1, 0, _capacityMask + 1);
			return;
		}

		take = Math.Min(take, count - skip);

		if (skip > 0) {
			Offset = skip;
			_keys = _keys.Slice(skip, take);
			_values = _values.Slice(skip, take);
		}

		Count = take;

		// Rebuild metadata once
		var capacityMask = _capacityMask;
		Array.Fill(_metadata, -1, 0, capacityMask + 1);
		var keys = _keys.Span;

		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(_metadata);
		ref var keysRef = ref MemoryMarshal.GetReference(keys);

		for (var i = 0; i < take; i++) {
			var hashCode = GetHashCode(Unsafe.Add(ref keysRef, i));
			var slot = hashCode & capacityMask;

			while (Unsafe.Add(ref metaRef, slot) >= 0) {
				slot = (slot + 1) & capacityMask;
			}

			Unsafe.Add(ref metaRef, slot) = i;
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Dispose() {
		if (_metadata != null) {
			MetadataPool.Return(_metadata);
			KeysPool.Return(_keysArray, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
			_keys = null;
			_metadata = null;
		}

		ValuesArray = null;
		Count = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Dispose(bool withValues) {
		if (_metadata != null) {
			MetadataPool.Return(_metadata);
			KeysPool.Return(_keysArray, RuntimeHelpers.IsReferenceOrContainsReferences<TKey>());
			_keys = null;
			_metadata = null;
		}

		if (withValues)
			ValuesPool.Return(ValuesArray, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
		ValuesArray = null;
		Count = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private readonly int GetHashCode(TKey key) => _comparer.GetHashCode(key);

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private readonly bool KeyEquals(TKey a, TKey b) => _comparer.Equals(a, b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetPowerOf2Capacity(int expectedCount) {
		var val = (expectedCount * 4 + 2) / 3;
		val = Math.Max(val, 4);
		return (int)BitOperations.RoundUpToPowerOf2((uint)val);
	}
}

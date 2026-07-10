// CS0649: Inline array fields are intentionally never assigned directly
// CS8619: Unsafe ref operations on inline arrays have known nullability mismatches
#pragma warning disable CS0649, CS8619

namespace Prague.Core.Collections;

using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utils;

internal interface IInto<TFrom, TInto> {
	TInto Into(TFrom from);
	TFrom From(TInto into);
}

/// <summary>
///   Non-generic slot metadata - shared across all ValueSet&lt;T&gt; instances in a single pool
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlotMeta {
	internal int HashCode;
	internal int Next;
}

[DebuggerDisplay("Count = {Count}")]
[SkipLocalsInit]
internal struct ValueSet<T, TKeyComparer> : IDisposable
	where TKeyComparer : struct, IKeyComparer<T> {
	private const int StackSize = 47; // prime
	private const int Lower31BitMask = 0x7FFFFFFF;
	internal const int StackAllocThreshold = 100;

	// Precomputed multiplier for StackSize (47)
	private const ulong StackSizeFastModMultiplier = ulong.MaxValue / StackSize + 1;

	public readonly bool IsInitlized;

	// Inline storage for small sets
	private InlineBuckets _inlineBuckets;
	private InlineSlotMetas _inlineSlotMetas;
	private InlineValues _inlineValues;

	// ReSharper disable once StaticMemberInGenericType
	// Shared pool for SlotMeta (non-generic, truly shared!)
	private static readonly ArrayPool<SlotMeta> SSlotMetaPool = ArrayPool<SlotMeta>.Shared;

	// ReSharper disable once StaticMemberInGenericType
	private static readonly ArrayPool<int> SBucketPool = ArrayPool<int>.Shared;

	private readonly bool _clearOnFree;

	// Pooled arrays for larger sets
	private int[]? _bucketsArray;
	private SlotMeta[]? _slotMetasArray;
	private T[]? _valuesArray;

	private int _freeList;
	private int _lastIndex;
	private int _size;
	private ulong _fastModMultiplier;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ValueSet() : this(default(TKeyComparer)) {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ValueSet(TKeyComparer comparer) {
		IsInitlized = true;
		_comparer = comparer;
		_lastIndex = 0;
		Count = 0;
		_freeList = -1;
		_size = StackSize;
		_fastModMultiplier = StackSizeFastModMultiplier;
		_clearOnFree = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ValueSet(int capacity) : this(capacity, default(TKeyComparer)) {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ValueSet(int capacity, TKeyComparer comparer) : this(comparer) {
		if (capacity > StackSize)
			Initialize(capacity);
	}


	public int Count { get; private set; }

	private readonly TKeyComparer _comparer;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T GetSingle() {
		Debug.Assert(Count == 1);
		var valuesArr = _valuesArray;
		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var lastIndex = _lastIndex;
		for (var i = 0; i < lastIndex; i++) {
			if (Unsafe.Add(ref metaStart, i).HashCode >= 0)
				return ref Unsafe.Add(ref valueStart, i);
		}

		return ref Unsafe.Add(ref valueStart, 0); // unreachable
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose() {
		ReturnArrays();
		_size = 0;
		_lastIndex = 0;
		Count = 0;
		_freeList = -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool Add(T item)
		=> AddIfNotPresent(item);

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool Contains(T item) {
		var hashCode = InternalGetHashCode(item);
		var bucket = GetBucket(hashCode);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var i = Unsafe.Add(ref bucketStart, bucket) - 1;
		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), item))
				return true;
			i = meta.Next;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool Remove(T item) {
		var hashCode = InternalGetHashCode(item);
		var bucket = GetBucket(hashCode);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var last = -1;
		var i = Unsafe.Add(ref bucketStart, bucket) - 1;

		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), item)) {
				if (last < 0)
					Unsafe.Add(ref bucketStart, bucket) = meta.Next + 1;
				else
					Unsafe.Add(ref metaStart, last).Next = meta.Next;

				meta.HashCode = -1;
				if (_clearOnFree)
					Unsafe.Add(ref valueStart, i) = default!;
				meta.Next = _freeList;

				Count--;
				if (Count == 0) {
					_lastIndex = 0;
					_freeList = -1;
				}
				else {
					_freeList = i;
				}

				return true;
			}

			last = i;
			i = meta.Next;
		}

		return false;
	}

	public void Clear() {
		var lastIndex = _lastIndex;
		if (lastIndex > 0) {
			var size = _size;
			var metasArr = _slotMetasArray;
			var valuesArr = _valuesArray;
			var bucketsArr = _bucketsArray;

			if (metasArr is null)
				MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in _inlineSlotMetas.Value), lastIndex).Clear();
			else
				metasArr.AsSpan(0, lastIndex).Clear();

			if (_clearOnFree) {
				if (valuesArr is null)
					MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in _inlineValues.Value), lastIndex).Clear();
				else
					valuesArr.AsSpan(0, lastIndex).Clear();
			}

			if (bucketsArr is null)
				MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in _inlineBuckets.Value), size).Clear();
			else
				bucketsArr.AsSpan(0, size).Clear();

			_lastIndex = 0;
			Count = 0;
			_freeList = -1;
		}
	}

	public void UnionWith<TKey, TOther, TInto>(TKey key, TInto into, IEnumerable<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		ArgumentNullException.ThrowIfNull(other);
		foreach (var item in other) AddIfNotPresent(into.Into(item));
	}

	public void UnionWith<TOther, TInto>(TInto into, IEnumerable<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		ArgumentNullException.ThrowIfNull(other);
		foreach (var item in other) AddIfNotPresent(into.Into(item));
	}

	public void UnionWith<TOther, TInto>(TInto into, ImmutableHashSet<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		ArgumentNullException.ThrowIfNull(other);
		foreach (var item in other) AddIfNotPresent(into.Into(item));
	}

	public void UnionWith(ImmutableHashSet<T> other) {
		foreach (var item in other) AddIfNotPresent(item);
	}

	public void UnionWith<TOther, TInto>(TInto into, PooledSet<TOther, DefaultKeyComparer<TOther>> other)
		where TOther : notnull
		where TInto : struct, IInto<TOther, T> {
		foreach (var item in other)
			AddIfNotPresent(into.Into(item));
	}

	public void UnionWith(PooledSet<T, DefaultKeyComparer<T>> other) {
		using var enumerator = other.GetEnumerator();
		while (enumerator.MoveNext())
			AddIfNotPresent(enumerator.Current, enumerator.CurrentHashCode);
	}

	public void UnionWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);
		foreach (var item in other) AddIfNotPresent(item);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UnionWith(T[] other)
		=> UnionWith((ReadOnlySpan<T>)other);

	public void UnionWith(ref ValueSet<T, TKeyComparer> other) {
		foreach (var val in other)
			AddIfNotPresent(val);
	}

	public void UnionWith(ref ValueSet<T, TKeyComparer> v1, ref ValueSet<T, TKeyComparer> v2) {
		if (v1.IsInitlized) UnionWith(ref v1);
		if (v2.IsInitlized) UnionWith(ref v2);
	}

	public void UnionWith(ReadOnlySpan<T> other) {
		ref var otherRef = ref MemoryMarshal.GetReference(other);
		var len = other.Length;
		for (nint i = 0; i < len; i++)
			AddIfNotPresent(Unsafe.Add(ref otherRef, i));
	}

	public void IntersectWith(T prop) {
		if (Count == 0)
			return;

		var contains = ContainsInternal(prop, out var bucket, out var hashCode, out var foundValue);

		if (Count == 1 && contains)
			return;

		Clear();

		if (contains)
			AddAgain(bucket, hashCode, foundValue);
	}

	public void IntersectWith<TOther, TInto>(TInto convertor, ref ValueSet<TOther, DefaultKeyComparer<TOther>> other)
		where TInto : struct, IInto<TOther, T> {
		if (Count == 0)
			return;
		IntersectWithHashSetWithSameEc(convertor, ref other);
	}

	public void IntersectWith<TOther, TInto>(TInto convertor, PooledSet<TOther, DefaultKeyComparer<TOther>> other)
		where TOther : notnull
		where TInto : struct, IInto<TOther, T> {
		if (Count == 0)
			return;
		if (other.Count == 0) {
			Clear();
			return;
		}
		IntersectWithPooledSet(convertor, other);
	}

	public void IntersectWith(PooledSet<T, DefaultKeyComparer<T>> other) {
		if (Count == 0)
			return;
		if (other.Count == 0) {
			Clear();
			return;
		}
		IntersectWithPooledSet(other);
	}

	public void IntersectWith<TOther, TInto>(TInto convertor, IEnumerable<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		ArgumentNullException.ThrowIfNull(other);

		if (Count == 0)
			return;

		if (other is ICollection<T> otherAsCollection) {
			if (otherAsCollection.Count == 0) {
				Clear();
				return;
			}

			if (other is ImmutableHashSet<TOther> otherImHsSet) {
				IntersectWithHashSetWithSameEc(convertor, otherImHsSet);
				return;
			}

			if (other is HashSet<TOther> otherAsHs) {
				IntersectWithHashSetWithSameEc(convertor, otherAsHs);
				return;
			}
		}

		IntersectWithEnumerable(convertor, other);
	}

	public void IntersectWith(ref ValueSet<T, TKeyComparer> other) {
		if (Count == 0)
			return;
		IntersectWithHashSetWithSameEc(ref other);
	}

	/// <summary>
	///   Intersects the receiver against the union of <paramref name="v1" /> and <paramref name="v2" />.
	///   Keeps only elements present in at least one of the two sets.
	///   If both are uninitialized the receiver is left unchanged (treated as no-op).
	/// </summary>
	public void IntersectWith(ref ValueSet<T, TKeyComparer> v1, ref ValueSet<T, TKeyComparer> v2) {
		if (!v1.IsInitlized && !v2.IsInitlized)
			return;
		if (Count == 0)
			return;
		IntersectWithUnion(ref v1, ref v2);
	}

	public void IntersectWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		if (Count == 0)
			return;

		if (other is ICollection<T> otherAsCollection) {
			if (otherAsCollection.Count == 0) {
				Clear();
				return;
			}

			if (other is ImmutableHashSet<T> otherImHsSet) {
				IntersectWithHashSetWithSameEc(otherImHsSet);
				return;
			}

			if (other is HashSet<T> otherAsHs) {
				IntersectWithHashSetWithSameEc(otherAsHs);
				return;
			}
		}

		IntersectWithEnumerable(other);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void IntersectWith(T[] other)
		=> IntersectWith((ReadOnlySpan<T>)other);

	public void IntersectWith(ReadOnlySpan<T> other) {
		if (Count == 0)
			return;

		if (other.Length == 0) {
			Clear();
			return;
		}

		IntersectWithSpan(other);
	}

	public int EnsureCapacity(int capacity) {
		if (capacity < 0)
			ThrowHelper.ThrowArgumentOutOfRangeException();

		if (_size >= capacity)
			return _size;

		var newSize = HashHelpers.GetPrime(capacity);
		SetCapacity(newSize);
		return newSize;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Enumerator GetEnumerator()
		=> new(ref Unsafe.AsRef(ref this));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucket(int hashCode)
		=> (int)HashHelpers.FastMod((uint)hashCode, (uint)_size, _fastModMultiplier);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int Initialize(int capacity) {
		_size = HashHelpers.GetPrime(capacity);
		_fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)_size);
		_bucketsArray = SBucketPool.Rent(_size);
		Array.Clear(_bucketsArray, 0, _bucketsArray.Length);
		_slotMetasArray = SSlotMetaPool.Rent(_size);
		_valuesArray = ArrayPool<T>.Shared.Rent(_size);
		return _size;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void IncreaseCapacity() {
		var newSize = HashHelpers.ExpandPrime(Count);
		SetCapacity(newSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SetCapacity(int newSize) {
		Debug.Assert(HashHelpers.IsPrime(newSize), "New size is not prime!");

		int[] newBuckets;
		SlotMeta[] newSlotMetas;
		T[] newValues;
		bool replaceArrays;

		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;

		if (_bucketsArray?.Length >= newSize && metasArr?.Length >= newSize && valuesArr?.Length >= newSize) {
			Array.Clear(_bucketsArray, 0, _bucketsArray.Length);
			Array.Clear(metasArr, _size, newSize - _size);
			if (_clearOnFree)
				Array.Clear(valuesArr, _size, newSize - _size);
			newBuckets = _bucketsArray;
			newSlotMetas = metasArr;
			newValues = valuesArr;
			replaceArrays = false;
		}
		else {
			newSlotMetas = SSlotMetaPool.Rent(newSize);
			newBuckets = SBucketPool.Rent(newSize);
			newValues = ArrayPool<T>.Shared.Rent(newSize);

			Array.Clear(newBuckets, 0, newBuckets.Length);
			var lastIndex = _lastIndex;
			if (lastIndex > 0) {
				Span<SlotMeta> srcMetaSpan = metasArr is null ? _inlineSlotMetas : metasArr;
				Span<T> srcValueSpan = valuesArr is null ? _inlineValues : valuesArr;

				srcMetaSpan[..lastIndex].CopyTo(newSlotMetas);
				srcValueSpan[..lastIndex].CopyTo(newValues);
			}

			replaceArrays = true;
		}

		// Compute new multiplier for FastMod
		var newFastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);

		ref var metaRef = ref MemoryMarshal.GetArrayDataReference(newSlotMetas);
		ref var bucketRef = ref MemoryMarshal.GetArrayDataReference(newBuckets);

		for (nint i = 0; i < _lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaRef, i);
			if (meta.HashCode >= 0) {
				var bucket = (int)HashHelpers.FastMod((uint)meta.HashCode, (uint)newSize, newFastModMultiplier);
				meta.Next = Unsafe.Add(ref bucketRef, bucket) - 1;
				Unsafe.Add(ref bucketRef, bucket) = (int)i + 1;
			}
		}

		if (replaceArrays) {
			ReturnArrays();
			_slotMetasArray = newSlotMetas;
			_bucketsArray = newBuckets;
			_valuesArray = newValues;
		}

		_size = newSize;
		_fastModMultiplier = newFastModMultiplier;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ReturnArrays() {
		var slots = _slotMetasArray;
		var buckets = _bucketsArray;
		var values = _valuesArray;

		_slotMetasArray = null;
		_bucketsArray = null;
		_valuesArray = null;

		if (slots?.Length > 0)
			try {
				SSlotMetaPool.Return(slots);
			}
			catch (ArgumentException) {
			}

		if (buckets?.Length > 0)
			try {
				SBucketPool.Return(buckets);
			}
			catch (ArgumentException) {
			}

		if (values?.Length > 0)
			try {
				ArrayPool<T>.Shared.Return(values, _clearOnFree);
			}
			catch (ArgumentException) {
			}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AddAgain(int bucket, int hashCode, T value) {
		var index = _lastIndex++;

		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		var bucketsArr = _bucketsArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);

		ref var meta = ref Unsafe.Add(ref metaStart, index);
		ref var bucketRef = ref Unsafe.Add(ref bucketStart, bucket);

		meta.HashCode = hashCode;
		meta.Next = bucketRef - 1;
		Unsafe.Add(ref valueStart, index) = value;
		bucketRef = index + 1;
		Count++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private bool AddIfNotPresent(T value, int hashCode) {
		var size = _size;
		var fastModMultiplier = _fastModMultiplier;
		var bucket = (int)HashHelpers.FastMod((uint)hashCode, (uint)size, fastModMultiplier);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var i = Unsafe.Add(ref bucketStart, bucket) - 1;
		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), value))
				return false;
			i = meta.Next;
		}

		int index;
		if (_freeList >= 0) {
			index = _freeList;
			_freeList = Unsafe.Add(ref metaStart, index).Next;
		}
		else {
			if (_lastIndex == size) {
				IncreaseCapacity();
				bucket = GetBucket(hashCode);
				bucketsArr = _bucketsArray;
				metasArr = _slotMetasArray;
				valuesArr = _valuesArray;
				bucketStart = ref bucketsArr is null
					? ref Unsafe.AsRef(in _inlineBuckets.Value)
					: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
				metaStart = ref metasArr is null
					? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
					: ref MemoryMarshal.GetArrayDataReference(metasArr);
				valueStart = ref valuesArr is null
					? ref Unsafe.AsRef(in _inlineValues.Value)!
					: ref MemoryMarshal.GetArrayDataReference(valuesArr);
			}

			index = _lastIndex++;
		}

		ref var slotMeta = ref Unsafe.Add(ref metaStart, index);
		slotMeta.HashCode = hashCode;
		slotMeta.Next = Unsafe.Add(ref bucketStart, bucket) - 1;
		Unsafe.Add(ref valueStart, index) = value;
		Unsafe.Add(ref bucketStart, bucket) = index + 1;
		Count++;

		return true;
	}

	private bool AddIfNotPresent(T value) {
		var hashCode = InternalGetHashCode(value);
		var size = _size;
		var fastModMultiplier = _fastModMultiplier;
		var bucket = (int)HashHelpers.FastMod((uint)hashCode, (uint)size, fastModMultiplier);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var i = Unsafe.Add(ref bucketStart, bucket) - 1;
		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), value))
				return false;
			i = meta.Next;
		}

		int index;
		if (_freeList >= 0) {
			index = _freeList;
			_freeList = Unsafe.Add(ref metaStart, index).Next;
		}
		else {
			if (_lastIndex == size) {
				IncreaseCapacity();
				bucket = GetBucket(hashCode);
				// Re-fetch after resize
				bucketsArr = _bucketsArray;
				metasArr = _slotMetasArray;
				valuesArr = _valuesArray;
				bucketStart = ref bucketsArr is null
					? ref Unsafe.AsRef(in _inlineBuckets.Value)
					: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
				metaStart = ref metasArr is null
					? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
					: ref MemoryMarshal.GetArrayDataReference(metasArr);
				valueStart = ref valuesArr is null
					? ref Unsafe.AsRef(in _inlineValues.Value)!
					: ref MemoryMarshal.GetArrayDataReference(valuesArr);
			}

			index = _lastIndex++;
		}

		ref var slotMeta = ref Unsafe.Add(ref metaStart, index);
		slotMeta.HashCode = hashCode;
		slotMeta.Next = Unsafe.Add(ref bucketStart, bucket) - 1;
		Unsafe.Add(ref valueStart, index) = value;
		Unsafe.Add(ref bucketStart, bucket) = index + 1;
		Count++;

		return true;
	}

	/// <summary>
	///   Removes item at known slot index with known hashCode.
	///   Skips hash computation and equality comparison.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void RemoveAt(int index, int hashCode) {
		var bucket = GetBucket(hashCode);

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var last = -1;
		var i = Unsafe.Add(ref bucketStart, bucket) - 1;

		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (i == index) {
				if (last < 0)
					Unsafe.Add(ref bucketStart, bucket) = meta.Next + 1;
				else
					Unsafe.Add(ref metaStart, last).Next = meta.Next;

				meta.HashCode = -1;
				if (_clearOnFree)
					Unsafe.Add(ref valueStart, index) = default!;
				meta.Next = _freeList;

				Count--;
				if (Count == 0) {
					_lastIndex = 0;
					_freeList = -1;
				}
				else {
					_freeList = i;
				}

				return;
			}

			last = i;
			i = meta.Next;
		}
	}

	// Returns the value stored at slot `slot`. Caller must guarantee that the slot is live
	// (meta.HashCode >= 0). Used by IncrementalIntersecter.RetainOnly during prune walk —
	// the bitmap can only have a bit set for a slot that was live at mark time, so reading
	// back the value at that slot is safe as long as no deletions happened in between.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ref T GetValueAtSlotUnsafe(int slot) {
		var valuesArr = _valuesArray;
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);
		return ref Unsafe.Add(ref valueStart, slot);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int InternalIndexOf(T item) {
		var hashCode = InternalGetHashCode(item);
		return InternalIndexOf(item, hashCode);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private int InternalIndexOf(T item, int hashCode) {
		var bucket = GetBucket(hashCode);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var i = Unsafe.Add(ref bucketStart, bucket) - 1;
		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), item))
				return i;
			i = meta.Next;
		}

		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private int InternalGetHashCode(T item)
		=> item is null ? 0 : _comparer.GetHashCode(item) & Lower31BitMask;

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private bool ContainsInternal(T item, out int bucketNumber, out int hashCode, out T foundValue) {
		hashCode = InternalGetHashCode(item);
		bucketNumber = GetBucket(hashCode);
		var comparer = _comparer;

		var bucketsArr = _bucketsArray;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var bucketStart = ref bucketsArr is null
			? ref Unsafe.AsRef(in _inlineBuckets.Value)
			: ref MemoryMarshal.GetArrayDataReference(bucketsArr);
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		var i = Unsafe.Add(ref bucketStart, bucketNumber) - 1;
		while (i >= 0) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode == hashCode && comparer.Equals(Unsafe.Add(ref valueStart, i), item)) {
				foundValue = Unsafe.Add(ref valueStart, i);
				return true;
			}

			i = meta.Next;
		}

		bucketNumber = 0;
		hashCode = 0;
		foundValue = default!;
		return false;
	}

	private void IntersectWithHashSetWithSameEc<TOther, TInto>(TInto into, ImmutableHashSet<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(into.From(item)))
					Remove(item);
			}
		}
	}

	private void IntersectWithHashSetWithSameEc<TOther, TInto>(TInto into, ref ValueSet<TOther, DefaultKeyComparer<TOther>> other)
		where TInto : struct, IInto<TOther, T> {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(into.From(item)))
					Remove(item);
			}
		}
	}

	private void IntersectWithHashSetWithSameEc<TOther, TInto>(TInto into, HashSet<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(into.From(item)))
					Remove(item);
			}
		}
	}

	private void IntersectWithHashSetWithSameEc(ImmutableHashSet<T> other) {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(item))
					Remove(item);
			}
		}
	}

	private void IntersectWithHashSetWithSameEc(ref ValueSet<T, TKeyComparer> other) {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(item))
					Remove(item);
			}
		}
	}

	private void IntersectWithUnion(ref ValueSet<T, TKeyComparer> v1, ref ValueSet<T, TKeyComparer> v2) {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);
		var v1Init = v1.IsInitlized;
		var v2Init = v2.IsInitlized;

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				var inV1 = v1Init && v1.Contains(item);
				var inV2 = !inV1 && v2Init && v2.Contains(item);
				if (!inV1 && !inV2)
					Remove(item);
			}
		}
	}

	private void IntersectWithPooledSet<TOther, TInto>(TInto into, PooledSet<TOther, DefaultKeyComparer<TOther>> other)
		where TOther : notnull
		where TInto : struct, IInto<TOther, T> {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(into.From(item)))
					Remove(item);
			}
		}
	}

	private void IntersectWithPooledSet(PooledSet<T, DefaultKeyComparer<T>> other) {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.ContainsWithHashCode(item, meta.HashCode))
					Remove(item);
			}
		}
	}

	private void IntersectWithHashSetWithSameEc(HashSet<T> other) {
		var lastIndex = _lastIndex;
		var metasArr = _slotMetasArray;
		var valuesArr = _valuesArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);
		ref var valueStart = ref valuesArr is null
			? ref Unsafe.AsRef(in _inlineValues.Value)
			: ref MemoryMarshal.GetArrayDataReference(valuesArr);

		for (var i = 0; i < lastIndex; i++) {
			ref var meta = ref Unsafe.Add(ref metaStart, i);
			if (meta.HashCode >= 0) {
				var item = Unsafe.Add(ref valueStart, i);
				if (!other.Contains(item))
					Remove(item);
			}
		}
	}

	internal void IntersectWithValueSet<TOther, TInto>(TInto into, ref ValueSet<TOther, DefaultKeyComparer<TOther>> other)
		where TInto : struct, IInto<TOther, T> {
		var originalLastIndex = _lastIndex;
		var intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

		Span<int> span = stackalloc int[StackAllocThreshold];
		int[]? rentedArray = null;
		var bitHelper = intArrayLength <= StackAllocThreshold
			? new BitHelper(span.Slice(0, intArrayLength), true)
			: new BitHelper((rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength)).AsSpan(0, intArrayLength), true);

		foreach (var item in other) {
			var index = InternalIndexOf(into.Into(item));
			if (index >= 0) bitHelper.MarkBit(index);
		}

		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);

		for (var i = bitHelper.FindFirstUnmarked();
		     (uint)i < (uint)originalLastIndex;
		     i = bitHelper.FindFirstUnmarked(i + 1)) {
			var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
			if (hashCode >= 0)
				RemoveAt(i, hashCode);
		}

		if (rentedArray is not null)
			ArrayPool<int>.Shared.Return(rentedArray);
	}

	internal void IntersectWithSpan<TOther, TInto>(TInto into, ReadOnlySpan<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		var originalLastIndex = _lastIndex;
		var intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

		Span<int> span = stackalloc int[StackAllocThreshold];
		int[]? rentedArray = null;
		var bitHelper = intArrayLength <= StackAllocThreshold
			? new BitHelper(span.Slice(0, intArrayLength), true)
			: new BitHelper((rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength)).AsSpan(0, intArrayLength), true);

		foreach (var item in other) {
			var index = InternalIndexOf(into.Into(item));
			if (index >= 0) bitHelper.MarkBit(index);
		}

		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);

		for (var i = bitHelper.FindFirstUnmarked();
		     (uint)i < (uint)originalLastIndex;
		     i = bitHelper.FindFirstUnmarked(i + 1)) {
			var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
			if (hashCode >= 0)
				RemoveAt(i, hashCode);
		}

		if (rentedArray is not null)
			ArrayPool<int>.Shared.Return(rentedArray);
	}


	private void IntersectWithEnumerable<TOther, TInto>(TInto into, IEnumerable<TOther> other)
		where TInto : struct, IInto<TOther, T> {
		var originalLastIndex = _lastIndex;
		var intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

		Span<int> span = stackalloc int[StackAllocThreshold];
		int[]? rentedArray = null;
		var bitHelper = intArrayLength <= StackAllocThreshold
			? new BitHelper(span.Slice(0, intArrayLength), true)
			: new BitHelper((rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength)).AsSpan(0, intArrayLength), true);

		foreach (var item in other) {
			var index = InternalIndexOf(into.Into(item));
			if (index >= 0) bitHelper.MarkBit(index);
		}

		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);

		for (var i = bitHelper.FindFirstUnmarked();
		     (uint)i < (uint)originalLastIndex;
		     i = bitHelper.FindFirstUnmarked(i + 1)) {
			var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
			if (hashCode >= 0)
				RemoveAt(i, hashCode);
		}

		if (rentedArray is not null)
			ArrayPool<int>.Shared.Return(rentedArray);
	}

	private void IntersectWithEnumerable(IEnumerable<T> other) {
		var originalLastIndex = _lastIndex;
		var intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

		Span<int> span = stackalloc int[StackAllocThreshold];
		int[]? rentedArray = null;
		var bitHelper = intArrayLength <= StackAllocThreshold
			? new BitHelper(span.Slice(0, intArrayLength), true)
			: new BitHelper((rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength)).AsSpan(0, intArrayLength), true);

		foreach (var item in other) {
			var index = InternalIndexOf(item);
			if (index >= 0) bitHelper.MarkBit(index);
		}

		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);

		for (var i = bitHelper.FindFirstUnmarked();
		     (uint)i < (uint)originalLastIndex;
		     i = bitHelper.FindFirstUnmarked(i + 1)) {
			var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
			if (hashCode >= 0)
				RemoveAt(i, hashCode);
		}

		if (rentedArray is not null)
			ArrayPool<int>.Shared.Return(rentedArray);
	}

	private void IntersectWithSpan(ReadOnlySpan<T> other) {
		var originalLastIndex = _lastIndex;
		var intArrayLength = BitHelper.ToIntArrayLength(originalLastIndex);

		Span<int> span = stackalloc int[StackAllocThreshold];
		int[]? rentedArray = null;
		var bitHelper = intArrayLength <= StackAllocThreshold
			? new BitHelper(span.Slice(0, intArrayLength), true)
			: new BitHelper((rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength)).AsSpan(0, intArrayLength), true);

		foreach (var item in other) {
			var index = InternalIndexOf(item);
			if (index >= 0) bitHelper.MarkBit(index);
		}

		var metasArr = _slotMetasArray;
		ref var metaStart = ref metasArr is null
			? ref Unsafe.AsRef(in _inlineSlotMetas.Value)
			: ref MemoryMarshal.GetArrayDataReference(metasArr);

		for (var i = bitHelper.FindFirstUnmarked();
		     (uint)i < (uint)originalLastIndex;
		     i = bitHelper.FindFirstUnmarked(i + 1)) {
			var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
			if (hashCode >= 0)
				RemoveAt(i, hashCode);
		}

		if (rentedArray is not null)
			ArrayPool<int>.Shared.Return(rentedArray);
	}

	internal ref struct IncrementalIntersecter<TFrom, TInto> where TInto : struct, IInto<TFrom, T> {
		public bool IsCleared = false;

		private readonly int _originalLastIndex;
		private readonly int[]? _rentedArray;
		private ref ValueSet<T, TKeyComparer> _self;
		private TInto _into;
		private BitHelper _bitHelper;

		[UnscopedRef]
		public ReadOnlySpan<int> Bits => _bitHelper.Bits;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public IncrementalIntersecter(TInto into, ref ValueSet<T, TKeyComparer> self, Span<int> helperBuffer) {
			_into = into;
			_self = ref self;
			_originalLastIndex = self._lastIndex;
			var intArrayLength = BitHelper.ToIntArrayLength(_originalLastIndex);

			if (intArrayLength <= helperBuffer.Length) {
				_bitHelper = new BitHelper(helperBuffer[..intArrayLength], false);
				_rentedArray = null;
			} else {
				_rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength);
				_bitHelper = new BitHelper(_rentedArray.AsSpan()[..intArrayLength], true);
			}
		}

		[UnscopedRef]
		public IncrementalIntersecter<TFrom, TInto> CreateChild(Span<int> helperBuffer)
			=> new IncrementalIntersecter<TFrom, TInto>(_into, ref _self, helperBuffer);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void IntersectWith(TFrom prop) {
			if (IsCleared) return;
			var index = _self.InternalIndexOf(_into.Into(prop));
			if (index >= 0)
				_bitHelper.MarkBit(index);
		}

		public void IntersectWith(PooledSet<TFrom, DefaultKeyComparer<TFrom>> other) {
			if (IsCleared) return;
			var gate = ReaderGate.Enter();
			try {
				IntersectWithCore(other);
			}
			finally {
				ReaderGate.Exit(gate);
			}
		}

		// NoInlining: keeps the scan loop out of the gated wrapper's EH region, which
		// would otherwise pessimize the whole method's codegen.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void IntersectWithCore(PooledSet<TFrom, DefaultKeyComparer<TFrom>> other) {
			// Single consistent snapshot — reading through separate properties could
			// straddle a concurrent Grow and go out of bounds. The caller's gate pin
			// keeps the arrays out of the pool for the whole scan.
			other.GetSnapshot(out var slots, out var versions, out var lastIndex);
			ref var start = ref MemoryMarshal.GetArrayDataReference(slots);
			for (var i = 0; i < lastIndex; i++) {
				ref var slot = ref Unsafe.Add(ref start, i);
				if (versions == null) {
					// Atomically-copyable T: stale-or-new, never torn.
					var hashCode = Volatile.Read(ref slot.HashCode);
					if (hashCode < 0) continue;
					var index = _self.InternalIndexOf(_into.Into(slot.Value));
					if (index >= 0)
						_bitHelper.MarkBit(index);
				}
				else {
					// Version-guarded copy-out (multi-word T): rejects torn copies
					// even under remove + re-add with an equal hash (ABA).
					var version = Volatile.Read(ref versions[i]);
					var hashCode = Volatile.Read(ref slot.HashCode);
					if (hashCode < 0) continue;
					var value = slot.Value;
					if (Volatile.Read(ref versions[i]) != version) continue;
					var index = _self.InternalIndexOf(_into.Into(value));
					if (index >= 0)
						_bitHelper.MarkBit(index);
				}
			}
		}

		// Prune phase for chained UseIndex within an Or branch.
		// Walks currently-marked slots; for each, extracts the TFrom from the slot value via _into.From;
		// if not in `keepIf`, clears the bit.
		public void RetainOnly(PooledSet<TFrom, DefaultKeyComparer<TFrom>> keepIf) {
			if (IsCleared) return;
			if (keepIf.Count == 0) {
				Clear();
				return;
			}

			var lastIndex = _originalLastIndex;
			var anySurvived = false;
			var slot = _bitHelper.FindFirstMarked(0);
			while (slot >= 0 && slot < lastIndex) {
				ref var slotValue = ref _self.GetValueAtSlotUnsafe(slot);
				if (keepIf.Contains(_into.From(slotValue)))
					anySurvived = true;
				else
					_bitHelper.UnmarkBit(slot);
				slot = _bitHelper.FindFirstMarked(slot + 1);
			}

			if (!anySurvived)
				IsCleared = true;
		}

		// 1:1 specialization: keep only the slot whose value maps via Into to `keepIfEqual`.
		public void RetainOnly(TFrom keepIfEqual) {
			if (IsCleared) return;
			var slot = _self.InternalIndexOf(_into.Into(keepIfEqual));
			if (slot < 0 || !_bitHelper.IsMarked(slot)) {
				Clear();
				return;
			}
			_bitHelper.Clear();
			_bitHelper.MarkBit(slot);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Union(scoped ref IncrementalIntersecter<TFrom, TInto> other) {
			if (other.IsCleared) return;
			_bitHelper.Union(ref other._bitHelper);
			IsCleared = false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Union(scoped ReadOnlySpan<int> otherBits) {
			_bitHelper.Union(otherBits);
			IsCleared = false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Intersect(scoped ref IncrementalIntersecter<TFrom, TInto> other) {
			if (other.IsCleared) {
				Clear();
				return;
			}
			_bitHelper.Intersect(ref other._bitHelper);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Intersect(scoped ReadOnlySpan<int> otherBits)
			=> _bitHelper.Intersect(otherBits);

		public void Clear() {
			_bitHelper.Clear();
			IsCleared = true;
		}

		public void Dispose() => Dispose(true);

		public void Dispose(bool flush) {
			if (flush) Flush();
			if (_rentedArray is not null)
				ArrayPool<int>.Shared.Return(_rentedArray);
		}

		private void Flush() {
			var metasArr = _self._slotMetasArray;
			ref var metaStart = ref metasArr is null
				? ref Unsafe.AsRef(in _self._inlineSlotMetas.Value)
				: ref MemoryMarshal.GetArrayDataReference(metasArr);
			var originalLastIndex = _originalLastIndex;

			for (var i = _bitHelper.FindFirstUnmarked();
			     (uint)i < (uint)originalLastIndex;
			     i = _bitHelper.FindFirstUnmarked(i + 1)) {
				var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
				if (hashCode >= 0)
					_self.RemoveAt(i, hashCode);
			}
		}
	}

	internal ref struct IncrementalIntersecter {
		public bool IsCleared = false;

		private ref ValueSet<T, TKeyComparer> _self;
		private readonly int _originalLastIndex;
		private readonly int[]? _rentedArray;
		private BitHelper _bitHelper;

		[UnscopedRef]
		public ReadOnlySpan<int> Bits => _bitHelper.Bits;   // expose the bitmap

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public IncrementalIntersecter(ref ValueSet<T, TKeyComparer> self, Span<int> helperBuffer) {
			_self = ref self;
			_originalLastIndex = self._lastIndex;
			var intArrayLength = BitHelper.ToIntArrayLength(_originalLastIndex);

			if (intArrayLength <= helperBuffer.Length) {
				_bitHelper = new BitHelper(helperBuffer[..intArrayLength], false);
				_rentedArray = null;
			} else {
				_rentedArray = ArrayPool<int>.Shared.Rent(intArrayLength);
				_bitHelper = new BitHelper(_rentedArray.AsSpan()[..intArrayLength], true);
			}
		}

		[UnscopedRef]
		public ValueSet<T, TKeyComparer>.IncrementalIntersecter CreateChild(Span<int> helperBuffer)
			=> new ValueSet<T, TKeyComparer>.IncrementalIntersecter(ref _self, helperBuffer);


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void IntersectWith(T prop) {
			if (IsCleared) return;
			var index = _self.InternalIndexOf(prop);
			if (index >= 0)
				_bitHelper.MarkBit(index);
		}

		public void IntersectWith(PooledSet<T, DefaultKeyComparer<T>> other) {
			if (IsCleared) return;
			var gate = ReaderGate.Enter();
			try {
				IntersectWithCore(other);
			}
			finally {
				ReaderGate.Exit(gate);
			}
		}

		// NoInlining: keeps the scan loop out of the gated wrapper's EH region, which
		// would otherwise pessimize the whole method's codegen.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void IntersectWithCore(PooledSet<T, DefaultKeyComparer<T>> other) {
			// Single consistent snapshot — see the TFrom overload for the rationale.
			other.GetSnapshot(out var slots, out var versions, out var lastIndex);
			ref var start = ref MemoryMarshal.GetArrayDataReference(slots);
			for (var i = 0; i < lastIndex; i++) {
				ref var slot = ref Unsafe.Add(ref start, i);
				if (versions == null) {
					var hashCode = Volatile.Read(ref slot.HashCode);
					if (hashCode < 0) continue;
					var index = _self.InternalIndexOf(slot.Value, hashCode);
					if (index >= 0)
						_bitHelper.MarkBit(index);
				}
				else {
					var version = Volatile.Read(ref versions[i]);
					var hashCode = Volatile.Read(ref slot.HashCode);
					if (hashCode < 0) continue;
					var value = slot.Value;
					if (Volatile.Read(ref versions[i]) != version) continue;
					var index = _self.InternalIndexOf(value, hashCode);
					if (index >= 0)
						_bitHelper.MarkBit(index);
				}
			}
		}

		// Prune phase for chained UseIndex within an Or branch.
		// Walks currently-marked slots; for each, looks up the slot's value in _self;
		// if not in `keepIf`, clears the bit. Result: bitmap ← bitmap ∩ {slots whose value ∈ keepIf}.
		public void RetainOnly(PooledSet<T, DefaultKeyComparer<T>> keepIf) {
			if (IsCleared) return;
			if (keepIf.Count == 0) {
				Clear();
				return;
			}

			var lastIndex = _originalLastIndex;
			var anySurvived = false;
			var slot = _bitHelper.FindFirstMarked(0);
			while (slot >= 0 && slot < lastIndex) {
				ref var slotValue = ref _self.GetValueAtSlotUnsafe(slot);
				if (keepIf.Contains(slotValue))
					anySurvived = true;
				else
					_bitHelper.UnmarkBit(slot);
				slot = _bitHelper.FindFirstMarked(slot + 1);
			}

			if (!anySurvived)
				IsCleared = true;
		}

		// Specialization for 1:1 KeyValueIndex result: keep only the bit at the slot
		// for `keepIfEqual` (if it was marked). Single hash lookup + bitmap reset+set.
		public void RetainOnly(T keepIfEqual) {
			if (IsCleared) return;
			var slot = _self.InternalIndexOf(keepIfEqual);
			if (slot < 0 || !_bitHelper.IsMarked(slot)) {
				Clear();
				return;
			}
			_bitHelper.Clear();
			_bitHelper.MarkBit(slot);
			// IsCleared stays false — exactly one bit is set.
		}

		public void Clear() {
			_bitHelper.Clear();
			IsCleared = true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Intersect(scoped ref ValueSet<T, TKeyComparer>.IncrementalIntersecter other) {
			// AND `other` into self. If `other` is fully cleared, the AND result is zero —
			// shortcut to Clear() so subsequent IntersectWith calls early-exit.
			if (other.IsCleared) {
				Clear();
				return;
			}
			_bitHelper.Intersect(ref other._bitHelper);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Intersect(scoped ReadOnlySpan<int> otherBits) {
			_bitHelper.Intersect(otherBits);
			// Cannot detect all-zero without scanning; caller may explicitly set IsCleared if known.
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Union(scoped ref ValueSet<T, TKeyComparer>.IncrementalIntersecter other) {
			if (other.IsCleared) return;
			_bitHelper.Union(ref other._bitHelper);
			IsCleared = false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Union(scoped ReadOnlySpan<int> other) {
			_bitHelper.Union(other);
			IsCleared = false;
		}

		public static void Union(scoped ref IncrementalIntersecter target, scoped ref IncrementalIntersecter source) {
			if (source.IsCleared) return;
			target._bitHelper.Union(ref source._bitHelper);
			target.IsCleared = false;
		}

		public void Dispose()
			=> Dispose(true);

		public void Dispose(bool flush) {
			if (flush) Flush();
			if (_rentedArray is not null)
				ArrayPool<int>.Shared.Return(_rentedArray);
		}

		private void Flush() {
			var metasArr = _self._slotMetasArray;
			ref var metaStart = ref metasArr is null
				? ref Unsafe.AsRef(in _self._inlineSlotMetas.Value)
				: ref MemoryMarshal.GetArrayDataReference(metasArr);
			var originalLastIndex = _originalLastIndex;

			for (var i = _bitHelper.FindFirstUnmarked();
			     (uint)i < (uint)originalLastIndex;
			     i = _bitHelper.FindFirstUnmarked(i + 1)) {
				var hashCode = Unsafe.Add(ref metaStart, i).HashCode;
				if (hashCode >= 0)
					_self.RemoveAt(i, hashCode);
			}
		}
	}

	[InlineArray(StackSize)]
	private struct InlineBuckets {
		public int Value;
	}

	[InlineArray(StackSize)]
	private struct InlineSlotMetas {
		public SlotMeta Value;
	}

	[InlineArray(StackSize)]
	private struct InlineValues {
		public T Value;
	}

	public ref struct Enumerator : IEnumerator<T>, IEnumerator {
		private readonly int _lastIndex;
		private readonly ref SlotMeta _metaStart;
		private readonly ref T _valueStart;
		private int _index;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(ref ValueSet<T, TKeyComparer> self) {
			_lastIndex = self._lastIndex;
			var metasArr = self._slotMetasArray;
			var valuesArr = self._valuesArray;
			_metaStart = ref metasArr is null
				? ref Unsafe.AsRef(in self._inlineSlotMetas.Value)
				: ref MemoryMarshal.GetArrayDataReference(metasArr);
			_valueStart = ref valuesArr is null
				? ref Unsafe.AsRef(in self._inlineValues.Value)
				: ref MemoryMarshal.GetArrayDataReference(valuesArr);
			_index = 0;
			Current = default!;
		}

		void IDisposable.Dispose() {
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext() {
			var index = _index;
			var lastIndex = _lastIndex;

			while (index < lastIndex) {
				if (Unsafe.Add(ref _metaStart, index).HashCode >= 0) {
					Current = Unsafe.Add(ref _valueStart, index);
					_index = index + 1;
					return true;
				}

				index++;
			}

			_index = lastIndex + 1;
			Current = default!;
			return false;
		}

		public T Current { get; private set; }

		object IEnumerator.Current {
			get {
				if (_index == 0 || _index == _lastIndex + 1)
					ThrowHelper.ThrowInvalidOperationException();
				return Current!;
			}
		}

		void IEnumerator.Reset() {
			_index = 0;
			Current = default!;
		}
	}
}

internal static class ThrowHelper {
	[DoesNotReturn]
	public static void ThrowInvalidOperationException()
		=> throw new InvalidOperationException();

	[DoesNotReturn]
	public static void ThrowArgumentOutOfRangeException()
		=> throw new ArgumentOutOfRangeException();
}

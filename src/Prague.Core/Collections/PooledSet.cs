using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Prague.Core.Utils;

namespace Prague.Core.Collections;

internal sealed class PooledSet<T, TKeyComparer> : IReadOnlyCollection<T>, IEnumerable<T>, IEnumerable, IDisposable
	where TKeyComparer : struct, IKeyComparer<T>
{
	public ref struct Enumerator
	{
		private readonly PooledSet<T, TKeyComparer> _owner;

		private readonly HashSlot<T>[] _slots;

		private readonly int _lastIndex;

		private readonly bool _isPooled;

		private ref HashSlot<T> _current;

		private int _index;

		public T Current
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				return _current.Value;
			}
		}

		public int CurrentHashCode
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				return _current.HashCode;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(PooledSet<T, TKeyComparer> owner, HashSlot<T>[] slots, int lastIndex, bool isPooled)
		{
			_owner = owner;
			_slots = slots;
			_lastIndex = lastIndex;
			_isPooled = isPooled;
			_current = ref Unsafe.NullRef<HashSlot<T>>();
			_index = -1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext()
		{
			ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_slots);
			while (++_index < _lastIndex)
			{
				ref var reference = ref Unsafe.Add(ref arrayDataReference, _index);
				if (reference.HashCode >= 0)
				{
					_current = ref reference;
					return true;
				}
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose()
		{
			if (_isPooled)
			{
				_owner.ReleasePooledArrays(_slots);
			}
		}
	}

	private sealed class BoxedEnumerator : IEnumerator<T>, IEnumerator, IDisposable
	{
		private readonly HashSlot<T>[] _slots;

		private readonly int _lastIndex;

		private int _index;

		public T Current => _slots[_index].Value;

		object? IEnumerator.Current => Current;

		internal BoxedEnumerator(HashSlot<T>[] slots, int lastIndex)
		{
			_slots = slots;
			_lastIndex = lastIndex;
			_index = -1;
		}

		public bool MoveNext()
		{
			while (++_index < _lastIndex)
			{
				if (_slots[_index].HashCode >= 0)
				{
					return true;
				}
			}
			return false;
		}

		public void Reset()
		{
			_index = -1;
		}

		public void Dispose()
		{
		}
	}

	private const int DefaultCapacity = 127;

	private const int Lower31BitMask = int.MaxValue;

	private static readonly ulong DefaultFastModMultiplier = HashHelpers.GetFastModMultiplier(127u);

	private readonly bool _clearOnFree = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

	private readonly TKeyComparer _comparer;

	private int[] _buckets;

	private HashSlot<T>[] _slots;

	private int _size;

	private ulong _fastModMultiplier;

	private int _count;

	private int _lastIndex;

	private int _freeList;

	private int[]? _pooledBuckets;

	private HashSlot<T>[]? _pooledSlots;

	private int _pooledRefCount;

	public static PooledSet<T, TKeyComparer> Empty = new PooledSet<T, TKeyComparer>();

	public int Count => _count;

	public bool IsEmpty => _count == 0;

	internal HashSlot<T>[] Slots
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			return _slots;
		}
	}

	internal int LastIndex
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			return _lastIndex;
		}
	}

	public PooledSet() : this(default) { }

	public PooledSet(TKeyComparer comparer)
	{
		_size = 127;
		_fastModMultiplier = DefaultFastModMultiplier;
		_buckets = ArrayPool<int>.Shared.Rent(_size);
		_slots = ArrayPool<HashSlot<T>>.Shared.Rent(_size);
		Array.Clear(_buckets, 0, _buckets.Length);
		_pooledBuckets = _buckets;
		_pooledSlots = _slots;
		_pooledRefCount = 1;
		_freeList = -1;
		_comparer = comparer;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Add(T item)
	{
		var hashCode = GetHashCode(item);
		var bucket = GetBucket(hashCode);
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_buckets);
		ref var arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(_slots);
		var num = Unsafe.Add(ref arrayDataReference, bucket) - 1;
		while (num >= 0)
		{
			ref var reference = ref Unsafe.Add(ref arrayDataReference2, num);
			if (reference.HashCode == hashCode && Equals(reference.Value, item))
			{
				return false;
			}
			num = reference.Next;
		}
		int num2;
		if (_freeList >= 0)
		{
			num2 = _freeList;
			_freeList = Unsafe.Add(ref arrayDataReference2, num2).Next;
		}
		else
		{
			if (_lastIndex == _size)
			{
				Grow();
				bucket = GetBucket(hashCode);
				arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_buckets);
				arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(_slots);
			}
			num2 = _lastIndex++;
		}
		ref var reference2 = ref Unsafe.Add(ref arrayDataReference2, num2);
		reference2.HashCode = hashCode;
		reference2.Next = Unsafe.Add(ref arrayDataReference, bucket) - 1;
		reference2.Value = item;
		Unsafe.Add(ref arrayDataReference, bucket) = num2 + 1;
		_count++;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(T item)
	{
		var hashCode = GetHashCode(item);
		var bucket = GetBucket(hashCode);
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_buckets);
		ref var arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(_slots);
		var num = -1;
		var num2 = Unsafe.Add(ref arrayDataReference, bucket) - 1;
		while (num2 >= 0)
		{
			ref var reference = ref Unsafe.Add(ref arrayDataReference2, num2);
			if (reference.HashCode == hashCode && Equals(reference.Value, item))
			{
				if (num < 0)
				{
					Unsafe.Add(ref arrayDataReference, bucket) = reference.Next + 1;
				}
				else
				{
					Unsafe.Add(ref arrayDataReference2, num).Next = reference.Next;
				}
				reference.HashCode = -1;
				reference.Next = _freeList;
				if (_clearOnFree)
				{
					reference.Value = default!;
				}
				_count--;
				if (_count == 0)
				{
					_lastIndex = 0;
					_freeList = -1;
				}
				else
				{
					_freeList = num2;
				}
				return true;
			}
			num = num2;
			num2 = reference.Next;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(T item)
	{
		var hashCode = GetHashCode(item);
		var bucket = GetBucket(hashCode);
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_buckets);
		ref var arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(_slots);
		var num = Unsafe.Add(ref arrayDataReference, bucket) - 1;
		while (num >= 0)
		{
			ref var reference = ref Unsafe.Add(ref arrayDataReference2, num);
			if (reference.HashCode == hashCode && Equals(reference.Value, item))
			{
				return true;
			}
			num = reference.Next;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool ContainsWithHashCode(T item, int hashCode)
	{
		var bucket = GetBucket(hashCode);
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(_buckets);
		ref var arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(_slots);
		var num = Unsafe.Add(ref arrayDataReference, bucket) - 1;
		while (num >= 0)
		{
			ref var reference = ref Unsafe.Add(ref arrayDataReference2, num);
			if (reference.HashCode == hashCode && Equals(reference.Value, item))
			{
				return true;
			}
			num = reference.Next;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Enumerator GetEnumerator()
	{
		var slots = _slots;
		var lastIndex = _lastIndex;
		var flag = slots == _pooledSlots;
		if (flag)
		{
			Interlocked.Increment(ref _pooledRefCount);
		}
		return new Enumerator(this, slots, lastIndex, flag);
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return new BoxedEnumerator(_slots, _lastIndex);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return new BoxedEnumerator(_slots, _lastIndex);
	}

	public void Dispose()
	{
		RetirePooledArrays();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetHashCode(T item) => _comparer.GetHashCode(item) & 0x7FFFFFFF;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Equals(T a, T b) => _comparer.Equals(a, b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucket(int hashCode)
	{
		return (int)HashHelpers.FastMod((uint)hashCode, (uint)_size, _fastModMultiplier);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow()
	{
		var num = HashHelpers.ExpandPrime(_count);
		var array = ArrayPool<int>.Shared.Rent(num);
		var array2 = ArrayPool<HashSlot<T>>.Shared.Rent(num);
		Array.Clear(array, 0, array.Length);
		if (_lastIndex > 0)
		{
			Array.Copy(_slots, array2, _lastIndex);
		}
		var fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)num);
		ref var arrayDataReference = ref MemoryMarshal.GetArrayDataReference(array2);
		ref var arrayDataReference2 = ref MemoryMarshal.GetArrayDataReference(array);
		for (var i = 0; i < _lastIndex; i++)
		{
			ref var reference = ref Unsafe.Add(ref arrayDataReference, i);
			if (reference.HashCode >= 0)
			{
				var elementOffset = (int)HashHelpers.FastMod((uint)reference.HashCode, (uint)num, fastModMultiplier);
				reference.Next = Unsafe.Add(ref arrayDataReference2, elementOffset) - 1;
				Unsafe.Add(ref arrayDataReference2, elementOffset) = i + 1;
			}
		}
		_buckets = array;
		_slots = array2;
		_size = num;
		_fastModMultiplier = fastModMultiplier;
		RetirePooledArrays();
	}

	private void RetirePooledArrays()
	{
		var pooledBuckets = _pooledBuckets;
		var pooledSlots = _pooledSlots;
		if (pooledBuckets != null)
		{
			_pooledBuckets = null;
			_pooledSlots = null;
			if (Interlocked.Decrement(ref _pooledRefCount) == 0)
			{
				ReturnPooledArrays(pooledBuckets, pooledSlots!);
			}
		}
	}

	internal void ReleasePooledArrays(HashSlot<T>[] slots)
	{
		if (Interlocked.Decrement(ref _pooledRefCount) == 0)
		{
			ReturnPooledArrays(_pooledBuckets!, slots);
		}
	}

	private static void ReturnPooledArrays(int[] buckets, HashSlot<T>[] slots)
	{
		try
		{
			ArrayPool<int>.Shared.Return(buckets);
		}
		catch (ArgumentException)
		{
		}
		try
		{
			ArrayPool<HashSlot<T>>.Shared.Return(slots, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
		}
		catch (ArgumentException)
		{
		}
	}
}

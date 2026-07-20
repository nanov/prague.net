using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Prague.Core.Collections;

public sealed class SortedArraySet<T> : IReadOnlyCollection<T>, IEnumerable<T>, IEnumerable, IDisposable
{
	public ref struct Enumerator
	{
		private readonly SortedArraySet<T> _owner;

		private readonly T[] _items;

		private readonly int _count;

		private bool _isPooled;

		private int _index;

		public T Current
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				return _items[_index];
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(SortedArraySet<T> owner, T[] items, int count, bool isPooled)
		{
			_owner = owner;
			_items = items;
			_count = count;
			_isPooled = isPooled;
			_index = -1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext()
		{
			return ++_index < _count;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose()
		{
			if (_isPooled)
			{
				_isPooled = false; // fire-once: a second Dispose must not decrement the refcount again
				_owner.ReleasePooledArray(_items);
			}
		}
	}

	private sealed class BoxedEnumerator : IEnumerator<T>, IEnumerator, IDisposable
	{
		private readonly T[] _items;

		private readonly int _count;

		private int _index;

		public T Current => _items[_index];

		object? IEnumerator.Current => Current;

		internal BoxedEnumerator(T[] items, int count)
		{
			_items = items;
			_count = count;
			_index = -1;
		}

		public bool MoveNext()
		{
			return ++_index < _count;
		}

		public void Reset()
		{
			_index = -1;
		}

		public void Dispose()
		{
		}
	}

	private const int PooledArraySize = 128;

	private T[] _items;

	private int _count;

	private T[]? _pooledArray;

	private int _pooledRefCount;

	public int Count => _count;

	public bool IsEmpty => _count == 0;

	public SortedArraySet()
	{
		_items = PragueArrayPool<T>.Pool.Rent(128);
		_pooledArray = _items;
		_pooledRefCount = 1;
		_count = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Add(T item)
	{
		var num = Array.BinarySearch(_items, 0, _count, item);
		if (num >= 0)
		{
			return false;
		}
		var num2 = ~num;
		if (_count == _items.Length)
		{
			var num3 = _items.Length * 2;
			var array = new T[num3];
			if (num2 > 0)
			{
				Array.Copy(_items, 0, array, 0, num2);
			}
			array[num2] = item;
			if (num2 < _count)
			{
				Array.Copy(_items, num2, array, num2 + 1, _count - num2);
			}
			_items = array;
			RetirePooledArray();
		}
		else
		{
			if (num2 < _count)
			{
				Array.Copy(_items, num2, _items, num2 + 1, _count - num2);
			}
			_items[num2] = item;
		}
		_count++;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(T item)
	{
		var num = Array.BinarySearch(_items, 0, _count, item);
		if (num < 0)
		{
			return false;
		}
		_count--;
		if (num < _count)
		{
			Array.Copy(_items, num + 1, _items, num, _count - num);
		}
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
		{
			_items[_count] = default!;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(T item)
	{
		return Array.BinarySearch(_items, 0, _count, item) >= 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<T> AsSpan()
	{
		return new ReadOnlySpan<T>(_items, 0, _count);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Enumerator GetEnumerator()
	{
		var items = _items;
		var flag = items == _pooledArray;
		if (flag)
		{
			Interlocked.Increment(ref _pooledRefCount);
		}
		return new Enumerator(this, items, _count, flag);
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return new BoxedEnumerator(_items, _count);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return new BoxedEnumerator(_items, _count);
	}

	public void Dispose()
	{
		RetirePooledArray();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void RetirePooledArray()
	{
		var pooledArray = _pooledArray;
		if (pooledArray != null)
		{
			_pooledArray = null;
			ReleasePooledArray(pooledArray);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void ReleasePooledArray(T[] array)
	{
		if (Interlocked.Decrement(ref _pooledRefCount) == 0)
		{
			PragueArrayPool<T>.Pool.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
		}
	}
}

namespace Prague.Core;

using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utils;

public interface IQueryResultDisposePolicy<T> {
	public bool CanDispose { get; set; }
	public void Dispose(ref T item);
}


/// <summary>
/// Non-generic interface for query results that can be enumerated as objects and disposed.
/// </summary>
public interface IQueryResults : IDisposable {
	int Count { get; }
	IEnumerable<object> AsEnumerable();
}

[DebuggerDisplay("Count = {Count}")]
public readonly struct QueryResults<T> : IList<T>, IReadOnlyList<T>, IDisposable, IQueryResults {
	public static QueryResults<T> Empty { get; } = new([]);

	private readonly T[]? _array;
	private readonly int _offset;
	private readonly int _capacity;
	private readonly int _count;
	private readonly int _totalCount;
	private readonly bool _isPooled;
	private readonly ArrayPool<T>? _pool;
	private readonly QueryResultsDisposer? _disposer;

	internal QueryResults(int totalCount) {
		_array = Array.Empty<T>();
		_offset = 0;
		_capacity = 0;
		_isPooled = false;
		_totalCount = totalCount;
	}


	internal QueryResults(int capacity, bool shouldPool) {
		_array = shouldPool ? ArrayPool<T>.Shared.Rent(capacity) : new T[capacity];
		_offset = 0;
		_capacity = _array.Length;
		_isPooled = shouldPool;
		_pool = shouldPool ? ArrayPool<T>.Shared : null;
	}

	internal QueryResults(int capacity, ArrayPool<T> pool) {
		_array = pool.Rent(capacity);
		_offset = 0;
		_capacity = _array.Length;
		_isPooled = true;
		_pool = pool;
	}

	private QueryResults(T[] array, bool isPooled = false) {
		_array = array;
		_offset = 0;
		_capacity = array.Length;
		_isPooled = isPooled;
	}

	private QueryResults(T[] array, int offset, int count, int actualCount, bool isPooled = false) {
		// Negative values discovered though conversion to high values when converted to unsigned
		if ((uint)offset > (uint)array.Length || (uint)count > (uint)(array.Length - offset))
			throw RangeException(array, offset, count);

		_array = array;
		_isPooled = isPooled;
		_offset = offset;
		_capacity = count;
		_count = actualCount;
		_totalCount = actualCount;
	}

	private QueryResults(int capacity, int offset, int count, int totalCount, bool shouldPool) {
		_array = shouldPool ? ArrayPool<T>.Shared.Rent(capacity) : new T[capacity];
		_offset = offset;
		_count = count;
		_totalCount = totalCount;
		_capacity = _array.Length;
		_isPooled = shouldPool;
		_pool = shouldPool ? ArrayPool<T>.Shared : null;
	}

	private QueryResults(int capacity, int offset, int count, int totalCount, ArrayPool<T> pool) {
		_array = pool.Rent(capacity);
		_offset = offset;
		_count = count;
		_totalCount = totalCount;
		_capacity = _array.Length;
		_isPooled = true;
		_pool = pool;
	}

	private static Exception RangeException(T[] array, int offset, int count) {
		if (offset < 0)
			return new ArgumentOutOfRangeException(nameof(offset), "Must be positive");
		if (count < 0)
			return new ArgumentOutOfRangeException(nameof(count), "Must be positive");

		Debug.Assert(array.Length - offset < count);
		return new ArgumentException("Offset out of array bounds");
	}

	public int Count => _count;
	public int TotalCount => _totalCount;

	internal ref T UnsafeGetRef(int index)
		=> ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_array!), _offset + index);

	public T this[int index] {
		get => (uint)index >= (uint)_count
			? throw new ArgumentOutOfRangeException(nameof(index))
			: _array![_offset + index];
		set {
			if ((uint)index >= (uint)_count)
				throw new ArgumentOutOfRangeException(nameof(index));
			_array![_offset + index] = value;
		}
	}

	internal static QueryResults<T> EmptyWithTotalCount(int totalCount) {
		return totalCount is 0 ? Empty : new QueryResults<T>(totalCount);
	}

	/// <summary>
	/// Creates a QueryResults from an existing array. Used by join builders that manage their own arrays.
	/// </summary>
	internal static QueryResults<T> FromArray(T[] array, int offset, int count, int totalCount, bool isPooled) {
		return new QueryResults<T>(array, offset, count, totalCount, isPooled, ArrayPool<T>.Shared, null);
	}

	/// <summary>
	/// Creates a QueryResults from an existing array with a disposer. Used by unified join builders.
	/// </summary>
	internal static QueryResults<T> FromArray(T[] array, int offset, int count, int totalCount, bool isPooled, QueryResultsDisposer? disposer) {
		return new QueryResults<T>(array, offset, count, totalCount, isPooled, ArrayPool<T>.Shared, disposer);
	}

	private QueryResults(T[] array, int offset, int count, int totalCount, bool isPooled, ArrayPool<T>? pool, QueryResultsDisposer? disposer) {
		if ((uint)offset > (uint)array.Length || (uint)count > (uint)(array.Length - offset))
			throw RangeException(array, offset, count);

		_array = array;
		_offset = offset;
		_capacity = count;
		_count = count;
		_totalCount = totalCount;
		_isPooled = isPooled;
		_pool = pool;
		_disposer = disposer;
	}


	public Enumerator GetEnumerator() {
		return new Enumerator(this);
	}

	public void Dispose() {
		_disposer?.Dispose();

		if (!_isPooled || _array is null || _array.Length is 0)
			return;

		(_pool ?? ArrayPool<T>.Shared).Return(_array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
		Unsafe.AsRef<T[]?>(in _array) = null;
	}

	public override int GetHashCode() {
		return _array is null ? 0 : HashCode.Combine(_offset, _count, _array.GetHashCode());
	}

	public override bool Equals(object? obj) {
		return obj is QueryResults<T> other && Equals(other);
	}

	public bool Equals(QueryResults<T> other) {
		return _array == other._array && _offset == other._offset && _count == other._count;
	}

	public void CopyTo(T[] destination) {
		CopyTo(destination, 0);
	}

	public void CopyTo(T[] destination, int destinationIndex) {
		Array.Copy(_array!, _offset, destination, destinationIndex, _count);
	}

	public void CopyTo(QueryResults<T> destination) {
		destination.SetEmptyIfNotInitilized();

		if (_count > destination._count)
			throw new IndexOutOfRangeException();

		Array.Copy(_array!, _offset, destination._array!, destination._offset, _count);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public QueryResults<TMapped> Map<TMapped>(Func<T, TMapped> mapper) {
		if (_count == 0)
			return QueryResults<TMapped>.Empty;
		var results = new QueryResults<TMapped>(_capacity, 0, _count, _totalCount, _isPooled);
		QueryResultMarshal.Map(AsSpan(), results.AsSpan(), mapper);
		Dispose();
		return results;
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<T> Slice(int index) {
		return (uint)index > (uint)_count
			? throw new ArgumentOutOfRangeException(nameof(index))
			: new QueryResults<T>(_array!, _offset + index, _count - index, _count - index, _isPooled);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public QueryResults<T> Slice(int index, int count) {
		if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index))
			throw new ArgumentOutOfRangeException(nameof(index));

		return new QueryResults<T>(_array!, _offset + index, count, count, _isPooled);
	}

	public void SliceLeaveTotalCount(int index, int count) {
		if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index))
			throw new ArgumentOutOfRangeException(nameof(index));

		Unsafe.AsRef(in _offset) = _offset + index;
		Unsafe.AsRef(in _capacity) = count;
		Unsafe.AsRef(in _count) = count;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Sort<TComparer>(TComparer comparer) where TComparer : IComparer<T> {
		AsSpan().Sort(comparer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Memory<T> AsMemory() {
		if (_array is null || _count is 0)
			return Memory<T>.Empty;

		return new Memory<T>(_array!, _offset, _count);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<T> AsSpan() {
		return new Span<T>(_array!, _offset, _count);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void CloneElements<TCloner>(TCloner cloner) where TCloner : struct, ICloner<T> {
		var span = AsSpan();
		QueryResultMarshal.CloneInPlace(span, cloner);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void CloneElements() {
		if (!typeof(T).IsAssignableTo(typeof(ICacheClonable<T>)))
			return;
		CloneElements(default(UnsafeCacheClonableCloner<T>));
	}

	internal int UnsafeAdd(T item) {
		if (_count == _capacity)
			throw new IndexOutOfRangeException($"UnsafeAdd overflow: count={_count}, capacity={_capacity}");
		_array![_offset + _count] = item;
		var count = _count;
		Unsafe.AsRef(in _count) += 1;
		Unsafe.AsRef(in _totalCount) += 1;
		return count;
	}

	internal void UnsafeSetTotal(int total) {
		Unsafe.AsRef(in _totalCount) = total;
	}

	internal void SetPendingCapacity(int capacity) {
		Unsafe.AsRef(in _capacity) = capacity;
	}

	internal int AssignSharedBuffer(T[] sharedArray, int offset) {
		if (_capacity is 0) {
			Unsafe.AsRef(in _array) = Array.Empty<T>();
			return offset;
		}

		Unsafe.AsRef(in _array) = sharedArray;
		Unsafe.AsRef(in _offset) = offset;
		return offset + _capacity;
	}

	public T[] ToArray() {
		if (_array is null)
			return Array.Empty<T>();

		if (!_isPooled && _offset == 0 && _count == _array.Length)
			return _array;

		var array = new T[_count];
		Array.Copy(_array, _offset, array, 0, _count);
		Dispose();
		return array;
	}

	public T[] ToPooledArray() {
		return ToPooledArray(ArrayPool<T>.Shared);
	}

	public T[] ToPooledArray(ArrayPool<T> pool) {
		if (_array is null)
			return Array.Empty<T>();

		if (_isPooled && ReferenceEquals(pool, _pool ?? ArrayPool<T>.Shared) && _offset == 0) {
			var arr = _array;
			Unsafe.AsRef<T[]?>(in _array) = null;
			return arr;
		}

		var array = pool.Rent(_count); // new T[_count];
		Array.Copy(_array, _offset, array, 0, _count);
		Dispose();
		return array;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public List<T> ToList() {
		if (_array is null)
			return new List<T>();

		if (_offset == 0 && _count == _array.Length) {
			var s = new List<T>(_array);
			Dispose();
			return s;
		}

		var span = AsSpan();
		var list = new List<T>(span.Length);
		CollectionsMarshal.SetCount(list, span.Length);
		span.CopyTo(CollectionsMarshal.AsSpan(list));
		Dispose();
		return list;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public HashSet<T> ToHashSet() {
		if (_array is null)
			return new HashSet<T>();

		if (_offset == 0 && _count == _array.Length) {
			var s = new HashSet<T>(_array);
			Dispose();
			return s;
		}

		var span = AsSpan();
		var set = new HashSet<T>(span.Length);
		ref var searchSpace = ref MemoryMarshal.GetReference(span);
		for (var i = 0; i < span.Length; i++)
			set.Add(Unsafe.Add(ref searchSpace, i));
		Dispose();
		return set;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public Dictionary<TKey, T> ToDictionary<TKey>(Func<T, TKey> keySelector) where TKey : notnull {
		if (_array is null)
			return new Dictionary<TKey, T>();

		var span = AsSpan();
		var dictionary = new Dictionary<TKey, T>(span.Length);
		ref var searchSpace = ref MemoryMarshal.GetReference(span);
		for (var i = 0; i < span.Length; i++) {
			ref var item = ref Unsafe.Add(ref searchSpace, i);
			var key = keySelector(item);
			ref var valueRef = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out var exists);

			if (exists)
				throw new ArgumentException($"An item with the same key has already been added. Key: {key}");

			valueRef = item;
		}

		Dispose();
		return dictionary;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(Func<T, TKey> keySelector, Func<T, TValue> valueSelector)
		where TKey : notnull {
		if (_array is null)
			return new Dictionary<TKey, TValue>();

		var span = AsSpan();
		var dictionary = new Dictionary<TKey, TValue>(span.Length);
		ref var searchSpace = ref MemoryMarshal.GetReference(span);
		for (var i = 0; i < span.Length; i++) {
			ref var item = ref Unsafe.Add(ref searchSpace, i);
			var key = keySelector(item);
			ref var valueRef = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out var exists);

			if (exists)
				throw new ArgumentException($"An item with the same key has already been added. Key: {key}");

			valueRef = valueSelector(item);
		}

		Dispose();
		return dictionary;
	}


	public static bool operator ==(QueryResults<T> a, QueryResults<T> b) {
		return a.Equals(b);
	}

	public static bool operator !=(QueryResults<T> a, QueryResults<T> b) {
		return !(a == b);
	}

	public static implicit operator ReadOnlySpan<T>(in QueryResults<T> results) {
		return results.AsSpan();
	}

	T IList<T>.this[int index] {
		get {
			if (index < 0 || index >= _count)
				throw new ArgumentOutOfRangeException(nameof(index));

			return _array![_offset + index];
		}

		set {
			if (index < 0 || index >= _count)
				throw new ArgumentOutOfRangeException(nameof(index));

			_array![_offset + index] = value;
		}
	}

	int IList<T>.IndexOf(T item) {
		var index = Array.IndexOf(_array!, item, _offset, _count);
		Debug.Assert(index < 0 || (index >= _offset && index < _offset + _count));
		return index >= 0 ? index - _offset : -1;
	}

	void IList<T>.Insert(int index, T item) {
		throw new NotSupportedException();
	}

	void IList<T>.RemoveAt(int index) {
		throw new NotSupportedException();
	}

	T IReadOnlyList<T>.this[int index] {
		get {
			if (index < 0 || index >= _count)
				throw new ArgumentOutOfRangeException(nameof(index));

			return _array![_offset + index];
		}
	}


	bool ICollection<T>.IsReadOnly => true;

	void ICollection<T>.Add(T item) {
		throw new NotSupportedException();
	}

	void ICollection<T>.Clear() {
		throw new NotSupportedException();
	}

	bool ICollection<T>.Contains(T item) {
		var index = Array.IndexOf(_array!, item, _offset, _count);
		Debug.Assert(index < 0 || (index >= _offset && index < _offset + _count));
		return index >= 0;
	}

	bool ICollection<T>.Remove(T item) {
		throw new NotSupportedException();
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator() {
		return new Enumerator(this);
	}

	IEnumerator IEnumerable.GetEnumerator() {
		return ((IEnumerable<T>)this).GetEnumerator();
	}

	IEnumerable<object> IQueryResults.AsEnumerable() {
		for (var i = 0; i < _count; i++)
			yield return _array![_offset + i]!;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SetEmptyIfNotInitilized() {
		if (_array == null)
			Unsafe.AsRef(in _array) = Array.Empty<T>();
	}

	public struct Enumerator : IEnumerator<T> {
		private readonly T[]? _array;
		private readonly int _start;
		private readonly int _end; // cache Offset + Count, since it's a little slow
		private int _current;

		internal Enumerator(QueryResults<T> arraySegment) {
			var array = arraySegment._array ?? Array.Empty<T>();
			Debug.Assert(arraySegment._offset >= 0);
			Debug.Assert(arraySegment._count >= 0);
			Debug.Assert(arraySegment._offset + arraySegment._count <= array.Length);

			_array = array;
			_start = arraySegment._offset;
			_end = arraySegment._offset + arraySegment._count;
			_current = arraySegment._offset - 1;
		}

		public bool MoveNext() {
			if (_current >= _end)
				return false;
			_current++;
			return _current < _end;
		}

		public ref T Current {
			get {
				if (_current < _start)
					throw new InvalidOperationException("Not started?");
				if (_current >= _end)
					throw new InvalidOperationException("Ended?");
				return ref _array![_current];
			}
		}

		// Explicit: IEnumerator<T>.Current is by-value; the by-ref Current above is what foreach binds to.
		T IEnumerator<T>.Current => Current;

		object? IEnumerator.Current => Current;

		void IEnumerator.Reset() {
			_current = _start - 1;
		}

		public void Dispose() {
		}
	}
}

/// <summary>
/// A simple disposer that holds a list of dispose actions.
/// Used for unified join builders where the disposer type cannot be statically determined.
/// Small allocation but simplifies the API significantly.
/// </summary>
public sealed class QueryResultsDisposer {
	private readonly Action[] _actions;
	private int _count;

	public QueryResultsDisposer(int capacity) {
		_actions = new Action[capacity];
		_count = 0;
	}

	public void Add(Action action) {
		_actions[_count++] = action;
	}

	public void Dispose() {
		for (var i = 0; i < _count; i++)
			_actions[i]();
	}
}

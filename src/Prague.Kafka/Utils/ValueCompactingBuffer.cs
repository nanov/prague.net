namespace Prague.Kafka.Utils;

using System.Runtime.CompilerServices;

/// <summary>
///   Object-level last-write-wins compaction buffer for the raw consume path's loading phase.
///   Unlike <see cref="CompactingBuffer{TKey}"/> (which held pooled value bytes), this holds the
///   already-deserialized + enriched <typeparamref name="TValue"/> — the raw path can't retain the
///   native value spans, and copying bytes back into a pool is exactly the allocation we're cutting.
///   Deduping the materialized object by key still skips the costly <c>AddOrUpdate</c>/index work for
///   superseded records, which is flushed once per surviving key at partition EOF.
/// </summary>
internal sealed class ValueCompactingBuffer<TKey, TValue>
	where TKey : IEquatable<TKey>, IComparable<TKey>
	where TValue : class {

	private readonly IndexMap<TKey> _index;
	private readonly TValue?[] _values;
	private readonly long[] _timestamps;
	private int _count;

	public ValueCompactingBuffer(int capacity) {
		_index = new IndexMap<TKey>(capacity);
		_values = new TValue?[capacity];
		_timestamps = new long[capacity];
		_count = 0;
	}

	public int Count => _index.Count;

	/// <summary>Buffer or replace the latest value for <paramref name="key"/> (last-write-wins).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOrReplace(TKey key, TValue value, long timestampMs) {
		if (_index.TryRemove(key, out var existingIdx))
			_values[existingIdx] = null; // vacate the superseded slot

		var idx = _count++;
		_values[idx] = value;
		_timestamps[idx] = timestampMs;
		_index.Insert(key, idx);
	}

	/// <summary>Cancel any buffered value for <paramref name="key"/> (a tombstone/filter-delete during load).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key) {
		if (_index.TryRemove(key, out var existingIdx))
			_values[existingIdx] = null;
	}

	public bool IsFull(int capacity) => _count >= capacity;

	public Enumerator GetEnumerator() => new(_values, _timestamps, _count);

	public void Clear() {
		_values.AsSpan(0, _count).Clear();
		_index.Clear(_count);
		_count = 0;
	}

	internal struct Enumerator {
		private readonly TValue?[] _values;
		private readonly long[] _timestamps;
		private readonly int _count;
		private int _index;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(TValue?[] values, long[] timestamps, int count) {
			_values = values;
			_timestamps = timestamps;
			_count = count;
			_index = -1;
		}

		public readonly (TValue Value, long TimestampMs) Current {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => (_values[_index]!, _timestamps[_index]);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext() {
			while (++_index < _count)
				if (_values[_index] is not null)
					return true;
			return false;
		}
	}
}

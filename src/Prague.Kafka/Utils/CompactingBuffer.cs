namespace Prague.Kafka.Utils;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Prague.Kafka.SerDe;
using Confluent.Kafka;

internal class CompactingBuffer<TKey>
	where TKey : IEquatable<TKey> {

	private readonly int _capacity;
	private readonly IndexMap<TKey> _index;
	private readonly ConsumeResult<RentedBytesWithHandler, RentedBytes>?[] _entries;
	private int _count;

	public CompactingBuffer(int capacity) {
		_capacity = capacity;
		_index = new IndexMap<TKey>(capacity);
		_entries = new ConsumeResult<RentedBytesWithHandler, RentedBytes>?[capacity];
		_count = 0;
	}

	public bool IsFull => _count >= _capacity;
	public int Count => _index.Count;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(TKey key, ConsumeResult<RentedBytesWithHandler, RentedBytes> result) {
		if (_index.TryRemove(key, out var existingIdx)) {
			// Dispose the old entry's rented value and vacate the old slot
			_entries[existingIdx]!.Message.Value.Dispose();
			_entries[existingIdx] = null;
		}

		// Null value means delete — skip buffering
		if (result.Message.Value.IsNull) {
			result.Message.Value.Dispose();
			return;
		}

		// Always append to the end to preserve correct order
		var idx = _count++;
		_entries[idx] = result;
		_index.Insert(key, idx);
	}

	public Enumerator GetEnumerator() => new(_entries, _index.Keys, _count);

	public void Clear() {
		_entries.AsSpan(0, _count).Clear();
		_index.Clear(_count);
		_count = 0;
	}

	/// Disposes any remaining entries that weren't flushed.
	public void DisposeEntries() {
		for (var i = 0; i < _count; i++) {
			if (_entries[i] is not null)
				_entries[i]!.Message.Value.Dispose();
		}
	}

	internal struct Enumerator {
		private readonly ConsumeResult<RentedBytesWithHandler, RentedBytes>?[] _entries;
		private readonly TKey[] _keys;
		private readonly int _count;
		private int _index;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(
			ConsumeResult<RentedBytesWithHandler, RentedBytes>?[] entries,
			TKey[] keys,
			int count) {
			_entries = entries;
			_keys = keys;
			_count = count;
			_index = -1;
		}

		public readonly KeyValuePair<TKey, ConsumeResult<RentedBytesWithHandler, RentedBytes>> Current {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => new(_keys[_index], _entries[_index]!);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext() {
			while (++_index < _count) {
				if (_entries[_index] is not null)
					return true;
			}

			return false;
		}
	}
}

[SkipLocalsInit]
internal class IndexMap<TKey>
	where TKey : IEquatable<TKey> {

	private const int Empty = -1;
	private const int Tombstone = -2;

	private readonly int _capacityMask;
	private readonly int _bucketCount;
	private readonly int[] _metadata;   // bucket -> slot index
	private readonly TKey[] _keys;      // slot index -> key
	private int _count;

	public IndexMap(int capacity) {
		_bucketCount = GetPowerOf2Capacity(capacity);
		_capacityMask = _bucketCount - 1;
		_metadata = new int[_bucketCount];
		_metadata.AsSpan().Fill(Empty);
		_keys = new TKey[capacity];
		_count = 0;
	}

	public int Count => _count;
	public TKey[] Keys => _keys;

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void Insert(TKey key, int slotIndex) {
		var capacityMask = _capacityMask;
		var bucket = key.GetHashCode() & capacityMask;

		ref var metadataStart = ref MemoryMarshal.GetArrayDataReference(_metadata);

		while (Unsafe.Add(ref metadataStart, bucket) >= 0)
			bucket = (bucket + 1) & capacityMask;

		Unsafe.Add(ref metadataStart, bucket) = slotIndex;
		_keys[slotIndex] = key;
		_count++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool TryRemove(TKey key, out int slotIndex) {
		var capacityMask = _capacityMask;
		var bucket = key.GetHashCode() & capacityMask;

		ref var metadataStart = ref MemoryMarshal.GetArrayDataReference(_metadata);
		ref var keysStart = ref MemoryMarshal.GetArrayDataReference(_keys);

		while (true) {
			var idx = Unsafe.Add(ref metadataStart, bucket);

			if (idx == Empty) {
				slotIndex = default;
				return false;
			}

			if (idx >= 0 && Unsafe.Add(ref keysStart, idx).Equals(key)) {
				slotIndex = idx;
				Unsafe.Add(ref metadataStart, bucket) = Tombstone;
				_count--;
				return true;
			}

			bucket = (bucket + 1) & capacityMask;
		}
	}

	public void Clear(int slotCount) {
		_metadata.AsSpan(0, _bucketCount).Fill(Empty);
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
			_keys.AsSpan(0, slotCount).Clear();
		_count = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetPowerOf2Capacity(int expectedCount) {
		var minCapacity = (expectedCount * 4 + 2) / 3;
		minCapacity = Math.Max(minCapacity, 4);
		return (int)BitOperations.RoundUpToPowerOf2((uint)minCapacity);
	}
}

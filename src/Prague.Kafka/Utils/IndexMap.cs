namespace Prague.Kafka.Utils;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

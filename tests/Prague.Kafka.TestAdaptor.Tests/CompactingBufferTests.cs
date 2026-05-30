namespace Prague.Kafka.TestAdaptor.Tests;

using System.Collections.Generic;
using Prague.Kafka.SerDe;
using Confluent.Kafka;
using Utils;

[TestFixture]
public class CompactingBufferTests {
	private static ConsumeResult<RentedBytesWithHandler, RentedBytes> MakeEntry(byte[] value) {
		return new ConsumeResult<RentedBytesWithHandler, RentedBytes> {
			Message = new Message<RentedBytesWithHandler, RentedBytes> {
				Key = new RentedBytesWithHandler(null!, true, default, true),
				Value = new RentedBytes(false, value),
				Timestamp = new Timestamp(DateTimeOffset.UtcNow)
			},
			Offset = 0
		};
	}

	private static ConsumeResult<RentedBytesWithHandler, RentedBytes> MakeTombstone() {
		return new ConsumeResult<RentedBytesWithHandler, RentedBytes> {
			Message = new Message<RentedBytesWithHandler, RentedBytes> {
				Key = new RentedBytesWithHandler(null!, true, default, true),
				Value = new RentedBytes(true, ReadOnlySpan<byte>.Empty),
				Timestamp = new Timestamp(DateTimeOffset.UtcNow)
			},
			Offset = 0
		};
	}

	private static List<KeyValuePair<string, ConsumeResult<RentedBytesWithHandler, RentedBytes>>> Collect(
		CompactingBuffer<string> buffer) {
		var list = new List<KeyValuePair<string, ConsumeResult<RentedBytesWithHandler, RentedBytes>>>();
		foreach (var entry in buffer)
			list.Add(entry);
		return list;
	}

	[Test]
	public void Add_SingleEntry_CountIsOne() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1, 2, 3]));

		Assert.That(buffer.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleDistinctKeys_CountMatchesKeys() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));
		buffer.Add("key3", MakeEntry([3]));

		Assert.That(buffer.Count, Is.EqualTo(3));
	}

	[Test]
	public void Add_DuplicateKey_CountRemainsOne() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key1", MakeEntry([2]));

		Assert.That(buffer.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_DuplicateKey_PreservesLatestValueAtEnd() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));
		buffer.Add("key1", MakeEntry([3]));

		var entries = Collect(buffer);

		Assert.That(entries, Has.Count.EqualTo(2));
		// key2 should come first (original order), key1 should be last (moved to end)
		Assert.That(entries[0].Key, Is.EqualTo("key2"));
		Assert.That(entries[1].Key, Is.EqualTo("key1"));
		// key1's value should be the latest
		Assert.That(entries[1].Value.Message.Value.AsSpan().ToArray(), Is.EqualTo(new byte[] { 3 }));
	}

	[Test]
	public void Add_Tombstone_SkipsBuffering() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeTombstone());

		Assert.That(buffer.Count, Is.EqualTo(0));
	}

	[Test]
	public void Add_TombstoneAfterExisting_RemovesEntry() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key1", MakeTombstone());

		Assert.That(buffer.Count, Is.EqualTo(0));
		var entries = Collect(buffer);
		Assert.That(entries, Is.Empty);
	}

	[Test]
	public void Add_TombstoneThenNewValue_ReAddsEntry() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key1", MakeTombstone());
		buffer.Add("key1", MakeEntry([2]));

		Assert.That(buffer.Count, Is.EqualTo(1));
		var entries = Collect(buffer);
		Assert.That(entries, Has.Count.EqualTo(1));
		Assert.That(entries[0].Value.Message.Value.AsSpan().ToArray(), Is.EqualTo(new byte[] { 2 }));
	}

	[Test]
	public void Enumeration_PreservesInsertionOrder() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("c", MakeEntry([3]));
		buffer.Add("a", MakeEntry([1]));
		buffer.Add("b", MakeEntry([2]));

		var entries = Collect(buffer);

		Assert.That(entries, Has.Count.EqualTo(3));
		Assert.That(entries[0].Key, Is.EqualTo("c"));
		Assert.That(entries[1].Key, Is.EqualTo("a"));
		Assert.That(entries[2].Key, Is.EqualTo("b"));
	}

	[Test]
	public void Enumeration_SkipsVacatedSlots() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));
		buffer.Add("key3", MakeEntry([3]));
		// Override key1 — slot 0 becomes vacated, key1 moves to slot 3
		buffer.Add("key1", MakeEntry([4]));

		var entries = Collect(buffer);

		Assert.That(entries, Has.Count.EqualTo(3));
		Assert.That(entries[0].Key, Is.EqualTo("key2"));
		Assert.That(entries[1].Key, Is.EqualTo("key3"));
		Assert.That(entries[2].Key, Is.EqualTo("key1"));
	}

	[Test]
	public void IsFull_ReturnsTrueWhenCapacityReached() {
		var buffer = new CompactingBuffer<string>(3);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));
		Assert.That(buffer.IsFull, Is.False);

		buffer.Add("key3", MakeEntry([3]));
		Assert.That(buffer.IsFull, Is.True);
	}

	[Test]
	public void IsFull_DuplicateConsumesSlot() {
		// Duplicates vacate old slot but append to end, consuming a new slot
		var buffer = new CompactingBuffer<string>(3);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));
		// Override key1 — uses slot 2 (the last one)
		buffer.Add("key1", MakeEntry([3]));

		Assert.That(buffer.IsFull, Is.True);
		Assert.That(buffer.Count, Is.EqualTo(2));
	}

	[Test]
	public void Clear_ResetsBuffer() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));
		buffer.Add("key2", MakeEntry([2]));

		// Manually dispose values before clear (as caller would in real usage)
		foreach (var (_, entry) in buffer)
			entry.Message.Value.Dispose();

		buffer.Clear();

		Assert.That(buffer.Count, Is.EqualTo(0));
		Assert.That(buffer.IsFull, Is.False);
		var entries = Collect(buffer);
		Assert.That(entries, Is.Empty);
	}

	[Test]
	public void Clear_ThenReuse_WorksCorrectly() {
		var buffer = new CompactingBuffer<string>(16);
		buffer.Add("key1", MakeEntry([1]));

		foreach (var (_, entry) in buffer)
			entry.Message.Value.Dispose();
		buffer.Clear();

		buffer.Add("key2", MakeEntry([2]));

		Assert.That(buffer.Count, Is.EqualTo(1));
		var entries = Collect(buffer);
		Assert.That(entries, Has.Count.EqualTo(1));
		Assert.That(entries[0].Key, Is.EqualTo("key2"));
	}
}

[TestFixture]
public class IndexMapTests {
	[Test]
	public void Insert_ThenTryRemove_FindsEntry() {
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);

		Assert.That(map.Count, Is.EqualTo(1));
		Assert.That(map.TryRemove("key1", out var idx), Is.True);
		Assert.That(idx, Is.EqualTo(0));
		Assert.That(map.Count, Is.EqualTo(0));
	}

	[Test]
	public void TryRemove_NonExistent_ReturnsFalse() {
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);

		Assert.That(map.TryRemove("key2", out _), Is.False);
		Assert.That(map.Count, Is.EqualTo(1));
	}

	[Test]
	public void TryRemove_Empty_ReturnsFalse() {
		var map = new IndexMap<string>(16);
		Assert.That(map.TryRemove("key1", out _), Is.False);
	}

	[Test]
	public void Insert_MultipleKeys_AllResolvable() {
		var map = new IndexMap<string>(16);
		map.Insert("a", 0);
		map.Insert("b", 1);
		map.Insert("c", 2);

		Assert.That(map.Count, Is.EqualTo(3));

		Assert.That(map.TryRemove("b", out var idx), Is.True);
		Assert.That(idx, Is.EqualTo(1));

		Assert.That(map.TryRemove("a", out idx), Is.True);
		Assert.That(idx, Is.EqualTo(0));

		Assert.That(map.TryRemove("c", out idx), Is.True);
		Assert.That(idx, Is.EqualTo(2));
	}

	[Test]
	public void TryRemove_AfterRemoval_ReturnsFalse() {
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);
		map.TryRemove("key1", out _);

		Assert.That(map.TryRemove("key1", out _), Is.False);
	}

	[Test]
	public void Insert_WithHashCollisions_StillResolvable() {
		// Insert many keys to increase collision likelihood
		var map = new IndexMap<int>(64);
		for (var i = 0; i < 40; i++)
			map.Insert(i, i);

		Assert.That(map.Count, Is.EqualTo(40));

		for (var i = 0; i < 40; i++) {
			Assert.That(map.TryRemove(i, out var idx), Is.True);
			Assert.That(idx, Is.EqualTo(i));
		}

		Assert.That(map.Count, Is.EqualTo(0));
	}

	[Test]
	public void Clear_ResetsState() {
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);
		map.Insert("key2", 1);

		map.Clear(2);

		Assert.That(map.Count, Is.EqualTo(0));
		Assert.That(map.TryRemove("key1", out _), Is.False);
		Assert.That(map.TryRemove("key2", out _), Is.False);
	}

	[Test]
	public void Clear_ThenReinsert_WorksCorrectly() {
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);
		map.Clear(1);

		map.Insert("key2", 0);
		Assert.That(map.Count, Is.EqualTo(1));
		Assert.That(map.TryRemove("key2", out var idx), Is.True);
		Assert.That(idx, Is.EqualTo(0));
	}

	[Test]
	public void Remove_ThenInsertNew_TombstoneDoesNotBlock() {
		// After removing a key, a tombstone is left in the bucket.
		// Inserting a new key that probes through the tombstone should still work.
		var map = new IndexMap<string>(16);
		map.Insert("key1", 0);
		map.TryRemove("key1", out _);

		map.Insert("key2", 1);
		Assert.That(map.Count, Is.EqualTo(1));
		Assert.That(map.TryRemove("key2", out var idx), Is.True);
		Assert.That(idx, Is.EqualTo(1));
	}

	[Test]
	public void Keys_StoresKeyAtSlotIndex() {
		var map = new IndexMap<string>(16);
		map.Insert("alpha", 3);
		map.Insert("beta", 7);

		Assert.That(map.Keys[3], Is.EqualTo("alpha"));
		Assert.That(map.Keys[7], Is.EqualTo("beta"));
	}
}

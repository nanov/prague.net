namespace Prague.Core.Tests.Indexing;

using NUnit.Framework;
using Prague.Core;
using Prague.Core.Collections;

/// <summary>
///   The keys / values counters of a Many index (<c>prague.kafka.cache.index.size</c>). A bucket is
///   mutated in place, so the counters must follow what each add and remove actually did: comparing
///   the bucket's count "before" and "after" compares one object with itself.
/// </summary>
[TestFixture]
public class ListIndexCounterTests {
	private static readonly DefaultKeyComparer<long> Hash = default;

	private static (ulong Keys, ulong Values) Counters(ICountableCacheIndex index) {
		var keys = index.GetCounters(out var values);
		return (keys, values);
	}

	[Test]
	public void OneBucket_CountsEveryValue_AndGivesThemBack() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, _) => 0L);

		for (long key = 0; key < 1000; key++) {
			index.Add(key, Hash.GetHashCode(key), "v", 0);
		}

		Assert.That(Counters(index), Is.EqualTo((1UL, 1000UL)), "one bucket holding a thousand values");

		for (long key = 0; key < 1000; key++) {
			index.Remove(key, Hash.GetHashCode(key), "v", 0);
		}

		Assert.That(Counters(index), Is.EqualTo((0UL, 0UL)), "an emptied index counts nothing");
	}

	[Test]
	public void ManyBuckets_FollowAddsAndRemoves() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (key, _) => key % 10);

		for (long key = 0; key < 1000; key++) {
			index.Add(key, Hash.GetHashCode(key), "v", 0);
		}

		Assert.That(Counters(index), Is.EqualTo((10UL, 1000UL)));

		for (long key = 0; key < 500; key++) {
			index.Remove(key, Hash.GetHashCode(key), "v", 0);
		}

		Assert.That(Counters(index), Is.EqualTo((10UL, 500UL)), "every bucket still holds half of its values");

		for (long key = 500; key < 1000; key++) {
			index.Remove(key, Hash.GetHashCode(key), "v", 0);
		}

		Assert.That(Counters(index), Is.EqualTo((0UL, 0UL)));
	}

	[Test]
	public void AddingTheSameKeyTwice_CountsItOnce() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, _) => 0L);

		index.Add(1, Hash.GetHashCode(1), "v", 0);
		index.Add(1, Hash.GetHashCode(1), "v", 0);

		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));
	}

	[Test]
	public void Update_MovingAKeyBetweenBuckets_KeepsBothCounters() {
		// The index key is the value's length: "a" -> 1, "bb" -> 2.
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);

		index.Add(1, Hash.GetHashCode(1), "a", 0);
		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));

		// The only value of bucket 1 moves to a new bucket 2: one bucket created, one removed.
		index.Update(1, Hash.GetHashCode(1), "a", "bb", 0);
		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)), "a move must not change either counter");

		index.Add(2, Hash.GetHashCode(2), "bb", 0);
		Assert.That(Counters(index), Is.EqualTo((1UL, 2UL)));

		// Key 1 moves back: bucket 1 is created again, bucket 2 keeps key 2.
		index.Update(1, Hash.GetHashCode(1), "bb", "a", 0);
		Assert.That(Counters(index), Is.EqualTo((2UL, 2UL)));

		index.Remove(1, Hash.GetHashCode(1), "a", 0);
		index.Remove(2, Hash.GetHashCode(2), "bb", 0);
		Assert.That(Counters(index), Is.EqualTo((0UL, 0UL)), "a bucket created by a move must be counted when it goes");
	}

	[Test]
	public void Update_WithinTheSameBucket_ChangesNothing() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);
		index.Add(1, Hash.GetHashCode(1), "a", 0);

		index.Update(1, Hash.GetHashCode(1), "a", "b", 0);

		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));
	}

	// A move of which only one half happens: the key is not in the bucket it is said to leave,
	// or is already in the one it enters. The counters follow what was done.

	[Test]
	public void Update_FromABucketThatDoesNotExist_CountsTheNewEdge() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);

		index.Update(1, Hash.GetHashCode(1), "a", "bb", 0);

		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));
	}

	[Test]
	public void Update_FromABucketThatLacksTheKey_CountsTheNewEdge() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);
		index.Add(2, Hash.GetHashCode(2), "a", 0);

		// Bucket 1 holds key 2 only: nothing leaves it, key 1 enters a new bucket 2.
		index.Update(1, Hash.GetHashCode(1), "a", "bb", 0);

		Assert.That(Counters(index), Is.EqualTo((2UL, 2UL)));
	}

	[Test]
	public void Update_IntoABucketThatAlreadyHoldsTheKey_CountsTheRemovedEdge() {
		var index = new CacheKeyValueListIndex<long, string, long>(static (_, v) => v.Length);
		index.Add(1, Hash.GetHashCode(1), "a", 0);
		index.Add(1, Hash.GetHashCode(1), "bb", 0);
		Assert.That(Counters(index), Is.EqualTo((2UL, 2UL)));

		// Key 1 is already in bucket 2: nothing enters it, key 1 leaves bucket 1, which goes.
		index.Update(1, Hash.GetHashCode(1), "a", "bb", 0);

		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));
	}

	[Test]
	public void CollectionIndex_CountsOneLinkPerDistinctElement() {
		var index = new CacheKeyValueListIndex<long, long[], long>(static (_, elements) => elements);

		// 20 appears twice in the first owner's collection: still one link.
		index.Add(1, Hash.GetHashCode(1), [10, 20, 20], 0);
		index.Add(2, Hash.GetHashCode(2), [20], 0);
		Assert.That(Counters(index), Is.EqualTo((2UL, 3UL)), "elements 10 and 20; links 1-10, 1-20, 2-20");

		index.Remove(1, Hash.GetHashCode(1), [10, 20, 20], 0);
		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));

		index.Remove(2, Hash.GetHashCode(2), [20], 0);
		Assert.That(Counters(index), Is.EqualTo((0UL, 0UL)));
	}

	[Test]
	public void SymmetricCollectionIndex_ReportsItsForwardHalf() {
		var index = new CacheCollectionSymmetricKeyValueListIndex<long, long[], long>(static (_, elements) => elements);

		// Forward maps an element to its owners, Reverse an owner to its elements: one owner
		// with three elements is three keys in Forward and one key in Reverse.
		index.Add(1, Hash.GetHashCode(1), [10, 20, 30], 0);
		Assert.That(Counters(index), Is.EqualTo((3UL, 3UL)));

		index.Add(2, Hash.GetHashCode(2), [20], 0);
		Assert.That(Counters(index), Is.EqualTo((3UL, 4UL)), "a link counts once, not once per half");

		index.Remove(1, Hash.GetHashCode(1), [10, 20, 30], 0);
		Assert.That(Counters(index), Is.EqualTo((1UL, 1UL)));
	}
}

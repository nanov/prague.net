namespace Prague.Core.Tests.DataStructures;

	using System.Buffers;
	using Prague.Core.Collections;
	using NUnit.Framework;

/// <summary>
///   Lifetime guarantees of PooledSet under the single-writer / lock-free-reader model.
///   Suspected bug (upstream PR #56): backing arrays are ArrayPool-rented and returned on
///   Dispose/Grow while interface enumerators (what CacheKeyValueListIndex.GetValues hands
///   to callers) hold them with no refcount — the next renter overwrites the array under a
///   running enumeration, serving foreign keys. ArrayPool.Shared returns to a thread-local
///   slot, so the very next same-size Rent on the same thread receives the enumerator's
///   live array — deterministic, single-threaded.
/// </summary>
[TestFixture]
public class PooledSetLifetimeTests {
	[Test]
	public void BoxedEnumerator_DisposeMidIteration_KeepsServingOriginalKeys() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		set.Add(11);
		set.Add(22);
		set.Add(33);
		set.GetSnapshot(out var liveSlots, out _, out _);

		// Exactly what app code gets from CacheKeyValueListIndex.GetValues(key)
		IEnumerable<long> view = set;
		using var enumerator = view.GetEnumerator();
		Assert.That(enumerator.MoveNext(), Is.True);
		var seen = new List<long> { enumerator.Current };

		// Writer side: the bucket empties -> RemoveUnderKey disposes the set
		set.Dispose();

		// The live array must NOT be obtainable from the shared pool by anyone
		var rented = ArrayPool<HashSlot<long>>.Shared.Rent(liveSlots.Length);
		Assert.That(ReferenceEquals(rented, liveSlots), Is.False,
			"Dispose must not hand the enumerator's live array to the ArrayPool");
		ArrayPool<HashSlot<long>>.Shared.Return(rented);

		// The in-flight enumeration keeps yielding the original snapshot
		while (enumerator.MoveNext()) {
			seen.Add(enumerator.Current);
		}

		Assert.That(seen, Is.EquivalentTo(new long[] { 11, 22, 33 }));
	}

	[Test]
	public void StructEnumerator_GrowMidIteration_KeepsServingCapturedSnapshot() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		for (long i = 0; i < 100; i++) {
			set.Add(i);
		}

		set.GetSnapshot(out var slotsBeforeGrow, out _, out _);

		var seen = new List<long>();
		var enumerator = set.GetEnumerator();
		try {
			Assert.That(enumerator.MoveNext(), Is.True);
			seen.Add(enumerator.Current);

			// Writer grows the set past the default first-generation capacity mid-enumeration
			for (long i = 100; i < 400; i++) {
				set.Add(i);
			}

			set.GetSnapshot(out var slotsAfterGrow, out _, out _);
			Assert.That(ReferenceEquals(slotsBeforeGrow, slotsAfterGrow), Is.False, "Grow must swap tables");

			// The captured snapshot still serves the original 100 keys, untouched
			while (enumerator.MoveNext()) {
				seen.Add(enumerator.Current);
			}
		}
		finally {
			enumerator.Dispose();
		}

		Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0, 100).Select(i => (long)i)));

		// A fresh enumeration sees everything, and lookups work after the grow
		Assert.That(set.Count, Is.EqualTo(400));
		var all = new List<long>();
		foreach (var v in set) {
			all.Add(v);
		}

		Assert.That(all, Is.EquivalentTo(Enumerable.Range(0, 400).Select(i => (long)i)));
		for (long i = 0; i < 400; i++) {
			Assert.That(set.Contains(i), Is.True, $"key {i} lost after grow");
		}
	}

	[Test]
	public void RemoveInsideForeach_OverInterfaceView_DoesNotCorrupt() {
		// The reader-cleanup pattern: enumerate the GetValues result and remove
		// every yielded key from the same bucket. The last removal disposes the
		// set and recycles its arrays under the running foreach.
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		for (long i = 0; i < 50; i++) {
			set.Add(i);
		}

		IEnumerable<long> view = set;
		var removed = new List<long>();
		foreach (var key in view) {
			Assert.That(set.Remove(key), Is.True);
			removed.Add(key);
			if (set.Count == 0) {
				set.Dispose(); // what RemoveUnderKey does when the bucket empties
			}
		}

		Assert.That(removed, Is.EquivalentTo(Enumerable.Range(0, 50).Select(i => (long)i)));
		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void ReadsAfterDispose_LandOnSentinel_SafeAndEmpty() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		set.Add(11);
		set.Add(22);
		set.Remove(11);
		set.Remove(22);
		set.Dispose();
		set.Dispose(); // idempotent

		// Scoped readers on a disposed set must be safe (sentinel generation) and see
		// nothing — never the retired arrays.
		Assert.That(set.Contains(11), Is.False);

		var seen = 0;
		foreach (var _ in set)
			seen++;
		Assert.That(seen, Is.EqualTo(0), "struct enumerator on a disposed set");

		IEnumerable<long> view = set;
		foreach (var _ in view)
			seen++;
		Assert.That(seen, Is.EqualTo(0), "boxed enumerator on a disposed set");
	}

	[Test]
	public void AddRemoveChurn_FreelistReuse_StaysConsistent() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		for (var round = 0; round < 20; round++) {
			for (long i = 0; i < 200; i++) {
				Assert.That(set.Add(round * 1000 + i), Is.True);
			}

			for (long i = 0; i < 200; i++) {
				Assert.That(set.Remove(round * 1000 + i), Is.True);
			}

			Assert.That(set.Count, Is.EqualTo(0));
		}

		set.Add(42);
		Assert.That(set.Contains(42), Is.True);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void BoxedEnumerator_DisposeMidIteration_NextRenterCannotOverwriteLiveView() {
		// The deterministic corruption PoC from the upstream incident: after Dispose
		// returns the arrays, the very next same-size renter on this thread receives
		// the enumerator's live array and overwrites it under the running enumeration.
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		set.Add(11);
		set.Add(22);
		set.Add(33);
		set.GetSnapshot(out var liveSlots, out _, out _);

		IEnumerable<long> view = set;
		using var enumerator = view.GetEnumerator();
		Assert.That(enumerator.MoveNext(), Is.True);
		var seen = new List<long> { enumerator.Current };

		set.Dispose();

		// Simulate the next renter (another PooledSet, any code using the shared pool)
		var rented = ArrayPool<HashSlot<long>>.Shared.Rent(liveSlots.Length);
		for (var i = 0; i < rented.Length; i++) {
			rented[i] = new HashSlot<long> { HashCode = 1, Next = -1, Value = 9999 };
		}

		while (enumerator.MoveNext()) {
			seen.Add(enumerator.Current);
		}

		ArrayPool<HashSlot<long>>.Shared.Return(rented, clearArray: true);

		Assert.That(seen, Has.No.Member(9999L),
			"enumeration served foreign data written by the next pool renter");
		Assert.That(seen, Is.EquivalentTo(new long[] { 11, 22, 33 }));
	}
}

namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;

// PooledSet.TryGetSlot: the slot an item occupies is the position the ref-struct enumerator yields it
// at, so sorting a subset by slot reproduces the set's enumeration order (the pipeline's
// order-preserving small-probe seed). Absent items and disposed sets report false.
[TestFixture]
[NonParallelizable]
public class PooledSetTryGetSlotTests {
	[Test]
	public void FreshSet_SlotsFollowEnumerationOrder() {
		using var set = new PooledSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < 500; i++)
			set.Add(i * 7);
		var expected = 0;
		foreach (var item in set) {
			Assert.That(set.TryGetSlot(item, out var slot), Is.True, "item " + item);
			Assert.That(slot, Is.EqualTo(expected), "slot of item " + item);
			expected++;
		}

		Assert.That(expected, Is.EqualTo(500));
	}

	[Test]
	public void AfterRemovesAndReAdds_SlotsStillFollowEnumerationOrder() {
		using var set = new PooledSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < 300; i++)
			set.Add(i);
		for (var i = 0; i < 300; i += 3)
			set.Remove(i);
		for (var i = 1000; i < 1050; i++)
			set.Add(i); // reuses freed slots, then appends

		var previous = -1;
		var seen = 0;
		foreach (var item in set) {
			Assert.That(set.TryGetSlot(item, out var slot), Is.True);
			Assert.That(slot, Is.GreaterThan(previous), "enumeration is slot order");
			previous = slot;
			seen++;
		}

		Assert.That(seen, Is.EqualTo(set.Count));
		Assert.That(set.TryGetSlot(3, out var absent), Is.False);
		Assert.That(absent, Is.EqualTo(-1));
	}

	[Test]
	public void AbsentAndDisposed_ReturnFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		set.Add(5);
		Assert.That(set.TryGetSlot(6, out _), Is.False);
		Assert.That(set.TryGetSlot(5, out var slot), Is.True);
		Assert.That(slot, Is.EqualTo(0));
		set.Dispose();
		Assert.That(set.TryGetSlot(5, out var afterDispose), Is.False);
		Assert.That(afterDispose, Is.EqualTo(-1));
		Assert.That(PooledSet<int, DefaultKeyComparer<int>>.Empty.TryGetSlot(5, out _), Is.False);
	}

	// Single writer churning the set through grows, removes and slot reuse against lock-free
	// TryGetSlot readers: the readers must never throw and a reported slot must be a valid index of
	// some generation (stale hits and misses are the documented model).
	[Test]
	public void ConcurrentWriter_TryGetSlot_NeverThrows_AndReportsValidSlots() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		const int items = 2048;
		for (var i = 0; i < items; i++) set.Add(i);
		var stop = false;
		Exception? failure = null;
		var writer = new Thread(() => {
			try {
				var round = 0;
				while (!Volatile.Read(ref stop)) {
					for (var i = 0; i < items; i += 2) set.Remove(i + (round & 1));
					for (var i = 0; i < items; i += 2) set.Add(i + (round & 1));
					if (round % 8 == 0)
						for (var i = items; i < items + 4096; i++) set.Add(i); // force a grow
					if (round % 8 == 4)
						for (var i = items; i < items + 4096; i++) set.Remove(i);
					round++;
				}
			} catch (Exception ex) {
				failure = ex;
			}
		});
		var readers = new Thread[4];
		var hits = 0L;
		for (var r = 0; r < readers.Length; r++)
			readers[r] = new Thread(() => {
				try {
					var local = 0L;
					var seed = Environment.CurrentManagedThreadId;
					while (!Volatile.Read(ref stop)) {
						for (var i = 0; i < items + 4096; i++) {
							if (!set.TryGetSlot((i * 31 + seed) % (items + 4096), out var slot))
								continue;
							if (slot < 0 || slot >= 1 << 20)
								throw new InvalidOperationException("slot out of range: " + slot);
							local++;
						}
					}

					Interlocked.Add(ref hits, local);
				} catch (Exception ex) {
					failure = ex;
				}
			});
		writer.Start();
		foreach (var t in readers) t.Start();
		Thread.Sleep(1000);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers) t.Join();
		set.Dispose();
		Assert.That(failure, Is.Null);
		Assert.That(hits, Is.GreaterThan(0));
	}
}

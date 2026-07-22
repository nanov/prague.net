namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using Prague.Core.Tests.Infrastructure;
	using NUnit.Framework;

/// <summary>
///   Lazy table initialization: constructing a PooledSet must rent nothing (index
///   buckets are created per distinct index key, and a set may well see no Add at
///   all), the first Add rents the first generation through Grow's cold path, and
///   the shared unallocated sentinel must survive any single set's lifecycle.
/// </summary>
[TestFixture]
public class PooledSetLazyInitTests {
	[Test]
	public void Constructor_RentsNothing_FreshSetsShareTheUnallocatedSentinel() {
		var a = new PooledSet<long, DefaultKeyComparer<long>>();
		var b = new PooledSet<long, DefaultKeyComparer<long>>();

		a.GetSnapshot(out var slotsA, out _, out _);
		b.GetSnapshot(out var slotsB, out _, out _);

		Assert.That(ReferenceEquals(slotsA, slotsB), Is.True,
			"fresh sets must share one process-wide sentinel generation instead of renting per set");
	}

	[Test]
	public void FreshSet_ReadsAsEmpty_AllOpsSafe() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.Contains(42), Is.False);
		Assert.That(set.Remove(42), Is.False);

		var seen = 0;
		foreach (var _ in set)
			seen++;
		IEnumerable<long> view = set;
		foreach (var _ in view)
			seen++;
		Assert.That(seen, Is.EqualTo(0), "enumerators over the unallocated sentinel must yield nothing");
	}

	[Test]
	public void FirstAdd_RentsTheDefaultGeneration_StableUntilFull() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		set.GetSnapshot(out var sentinelSlots, out _, out _);

		Assert.That(set.Add(1), Is.True);
		set.GetSnapshot(out var firstGenSlots, out _, out _);
		Assert.That(ReferenceEquals(sentinelSlots, firstGenSlots), Is.False,
			"first Add must swap the sentinel for a real generation");
		Assert.That(set.CapacitySlots, Is.EqualTo(107),
			"unhinted sets must use the default table geometry (a table prime that still fits the historic 128-slot pooled array)");

		// The default generation serves up to its full capacity without another swap.
		for (long i = 2; i <= 107; i++) {
			Assert.That(set.Add(i), Is.True);
		}

		set.GetSnapshot(out var fullSlots, out _, out _);
		Assert.That(ReferenceEquals(firstGenSlots, fullSlots), Is.True,
			"the default generation must hold its full capacity without a swap");

		Assert.That(set.Add(108), Is.True); // exceeds the first generation -> grow
		set.GetSnapshot(out var grownSlots, out _, out _);
		Assert.That(ReferenceEquals(fullSlots, grownSlots), Is.False, "growth past the first generation must swap tables");

		Assert.That(set.Count, Is.EqualTo(108));
		for (long i = 1; i <= 108; i++) {
			Assert.That(set.Contains(i), Is.True, $"key {i} lost across the lazy ladder");
		}
	}

	[Test]
	public void InitialCapacityHint_ShrinksTheFirstGeneration_ForSmallBuckets() {
		// The per-entity bucket case (a handful of values per index key): the hint
		// replaces the default first table with a prime-rounded small one.
		var set = new PooledSet<long, DefaultKeyComparer<long>>(default, initialCapacity: 8);
		Assert.That(set.CapacitySlots, Is.EqualTo(0), "still lazy: nothing rented before the first Add");

		set.Add(1);
		Assert.That(set.CapacitySlots, Is.GreaterThanOrEqualTo(8));
		Assert.That(set.CapacitySlots, Is.LessThan(107),
			"the hint must shrink the first generation below the default");

		for (long i = 2; i <= 300; i++) {
			set.Add(i);
		}

		Assert.That(set.Count, Is.EqualTo(300));
		Assert.That(set.CapacitySlots, Is.GreaterThanOrEqualTo(300), "hinted sets still grow on demand");
		set.Dispose();
	}

	[Test]
	public void DisposeOfNeverUsedSet_DoesNotRetireTheSharedSentinel() {
		var doomed = new PooledSet<long, DefaultKeyComparer<long>>();
		doomed.Dispose();
		doomed.Dispose(); // idempotent

		// Post-dispose reads stay safe and empty.
		Assert.That(doomed.Contains(1), Is.False);
		var seen = 0;
		foreach (var _ in doomed)
			seen++;
		Assert.That(seen, Is.EqualTo(0));

		// Other fresh sets still live on the sentinel and must remain fully functional.
		var survivor = new PooledSet<long, DefaultKeyComparer<long>>();
		Assert.That(survivor.Contains(1), Is.False);
		Assert.That(survivor.Add(1), Is.True);
		Assert.That(survivor.Contains(1), Is.True);
		survivor.Dispose();
	}

	[Test]
	public void LazyLifecycle_GrowLadderAndDispose_LeaveNoOutstandingArrays() {
		LeakAssert.Balanced(() => {
			// Never-used set: ctor + Dispose must not touch the pool at all.
			var empty = new PooledSet<long, DefaultKeyComparer<long>>();
			empty.Dispose();

			// Ladder lifecycle: every intermediate generation rented by the lazy
			// growth path must come back to the pool after Dispose.
			var set = new PooledSet<long, DefaultKeyComparer<long>>();
			for (long i = 0; i < 500; i++) {
				set.Add(i);
			}

			var seen = 0;
			foreach (var _ in set)
				seen++;
			Assert.That(seen, Is.EqualTo(500));
			set.Dispose();
		});
	}

	[Test]
	public void LazyLadder_MultiWordKeys_VersionGuardedPathStaysConsistent() {
		// (long, long) is a multi-word key: AtomicCopy == false, so the sentinel and
		// every ladder generation carry the Versions array — cover that shape too.
		var set = new PooledSet<(long, long), DefaultKeyComparer<(long, long)>>();
		Assert.That(set.Contains((1, 1)), Is.False);

		for (long i = 0; i < 100; i++) {
			Assert.That(set.Add((i, i)), Is.True);
		}

		Assert.That(set.Count, Is.EqualTo(100));
		for (long i = 0; i < 100; i++) {
			Assert.That(set.Contains((i, i)), Is.True);
		}

		set.Dispose();
	}
}

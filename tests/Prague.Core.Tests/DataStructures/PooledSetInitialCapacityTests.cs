namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using Prague.Core.Tests.Infrastructure;
	using NUnit.Framework;

/// <summary>
///   First-generation sizing: construction eagerly rents the first table (the default
///   capacity, or an explicit hint rounded up to a prime), the generation is stable
///   until full, and growth/dispose keep the pool balanced.
/// </summary>
[TestFixture]
public class PooledSetInitialCapacityTests {
	[Test]
	public void Constructor_RentsTheDefaultFirstGeneration_Eagerly() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();

		Assert.That(set.CapacitySlots, Is.EqualTo(107),
			"unhinted sets must rent the default table geometry up front (a table prime that still fits the historic 128-slot pooled array)");
		set.Dispose();
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
		Assert.That(seen, Is.EqualTo(0), "enumerators over an empty set must yield nothing");
		set.Dispose();
	}

	[Test]
	public void DefaultGeneration_StableUntilFull_ThenGrows() {
		var set = new PooledSet<long, DefaultKeyComparer<long>>();
		set.GetSnapshot(out var firstGenSlots, out _, out _);

		// The first generation serves up to its full capacity without a swap.
		for (long i = 1; i <= 107; i++) {
			Assert.That(set.Add(i), Is.True);
		}

		set.GetSnapshot(out var fullSlots, out _, out _);
		Assert.That(ReferenceEquals(firstGenSlots, fullSlots), Is.True,
			"the first generation must hold its full capacity without a swap");

		Assert.That(set.Add(108), Is.True); // exceeds the first generation -> grow
		set.GetSnapshot(out var grownSlots, out _, out _);
		Assert.That(ReferenceEquals(fullSlots, grownSlots), Is.False, "growth past the first generation must swap tables");

		Assert.That(set.Count, Is.EqualTo(108));
		for (long i = 1; i <= 108; i++) {
			Assert.That(set.Contains(i), Is.True, $"key {i} lost across the growth ladder");
		}

		set.Dispose();
	}

	[Test]
	public void InitialCapacityHint_ShrinksTheFirstGeneration_ForSmallBuckets() {
		// The per-entity bucket case (a handful of values per index key): the hint
		// replaces the default first table with a prime-rounded small one, rented
		// eagerly at construction.
		var set = new PooledSet<long, DefaultKeyComparer<long>>(default, initialCapacity: 8);
		Assert.That(set.CapacitySlots, Is.GreaterThanOrEqualTo(8),
			"the hinted first generation must be rented up front");
		Assert.That(set.CapacitySlots, Is.LessThan(107),
			"the hint must shrink the first generation below the default");

		for (long i = 1; i <= 300; i++) {
			set.Add(i);
		}

		Assert.That(set.Count, Is.EqualTo(300));
		Assert.That(set.CapacitySlots, Is.GreaterThanOrEqualTo(300), "hinted sets still grow on demand");
		set.Dispose();
	}

	[Test]
	public void Dispose_IsIdempotent_AndLeavesOtherSetsIntact() {
		var doomed = new PooledSet<long, DefaultKeyComparer<long>>();
		doomed.Dispose();
		doomed.Dispose(); // idempotent
		Assert.That(doomed.CapacitySlots, Is.EqualTo(0), "a disposed set reports no rented slots");

		// Post-dispose reads stay safe and empty.
		Assert.That(doomed.Contains(1), Is.False);
		var seen = 0;
		foreach (var _ in doomed)
			seen++;
		Assert.That(seen, Is.EqualTo(0));

		// Other sets are unaffected and remain fully functional.
		var survivor = new PooledSet<long, DefaultKeyComparer<long>>();
		Assert.That(survivor.Contains(1), Is.False);
		Assert.That(survivor.Add(1), Is.True);
		Assert.That(survivor.Contains(1), Is.True);
		survivor.Dispose();
	}

	[Test]
	public void Lifecycle_GrowLadderAndDispose_LeaveNoOutstandingArrays() {
		LeakAssert.Balanced(() => {
			// Never-used set: the eagerly rented first generation must come back to
			// the pool on Dispose.
			var empty = new PooledSet<long, DefaultKeyComparer<long>>();
			empty.Dispose();

			// Hinted set: same round-trip for a non-default first generation.
			var hinted = new PooledSet<long, DefaultKeyComparer<long>>(default, initialCapacity: 8);
			hinted.Dispose();

			// Ladder lifecycle: every intermediate generation rented by the growth
			// path must come back to the pool after Dispose.
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
	public void GrowLadder_MultiWordKeys_VersionGuardedPathStaysConsistent() {
		// (long, long) is a multi-word key: AtomicCopy == false, so every generation
		// carries the Versions array — cover that shape too.
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

namespace Prague.Core.Tests.DataStructures;

using NUnit.Framework;
using Prague.Core.Collections;
using Prague.Core.Tests.Infrastructure;

/// <summary>
///   A generation with reference keys must go back to the pool holding no reference, and it gets
///   there by clearing only <c>[0, LastIndex)</c>: a slot at or past <c>LastIndex</c> was either
///   never written or cleared when it was freed. Clearing the whole array instead costs O(length)
///   under the reclamation gate's lock, mostly over slots that are already empty.
///   Each test keeps the array it captured and reads it after the set returned it.
/// </summary>
[TestFixture]
[NonParallelizable] // reads arrays that are back in the pool: nothing else may rent meanwhile
public class PooledSetReclaimClearTests {
	private const int DeadSlot = -1;

	private static string Key(int i) => $"key-{i:D4}";

	private static PooledSet<string, DefaultKeyComparer<string>> Filled(int count) {
		var set = new PooledSet<string, DefaultKeyComparer<string>>();
		for (var i = 1; i <= count; i++) {
			set.Add(Key(i));
		}

		return set;
	}

	private static void AssertHoldsNoReference(HashSlot<string>[] slots) {
		for (var i = 0; i < slots.Length; i++) {
			Assert.That(slots[i].Value, Is.Null, $"slot {i} of a returned array still holds a key");
		}
	}

	[Test]
	public void EmptiedSet_HasNothingLeftToClear_AndIsNotRewritten() {
		var set = Filled(40);
		set.GetSnapshot(out var slots, out _, out _);
		for (var i = 1; i <= 40; i++) {
			set.Remove(Key(i));
		}

		set.Dispose();
		LeakAssert.Quiesce();

		AssertHoldsNoReference(slots);
		Assert.That(slots[0].HashCode, Is.EqualTo(DeadSlot),
			"every slot was cleared when its key was removed: the reclaim must not clear the array again");
	}

	[Test]
	public void NonEmptySet_Disposed_LeavesNoReference() {
		var set = Filled(40);
		set.GetSnapshot(out var slots, out _, out _);

		set.Dispose();
		LeakAssert.Quiesce();

		AssertHoldsNoReference(slots);
	}

	[Test]
	public void PartlyDrainedSet_Disposed_LeavesNoReference() {
		// Survivors scattered over the whole written range, holes between them.
		var set = Filled(1000);
		set.GetSnapshot(out var slots, out _, out _);
		for (var i = 1; i <= 1000; i++) {
			if (i % 100 != 0) {
				set.Remove(Key(i));
			}
		}

		set.Dispose();
		LeakAssert.Quiesce();

		AssertHoldsNoReference(slots);
	}

	[Test]
	public void GrownGeneration_LeavesNoReference() {
		var set = Filled(59);
		set.GetSnapshot(out var firstGeneration, out _, out _);

		set.Add(Key(60)); // retires the full first generation
		LeakAssert.Quiesce();

		AssertHoldsNoReference(firstGeneration);
		set.Dispose();
	}

	[Test]
	public void EmptiedThenRefilledSet_LeavesNoReference() {
		// LastIndex falls back to zero when the set empties and climbs again from there: the
		// slots above the second, lower peak were cleared one by one during the first drain.
		var set = Filled(50);
		set.GetSnapshot(out var slots, out _, out _);
		for (var i = 1; i <= 50; i++) {
			set.Remove(Key(i));
		}

		for (var i = 101; i <= 105; i++) {
			set.Add(Key(i));
		}

		set.Dispose();
		LeakAssert.Quiesce();

		AssertHoldsNoReference(slots);
	}

	[Test]
	public void MultiWordKeysWithAReference_EmptiedSet_IsNotRewritten() {
		// (string, long) is cleared on free like a plain reference key, and takes the
		// version-guarded reader path.
		var set = new PooledSet<(string, long), DefaultKeyComparer<(string, long)>>();
		for (var i = 1; i <= 40; i++) {
			set.Add((Key(i), i));
		}

		set.GetSnapshot(out var slots, out _, out _);
		for (var i = 1; i <= 40; i++) {
			set.Remove((Key(i), i));
		}

		set.Dispose();
		LeakAssert.Quiesce();

		for (var i = 0; i < slots.Length; i++) {
			Assert.That(slots[i].Value.Item1, Is.Null, $"slot {i} of a returned array still holds a key");
		}

		Assert.That(slots[0].HashCode, Is.EqualTo(DeadSlot));
	}
}

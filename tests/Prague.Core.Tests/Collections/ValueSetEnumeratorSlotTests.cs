namespace Prague.Core.Tests.Collections;

using System.Collections.Generic;
using Prague.Core.Collections;
using NUnit.Framework;

// ── ValueSet.Enumerator.CurrentSlot ─────────────────────────────────────────
// The JoinMany fan-out keys its right → extra-lefts chains by the slot AddOrFind reported and reads
// them back by the slot the store walk reports while enumerating the pair set. The two numberings
// must agree across growth out of inline storage and across the holes in-place removals leave.

[TestFixture]
public class ValueSetEnumeratorSlotTests {
	[Test]
	public void CurrentSlot_MatchesAddOrFindSlot_WithinInlineStorage() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>(4);
		var slots = new Dictionary<int, int>();
		try {
			for (var i = 0; i < 20; i++) {
				Assert.That(set.AddOrFind(i, out var slot), Is.True);
				slots[i] = slot;
			}

			AssertSlotsAgree(ref set, slots);
		} finally {
			set.Dispose();
		}
	}

	[Test]
	public void CurrentSlot_MatchesAddOrFindSlot_AfterGrowthAndRemovals() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>(4);
		var slots = new Dictionary<int, int>();
		try {
			for (var i = 0; i < 300; i++) {
				Assert.That(set.AddOrFind(i, out var slot), Is.True);
				slots[i] = slot;
			}

			for (var i = 0; i < 300; i += 3) {
				Assert.That(set.Remove(i), Is.True);
				slots.Remove(i);
			}

			Assert.That(set.AddOrFind(7, out var found), Is.False, "a survivor is found, not re-added");
			Assert.That(found, Is.EqualTo(slots[7]), "and reports the slot it has always had");

			AssertSlotsAgree(ref set, slots);
		} finally {
			set.Dispose();
		}
	}

	[Test]
	public void CurrentSlot_IsZeroBasedAndDenseWhileOnlyAdding() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>(4);
		try {
			for (var i = 0; i < 100; i++) {
				set.AddOrFind(1000 + i, out var slot);
				Assert.That(slot, Is.EqualTo(i), "slots are handed out in order while nothing is removed");
			}

			var expected = 0;
			var e = set.GetEnumerator();
			while (e.MoveNext()) {
				Assert.That(e.CurrentSlot, Is.EqualTo(expected));
				Assert.That(e.Current, Is.EqualTo(1000 + expected));
				expected++;
			}

			Assert.That(expected, Is.EqualTo(100));
		} finally {
			set.Dispose();
		}
	}

	private static void AssertSlotsAgree(ref ValueSet<int, DefaultKeyComparer<int>> set, Dictionary<int, int> slots) {
		var seen = 0;
		var e = set.GetEnumerator();
		while (e.MoveNext()) {
			Assert.That(e.CurrentSlot, Is.EqualTo(slots[e.Current]), $"item {e.Current}");
			seen++;
		}

		Assert.That(seen, Is.EqualTo(set.Count), "every live slot is enumerated exactly once");
		Assert.That(seen, Is.EqualTo(slots.Count));
	}
}

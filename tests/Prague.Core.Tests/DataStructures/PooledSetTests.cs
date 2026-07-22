namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class PooledSetTests {
	// --- Construction ---

	[Test]
	public void Default_Constructor_CreatesEmptySet() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsEmpty, Is.True);
	}

	// --- Add ---

	[Test]
	public void Add_SingleItem_ReturnsTrue() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Add(5), Is.True);
		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.IsEmpty, Is.False);
	}

	[Test]
	public void Add_Duplicate_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Add(5), Is.True);
		Assert.That(set.Add(5), Is.False);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleItems_AllPresent() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(10), Is.True);
		Assert.That(set.Contains(20), Is.True);
		Assert.That(set.Contains(30), Is.True);
	}

	[Test]
	public void Add_ManyItems_AllPresent() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 500; i++)
			Assert.That(set.Add(i), Is.True);

		Assert.That(set.Count, Is.EqualTo(500));

		for (var i = 0; i < 500; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	[Test]
	public void Add_AllDuplicates_CountStaysOne() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(42);
		for (var i = 0; i < 100; i++)
			Assert.That(set.Add(42), Is.False);

		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_NegativeValues_Works() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(0);
		set.Add(-5);
		set.Add(5);
		set.Add(-10);
		set.Add(10);

		Assert.That(set.Count, Is.EqualTo(5));
		Assert.That(set.Contains(-10), Is.True);
		Assert.That(set.Contains(-5), Is.True);
		Assert.That(set.Contains(0), Is.True);
		Assert.That(set.Contains(5), Is.True);
		Assert.That(set.Contains(10), Is.True);
	}

	[Test]
	public void Add_TriggersGrow_AllPresent() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		// Adding 200 exceeds the default first-generation capacity and must trigger grow
		for (var i = 0; i < 200; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(200));
		for (var i = 0; i < 200; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	// --- Remove ---

	[Test]
	public void Remove_ExistingItem_ReturnsTrue() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		Assert.That(set.Remove(20), Is.True);
		Assert.That(set.Count, Is.EqualTo(2));
	}

	[Test]
	public void Remove_NonExistingItem_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(30);

		Assert.That(set.Remove(20), Is.False);
		Assert.That(set.Count, Is.EqualTo(2));
	}

	[Test]
	public void Remove_FromEmpty_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Remove(5), Is.False);
	}

	[Test]
	public void Remove_ItemNoLongerContained() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(20);

		Assert.That(set.Contains(20), Is.False);
		Assert.That(set.Contains(10), Is.True);
		Assert.That(set.Contains(30), Is.True);
	}

	[Test]
	public void Remove_OnlyElement_BecomesEmpty() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(42);
		set.Remove(42);

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsEmpty, Is.True);
	}

	[Test]
	public void Remove_AllElements_BecomesEmpty() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		set.Remove(20);
		set.Remove(10);
		set.Remove(30);

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsEmpty, Is.True);
	}

	[Test]
	public void Remove_ThenAdd_ReusesFreeSlots() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		set.Remove(20);
		set.Add(25);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(10), Is.True);
		Assert.That(set.Contains(25), Is.True);
		Assert.That(set.Contains(30), Is.True);
		Assert.That(set.Contains(20), Is.False);
	}

	[Test]
	public void Remove_SameItemTwice_SecondReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		Assert.That(set.Remove(10), Is.True);
		Assert.That(set.Remove(10), Is.False);
	}

	// --- Contains ---

	[Test]
	public void Contains_ExistingItem_ReturnsTrue() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		Assert.That(set.Contains(20), Is.True);
	}

	[Test]
	public void Contains_NonExistingItem_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(30);

		Assert.That(set.Contains(20), Is.False);
	}

	[Test]
	public void Contains_EmptySet_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Contains(5), Is.False);
	}

	[Test]
	public void Contains_AfterRemove_ReturnsFalse() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Remove(10);

		Assert.That(set.Contains(10), Is.False);
	}

	// --- Enumeration ---

	[Test]
	public void GetEnumerator_EmptySet_NoElements() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		var count = 0;

		foreach (var _ in set)
			count++;

		Assert.That(count, Is.EqualTo(0));
	}

	[Test]
	public void GetEnumerator_ReturnsAllElements() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var items = new HashSet<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Is.EquivalentTo(new[] { 10, 20, 30 }));
	}

	[Test]
	public void GetEnumerator_AfterRemove_SkipsRemoved() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(20);

		var items = new HashSet<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Is.EquivalentTo(new[] { 10, 30 }));
	}

	[Test]
	public void GetEnumerator_Dispose_DoesNotThrow() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(1);
		set.Add(2);

		var enumerator = set.GetEnumerator();
		while (enumerator.MoveNext()) { }
		enumerator.Dispose();
	}

	[Test]
	public void IEnumerable_GetEnumerator_Works() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var items = ((IEnumerable<int>)set).ToHashSet();
		Assert.That(items, Is.EquivalentTo(new[] { 10, 20, 30 }));
	}

	// --- IReadOnlyCollection ---

	[Test]
	public void IReadOnlyCollection_Count_ReturnsCorrectValue() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(1);
		set.Add(2);
		set.Add(3);

		IReadOnlyCollection<int> roc = set;
		Assert.That(roc.Count, Is.EqualTo(3));
	}

	// --- Mixed operations ---

	[Test]
	public void InterleavedAddRemove_CorrectState() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(50);
		set.Add(30);
		set.Add(70);
		set.Remove(50);
		set.Add(40);
		set.Add(60);
		set.Remove(30);
		set.Add(35);

		Assert.That(set.Count, Is.EqualTo(4));
		Assert.That(set.Contains(35), Is.True);
		Assert.That(set.Contains(40), Is.True);
		Assert.That(set.Contains(60), Is.True);
		Assert.That(set.Contains(70), Is.True);
		Assert.That(set.Contains(50), Is.False);
		Assert.That(set.Contains(30), Is.False);
	}

	[Test]
	public void AddRemoveAdd_SameItem_Works() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Add(10), Is.True);
		Assert.That(set.Remove(10), Is.True);
		Assert.That(set.Add(10), Is.True);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(10), Is.True);
	}

	[Test]
	public void LargeSet_AddRemove_Consistent() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		var reference = new HashSet<int>();
		var rng = new Random(42);

		// Add 1000 random items
		for (var i = 0; i < 1000; i++) {
			var val = rng.Next(500);
			set.Add(val);
			reference.Add(val);
		}

		Assert.That(set.Count, Is.EqualTo(reference.Count));

		// Remove half
		var toRemove = reference.Take(reference.Count / 2).ToList();
		foreach (var val in toRemove) {
			set.Remove(val);
			reference.Remove(val);
		}

		Assert.That(set.Count, Is.EqualTo(reference.Count));

		// Verify all remaining items match
		foreach (var val in reference)
			Assert.That(set.Contains(val), Is.True);

		var enumerated = new HashSet<int>();
		foreach (var item in set)
			enumerated.Add(item);

		Assert.That(enumerated, Is.EquivalentTo(reference));
	}

	// --- String type ---

	[Test]
	public void StringType_Works() {
		var set = new PooledSet<string, DefaultKeyComparer<string>>();

		set.Add("charlie");
		set.Add("alpha");
		set.Add("bravo");

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains("alpha"), Is.True);
		Assert.That(set.Contains("bravo"), Is.True);
		Assert.That(set.Contains("charlie"), Is.True);
	}

	[Test]
	public void StringType_Remove_Works() {
		var set = new PooledSet<string, DefaultKeyComparer<string>>();

		set.Add("alpha");
		set.Add("bravo");
		set.Remove("alpha");

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains("alpha"), Is.False);
		Assert.That(set.Contains("bravo"), Is.True);
	}

	// --- Edge cases ---

	[Test]
	public void Add_IntMinMax_HandledCorrectly() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(int.MaxValue);
		set.Add(int.MinValue);
		set.Add(0);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(int.MinValue), Is.True);
		Assert.That(set.Contains(0), Is.True);
		Assert.That(set.Contains(int.MaxValue), Is.True);
	}

	// --- Pooling ---

	[Test]
	public void Pooling_NoGrow_Works() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 50; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(50));

		var items = new HashSet<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(50));
	}

	[Test]
	public void Pooling_GrowBeyondPooled_StillFunctional() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 200; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(200));

		for (var i = 0; i < 200; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	[Test]
	public void Pooling_EnumeratorDispose_AfterGrow_DoesNotThrow() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 10; i++)
			set.Add(i);

		var enumerator = set.GetEnumerator();

		// Force grow
		for (var i = 10; i < 200; i++)
			set.Add(i);

		while (enumerator.MoveNext()) { }
		enumerator.Dispose();

		Assert.That(set.Count, Is.EqualTo(200));
	}

	[Test]
	public void Dispose_ReturnsPooledArrays() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.Dispose();

		// After dispose, the set should still be readable but pool is returned
		// (no crash)
	}

	[Test]
	public void Pooling_MultipleEnumerators_AllDispose_Safely() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 10; i++)
			set.Add(i);

		var e1 = set.GetEnumerator();
		var e2 = set.GetEnumerator();
		var e3 = set.GetEnumerator();

		while (e1.MoveNext()) { }
		while (e2.MoveNext()) { }
		while (e3.MoveNext()) { }

		e1.Dispose();
		e2.Dispose();
		e3.Dispose();

		set.Add(100);
		Assert.That(set.Count, Is.EqualTo(11));
	}
}

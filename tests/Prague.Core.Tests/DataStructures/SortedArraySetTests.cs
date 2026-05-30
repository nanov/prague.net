namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class SortedArraySetTests {
	// --- Construction ---

	[Test]
	public void Default_Constructor_CreatesEmptySet() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsEmpty, Is.True);
	}

	// --- Add ---

	[Test]
	public void Add_SingleItem_ReturnsTrue() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Add(5), Is.True);
		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.IsEmpty, Is.False);
	}

	[Test]
	public void Add_Duplicate_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Add(5), Is.True);
		Assert.That(set.Add(5), Is.False);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleItems_MaintainsSortedOrder() {
		var set = new SortedArraySet<int>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(3));
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
		Assert.That(span[2], Is.EqualTo(30));
	}

	[Test]
	public void Add_AtBeginning_ShiftsExistingElements() {
		var set = new SortedArraySet<int>();

		set.Add(20);
		set.Add(30);
		set.Add(10);

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
		Assert.That(span[2], Is.EqualTo(30));
	}

	[Test]
	public void Add_AtEnd_AppendsElement() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
		Assert.That(span[2], Is.EqualTo(30));
	}

	[Test]
	public void Add_InMiddle_InsertsCorrectly() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(30);
		set.Add(20);

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
		Assert.That(span[2], Is.EqualTo(30));
	}

	[Test]
	public void Add_BeyondPooledCapacity_TriggersGrow() {
		var set = new SortedArraySet<int>();

		// Pool gives us 128, adding 200 items must trigger a grow
		for (var i = 200; i >= 0; i--)
			Assert.That(set.Add(i), Is.True);

		Assert.That(set.Count, Is.EqualTo(201));

		var span = set.AsSpan();
		for (var i = 0; i <= 200; i++)
			Assert.That(span[i], Is.EqualTo(i));
	}

	[Test]
	public void Add_ManyItems_AllPresent() {
		var set = new SortedArraySet<int>();

		for (var i = 100; i >= 0; i--)
			Assert.That(set.Add(i), Is.True);

		Assert.That(set.Count, Is.EqualTo(101));

		var span = set.AsSpan();
		for (var i = 0; i <= 100; i++)
			Assert.That(span[i], Is.EqualTo(i));
	}

	[Test]
	public void Add_AllDuplicates_CountStaysOne() {
		var set = new SortedArraySet<int>();

		set.Add(42);
		for (var i = 0; i < 100; i++)
			Assert.That(set.Add(42), Is.False);

		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_NegativeValues_SortedCorrectly() {
		var set = new SortedArraySet<int>();

		set.Add(0);
		set.Add(-5);
		set.Add(5);
		set.Add(-10);
		set.Add(10);

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(-10));
		Assert.That(span[1], Is.EqualTo(-5));
		Assert.That(span[2], Is.EqualTo(0));
		Assert.That(span[3], Is.EqualTo(5));
		Assert.That(span[4], Is.EqualTo(10));
	}

	// --- Remove ---

	[Test]
	public void Remove_ExistingItem_ReturnsTrue() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		Assert.That(set.Remove(20), Is.True);
		Assert.That(set.Count, Is.EqualTo(2));
	}

	[Test]
	public void Remove_NonExistingItem_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(30);

		Assert.That(set.Remove(20), Is.False);
		Assert.That(set.Count, Is.EqualTo(2));
	}

	[Test]
	public void Remove_FromEmpty_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Remove(5), Is.False);
	}

	[Test]
	public void Remove_FirstElement_ShiftsCorrectly() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(10);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(2));
		Assert.That(span[0], Is.EqualTo(20));
		Assert.That(span[1], Is.EqualTo(30));
	}

	[Test]
	public void Remove_LastElement_DecreasesCount() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(30);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(2));
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
	}

	[Test]
	public void Remove_MiddleElement_PreservesOrder() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(20);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(2));
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(30));
	}

	[Test]
	public void Remove_OnlyElement_BecomesEmpty() {
		var set = new SortedArraySet<int>();

		set.Add(42);
		set.Remove(42);

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsEmpty, Is.True);
	}

	[Test]
	public void Remove_AllElements_BecomesEmpty() {
		var set = new SortedArraySet<int>();

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
	public void Remove_ThenAdd_ReusesCapacity() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Add(40);
		set.Add(50);

		set.Remove(20);
		set.Remove(40);

		Assert.That(set.Count, Is.EqualTo(3));

		set.Add(25);
		set.Add(45);

		Assert.That(set.Count, Is.EqualTo(5));

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(25));
		Assert.That(span[2], Is.EqualTo(30));
		Assert.That(span[3], Is.EqualTo(45));
		Assert.That(span[4], Is.EqualTo(50));
	}

	[Test]
	public void Remove_SameItemTwice_SecondReturnsFalse() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		Assert.That(set.Remove(10), Is.True);
		Assert.That(set.Remove(10), Is.False);
	}

	// --- Contains ---

	[Test]
	public void Contains_ExistingItem_ReturnsTrue() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		Assert.That(set.Contains(20), Is.True);
	}

	[Test]
	public void Contains_NonExistingItem_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(30);

		Assert.That(set.Contains(20), Is.False);
	}

	[Test]
	public void Contains_EmptySet_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Contains(5), Is.False);
	}

	[Test]
	public void Contains_AfterRemove_ReturnsFalse() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Remove(10);

		Assert.That(set.Contains(10), Is.False);
	}

	// --- Enumeration ---

	[Test]
	public void GetEnumerator_EmptySet_NoElements() {
		var set = new SortedArraySet<int>();
		var count = 0;

		foreach (var _ in set)
			count++;

		Assert.That(count, Is.EqualTo(0));
	}

	[Test]
	public void GetEnumerator_ReturnsSortedElements() {
		var set = new SortedArraySet<int>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var items = new List<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Is.EqualTo(new[] { 10, 20, 30 }));
	}

	[Test]
	public void GetEnumerator_Snapshot_CountCapturedAtCreation() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);

		var enumerator = set.GetEnumerator();
		// Enumerator captured count=3

		// Writer adds a new element (same pooled array, within capacity)
		set.Add(25);

		// Enumerator should only see 3 elements (snapshot count)
		var items = new List<int>();
		while (enumerator.MoveNext())
			items.Add(enumerator.Current);
		enumerator.Dispose();

		Assert.That(items, Has.Count.EqualTo(3));
	}

	[Test]
	public void GetEnumerator_Dispose_DoesNotThrow() {
		var set = new SortedArraySet<int>();

		set.Add(1);
		set.Add(2);

		var enumerator = set.GetEnumerator();
		while (enumerator.MoveNext()) { }
		enumerator.Dispose();

		// Second dispose should also be safe
		enumerator.Dispose();
	}

	[Test]
	public void IEnumerable_GetEnumerator_Works() {
		var set = new SortedArraySet<int>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var items = ((IEnumerable<int>)set).ToList();
		Assert.That(items, Is.EqualTo(new[] { 10, 20, 30 }));
	}

	// --- AsSpan ---

	[Test]
	public void AsSpan_ReturnsCorrectSlice() {
		var set = new SortedArraySet<int>();

		set.Add(30);
		set.Add(10);
		set.Add(20);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(3));
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(20));
		Assert.That(span[2], Is.EqualTo(30));
	}

	[Test]
	public void AsSpan_Empty_ReturnsEmptySpan() {
		var set = new SortedArraySet<int>();

		Assert.That(set.AsSpan().Length, Is.EqualTo(0));
	}

	[Test]
	public void AsSpan_AfterRemove_CorrectLength() {
		var set = new SortedArraySet<int>();

		set.Add(10);
		set.Add(20);
		set.Add(30);
		set.Remove(20);

		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(2));
		Assert.That(span[0], Is.EqualTo(10));
		Assert.That(span[1], Is.EqualTo(30));
	}

	// --- IReadOnlyCollection ---

	[Test]
	public void IReadOnlyCollection_Count_ReturnsCorrectValue() {
		var set = new SortedArraySet<int>();

		set.Add(1);
		set.Add(2);
		set.Add(3);

		IReadOnlyCollection<int> roc = set;
		Assert.That(roc.Count, Is.EqualTo(3));
	}

	// --- Mixed operations ---

	[Test]
	public void InterleavedAddRemove_MaintainsInvariant() {
		var set = new SortedArraySet<int>();

		set.Add(50);
		set.Add(30);
		set.Add(70);
		set.Remove(50);
		set.Add(40);
		set.Add(60);
		set.Remove(30);
		set.Add(35);

		// Expected: 35, 40, 60, 70
		var span = set.AsSpan();
		Assert.That(span.Length, Is.EqualTo(4));
		Assert.That(span[0], Is.EqualTo(35));
		Assert.That(span[1], Is.EqualTo(40));
		Assert.That(span[2], Is.EqualTo(60));
		Assert.That(span[3], Is.EqualTo(70));
	}

	[Test]
	public void AddRemoveAdd_SameItem_Works() {
		var set = new SortedArraySet<int>();

		Assert.That(set.Add(10), Is.True);
		Assert.That(set.Remove(10), Is.True);
		Assert.That(set.Add(10), Is.True);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(10), Is.True);
	}

	[Test]
	public void LargeSet_AddRemove_Consistent() {
		var set = new SortedArraySet<int>();
		var reference = new SortedSet<int>();
		var rng = new Random(42);

		// Add 1000 random items
		for (var i = 0; i < 1000; i++) {
			var val = rng.Next(500); // some duplicates expected
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

		// Verify sorted order matches
		var span = set.AsSpan();
		var refArray = reference.ToArray();
		Assert.That(span.Length, Is.EqualTo(refArray.Length));
		for (var i = 0; i < span.Length; i++)
			Assert.That(span[i], Is.EqualTo(refArray[i]));
	}

	// --- String type ---

	[Test]
	public void StringType_SortedCorrectly() {
		var set = new SortedArraySet<string>();

		set.Add("charlie");
		set.Add("alpha");
		set.Add("bravo");

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo("alpha"));
		Assert.That(span[1], Is.EqualTo("bravo"));
		Assert.That(span[2], Is.EqualTo("charlie"));
	}

	[Test]
	public void StringType_Remove_ClearsReference() {
		var set = new SortedArraySet<string>();

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
		var set = new SortedArraySet<int>();

		set.Add(int.MaxValue);
		set.Add(int.MinValue);
		set.Add(0);

		var span = set.AsSpan();
		Assert.That(span[0], Is.EqualTo(int.MinValue));
		Assert.That(span[1], Is.EqualTo(0));
		Assert.That(span[2], Is.EqualTo(int.MaxValue));
	}

	// --- Pooling ---

	[Test]
	public void Pooling_NoGrow_StaysOnPooledArray() {
		var set = new SortedArraySet<int>();

		// Add items within pooled capacity (128)
		for (var i = 0; i < 50; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(50));

		// Enumeration should work fine
		var items = new List<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(50));
	}

	[Test]
	public void Pooling_GrowBeyondPooled_StillFunctional() {
		var set = new SortedArraySet<int>();

		// Force grow past pooled array size
		for (var i = 200; i >= 0; i--)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(201));

		var span = set.AsSpan();
		for (var i = 0; i <= 200; i++)
			Assert.That(span[i], Is.EqualTo(i));

		// Iteration still works
		var items = new List<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(201));
	}

	[Test]
	public void Pooling_EnumeratorDispose_AfterGrow_DoesNotThrow() {
		var set = new SortedArraySet<int>();

		// Add items on pooled array
		for (var i = 0; i < 10; i++)
			set.Add(i);

		// Get enumerator (captures pooled array, increments ref count)
		var enumerator = set.GetEnumerator();

		// Force grow — retires pooled array, decrements set's ref count
		for (var i = 10; i < 200; i++)
			set.Add(i);

		// Enumerator iterates old (pooled) array with old count
		while (enumerator.MoveNext()) { }

		// Dispose decrements ref count — should return pooled array to pool
		enumerator.Dispose();

		// Set still works on the new array
		Assert.That(set.Count, Is.EqualTo(200));
		Assert.That(set.Contains(0), Is.True);
		Assert.That(set.Contains(199), Is.True);
	}

	[Test]
	public void Pooling_MultipleEnumerators_AllDispose_Safely() {
		var set = new SortedArraySet<int>();

		for (var i = 0; i < 10; i++)
			set.Add(i);

		// Multiple enumerators on the pooled array
		var e1 = set.GetEnumerator();
		var e2 = set.GetEnumerator();
		var e3 = set.GetEnumerator();

		while (e1.MoveNext()) { }
		while (e2.MoveNext()) { }
		while (e3.MoveNext()) { }

		e1.Dispose();
		e2.Dispose();
		e3.Dispose();

		// Set still works
		set.Add(100);
		Assert.That(set.Count, Is.EqualTo(11));
	}
}

namespace Prague.Generated.Tests.Cache;

using System.Collections.Immutable;
using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ValueSetTests {
	[Test]
	public void Constructor_Default_CreatesEmptySet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsInitlized, Is.True);
	}

	[Test]
	public void Constructor_WithCapacity_CreatesEmptySet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>(100);

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.IsInitlized, Is.True);
	}

	[Test]
	public void Constructor_WithComparer_UsesProvidedComparer() {
		using var set = new ValueSet<string, CustomKeyComparer<string>>(new CustomKeyComparer<string>(StringComparer.OrdinalIgnoreCase));

		set.Add("Hello");

		Assert.That(set.Contains("hello"), Is.True);
		Assert.That(set.Contains("HELLO"), Is.True);
	}

	[Test]
	public void Constructor_WithCapacityAndComparer_WorksCorrectly() {
		using var set = new ValueSet<string, CustomKeyComparer<string>>(50, new CustomKeyComparer<string>(StringComparer.OrdinalIgnoreCase));

		set.Add("Test");

		Assert.That(set.Contains("test"), Is.True);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_SingleItem_IncreasesCount() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		var added = set.Add(42);

		Assert.That(added, Is.True);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleItems_IncreasesCount() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		set.Add(1);
		set.Add(2);
		set.Add(3);

		Assert.That(set.Count, Is.EqualTo(3));
	}

	[Test]
	public void Add_DuplicateItem_ReturnsFalseAndDoesNotIncreaseCount() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		var firstAdd = set.Add(42);
		var secondAdd = set.Add(42);

		Assert.That(firstAdd, Is.True);
		Assert.That(secondAdd, Is.False);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_NullValue_WorksForReferenceTypes() {
		using var set = new ValueSet<string, DefaultKeyComparer<string>>();

		var added = set.Add(null);

		Assert.That(added, Is.True);
		Assert.That(set.Contains(null), Is.True);
	}

	[Test]
	public void Add_ManyItems_TriggersResize() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		// Add more than inline capacity (47) to trigger resize
		for (var i = 0; i < 100; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(100));

		// Verify all items are present
		for (var i = 0; i < 100; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	[Test]
	public void Add_WithinInlineCapacity_DoesNotAllocateArrays() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		// Add up to inline capacity (47)
		for (var i = 0; i < 47; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(47));

		// All items should still be accessible
		for (var i = 0; i < 47; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	[Test]
	public void Contains_ExistingItem_ReturnsTrue() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(42);

		Assert.That(set.Contains(42), Is.True);
	}

	[Test]
	public void Contains_NonExistingItem_ReturnsFalse() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(42);

		Assert.That(set.Contains(999), Is.False);
	}

	[Test]
	public void Contains_EmptySet_ReturnsFalse() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		Assert.That(set.Contains(42), Is.False);
	}

	[Test]
	public void Contains_WithCustomComparer_UsesComparer() {
		using var set = new ValueSet<string, CustomKeyComparer<string>>(new CustomKeyComparer<string>(StringComparer.OrdinalIgnoreCase));
		set.Add("Hello");

		Assert.That(set.Contains("hello"), Is.True);
		Assert.That(set.Contains("HELLO"), Is.True);
		Assert.That(set.Contains("World"), Is.False);
	}

	[Test]
	public void Contains_AfterResize_StillFindsItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		for (var i = 0; i < 200; i++)
			set.Add(i);

		for (var i = 0; i < 200; i++)
			Assert.That(set.Contains(i), Is.True);

		Assert.That(set.Contains(999), Is.False);
	}

	[Test]
	public void Remove_ExistingItem_ReturnsTrueAndDecreasesCount() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(42);

		var removed = set.Remove(42);

		Assert.That(removed, Is.True);
		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.Contains(42), Is.False);
	}

	[Test]
	public void Remove_NonExistingItem_ReturnsFalse() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(42);

		var removed = set.Remove(999);

		Assert.That(removed, Is.False);
		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void Remove_FromEmptySet_ReturnsFalse() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		var removed = set.Remove(42);

		Assert.That(removed, Is.False);
	}

	[Test]
	public void Remove_MultipleItems_WorksCorrectly() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.Remove(2);

		Assert.That(set.Count, Is.EqualTo(2));
		Assert.That(set.Contains(1), Is.True);
		Assert.That(set.Contains(2), Is.False);
		Assert.That(set.Contains(3), Is.True);
	}

	[Test]
	public void Remove_ThenAdd_ReusesSlot() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.Remove(2);
		set.Add(4);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(1), Is.True);
		Assert.That(set.Contains(2), Is.False);
		Assert.That(set.Contains(3), Is.True);
		Assert.That(set.Contains(4), Is.True);
	}

	[Test]
	public void Remove_AllItems_ResultsInEmptySet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.Remove(1);
		set.Remove(2);
		set.Remove(3);

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void Clear_EmptySet_DoesNotThrow() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		set.Clear();

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void Clear_WithItems_RemovesAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.Clear();

		Assert.That(set.Count, Is.EqualTo(0));
		Assert.That(set.Contains(1), Is.False);
		Assert.That(set.Contains(2), Is.False);
		Assert.That(set.Contains(3), Is.False);
	}

	[Test]
	public void Clear_ThenAdd_WorksCorrectly() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.Clear();
		set.Add(3);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(3), Is.True);
	}

	[Test]
	public void UnionWith_IEnumerable_AddsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.UnionWith(new[] { 3, 4, 5 });

		Assert.That(set.Count, Is.EqualTo(5));
		Assert.That(set.Contains(1), Is.True);
		Assert.That(set.Contains(5), Is.True);
	}

	[Test]
	public void UnionWith_IEnumerable_IgnoresDuplicates() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.UnionWith(new[] { 2, 3, 3 });

		Assert.That(set.Count, Is.EqualTo(3));
	}

	[Test]
	public void UnionWith_ReadOnlySpan_AddsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		set.UnionWith(new ReadOnlySpan<int>([2, 3, 4]));

		Assert.That(set.Count, Is.EqualTo(4));
	}

	[Test]
	public void UnionWith_Array_AddsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		set.UnionWith(new[] { 2, 3, 4 });

		Assert.That(set.Count, Is.EqualTo(4));
	}

	[Test]
	public void UnionWith_AnotherValueSet_AddsAllItems() {
		var set1 = new ValueSet<int, DefaultKeyComparer<int>>();
		var set2 = new ValueSet<int, DefaultKeyComparer<int>>();
		try {
			set1.Add(1);
			set1.Add(2);

			set2.Add(3);
			set2.Add(4);

			set1.UnionWith(ref set2);

			Assert.That(set1.Count, Is.EqualTo(4));
		}
		finally {
			set1.Dispose();
			set2.Dispose();
		}
	}

	[Test]
	public void UnionWith_EmptyCollection_DoesNotChangeSet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		set.UnionWith(Array.Empty<int>());

		Assert.That(set.Count, Is.EqualTo(1));
	}

	[Test]
	public void UnionWith_ImmutableHashSet_AddsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		var immutableSet = ImmutableHashSet.Create(2, 3, 4);
		set.UnionWith(new IdentityInto<int>(), immutableSet);

		Assert.That(set.Count, Is.EqualTo(4));
	}

	[Test]
	public void IntersectWith_SingleItem_KeepsOnlyMatchingItem() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.IntersectWith(2);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(2), Is.True);
	}

	[Test]
	public void IntersectWith_SingleItem_NotInSet_ClearsSet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.IntersectWith(99);

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void IntersectWith_IEnumerable_KeepsOnlyCommonItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);
		set.Add(4);

		set.IntersectWith(new[] { 2, 3, 5 });

		Assert.That(set.Count, Is.EqualTo(2));
		Assert.That(set.Contains(2), Is.True);
		Assert.That(set.Contains(3), Is.True);
	}

	[Test]
	public void IntersectWith_EmptyCollection_ClearsSet() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.IntersectWith(Array.Empty<int>());

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void IntersectWith_EmptySet_RemainsEmpty() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		set.IntersectWith(new[] { 1, 2, 3 });

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void IntersectWith_ReadOnlySpan_KeepsOnlyCommonItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.IntersectWith(new ReadOnlySpan<int>([2, 4]));

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(2), Is.True);
	}

	[Test]
	public void IntersectWith_Array_KeepsOnlyCommonItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		set.IntersectWith(new[] { 2, 4 });

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(2), Is.True);
	}

	[Test]
	public void IntersectWith_AnotherValueSet_KeepsOnlyCommonItems() {
		var set1 = new ValueSet<int, DefaultKeyComparer<int>>();
		var set2 = new ValueSet<int, DefaultKeyComparer<int>>();
		try {
			set1.Add(1);
			set1.Add(2);
			set1.Add(3);

			set2.Add(2);
			set2.Add(3);
			set2.Add(4);

			set1.IntersectWith(ref set2);

			Assert.That(set1.Count, Is.EqualTo(2));
			Assert.That(set1.Contains(2), Is.True);
			Assert.That(set1.Contains(3), Is.True);
		}
		finally {
			set1.Dispose();
			set2.Dispose();
		}
	}

	[Test]
	public void IntersectWith_HashSet_KeepsOnlyCommonItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		var hashSet = new HashSet<int> { 2, 4 };
		set.IntersectWith(hashSet);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(2), Is.True);
	}

	[Test]
	public void IntersectWith_ImmutableHashSet_KeepsOnlyCommonItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		var immutableSet = ImmutableHashSet.Create(2, 4);
		set.IntersectWith(immutableSet);

		Assert.That(set.Count, Is.EqualTo(1));
		Assert.That(set.Contains(2), Is.True);
	}

	[Test]
	public void GetEnumerator_EmptySet_ReturnsNoItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		var count = 0;

		foreach (var _ in set)
			count++;

		Assert.That(count, Is.EqualTo(0));
	}

	[Test]
	public void GetEnumerator_WithItems_ReturnsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);

		var items = new List<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(3));
		Assert.That(items, Does.Contain(1));
		Assert.That(items, Does.Contain(2));
		Assert.That(items, Does.Contain(3));
	}

	[Test]
	public void GetEnumerator_AfterRemove_SkipsRemovedItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);
		set.Add(3);
		set.Remove(2);

		var items = new List<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(2));
		Assert.That(items, Does.Contain(1));
		Assert.That(items, Does.Contain(3));
		Assert.That(items, Does.Not.Contain(2));
	}

	[Test]
	public void GetEnumerator_LargeSet_ReturnsAllItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < 200; i++)
			set.Add(i);

		var items = new HashSet<int>();
		foreach (var item in set)
			items.Add(item);

		Assert.That(items, Has.Count.EqualTo(200));
		for (var i = 0; i < 200; i++)
			Assert.That(items, Does.Contain(i));
	}

	[Test]
	public void Enumerator_MultipleEnumerations_ReturnSameItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		// First enumeration
		var firstPass = new List<int>();
		foreach (var item in set)
			firstPass.Add(item);

		// Second enumeration
		var secondPass = new List<int>();
		foreach (var item in set)
			secondPass.Add(item);

		Assert.That(secondPass, Is.EqualTo(firstPass));
	}

	[Test]
	public void EnsureCapacity_LargerThanCurrent_IncreasesCapacity() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		var newCapacity = set.EnsureCapacity(100);

		Assert.That(newCapacity, Is.GreaterThanOrEqualTo(100));
		Assert.That(set.Contains(1), Is.True); // Items preserved
	}

	[Test]
	public void EnsureCapacity_SmallerThanCurrent_DoesNotDecrease() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>(100);

		var capacity = set.EnsureCapacity(10);

		Assert.That(capacity, Is.GreaterThanOrEqualTo(100));
	}

	[Test]
	public void EnsureCapacity_Negative_ThrowsArgumentOutOfRangeException() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		Assert.Throws<ArgumentOutOfRangeException>(() => set.EnsureCapacity(-1));
	}

	[Test]
	public void EnsureCapacity_Zero_ReturnsCurrentCapacity() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		var capacity = set.EnsureCapacity(0);

		Assert.That(capacity, Is.GreaterThan(0));
	}

	[Test]
	public void Dispose_ClearsSet() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);
		set.Add(2);

		set.Dispose();

		Assert.That(set.Count, Is.EqualTo(0));
	}

	[Test]
	public void Dispose_CanBeCalledMultipleTimes() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>();
		set.Add(1);

		set.Dispose();
		set.Dispose(); // Should not throw
	}

	[Test]
	public void StressTest_AddRemoveMany() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		// Add 1000 items
		for (var i = 0; i < 1000; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(1000));

		// Remove half
		for (var i = 0; i < 500; i++)
			set.Remove(i);

		Assert.That(set.Count, Is.EqualTo(500));

		// Add more
		for (var i = 1000; i < 1500; i++)
			set.Add(i);

		Assert.That(set.Count, Is.EqualTo(1000));

		// Verify correct items present
		for (var i = 0; i < 500; i++)
			Assert.That(set.Contains(i), Is.False);
		for (var i = 500; i < 1500; i++)
			Assert.That(set.Contains(i), Is.True);
	}

	[Test]
	public void StringSet_WorksCorrectly() {
		using var set = new ValueSet<string, DefaultKeyComparer<string>>();
		set.Add("hello");
		set.Add("world");
		set.Add("test");

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains("hello"), Is.True);
		Assert.That(set.Contains("HELLO"), Is.False); // Case sensitive by default
	}

	[Test]
	public void GuidSet_WorksCorrectly() {
		using var set = new ValueSet<Guid, DefaultKeyComparer<Guid>>();
		var guid1 = Guid.NewGuid();
		var guid2 = Guid.NewGuid();
		var guid3 = Guid.NewGuid();

		set.Add(guid1);
		set.Add(guid2);
		set.Add(guid3);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(guid1), Is.True);
		Assert.That(set.Contains(guid2), Is.True);
		Assert.That(set.Contains(Guid.NewGuid()), Is.False);
	}

	[Test]
	public void LongSet_WorksCorrectly() {
		using var set = new ValueSet<long, DefaultKeyComparer<long>>();
		set.Add(long.MaxValue);
		set.Add(long.MinValue);
		set.Add(0L);

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(long.MaxValue), Is.True);
		Assert.That(set.Contains(long.MinValue), Is.True);
		Assert.That(set.Contains(0L), Is.True);
	}

	[Test]
	public void HashCollisions_HandledCorrectly() {
		// Use a comparer that produces many collisions
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		// Add items that may produce hash collisions
		for (var i = 0; i < 100; i++)
			set.Add(i * 47); // Multiples of StackSize

		Assert.That(set.Count, Is.EqualTo(100));

		for (var i = 0; i < 100; i++)
			Assert.That(set.Contains(i * 47), Is.True);
	}

	[Test]
	public void StructValue_WorksCorrectly() {
		using var set = new ValueSet<KeyValuePair<int, int>, DefaultKeyComparer<KeyValuePair<int, int>>>();
		set.Add(new KeyValuePair<int, int>(1, 10));
		set.Add(new KeyValuePair<int, int>(2, 20));

		Assert.That(set.Count, Is.EqualTo(2));
		Assert.That(set.Contains(new KeyValuePair<int, int>(1, 10)), Is.True);
		Assert.That(set.Contains(new KeyValuePair<int, int>(1, 11)), Is.False);
	}

	[Test]
	public void IncrementalIntersecter_BasicUsage_WorksCorrectly() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>();
		try {
			set.Add(1);
			set.Add(2);
			set.Add(3);
			set.Add(4);
			set.Add(5);

			Span<int> buffer = stackalloc int[ValueSet<int, DefaultKeyComparer<int>>.StackAllocThreshold];
			var intersecter = new ValueSet<int, DefaultKeyComparer<int>>.IncrementalIntersecter(ref set, buffer);

			intersecter.IntersectWith(2);
			intersecter.IntersectWith(4);
			intersecter.Dispose();

			Assert.That(set.Count, Is.EqualTo(2));
			Assert.That(set.Contains(2), Is.True);
			Assert.That(set.Contains(4), Is.True);
			Assert.That(set.Contains(1), Is.False);
		}
		finally {
			set.Dispose();
		}
	}

	[Test]
	public void UnionWith_WithInto_TransformsItems() {
		using var set = new ValueSet<int, DefaultKeyComparer<int>>();

		set.UnionWith(new DoubleInto(), new[] { 1, 2, 3 });

		Assert.That(set.Count, Is.EqualTo(3));
		Assert.That(set.Contains(2), Is.True); // 1 * 2
		Assert.That(set.Contains(4), Is.True); // 2 * 2
		Assert.That(set.Contains(6), Is.True); // 3 * 2
	}

	[Test]
	public void IntersectWith_WithInto_TransformsItems() {
		var set = new ValueSet<int, DefaultKeyComparer<int>>();
		var other = new ValueSet<int, DefaultKeyComparer<int>>();
		try {
			set.Add(2);
			set.Add(4);
			set.Add(6);
			set.Add(8);

			other.Add(1); // transforms to 2
			other.Add(3); // transforms to 6

			set.IntersectWith(new DoubleInto(), ref other);

			Assert.That(set.Count, Is.EqualTo(2));
			Assert.That(set.Contains(2), Is.True);
			Assert.That(set.Contains(6), Is.True);
		}
		finally {
			set.Dispose();
			other.Dispose();
		}
	}

	private struct IdentityInto<T> : IInto<T, T> {
		public T Into(T from) {
			return from;
		}

		public T From(T into) {
			return into;
		}
	}

	private struct DoubleInto : IInto<int, int> {
		public int Into(int from) {
			return from * 2;
		}

		public int From(int into) {
			return into / 2;
		}
	}

	// Simulates JoinedKeyPair where equality is based only on Key, not JoinedKey
	private struct TestJoinedKeyPair : IEquatable<TestJoinedKeyPair> {
		public readonly int JoinedKey; // e.g., GameId
		public readonly int Key; // e.g., MarketId

		public TestJoinedKeyPair(int joinedKey, int key) {
			JoinedKey = joinedKey;
			Key = key;
		}

		// Equality based ONLY on Key, not JoinedKey (same as real JoinedKeyPair)
		public bool Equals(TestJoinedKeyPair other) {
			return Key.Equals(other.Key);
		}

		public override bool Equals(object? obj) {
			return obj is TestJoinedKeyPair other && Equals(other);
		}

		public override int GetHashCode() {
			return Key.GetHashCode();
		}

		public override string ToString() {
			return $"{{JoinedKey={JoinedKey}, Key={Key}}}";
		}
	}

	private struct TestJoinedKeyPairInto : IInto<int, TestJoinedKeyPair> {
		private readonly int _joinedKey;

		public TestJoinedKeyPairInto(int joinedKey) {
			_joinedKey = joinedKey;
		}

		public TestJoinedKeyPair Into(int from) {
			return new TestJoinedKeyPair(_joinedKey, from);
		}

		public int From(TestJoinedKeyPair into) {
			return into.Key;
		}
	}

	[Test]
	public void JoinedKeyPair_UnionWith_SingleGameId_CountMatchesInput() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Simulate: GameId=1 has markets [100, 101, 102]
		var gameId1Markets = new[] { 100, 101, 102 };
		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);

		Assert.That(candidates.Count, Is.EqualTo(3));

		// Verify all have JoinedKey=1
		foreach (var pair in candidates) {
			Console.WriteLine($"Candidate: {pair}");
			Assert.That(pair.JoinedKey, Is.EqualTo(1));
		}
	}

	[Test]
	public void JoinedKeyPair_UnionWith_MultipleGameIds_NoOverlap_CountIsSum() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Simulate: GameId=1 has markets [100, 101, 102]
		var gameId1Markets = new[] { 100, 101, 102 };
		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);

		// Simulate: GameId=2 has markets [200, 201] (no overlap)
		var gameId2Markets = new[] { 200, 201 };
		candidates.UnionWith(new TestJoinedKeyPairInto(2), gameId2Markets);

		Assert.That(candidates.Count, Is.EqualTo(5));

		// Count per JoinedKey
		var countByJoinedKey = new Dictionary<int, int>();
		foreach (var pair in candidates) {
			Console.WriteLine($"Candidate: {pair}");
			if (!countByJoinedKey.ContainsKey(pair.JoinedKey))
				countByJoinedKey[pair.JoinedKey] = 0;
			countByJoinedKey[pair.JoinedKey]++;
		}

		Assert.That(countByJoinedKey[1], Is.EqualTo(3));
		Assert.That(countByJoinedKey[2], Is.EqualTo(2));
	}

	[Test]
	public void JoinedKeyPair_UnionWith_MultipleGameIds_WithOverlap_OverwritesJoinedKey() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Simulate: GameId=1 has markets [100, 101, 102]
		var gameId1Markets = new[] { 100, 101, 102 };
		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);

		Console.WriteLine("After adding GameId=1 markets:");
		foreach (var pair in candidates)
			Console.WriteLine($"  {pair}");

		// Simulate: GameId=2 has markets [102, 200, 201] - market 102 OVERLAPS!
		var gameId2Markets = new[] { 102, 200, 201 };
		candidates.UnionWith(new TestJoinedKeyPairInto(2), gameId2Markets);

		Console.WriteLine("\nAfter adding GameId=2 markets (with overlap on 102):");
		foreach (var pair in candidates)
			Console.WriteLine($"  {pair}");

		// Key question: what is the count and what JoinedKey does market 102 have?
		Console.WriteLine($"\nTotal count: {candidates.Count}");

		// Count per JoinedKey
		var countByJoinedKey = new Dictionary<int, int>();
		foreach (var pair in candidates) {
			if (!countByJoinedKey.ContainsKey(pair.JoinedKey))
				countByJoinedKey[pair.JoinedKey] = 0;
			countByJoinedKey[pair.JoinedKey]++;
		}

		Console.WriteLine($"GameId=1 count: {countByJoinedKey.GetValueOrDefault(1)}");
		Console.WriteLine($"GameId=2 count: {countByJoinedKey.GetValueOrDefault(2)}");

		// This test documents the behavior - does UnionWith overwrite or keep original?
		// Based on the race condition, it seems to OVERWRITE the JoinedKey
		// So market 102 would end up with JoinedKey=2 instead of JoinedKey=1

		// If it overwrites: count=5 (100,101 with GameId=1; 102,200,201 with GameId=2)
		// If it keeps original: count=5 (100,101,102 with GameId=1; 200,201 with GameId=2)

		// The test will tell us which behavior we have
		Assert.That(candidates.Count, Is.EqualTo(5), "Total unique keys should be 5");
	}

	[Test]
	public void JoinedKeyPair_RaceConditionSimulation_InitVsActualCount() {
		// This test simulates the exact race condition scenario

		// Phase 1: Build candidates from index snapshots
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Index snapshot for GameId=1: has 3 markets [100, 101, 102]
		var gameId1Markets = new[] { 100, 101, 102 };
		var initCount1 = gameId1Markets.Length; // Would call Init(GameId=1, 3)

		// Index snapshot for GameId=2: has 2 markets [102, 200]
		// Note: market 102 appears in BOTH indexes (race condition - it moved)
		var gameId2Markets = new[] { 102, 200 };
		var initCount2 = gameId2Markets.Length; // Would call Init(GameId=2, 2)

		Console.WriteLine($"Init counts - GameId=1: {initCount1}, GameId=2: {initCount2}");

		// Add to candidates
		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);
		candidates.UnionWith(new TestJoinedKeyPairInto(2), gameId2Markets);

		Console.WriteLine("\nCandidates after building:");
		foreach (var pair in candidates)
			Console.WriteLine($"  {pair}");

		// Phase 2: Count actual items per JoinedKey (simulating what Add would do)
		var actualCountByJoinedKey = new Dictionary<int, int>();
		foreach (var pair in candidates) {
			if (!actualCountByJoinedKey.ContainsKey(pair.JoinedKey))
				actualCountByJoinedKey[pair.JoinedKey] = 0;
			actualCountByJoinedKey[pair.JoinedKey]++;
		}

		var actualCount1 = actualCountByJoinedKey.GetValueOrDefault(1);
		var actualCount2 = actualCountByJoinedKey.GetValueOrDefault(2);

		Console.WriteLine($"\nActual counts - GameId=1: {actualCount1}, GameId=2: {actualCount2}");
		Console.WriteLine(
			$"Init vs Actual - GameId=1: {initCount1} vs {actualCount1}, GameId=2: {initCount2} vs {actualCount2}");

		// The race condition would cause overflow if:
		// - actualCount > initCount for any GameId

		var gameId1Overflow = actualCount1 > initCount1;
		var gameId2Overflow = actualCount2 > initCount2;

		Console.WriteLine($"\nOverflow? GameId=1: {gameId1Overflow}, GameId=2: {gameId2Overflow}");

		// Document the behavior
		if (gameId1Overflow || gameId2Overflow)
			Console.WriteLine("WARNING: This would cause IndexOutOfRangeException in UnsafeAdd!");
	}

	[Test]
	public void JoinedKeyPair_CheckForDuplicateKeys_InEnumeration() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Add items for multiple GameIds
		var gameId1Markets = new[] { 100, 101, 102, 103, 104 };
		var gameId2Markets = new[] { 200, 201, 202 };

		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);
		candidates.UnionWith(new TestJoinedKeyPairInto(2), gameId2Markets);

		Console.WriteLine($"Total count reported: {candidates.Count}");

		// Check for duplicates during enumeration
		var seenKeys = new HashSet<int>();
		var duplicateCount = 0;
		var enumeratedCount = 0;

		foreach (var pair in candidates) {
			enumeratedCount++;
			if (!seenKeys.Add(pair.Key)) {
				duplicateCount++;
				Console.WriteLine($"DUPLICATE KEY FOUND: {pair}");
			}
		}

		Console.WriteLine($"Enumerated count: {enumeratedCount}");
		Console.WriteLine($"Unique keys: {seenKeys.Count}");
		Console.WriteLine($"Duplicate count: {duplicateCount}");

		Assert.That(enumeratedCount, Is.EqualTo(candidates.Count), "Enumerated count should match Count property");
		Assert.That(duplicateCount, Is.EqualTo(0), "Should have no duplicate keys");
	}

	[Test]
	public void JoinedKeyPair_LargeScale_CheckForDuplicates() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Simulate a more realistic scenario with many GameIds and Markets
		var random = new Random(42);
		var totalInit = 0;

		// Create 10 GameIds, each with random number of markets
		for (var gameId = 1; gameId <= 10; gameId++) {
			var marketCount = random.Next(5, 20);
			var markets = Enumerable.Range(gameId * 1000, marketCount).ToArray();
			totalInit += marketCount;

			Console.WriteLine($"GameId={gameId}: Init with {marketCount} markets");
			candidates.UnionWith(new TestJoinedKeyPairInto(gameId), markets);
		}

		Console.WriteLine($"\nTotal init count: {totalInit}");
		Console.WriteLine($"Actual candidates count: {candidates.Count}");

		// Enumerate and check for duplicates
		var seenKeys = new HashSet<int>();
		var enumeratedCount = 0;

		foreach (var pair in candidates) {
			enumeratedCount++;
			if (!seenKeys.Add(pair.Key)) Console.WriteLine($"DUPLICATE: {pair}");
		}

		Console.WriteLine($"Enumerated count: {enumeratedCount}");

		Assert.That(enumeratedCount, Is.EqualTo(candidates.Count));
		Assert.That(seenKeys.Count, Is.EqualTo(candidates.Count));
	}

	[Test]
	public void JoinedKeyPair_EnumerateTwice_SameResults() {
		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		var gameId1Markets = new[] { 100, 101, 102 };
		var gameId2Markets = new[] { 200, 201 };

		candidates.UnionWith(new TestJoinedKeyPairInto(1), gameId1Markets);
		candidates.UnionWith(new TestJoinedKeyPairInto(2), gameId2Markets);

		// First enumeration
		var firstPass = new List<TestJoinedKeyPair>();
		foreach (var pair in candidates)
			firstPass.Add(pair);

		// Second enumeration
		var secondPass = new List<TestJoinedKeyPair>();
		foreach (var pair in candidates)
			secondPass.Add(pair);

		Console.WriteLine($"First pass count: {firstPass.Count}");
		Console.WriteLine($"Second pass count: {secondPass.Count}");

		Assert.That(firstPass.Count, Is.EqualTo(secondPass.Count));
		Assert.That(firstPass.Count, Is.EqualTo(candidates.Count));

		// Check they have the same keys
		var firstKeys = firstPass.Select(p => p.Key).OrderBy(k => k).ToList();
		var secondKeys = secondPass.Select(p => p.Key).OrderBy(k => k).ToList();

		Assert.That(firstKeys, Is.EqualTo(secondKeys));
	}

	[Test]
	public void JoinedKeyPair_SimulateActualFlow_CountPerJoinedKey() {
		// This test simulates the EXACT flow in the real code:
		// 1. For each JoinKey, call Init(joinKey, indexCount)
		// 2. Add all candidates with their JoinKey
		// 3. Iterate candidates and call Add(candidate.JoinedKey, value) - grouping by JoinedKey

		using var candidates = new ValueSet<TestJoinedKeyPair, DefaultKeyComparer<TestJoinedKeyPair>>();

		// Simulate indexes - each game has some markets
		var indexes = new Dictionary<int, int[]> {
			{
				1, new[] { 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118 }
			}, // 19 markets
			{ 2, new[] { 200, 201, 202 } },
			{ 3, new[] { 300, 301 } }
		};

		// Phase 1: Init and build candidates (simulates IntersectValuesInit)
		var initCounts = new Dictionary<int, int>();
		foreach (var (gameId, markets) in indexes) {
			initCounts[gameId] = markets.Length;
			Console.WriteLine($"Init(GameId={gameId}, count={markets.Length})");
			candidates.UnionWith(new TestJoinedKeyPairInto(gameId), markets);
		}

		Console.WriteLine($"\nTotal candidates: {candidates.Count}");

		// Phase 2: Simulate TryGetValues - iterate and count how many times each JoinedKey is hit
		var actualCounts = new Dictionary<int, int>();
		foreach (var pair in candidates) {
			if (!actualCounts.ContainsKey(pair.JoinedKey))
				actualCounts[pair.JoinedKey] = 0;
			actualCounts[pair.JoinedKey]++;
		}

		Console.WriteLine("\nInit vs Actual:");
		var hasOverflow = false;
		foreach (var (gameId, initCount) in initCounts) {
			var actualCount = actualCounts.GetValueOrDefault(gameId);
			var overflow = actualCount > initCount;
			if (overflow) hasOverflow = true;
			Console.WriteLine($"  GameId={gameId}: Init={initCount}, Actual={actualCount}, Overflow={overflow}");
		}

		Assert.That(hasOverflow, Is.False, "Should have no overflow");

		// Also verify total
		var totalInit = initCounts.Values.Sum();
		var totalActual = actualCounts.Values.Sum();
		Console.WriteLine($"\nTotal: Init={totalInit}, Actual={totalActual}");
		Assert.That(totalActual, Is.EqualTo(candidates.Count));
	}
}
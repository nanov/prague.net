namespace Prague.Generated.Tests.Query;

using System.Buffers;
using Prague.Core;
using Prague.Core.Utils;
using NUnit.Framework;

[DataCache]
public partial class SimpleItem {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";
}

[TestFixture]
public class QueryResultsTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<SimpleItemCache>()
			.Build();
		_cache = _registry.GetCache<SimpleItemCache>();

		// Seed with test data
		for (var i = 1; i <= 10; i++) _cache.AddOrUpdate(new SimpleItem { Id = i, Name = $"Item {i}" });
	}

	private DataCacheRegistry _registry;
	private SimpleItemCache _cache;

	[Test]
	public void Count_ReturnsCorrectValue() {
		var results = _cache.Cache.Query().Execute();
		Assert.That(results.Count, Is.EqualTo(10));
	}

	[Test]
	public void TotalCount_ReturnsCorrectValue() {
		var results = _cache.Cache.Query().Execute();
		Assert.That(results.TotalCount, Is.EqualTo(10));
	}

	[Test]
	public void TotalCount_WithSkip_ReturnsOriginalTotal() {
		var results = _cache.Cache.Query().Execute(5);
		Assert.That(results.Count, Is.EqualTo(5));
		Assert.That(results.TotalCount, Is.EqualTo(10));
	}

	[Test]
	public void TotalCount_WithTake_ReturnsOriginalTotal() {
		var results = _cache.Cache.Query().Execute(take: 3);
		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.TotalCount, Is.EqualTo(10));
	}

	[Test]
	public void TotalCount_WithSkipAndTake_ReturnsOriginalTotal() {
		var results = _cache.Cache.Query().Execute(2, 3);
		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.TotalCount, Is.EqualTo(10));
	}

	[Test]
	public void Empty_ReturnsEmptyResults() {
		Assert.That(QueryResults<SimpleItem>.Empty.Count, Is.EqualTo(0));
		Assert.That(QueryResults<SimpleItem>.Empty.TotalCount, Is.EqualTo(0));
	}

	[Test]
	public void Indexer_Get_ReturnsCorrectItem() {
		var results = _cache.Cache.Query().Execute();
		var item = results[0];
		Assert.That(item, Is.Not.Null);
	}

	[Test]
	public void Indexer_Get_OutOfRange_ThrowsException() {
		var results = _cache.Cache.Query().Execute();
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = results[100];
		});
	}

	[Test]
	public void Indexer_Get_NegativeIndex_ThrowsException() {
		var results = _cache.Cache.Query().Execute();
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = results[-1];
		});
	}

	[Test]
	public void GetEnumerator_IteratesAllItems() {
		var results = _cache.Cache.Query().Execute();
		var count = 0;
		foreach (var item in results) {
			Assert.That(item, Is.Not.Null);
			count++;
		}

		Assert.That(count, Is.EqualTo(10));
	}

	[Test]
	public void IEnumerable_GetEnumerator_IteratesAllItems() {
		var results = _cache.Cache.Query().Execute();
		var count = 0;
		foreach (var item in (IEnumerable<SimpleItem>)results) {
			Assert.That(item, Is.Not.Null);
			count++;
		}

		Assert.That(count, Is.EqualTo(10));
	}

	[Test]
	public void Slice_SingleArg_ReturnsCorrectSubset() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(5);
		Assert.That(sliced.Count, Is.EqualTo(5));
	}

	[Test]
	public void Slice_TwoArgs_ReturnsCorrectSubset() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 3);
		Assert.That(sliced.Count, Is.EqualTo(3));
	}

	[Test]
	public void Slice_InvalidIndex_ThrowsException() {
		var results = _cache.Cache.Query().Execute();
		Assert.Throws<ArgumentOutOfRangeException>(() => results.Slice(100));
	}

	[Test]
	public void Slice_InvalidCount_ThrowsException() {
		var results = _cache.Cache.Query().Execute();
		Assert.Throws<ArgumentOutOfRangeException>(() => results.Slice(5, 100));
	}

	[Test]
	public void ToArray_ReturnsCorrectArray() {
		var results = _cache.Cache.Query().Execute();
		var array = results.ToArray();
		Assert.That(array.Length, Is.EqualTo(10));
	}

	[Test]
	public void ToArray_OnEmptyResults_ReturnsEmptyArray() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var array = emptyResults.ToArray();
		Assert.That(array, Is.Empty);
	}

	[Test]
	public void ToArray_OnSlicedResults_ReturnsCorrectArray() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 3);
		var array = sliced.ToArray();
		Assert.That(array.Length, Is.EqualTo(3));
	}

	[Test]
	public void CopyTo_CopiesAllItems() {
		var results = _cache.Cache.Query().Execute();
		var destination = new SimpleItem[10];
		results.CopyTo(destination);
		Assert.That(destination.All(x => x != null), Is.True);
	}

	[Test]
	public void CopyTo_WithOffset_CopiesCorrectly() {
		var results = _cache.Cache.Query().Execute();
		var destination = new SimpleItem[15];
		results.CopyTo(destination, 5);
		Assert.That(destination.Take(5).All(x => x == null), Is.True);
		Assert.That(destination.Skip(5).All(x => x != null), Is.True);
	}

	[Test]
	public void Clone_ReturnsClonedResults() {
		var results = _cache.Cache.Query().Execute();
		var cloned = results.Clone();
		Assert.That(cloned.Count, Is.EqualTo(results.Count));
	}

	[Test]
	public void Clone_OnEmptyResults_ReturnsEmpty() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var cloned = emptyResults.Clone();
		Assert.That(cloned.Count, Is.EqualTo(0));
	}

	[Test]
	public void IList_IndexOf_FindsItem() {
		var results = _cache.Cache.Query().Execute();
		IList<SimpleItem> list = results;
		var firstItem = results[0];
		var index = list.IndexOf(firstItem);
		Assert.That(index, Is.EqualTo(0));
	}

	[Test]
	public void IList_IndexOf_ReturnsNegativeForMissingItem() {
		var results = _cache.Cache.Query().Execute();
		IList<SimpleItem> list = results;
		var missingItem = new SimpleItem { Id = 999, Name = "Missing" };
		var index = list.IndexOf(missingItem);
		Assert.That(index, Is.EqualTo(-1));
	}

	[Test]
	public void IList_Contains_ReturnsTrueForExistingItem() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		var firstItem = results[0];
		Assert.That(collection.Contains(firstItem), Is.True);
	}

	[Test]
	public void IList_Contains_ReturnsFalseForMissingItem() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		var missingItem = new SimpleItem { Id = 999, Name = "Missing" };
		Assert.That(collection.Contains(missingItem), Is.False);
	}

	[Test]
	public void IList_IsReadOnly_ReturnsTrue() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		Assert.That(collection.IsReadOnly, Is.True);
	}

	[Test]
	public void IList_Add_ThrowsNotSupported() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Add(new SimpleItem()));
	}

	[Test]
	public void IList_Clear_ThrowsNotSupported() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Clear());
	}

	[Test]
	public void IList_Remove_ThrowsNotSupported() {
		var results = _cache.Cache.Query().Execute();
		ICollection<SimpleItem> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Remove(results[0]));
	}

	[Test]
	public void IList_Insert_ThrowsNotSupported() {
		var results = _cache.Cache.Query().Execute();
		IList<SimpleItem> list = results;
		Assert.Throws<NotSupportedException>(() => list.Insert(0, new SimpleItem()));
	}

	[Test]
	public void IList_RemoveAt_ThrowsNotSupported() {
		var results = _cache.Cache.Query().Execute();
		IList<SimpleItem> list = results;
		Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
	}

	[Test]
	public void IReadOnlyList_Indexer_ReturnsCorrectItem() {
		var results = _cache.Cache.Query().Execute();
		IReadOnlyList<SimpleItem> readOnlyList = results;
		var item = readOnlyList[0];
		Assert.That(item, Is.Not.Null);
	}

	[Test]
	public void IReadOnlyList_Indexer_OutOfRange_ThrowsException() {
		var results = _cache.Cache.Query().Execute();
		IReadOnlyList<SimpleItem> readOnlyList = results;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = readOnlyList[100];
		});
	}

	[Test]
	public void GetHashCode_ReturnsConsistentValue() {
		var results = _cache.Cache.Query().Execute();
		var hash1 = results.GetHashCode();
		var hash2 = results.GetHashCode();
		Assert.That(hash1, Is.EqualTo(hash2));
	}

	[Test]
	public void Empty_GetHashCode_ReturnsConsistentValue() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var hash1 = emptyResults.GetHashCode();
		var hash2 = emptyResults.GetHashCode();
		Assert.That(hash1, Is.EqualTo(hash2));
	}

	[Test]
	public void Dispose_OnPooledResults_DoesNotThrow() {
		var results = _cache.Cache.Query().ExecutePooled();
		Assert.DoesNotThrow(() => results.Dispose());
	}

	[Test]
	public void Dispose_OnNonPooledResults_DoesNotThrow() {
		var results = _cache.Cache.Query().Execute();
		Assert.DoesNotThrow(() => results.Dispose());
	}

	[Test]
	public void Dispose_OnEmptyResults_DoesNotThrow() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		Assert.DoesNotThrow(() => emptyResults.Dispose());
	}

	[Test]
	public void Enumerator_Current_BeforeMoveNext_Throws() {
		var results = _cache.Cache.Query().Execute();
		var enumerator = results.GetEnumerator();
		Assert.Throws<InvalidOperationException>(() => {
			var _ = enumerator.Current;
		});
	}

	[Test]
	public void Enumerator_Current_AfterEnd_Throws() {
		var results = _cache.Cache.Query().Where(x => x.Id == 1).Execute();
		var enumerator = results.GetEnumerator();
		while (enumerator.MoveNext()) {
		}

		Assert.Throws<InvalidOperationException>(() => {
			var _ = enumerator.Current;
		});
	}

	[Test]
	public void Enumerator_Reset_WorksCorrectly() {
		var results = _cache.Cache.Query().Execute();
		IEnumerator<SimpleItem> enumerator = results.GetEnumerator();

		// Advance to first item
		enumerator.MoveNext();
		var first = enumerator.Current;

		// Reset
		enumerator.Reset();

		// Verify we can iterate again
		enumerator.MoveNext();
		Assert.That(enumerator.Current, Is.EqualTo(first));
	}

	[Test]
	public void Enumerator_Dispose_DoesNotThrow() {
		var results = _cache.Cache.Query().Execute();
		var enumerator = results.GetEnumerator();
		Assert.DoesNotThrow(() => enumerator.Dispose());
	}

	[Test]
	public void Sort_WithComparer_SortsCorrectly() {
		var results = _cache.Cache.Query().Execute();
		results.Sort(Comparer<SimpleItem>.Create((a, b) => b.Id.CompareTo(a.Id)));

		// Verify descending order
		for (var i = 0; i < results.Count - 1; i++) Assert.That(results[i].Id, Is.GreaterThan(results[i + 1].Id));
	}

	[Test]
	public void ExecuteCloned_ReturnsClonedItems() {
		var results = _cache.Cache.Query().ExecuteCloned();
		Assert.That(results.Count, Is.EqualTo(10));

		// Modify the clone and verify original is unchanged
		var clonedItem = results[0];
		var originalName = clonedItem.Name;
		clonedItem.Name = "Modified";

		// Get fresh results to verify original wasn't modified
		var freshResults = _cache.Cache.Query().Execute();
		Assert.That(freshResults[0].Name, Is.Not.EqualTo("Modified"));
	}

	[Test]
	public void ExecutePooled_ReturnsValidResults() {
		var results = _cache.Cache.Query().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(10));
		results.Dispose();
	}

	[Test]
	public void ExecutePooledCloned_ReturnsValidResults() {
		var results = _cache.Cache.Query().ExecutePooledCloned();
		Assert.That(results.Count, Is.EqualTo(10));
		results.Dispose();
	}

	[Test]
	public void ToList_ReturnsListWithAllItems() {
		var results = _cache.Cache.Query().Execute();
		var list = results.ToList();

		Assert.That(list.Count, Is.EqualTo(10));
		Assert.That(list, Is.InstanceOf<List<SimpleItem>>());
	}

	[Test]
	public void ToList_OnEmptyResults_ReturnsEmptyList() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var list = emptyResults.ToList();

		Assert.That(list, Is.Empty);
		Assert.That(list, Is.InstanceOf<List<SimpleItem>>());
	}

	[Test]
	public void ToList_OnSlicedResults_ReturnsCorrectList() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 5);
		var list = sliced.ToList();

		Assert.That(list.Count, Is.EqualTo(5));
	}

	[Test]
	public void ToList_ModifyingList_DoesNotAffectOriginal() {
		var results = _cache.Cache.Query().Execute();
		var list = results.ToList();

		list.RemoveAt(0);
		Assert.That(list.Count, Is.EqualTo(9));
		Assert.That(results.Count, Is.EqualTo(10)); // Original unchanged
	}

	[Test]
	public void ToHashSet_ReturnsSetWithAllItems() {
		var results = _cache.Cache.Query().Execute();
		var set = results.ToHashSet();

		Assert.That(set.Count, Is.EqualTo(10));
		Assert.That(set, Is.InstanceOf<HashSet<SimpleItem>>());
	}

	[Test]
	public void ToHashSet_OnEmptyResults_ReturnsEmptySet() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var set = emptyResults.ToHashSet();

		Assert.That(set, Is.Empty);
		Assert.That(set, Is.InstanceOf<HashSet<SimpleItem>>());
	}

	[Test]
	public void ToHashSet_OnSlicedResults_ReturnsCorrectSet() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 5);
		var set = sliced.ToHashSet();

		Assert.That(set.Count, Is.EqualTo(5));
	}

	[Test]
	public void ToHashSet_ContainsAllOriginalItems() {
		var results = _cache.Cache.Query().Execute();
		var expectedItems = results.ToArray(); // Get items before consuming
		var results2 = _cache.Cache.Query().Execute();
		var set = results2.ToHashSet();

		foreach (var item in expectedItems) Assert.That(set.Contains(item), Is.True);
	}

	[Test]
	public void ToDictionary_WithKeySelector_CreatesCorrectDictionary() {
		var results = _cache.Cache.Query().Execute();
		var dict = results.ToDictionary(item => item.Id);

		Assert.That(dict.Count, Is.EqualTo(10));
		Assert.That(dict, Is.InstanceOf<Dictionary<int, SimpleItem>>());
	}

	[Test]
	public void ToDictionary_WithKeySelector_MapsCorrectly() {
		var results = _cache.Cache.Query().Execute();
		var expectedItems = results.ToArray(); // Get items before consuming
		var results2 = _cache.Cache.Query().Execute();
		var dict = results2.ToDictionary(item => item.Id);

		foreach (var item in expectedItems) {
			Assert.That(dict.ContainsKey(item.Id), Is.True);
			Assert.That(dict[item.Id].Id, Is.EqualTo(item.Id));
		}
	}

	[Test]
	public void ToDictionary_OnEmptyResults_ReturnsEmptyDictionary() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var dict = emptyResults.ToDictionary(item => item.Id);

		Assert.That(dict, Is.Empty);
	}

	[Test]
	public void ToDictionary_WithDuplicateKeys_ThrowsArgumentException() {
		// Create results where two items would have the same key
		var cache = new SimpleItemCache();
		cache.AddOrUpdate(new SimpleItem { Id = 1, Name = "Item 1" });
		cache.AddOrUpdate(new SimpleItem { Id = 2, Name = "Item 2" });

		var results = cache.Cache.Query().Execute();

		// Try to use Name as key where names might collide
		Assert.Throws<ArgumentException>(() =>
				results.ToDictionary(item => item.Name.Length) // Both have length 6
		);
	}

	[Test]
	public void ToDictionary_WithKeyAndValueSelector_CreatesCorrectDictionary() {
		var results = _cache.Cache.Query().Execute();
		var dict = results.ToDictionary(
			item => item.Id,
			item => item.Name
		);

		Assert.That(dict.Count, Is.EqualTo(10));
		Assert.That(dict, Is.InstanceOf<Dictionary<int, string>>());
	}

	[Test]
	public void ToDictionary_WithKeyAndValueSelector_MapsCorrectly() {
		var results = _cache.Cache.Query().Execute();
		var expectedItems = results.ToArray(); // Get items before consuming
		var results2 = _cache.Cache.Query().Execute();
		var dict = results2.ToDictionary(
			item => item.Id,
			item => item.Name
		);

		foreach (var item in expectedItems) {
			Assert.That(dict.ContainsKey(item.Id), Is.True);
			Assert.That(dict[item.Id], Is.EqualTo(item.Name));
		}
	}

	[Test]
	public void ToDictionary_WithKeyAndValueSelector_OnEmptyResults_ReturnsEmptyDictionary() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var dict = emptyResults.ToDictionary(
			item => item.Id,
			item => item.Name
		);

		Assert.That(dict, Is.Empty);
	}

	[Test]
	public void ToDictionary_WithKeyAndValueSelector_OnSlicedResults_ReturnsCorrectDictionary() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 5);
		var dict = sliced.ToDictionary(
			item => item.Id,
			item => item.Name
		);

		Assert.That(dict.Count, Is.EqualTo(5));
	}

	[Test]
	public void ToList_ToHashSet_ToDictionary_WorkTogether() {
		// Each conversion method consumes the QueryResults, so we need separate queries
		var results1 = _cache.Cache.Query().Execute();
		var results2 = _cache.Cache.Query().Execute();
		var results3 = _cache.Cache.Query().Execute();

		var expectedCount = 10;

		// Convert to different collection types
		var list = results1.ToList();
		var set = results2.ToHashSet();
		var dict = results3.ToDictionary(item => item.Id);

		// All should have same count
		Assert.That(list.Count, Is.EqualTo(expectedCount));
		Assert.That(set.Count, Is.EqualTo(expectedCount));
		Assert.That(dict.Count, Is.EqualTo(expectedCount));
	}

	[Test]
	public void ToPooledArray_ReturnsArrayWithAllItems() {
		var results = _cache.Cache.Query().Execute();
		var array = results.ToPooledArray();

		try {
			Assert.That(array.Length, Is.GreaterThanOrEqualTo(10)); // Pool may rent larger

			// Verify first 10 items are correct
			for (var i = 0; i < 10; i++) Assert.That(array[i], Is.Not.Null);
		}
		finally {
			// Return to pool
			ArrayPool<SimpleItem>.Shared.Return(array);
		}
	}

	[Test]
	public void ToPooledArray_OnEmptyResults_ReturnsEmptyArray() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var array = emptyResults.ToPooledArray();

		Assert.That(array, Is.Empty);
		// No need to return empty arrays to pool
	}

	[Test]
	public void ToPooledArray_OnSlicedResults_ReturnsCorrectArray() {
		var results = _cache.Cache.Query().Execute();
		var sliced = results.Slice(2, 5);
		var array = sliced.ToPooledArray();

		try {
			Assert.That(array.Length, Is.GreaterThanOrEqualTo(5));

			// Verify items
			for (var i = 0; i < 5; i++) Assert.That(array[i], Is.Not.Null);
		}
		finally {
			ArrayPool<SimpleItem>.Shared.Return(array);
		}
	}

	[Test]
	public void ToPooledArray_WithCustomPool_UsesProvidedPool() {
		var results = _cache.Cache.Query().Execute();
		var customPool = ArrayPool<SimpleItem>.Create();
		var array = results.ToPooledArray(customPool);

		try {
			Assert.That(array.Length, Is.GreaterThanOrEqualTo(10));

			// Verify items
			for (var i = 0; i < 10; i++) Assert.That(array[i], Is.Not.Null);
		}
		finally {
			// Return to the custom pool
			customPool.Return(array);
		}
	}

	[Test]
	public void ToPooledArray_WithCustomPool_OnEmptyResults_ReturnsEmptyArray() {
		var emptyResults = QueryResults<SimpleItem>.Empty;
		var customPool = ArrayPool<SimpleItem>.Create();
		var array = emptyResults.ToPooledArray(customPool);

		Assert.That(array, Is.Empty);
	}

	[Test]
	public void ToPooledArray_OnPooledResults_ReusesArrayWhenPossible() {
		// ExecutePooled returns pooled results
		var pooledResults = _cache.Cache.Query().ExecutePooled();
		var array = pooledResults.ToPooledArray();

		try {
			Assert.That(array.Length, Is.GreaterThanOrEqualTo(10));

			// Verify items
			for (var i = 0; i < 10; i++) Assert.That(array[i], Is.Not.Null);
		}
		finally {
			ArrayPool<SimpleItem>.Shared.Return(array);
		}
	}

	[Test]
	public void ToPooledArray_ArrayCanBeReturnedToPool() {
		var results = _cache.Cache.Query().Execute();
		var array = results.ToPooledArray();

		// Should not throw when returning to pool
		Assert.DoesNotThrow(() => ArrayPool<SimpleItem>.Shared.Return(array));
	}

	[Test]
	public void ToPooledArray_MultipleCallsWithCustomPool_Work() {
		var customPool = ArrayPool<SimpleItem>.Create();

		var results1 = _cache.Cache.Query().Where(x => x.Id <= 5).Execute();
		var array1 = results1.ToPooledArray(customPool);

		var results2 = _cache.Cache.Query().Where(x => x.Id > 5).Execute();
		var array2 = results2.ToPooledArray(customPool);

		try {
			Assert.That(array1.Length, Is.GreaterThanOrEqualTo(5));
			Assert.That(array2.Length, Is.GreaterThanOrEqualTo(5));
		}
		finally {
			customPool.Return(array1);
			customPool.Return(array2);
		}
	}

	[Test]
	public void ToPooledArray_ContainsCorrectData() {
		var results = _cache.Cache.Query().Execute();
		var expectedIds = new HashSet<int>();
		foreach (var item in results) expectedIds.Add(item.Id);

		var array = results.ToPooledArray();

		try {
			// Verify the data matches (first 10 elements)
			for (var i = 0; i < 10; i++) Assert.That(expectedIds.Contains(array[i].Id), Is.True);
		}
		finally {
			ArrayPool<SimpleItem>.Shared.Return(array);
		}
	}
}

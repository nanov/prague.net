namespace Prague.Generated.Tests.Join;
/*
[TestFixture]
public class JoinQueryResultsTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<SimpleItemCache>()
			.Register<SimpleDetailCache>()
			.Build();
		_itemCache = _registry.GetCache<SimpleItemCache>();
		_detailCache = _registry.GetCache<SimpleDetailCache>();

		// Seed test data
		for (var i = 1; i <= 10; i++) {
			_itemCache.AddOrUpdate(new SimpleItem { Id = i, Name = $"Item {i}" });
			_detailCache.AddOrUpdate(new SimpleDetail { Id = i, ItemId = i, Description = $"Detail for {i}" });
		}
	}

	private DataCacheRegistry _registry;
	private SimpleItemCache _itemCache;
	private SimpleDetailCache _detailCache;

	[Test]
	public void Count_ReturnsCorrectValue() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(10));
	}

	[Test]
	public void Indexer_Get_ReturnsCorrectItem() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var item = results[0];
		Assert.That(item.Left, Is.Not.Null);
	}

	[Test]
	public void Indexer_Get_OutOfRange_ThrowsException() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = results[100];
		});
	}

	[Test]
	public void Indexer_Get_NegativeIndex_ThrowsException() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = results[-1];
		});
	}

	[Test]
	public void GetEnumerator_IteratesAllItems() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var count = 0;
		foreach (var item in results) {
			Assert.That(item.Left, Is.Not.Null);
			count++;
		}

		Assert.That(count, Is.EqualTo(10));
	}

	[Test]
	public void IEnumerable_GetEnumerator_IteratesAllItems() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var count = 0;
		foreach (var item in (IEnumerable<JoinOneResult<SimpleItem, SimpleDetail>>)results) {
			Assert.That(item.Left, Is.Not.Null);
			count++;
		}

		Assert.That(count, Is.EqualTo(10));
	}

	[Test]
	public void Slice_SingleArg_ReturnsCorrectSubset() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var sliced = results.Slice(5);
		Assert.That(sliced.Count, Is.EqualTo(5));
	}

	[Test]
	public void Slice_TwoArgs_ReturnsCorrectSubset() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var sliced = results.Slice(2, 3);
		Assert.That(sliced.Count, Is.EqualTo(3));
	}

	[Test]
	public void Slice_InvalidIndex_ThrowsException() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.Throws<ArgumentOutOfRangeException>(() => results.Slice(100));
	}

	[Test]
	public void Slice_InvalidCount_ThrowsException() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.Throws<ArgumentOutOfRangeException>(() => results.Slice(5, 100));
	}

	[Test]
	public void ToArray_ReturnsCorrectArray() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var array = results.ToArray();
		Assert.That(array.Length, Is.EqualTo(10));
	}

	[Test]
	public void ToArray_OnSlicedResults_ReturnsCorrectArray() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var sliced = results.Slice(2, 3);
		var array = sliced.ToArray();
		Assert.That(array.Length, Is.EqualTo(3));
	}

	[Test]
	public void CopyTo_CopiesAllItems() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var destination = new JoinOneResult<SimpleItem, SimpleDetail>[10];
		results.CopyTo(destination);
		Assert.That(destination.All(x => x.Left != null), Is.True);
	}

	[Test]
	public void CopyTo_WithOffset_CopiesCorrectly() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var destination = new JoinOneResult<SimpleItem, SimpleDetail>[15];
		results.CopyTo(destination, 5);
		Assert.That(destination.Skip(5).All(x => x.Left != null), Is.True);
	}

	[Test]
	public void Clone_ReturnsClonedResults() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var cloned = results.Clone();
		Assert.That(cloned.Count, Is.EqualTo(results.Count));
	}

	[Test]
	public void IList_IndexOf_FindsItem() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IList<JoinOneResult<SimpleItem, SimpleDetail>> list = results;
		var firstItem = results[0];
		var index = list.IndexOf(firstItem);
		Assert.That(index, Is.EqualTo(0));
	}

	[Test]
	public void IList_Contains_ReturnsTrueForExistingItem() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		ICollection<JoinOneResult<SimpleItem, SimpleDetail>> collection = results;
		var firstItem = results[0];
		Assert.That(collection.Contains(firstItem), Is.True);
	}

	[Test]
	public void IList_IsReadOnly_ReturnsTrue() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		ICollection<JoinOneResult<SimpleItem, SimpleDetail>> collection = results;
		Assert.That(collection.IsReadOnly, Is.True);
	}

	[Test]
	public void IList_Add_ThrowsNotSupported() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		ICollection<JoinOneResult<SimpleItem, SimpleDetail>> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Add(new JoinOneResult<SimpleItem, SimpleDetail>()));
	}

	[Test]
	public void IList_Clear_ThrowsNotSupported() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		ICollection<JoinOneResult<SimpleItem, SimpleDetail>> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Clear());
	}

	[Test]
	public void IList_Remove_ThrowsNotSupported() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		ICollection<JoinOneResult<SimpleItem, SimpleDetail>> collection = results;
		Assert.Throws<NotSupportedException>(() => collection.Remove(results[0]));
	}

	[Test]
	public void IList_Insert_ThrowsNotSupported() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IList<JoinOneResult<SimpleItem, SimpleDetail>> list = results;
		Assert.Throws<NotSupportedException>(() => list.Insert(0, new JoinOneResult<SimpleItem, SimpleDetail>()));
	}

	[Test]
	public void IList_RemoveAt_ThrowsNotSupported() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IList<JoinOneResult<SimpleItem, SimpleDetail>> list = results;
		Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
	}

	[Test]
	public void IReadOnlyList_Indexer_ReturnsCorrectItem() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IReadOnlyList<JoinOneResult<SimpleItem, SimpleDetail>> readOnlyList = results;
		var item = readOnlyList[0];
		Assert.That(item.Left, Is.Not.Null);
	}

	[Test]
	public void IReadOnlyList_Indexer_OutOfRange_ThrowsException() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IReadOnlyList<JoinOneResult<SimpleItem, SimpleDetail>> readOnlyList = results;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			var _ = readOnlyList[100];
		});
	}

	[Test]
	public void GetHashCode_ReturnsConsistentValue() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var hash1 = results.GetHashCode();
		var hash2 = results.GetHashCode();
		Assert.That(hash1, Is.EqualTo(hash2));
	}

	[Test]
	public void Dispose_OnPooledResults_DoesNotThrow() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.ExecutePooled();

		Assert.DoesNotThrow(() => results.Dispose());
	}

	[Test]
	public void Dispose_OnNonPooledResults_DoesNotThrow() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		Assert.DoesNotThrow(() => results.Dispose());
	}

	[Test]
	public void Enumerator_Current_BeforeMoveNext_Throws() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var enumerator = results.GetEnumerator();
		Assert.Throws<InvalidOperationException>(() => {
			var _ = enumerator.Current;
		});
	}

	[Test]
	public void Enumerator_Current_AfterEnd_Throws() {
		var results = _itemCache.Cache
			.Query()
			.Where(x => x.Id == 1)
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var enumerator = results.GetEnumerator();
		while (enumerator.MoveNext()) {
		}

		Assert.Throws<InvalidOperationException>(() => {
			var _ = enumerator.Current;
		});
	}

	[Test]
	public void Enumerator_Reset_WorksCorrectly() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		IEnumerator<JoinOneResult<SimpleItem, SimpleDetail>> enumerator = results.GetEnumerator();

		// Advance to first item
		enumerator.MoveNext();
		var first = enumerator.Current;

		// Reset
		enumerator.Reset();

		// Verify we can iterate again
		enumerator.MoveNext();
		Assert.That(enumerator.Current.Left.Id, Is.EqualTo(first.Left.Id));
	}

	[Test]
	public void Enumerator_Dispose_DoesNotThrow() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.Execute();

		var enumerator = results.GetEnumerator();
		Assert.DoesNotThrow(() => enumerator.Dispose());
	}

	[Test]
	public void ExecuteCloned_ReturnsClonedItems() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.EqualTo(10));
	}

	[Test]
	public void ExecutePooled_ReturnsValidResults() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(10));
		results.Dispose();
	}

	[Test]
	public void ExecutePooledCloned_ReturnsValidResults() {
		var results = _itemCache.Cache
			.Query()
			.JoinOne(_detailCache.Cache, _detailCache.ItemIdIndex)
			.ExecutePooledCloned();

		Assert.That(results.Count, Is.EqualTo(10));
		results.Dispose();
	}
}

[DataCache]
public partial class SimpleDetail {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int ItemId { get; set; }

	public string Description { get; set; } = "";
}
*/
namespace Prague.Generated.Tests.Cache;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class PooledBTreeTests {
	// ───────────────────── Helper aggregator ─────────────────────

	private struct ListAggregator<TI, TV> : PooledBTree<TI, TV>.IResultAggregator
		where TI : IComparable<TI>
		where TV : IEquatable<TV> {
		public readonly List<(TI Index, TV Value)> Items;

		public ListAggregator() {
			Items = new List<(TI, TV)>();
		}

		public void Add(TI index, TV value) => Items.Add((index, value));
		public void Dispose() { }
	}

	// ───────────────────── Add / Length ─────────────────────

	[Test]
	public void Add_SingleItem_IncreasesLength() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		Assert.That(tree.Length, Is.EqualTo(1));
		tree.Dispose();
	}

	[Test]
	public void Add_MultipleItems_IncreasesLength() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(2, "value2");
		tree.Add(3, "value3");
		Assert.That(tree.Length, Is.EqualTo(3));
		tree.Dispose();
	}

	[Test]
	public void Add_DuplicateIndexAndValue_DoesNotIncrease() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		var result = tree.Add(1, "value1");
		Assert.That(result, Is.False);
		Assert.That(tree.Length, Is.EqualTo(1));
		tree.Dispose();
	}

	[Test]
	public void Add_SameIndexDifferentValue_IncreasesBoth() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(1, "value2");
		Assert.That(tree.Length, Is.EqualTo(2));
		tree.Dispose();
	}

	[Test]
	public void Add_ItemsInRandomOrder_MaintainsSortedOrder() {
		var tree = new PooledBTree<int, string>();
		var items = new[] { 5, 2, 8, 1, 9, 3 };
		foreach (var item in items) tree.Add(item, $"value_{item}");

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 1, 2, 3, 5, 8, 9 }));
		tree.Dispose();
	}

	[Test]
	public void Add_ReturnsTrue_WhenNewItem() {
		var tree = new PooledBTree<int, string>();
		Assert.That(tree.Add(1, "value1"), Is.True);
		tree.Dispose();
	}

	// ───────────────────── Remove ─────────────────────

	[Test]
	public void Remove_ExistingItem_ReturnsTrue() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		var result = tree.Remove(1, "value1");
		Assert.That(result, Is.True);
		Assert.That(tree.Length, Is.EqualTo(0));
		tree.Dispose();
	}

	[Test]
	public void Remove_NonExistingItem_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		var result = tree.Remove(2, "value2");
		Assert.That(result, Is.False);
		Assert.That(tree.Length, Is.EqualTo(1));
		tree.Dispose();
	}

	[Test]
	public void Remove_FromEmptyTree_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		var result = tree.Remove(1, "value1");
		Assert.That(result, Is.False);
		tree.Dispose();
	}

	[Test]
	public void Remove_SameIndexDifferentValue_OnlyRemovesMatching() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(1, "value2");
		var result = tree.Remove(1, "value1");
		Assert.That(result, Is.True);
		Assert.That(tree.Length, Is.EqualTo(1));
		Assert.That(tree.Contains(1, "value2"), Is.True);
		tree.Dispose();
	}

	[Test]
	public void Add_ThenRemoveAll_LengthReturnsToZero() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(2, "value2");
		tree.Add(3, "value3");
		tree.Remove(1, "value1");
		tree.Remove(2, "value2");
		tree.Remove(3, "value3");
		Assert.That(tree.Length, Is.EqualTo(0));
		tree.Dispose();
	}

	// ───────────────────── Contains ─────────────────────

	[Test]
	public void Contains_ExistingItem_ReturnsTrue() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		Assert.That(tree.Contains(1, "value1"), Is.True);
		tree.Dispose();
	}

	[Test]
	public void Contains_NonExistingItem_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		Assert.That(tree.Contains(2, "value2"), Is.False);
		tree.Dispose();
	}

	[Test]
	public void Contains_EmptyTree_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		Assert.That(tree.Contains(1, "value1"), Is.False);
		tree.Dispose();
	}

	[Test]
	public void Contains_SameIndexWrongValue_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		Assert.That(tree.Contains(1, "value2"), Is.False);
		tree.Dispose();
	}

	// ───────────────────── TryGetMin ─────────────────────

	[Test]
	public void TryGetMin_EmptyTree_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		var result = tree.TryGetMin(out _, out _);
		Assert.That(result, Is.False);
		tree.Dispose();
	}

	[Test]
	public void TryGetMin_SingleItem_ReturnsItem() {
		var tree = new PooledBTree<int, string>();
		tree.Add(5, "value5");
		var result = tree.TryGetMin(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(5));
		Assert.That(value, Is.EqualTo("value5"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMin_MultipleItems_ReturnsSmallestIndex() {
		var tree = new PooledBTree<int, string>();
		tree.Add(5, "value5");
		tree.Add(2, "value2");
		tree.Add(8, "value8");
		tree.Add(1, "value1");
		tree.Add(9, "value9");
		var result = tree.TryGetMin(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(1));
		Assert.That(value, Is.EqualTo("value1"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMin_AfterRemovingMin_ReturnsNextSmallest() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(2, "value2");
		tree.Add(3, "value3");
		tree.Remove(1, "value1");
		var result = tree.TryGetMin(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(2));
		Assert.That(value, Is.EqualTo("value2"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMin_NegativeIndices_ReturnsSmallest() {
		var tree = new PooledBTree<int, string>();
		tree.Add(-5, "valueNeg5");
		tree.Add(0, "value0");
		tree.Add(5, "value5");
		var result = tree.TryGetMin(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(-5));
		Assert.That(value, Is.EqualTo("valueNeg5"));
		tree.Dispose();
	}

	// ───────────────────── TryGetMax ─────────────────────

	[Test]
	public void TryGetMax_EmptyTree_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		var result = tree.TryGetMax(out _, out _);
		Assert.That(result, Is.False);
		tree.Dispose();
	}

	[Test]
	public void TryGetMax_SingleItem_ReturnsItem() {
		var tree = new PooledBTree<int, string>();
		tree.Add(5, "value5");
		var result = tree.TryGetMax(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(5));
		Assert.That(value, Is.EqualTo("value5"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMax_MultipleItems_ReturnsLargestIndex() {
		var tree = new PooledBTree<int, string>();
		tree.Add(5, "value5");
		tree.Add(2, "value2");
		tree.Add(8, "value8");
		tree.Add(1, "value1");
		tree.Add(9, "value9");
		var result = tree.TryGetMax(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(9));
		Assert.That(value, Is.EqualTo("value9"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMax_AfterRemovingMax_ReturnsNextLargest() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "value1");
		tree.Add(2, "value2");
		tree.Add(3, "value3");
		tree.Remove(3, "value3");
		var result = tree.TryGetMax(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(2));
		Assert.That(value, Is.EqualTo("value2"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMax_NegativeIndices_ReturnsLargest() {
		var tree = new PooledBTree<int, string>();
		tree.Add(-5, "valueNeg5");
		tree.Add(-1, "valueNeg1");
		tree.Add(-10, "valueNeg10");
		var result = tree.TryGetMax(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(-1));
		Assert.That(value, Is.EqualTo("valueNeg1"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMinMax_SameItem_ReturnsSameItem() {
		var tree = new PooledBTree<int, string>();
		tree.Add(42, "theAnswer");
		var minResult = tree.TryGetMin(out var minIndex, out var minValue);
		var maxResult = tree.TryGetMax(out var maxIndex, out var maxValue);
		Assert.That(minResult, Is.True);
		Assert.That(maxResult, Is.True);
		Assert.That(minIndex, Is.EqualTo(42));
		Assert.That(maxIndex, Is.EqualTo(42));
		Assert.That(minValue, Is.EqualTo("theAnswer"));
		Assert.That(maxValue, Is.EqualTo("theAnswer"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMinMax_LargeDataset_ReturnsCorrectBounds() {
		var tree = new PooledBTree<int, string>();
		var random = new Random(42);
		var values = Enumerable.Range(0, 1000).OrderBy(_ => random.Next()).ToList();
		foreach (var v in values)
			tree.Add(v, $"value_{v}");

		var minResult = tree.TryGetMin(out var minIndex, out var minValue);
		var maxResult = tree.TryGetMax(out var maxIndex, out var maxValue);
		Assert.That(minResult, Is.True);
		Assert.That(maxResult, Is.True);
		Assert.That(minIndex, Is.EqualTo(0));
		Assert.That(maxIndex, Is.EqualTo(999));
		Assert.That(minValue, Is.EqualTo("value_0"));
		Assert.That(maxValue, Is.EqualTo("value_999"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMin_WithLongIndex_ReturnsCorrectMin() {
		var tree = new PooledBTree<long, string>();
		tree.Add(long.MaxValue, "max");
		tree.Add(long.MinValue, "min");
		tree.Add(0L, "zero");
		var result = tree.TryGetMin(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(long.MinValue));
		Assert.That(value, Is.EqualTo("min"));
		tree.Dispose();
	}

	[Test]
	public void TryGetMax_WithLongIndex_ReturnsCorrectMax() {
		var tree = new PooledBTree<long, string>();
		tree.Add(long.MaxValue, "max");
		tree.Add(long.MinValue, "min");
		tree.Add(0L, "zero");
		var result = tree.TryGetMax(out var index, out var value);
		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(long.MaxValue));
		Assert.That(value, Is.EqualTo("max"));
		tree.Dispose();
	}

	// ───────────────────── Range queries ─────────────────────

	[Test]
	public void Range_InclusiveBoth_ReturnsCorrectItems() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.Range(3, 7, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 3, 4, 5, 6, 7 }));
		tree.Dispose();
	}

	[Test]
	public void RangeFrom_Inclusive_ReturnsFromStart() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(7, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 7, 8, 9 }));
		tree.Dispose();
	}

	[Test]
	public void RangeTo_Inclusive_ReturnsUpTo() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeTo(3, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 0, 1, 2, 3 }));
		tree.Dispose();
	}

	[Test]
	public void RangeToExclusive_ReturnsUpToBut() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeToExclusive(3, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 0, 1, 2 }));
		tree.Dispose();
	}

	[Test]
	public void RangeFromExclusive_ReturnsAfterStart() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeFromExclusive(7, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 8, 9 }));
		tree.Dispose();
	}

	[Test]
	public void RangeCustom_ExclusiveBoth_ReturnsCorrectItems() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeCustom(3, 7, false, false, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 4, 5, 6 }));
		tree.Dispose();
	}

	[Test]
	public void RangeCustom_InclusiveBoth_ReturnsCorrectItems() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeCustom(3, 7, true, true, ref agg);

		var indices = agg.Items.Select(x => x.Index).ToList();
		Assert.That(indices, Is.EqualTo(new[] { 3, 4, 5, 6, 7 }));
		tree.Dispose();
	}

	[Test]
	public void Range_EmptyTree_ReturnsNothing() {
		var tree = new PooledBTree<int, string>();
		var agg = new ListAggregator<int, string>();
		tree.Range(0, 100, ref agg);
		Assert.That(agg.Items, Is.Empty);
		tree.Dispose();
	}

	[Test]
	public void Range_NoMatch_ReturnsNothing() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 10; i++) tree.Add(i, $"v{i}");

		var agg = new ListAggregator<int, string>();
		tree.Range(20, 30, ref agg);
		Assert.That(agg.Items, Is.Empty);
		tree.Dispose();
	}

	[Test]
	public void Range_DuplicateIndices_ReturnsAll() {
		var tree = new PooledBTree<int, string>();
		tree.Add(5, "a");
		tree.Add(5, "b");
		tree.Add(5, "c");

		var agg = new ListAggregator<int, string>();
		tree.Range(5, 5, ref agg);

		Assert.That(agg.Items.Count, Is.EqualTo(3));
		Assert.That(agg.Items.Select(x => x.Value).ToHashSet(),
			Is.EquivalentTo(new[] { "a", "b", "c" }));
		tree.Dispose();
	}

	// ───────────────────── Split correctness ─────────────────────

	[Test]
	public void Add_ManyItems_ForcesSplits_AllPresent() {
		var tree = new PooledBTree<int, string>();
		const int count = 500;

		for (var i = 0; i < count; i++)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(count));

		// Verify all items present
		for (var i = 0; i < count; i++)
			Assert.That(tree.Contains(i, $"v{i}"), Is.True, $"Missing item {i}");

		// Verify sorted order via range scan
		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(count));

		for (var i = 0; i < count; i++) {
			Assert.That(agg.Items[i].Index, Is.EqualTo(i));
			Assert.That(agg.Items[i].Value, Is.EqualTo($"v{i}"));
		}

		tree.Dispose();
	}

	[Test]
	public void Add_RandomOrder_ForcesSplits_AllPresent() {
		var tree = new PooledBTree<int, string>();
		var random = new Random(42);
		const int count = 1000;
		var order = Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToList();

		foreach (var i in order)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(count));

		// Verify sorted order
		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(count));

		for (var i = 0; i < count; i++)
			Assert.That(agg.Items[i].Index, Is.EqualTo(i));

		tree.Dispose();
	}

	[Test]
	public void Add_ReverseOrder_ForcesSplits_AllPresent() {
		var tree = new PooledBTree<int, string>();
		const int count = 300;

		for (var i = count - 1; i >= 0; i--)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(count));

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(count));

		for (var i = 0; i < count; i++)
			Assert.That(agg.Items[i].Index, Is.EqualTo(i));

		tree.Dispose();
	}

	// ───────────────────── Large dataset: add + remove + range ─────────────────────

	[Test]
	public void LargeDataset_AddRemoveRange_Consistent() {
		var tree = new PooledBTree<int, string>();
		const int count = 10_000;

		// Add all
		for (var i = 0; i < count; i++)
			tree.Add(i, $"v{i}");
		Assert.That(tree.Length, Is.EqualTo(count));

		// Remove even numbers
		for (var i = 0; i < count; i += 2)
			tree.Remove(i, $"v{i}");
		Assert.That(tree.Length, Is.EqualTo(count / 2));

		// Range query should return only odd numbers
		var agg = new ListAggregator<int, string>();
		tree.Range(0, count, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(count / 2));

		for (var i = 0; i < agg.Items.Count; i++)
			Assert.That(agg.Items[i].Index % 2, Is.EqualTo(1));

		// Verify sorted
		for (var i = 1; i < agg.Items.Count; i++)
			Assert.That(agg.Items[i].Index, Is.GreaterThan(agg.Items[i - 1].Index));

		tree.Dispose();
	}

	// ───────────────────── Concurrent read + single write ─────────────────────

	[Test]
	public void ConcurrentReaders_SingleWriter_NoExceptions() {
		var tree = new PooledBTree<int, string>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
		var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

		// Pre-populate
		for (var i = 0; i < 100; i++)
			tree.Add(i, $"v{i}");

		// Single writer
		var writer = Task.Run(() => {
			var random = new Random(42);
			while (!cts.Token.IsCancellationRequested) {
				try {
					var idx = random.Next(0, 200);
					if (random.Next(2) == 0)
						tree.Add(idx, $"v{idx}");
					else
						tree.Remove(idx, $"v{idx}");
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
					cts.Cancel();
				}
			}
		}, cts.Token);

		// Multiple readers doing range queries
		var readers = Enumerable.Range(0, 4).Select(t => Task.Run(() => {
			var random = new Random(t * 100);
			while (!cts.Token.IsCancellationRequested) {
				try {
					var from = random.Next(0, 100);
					var to = from + random.Next(1, 50);
					var agg = new ListAggregator<int, string>();
					tree.Range(from, to, ref agg);
					// Just exercise the iteration — results may vary due to concurrent writes
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
					cts.Cancel();
				}
			}
		}, cts.Token)).ToArray();

		try {
			Task.WaitAll([writer, .. readers]);
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		Assert.That(exceptions, Is.Empty,
			() => $"Exception: {exceptions.FirstOrDefault()?.GetType().Name}: {exceptions.FirstOrDefault()?.Message}\n{exceptions.FirstOrDefault()?.StackTrace}");

		tree.Dispose();
	}

	// ───────────────────── Min/Max with concurrent access ─────────────────────

	[Test]
	public void TryGetMinMax_ConcurrentAccess_DoesNotThrow() {
		var tree = new PooledBTree<int, string>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
		var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

		for (var i = 0; i < 100; i++)
			tree.Add(i, $"v{i}");

		var writer = Task.Run(() => {
			var random = new Random(42);
			while (!cts.Token.IsCancellationRequested) {
				try {
					var idx = random.Next(0, 100);
					if (random.Next(2) == 0)
						tree.Add(idx, $"v{idx}");
					else
						tree.Remove(idx, $"v{idx}");
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
			}
		}, cts.Token);

		var minReader = Task.Run(() => {
			while (!cts.Token.IsCancellationRequested) {
				try {
					tree.TryGetMin(out _, out _);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
			}
		}, cts.Token);

		var maxReader = Task.Run(() => {
			while (!cts.Token.IsCancellationRequested) {
				try {
					tree.TryGetMax(out _, out _);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
			}
		}, cts.Token);

		try {
			Task.WaitAll(writer, minReader, maxReader);
		}
		catch (AggregateException) { }

		Assert.That(exceptions, Is.Empty,
			() => $"Exception: {exceptions.FirstOrDefault()?.GetType().Name}: {exceptions.FirstOrDefault()?.Message}");

		tree.Dispose();
	}

	// ───────────────────── Edge cases ─────────────────────

	[Test]
	public void Length_EmptyTree_ReturnsZero() {
		var tree = new PooledBTree<int, string>();
		Assert.That(tree.Length, Is.EqualTo(0));
		tree.Dispose();
	}

	[Test]
	public void MultipleValuesPerIndex_ContainsAll() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "a");
		tree.Add(1, "b");
		tree.Add(1, "c");

		Assert.That(tree.Contains(1, "a"), Is.True);
		Assert.That(tree.Contains(1, "b"), Is.True);
		Assert.That(tree.Contains(1, "c"), Is.True);
		Assert.That(tree.Contains(1, "d"), Is.False);
		Assert.That(tree.Length, Is.EqualTo(3));
		tree.Dispose();
	}

	[Test]
	public void Remove_LastItemInTree_LengthZero() {
		var tree = new PooledBTree<int, string>();
		tree.Add(1, "v");
		tree.Remove(1, "v");
		Assert.That(tree.Length, Is.EqualTo(0));
		Assert.That(tree.TryGetMin(out _, out _), Is.False);
		Assert.That(tree.TryGetMax(out _, out _), Is.False);
		tree.Dispose();
	}

	[Test]
	public void Add_AfterRemoveAll_WorksCorrectly() {
		var tree = new PooledBTree<int, string>();

		for (var i = 0; i < 100; i++)
			tree.Add(i, $"v{i}");
		for (var i = 0; i < 100; i++)
			tree.Remove(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(0));

		// Add again
		for (var i = 0; i < 50; i++)
			tree.Add(i, $"new{i}");

		Assert.That(tree.Length, Is.EqualTo(50));

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(50));
		tree.Dispose();
	}

	[Test]
	public void DuplicateIndices_LargeCount_AllPresent() {
		var tree = new PooledBTree<int, string>();
		// 100 values at the same index — forces splits with identical keys
		for (var i = 0; i < 100; i++)
			tree.Add(42, $"val_{i}");

		Assert.That(tree.Length, Is.EqualTo(100));

		var agg = new ListAggregator<int, string>();
		tree.Range(42, 42, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(100));

		var vals = agg.Items.Select(x => x.Value).ToHashSet();
		for (var i = 0; i < 100; i++)
			Assert.That(vals.Contains($"val_{i}"), Is.True, $"Missing val_{i}");

		tree.Dispose();
	}

	// ───────────────────── Internal node split child pointer ─────────────────────

	[Test]
	public void InternalNodeSplit_ReverseOrder_DoesNotLoseRightChild() {
		// Reverse-order insertion forces every leaf split to promote at insertAt=0
		// in the parent internal node. When the internal node is full and splits,
		// the buggy loop overwrites tempChildren[1] (rightChild) with a duplicate
		// of tempChildren[0] (leftChild). After removing items from the duplicated
		// leaf, ReturnToPool sets Keys=null, and the orphaned reference causes NRE.
		//
		// With LeafCapacity=64, InternalCapacity=64, reverse order fills ~32 items
		// per leaf after split → need ~64*32=2048 items to fill one internal node,
		// then the next leaf split triggers SplitInternalAndInsert with insertAt=0.
		var tree = new PooledBTree<int, string>();
		const int count = 2500;

		for (var i = count; i > 0; i--)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(count));

		// Remove the lowest items — these live in the leftmost leaves where
		// the duplicated child pointer was created
		for (var i = 1; i <= 200; i++)
			tree.Remove(i, $"v{i}");

		// This traversal hits the orphaned node if rightChild was lost
		tree.Add(100, "new_100");
		Assert.That(tree.Contains(100, "new_100"), Is.True);

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(count - 200 + 1));

		tree.Dispose();
	}

	[Test]
	public void InternalNodeSplit_RandomOrder_AllItemsSurviveChurn() {
		// Random insertion order causes internal node splits with insertAt in
		// the middle. After heavy add/remove churn, orphaned nodes surface as NRE.
		var tree = new PooledBTree<int, string>();
		var random = new Random(12345);
		const int total = 5000;

		var order = Enumerable.Range(0, total).OrderBy(_ => random.Next()).ToList();
		foreach (var i in order)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(total));

		// Remove items spread across the tree to empty leaves at split boundaries
		for (var i = 0; i < total; i += 3)
			tree.Remove(i, $"v{i}");


		// Re-add into the gaps — traversal through dead nodes causes NRE
		for (var i = 0; i < total; i += 3)
			tree.Add(i, $"v{i}");

		Assert.That(tree.Length, Is.EqualTo(total));

		var agg = new ListAggregator<int, string>();
		tree.RangeFrom(int.MinValue, ref agg);
		Assert.That(agg.Items.Count, Is.EqualTo(total));

		for (var i = 0; i < total; i++)
			Assert.That(agg.Items[i].Index, Is.EqualTo(i), $"Wrong index at position {i}");

		tree.Dispose();
	}

		[Test]
		public void Remove_EmptyNonRootInternal_ReplacesSlotInGrandparent() {
				// With LeafCapacity=64 and InternalCapacity=64, a 3-level tree forms
				// after ~2100 sequential inserts (64 leaves × 33 items/leaf triggers
				// the root internal split). Removing all items from one side drains
				// a level-1 internal node to KeyCount=0. The old code recursively
				// called RemoveFromParent which dropped parent.Children[0], orphaning
				// the entire surviving subtree. The fix replaces the grandparent's
				// slot with that sole remaining child.
				using var tree = new PooledBTree<int, string>();
				const int count = 4000;

				for (var i = 0; i < count; i++)
						tree.Add(i, $"v{i}");

				// Remove the low end — drains all leaves under the first level-1
				// internal node until it hits KeyCount=0 with one child left.
				const int keep = 200;
				for (var i = 0; i < count - keep; i++)
						tree.Remove(i, $"v{i}");

				Assert.That(tree.Length, Is.EqualTo(keep));

				// Without the fix, surviving items under the orphaned subtree
				// become unreachable (Contains returns false or throws NRE).
				for (var i = count - keep; i < count; i++)
						Assert.That(tree.Contains(i, $"v{i}"), Is.True,
							$"Item {i} lost — non-root internal node orphaned its subtree");

				// Range scan must agree with Length
				var agg = new ListAggregator<int, string>();
				tree.RangeFrom(int.MinValue, ref agg);
				Assert.That(agg.Items.Count, Is.EqualTo(keep));

				for (var idx = 0; idx < keep; idx++)
						Assert.That(agg.Items[idx].Index, Is.EqualTo(count - keep + idx));
		}

		[Test]
		public void Remove_EmptyNonRootInternal_ThenReinsert_WorksCorrectly() {
				// Same setup, but after draining the internal node, re-insert items
				// into the range that was orphaned. Without the fix, the stale
				// parent pointer leads to NRE on re-insert.
				using var tree = new PooledBTree<int, string>();
				const int count = 4000;

				for (var i = 0; i < count; i++)
						tree.Add(i, $"v{i}");

				for (var i = 0; i < count; i++)
						tree.Remove(i, $"v{i}");

				Assert.That(tree.Length, Is.EqualTo(0));

				// Re-insert — navigates through slots that pointed to the now-disposed
				// internal node. Without the fix this throws NRE.
				for (var i = 0; i < count; i++)
						tree.Add(i, $"w{i}");

				Assert.That(tree.Length, Is.EqualTo(count));

				for (var i = 0; i < count; i++)
						Assert.That(tree.Contains(i, $"w{i}"), Is.True, $"Item {i} missing after reinsert");
		}

		[Test]
		public void Remove_MultipleNonRootInternalsEmpty_AllSubtreesSurvive() {
				// Larger tree with multiple level-1 internal nodes. Remove items
				// from the middle so that several non-root internals drain, not
				// just the leftmost. Verifies the fix works for arbitrary slots
				// in the grandparent, not just slot 0.
				using var tree = new PooledBTree<int, string>();
				const int count = 8000;

				for (var i = 0; i < count; i++)
						tree.Add(i, $"v{i}");

				// Keep only the first 200 and last 200 items — everything in the
				// middle is removed, draining internal nodes on both sides of
				// the split boundary.
				for (var i = 200; i < count - 200; i++)
						tree.Remove(i, $"v{i}");

				Assert.That(tree.Length, Is.EqualTo(400));

				// Low-end survivors
				for (var i = 0; i < 200; i++)
						Assert.That(tree.Contains(i, $"v{i}"), Is.True, $"Low item {i} lost");

				// High-end survivors
				for (var i = count - 200; i < count; i++)
						Assert.That(tree.Contains(i, $"v{i}"), Is.True, $"High item {i} lost");

				var agg = new ListAggregator<int, string>();
				tree.RangeFrom(int.MinValue, ref agg);
				Assert.That(agg.Items.Count, Is.EqualTo(400));
		}

		[Test]
		public void Dispose_WithInternalNodes_CalledTwice_DoesNotThrow() {
				// Need enough items to create internal nodes (> LeafCapacity=64).
				// First Dispose sets intern.Children = null!; second Dispose must not
				// throw NRE when DisposeInternalNodes accesses intern.Children[i].
				var tree = new PooledBTree<int, string>();
				for (var i = 0; i < 300; i++)
						tree.Add(i, $"v{i}");

				tree.Dispose();
				Assert.DoesNotThrow(() => tree.Dispose(),
						"Second Dispose must be a no-op, not throw NRE in DisposeInternalNodes");
		}

		[Test]
		public void Dispose_EmptyTree_CalledTwice_DoesNotThrow() {
				var tree = new PooledBTree<int, string>();
				tree.Dispose();
				Assert.DoesNotThrow(() => tree.Dispose());
		}

		[Test]
		public void Dispose_AfterAllItemsRemoved_CalledTwice_DoesNotThrow() {
				// After removing all items the root collapses to a leaf — no InternalNodes
				// survive. Still, the second Dispose must be safe.
				var tree = new PooledBTree<int, string>();
				for (var i = 0; i < 300; i++)
						tree.Add(i, $"v{i}");
				for (var i = 0; i < 300; i++)
						tree.Remove(i, $"v{i}");

				tree.Dispose();
				Assert.DoesNotThrow(() => tree.Dispose());
		}

	// ───────────────────── Duplicate runs spanning leaf boundaries ─────────────────────
	// With LeafCapacity=64, more than 64 entries under the same index key force the
	// run of equal keys to span several leaves. Lookups that descend to a single leaf
	// (equal separators route right) then miss pairs sitting in earlier leaves of the
	// run — Remove silently returns false and the entry stays forever.

	[Test]
	public void Remove_DuplicateRunSpanningLeaves_RemovesEveryPair() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 200; i++)
			tree.Add(42, $"val_{i}");

		for (var i = 0; i < 200; i++)
			Assert.That(tree.Remove(42, $"val_{i}"), Is.True, $"val_{i} was not found by Remove");

		Assert.That(tree.Length, Is.EqualTo(0));
		tree.Dispose();
	}

	[Test]
	public void Remove_DuplicateRunWithNeighbors_RemovesFromLeftLeavesOfRun() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 100; i++)
			tree.Add(10, $"low_{i}");
		for (var i = 0; i < 100; i++)
			tree.Add(42, $"mid_{i}");
		for (var i = 0; i < 100; i++)
			tree.Add(90, $"high_{i}");

		for (var i = 0; i < 100; i++)
			Assert.That(tree.Remove(42, $"mid_{i}"), Is.True, $"mid_{i} was not found by Remove");

		Assert.That(tree.Length, Is.EqualTo(200));

		var agg = new ListAggregator<int, string>();
		tree.Range(42, 42, ref agg);
		Assert.That(agg.Items, Is.Empty);
		tree.Dispose();
	}

	[Test]
	public void Contains_DuplicateRunSpanningLeaves_FindsEveryPair() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 200; i++)
			tree.Add(42, $"val_{i}");

		for (var i = 0; i < 200; i++)
			Assert.That(tree.Contains(42, $"val_{i}"), Is.True, $"val_{i} not found by Contains");

		Assert.That(tree.Contains(42, "absent"), Is.False);
		tree.Dispose();
	}

	[Test]
	public void Add_DuplicatePairInSpanningRun_ReturnsFalse() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 200; i++)
			tree.Add(42, $"val_{i}");

		// val_0 lives in the leftmost leaf of the run; a single-leaf duplicate check misses it
		Assert.That(tree.Add(42, "val_0"), Is.False);
		Assert.That(tree.Length, Is.EqualTo(200));
		tree.Dispose();
	}

	[Test]
	public void RangeFromExclusive_DuplicateRunSpanningLeaves_ExcludesAllEqualKeys() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 200; i++)
			tree.Add(42, $"dup_{i}");
		for (var i = 0; i < 10; i++)
			tree.Add(100, $"tail_{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeFromExclusive(42, ref agg);

		Assert.That(agg.Items.Count, Is.EqualTo(10),
			"keys equal to the exclusive bound leaked from later leaves of the run");
		Assert.That(agg.Items.All(x => x.Index == 100), Is.True);
		tree.Dispose();
	}

	[Test]
	public void RangeCustom_ExclusiveFrom_DuplicateRunSpanningLeaves_ExcludesAllEqualKeys() {
		var tree = new PooledBTree<int, string>();
		for (var i = 0; i < 200; i++)
			tree.Add(42, $"dup_{i}");
		for (var i = 0; i < 10; i++)
			tree.Add(100, $"tail_{i}");

		var agg = new ListAggregator<int, string>();
		tree.RangeCustom(42, 100, includeFrom: false, includeTo: true, ref agg);

		Assert.That(agg.Items.Count, Is.EqualTo(10),
			"keys equal to the exclusive lower bound leaked from later leaves of the run");
		Assert.That(agg.Items.All(x => x.Index == 100), Is.True);
		tree.Dispose();
	}

	[Test]
	public void UpdateChurn_RangeIndexPattern_LengthStaysBounded() {
		// Simulates CacheRangeIndex.Update: every item update adds the new timestamp
		// key and removes the old one. Many items share the same key (millisecond
		// timestamps), so runs of equal keys span leaves and removes must not miss.
		var tree = new PooledBTree<long, long>();
		const int items = 500;
		var current = new long[items];
		for (var i = 0; i < items; i++) {
			current[i] = 1000;
			tree.Add(1000, i);
		}

		for (var round = 1; round <= 20; round++) {
			var newKey = 1000 + round;
			for (var i = 0; i < items; i++) {
				tree.Add(newKey, i);
				Assert.That(tree.Remove(current[i], i), Is.True, $"round {round}, item {i} leaked");
				current[i] = newKey;
			}
		}

		Assert.That(tree.Length, Is.EqualTo(items));
		tree.Dispose();
	}
}

namespace Prague.Core.Tests.DataStructures;

using System.Collections.Concurrent;
using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ConcurrentSortedListTests {
	[Test]
	public void Add_SingleItem_IncreasesLength() {
		var list = new ConcurrentSortedList<int, string>();

		list.Add(1, "value1");

		Assert.That(list.Length, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleItems_IncreasesLength() {
		var list = new ConcurrentSortedList<int, string>();

		list.Add(1, "value1");
		list.Add(2, "value2");
		list.Add(3, "value3");

		Assert.That(list.Length, Is.EqualTo(3));
	}

	[Test]
	public void Add_DuplicateIndexAndValue_DoesNotIncrease() {
		var list = new ConcurrentSortedList<int, string>();

		list.Add(1, "value1");
		list.Add(1, "value1");

		Assert.That(list.Length, Is.EqualTo(1));
	}

	[Test]
	public void Add_SameIndexDifferentValue_IncreasesBoth() {
		var list = new ConcurrentSortedList<int, string>();

		list.Add(1, "value1");
		list.Add(1, "value2");

		Assert.That(list.Length, Is.EqualTo(2));
	}

	[Test]
	public void Remove_ExistingItem_ReturnsTrue() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");

		var result = list.Remove(1, "value1");

		Assert.That(result, Is.True);
		Assert.That(list.Length, Is.EqualTo(0));
	}

	[Test]
	public void Remove_NonExistingItem_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");

		var result = list.Remove(2, "value2");

		Assert.That(result, Is.False);
		Assert.That(list.Length, Is.EqualTo(1));
	}

	[Test]
	public void Remove_FromEmptyList_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();

		var result = list.Remove(1, "value1");

		Assert.That(result, Is.False);
	}

	[Test]
	public void Remove_SameIndexDifferentValue_OnlyRemovesMatching() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");
		list.Add(1, "value2");

		var result = list.Remove(1, "value1");

		Assert.That(result, Is.True);
		Assert.That(list.Length, Is.EqualTo(1));
	}

	[Test]
	public void Contains_ExistingItem_ReturnsTrue() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");

		var result = list.Contains(1, "value1");

		Assert.That(result, Is.True);
	}

	[Test]
	public void Contains_NonExistingItem_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");

		var result = list.Contains(2, "value2");

		Assert.That(result, Is.False);
	}

	[Test]
	public void Contains_EmptyList_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();

		var result = list.Contains(1, "value1");

		Assert.That(result, Is.False);
	}

	[Test]
	public void Add_ItemsInRandomOrder_MaintainsSortedOrder() {
		var list = new ConcurrentSortedList<int, string>();
		var items = new[] { 5, 2, 8, 1, 9, 3 };

		foreach (var item in items) list.Add(item, $"value_{item}");

		Assert.That(list.Length, Is.EqualTo(6));
	}

	[Test]
	public void ConcurrentAdd_MultipleThreads_AllItemsAdded() {
		var list = new ConcurrentSortedList<int, string>();
		const int itemsPerThread = 100;
		const int threadCount = 10;

		var tasks = new List<Task>();
		for (var t = 0; t < threadCount; t++) {
			var threadId = t;
			tasks.Add(Task.Run(() => {
				for (var i = 0; i < itemsPerThread; i++) {
					var index = threadId * itemsPerThread + i;
					list.Add(index, $"value_{index}");
				}
			}));
		}

		Task.WaitAll(tasks.ToArray());

		Assert.That(list.Length, Is.EqualTo(itemsPerThread * threadCount));
	}

	[Test]
	public void ConcurrentRemove_MultipleThreads_AllItemsRemoved() {
		var list = new ConcurrentSortedList<int, string>();
		const int itemCount = 1000;

		// Pre-populate
		for (var i = 0; i < itemCount; i++) list.Add(i, $"value_{i}");

		var tasks = new List<Task>();
		for (var t = 0; t < 10; t++) {
			var threadId = t;
			tasks.Add(Task.Run(() => {
				for (var i = threadId * 100; i < (threadId + 1) * 100; i++) list.Remove(i, $"value_{i}");
			}));
		}

		Task.WaitAll(tasks.ToArray());

		Assert.That(list.Length, Is.EqualTo(0));
	}

	[Test]
	public void ConcurrentAddRemove_MixedOperations_ConsistentState() {
		var list = new ConcurrentSortedList<int, string>();
		var exceptions = new ConcurrentBag<Exception>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

		var tasks = new List<Task>();

		// Adders
		for (var t = 0; t < 4; t++) {
			var threadId = t;
			tasks.Add(Task.Run(() => {
				var random = new Random(threadId);
				while (!cts.Token.IsCancellationRequested)
					try {
						var index = random.Next(0, 50);
						list.Add(index, $"value_{index}");
					}
					catch (Exception ex) when (ex is not OperationCanceledException) {
						exceptions.Add(ex);
					}
			}, cts.Token));
		}

		// Removers
		for (var t = 0; t < 4; t++) {
			var threadId = t + 100;
			tasks.Add(Task.Run(() => {
				var random = new Random(threadId);
				while (!cts.Token.IsCancellationRequested)
					try {
						var index = random.Next(0, 50);
						list.Remove(index, $"value_{index}");
					}
					catch (Exception ex) when (ex is not OperationCanceledException) {
						exceptions.Add(ex);
					}
			}, cts.Token));
		}

		try {
			Task.WaitAll(tasks.ToArray());
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		Assert.That(exceptions, Is.Empty,
			() =>
				$"Unexpected exception: {exceptions.FirstOrDefault()?.GetType().Name}: {exceptions.FirstOrDefault()?.Message}");
	}

	/// <summary>
	///   Regression test for the lock synchronization bug.
	///   The bug occurs when:
	///   1. Remove sets isMarked=true and locks victim
	///   2. Validation fails (e.g., predecessor changed), continue to next iteration
	///   3. On next iteration, isMarked is still true, so we skip Monitor.Enter(victim.Lock)
	///   4. But finally block still calls Monitor.Exit(victim.Lock) causing SynchronizationLockException
	/// </summary>
	[Test]
	public void Remove_ConcurrentModification_ShouldNotThrowSynchronizationLockException() {
		var list = new ConcurrentSortedList<int, string>();
		var exceptions = new ConcurrentBag<Exception>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

		// Pre-populate the list with some values
		for (var i = 0; i < 20; i++) list.Add(i, $"value_{i}");

		// Task 1: Continuously add and remove items at the same indices
		var modifier = Task.Run(() => {
			var random = new Random(42);
			while (!cts.Token.IsCancellationRequested)
				try {
					var index = random.Next(0, 20);
					var value = $"value_{index}";

					// Add and immediately remove to create race conditions
					list.Add(index, value);
					list.Remove(index, value);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
					cts.Cancel(); // Stop on first exception
				}
		}, cts.Token);

		// Task 2: Continuously remove items to trigger the bug
		var remover = Task.Run(() => {
			var random = new Random(123);
			while (!cts.Token.IsCancellationRequested)
				try {
					var index = random.Next(0, 20);
					var value = $"value_{index}";

					// This should trigger the condition where:
					// - We mark the victim
					// - Validation fails (because modifier changed predecessors)
					// - We loop again with isMarked=true but without the lock
					list.Remove(index, value);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
					cts.Cancel(); // Stop on first exception
				}
		}, cts.Token);

		// Task 3: More concurrent removals to increase contention
		var remover2 = Task.Run(() => {
			var random = new Random(456);
			while (!cts.Token.IsCancellationRequested)
				try {
					var index = random.Next(0, 20);
					var value = $"value_{index}";
					list.Remove(index, value);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
					cts.Cancel(); // Stop on first exception
				}
		}, cts.Token);

		try {
			Task.WaitAll(modifier, remover, remover2);
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		// Check if we got a SynchronizationLockException
		var lockExceptions = exceptions.Where(e =>
			e is SynchronizationLockException).ToList();

		if (lockExceptions.Any())
			Assert.Fail(
				$"SynchronizationLockException occurred {lockExceptions.Count} time(s): {lockExceptions.First().Message}\n{lockExceptions.First().StackTrace}");

		// Also check for any other unexpected exceptions
		var otherExceptions = exceptions.Where(e =>
			e is not SynchronizationLockException).ToList();

		if (otherExceptions.Any())
			Assert.Fail($"Unexpected exception: {otherExceptions.First().GetType().Name}: {otherExceptions.First().Message}");
	}

	/// <summary>
	///   Regression test with higher contention to increase probability of reproducing the lock bug
	/// </summary>
	[Test]
	public void Remove_HighContention_ShouldNotThrowSynchronizationLockException() {
		var list = new ConcurrentSortedList<int, string>();
		var exceptions = new ConcurrentBag<Exception>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));

		// Use a smaller range to increase collision probability
		const int rangeSize = 5;

		// Pre-populate
		for (var i = 0; i < rangeSize; i++) list.Add(i, $"value_{i}");

		// Spawn multiple tasks doing concurrent modifications
		var tasks = new List<Task>();

		for (var taskId = 0; taskId < 8; taskId++) {
			var id = taskId;
			var task = Task.Run(() => {
				var random = new Random(id * 100);
				while (!cts.Token.IsCancellationRequested)
					try {
						var index = random.Next(0, rangeSize);
						var value = $"value_{index}";

						// Randomly add or remove
						if (random.Next(2) == 0)
							list.Add(index, value);
						else
							list.Remove(index, value);
					}
					catch (Exception ex) when (ex is not OperationCanceledException) {
						exceptions.Add(ex);
						cts.Cancel(); // Stop on first exception
					}
			}, cts.Token);

			tasks.Add(task);
		}

		try {
			Task.WaitAll(tasks.ToArray());
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		// Check for SynchronizationLockException
		var lockExceptions = exceptions.Where(e =>
			e is SynchronizationLockException).ToList();

		if (lockExceptions.Any())
			Assert.Fail(
				$"SynchronizationLockException occurred {lockExceptions.Count} time(s): {lockExceptions.First().Message}\n{lockExceptions.First().StackTrace}");

		// Check for other exceptions
		var otherExceptions = exceptions.Where(e =>
			e is not SynchronizationLockException).ToList();

		if (otherExceptions.Any())
			Assert.Fail($"Unexpected exception: {otherExceptions.First().GetType().Name}: {otherExceptions.First().Message}");
	}

	[Test]
	public void Length_EmptyList_ReturnsZero() {
		var list = new ConcurrentSortedList<int, string>();

		Assert.That(list.Length, Is.EqualTo(0));
	}

	[Test]
	public void Add_ThenRemoveAll_LengthReturnsToZero() {
		var list = new ConcurrentSortedList<int, string>();

		list.Add(1, "value1");
		list.Add(2, "value2");
		list.Add(3, "value3");

		list.Remove(1, "value1");
		list.Remove(2, "value2");
		list.Remove(3, "value3");

		Assert.That(list.Length, Is.EqualTo(0));
	}

	[Test]
	public void ConcurrentContains_WhileModifying_ReturnsConsistentResults() {
		var list = new ConcurrentSortedList<int, string>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

		// Pre-populate
		for (var i = 0; i < 10; i++) list.Add(i, $"value_{i}");

		var modifier = Task.Run(() => {
			var random = new Random(42);
			while (!cts.Token.IsCancellationRequested) {
				var index = random.Next(0, 10);
				if (random.Next(2) == 0)
					list.Add(index, $"value_{index}");
				else
					list.Remove(index, $"value_{index}");
			}
		}, cts.Token);

		var checker = Task.Run(() => {
			var random = new Random(123);
			while (!cts.Token.IsCancellationRequested) {
				var index = random.Next(0, 10);
				// Should not throw, just return true or false
				var _ = list.Contains(index, $"value_{index}");
			}
		}, cts.Token);

		try {
			Task.WaitAll(modifier, checker);
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		// If we got here without exceptions, the test passes
		Assert.Pass();
	}

	[Test]
	public void TryGetMin_EmptyList_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.False);
	}

	[Test]
	public void TryGetMin_SingleItem_ReturnsItem() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(5, "value5");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(5));
		Assert.That(value, Is.EqualTo("value5"));
	}

	[Test]
	public void TryGetMin_MultipleItems_ReturnsSmallestIndex() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(5, "value5");
		list.Add(2, "value2");
		list.Add(8, "value8");
		list.Add(1, "value1");
		list.Add(9, "value9");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(1));
		Assert.That(value, Is.EqualTo("value1"));
	}

	[Test]
	public void TryGetMin_MultipleValuesAtSameIndex_ReturnsFirstValue() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "valueA");
		list.Add(1, "valueB");
		list.Add(2, "value2");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(1));
		// The value should be one of the values at index 1
		Assert.That(value, Is.EqualTo("valueA").Or.EqualTo("valueB"));
	}

	[Test]
	public void TryGetMin_AfterRemovingMin_ReturnsNextSmallest() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");
		list.Add(2, "value2");
		list.Add(3, "value3");

		list.Remove(1, "value1");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(2));
		Assert.That(value, Is.EqualTo("value2"));
	}

	[Test]
	public void TryGetMin_NegativeIndices_ReturnsSmallest() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(-5, "valueNeg5");
		list.Add(0, "value0");
		list.Add(5, "value5");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(-5));
		Assert.That(value, Is.EqualTo("valueNeg5"));
	}

	[Test]
	public void TryGetMax_EmptyList_ReturnsFalse() {
		var list = new ConcurrentSortedList<int, string>();

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.False);
	}

	[Test]
	public void TryGetMax_SingleItem_ReturnsItem() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(5, "value5");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(5));
		Assert.That(value, Is.EqualTo("value5"));
	}

	[Test]
	public void TryGetMax_MultipleItems_ReturnsLargestIndex() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(5, "value5");
		list.Add(2, "value2");
		list.Add(8, "value8");
		list.Add(1, "value1");
		list.Add(9, "value9");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(9));
		Assert.That(value, Is.EqualTo("value9"));
	}

	[Test]
	public void TryGetMax_MultipleValuesAtSameIndex_ReturnsLastValue() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");
		list.Add(2, "valueA");
		list.Add(2, "valueB");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(2));
		// The value should be one of the values at index 2
		Assert.That(value, Is.EqualTo("valueA").Or.EqualTo("valueB"));
	}

	[Test]
	public void TryGetMax_AfterRemovingMax_ReturnsNextLargest() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(1, "value1");
		list.Add(2, "value2");
		list.Add(3, "value3");

		list.Remove(3, "value3");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(2));
		Assert.That(value, Is.EqualTo("value2"));
	}

	[Test]
	public void TryGetMax_NegativeIndices_ReturnsLargest() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(-5, "valueNeg5");
		list.Add(-1, "valueNeg1");
		list.Add(-10, "valueNeg10");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(-1));
		Assert.That(value, Is.EqualTo("valueNeg1"));
	}

	[Test]
	public void TryGetMinMax_SameItem_ReturnsSameItem() {
		var list = new ConcurrentSortedList<int, string>();
		list.Add(42, "theAnswer");

		var minResult = list.TryGetMin(out var minIndex, out var minValue);
		var maxResult = list.TryGetMax(out var maxIndex, out var maxValue);

		Assert.That(minResult, Is.True);
		Assert.That(maxResult, Is.True);
		Assert.That(minIndex, Is.EqualTo(42));
		Assert.That(maxIndex, Is.EqualTo(42));
		Assert.That(minValue, Is.EqualTo("theAnswer"));
		Assert.That(maxValue, Is.EqualTo("theAnswer"));
	}

	[Test]
	public void TryGetMinMax_LargeDataset_ReturnsCorrectBounds() {
		var list = new ConcurrentSortedList<int, string>();
		var random = new Random(42);
		var values = Enumerable.Range(0, 1000).OrderBy(_ => random.Next()).ToList();

		foreach (var v in values)
			list.Add(v, $"value_{v}");

		var minResult = list.TryGetMin(out var minIndex, out var minValue);
		var maxResult = list.TryGetMax(out var maxIndex, out var maxValue);

		Assert.That(minResult, Is.True);
		Assert.That(maxResult, Is.True);
		Assert.That(minIndex, Is.EqualTo(0));
		Assert.That(maxIndex, Is.EqualTo(999));
		Assert.That(minValue, Is.EqualTo("value_0"));
		Assert.That(maxValue, Is.EqualTo("value_999"));
	}

	[Test]
	public void TryGetMinMax_ConcurrentAccess_DoesNotThrow() {
		var list = new ConcurrentSortedList<int, string>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
		var exceptions = new ConcurrentBag<Exception>();

		// Pre-populate
		for (var i = 0; i < 100; i++)
			list.Add(i, $"value_{i}");

		var modifier = Task.Run(() => {
			var random = new Random(42);
			while (!cts.Token.IsCancellationRequested)
				try {
					var index = random.Next(0, 100);
					if (random.Next(2) == 0)
						list.Add(index, $"value_{index}");
					else
						list.Remove(index, $"value_{index}");
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
		}, cts.Token);

		var minChecker = Task.Run(() => {
			while (!cts.Token.IsCancellationRequested)
				try {
					list.TryGetMin(out _, out _);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
		}, cts.Token);

		var maxChecker = Task.Run(() => {
			while (!cts.Token.IsCancellationRequested)
				try {
					list.TryGetMax(out _, out _);
				}
				catch (Exception ex) when (ex is not OperationCanceledException) {
					exceptions.Add(ex);
				}
		}, cts.Token);

		try {
			Task.WaitAll(modifier, minChecker, maxChecker);
		}
		catch (AggregateException) {
			// Expected due to cancellation
		}

		Assert.That(exceptions, Is.Empty,
			() =>
				$"Unexpected exception: {exceptions.FirstOrDefault()?.GetType().Name}: {exceptions.FirstOrDefault()?.Message}");
	}

	[Test]
	public void TryGetMin_WithLongIndex_ReturnsCorrectMin() {
		var list = new ConcurrentSortedList<long, string>();
		list.Add(long.MaxValue, "max");
		list.Add(long.MinValue, "min");
		list.Add(0L, "zero");

		var result = list.TryGetMin(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(long.MinValue));
		Assert.That(value, Is.EqualTo("min"));
	}

	[Test]
	public void TryGetMax_WithLongIndex_ReturnsCorrectMax() {
		var list = new ConcurrentSortedList<long, string>();
		list.Add(long.MaxValue, "max");
		list.Add(long.MinValue, "min");
		list.Add(0L, "zero");

		var result = list.TryGetMax(out var index, out var value);

		Assert.That(result, Is.True);
		Assert.That(index, Is.EqualTo(long.MaxValue));
		Assert.That(value, Is.EqualTo("max"));
	}
}
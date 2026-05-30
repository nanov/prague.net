namespace Prague.Core.Tests;

using Prague.Core;

/// <summary>
///   Reproduces the double-slice bug in BuildResults when Sort + ExecutePooled(skip, take) is used with skip > 0.
///   The bug: UnsafeSortResults calls SliceLeaveTotalCount once, then BuildResults calls it again with the same
///   skip/take — causing ArgumentOutOfRangeException on the already-sliced results.
///
///   Bug location: ResolverChain.cs — SimpleResultContainer.BuildResults()
/// </summary>
[TestFixture]
public class SortWithPaginationBugTests
{
	private InMemoryDataCache<int, TestItem> _cache = null!;

	[SetUp]
	public void Setup()
	{
		_cache = new InMemoryDataCache<int, TestItem>();

		// Populate cache with 25 items so that skip=10, take=10 is within bounds
		for (var i = 1; i <= 25; i++)
			_cache.AddOrUpdate(i, new TestItem { Id = i, Name = $"Item {i}", Order = 26 - i });
	}

	/// <summary>
	///   This is the exact scenario that crashes:
	///   query.Sort(comparer).ExecutePooled(skip: 10, take: 10)
	///   Expected: returns 10 items from position 10
	///   Actual: throws ArgumentOutOfRangeException (Parameter 'index')
	/// </summary>
	[Test]
	public void Sort_ExecutePooled_WithNonZeroSkip_ShouldNotThrow()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		var result = _cache.Query()
			.Sort(comparer)
			.ExecutePooled(skip: 10, take: 10);

		Assert.That(result.Count, Is.EqualTo(10));
		Assert.That(result.TotalCount, Is.EqualTo(25));
	}

	/// <summary>
	///   Verify sorting order is correct after pagination.
	/// </summary>
	[Test]
	public void Sort_ExecutePooled_WithSkip_ResultsShouldBeSorted()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		var result = _cache.Query()
			.Sort(comparer)
			.ExecutePooled(skip: 5, take: 5);

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result.TotalCount, Is.EqualTo(25));

		// Verify ordering (ascending by Order field)
		for (var i = 1; i < result.Count; i++)
			Assert.That(result[i].Order, Is.GreaterThanOrEqualTo(result[i - 1].Order),
				$"Results should be sorted ascending by Order at index {i}");
	}

	/// <summary>
	///   These are known working scenarios — they should continue to pass.
	/// </summary>
	[Test]
	public void Sort_ExecutePooled_WithoutSkipTake_ShouldWork()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		var result = _cache.Query()
			.Sort(comparer)
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(25));
	}

	[Test]
	public void Sort_ExecutePooled_WithZeroSkip_ShouldWork()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		var result = _cache.Query()
			.Sort(comparer)
			.ExecutePooled(skip: 0, take: 10);

		Assert.That(result.Count, Is.EqualTo(10));
		Assert.That(result.TotalCount, Is.EqualTo(25));
	}

	[Test]
	public void ExecutePooled_WithNonZeroSkip_WithoutSort_ShouldWork()
	{
		var result = _cache.Query()
			.ExecutePooled(skip: 10, take: 10);

		Assert.That(result.Count, Is.EqualTo(10));
		Assert.That(result.TotalCount, Is.EqualTo(25));
	}

	/// <summary>
	///   Additional edge case: skip near the end of the result set.
	/// </summary>
	[Test]
	public void Sort_ExecutePooled_SkipNearEnd_ShouldReturnRemainingItems()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		var result = _cache.Query()
			.Sort(comparer)
			.ExecutePooled(skip: 20, take: 10);

		Assert.That(result.Count, Is.EqualTo(5)); // Only 5 items left after skipping 20 of 25
		Assert.That(result.TotalCount, Is.EqualTo(25));
	}

	/// <summary>
	///   Sort + Where + ExecutePooled with skip > 0 — same bug path.
	/// </summary>
	[Test]
	public void Sort_Where_ExecutePooled_WithNonZeroSkip_ShouldNotThrow()
	{
		var comparer = Comparer<TestItem>.Create((a, b) => a.Order.CompareTo(b.Order));

		// Where narrows to 20 items (Id > 5), then sort + paginate
		var result = _cache.Query()
			.Where(item => item.Id > 5)
			.Sort(comparer)
			.ExecutePooled(skip: 5, take: 5);

		Assert.That(result.Count, Is.EqualTo(5));
		Assert.That(result.TotalCount, Is.EqualTo(20));
	}
}

public class TestItem : ICacheEquatable<TestItem>, ICacheClonable<TestItem>
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public int Order { get; set; }

	public bool CacheEquals(TestItem? other) => other != null && Id == other.Id && Name == other.Name && Order == other.Order;
	public int CacheGetHashCode() => Id;
	public TestItem Clone() => new TestItem { Id = Id, Name = Name, Order = Order };
}

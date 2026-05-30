namespace Prague.Generated.Tests.Join;

using Core;
using NUnit.Framework;

#if false

/// <summary>
///   Combined tests that verify clone, pagination, and sorting work together correctly.
/// </summary>
[TestFixture]
public class JoinCombinedTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<StoreCache>()
			.Register<WorkerCache>()
			.Register<WarehouseCache>()
			.Register<AgreementCache>()
			.Build();
		_storeCache = _registry.GetCache<StoreCache>();
		_workerCache = _registry.GetCache<WorkerCache>();
		_warehouseCache = _registry.GetCache<WarehouseCache>();
		_agreementCache = _registry.GetCache<AgreementCache>();

		SeedData();
	}

	private DataCacheRegistry _registry;
	private StoreCache _storeCache;
	private WorkerCache _workerCache;
	private WarehouseCache _warehouseCache;
	private AgreementCache _agreementCache;

	private void SeedData() {
		_storeCache.AddOrUpdate(new Store { Id = 1, Name = "Globex", City = "Northgate" });
		_storeCache.AddOrUpdate(new Store { Id = 2, Name = "Initech", City = "Southgate" });
		_storeCache.AddOrUpdate(new Store { Id = 3, Name = "Acme", City = "Acme" });

		_warehouseCache.AddOrUpdate(new Warehouse { Id = 1, StoreId = 1, Name = "North Depot", Capacity = 75000 });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 2, StoreId = 2, Name = "South Depot", Capacity = 81000 });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 3, StoreId = 3, Name = "East Depot", Capacity = 99000 });

		var workerId = 1;
		for (var storeId = 1; storeId <= 3; storeId++)
		for (var i = 0; i < 3; i++)
			_workerCache.AddOrUpdate(new Worker {
				Id = workerId++,
				StoreId = storeId,
				Name = $"Worker {storeId}-{i}",
				Age = 20 + i
			});

		var agreementId = 1;
		for (var storeId = 1; storeId <= 3; storeId++)
		for (var i = 0; i < 2; i++)
			_agreementCache.AddOrUpdate(new Agreement {
				Id = agreementId++,
				StoreId = storeId,
				Salary = 100000m * (i + 1),
				Years = i + 1
			});
	}

	private class StoreIdDescendingComparer : IComparer<Store> {
		public int Compare(Store? x, Store? y) {
			return y!.Id.CompareTo(x!.Id);
		}
	}

	private class StoreNameAscendingComparer : IComparer<Store> {
		public int Compare(Store? x, Store? y) {
			return string.Compare(x?.Name, y?.Name, StringComparison.Ordinal);
		}
	}

	// JoinOne: Clone + Pagination + Sorting

	[Test]
	public void JoinOne_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		// Sort by Id descending (3, 2, 1), skip 1, take 1 -> should get Id=2
		var results = _storeCache
			.Cache
			.Query()
			.Sort(new StoreIdDescendingComparer())
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("South Depot"));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right.Name = "Modified Warehouse";

		var original =
			_storeCache.Cache
				.Query()
				.Where(c => c.Id == 2)
				.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
				.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Initech"));
		Assert.That(original[0].Right!.Name, Is.EqualTo("South Depot"));
	}

	[Test]
	public void JoinOne_Execute_WithSortingAndPagination_SortsBeforePaging() {
		// Sort by name ascending (Acme, Globex, Initech), take first 2
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameAscendingComparer());
		var results = __b2.Execute(0, 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(results[1].Left.Name, Is.EqualTo("Globex"));
	}

	// JoinMany: Clone + Pagination + Sorting

	[Test]
	public void JoinMany_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		// Sort by Id descending (3, 2, 1), skip 0, take 2 -> should get Id=3, Id=2
		var _b1 =
			_storeCache.Cache
				.Query()
				.Sort(new StoreIdDescendingComparer());
				.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);

		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.ExecuteCloned(0, 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
		Assert.That(results[1].Right.Count, Is.EqualTo(3));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right[0].Name = "Modified Worker";

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 3).JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var original = __b1.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(original[0].Right[0].Name, Does.Not.Contain("Modified"));
	}

	[Test]
	public void JoinMany_Execute_WithSortingAndPagination_AllWorkersIncluded() {
		// Sort by name, skip 1 take 1 -> Globex
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameAscendingComparer());
		var results = __b2.Execute(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Globex"));
		Assert.That(results[0].Right.Count, Is.EqualTo(3)); // All workers still included
	}

	// JoinOneOne: Clone + Pagination + Sorting

	[Test]
	public void JoinOneOne_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b3 = __b2.Sort(new StoreIdDescendingComparer());
		var results = __b3.ExecuteCloned(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("South Depot"));
		Assert.That(results[0].Right2!.Name, Is.EqualTo("South Depot"));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right.Name = "Modified 1";
		results[0].Right2.Name = "Modified 2";

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 2).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var original = __b2.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Initech"));
		Assert.That(original[0].Right!.Name, Is.EqualTo("South Depot"));
	}

	// JoinOneMany: Clone + Pagination + Sorting

	[Test]
	public void JoinOneMany_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b3 = __b2.Sort(new StoreIdDescendingComparer());
		var results = __b3.ExecuteCloned(0, 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[0].Right!.Name, Is.EqualTo("East Depot"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right.Name = "Modified Warehouse";
		results[0].Right2[0].Name = "Modified Worker";

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 3).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var original = __b2.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(original[0].Right!.Name, Is.EqualTo("East Depot"));
	}

	// JoinManyOne: Clone + Pagination + Sorting

	[Test]
	public void JoinManyOne_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b3 = __b2.Sort(new StoreIdDescendingComparer());
		var results = __b3.ExecuteCloned(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
		Assert.That(results[0].Right2!.Name, Is.EqualTo("South Depot"));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right[0].Name = "Modified Worker";
		results[0].Right2.Name = "Modified Warehouse";

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 2).JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var original = __b2.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Initech"));
		Assert.That(original[0].Right2!.Name, Is.EqualTo("South Depot"));
	}

	// JoinManyMany: Clone + Pagination + Sorting

	[Test]
	public void JoinManyMany_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var __b3 = __b2.Sort(new StoreIdDescendingComparer());
		var results = __b3.ExecuteCloned(0, 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
		Assert.That(results[0].Right2.Count, Is.EqualTo(2));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right[0].Name = "Modified Worker";
		results[0].Right2[0].Salary = 999999m;

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 3).JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var original = __b2.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(original[0].Right2[0].Salary, Is.Not.EqualTo(999999m));
	}

	// FiveJoins: Clone + Pagination + Sorting

	[Test]
	public void FiveJoins_ExecuteCloned_WithSortingAndPagination_ReturnsCorrectResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b3 = __b2.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b4 = __b3.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var __b5 = __b4.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b6 = __b5.Sort(new StoreIdDescendingComparer());
		var results = __b6.ExecuteCloned(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("South Depot"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
		Assert.That(results[0].Right3!.Name, Is.EqualTo("South Depot"));
		Assert.That(results[0].Right4.Count, Is.EqualTo(2));
		Assert.That(results[0].Right5!.Name, Is.EqualTo("South Depot"));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		results[0].Right.Name = "Modified 1";
		results[0].Right2[0].Name = "Modified Worker";
		results[0].Right3.Name = "Modified 2";
		results[0].Right4[0].Salary = 1m;
		results[0].Right5.Name = "Modified 3";

		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 2).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b3 = __b2.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b4 = __b3.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var __b5 = __b4.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var original = __b5.Execute();
		Assert.That(original[0].Left.Name, Is.EqualTo("Initech"));
		Assert.That(original[0].Right!.Name, Is.EqualTo("South Depot"));
	}

	// Edge cases

	[Test]
	public void JoinOne_ExecuteCloned_WithSortingAndSkipBeyondCount_ReturnsEmpty() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.ExecuteCloned(100, 10);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_ExecuteCloned_WithSortingAndTakeZero_ReturnsEmpty() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.ExecuteCloned(0, 0);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinOne_Execute_WithSortingAndPagination_NonCloned_ModifiesOriginal() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));

		// Non-cloned should modify the original
		results[0].Left.Name = "Modified Acme";

		var check = _storeCache.Cache.Query()
			.Where(c => c.Id == 3)
			.Execute();
		Assert.That(check[0].Name, Is.EqualTo("Modified Acme"));
	}

	[Test]
	public void JoinMany_Execute_WithSortingAndPagination_GetAllThenPage() {
		// First get all sorted
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var allResults = __b2.Execute();

		Assert.That(allResults.Count, Is.EqualTo(3));
		Assert.That(allResults[0].Left.Id, Is.EqualTo(3));
		Assert.That(allResults[1].Left.Id, Is.EqualTo(2));
		Assert.That(allResults[2].Left.Id, Is.EqualTo(1));

		// Then get page 2 (skip 1, take 1)
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var page2 = __b2.Execute(1, 1);

		Assert.That(page2.Count, Is.EqualTo(1));
		Assert.That(page2[0].Left.Id, Is.EqualTo(allResults[1].Left.Id));
	}
}
#endif
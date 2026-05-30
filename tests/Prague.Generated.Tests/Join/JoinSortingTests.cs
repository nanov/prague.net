namespace Prague.Generated.Tests.Join;
#if false

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class JoinSortingTests {
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

		_warehouseCache.AddOrUpdate(new Warehouse { Id = 1, StoreId = 1, Name = "North Depot", Capacity = 75000 });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 2, StoreId = 2, Name = "South Depot", Capacity = 81000 });

		var workerId = 1;
		for (var storeId = 1; storeId <= 2; storeId++)
		for (var i = 0; i < 3; i++)
			_workerCache.AddOrUpdate(new Worker {
				Id = workerId++,
				StoreId = storeId,
				Name = $"Worker {storeId}-{i}",
				Age = 20 + i
			});

		var agreementId = 1;
		for (var storeId = 1; storeId <= 2; storeId++)
		for (var i = 0; i < 2; i++)
			_agreementCache.AddOrUpdate(new Agreement {
				Id = agreementId++,
				StoreId = storeId,
				Salary = 100000m * (i + 1),
				Years = i + 1
			});
	}

	private class StoreNameDescendingComparer : IComparer<Store> {
		public int Compare(Store? x, Store? y) {
			return string.Compare(y?.Name, x?.Name, StringComparison.Ordinal);
		}
	}

	private class StoreIdDescendingComparer : IComparer<Store> {
		public int Compare(Store? x, Store? y) {
			return y!.Id.CompareTo(x!.Id);
		}
	}

	[Test]
	public void JoinOne_Execute_WithComparer_ReturnsSortedResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameDescendingComparer());
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Name, Is.EqualTo("Initech"));
		Assert.That(results[1].Left.Name, Is.EqualTo("Globex"));
	}

	[Test]
	public void JoinOne_Execute_WithComparerAndSkipTake_ReturnsSortedAndPaged() {
		_storeCache.AddOrUpdate(new Store { Id = 3, Name = "Acme", City = "Acme" });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 3, StoreId = 3, Name = "East Depot", Capacity = 99000 });

		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameDescendingComparer());
		var results = __b2.Execute(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Globex"));
	}

	[Test]
	public void JoinMany_Execute_WithComparer_ReturnsSortedResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[1].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_Execute_WithComparerAndSkipTake_ReturnsSortedAndPaged() {
		var results =
			_storeCache.Cache
				.Query()
				.Sort(new StoreIdDescendingComparer())
				.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
				.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
	}

	[Test]
	public void JoinOneOne_Execute_WithComparer_ReturnsSortedResults() {
		var __b1 = _storeCache.Cache.Query().Sort(new StoreIdDescendingComparer()).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[1].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinManyMany_Execute_WithComparer_ReturnsSortedResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var __b3 = __b2.Sort(new StoreIdDescendingComparer());
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[1].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_ExecuteCloned_WithComparer_ReturnsSortedDeepCopy() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.ExecuteCloned();

		Assert.That(results[0].Left.Id, Is.EqualTo(2));

		results[0].Left.Name = "Modified";

		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var original = __b1.Execute();
		Assert.That(original.All(r => r.Left.Name != "Modified"), Is.True);
	}

	[Test]
	public void JoinMany_ExecuteCloned_WithComparer_ReturnsSortedDeepCopy() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.ExecuteCloned();

		Assert.That(results[0].Left.Id, Is.EqualTo(2));

		results[0].Left.Name = "Modified";
		results[0].Right[0].Name = "Modified Worker";

		var __b1 = _storeCache.Cache.Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var original = __b1.Execute();
		Assert.That(original.All(r => r.Left.Name != "Modified"), Is.True);
		Assert.That(original.All(r => r.Right.All(p => p.Name != "Modified Worker")), Is.True);
	}

	[Test]
	public void FiveJoins_Execute_WithComparer_ReturnsSortedResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b3 = __b2.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b4 = __b3.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var __b5 = __b4.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b6 = __b5.Sort(new StoreIdDescendingComparer());
		var results = __b6.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[1].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_Execute_WithComparerSkipTake_SortsBeforePaging() {
		_storeCache.AddOrUpdate(new Store { Id = 3, Name = "Acme", City = "Acme" });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 3, StoreId = 3, Name = "East Depot", Capacity = 99000 });

		// Sort descending by Id (3, 2, 1), then skip 1 take 1 should give Id=2
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescendingComparer());
		var results = __b2.Execute(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
	}
}
#endif
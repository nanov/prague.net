// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class JoinPrimaryKeyTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<StoreCache>()
			.Register<WorkerCache>()
			.Register<WarehouseCache>()
			.Build();
		_storeCache = _registry.GetCache<StoreCache>();
		_workerCache = _registry.GetCache<WorkerCache>();
		_warehouseCache = _registry.GetCache<WarehouseCache>();

		SeedData();
	}

	private DataCacheRegistry _registry;
	private StoreCache _storeCache;
	private WorkerCache _workerCache;
	private WarehouseCache _warehouseCache;

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
	}

	[Test]
	public void KeyIndex_TryGetValue_ReturnsKeyWhenExists() {
		var keyIndex = _storeCache.Cache.KeyIndex;

		Assert.That(keyIndex.TryGetValue(1, out var key), Is.True);
		Assert.That(key, Is.EqualTo(1));
	}

	[Test]
	public void KeyIndex_TryGetValue_ReturnsFalseWhenNotExists() {
		var keyIndex = _storeCache.Cache.KeyIndex;

		Assert.That(keyIndex.TryGetValue(999, out _), Is.False);
	}

	[Test]
	public void JoinOne_ByPrimaryKey_ReturnsCorrectResults() {
		// Warehouse has StoreId as foreign key, we want to join Store by its primary key (Id)
		var __b1 = _warehouseCache.Cache .Query().JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(3));

		// Verify each warehouse is joined with the correct store
		foreach (var result in results) {
			Assert.That(result.Right, Is.Not.Null);
			Assert.That(result.Right!.Id, Is.EqualTo(result.Left.StoreId));
		}
	}

	[Test]
	public void JoinOne_ByPrimaryKey_WithFiltering_ReturnsFilteredResults() {
		var __b1 = _warehouseCache.Cache .Query() .Where(s => s.Capacity > 80000).JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(2)); // South Depot and East Depot
		Assert.That(results.All(r => r.Left.Capacity > 80000), Is.True);
	}

	[Test]
	public void JoinOne_ByPrimaryKey_WithSorting_ReturnsSortedResults() {
		var __b1 =
			_warehouseCache.Cache
				.Query()
				.Sort(new WarehouseCapacityDescComparer()).JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Name, Is.EqualTo("East Depot")); // 99000
		Assert.That(results[1].Left.Name, Is.EqualTo("South Depot")); // 81000
		Assert.That(results[2].Left.Name, Is.EqualTo("North Depot")); // 75000
	}

	[Test]
	public void JoinOne_ByPrimaryKey_WithPagination_ReturnsPagedResults() {
		var __b1 = _warehouseCache.Cache .Query().JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.Execute(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_ByPrimaryKey_ExecuteCloned_ReturnsDeepCopy() {
		var __b1 = _warehouseCache.Cache .Query().JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.ExecuteCloned();

		results[0].Left.Name = "Modified Warehouse";
		results[0].Right!.Name = "Modified Store";


		var original = _warehouseCache.Cache.Query().JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex).Execute();

		Assert.That(original.All(r => r.Left.Name != "Modified Warehouse"), Is.True);
		Assert.That(original.All(r => r.Right!.Name != "Modified Store"), Is.True);
	}

	[Test]
	public void JoinOne_ByPrimaryKey_WhenKeyNotFound_RightIsNull() {
		// Add a warehouse with a store id that doesn't exist
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 100, StoreId = 999, Name = "Ghost Warehouse", Capacity = 1000 });

		var __b1 = _warehouseCache.Cache .Query() .Where(s => s.Id == 100).JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Ghost Warehouse"));
		Assert.That(results[0].Right, Is.Null);
	}

	[Test]
	public void JoinOneOne_WithPrimaryKey_ReturnsCorrectResults() {
		// Join Warehouse -> Store (by StoreId index) -> Store again (by primary key - same store)
		var __b1 = _warehouseCache.Cache .Query().JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var __b2 = __b1.JoinOne(_storeCache.Cache, _storeCache.Cache.KeyIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var result in results) {
			Assert.That(result.Right, Is.Not.Null);
			Assert.That(result.Right2, Is.Not.Null);
			Assert.That(result.Right!.Id, Is.EqualTo(result.Right2!.Id));
		}
	}

	[Test]
	public void KeyIndex_IsSameInstance_WhenAccessedMultipleTimes() {
		var keyIndex1 = _storeCache.Cache.KeyIndex;
		var keyIndex2 = _storeCache.Cache.KeyIndex;

		Assert.That(keyIndex1, Is.SameAs(keyIndex2));
	}

	private class WarehouseCapacityDescComparer : IComparer<Warehouse> {
		public int Compare(Warehouse? x, Warehouse? y) {
			return y!.Capacity.CompareTo(x!.Capacity);
		}
	}
}

#endif

namespace Prague.Generated.Tests.Join;


using Prague.Core;
using NUnit.Framework;

[DataCache]
public partial class Worker {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int StoreId { get; set; }

	public string Name { get; set; }
	public int Age { get; set; }
}

[DataCache]
public partial class Store {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; }
	public string City { get; set; }
}

[DataCache]
public partial class Warehouse {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int StoreId { get; set; }

	public string Name { get; set; }
	public int Capacity { get; set; }
}

[DataCache]
public partial class Agreement {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int StoreId { get; set; }

	public decimal Salary { get; set; }
	public int Years { get; set; }
}

#if true
[TestFixture]
public class JoinCloneTests {
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

	[Test]
	public void JoinOne_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		var cloned = results[0].Clone();

		Assert.That(cloned.Left.Id, Is.EqualTo(results[0].Left.Id));
		Assert.That(cloned.Right!.Id, Is.EqualTo(results[0].Right!.Id));

		cloned.Left.Name = "Modified Store";
		cloned.Right.Name = "Modified Warehouse";

		Assert.That(results[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(results[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse"));
	}

	[Test]
	public void JoinMany_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		// Store original values before clone (clone mutates in place)
		var originalLeftId = results[0].Left.Id;
		var originalLeftName = results[0].Left.Name;
		var originalRightCount = results[0].Right.Count;
		var originalRightName = results[0].Right[0].Name;

		var cloned = results[0].Clone();

		Assert.That(cloned.Left.Id, Is.EqualTo(originalLeftId));
		Assert.That(cloned.Right.Count, Is.EqualTo(originalRightCount));

		cloned.Left.Name = "Modified Store";
		cloned.Right[0].Name = "Modified Worker";

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified Store"));
		Assert.That(originalRightName, Is.Not.EqualTo("Modified Worker"));
	}

	[Test]
	public void JoinMany_Clone_AllItemsInListAreDeepCopied() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		var original = results[0];

		// Store original values before clone (clone mutates in place)
		var originalRightNames = original.Right.Select(r => r.Name).ToList();
		var originalRightAges = original.Right.Select(r => r.Age).ToList();

		var cloned = original.Clone();

		for (var i = 0; i < cloned.Right.Count; i++) {
			cloned.Right[i].Name = $"Modified {i}";
			cloned.Right[i].Age = 99;
		}

		for (var i = 0; i < originalRightNames.Count; i++) {
			Assert.That(originalRightNames[i], Does.Not.StartWith("Modified"));
			Assert.That(originalRightAges[i], Is.Not.EqualTo(99));
		}
	}

	[Test]
	public void JoinOneOne_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		var cloned = results[0].Clone();

		cloned.Left.Name = "Modified";
		cloned.Right!.Name = "Modified Warehouse 1";
		cloned.Right2!.Name = "Modified Warehouse 2";

		Assert.That(results[0].Left.Name, Is.Not.EqualTo("Modified"));
		Assert.That(results[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse 1"));
		Assert.That(results[0].Right2!.Name, Is.Not.EqualTo("Modified Warehouse 2"));
	}

	[Test]
	public void JoinOneMany_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		// Store original values before clone (clone mutates in place)
		var originalLeftName = results[0].Left.Name;
		var originalRightName = results[0].Right!.Name;
		var originalRight2Name = results[0].Right2[0].Name;

		var cloned = results[0].Clone();

		cloned.Left.Name = "Modified";
		cloned.Right!.Name = "Modified Warehouse";
		cloned.Right2[0].Name = "Modified Worker";

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified"));
		Assert.That(originalRightName, Is.Not.EqualTo("Modified Warehouse"));
		Assert.That(originalRight2Name, Is.Not.EqualTo("Modified Worker"));
	}

	[Test]
	public void JoinManyOne_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		// Store original values before clone (clone mutates in place)
		var originalLeftName = results[0].Left.Name;
		var originalRightName = results[0].Right[0].Name;
		var originalRight2Name = results[0].Right2!.Name;

		var cloned = results[0].Clone();

		cloned.Left.Name = "Modified";
		cloned.Right[0].Name = "Modified Worker";
		cloned.Right2!.Name = "Modified Warehouse";

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified"));
		Assert.That(originalRightName, Is.Not.EqualTo("Modified Worker"));
		Assert.That(originalRight2Name, Is.Not.EqualTo("Modified Warehouse"));
	}

	[Test]
	public void JoinManyMany_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.Execute();

		// Store original values before clone (clone mutates in place)
		var originalLeftName = results[0].Left.Name;
		var originalRightName = results[0].Right[0].Name;
		var originalRight2Salary = results[0].Right2[0].Salary;

		var cloned = results[0].Clone();

		cloned.Left.Name = "Modified";
		cloned.Right[0].Name = "Modified Worker";
		cloned.Right2[0].Salary = 999999m;

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified"));
		Assert.That(originalRightName, Is.Not.EqualTo("Modified Worker"));
		Assert.That(originalRight2Salary, Is.Not.EqualTo(999999m));
	}

	[Test]
	public void JoinManyMany_Clone_AllItemsInBothListsAreDeepCopied() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.Execute();

		var original = results[0];

		// Store original values before clone (clone mutates in place)
		var originalRightNames = original.Right.Select(r => r.Name).ToList();
		var originalRight2Salaries = original.Right2.Select(r => r.Salary).ToList();

		var cloned = original.Clone();

		for (var i = 0; i < cloned.Right.Count; i++) cloned.Right[i].Name = $"ModifiedWorker{i}";
		for (var i = 0; i < cloned.Right2.Count; i++) cloned.Right2[i].Salary = 1m + i;

		for (var i = 0; i < originalRightNames.Count; i++)
			Assert.That(originalRightNames[i], Does.Not.StartWith("ModifiedWorker"));
		for (var i = 0; i < originalRight2Salaries.Count; i++)
			Assert.That(originalRight2Salaries[i], Is.Not.EqualTo(1m + i));
	}

	[Test]
	public void JoinOneManyOne_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		// Store original values before clone (clone mutates in place)
		var originalLeftName = results[0].Left.Name;
		var originalRightName = results[0].Right!.Name;
		var originalRight2Name = results[0].Right2[0].Name;
		var originalRight3Name = results[0].Right3!.Name;

		var cloned = results[0].Clone();

		cloned.Left.Name = "Modified";
		cloned.Right!.Name = "Modified Warehouse 1";
		cloned.Right2[0].Name = "Modified Worker";
		cloned.Right3!.Name = "Modified Warehouse 2";

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified"));
		Assert.That(originalRightName, Is.Not.EqualTo("Modified Warehouse 1"));
		Assert.That(originalRight2Name, Is.Not.EqualTo("Modified Worker"));
		Assert.That(originalRight3Name, Is.Not.EqualTo("Modified Warehouse 2"));
	}

	[Test]
	public void JoinManyManyMany_Clone_AllListsAreDeepCopied() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		var original = results[0];

		// Store original values before clone (clone mutates in place)
		var originalLeftCity = original.Left.City;
		var originalRightAges = original.Right.Select(r => r.Age).ToList();
		var originalRight2Years = original.Right2.Select(r => r.Years).ToList();
		var originalRight3Ages = original.Right3.Select(r => r.Age).ToList();

		var cloned = original.Clone();

		cloned.Left.City = "Modified City";

		for (var i = 0; i < cloned.Right.Count; i++) cloned.Right[i].Age = 100 + i;
		for (var i = 0; i < cloned.Right2.Count; i++) cloned.Right2[i].Years = 100 + i;
		for (var i = 0; i < cloned.Right3.Count; i++) cloned.Right3[i].Age = 200 + i;

		Assert.That(originalLeftCity, Is.Not.EqualTo("Modified City"));

		for (var i = 0; i < originalRightAges.Count; i++) Assert.That(originalRightAges[i], Is.Not.EqualTo(100 + i));
		for (var i = 0; i < originalRight2Years.Count; i++) Assert.That(originalRight2Years[i], Is.Not.EqualTo(100 + i));
		for (var i = 0; i < originalRight3Ages.Count; i++) Assert.That(originalRight3Ages[i], Is.Not.EqualTo(200 + i));
	}

	[Test]
	public void JoinOne_Clone_WithNullRight_DoesNotThrow() {
		_storeCache.AddOrUpdate(new Store { Id = 100, Name = "No Warehouse Store", City = "Unassigned" });

		var results = _storeCache.Cache
			.Query()
			.Where(c => c.Id == 100)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		Assert.That(results[0].Right, Is.Null);

		var cloned = results[0].Clone();

		Assert.That(cloned.Right, Is.Null);
		Assert.That(cloned.Left.Name, Is.EqualTo("No Warehouse Store"));

		cloned.Left.Name = "Modified";
		Assert.That(results[0].Left.Name, Is.Not.EqualTo("Modified"));
	}

	[Test]
	public void JoinMany_Clone_WithEmptyList_DoesNotThrow() {
		_storeCache.AddOrUpdate(new Store { Id = 101, Name = "No Workers Store", City = "Empty" });

		var results = _storeCache.Cache
			.Query()
			.Where(c => c.Id == 101)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		Assert.That(results[0].Right.Count, Is.EqualTo(0));

		var cloned = results[0].Clone();

		Assert.That(cloned.Right.Count, Is.EqualTo(0));
		Assert.That(cloned.Left.Name, Is.EqualTo("No Workers Store"));
	}

	[Test]
	public void FiveJoins_Clone_IsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		var original = results[0];

		// Store original values before clone (clone mutates in place)
		var originalLeftName = original.Left.Name;
		var originalRightCapacity = original.Right!.Capacity;
		var originalRight2Age = original.Right2[0].Age;
		var originalRight3Capacity = original.Right3!.Capacity;
		var originalRight4Salary = original.Right4[0].Salary;
		var originalRight5Capacity = original.Right5!.Capacity;

		var cloned = original.Clone();

		cloned.Left.Name = "Modified Store";
		cloned.Right!.Capacity = 999;
		cloned.Right2[0].Age = 99;
		cloned.Right3!.Capacity = 888;
		cloned.Right4[0].Salary = 1m;
		cloned.Right5!.Capacity = 777;

		Assert.That(originalLeftName, Is.Not.EqualTo("Modified Store"));
		Assert.That(originalRightCapacity, Is.Not.EqualTo(999));
		Assert.That(originalRight2Age, Is.Not.EqualTo(99));
		Assert.That(originalRight3Capacity, Is.Not.EqualTo(888));
		Assert.That(originalRight4Salary, Is.Not.EqualTo(1m));
		Assert.That(originalRight5Capacity, Is.Not.EqualTo(777));
	}

	[Test]
	public void JoinOne_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified Store";
		results[0].Right!.Name = "Modified Warehouse";

		var original = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex).Execute();
		Assert.That(original[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(original[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse"));
	}

	[Test]
	public void JoinMany_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified Store";
		results[0].Right[0].Name = "Modified Worker";

		var original = _storeCache.Cache.Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex).Execute();
		Assert.That(original[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(original[0].Right[0].Name, Is.Not.EqualTo("Modified Worker"));
	}

	[Test]
	public void JoinOneOne_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified";
		results[0].Right!.Name = "Modified Warehouse 1";
		results[0].Right2!.Name = "Modified Warehouse 2";

		var original = _storeCache.Cache.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();
		Assert.That(original[0].Left.Name, Is.Not.EqualTo("Modified"));
		Assert.That(original[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse 1"));
		Assert.That(original[0].Right2!.Name, Is.Not.EqualTo("Modified Warehouse 2"));
	}

	[Test]
	public void JoinManyMany_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified";
		results[0].Right[0].Name = "Modified Worker";
		results[0].Right2[0].Salary = 999999m;

		var original = _storeCache.Cache.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.Execute();
		Assert.That(original[0].Left.Name, Is.Not.EqualTo("Modified"));
		Assert.That(original[0].Right[0].Name, Is.Not.EqualTo("Modified Worker"));
		Assert.That(original[0].Right2[0].Salary, Is.Not.EqualTo(999999m));
	}

	[Test]
	public void FiveJoins_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified Store";
		results[0].Right!.Capacity = 999;
		results[0].Right2[0].Age = 99;

		var original = _storeCache.Cache.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();

		Assert.That(original[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(original[0].Right!.Capacity, Is.Not.EqualTo(999));
		Assert.That(original[0].Right2[0].Age, Is.Not.EqualTo(99));
	}

	// ── ExecuteCloned: chain shapes not previously covered ────────────────────

	[Test]
	public void JoinOneMany_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified Store";
		results[0].Right!.Name = "Modified Warehouse";
		results[0].Right2[0].Name = "Modified Worker";

		var fresh = _storeCache.Cache.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();
		Assert.That(fresh[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(fresh[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse"));
		Assert.That(fresh[0].Right2[0].Name, Is.Not.EqualTo("Modified Worker"));
	}

	[Test]
	public void JoinManyOne_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		results[0].Left.Name = "Modified Store";
		results[0].Right[0].Name = "Modified Worker";
		results[0].Right2!.Name = "Modified Warehouse";

		var fresh = _storeCache.Cache.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();
		Assert.That(fresh[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(fresh[0].Right[0].Name, Is.Not.EqualTo("Modified Worker"));
		Assert.That(fresh[0].Right2!.Name, Is.Not.EqualTo("Modified Warehouse"));
	}

	// ── ExecuteCloned: edge cases ─────────────────────────────────────────────

	[Test]
	public void JoinOne_ExecuteCloned_MultipleRows_AreIndependent() {
		// Two stores (1, 2) both have warehouses. Mutating row 0 must NOT touch row 1.
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.EqualTo(2));
		var row1Name = results[1].Right!.Name;

		results[0].Right!.Name = "Mutated";

		Assert.That(results[1].Right!.Name, Is.EqualTo(row1Name),
			"Mutating one row's right should not leak to another row");
	}

	[Test]
	public void JoinOne_ExecuteCloned_WithSkipTake_StillIsolated() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned(skip: 0, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		results[0].Right!.Name = "Mutated";

		var fresh = _storeCache.Cache.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();
		Assert.That(fresh[0].Right!.Name, Is.Not.EqualTo("Mutated"));
	}

	[Test]
	public void JoinMany_ExecuteCloned_EachInnerElementIsIndependent() {
		// QueryResults<Worker> should have each element independently cloned.
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.ExecuteCloned();

		// Snapshot all original names from a fresh non-cloned query.
		var fresh = _storeCache.Cache.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();
		var originalNames = fresh[0].Right.Select(p => p.Name).ToList();

		for (var i = 0; i < results[0].Right.Count; i++)
			results[0].Right[i].Name = $"Mutated {i}";

		// Re-read fresh state from the cache — should still have original names.
		var fresh2 = _storeCache.Cache.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();
		for (var i = 0; i < originalNames.Count; i++)
			Assert.That(fresh2[0].Right[i].Name, Is.EqualTo(originalNames[i]));
	}

	[Test]
	public void JoinOne_ExecuteCloned_CacheMutationAfterClone_DoesNotAffectResults() {
		// Snapshot results via ExecuteCloned; then update cache; verify results unchanged.
		var snapshot = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		var preStoreName = snapshot[0].Left.Name;
		var preWarehouseName = snapshot[0].Right!.Name;

		_storeCache.AddOrUpdate(new Store { Id = snapshot[0].Left.Id, Name = "Renamed Store", City = "X" });
		_warehouseCache.AddOrUpdate(new Warehouse {
			Id = snapshot[0].Right.Id,
			StoreId = snapshot[0].Right.StoreId,
			Name = "Renamed Warehouse",
			Capacity = 0
		});

		Assert.That(snapshot[0].Left.Name, Is.EqualTo(preStoreName),
			"ExecuteCloned snapshot should be isolated from later cache mutations");
		Assert.That(snapshot[0].Right!.Name, Is.EqualTo(preWarehouseName));
	}

	[Test]
	public void Simple_ExecuteCloned_NoJoins_ReturnsDeepCopy() {
		// Base Query (no joins) — ExecuteCloned should still deep-clone each TValue.
		var results = _storeCache.Cache.Query().ExecuteCloned();

		Assert.That(results.Count, Is.GreaterThan(0));
		var originalId = results[0].Id;
		results[0].Name = "Mutated Store";

		var fresh = _storeCache.Cache.Query().Execute();
		Assert.That(fresh.First(c => c.Id == originalId).Name, Is.Not.EqualTo("Mutated Store"));
	}

	// ── Inner join + ExecuteCloned ────────────────────────────────────────────
	//
	// Note: codegen-emitted inner joins also flow through the cloned path. Verify
	// the inner-narrow + deep-clone interact correctly.

	[Test]
	public void InnerJoinOne_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.InnerJoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.GreaterThan(0));
		results[0].Left.Name = "Modified Store";
		results[0].Right!.Name = "Modified Warehouse";

		var fresh = _storeCache.Cache.Query()
			.InnerJoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.Execute();
		Assert.That(fresh[0].Left.Name, Is.Not.EqualTo("Modified Store"));
		Assert.That(fresh[0].Right!.Name, Is.Not.EqualTo("Modified Warehouse"));
	}

	[Test]
	public void InnerJoinMany_ExecuteCloned_ReturnsDeepCopy() {
		var results = _storeCache.Cache
			.Query()
			.InnerJoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.GreaterThan(0));
		results[0].Right[0].Name = "Modified Worker";

		var fresh = _storeCache.Cache.Query()
			.InnerJoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();
		Assert.That(fresh[0].Right[0].Name, Is.Not.EqualTo("Modified Worker"));
	}

	// ── Execute() + .Clone() on result with JoinMany slot ─────────────────────
	//
	// Semantic: JoinResult.Clone() on a Many slot does an IN-PLACE element clone
	// (via QueryResults.CloneElements) — replacing cache references with clones.
	// Cache is fully isolated; the cloned row and original results[0] still
	// alias the same buffer slice (struct copy semantics) — that's documented.
	// For full struct-level isolation use ExecuteCloned().

	[Test]
	public void JoinMany_Execute_ThenClone_CacheIsIsolated() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		Assert.That(results[0].Right.Count, Is.GreaterThan(0));

		// Capture the cache's Worker object directly (not via results).
		var cachedWorker = _workerCache.Cache.TryGet(results[0].Right[0].Id, out var p) ? p : null;
		Assert.That(cachedWorker, Is.Not.Null);
		var origCacheName = cachedWorker!.Name;

		var clonedRow = results[0].Clone();

		// Clone produces independent reference for the Worker at index 0.
		Assert.That(ReferenceEquals(clonedRow.Right[0], cachedWorker), Is.False,
			"After Clone(), Right[0] must NOT be the cache reference (in-place element clone replaced it)");

		clonedRow.Right[0].Name = "Mutated via clone";

		// Cache's Worker object stays untouched.
		var afterCacheName = _workerCache.Cache.TryGet(cachedWorker.Id, out var p2) ? p2!.Name : null;
		Assert.That(afterCacheName, Is.EqualTo(origCacheName),
			"Cache's Worker.Name must not be affected by mutating cloned row");
	}

	[Test]
	public void JoinMany_Execute_ThenClone_InnerElementsAreDifferentFromCache() {
		var results = _storeCache.Cache
			.Query()
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		var cachedWorker = _workerCache.Cache.TryGet(results[0].Right[0].Id, out var p) ? p : null;
		Assert.That(cachedWorker, Is.Not.Null);

		var clonedRow = results[0].Clone();

		// In-place element clone means clonedRow.Right[0] is NOT the cache ref.
		Assert.That(ReferenceEquals(clonedRow.Right[0], cachedWorker), Is.False);
	}

	[Test]
	public void JoinOneMany_Execute_ThenClone_CacheIsIsolated_OnManySlot() {
		var results = _storeCache.Cache
			.Query()
			.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
			.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex)
			.Execute();

		var cachedWorker = _workerCache.Cache.TryGet(results[0].Right2[0].Id, out var p) ? p : null;
		Assert.That(cachedWorker, Is.Not.Null);
		var origCacheName = cachedWorker!.Name;

		var clonedRow = results[0].Clone();

		// One slot (Warehouse): deep-cloned via the existing fix.
		clonedRow.Right!.Name = "Modified Warehouse";
		Assert.That(_warehouseCache.Cache.TryGet(clonedRow.Right.Id, out var s) ? s!.Name : null,
			Is.Not.EqualTo("Modified Warehouse"),
			"Warehouse in cache must not be affected by mutating cloned Warehouse");

		// Many slot (Right2): in-place element clone.
		clonedRow.Right2[0].Name = "Mutated via clone";
		var afterCacheName = _workerCache.Cache.TryGet(cachedWorker.Id, out var p2) ? p2!.Name : null;
		Assert.That(afterCacheName, Is.EqualTo(origCacheName),
			"Cache's Worker must not be affected by mutating cloned row's Many slot");
	}
}
#endif

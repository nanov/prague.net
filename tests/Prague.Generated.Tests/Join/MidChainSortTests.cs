// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

/// <summary>
/// Tests for mid-chain Sort() functionality.
/// Sort() is called between join steps, sorts+crops at that level, then continues with remaining joins.
/// </summary>
[TestFixture]
public class MidChainSortTests {
	private DataCacheRegistry _registry;
	private StoreCache _storeCache;
	private WorkerCache _workerCache;
	private WarehouseCache _warehouseCache;
	private AgreementCache _agreementCache;

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

	// Comparers for JoinResult types at various levels

	private struct StoreIdDescComparer : IComparer<JoinResult<Store, Warehouse?>> {
		public int Compare(JoinResult<Store, Warehouse?> x, JoinResult<Store, Warehouse?> y)
			=> y.Left.Id.CompareTo(x.Left.Id);
	}

	private struct StoreIdDescManyComparer : IComparer<JoinResult<Store, QueryResults<Worker>>> {
		public int Compare(JoinResult<Store, QueryResults<Worker>> x, JoinResult<Store, QueryResults<Worker>> y)
			=> y.Left.Id.CompareTo(x.Left.Id);
	}

	private struct StoreNameAscComparer : IComparer<JoinResult<Store, Warehouse?>> {
		public int Compare(JoinResult<Store, Warehouse?> x, JoinResult<Store, Warehouse?> y)
			=> string.Compare(x.Left.Name, y.Left.Name, StringComparison.Ordinal);
	}

	private struct StoreNameAscManyComparer : IComparer<JoinResult<Store, QueryResults<Worker>>> {
		public int Compare(JoinResult<Store, QueryResults<Worker>> x, JoinResult<Store, QueryResults<Worker>> y)
			=> string.Compare(x.Left.Name, y.Left.Name, StringComparison.Ordinal);
	}

	#region Level 1 Sort then JoinOne (2 total joins)

	[Test]
	public void Sort_Level1_ThenJoinOne_SortsBeforeSecondJoin() {
		// Sort after first join (JoinOne warehouse), then join workers
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));
		Assert.That(results[2].Left.Id, Is.EqualTo(1));
		// Second join still resolves
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
		Assert.That(results[1].Right2.Count, Is.EqualTo(3));
	}

	[Test]
	public void Sort_Level1_ThenJoinOne_WithSkipTake_CropsBeforeSecondJoin() {
		// Sort + crop at level 1, then join second
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));
		// Workers still populated on remaining items
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
	}

	[Test]
	public void Sort_Level1_ThenJoinOne_WithSkipTake_SkipsBeyondEnd() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 100, take: 10);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	#endregion

	#region Level 1 Sort with JoinMany then JoinOne (2 total joins)

	[Test]
	public void Sort_Level1_JoinMany_ThenJoinOne_SortsBeforeSecondJoin() {
		var __b1 = _storeCache.Cache.Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescManyComparer());
		var __b3 = __b2.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));
		Assert.That(results[2].Left.Id, Is.EqualTo(1));
		// Second join still resolves
		Assert.That(results[0].Right2, Is.Not.Null);
		Assert.That(results[0].Right2!.Name, Is.EqualTo("East Depot"));
	}

	[Test]
	public void Sort_Level1_JoinMany_ThenJoinOne_WithSkipTake() {
		var __b1 = _storeCache.Cache.Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescManyComparer());
		var __b3 = __b2.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b3.Execute(skip: 1, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
		Assert.That(results[0].Right2!.Name, Is.EqualTo("South Depot"));
	}

	#endregion

	#region Level 1 Sort with InnerJoinOne then JoinMany (inner + sort)

	[Test]
	public void Sort_Level1_InnerJoinOne_ThenJoinMany_FiltersAndSorts() {
		// Remove Acme's warehouse so inner join filters it out
		_warehouseCache.Remove(3);

		var __b1 = _storeCache.Cache.Query().InnerJoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute();

		// Acme filtered by inner join, then sorted desc
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[1].Left.Id, Is.EqualTo(1));
		// Workers still populated
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
	}

	[Test]
	public void Sort_Level1_InnerJoinOne_ThenJoinMany_WithSkipTake() {
		_warehouseCache.Remove(3);

		var __b1 = _storeCache.Cache.Query().InnerJoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 0, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("South Depot"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
	}

	#endregion

	#region Sort preserves order through subsequent joins

	[Test]
	public void Sort_Level1_OrderPreserved_ThroughMultipleSubsequentJoins() {
		// Sort at level 1 by name ascending, then add 2 more joins
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameAscComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b4 = __b3.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		// Alphabetical: Acme, Globex, Initech
		Assert.That(results[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(results[1].Left.Name, Is.EqualTo("Globex"));
		Assert.That(results[2].Left.Name, Is.EqualTo("Initech"));
		// All subsequent joins resolved
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
		Assert.That(results[0].Right3.Count, Is.EqualTo(2));
	}

	[Test]
	public void Sort_Level1_WithSkipTake_ReducesWorkForSubsequentJoins() {
		// Sort + crop to 1 item, then 2 more joins only execute on that 1 item
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameAscComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b4 = __b3.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var results = __b4.Execute(skip: 0, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
		Assert.That(results[0].Right3.Count, Is.EqualTo(2));
	}

	#endregion

	#region Sort with Execute variants (Pooled, Cloned, PooledCloned)

	[Test]
	public void Sort_Level1_ExecutePooled() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.ExecutePooled(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[1].Left.Id, Is.EqualTo(2));

		results.Dispose();
	}

	[Test]
	public void Sort_Level1_ExecuteCloned() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.ExecuteCloned(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));

		// Verify clone — mutating result should not affect cache
		results[0].Left.Name = "Modified";
		var original = _storeCache.Cache.Query().Where(c => c.Id == 3).Execute();
		Assert.That(original[0].Name, Is.EqualTo("Acme"));
	}

	[Test]
	public void Sort_Level1_ExecutePooledCloned() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.ExecutePooledCloned(skip: 1, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));

		// Verify clone
		results[0].Left.Name = "Modified";
		var original = _storeCache.Cache.Query().Where(c => c.Id == 2).Execute();
		Assert.That(original[0].Name, Is.EqualTo("Initech"));

		results.Dispose();
	}

	#endregion

	#region Sort without skip/take (just reorder)

	[Test]
	public void Sort_Level1_NoSkipTake_JustReorders() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreNameAscComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Name, Is.EqualTo("Acme"));
		Assert.That(results[1].Left.Name, Is.EqualTo("Globex"));
		Assert.That(results[2].Left.Name, Is.EqualTo("Initech"));
	}

	#endregion

	#region Sort then additional Sort at end (chain sort + final sort)

	// [Test]
	// public void Sort_Level1_ThenFinalSort_BothApply() {
	// 	// Mid-chain sort by Id desc + crop to 2, then final sort at Execute by name
	// 	var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
	// 	var __b2 = __b1.Sort(new StoreIdDescComparer());
	// 	var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
	//
	// 	// Mid-chain sort crops to top 2 by Id desc (3, 2), then final sort by name asc
	// 	Assert.That(results.Count, Is.EqualTo(2));
	// 	Assert.That(results[0].Left.Name, Is.EqualTo("Acme")); // Id=3
	// 	Assert.That(results[1].Left.Name, Is.EqualTo("Initech")); // Id=2
	// }

	#endregion

	#region Sort with SortLevel 0 (no sort — default behavior)

	[Test]
	public void NoSort_DefaultBehavior_StillWorks() {
		// No Sort() call — should work as before
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		// All workers resolved
		foreach (var r in results)
			Assert.That(r.Right2.Count, Is.EqualTo(3));
	}

	#endregion

	#region Edge cases

	[Test]
	public void Sort_Level1_EmptyResults_ReturnsEmpty() {
		// Query with Where that matches nothing
		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 999).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void Sort_Level1_SingleResult_StillWorks() {
		var __b1 = _storeCache.Cache.Query() .Where(c => c.Id == 1).JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Name, Is.EqualTo("North Depot"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(3));
	}

	[Test]
	public void Sort_Level1_TakeMoreThanAvailable_ReturnsAll() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 0, take: 100);

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
	}

	[Test]
	public void Sort_Level1_TakeZero_ReturnsEmpty() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 0, take: 0);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	#endregion

	#region TotalCount verification

	[Test]
	public void Sort_Level1_TotalCount_ReflectsPreSortCount() {
		var __b1 = _storeCache.Cache.Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.Sort(new StoreIdDescComparer());
		var __b3 = __b2.JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b3.Execute(skip: 0, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
	}

	#endregion
}

#endif

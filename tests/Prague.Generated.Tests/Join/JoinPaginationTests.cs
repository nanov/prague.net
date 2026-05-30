// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class JoinPaginationTests {
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
	public void JoinOne_Execute_WithTake_ReturnsLimitedResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b1.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_Execute_WithSkip_ReturnsSkippedResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b1.Execute(1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_Execute_WithSkipAndTake_ReturnsCorrectSlice() {
		_storeCache.AddOrUpdate(new Store { Id = 3, Name = "Acme", City = "Acme" });
		_warehouseCache.AddOrUpdate(new Warehouse { Id = 3, StoreId = 3, Name = "East Depot", Capacity = 99000 });

		var allResults =
			_storeCache.Cache
				.Query()
				.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex)
				.Execute();

		Assert.That(allResults.Count, Is.EqualTo(3));

		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var pagedResults = __b1.Execute(1, 1);

		Assert.That(pagedResults.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_Execute_WithTake_ReturnsLimitedResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b1.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
	}

	[Test]
	public void JoinMany_Execute_WithSkipAndTake_ReturnsCorrectSlice() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var allResults = __b1.Execute();

		Assert.That(allResults.Count, Is.EqualTo(2));

		var __b2 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var pagedResults = __b2.Execute(1, 1);

		Assert.That(pagedResults.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinOneOne_Execute_WithTake_ReturnsLimitedResults() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var __b2 = __b1.JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b2.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinManyMany_Execute_WithTake_ReturnsLimitedResults() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var __b2 = __b1.JoinMany(_agreementCache.Cache, _agreementCache.StoreIdIndex);
		var results = __b2.Execute(0, 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
		Assert.That(results[0].Right2.Count, Is.EqualTo(2));
	}

	[Test]
	public void JoinOne_Execute_WithSkipBeyondCount_ReturnsEmpty() {
		var __b1 = _storeCache.Cache .Query().JoinOne(_warehouseCache.Cache, _warehouseCache.StoreIdIndex);
		var results = __b1.Execute(100, 10);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_Execute_WithSkipBeyondCount_ReturnsEmpty() {
		var __b1 = _storeCache.Cache .Query().JoinMany(_workerCache.Cache, _workerCache.StoreIdIndex);
		var results = __b1.Execute(100, 10);

		Assert.That(results.Count, Is.EqualTo(0));
	}
}

#endif

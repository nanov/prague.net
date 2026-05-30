namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Selector-form FK models ─────────────────────────────────────────────────
//   Canonical use case from CLAUDE.md / user spec:
//     Car has a COMPOUND PK (Id, RegionId) typed as a value-tuple.
//     CarData has a single PK CarId : int.
//   The relationship: each Car maps to one CarData identified by CarData.CarId == Car.Id
//   (regardless of RegionId). The selector extracts the int Id from Car's compound key.

public readonly struct CarKeySelector : IForeignKeySelector<(int Id, int RegionId), int> {
	public static int Select((int Id, int RegionId) k) => k.Id;
}

[DataCache]
public partial class Car {
	public int Id { get; set; }
	public int RegionId { get; set; }
	public string Make { get; set; } = string.Empty;

	[DataCacheKey]
	[DataCacheForeignKey<CarData, CarKeySelector>(DataCacheJoinType.OneToOne)]
	public (int Id, int RegionId) CompositeId => (Id, RegionId);
}

[DataCache]
public partial class CarData {
	[DataCacheKey] public int CarId { get; set; }
	public int Horsepower { get; set; }
}

[TestFixture]
public class SelectorForeignKeyTests {
	private DataCacheRegistry _registry = null!;
	private CarCache _cars = null!;
	private CarDataCache _data = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CarCache>()
			.Register<CarDataCache>()
			.Build();
		_cars = _registry.GetCache<CarCache>();
		_data = _registry.GetCache<CarDataCache>();

		_cars.AddOrUpdate(new Car { Id = 1, RegionId = 100, Make = "Toyota" });
		_cars.AddOrUpdate(new Car { Id = 2, RegionId = 100, Make = "Honda" });
		_cars.AddOrUpdate(new Car { Id = 3, RegionId = 200, Make = "Tesla" });
		_data.AddOrUpdate(new CarData { CarId = 1, Horsepower = 200 });
		_data.AddOrUpdate(new CarData { CarId = 2, Horsepower = 150 });
		// Car 3 (Tesla) deliberately has no CarData — orphan case.
	}

	[Test]
	public void SelectorFk_OnPk_NoLeftSideIndexEmitted() {
		// OneToOne + selector on PK: codegen should NOT emit a left-side index field.
		// The right cache's KeyIndex covers the lookup via the PK+selector JoinOne overload
		// (static method-group conversion is compiler-cached → zero-alloc per call).
		var carFields = typeof(CarCache).GetFields(System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic |
			System.Reflection.BindingFlags.Instance);
		Assert.That(carFields.Any(f => f.Name == "CompositeIdIndex"), Is.False,
			"OneToOne+selector on PK should not emit a left-side index field.");
	}

	[Test]
	public void SelectorFk_ForwardJoinWithCarData_LooksUpViaSelector() {
		using var results = _cars.Query().JoinWithCarData().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			var car = row.Left;
			var data = row.Right;
			if (car.Id == 1) {
				Assert.That(data, Is.Not.Null);
				Assert.That(data!.Horsepower, Is.EqualTo(200));
			}
			else if (car.Id == 2) {
				Assert.That(data, Is.Not.Null);
				Assert.That(data!.Horsepower, Is.EqualTo(150));
			}
			else {
				Assert.That(data, Is.Null, "Car 3 has no CarData");
			}
		}
	}

	[Test]
	public void SelectorFk_ForwardInnerJoinWithCarData_DropsOrphans() {
		using var results = _cars.Query().InnerJoinWithCarData().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null);
			Assert.That(row.Left.Id, Is.Not.EqualTo(3));
		}
	}
}

// ── ManyToOne + selector ────────────────────────────────────────────────────
//   Vehicle has a non-PK FK OwnerKey : long pointing to Owner whose PK is int Id.
//   Many vehicles can share the same owner, so it's ManyToOne. The selector
//   converts the long FK value to the int target PK.

public readonly struct OwnerKeySelector : IForeignKeySelector<long, int> {
	public static int Select(long fk) => (int)fk;
}

[DataCache]
public partial class Vehicle {
	[DataCacheKey] public int Id { get; set; }
	[DataCacheForeignKey<Owner, OwnerKeySelector>(DataCacheJoinType.ManyToOne)]
	public long OwnerKey { get; set; }
	public string Plate { get; set; } = string.Empty;
}

[DataCache]
public partial class Owner {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[TestFixture]
public class SelectorManyToOneForeignKeyTests {
	private DataCacheRegistry _registry = null!;
	private VehicleCache _vehicles = null!;
	private OwnerCache _owners = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<VehicleCache>()
			.Register<OwnerCache>()
			.Build();
		_vehicles = _registry.GetCache<VehicleCache>();
		_owners = _registry.GetCache<OwnerCache>();

		_owners.AddOrUpdate(new Owner { Id = 10, Name = "Alice" });
		_owners.AddOrUpdate(new Owner { Id = 20, Name = "Bob" });
		// Many-to-one: V1 and V2 share owner 10.
		_vehicles.AddOrUpdate(new Vehicle { Id = 1, OwnerKey = 10L, Plate = "AAA-1" });
		_vehicles.AddOrUpdate(new Vehicle { Id = 2, OwnerKey = 10L, Plate = "AAA-2" });
		_vehicles.AddOrUpdate(new Vehicle { Id = 3, OwnerKey = 20L, Plate = "BBB-3" });
		// V4 orphan — no matching owner id 99.
		_vehicles.AddOrUpdate(new Vehicle { Id = 4, OwnerKey = 99L, Plate = "ORPH-4" });
	}

	[Test]
	public void SelectorM2OFk_AutoEmitsSymmetricListIndex_KeyedByFkPropertyType() {
		// The index is keyed by the FK property's raw type (long OwnerKey); the selector
		// (long → int) is applied at join time, not at index-build time. This lets
		// WithOwnerKey(long) work as a plain filter.
		var indexType = _vehicles.OwnerKeyIndex.GetType();
		Assert.That(indexType.Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
		var indexValueType = indexType.GetGenericArguments()[^1];
		Assert.That(indexValueType, Is.EqualTo(typeof(long)));
	}

	[Test]
	public void SelectorM2OFk_WithOwnerKey_StandaloneFilter() {
		using var results = _vehicles.Query().WithOwnerKey(10L).ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var v in results)
			Assert.That(v.OwnerKey, Is.EqualTo(10L));
	}

	[Test]
	public void SelectorM2OFk_ForwardJoinWithOwner_FansOutAndMatchesViaSelector() {
		using var results = _vehicles.Query().JoinWithOwner().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(4));
		foreach (var row in results) {
			if (row.Left.Id == 1 || row.Left.Id == 2) {
				Assert.That(row.Right, Is.Not.Null);
				Assert.That(row.Right!.Id, Is.EqualTo(10));
			}
			else if (row.Left.Id == 3) {
				Assert.That(row.Right, Is.Not.Null);
				Assert.That(row.Right!.Id, Is.EqualTo(20));
			}
			else {
				Assert.That(row.Right, Is.Null, "V4 is an orphan");
			}
		}
	}

	[Test]
	public void SelectorM2OFk_InnerJoinWithOwner_DropsOrphans() {
		using var results = _vehicles.Query().InnerJoinWithOwner().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null);
			Assert.That(row.Left.Id, Is.Not.EqualTo(4));
		}
	}
}

namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Phase B-5 chained selector tests: LeftSym Shape B + key selector at chained levels.
// Shape B: CacheSymmetricKeyValueListIndex on left + explicit rightIndex on right cache.
// Selector maps TLookupKey → TRightIndexKey; rightIndex translates TRightIndexKey → TRightKey.
// At chained hops we use two distinct symmetric list indexes on the outer cache, each paired
// with a CacheKeyValueIndex on the corresponding right cache. Selector maps long → int.

internal sealed class LsBSelChainShipment : ICacheEquatable<LsBSelChainShipment>, ICacheClonable<LsBSelChainShipment> {
	public int Id { get; init; }
	public string? Code { get; init; }
	public long WarehouseLookupKey { get; init; }  // long; selector → int → rightIndex → Warehouse.Id
	public long CarrierLookupKey { get; init; }    // long; selector → int → rightIndex → Carrier.Id
	public bool CacheEquals(LsBSelChainShipment? o) => o is not null && o.Id == Id && o.Code == Code && o.WarehouseLookupKey == WarehouseLookupKey && o.CarrierLookupKey == CarrierLookupKey;
	public int CacheGetHashCode() => HashCode.Combine(Id, Code, WarehouseLookupKey, CarrierLookupKey);
	public LsBSelChainShipment Clone() => new() { Id = Id, Code = Code, WarehouseLookupKey = WarehouseLookupKey, CarrierLookupKey = CarrierLookupKey };
}

internal sealed class LsBSelChainWarehouse : ICacheEquatable<LsBSelChainWarehouse>, ICacheClonable<LsBSelChainWarehouse> {
	public int Id { get; init; }
	public string? Location { get; init; }
	public int RegionCode { get; init; }  // the right-index key (int)
	public bool CacheEquals(LsBSelChainWarehouse? o) => o is not null && o.Id == Id && o.Location == Location && o.RegionCode == RegionCode;
	public int CacheGetHashCode() => HashCode.Combine(Id, Location, RegionCode);
	public LsBSelChainWarehouse Clone() => new() { Id = Id, Location = Location, RegionCode = RegionCode };
}

internal sealed class LsBSelChainCarrier : ICacheEquatable<LsBSelChainCarrier>, ICacheClonable<LsBSelChainCarrier> {
	public int Id { get; init; }
	public string? Name { get; init; }
	public int RouteCode { get; init; }  // the right-index key (int)
	public int Priority { get; init; }
	public bool CacheEquals(LsBSelChainCarrier? o) => o is not null && o.Id == Id && o.Name == Name && o.RouteCode == RouteCode && o.Priority == Priority;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, RouteCode, Priority);
	public LsBSelChainCarrier Clone() => new() { Id = Id, Name = Name, RouteCode = RouteCode, Priority = Priority };
}

[TestFixture]
public class JoinOneLeftSymShapeBSelectorChainedCoreTests {
	private InMemoryDataCache<int, LsBSelChainShipment> _shipmentCache = null!;
	private InMemoryDataCache<int, LsBSelChainWarehouse> _warehouseCache = null!;
	private InMemoryDataCache<int, LsBSelChainCarrier> _carrierCache = null!;
	private CacheSymmetricKeyValueListIndex<int, LsBSelChainShipment, long> _shipByWarehouseLookupKey = null!;
	private CacheSymmetricKeyValueListIndex<int, LsBSelChainShipment, long> _shipByCarrierLookupKey = null!;
	private CacheUniqueIndex<int, LsBSelChainWarehouse, int> _warehouseByRegionCode = null!;
	private CacheUniqueIndex<int, LsBSelChainCarrier, int> _carrierByRouteCode = null!;

	[SetUp]
	public void SetUp() {
		_shipmentCache = new InMemoryDataCache<int, LsBSelChainShipment>();
		_warehouseCache = new InMemoryDataCache<int, LsBSelChainWarehouse>();
		_carrierCache = new InMemoryDataCache<int, LsBSelChainCarrier>();
		_shipByWarehouseLookupKey = _shipmentCache.CacheSymmetricKeyValueListIndex<long>(static (_, v) => v.WarehouseLookupKey);
		_shipByCarrierLookupKey = _shipmentCache.CacheSymmetricKeyValueListIndex<long>(static (_, v) => v.CarrierLookupKey);
		_warehouseByRegionCode = _warehouseCache.AddKeyValueIndex<int>(static (_, v) => v.RegionCode);
		_carrierByRouteCode = _carrierCache.AddKeyValueIndex<int>(static (_, v) => v.RouteCode);
	}

	private void SeedFullChain() {
		_shipmentCache.AddOrUpdate(1, new LsBSelChainShipment { Id = 1, Code = "S1", WarehouseLookupKey = 101L, CarrierLookupKey = 201L });
		_shipmentCache.AddOrUpdate(2, new LsBSelChainShipment { Id = 2, Code = "S2", WarehouseLookupKey = 102L, CarrierLookupKey = 202L });
		_shipmentCache.AddOrUpdate(3, new LsBSelChainShipment { Id = 3, Code = "S3", WarehouseLookupKey = 103L, CarrierLookupKey = 203L });
		_warehouseCache.AddOrUpdate(10, new LsBSelChainWarehouse { Id = 10, Location = "W-A", RegionCode = 101 });
		_warehouseCache.AddOrUpdate(20, new LsBSelChainWarehouse { Id = 20, Location = "W-B", RegionCode = 102 });
		_warehouseCache.AddOrUpdate(30, new LsBSelChainWarehouse { Id = 30, Location = "W-C", RegionCode = 103 });
		_carrierCache.AddOrUpdate(70, new LsBSelChainCarrier { Id = 70, Name = "C-A", RouteCode = 201, Priority = 1 });
		_carrierCache.AddOrUpdate(80, new LsBSelChainCarrier { Id = 80, Name = "C-B", RouteCode = 202, Priority = 2 });
		_carrierCache.AddOrUpdate(90, new LsBSelChainCarrier { Id = 90, Name = "C-C", RouteCode = 203, Priority = 3 });
	}

	[Test]
	public void Chained_S1Outer_AllMatched() {
		SeedFullChain();
		var results = _shipmentCache.Query()
			.JoinOne(_shipByWarehouseLookupKey, static lk => (int)lk, _warehouseCache, _warehouseByRegionCode)
			.JoinOne(_shipByCarrierLookupKey, static lk => (int)lk, _carrierCache, _carrierByRouteCode)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Location, Is.EqualTo("W-A"));
		Assert.That(byId[1].Right2!.Name, Is.EqualTo("C-A"));
	}

	[Test]
	public void Chained_S1Inner_DropsUnmatchedAtSecondHop() {
		SeedFullChain();
		_carrierCache.Remove(80);
		var results = _shipmentCache.Query()
			.InnerJoinOne(_shipByWarehouseLookupKey, static lk => (int)lk, _warehouseCache, _warehouseByRegionCode)
			.InnerJoinOne(_shipByCarrierLookupKey, static lk => (int)lk, _carrierCache, _carrierByRouteCode)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S3Inner_WithFilter_DropsPredicateReject() {
		SeedFullChain();
		var results = _shipmentCache.Query()
			.InnerJoinOne(_shipByWarehouseLookupKey, static lk => (int)lk, _warehouseCache, _warehouseByRegionCode)
			.InnerJoinOne(_shipByCarrierLookupKey, static lk => (int)lk, _carrierCache, _carrierByRouteCode,
				static q => q.Where(c => c.Priority < 3))
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Chained_S5Inner_WithFilterArg_StaticLambdaZeroAlloc() {
		SeedFullChain();
		const int cutoff = 3;
		var results = _shipmentCache.Query()
			.InnerJoinOne(_shipByWarehouseLookupKey, static lk => (int)lk, _warehouseCache, _warehouseByRegionCode)
			.InnerJoinOne(_shipByCarrierLookupKey, static lk => (int)lk, _carrierCache, _carrierByRouteCode,
				static (q, c) => q.Where(x => x.Priority < c),
				cutoff)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
	}
}

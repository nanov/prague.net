// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[DataCache]
public partial class Vendor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; }
}

[DataCache]
public partial class Maker {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int VendorId { get; set; }

	public string Name { get; set; }
}

[DataCache]
public partial class Listing {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int VendorId { get; set; }

	public int ManufacturerId { get; set; }
	public int SupplierId { get; set; }
	public string Status { get; set; }
}

[DataCache]
public partial class ListingStats {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int VendorId { get; set; }

	public int Quantity { get; set; }
	public int Returns { get; set; }
}

[DataCache]
public partial class Order {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int VendorId { get; set; }

	public string OrderType { get; set; }
	public decimal Price { get; set; }
}

[DataCache]
public partial class OrderLine {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public int VendorId { get; set; }

	public string Variant { get; set; }
}

[TestFixture]
public class MultiJoinTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<VendorCache>()
			.Register<MakerCache>()
			.Register<ListingCache>()
			.Register<ListingStatsCache>()
			.Register<OrderCache>()
			.Register<OrderLineCache>()
			.Build();
		_vendorCache = _registry.GetCache<VendorCache>();
		_makerCache = _registry.GetCache<MakerCache>();
		_listingCache = _registry.GetCache<ListingCache>();
		_listingStatsCache = _registry.GetCache<ListingStatsCache>();
		_orderCache = _registry.GetCache<OrderCache>();
		_orderLineCache = _registry.GetCache<OrderLineCache>();

		SeedData();
	}

	private DataCacheRegistry _registry;
	private VendorCache _vendorCache;
	private MakerCache _makerCache;
	private ListingCache _listingCache;
	private ListingStatsCache _listingStatsCache;
	private OrderCache _orderCache;
	private OrderLineCache _orderLineCache;

	private void SeedData() {
		// Add 3 vendors
		_vendorCache.AddOrUpdate(new Vendor { Id = 1, Name = "Globex Line" });
		_vendorCache.AddOrUpdate(new Vendor { Id = 2, Name = "Initech Line" });
		_vendorCache.AddOrUpdate(new Vendor { Id = 3, Name = "Vandelay Line" });

		// Add makers (Many per vendor)
		var makerId = 1;
		for (var t = 1; t <= 3; t++)
		for (var i = 0; i < 4; i++)
			_makerCache.AddOrUpdate(new Maker { Id = makerId++, VendorId = t, Name = $"Maker {t}-{i}" });

		// Add listings (One per vendor)
		_listingCache.AddOrUpdate(new Listing { Id = 1, VendorId = 1, ManufacturerId = 1, SupplierId = 2, Status = "Active" });
		_listingCache.AddOrUpdate(
			new Listing { Id = 2, VendorId = 2, ManufacturerId = 5, SupplierId = 6, Status = "Draft" });
		_listingCache.AddOrUpdate(
			new Listing { Id = 3, VendorId = 3, ManufacturerId = 9, SupplierId = 10, Status = "Archived" });

		// Add listing stats (One per vendor)
		_listingStatsCache.AddOrUpdate(new ListingStats { Id = 1, VendorId = 1, Quantity = 3, Returns = 12 });
		_listingStatsCache.AddOrUpdate(new ListingStats { Id = 2, VendorId = 2, Quantity = 1, Returns = 8 });
		_listingStatsCache.AddOrUpdate(new ListingStats { Id = 3, VendorId = 3, Quantity = 2, Returns = 15 });

		// Add orders (Many per vendor)
		var orderId = 1;
		for (var t = 1; t <= 3; t++)
		for (var i = 0; i < 5; i++)
			_orderCache.AddOrUpdate(new Order { Id = orderId++, VendorId = t, OrderType = $"Type{i}", Price = 1.5m + i * 0.1m });

		// Add order lines (Many per vendor)
		var variantId = 1;
		for (var t = 1; t <= 3; t++)
		for (var i = 0; i < 3; i++)
			_orderLineCache.AddOrUpdate(new OrderLine
				{ Id = variantId++, VendorId = t, Variant = $"Variant{i}" });
	}

	[Test]
	public void JoinOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right!.VendorId, Is.EqualTo(r.Left.Id));
		}
	}

	[Test]
	public void JoinMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4)); // 4 makers per vendor
			foreach (var maker in r.Right) Assert.That(maker.VendorId, Is.EqualTo(r.Left.Id));
		}
	}

	[Test]
	public void JoinOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right!.VendorId, Is.EqualTo(r.Left.Id));
			Assert.That(r.Right2!.VendorId, Is.EqualTo(r.Left.Id));
		}
	}

	[Test]
	public void JoinOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(5)); // 5 orders per vendor
		}
	}

	[Test]
	public void JoinManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4)); // 4 makers per vendor
			Assert.That(r.Right2, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4)); // 4 makers
			Assert.That(r.Right2.Count, Is.EqualTo(5)); // 5 orders
		}
	}

	[Test]
	public void JoinOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b3.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinOneOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinOneManyOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinManyOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4.Count, Is.EqualTo(4));
			Assert.That(r.Right5.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneManyOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneManyOneManyReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinOne_WithNoMatchingData_ReturnsNullRight() {
		// Add a vendor with no listing
		_vendorCache.AddOrUpdate(new Vendor { Id = 100, Name = "No Listing Vendor" });

		var __b1 = _vendorCache.Cache .Query() .Where(t => t.Id == 100).JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(100));
		Assert.That(results[0].Right, Is.Null);
	}

	[Test]
	public void JoinMany_WithNoMatchingData_ReturnsEmptyCollection() {
		// Add a vendor with no makers
		_vendorCache.AddOrUpdate(new Vendor { Id = 101, Name = "No Makers Vendor" });

		var __b1 = _vendorCache.Cache .Query() .Where(t => t.Id == 101).JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(101));
		Assert.That(results[0].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinWithFilter_OnLeftQuery_FiltersCorrectly() {
		var __b1 = _vendorCache.Cache .Query() .Where(t => t.Name == "Globex Line").JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b2.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Globex Line"));
		Assert.That(results[0].Right, Is.Not.Null);
		Assert.That(results[0].Right2.Count, Is.EqualTo(4));
	}

	[Test]
	public void JoinWithFilter_OnRightQuery_FiltersCorrectly() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex, q => q.Where(b => b.OrderType == "Type0"));
		var results = __b1.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Right.Count, Is.EqualTo(1)); // Only Type0 orders
			Assert.That(r.Right[0].OrderType, Is.EqualTo("Type0"));
		}
	}

	[Test]
	public void Deconstruct_WorksCorrectly_ForTwoJoins() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b2.Execute();

		foreach (var r in results) {
			var (left, right, right2) = r;
			Assert.That(left, Is.Not.Null);
			Assert.That(right, Is.Not.Null);
			Assert.That(right2.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void Deconstruct_WorksCorrectly_ForThreeJoins() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b3.Execute();

		foreach (var r in results) {
			var (left, right, right2, right3) = r;
			Assert.That(left, Is.Not.Null);
			Assert.That(right, Is.Not.Null);
			Assert.That(right2.Count, Is.EqualTo(4));
			Assert.That(right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinManyOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinManyOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b4.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinManyManyManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4.Count, Is.EqualTo(4));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyManyOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinManyManyManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3.Count, Is.EqualTo(3));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinManyManyOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyManyOneOneManyReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinManyManyOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2.Count, Is.EqualTo(5));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinManyOneManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneManyOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinManyOneManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinManyOneOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinManyOneOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinManyOneOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right.Count, Is.EqualTo(4));
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5.Count, Is.EqualTo(4));
		}
	}

	[Test]
	public void JoinOneManyManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4.Count, Is.EqualTo(3));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyManyOneManyReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinOneManyManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3.Count, Is.EqualTo(5));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneManyOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinOneManyOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneManyOneOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2.Count, Is.EqualTo(4));
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneManyManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderLineCache.Cache, _orderLineCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5.Count, Is.EqualTo(3));
		}
	}

	[Test]
	public void JoinOneOneManyManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4.Count, Is.EqualTo(5));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneManyOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneOneManyOneOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3.Count, Is.EqualTo(4));
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneOneManyMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_orderCache.Cache, _orderCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(4));
			Assert.That(r.Right5.Count, Is.EqualTo(5));
		}
	}

	[Test]
	public void JoinOneOneOneManyOne_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var __b5 = __b4.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4.Count, Is.EqualTo(4));
			Assert.That(r.Right5, Is.Not.Null);
		}
	}

	[Test]
	public void JoinOneOneOneOneMany_ReturnsCorrectResults() {
		var __b1 = _vendorCache.Cache .Query().JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b2 = __b1.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b3 = __b2.JoinOne(_listingCache.Cache, _listingCache.VendorIdIndex);
		var __b4 = __b3.JoinOne(_listingStatsCache.Cache, _listingStatsCache.VendorIdIndex);
		var __b5 = __b4.JoinMany(_makerCache.Cache, _makerCache.VendorIdIndex);
		var results = __b5.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var r in results) {
			Assert.That(r.Left, Is.Not.Null);
			Assert.That(r.Right, Is.Not.Null);
			Assert.That(r.Right2, Is.Not.Null);
			Assert.That(r.Right3, Is.Not.Null);
			Assert.That(r.Right4, Is.Not.Null);
			Assert.That(r.Right5.Count, Is.EqualTo(4));
		}
	}
}

#endif

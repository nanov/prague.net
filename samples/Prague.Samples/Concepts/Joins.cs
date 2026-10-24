// docs: every code block of www/docs/articles/core-concepts/joins.md. The relationships are declared on
// the models: Order → Customer (ManyToOne, Models/Order.cs), Shipment → Order (OneToOne, Models/Shipment.cs),
// OrderLine → Order and → Product (ManyToOne, Models/OrderLine.cs), ShipmentLeg → Shipment (selector form,
// Models/ShipmentLeg.cs). Each #region is a call against them.
namespace Prague.Samples.Concepts;

using NUnit.Framework;
using Prague.Core;
using Prague.Samples.Models;

[TestFixture]
public class Joins {
	private const long Now = 1_760_000_000_000L;

	private DataCacheRegistry _registry = null!;
	private OrderCache _orders = null!;
	private CustomerCache _customers = null!;
	private OrderLineCache _lines = null!;
	private ShipmentCache _shipments = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<OrderCache>()
			.Register<CustomerCache>()
			.Register<OrderLineCache>()
			.Register<ShipmentCache>()
			.Register<ProductCache>()
			.Build();
		_orders = _registry.GetCache<OrderCache>();
		_customers = _registry.GetCache<CustomerCache>();
		_lines = _registry.GetCache<OrderLineCache>();
		_shipments = _registry.GetCache<ShipmentCache>();

		_customers.AddOrUpdate(new Customer { Id = 42, Email = "alice@example.com", Country = "US", Tier = CustomerTier.Gold, Name = "Alice" });
		_customers.AddOrUpdate(new Customer { Id = 7, Email = "bob@example.com", Country = "GB", Tier = CustomerTier.Standard, Name = "Bob" });

		_orders.AddOrUpdate(NewOrder("ORD-001", 42, 150m));
		_orders.AddOrUpdate(NewOrder("ORD-002", 42, 80m));
		_orders.AddOrUpdate(NewOrder("ORD-003", 7, 20m));
		_orders.AddOrUpdate(NewOrder("ORD-404", 999, 5m));                            // customer 999 does not exist

		_lines.AddOrUpdate(new OrderLine { LineId = "ORD-001/1", OrderId = "ORD-001", ProductId = 1, Quantity = 2, UnitPrice = 50m });
		_lines.AddOrUpdate(new OrderLine { LineId = "ORD-001/2", OrderId = "ORD-001", ProductId = 2, Quantity = 1, UnitPrice = 50m });
		_lines.AddOrUpdate(new OrderLine { LineId = "ORD-002/1", OrderId = "ORD-002", ProductId = 1, Quantity = 1, UnitPrice = 80m });

		_shipments.AddOrUpdate(new Shipment { ShipmentId = "SHP-1", OrderId = "ORD-001", Carrier = "DHL", ShippedAtUnixMs = Now });
	}

	[Test]
	public void ForwardJoinIsOuter() {
		#region forward-join
		using var rows = _orders.Query()
			.WithCustomerId(42)
			.JoinWithCustomer()                          // outer: rows are kept even without a customer
			.ExecutePooled();

		foreach (var row in rows)
			Console.WriteLine($"{row.Left.OrderId} by {row.Right?.Name ?? "unknown"}");
		#endregion

		Assert.That(rows.Count, Is.EqualTo(2));
		Assert.That(rows[0].Right!.Name, Is.EqualTo("Alice"));

		using var orphan = _orders.Query().WithOrderId("ORD-404").JoinWithCustomer().ExecutePooled();

		Assert.That(orphan.Count, Is.EqualTo(1));
		Assert.That(orphan[0].Right, Is.Null);
	}

	[Test]
	public void InnerJoinDropsLeftsWithoutAMatch() {
		#region inner-join
		using var rows = _orders.Query()
			.InnerJoinWithCustomer()                     // ORD-404 points at a customer that does not exist: dropped
			.ExecutePooled();
		#endregion

		Assert.That(rows.Count, Is.EqualTo(3));
		Assert.That(rows.Map(r => r.Right!.Id).ToArray(), Is.EquivalentTo(new[] { 42, 42, 7 }));
	}

	[Test]
	public void ReverseJoinYieldsMany() {
		#region reverse-join
		using var rows = _customers.Query()
			.WithId(42)
			.JoinWithOrder()                             // reverse side of the ManyToOne: QueryResults<Order>
			.ExecutePooled();

		foreach (var row in rows)
			Console.WriteLine($"{row.Left.Name} has {row.Right.Count} orders");
		#endregion

		Assert.That(rows.Count, Is.EqualTo(1));
		Assert.That(rows[0].Right.Map(o => o.OrderId).ToArray(), Is.EquivalentTo(new[] { "ORD-001", "ORD-002" }));
	}

	[Test]
	public void CountJoinYieldsTheNumberOfRights() {
		#region count-join
		using var rows = _customers.Query()
			.CountJoinWithOrder()                        // int per customer: no Order is materialised
			.ExecutePooled();

		foreach (var row in rows)
			Console.WriteLine($"{row.Left.Name} has {row.Right} orders");

		using var buyers = _customers.Query()
			.InnerCountJoinWithOrder()                   // drops customers with no orders
			.ExecutePooled();
		#endregion

		Assert.That(rows.Map(r => (r.Left.Id, r.Right)).ToArray(), Is.EquivalentTo(new[] { (42, 2), (7, 1) }));
		Assert.That(buyers.Count, Is.EqualTo(2));
	}

	[Test]
	public void OneToOneBothWays() {
		#region one-to-one
		using var withShipment = _orders.Query().WithOrderId("ORD-001").JoinWithShipment().ExecutePooled();   // Shipment?
		using var withOrder = _shipments.Query().WithCarrier("DHL").JoinWithOrder().ExecutePooled();           // Order?
		#endregion

		Assert.That(withShipment[0].Right!.ShipmentId, Is.EqualTo("SHP-1"));
		Assert.That(withOrder[0].Right!.OrderId, Is.EqualTo("ORD-001"));
	}

	[Test]
	public void OneToManyFromTheOneSide() {
		#region one-to-many
		using var rows = _orders.Query()
			.WithCustomerId(42)
			.JoinWithOrderLine()                         // QueryResults<OrderLine> per order
			.ExecutePooled();
		#endregion

		Assert.That(rows.Count, Is.EqualTo(2));
		Assert.That(rows.Map(r => r.Right.Count).ToArray(), Is.EquivalentTo(new[] { 2, 1 }));
	}

	[Test]
	public void ChainedJoins() {
		#region chained-joins
		using var rows = _orders.Query()
			.WithCustomerId(42)
			.JoinWithCustomer()
			.JoinWithShipment()
			.ExecutePooled();                            // JoinResult<Order, Customer?, Shipment?>

		foreach (var row in rows)
			Console.WriteLine($"{row.Left.OrderId}: {row.Right?.Name}, shipped by {row.Right2?.Carrier ?? "nobody yet"}");
		#endregion

		Assert.That(rows.Count, Is.EqualTo(2));
		Assert.That(rows.Map(r => r.Right2?.Carrier).ToArray(), Is.EquivalentTo(new[] { "DHL", null }));
	}

	[Test]
	public void StandaloneCachesNeedExplicitWiring() {
		#region standalone-wiring
		var orders = new OrderCache();
		var customers = new CustomerCache();
		var lines = new OrderLineCache();
		var shipments = new ShipmentCache();
		orders.AddJoinedCaches(customers, lines, shipments);   // once, before the first join; the registry does this for you
		#endregion

		customers.AddOrUpdate(new Customer { Id = 42, Email = "alice@example.com", Country = "US", Tier = CustomerTier.Gold, Name = "Alice" });
		orders.AddOrUpdate(NewOrder("ORD-001", 42, 150m));

		using var rows = orders.Query().JoinWithCustomer().ExecutePooled();

		Assert.That(rows[0].Right!.Name, Is.EqualTo("Alice"));
	}

	[Test]
	public void FilteringTheRightSide() {
		#region join-filter
		using var goldOnly = _orders.Query()
			.JoinWithCustomer(q => q.WithTier(CustomerTier.Gold))                        // the slot is null unless the customer is Gold
			.ExecutePooled();

		using var goldOnlyNoCapture = _orders.Query()
			.JoinWithCustomer(static (q, tier) => q.WithTier(tier), CustomerTier.Gold)   // static lambda + argument: no closure
			.ExecutePooled();
		#endregion

		Assert.That(goldOnly.Count, Is.EqualTo(4), "outer: every order stays");
		Assert.That(goldOnly.Map(r => r.Right?.Name).ToArray(), Is.EquivalentTo(new[] { "Alice", "Alice", null, null }));
		Assert.That(goldOnlyNoCapture.Map(r => r.Right?.Name).ToArray(), Is.EquivalentTo(new[] { "Alice", "Alice", null, null }));
	}

	[Test]
	public void WhereInsideTheFilter() {
		#region join-filter-where
		using var rows = _orders.Query()
			.JoinWithCustomer(q => q.WithCountry("GB").Where(c => c.Name.Length <= 3))   // a residual predicate on the right rows
			.ExecutePooled();
		#endregion

		Assert.That(rows.Map(r => r.Right?.Name).ToArray(), Is.EquivalentTo(new[] { null, null, "Bob", null }));
	}

	[Test]
	public void OrInsideTheFilter() {
		#region join-filter-or
		using var rows = _orders.Query()
			.JoinWithCustomer(q => q.Or(                 // attach the customer only if Country = GB OR Tier = Gold
				b => b.WithCountry("GB"),
				b => b.WithTier(CustomerTier.Gold)))
			.ExecutePooled();
		#endregion

		Assert.That(rows.Map(r => r.Right?.Name).ToArray(), Is.EquivalentTo(new[] { "Alice", "Alice", "Bob", null }));
	}

	[Test]
	public void SortingBeforeTheJoin() {
		#region sort-then-join
		using var top = _orders.Query()
			.SortBounded(OrderCache.ByCreatedAtComparer)
			.JoinWithCustomer()                          // after a sorter only the outer, unfiltered JoinWith{T}() binds
			.ExecutePooled(skip: 0, take: 2);
		#endregion

		Assert.That(top.Count, Is.EqualTo(2));
		Assert.That(top.TotalCount, Is.EqualTo(4));
	}

	[Test]
	public void SelectorFormJoin() {
		_shipments.AddOrUpdate(new Shipment { ShipmentId = "SHP-2", OrderId = "ORD-002", Carrier = "UPS", ShippedAtUnixMs = Now });
		var legs = new ShipmentLegCache();
		legs.AddJoinedCaches(_shipments);
		legs.AddOrUpdate(new ShipmentLeg { LegId = "SHP-1/1", Hop = ("SHP-1", 1), FromHub = "PRG", ToHub = "FRA" });
		legs.AddOrUpdate(new ShipmentLeg { LegId = "SHP-2/1", Hop = ("SHP-2", 1), FromHub = "FRA", ToHub = "LHR" });

		#region selector-form-join
		using var rows = legs.Query()
			.JoinWithShipment()                          // ShipmentLegShipmentKey.Select(Hop) → Shipment.ShipmentId
			.ExecutePooled();
		#endregion

		Assert.That(rows.Count, Is.EqualTo(2));
		Assert.That(rows.Map(r => r.Right!.Carrier).ToArray(), Is.EquivalentTo(new[] { "DHL", "UPS" }));
	}

	private static Order NewOrder(string orderId, int customerId, decimal total) => new() {
		OrderId = orderId,
		CustomerId = customerId,
		CreatedAtUnixMs = Now + orderId.GetHashCode() % 1000,
		Status = OrderStatus.Pending,
		Total = total,
	};
}

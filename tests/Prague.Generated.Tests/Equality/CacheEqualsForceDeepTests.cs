namespace Prague.Generated.Tests.Equality;

using Prague.Core;
using Prague.Generated.Tests.Models;
using NUnit.Framework;

// Verifies the `forceDeep` overload of the generated CacheEquals: when true it skips the
// reference-equality fast path and runs the full structural comparison, propagating the skip to
// every nested level (nested objects, lists/arrays of cache types, dictionary-valued cache types).
//
// The double.NaN trick makes the difference observable: NaN != NaN, so a value reached only by the
// deep path flips the result to false while the fast path (which short-circuits identical
// references) still returns true.
[TestFixture]
public class CacheEqualsForceDeepTests {
	// ── Default path keeps the reference-equality fast path (no behavior change) ──

	[Test]
	public void Default_SameReferenceWithNestedNaN_ShortCircuitsTrue() {
		var order = CreateOrderWithNestedObjectNaN();

		// ReferenceEquals short-circuits before the NaN is ever reached.
		Assert.That(order.CacheEquals(order), Is.True);
		Assert.That(order.CacheEquals(order, forceDeep: false), Is.True);
	}

	// ── forceDeep:true skips the fast path and reaches the nested NaN ──

	[Test]
	public void ForceDeep_NestedObjectNaN_ReturnsFalse() {
		var order = CreateOrderWithNestedObjectNaN();

		Assert.That(order.CacheEquals(order), Is.True);                   // fast path
		Assert.That(order.CacheEquals(order, forceDeep: true), Is.False); // Customer -> Address -> GeoLocation
	}

	[Test]
	public void ForceDeep_ListElementNaN_ReturnsFalse() {
		var order = CreateTestOrder();
		order.Lines.Add(new OrderLine {
			Product = new Product {
				Supplier = new Supplier {
					Address = new Address { Location = new GeoLocation { Latitude = double.NaN } },
				},
			},
		});

		Assert.That(order.CacheEquals(order), Is.True);
		Assert.That(order.CacheEquals(order, forceDeep: true), Is.False); // List<OrderLine> -> ... -> GeoLocation
	}

	[Test]
	public void ForceDeep_DictionaryValueNaN_ReturnsFalse() {
		var order = CreateTestOrder();
		order.CustomersByRegion["EU"] = new Customer {
			PrimaryAddress = new Address { Location = new GeoLocation { Latitude = double.NaN } },
		};

		Assert.That(order.CacheEquals(order), Is.True);
		Assert.That(order.CacheEquals(order, forceDeep: true), Is.False); // Dictionary<string, Customer> -> ... -> GeoLocation
	}

	// ── No false negatives: distinct-but-equal graphs (incl. null nested objects) stay equal ──

	[Test]
	public void ForceDeep_DistinctButEqualGraphs_ReturnsTrue() {
		var a = CreateTestOrder();
		var b = CreateTestOrder();

		// Exercises the both-null nested-object path that the fast path used to cover.
		Assert.That(a.CacheEquals(b, forceDeep: true), Is.True);
	}

	[Test]
	public void ForceDeep_BothNullNestedObject_ReturnsTrue() {
		var a = CreateTestOrder();
		var b = CreateTestOrder();
		a.ShippingInfo = null;
		b.ShippingInfo = null;

		Assert.That(a.CacheEquals(b, forceDeep: true), Is.True);
	}

	[Test]
	public void ForceDeep_OneNullNestedObject_ReturnsFalse() {
		var a = CreateTestOrder();
		var b = CreateTestOrder();
		a.Customer = null;

		Assert.That(a.CacheEquals(b, forceDeep: true), Is.False);
	}

	// ── Reachable through the ICacheEquatable<T> interface (generated override) ──

	[Test]
	public void ForceDeep_ThroughInterface_DispatchesToGeneratedDeepCompare() {
		var order = CreateOrderWithNestedObjectNaN();
		ICacheEquatable<Order> equatable = order;

		Assert.That(equatable.CacheEquals(order, forceDeep: false), Is.True);
		Assert.That(equatable.CacheEquals(order, forceDeep: true), Is.False);
	}

	// ── Non-breaking default interface method: hand-written implementers ignore the flag ──

	[Test]
	public void DefaultInterfaceMethod_HandWrittenImplementer_IgnoresFlag() {
		ICacheEquatable<HandWritten> hw = new HandWritten(1);

		// No 2-arg override → inherits the default method → delegates to the 1-arg; flag ignored.
		Assert.That(hw.CacheEquals(new HandWritten(1), forceDeep: true), Is.True);
		Assert.That(hw.CacheEquals(new HandWritten(2), forceDeep: true), Is.False);
	}

	private sealed class HandWritten : ICacheEquatable<HandWritten> {
		private readonly int _id;

		public HandWritten(int id) => _id = id;

		public bool CacheEquals(HandWritten? other) => other is not null && other._id == _id;

		public int CacheGetHashCode() => _id;
	}

	private static Order CreateOrderWithNestedObjectNaN() {
		var order = CreateTestOrder();
		order.Customer!.PrimaryAddress = new Address { Location = new GeoLocation { Latitude = double.NaN } };
		return order;
	}

	private static Order CreateTestOrder() {
		return new Order {
			OrderId = "ORDER-123",
			OrderNumber = 12345,
			OrderDate = new DateTime(2024, 1, 1, 10, 30, 0),
			TotalAmount = 299.99m,
			Status = OrderStatus.Pending,
			Customer = new Customer {
				CustomerId = 1,
				Name = "John Doe",
				Email = "john@example.com",
			},
			ShippingInfo = new ShippingInfo {
				TrackingId = "TRACK-123",
				Carrier = "FastShip",
				Status = ShippingStatus.InTransit,
			},
		};
	}
}

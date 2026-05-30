namespace Prague.Generated.Tests.Equality;
using Prague.Generated.Tests.Models;

using NUnit.Framework;

[TestFixture]
public class CacheEqualityTests {
	[Test]
	public void CacheEquals_SameReference_ReturnsTrue() {
		// Arrange
		var order = CreateTestOrder();

		// Act & Assert
		Assert.That(order.CacheEquals(order), Is.True);
	}

	[Test]
	public void CacheEquals_IdenticalObjects_ReturnsTrue() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_Null_ReturnsFalse() {
		// Arrange
		var order = CreateTestOrder();

		// Act & Assert
		Assert.That(order.CacheEquals(null), Is.False);
	}


	[Test]
	public void CacheEquals_DifferentPropertyValue_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.TotalAmount = 999.99m;

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheGetHashCode_IdenticalObjects_ReturnsSameHashCode() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act
		var hash1 = order1.CacheGetHashCode();
		var hash2 = order2.CacheGetHashCode();

		// Assert
		Assert.That(hash1, Is.EqualTo(hash2));
	}

	[Test]
	public void CacheGetHashCode_DifferentObjects_ReturnsDifferentHashCode() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.OrderId = "ORDER-999";

		// Act
		var hash1 = order1.CacheGetHashCode();
		var hash2 = order2.CacheGetHashCode();

		// Assert
		Assert.That(hash1, Is.Not.EqualTo(hash2));
	}

	[Test]
	public void CacheGetHashCode_SameObject_ReturnsConsistentHashCode() {
		// Arrange
		var order = CreateTestOrder();

		// Act
		var hash1 = order.CacheGetHashCode();
		var hash2 = order.CacheGetHashCode();
		var hash3 = order.CacheGetHashCode();

		// Assert
		Assert.That(hash1, Is.EqualTo(hash2));
		Assert.That(hash2, Is.EqualTo(hash3));
	}

	[Test]
	public void CacheEquals_IdenticalLists_ReturnsTrue() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentListContent_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.Tags.Add("extra-tag");

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheEquals_DifferentArrayContent_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.TrackingNumbers = new[] { "TN1", "TN2", "EXTRA" };

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheEquals_IdenticalHashSets_ReturnsTrue() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentHashSetContent_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.RelatedOrderIds.Add(999);

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheEqualityComparer_EqualObjects_ReturnsTrue() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var order1 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 100,
			Tags = new List<string> { "A", "B" }
		};
		var order2 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 100,
			Tags = new List<string> { "A", "B" }
		};

		// Act
		var result = comparer.Equals(order1, order2);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void CacheEqualityComparer_DifferentObjects_ReturnsFalse() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var order1 = new Order { OrderId = "ORDER-1", OrderNumber = 100 };
		var order2 = new Order { OrderId = "ORDER-2", OrderNumber = 200 };

		// Act
		var result = comparer.Equals(order1, order2);

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void CacheEqualityComparer_GetHashCode_EqualObjectsSameHash() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var order1 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 100,
			Tags = new List<string> { "A", "B" }
		};
		var order2 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 100,
			Tags = new List<string> { "A", "B" }
		};

		// Act
		var hash1 = comparer.GetHashCode(order1);
		var hash2 = comparer.GetHashCode(order2);

		// Assert
		Assert.That(hash1, Is.EqualTo(hash2));
	}

	[Test]
	public void CacheEqualityComparer_WorksWithHashSet() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var hashSet = new HashSet<Order>(comparer);
		var order1 = new Order { OrderId = "ORDER-1", OrderNumber = 100 };
		var order2 = new Order { OrderId = "ORDER-1", OrderNumber = 100 }; // Same content

		// Act
		hashSet.Add(order1);
		var added = hashSet.Add(order2); // Should not add duplicate

		// Assert
		Assert.That(added, Is.False);
		Assert.That(hashSet.Count, Is.EqualTo(1));
	}

	[Test]
	public void CacheEqualityComparer_WorksWithDictionary() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var dict = new Dictionary<Order, string>(comparer);
		var order1 = new Order { OrderId = "ORDER-1", OrderNumber = 100 };
		var order2 = new Order { OrderId = "ORDER-1", OrderNumber = 100 }; // Same content

		// Act
		dict[order1] = "Value1";
		dict[order2] = "Value2"; // Should replace, not add

		// Assert
		Assert.That(dict.Count, Is.EqualTo(1));
		Assert.That(dict[order1], Is.EqualTo("Value2"));
	}

	[Test]
	public void CacheEqualityComparer_NullHandling() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var order = new Order { OrderId = "ORDER-1" };

		// Act & Assert
		Assert.That(comparer.Equals(null, null), Is.True);
		Assert.That(comparer.Equals(order, null), Is.False);
		Assert.That(comparer.Equals(null, order), Is.False);
		Assert.That(comparer.GetHashCode(null!), Is.EqualTo(0));
	}

	[Test]
	public void CacheEqualityComparer_SameReference_ReturnsTrue() {
		// Arrange
		var comparer = Order.CacheEqualityComparer;
		var order = new Order { OrderId = "ORDER-1" };

		// Act
		var result = comparer.Equals(order, order);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void CacheEquals_HashSetsDifferentOrder_ReturnsTrue() {
		// Arrange
		var order1 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 1,
			OrderDate = new DateTime(2024, 1, 1),
			TotalAmount = 100,
			RelatedOrderIds = new HashSet<int> { 1, 2, 3 }
		};
		var order2 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 1,
			OrderDate = new DateTime(2024, 1, 1),
			TotalAmount = 100,
			RelatedOrderIds = new HashSet<int> { 3, 2, 1 }
		};

		// Act & Assert (HashSets are unordered, so order shouldn't matter)
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_IdenticalDictionaries_ReturnsTrue() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentDictionaryContent_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.Metadata["extra"] = "value";

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheEquals_IdenticalNestedObjects_ReturnsTrue() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentNestedObject_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = CreateTestOrder();
		order2.ShippingInfo.Carrier = "Different Carrier";

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	[Test]
	public void CacheEquals_NullNestedObject_HandlesCorrectly() {
		// Arrange
		var order1 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 1,
			OrderDate = DateTime.Now,
			TotalAmount = 100,
			ShippingInfo = null
		};
		var order2 = new Order {
			OrderId = "ORDER-1",
			OrderNumber = 1,
			OrderDate = order1.OrderDate,
			TotalAmount = 100,
			ShippingInfo = null
		};

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.True);
	}

	[Test]
	public void CacheEquals_OneNullNestedObject_ReturnsFalse() {
		// Arrange
		var order1 = CreateTestOrder();
		var order2 = new Order {
			OrderId = order1.OrderId,
			OrderNumber = order1.OrderNumber,
			OrderDate = order1.OrderDate,
			TotalAmount = order1.TotalAmount,
			ShippingInfo = null
		};

		// Act & Assert
		Assert.That(order1.CacheEquals(order2), Is.False);
	}

	private static Order CreateTestOrder() {
		return new Order {
			OrderId = "ORDER-123",
			OrderNumber = 12345,
			OrderDate = new DateTime(2024, 1, 1, 10, 30, 0),
			TotalAmount = 299.99m,
			Status = OrderStatus.Pending,
			Tags = new List<string> { "priority", "express" },
			TrackingNumbers = new[] { "TN1", "TN2" },
			RelatedOrderIds = new HashSet<int> { 100, 200 },
			Metadata = new Dictionary<string, string> {
				{ "source", "web" },
				{ "campaign", "summer2024" }
			},
			Customer = new Customer {
				CustomerId = 1,
				Name = "John Doe",
				Email = "john@example.com"
			},
			ShippingInfo = new ShippingInfo {
				TrackingId = "TRACK-123",
				Carrier = "FastShip",
				EstimatedDelivery = new DateTime(2024, 1, 5),
				Status = ShippingStatus.InTransit
			},
			Lines = new List<OrderLine>(),
			Payments = new List<Payment>()
		};
	}
}
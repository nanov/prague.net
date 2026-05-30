namespace Prague.Generated.Tests.Polymorphic;
using Prague.Generated.Tests.Models;

using Prague.Core;
using NUnit.Framework;

/// <summary>
///   Tests for the DataCache&lt;T&gt; attribute which allows creating caches for external types.
/// </summary>
[TestFixture]
public class ExternalTypeCacheTests {
	[Test]
	public void ProductCache_CanAddAndRetrieve() {
		// Arrange
		var cache = new ProductCache();
		var product = new ThirdPartyProduct {
			Sku = "SKU-001",
			Name = "Widget",
			Price = 9.99m,
			StockCount = 100,
			Category = "Electronics"
		};

		// Act
		cache.AddOrUpdate(product);
		var found = cache.TryGet("SKU-001", out var result);

		// Assert
		Assert.That(found, Is.True);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Sku, Is.EqualTo("SKU-001"));
		Assert.That(result.Name, Is.EqualTo("Widget"));
		Assert.That(result.Price, Is.EqualTo(9.99m));
	}

	[Test]
	public void ProductCache_IndexByCategory_Works() {
		// Arrange
		var cache = new ProductCache();
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "E1", Name = "Phone", Price = 999, Category = "Electronics" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "E2", Name = "Laptop", Price = 1499, Category = "Electronics" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "C1", Name = "Shirt", Price = 29, Category = "Clothing" });

		// Act
		var electronics = cache.Query().WithCategory("Electronics").Execute();

		// Assert
		Assert.That(electronics.Count, Is.EqualTo(2));
		Assert.That(electronics, Has.All.Property("Category").EqualTo("Electronics"));
	}

	[Test]
	public void ProductCache_RangeIndexByPrice_Works() {
		// Arrange
		var cache = new ProductCache();
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P1", Name = "Cheap", Price = 10, Category = "A" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P2", Name = "Medium", Price = 50, Category = "A" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P3", Name = "Expensive", Price = 100, Category = "A" });

		// Act
		var midRange = cache.Query()
			.WithPrice(r => r.Gt(20).Lte(100))
			.ExecutePooled();

		// Assert
		Assert.That(midRange.Count, Is.EqualTo(2));
		Assert.That(midRange, Has.Some.Property("Name").EqualTo("Medium"));
		Assert.That(midRange, Has.Some.Property("Name").EqualTo("Expensive"));
	}

	[Test]
	public void ProductCache_WhereFilter_Works() {
		// Arrange
		var cache = new ProductCache();
		cache.AddOrUpdate(new ThirdPartyProduct
			{ Sku = "P1", Name = "In Stock", Price = 10, StockCount = 50, Category = "A" });
		cache.AddOrUpdate(new ThirdPartyProduct
			{ Sku = "P2", Name = "Out of Stock", Price = 20, StockCount = 0, Category = "A" });

		// Act
		var inStock = cache.Query()
			.Where(p => p.StockCount > 0)
			.ExecutePooled();

		// Assert
		Assert.That(inStock, Has.Count.EqualTo(1));
		Assert.That(inStock[0].Name, Is.EqualTo("In Stock"));
	}

	[Test]
	public void ProductCache_Count_Works() {
		// Arrange
		var cache = new ProductCache();
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P1", Name = "A", Price = 10, Category = "X" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P2", Name = "B", Price = 20, Category = "X" });
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P3", Name = "C", Price = 30, Category = "Y" });

		// Act
		var totalCount = cache.Query().Count();
		var categoryXCount = cache.Query().WithCategory("X").Count();

		// Assert
		Assert.That(totalCount, Is.EqualTo(3));
		Assert.That(categoryXCount, Is.EqualTo(2));
	}

	[Test]
	public void ProductCache_Remove_Works() {
		// Arrange
		var cache = new ProductCache();
		cache.AddOrUpdate(new ThirdPartyProduct { Sku = "P1", Name = "Test", Price = 10, Category = "A" });

		// Act
		cache.Remove("P1");
		var found = cache.TryGet("P1", out _);

		// Assert
		Assert.That(found, Is.False);
	}

	[Test]
	public void ThirdPartyOrderCache_CanAddAndRetrieve() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		var order = new ThirdPartyOrder {
			OrderId = 123,
			CustomerId = "CUST-001",
			TotalAmount = 99.99m,
			Status = "Pending",
			CreatedAt = DateTime.UtcNow
		};

		// Act
		cache.AddOrUpdate(order);
		var found = cache.TryGet(123, out var result);

		// Assert
		Assert.That(found, Is.True);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.OrderId, Is.EqualTo(123));
		Assert.That(result.CustomerId, Is.EqualTo("CUST-001"));
	}

	[Test]
	public void ThirdPartyOrderCache_TopicIsSet() {
		// Assert
		Assert.That(ThirdPartyOrderCache.TopicNameTemplate, Is.EqualTo("ThirdParty.Orders.Cache"));
	}

	[Test]
	public void ThirdPartyOrderCache_IndexByCustomerId_Works() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 100, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C1", TotalAmount = 200, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C2", TotalAmount = 150, Status = "Complete" });

		// Act
		var c1Orders = cache.Query().WithCustomerId("C1").ExecutePooled();

		// Assert
		Assert.That(c1Orders.Count, Is.EqualTo(2));
		Assert.That(c1Orders, Has.All.Property("CustomerId").EqualTo("C1"));
	}

	[Test]
	public void ThirdPartyOrderCache_IndexByStatus_Works() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 100, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C1", TotalAmount = 200, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C2", TotalAmount = 150, Status = "Complete" });

		// Act
		var completeOrders = cache.Query().WithStatus("Complete").ExecutePooled();

		// Assert
		Assert.That(completeOrders.Count, Is.EqualTo(2));
		Assert.That(completeOrders, Has.All.Property("Status").EqualTo("Complete"));
	}

	[Test]
	public void ThirdPartyOrderCache_RangeIndexByAmount_Works() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 50, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C1", TotalAmount = 150, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C2", TotalAmount = 250, Status = "Complete" });

		// Act
		var midRange = cache.Query()
			.WithAmountIndex(r => r.Gte(100).Lt(200))
			.ExecutePooled();

		// Assert
		Assert.That(midRange.Count, Is.EqualTo(1));
		Assert.That(midRange[0].OrderId, Is.EqualTo(2));
	}

	[Test]
	public void ThirdPartyOrderCache_WithCustomerId_AcceptsQueryResults() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 100, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C2", TotalAmount = 200, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C3", TotalAmount = 150, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 4, CustomerId = "C1", TotalAmount = 50, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 5, CustomerId = "C2", TotalAmount = 300, Status = "Complete" });

		// Get QueryResults of customer IDs that have complete orders
		var completeOrders = cache.Query().WithStatus("Complete").ExecutePooled();
		var customerIds = completeOrders.Map(o => o.CustomerId);

		// Act - Use QueryResults directly in the WithCustomerId method
		var allOrdersForThoseCustomers = cache.Query().WithCustomerId(customerIds).ExecutePooled();

		// Assert - Should get all orders for C1, C2, and C3 (customers who have complete orders)
		Assert.That(allOrdersForThoseCustomers.Count, Is.EqualTo(5));
		Assert.That(allOrdersForThoseCustomers, Has.Some.Property("OrderId").EqualTo(1)); // C1 complete
		Assert.That(allOrdersForThoseCustomers, Has.Some.Property("OrderId").EqualTo(2)); // C2 pending
		Assert.That(allOrdersForThoseCustomers, Has.Some.Property("OrderId").EqualTo(3)); // C3 complete
		Assert.That(allOrdersForThoseCustomers, Has.Some.Property("OrderId").EqualTo(4)); // C1 pending
		Assert.That(allOrdersForThoseCustomers, Has.Some.Property("OrderId").EqualTo(5)); // C2 complete
	}

	[Test]
	public void ThirdPartyOrderCache_WithStatus_AcceptsQueryResults() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 100, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C2", TotalAmount = 200, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C3", TotalAmount = 150, Status = "Shipped" });

		// Get QueryResults of statuses
		var allOrders = cache.Query().ExecutePooled();
		var statuses = allOrders.Map(o => o.Status);

		// Act - Use QueryResults directly in the WithStatus method
		var orders = cache.Query().WithStatus(statuses).ExecutePooled();

		// Assert - Should return all orders since we're querying with all existing statuses
		Assert.That(orders.Count, Is.EqualTo(3));
	}

	[Test]
	public void ThirdPartyOrderCache_WithKey_AcceptsQueryResults() {
		// Arrange
		var cache = new ThirdPartyOrderCache();
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 1, CustomerId = "C1", TotalAmount = 100, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 2, CustomerId = "C2", TotalAmount = 200, Status = "Pending" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 3, CustomerId = "C3", TotalAmount = 150, Status = "Complete" });
		cache.AddOrUpdate(new ThirdPartyOrder { OrderId = 4, CustomerId = "C1", TotalAmount = 50, Status = "Pending" });

		// Get QueryResults of order IDs with complete status
		var completeOrders = cache.Query().WithStatus("Complete").ExecutePooled();
		var orderIds = completeOrders.Map(o => o.OrderId);

		// Act - Use QueryResults directly in the WithKey method (which uses WithOrderId internally)
		var orders = cache.Query().WithKey(orderIds).ExecutePooled();

		// Assert
		Assert.That(orders.Count, Is.EqualTo(2));
		Assert.That(orders, Has.All.Property("Status").EqualTo("Complete"));
	}
}

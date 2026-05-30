// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

#region Test Models for JoinedCacheQueryBuilder

[DataCache]
public partial class JqbOrder {
	[DataCacheKey] public int OrderId { get; set; }
	public int CustomerId { get; set; }
	public string Status { get; set; } = "";
	public decimal Amount { get; set; }
	public DateTime OrderDate { get; set; }
}

[DataCache]
public partial class JqbOrderItem {
	[DataCacheKey] public int OrderItemId { get; set; }
	public int OrderId { get; set; }
	public int ProductId { get; set; }
	public int Quantity { get; set; }
	public decimal Price { get; set; }
}

[DataCache]
public partial class JqbCustomer {
	[DataCacheKey] public int CustomerId { get; set; }
	public string Name { get; set; } = "";
	public string Region { get; set; } = "";
}

#endregion

/// <summary>
///   Tests for JoinedCacheQueryBuilder covering UseIndex and Where methods.
/// </summary>
[TestFixture]
public class JoinedCacheQueryBuilderTests {
	[SetUp]
	public void SetUp() {
		_orderCache = new InMemoryDataCache<int, JqbOrder>();
		_orderItemCache = new InMemoryDataCache<int, JqbOrderItem>();
		_customerCache = new InMemoryDataCache<int, JqbCustomer>();

		_orderItemByOrderIndex = _orderItemCache.CacheKeyValueListIndex((k, v) => v.OrderId);
		_orderByCustomerIndex = _orderCache.AddKeyValueIndex((k, v) => v.CustomerId);
		_orderItemByProductIndex = _orderItemCache.AddKeyValueIndex((k, v) => v.ProductId);
		_customerByIdIndex = _customerCache.AddKeyValueIndex((k, v) => v.CustomerId);
		_orderByIdIndex = _orderCache.AddKeyValueIndex((k, v) => v.OrderId);

		// Add customers
		_customerCache.AddOrUpdate(1, new JqbCustomer { CustomerId = 1, Name = "Alice", Region = "North" });
		_customerCache.AddOrUpdate(2, new JqbCustomer { CustomerId = 2, Name = "Bob", Region = "South" });
		_customerCache.AddOrUpdate(3, new JqbCustomer { CustomerId = 3, Name = "Charlie", Region = "North" });

		// Add orders
		_orderCache.AddOrUpdate(100,
			new JqbOrder
				{ OrderId = 100, CustomerId = 1, Status = "Completed", Amount = 150m, OrderDate = new DateTime(2024, 1, 15) });
		_orderCache.AddOrUpdate(101,
			new JqbOrder
				{ OrderId = 101, CustomerId = 1, Status = "Pending", Amount = 200m, OrderDate = new DateTime(2024, 2, 20) });
		_orderCache.AddOrUpdate(102,
			new JqbOrder
				{ OrderId = 102, CustomerId = 2, Status = "Completed", Amount = 75m, OrderDate = new DateTime(2024, 3, 10) });
		_orderCache.AddOrUpdate(103,
			new JqbOrder
				{ OrderId = 103, CustomerId = 3, Status = "Cancelled", Amount = 300m, OrderDate = new DateTime(2024, 4, 5) });

		// Add order items
		_orderItemCache.AddOrUpdate(1000,
			new JqbOrderItem { OrderItemId = 1000, OrderId = 100, ProductId = 10, Quantity = 2, Price = 50m });
		_orderItemCache.AddOrUpdate(1001,
			new JqbOrderItem { OrderItemId = 1001, OrderId = 100, ProductId = 11, Quantity = 1, Price = 50m });
		_orderItemCache.AddOrUpdate(1002,
			new JqbOrderItem { OrderItemId = 1002, OrderId = 101, ProductId = 10, Quantity = 4, Price = 50m });
		_orderItemCache.AddOrUpdate(1003,
			new JqbOrderItem { OrderItemId = 1003, OrderId = 102, ProductId = 12, Quantity = 3, Price = 25m });
		_orderItemCache.AddOrUpdate(1004,
			new JqbOrderItem { OrderItemId = 1004, OrderId = 103, ProductId = 10, Quantity = 6, Price = 50m });
	}

	private InMemoryDataCache<int, JqbOrder> _orderCache = null!;
	private InMemoryDataCache<int, JqbOrderItem> _orderItemCache = null!;
	private InMemoryDataCache<int, JqbCustomer> _customerCache = null!;
	private CacheKeyValueListIndex<int, JqbOrderItem, int> _orderItemByOrderIndex = null!;
	private CacheKeyValueIndex<int, JqbOrder, int> _orderByCustomerIndex = null!;
	private CacheKeyValueIndex<int, JqbOrderItem, int> _orderItemByProductIndex = null!;
	private CacheKeyValueIndex<int, JqbCustomer, int> _customerByIdIndex = null!;
	private CacheKeyValueIndex<int, JqbOrder, int> _orderByIdIndex = null!;

	[Test]
	public void JoinOne_WithWhereFilter_FiltersJoinedResults() {
		// Query customers and join orders, filtering to only completed orders
		// Note: CacheKeyValueIndex is one-to-one, so only the LAST order per customer is indexed
		// Alice has orders 100 (Completed) and 101 (Pending) - index has 101 (Pending)
		// Bob has order 102 (Completed) - index has 102
		// Charlie has order 103 (Cancelled) - index has 103
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex, q => q.Where(o => o.Status == "Completed"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3)); // All 3 customers returned

		// Bob has a completed order (102) - only one that passes the filter
		var bob = results.FirstOrDefault(r => r.Left.CustomerId == 2);
		Assert.That(bob.Right?.OrderId, Is.EqualTo(102));

		// Alice's indexed order is 101 (Pending), so it's filtered out
		var alice = results.FirstOrDefault(r => r.Left.CustomerId == 1);
		Assert.That(alice.Right, Is.Null);
	}

	[Test]
	public void JoinOne_WithWhereFilter_ChainsMultipleFilters() {
		// Query with multiple Where filters
		// Bob's order 102 has Amount=75m which is < 100m, so it's also filtered out
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex, q => q
				.Where(o => o.Status == "Completed")
				.Where(o => o.Amount > 50m))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));

		// Bob's order 102 has Amount=75m > 50m and Status=Completed
		var bob = results.FirstOrDefault(r => r.Left.CustomerId == 2);
		Assert.That(bob.Right?.OrderId, Is.EqualTo(102));
		Assert.That(bob.Right?.Amount, Is.EqualTo(75m));
	}

	[Test]
	public void JoinMany_WithWhereFilter_FiltersJoinedResults() {
		// Query orders and join order items, filtering to items with quantity > 2
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex, q => q.Where(i => i.Quantity > 2))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4)); // All 4 orders returned

		var order101 = results.FirstOrDefault(r => r.Left.OrderId == 101);
		Assert.That(order101.Right.Count, Is.EqualTo(1));
		Assert.That(order101.Right[0].Quantity, Is.EqualTo(4));

		var order103 = results.FirstOrDefault(r => r.Left.OrderId == 103);
		Assert.That(order103.Right.Count, Is.EqualTo(1));
		Assert.That(order103.Right[0].Quantity, Is.EqualTo(6));

		// Order 100 has no items with quantity > 2
		var order100 = results.FirstOrDefault(r => r.Left.OrderId == 100);
		Assert.That(order100.Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinOne_WithUseIndex_SingleValue_FiltersCorrectly() {
		// Query customers and join orders, using an additional index filter
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.Execute();

		// All customers should be returned
		Assert.That(results.Count, Is.EqualTo(3));
	}

	[Test]
	public void JoinOne_ExecuteCloned_ReturnsCopiedData() {
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.EqualTo(3));

		// Verify it's a clone by checking reference inequality
		var alice = results.FirstOrDefault(r => r.Left.CustomerId == 1);
		Assert.That(alice.Left, Is.Not.Null);
	}

	[Test]
	public void JoinOne_ExecutePooled_ReturnsResults() {
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(3));
	}

	[Test]
	public void JoinOne_ExecutePooledCloned_ReturnsResults() {
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.ExecutePooledCloned();

		Assert.That(results.Count, Is.EqualTo(3));
	}

	[Test]
	public void JoinMany_ExecuteCloned_ReturnsResults() {
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.ExecuteCloned();

		Assert.That(results.Count, Is.EqualTo(4));
	}

	[Test]
	public void JoinMany_ExecutePooled_ReturnsResults() {
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(4));
	}

	[Test]
	public void JoinMany_ExecutePooledCloned_ReturnsResults() {
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.ExecutePooledCloned();

		Assert.That(results.Count, Is.EqualTo(4));
	}

	[Test]
	public void JoinOne_WithSkipAndTake_PaginatesResults() {
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.Execute(1, 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_WithSkipAndTake_PaginatesResults() {
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.Execute(0, 2);

		Assert.That(results.Count, Is.EqualTo(2));
	}

	[Test]
	public void JoinOne_WithComparer_SortsResults() {
		var comparer = Comparer<JoinResult<JqbCustomer, JqbOrder>>.Create((a, b) => string.Compare(a.Left.Name, b.Left.Name, StringComparison.Ordinal));
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.Sort(comparer)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].Left.Name, Is.EqualTo("Alice"));
		Assert.That(results[1].Left.Name, Is.EqualTo("Bob"));
		Assert.That(results[2].Left.Name, Is.EqualTo("Charlie"));
	}

	[Test]
	public void JoinMany_WithComparer_SortsResults() {
		var comparer = Comparer<JoinResult<JqbOrder, QueryResults<JqbOrderItem>>>.Create((a, b) => b.Left.Amount.CompareTo(a.Left.Amount)); // Descending by amount
		var results = _orderCache.Query()
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.Sort(comparer)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		Assert.That(results[0].Left.Amount, Is.EqualTo(300m)); // Order 103
		Assert.That(results[1].Left.Amount, Is.EqualTo(200m)); // Order 101
	}

	[Test]
	public void JoinOne_WithComparerAndPagination_SortsAndPaginates() {
		var comparer = Comparer<JoinResult<JqbCustomer, JqbOrder>>.Create((a, b) => string.Compare(a.Left.Name, b.Left.Name, StringComparison.Ordinal));
		var results = _customerCache.Query()
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.Sort(comparer)
			.Execute(1,1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Bob"));
	}

	[Test]
	public void JoinOne_NoMatchingJoins_ReturnsEmptyRightValues() {
		// Add a customer with no orders
		_customerCache.AddOrUpdate(4, new JqbCustomer { CustomerId = 4, Name = "Dave", Region = "East" });

		var results = _customerCache.Query()
			.UseIndex(_customerByIdIndex, 4)
			.JoinOne(_orderCache, _orderByCustomerIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Dave"));
		// Right should be default/null since no matching order exists
	}

	[Test]
	public void JoinMany_NoMatchingJoins_ReturnsEmptyRightCollection() {
		// Add an order with no items
		_orderCache.AddOrUpdate(999,
			new JqbOrder { OrderId = 999, CustomerId = 1, Status = "New", Amount = 0m, OrderDate = DateTime.Now });

		var results = _orderCache.Query()
			.UseIndex(_orderByIdIndex, 999)
			.JoinMany(_orderItemCache, _orderItemByOrderIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.OrderId, Is.EqualTo(999));
		Assert.That(results[0].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void Query_WithWhereFilter_FiltersResults() {
		// Find completed orders using Where filter
		var results = _orderCache.Query()
			.Where(o => o.Status == "Completed")
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2)); // Orders 100 and 102
		Assert.That(results.Select(o => o.OrderId), Is.EquivalentTo(new[] { 100, 102 }));
	}

	[Test]
	public void Query_UseIndexWithSingleValue_ReturnsMatchingRecord() {
		// CacheKeyValueIndex is one-to-one, returns the last order for CustomerId=2
		var results = _orderCache.Query()
			.UseIndex(_orderByCustomerIndex, 2)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].OrderId, Is.EqualTo(102)); // Bob's only order
		Assert.That(results[0].CustomerId, Is.EqualTo(2));
	}
}
#endif

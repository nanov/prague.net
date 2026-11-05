// docs: every code block of www/docs/articles/how-to/test-without-a-broker.md — unit-testing code that
// reads Prague caches: construct caches directly or through the registry, seed with AddOrUpdate, drive
// after-handlers by hand, assert on pooled results, and build the Kafka container without starting it.
namespace Prague.Samples.HowTo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Prague.Core;
using Prague.Kafka;
using Prague.Samples.Models;

#region test-subject
/// <summary>The code under test: a read model over two caches. It neither knows nor cares where the data came from.</summary>
public sealed class CustomerStatement(OrderCache orders, CustomerCache customers) {
	public string? Describe(int customerId) {
		if (!customers.Cache.TryGet(customerId, out var customer))
			return null;
		using var open = orders.Query()
			.WithCustomerId(customerId)
			.Or(b => b.WithStatus(OrderStatus.Pending), b => b.WithStatus(OrderStatus.Paid))
			.ExecutePooled();
		return $"{customer.Name}: {open.Count} open order(s), {open.Sum(o => o.Total):0.00} outstanding";
	}
}
#endregion

[TestFixture]
public class TestWithoutABroker {
	[Test]
	public void StandaloneCache() {
		#region test-standalone-cache
		var orders = new OrderCache();                          // no registry, no Kafka, no DI
		orders.AddOrUpdate(NewOrder("ORD-001", 42, OrderStatus.Pending, 120m));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, OrderStatus.Cancelled, 80m), timestamp: 1_700_000_000_000);   // explicit timestamp

		using var open = orders.Query().WithCustomerId(42).WithStatus(OrderStatus.Pending).ExecutePooled();

		Assert.That(open.Count, Is.EqualTo(1));
		Assert.That(open[0].OrderId, Is.EqualTo("ORD-001"));
		Assert.That(open.Select(o => o.Total), Is.EqualTo(new[] { 120m }));   // QueryResults<T> is an IReadOnlyList<T>
		#endregion
	}

	[Test]
	public void RegistryForJoinsAndGlobalIndices() {
		#region test-registry
		var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // wires FK joins + CustomerActivityIndex
		var orders = registry.GetCache<OrderCache>();
		var customers = registry.GetCache<CustomerCache>();
		customers.AddOrUpdate(new Customer { Id = 42, Email = "ada@example.com", Country = "GB", Tier = CustomerTier.Gold, Name = "Ada" });
		orders.AddOrUpdate(NewOrder("ORD-001", 42, OrderStatus.Paid, 120m));

		var statement = new CustomerStatement(orders, customers);

		Assert.That(statement.Describe(42), Is.EqualTo("Ada: 1 open order(s), 120.00 outstanding"));
		Assert.That(statement.Describe(7), Is.Null);
		#endregion

		// Two stand-alone caches can be joined too, once: orders.AddJoinedCaches(customers, lines, shipments).
		using var joined = orders.Query().WithCustomerId(42).JoinWithCustomer().ExecutePooled();
		Assert.That(joined[0].Right!.Name, Is.EqualTo("Ada"));
	}

	[Test]
	public async Task DriveAnAfterHandlerByHand() {
		#region test-after-handler
		var projector = new OrderProjector();                   // the ICacheAfterHandler<string, Order> from "React to changes"
		var order = NewOrder("ORD-001", 42, OrderStatus.Pending, 100m);

		await projector.Handle(UpdateType.Add, order.OrderId, order, null);
		await projector.Handle(UpdateType.Delete, order.OrderId, null, order);

		Assert.That(projector.OpenTotalByCustomer[42], Is.Zero);
		#endregion
	}

	[Test]
	public void AssertingOnPooledResults() {
		var orders = new OrderCache();
		orders.AddOrUpdate(NewOrder("ORD-001", 42, OrderStatus.Paid, 120m));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, OrderStatus.Paid, 80m));

		#region test-pooled-assert
		using var rows = orders.Query().WithCustomerId(42).ExecutePooled();   // `using`: the buffer goes back when the test ends

		Assert.That(rows.Count, Is.EqualTo(2));
		Assert.That(rows.Select(o => o.OrderId), Is.EquivalentTo(new[] { "ORD-001", "ORD-002" }));   // order is not asserted
		Assert.That(rows.TotalCount, Is.EqualTo(2));

		var ids = rows.Map(o => o.OrderId);                     // consumes `rows`; the later `using` Dispose is a no-op
		using (ids)
			Assert.That(ids.Count, Is.EqualTo(2));
		#endregion
	}

	[Test]
	public void FrozenAgainstEager() {
		var orders = new OrderCache();
		for (var i = 0; i < 20; i++)
			orders.AddOrUpdate(NewOrder($"ORD-{i:000}", i % 3, i % 2 == 0 ? OrderStatus.Paid : OrderStatus.Pending, i));

		#region test-frozen-vs-eager
		var frozen = orders.Prepare<int>().WithCustomerId(static c => c).WithStatus(OrderStatus.Paid).BuildFrozen();

		foreach (var customerId in new[] { 0, 1, 2, 99 }) {
			using var expected = orders.Query().WithCustomerId(customerId).WithStatus(OrderStatus.Paid).ExecutePooled();
			using var actual = frozen.ExecutePooled(customerId);
			// Same rows, same counts; the unsorted order is not part of the frozen contract, so compare as sets.
			Assert.That(actual.Select(o => o.OrderId), Is.EquivalentTo(expected.Select(o => o.OrderId)));
			Assert.That(actual.TotalCount, Is.EqualTo(expected.TotalCount));
		}
		#endregion
	}

	[Test]
	public void TheKafkaContainerBuildsWithoutAConnection() {
		#region test-di-container
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());   // required by the options binder
		services.AddSingleton<OrderProjector>();                                      // one instance: the cache's handler and what readers inject
		services.AddKafkaCaches(o => o.BootstrapServers = "unreachable:9092",   // never contacted: no consumer is created
			b => b.AddCache<OrderCache, string, Order>().WithAfterHandler(sp => sp.GetRequiredService<OrderProjector>()));
		// CustomerCache rides in through the FK: it is in the registry but not a DI service until you expose it.
		services.AddSingleton(sp => sp.GetRequiredService<IDataCacheRegistry>().GetCache<CustomerCache>());
		services.AddSingleton<CustomerStatement>();

		using var provider = services.BuildServiceProvider();   // do NOT resolve/start IHostedService in a unit test
		var orders = provider.GetRequiredService<OrderCache>();
		orders.AddOrUpdate(NewOrder("ORD-001", 42, OrderStatus.Paid, 10m));   // seed the real singleton the app would use

		var projector = provider.GetRequiredService<OrderProjector>();
		Assert.That(provider.GetRequiredService<ICacheAfterHandler<string, Order>>(), Is.SameAs(projector));
		Assert.That(projector.OpenTotalByCustomer, Is.Empty);   // seeding runs no after-handler: only the consumer calls them
		Assert.That(provider.GetRequiredService<CustomerStatement>().Describe(42), Is.Null);   // no customer seeded
		#endregion
	}

	private static Order NewOrder(string orderId, int customerId, OrderStatus status, decimal total) => new() {
		OrderId = orderId,
		CustomerId = customerId,
		CreatedAtUnixMs = 1_000,
		Status = status,
		Total = total,
	};
}

// docs: every code block of www/docs/articles/reference/attributes.md. The shared entities are in
// Models/ (Order, Customer, Product, OrderLine, Shipment, CustomerActivityIndex); the ones that exist
// only for this page are in ReferenceModels.cs. Each #region is one block the article pulls in verbatim.
namespace Prague.Samples.Reference;

using System.Text.Json;
using Nanov.Kafka;
using NUnit.Framework;
using Prague.Core;
using Prague.Kafka.SerDe;
using Prague.Samples.Models;
using Prague.Samples.Serialization;

[TestFixture]
public class Attributes {
	[Test]
	public void DataCache() {
		#region data-cache
		var orders = new OrderCache();                      // [DataCache]            on Order      → OrderCache
		var audit = new AuditLog();                         // [DataCache("AuditLog")] on AuditEvent → AuditLog
		#endregion

		Assert.That(orders.Query().Count(), Is.Zero);
		Assert.That(audit.Query().Count(), Is.Zero);
	}

	[Test]
	public void DataCacheKey() {
		var cache = new OrderCache();
		var order = NewOrder("ORD-001", 42, Now);

		#region data-cache-key
		cache.AddOrUpdate(order);                                        // key = order.OrderId

		var found = cache.TryGet("ORD-001", out var byKey);              // point lookup
		var viaQuery = cache.Query().WithOrderId("ORD-001").Execute();   // With{KeyProperty}; WithKey(...) is the same lane
		#endregion

		Assert.That(found, Is.True);
		Assert.That(byKey, Is.SameAs(order));
		Assert.That(viaQuery.Count, Is.EqualTo(1));
		Assert.That(cache.Query().WithKey("ORD-001").Count(), Is.EqualTo(1));
	}

	[Test]
	public void IndexUnique() {
		var customers = new CustomerCache();
		customers.AddOrUpdate(NewCustomer(42, "alice@example.com"));
		customers.AddOrUpdate(NewCustomer(7, "bob@example.com"));

		#region index-unique
		var alice = customers.Query().WithEmail("alice@example.com").Execute();
		var known = customers.EmailIndex.TryGetValue("alice@example.com", out var customerId);   // index value → key
		#endregion

		Assert.That(alice.Count, Is.EqualTo(1));
		Assert.That(known, Is.True);
		Assert.That(customerId, Is.EqualTo(42));
	}

	[Test]
	public void IndexMany() {
		var orders = new OrderCache();
		orders.AddOrUpdate(NewOrder("ORD-001", 42, Now));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, Now, status: OrderStatus.Paid));
		orders.AddOrUpdate(NewOrder("ORD-003", 7, Now, status: OrderStatus.Shipped));

		#region index-many
		var pending = orders.Query().WithStatus(OrderStatus.Pending).Execute();
		var paidOrShipped = orders.Query().WithStatus(new[] { OrderStatus.Paid, OrderStatus.Shipped }).Execute();   // OR inside one lane
		var customerOrderIds = orders.CustomerIdIndex.GetValues(42);                                        // index value → keys
		#endregion

		Assert.That(pending.Count, Is.EqualTo(1));
		Assert.That(paidOrShipped.Count, Is.EqualTo(2));
		Assert.That(customerOrderIds, Is.EquivalentTo(new[] { "ORD-001", "ORD-002" }));
	}

	[Test]
	public void IndexRange() {
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, price: 5m));
		products.AddOrUpdate(NewProduct(2, price: 25m));
		products.AddOrUpdate(NewProduct(3, price: 75m));

		#region index-range
		var midPriced = products.Query().WithPrice(q => q.Gte(10m).Lt(50m)).Execute();   // Gt / Gte / Lt / Lte sub-builder
		var expensiveIds = products.PriceIndex.GetValuesGte(50m);                        // sorted probe on the index itself
		#endregion

		Assert.That(midPriced.Count, Is.EqualTo(1));
		Assert.That(midPriced[0].Id, Is.EqualTo(2));
		Assert.That(expensiveIds, Is.EquivalentTo(new[] { 3 }));
	}

	[Test]
	public void IndexSymmetric() {
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, categoryId: 3));
		products.AddOrUpdate(NewProduct(2, categoryId: 3));

		#region index-symmetric
		var inCategory = products.CategoryIdIndex.GetValues(3);                                       // forward: value → keys
		var known = products.CategoryIdIndex.Reverse.TryGetValue(2, out var categoryOfProduct2);      // reverse: key → value
		#endregion

		Assert.That(inCategory, Is.EquivalentTo(new[] { 1, 2 }));
		Assert.That(known, Is.True);
		Assert.That(categoryOfProduct2, Is.EqualTo(3));
	}

	[Test]
	public void IndexCapacityHint() {
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, brand: "Acme"));

		#region index-capacity
		var acme = products.Query().WithBrand("Acme").Execute();     // [DataCacheIndex(DataCacheIndexType.Many, 4)]: same API, smaller buckets
		#endregion

		Assert.That(acme.Count, Is.EqualTo(1));
	}

	[Test]
	public void ForeignKey() {
		var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();
		var orders = registry.GetCache<OrderCache>();
		var customers = registry.GetCache<CustomerCache>();
		var shipments = registry.GetCache<ShipmentCache>();
		customers.AddOrUpdate(NewCustomer(42, "alice@example.com"));
		orders.AddOrUpdate(NewOrder("ORD-001", 42, Now));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, Now));
		shipments.AddOrUpdate(new Shipment { ShipmentId = "SHP-1", OrderId = "ORD-001", Carrier = "DHL", ShippedAtUnixMs = Now });

		#region foreign-key
		using var withCustomer = orders.Query().WithCustomerId(42).JoinWithCustomer().ExecutePooled();   // forward: Right is Customer?
		foreach (var row in withCustomer)
			Console.WriteLine($"{row.Left.OrderId} → {row.Right?.Name}");

		using var fanOut = customers.Query().WithId(42).JoinWithOrder().ExecutePooled();                // reverse: Right is QueryResults<Order>
		using var shipped = orders.Query().InnerJoinWithShipment().ExecutePooled();                     // 1:1; drops orders without a shipment
		#endregion

		Assert.That(withCustomer.Count, Is.EqualTo(2));
		Assert.That(withCustomer[0].Right?.Name, Is.EqualTo("Customer 42"));
		Assert.That(fanOut.Count, Is.EqualTo(1));
		Assert.That(fanOut[0].Right.Count, Is.EqualTo(2));
		Assert.That(shipped.Count, Is.EqualTo(1));
		Assert.That(shipped[0].Right?.Carrier, Is.EqualTo("DHL"));
	}

	[Test]
	public void ForeignKeySelector() {
		var registry = new DataCacheRegistryBuilder().Register<AuditLog>().Build();
		var audit = registry.GetCache<AuditLog>();
		var orders = registry.GetCache<OrderCache>();
		orders.AddOrUpdate(NewOrder("ORD-001", 42, Now));
		audit.AddOrUpdate(NewAuditEvent(1, "ORD-001#3"));

		#region foreign-key-selector
		using var rows = audit.Query().WithOrderRef("ORD-001#3").JoinWithOrder().ExecutePooled();   // filter on the raw value; selector runs at join time
		#endregion

		Assert.That(rows.Count, Is.EqualTo(1));
		Assert.That(rows[0].Right?.OrderId, Is.EqualTo("ORD-001"));
	}

	[Test]
	public void ForeignKeyDualForm() {
		var registry = new DataCacheRegistryBuilder().Register<StockLevelCache>().Build();
		var warehouses = registry.GetCache<WarehouseCache>();
		var stock = registry.GetCache<StockLevelCache>();
		warehouses.AddOrUpdate(new Warehouse { Id = 1, Code = "PRG" });
		stock.AddOrUpdate(new StockLevel { Id = "PRG/1", WarehouseId = 1, Quantity = 10 });
		stock.AddOrUpdate(new StockLevel { Id = "PRG/2", WarehouseId = 1, Quantity = 0 });

		#region foreign-key-dual-form
		using var perWarehouse = warehouses.Query().JoinWithStockLevel().ExecutePooled();                      // Right is QueryResults<StockLevel>
		using var withWarehouse = stock.Query().WithWarehouseId(1).JoinWithWarehouse().ExecutePooled();         // Right is Warehouse?
		#endregion

		Assert.That(perWarehouse.Count, Is.EqualTo(1));
		Assert.That(perWarehouse[0].Right.Count, Is.EqualTo(2));
		Assert.That(withWarehouse.Count, Is.EqualTo(2));
		Assert.That(withWarehouse[0].Right?.Code, Is.EqualTo("PRG"));
	}

	[Test]
	public void HasValueIndex() {
		var orders = new OrderCache();
		orders.AddOrUpdate(NewOrder("ORD-001", 42, Now, settledAt: Now));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, Now));
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, isPublished: true));
		products.AddOrUpdate(NewProduct(2, isPublished: false));

		#region has-value-index
		var settled = orders.Query().WithSettledAtUnixMs().Execute();        // nullable: SettledAtUnixMs != null
		var published = products.Query().WithIsPublished().Execute();        // bool: IsPublished == true
		var isSettled = orders.HasSettledAtUnixMsIndex.Contains("ORD-001");  // the key set itself
		#endregion

		Assert.That(settled.Count, Is.EqualTo(1));
		Assert.That(published.Count, Is.EqualTo(1));
		Assert.That(isSettled, Is.True);
	}

	[Test]
	public void HasNotValueIndex() {
		var audit = new AuditLog();
		audit.AddOrUpdate(NewAuditEvent(1, "ORD-001", resolvedAt: Now));
		audit.AddOrUpdate(NewAuditEvent(2, "ORD-001"));

		#region has-not-value-index
		var open = audit.Query().WithoutResolvedAtUnixMs().Execute();        // ResolvedAtUnixMs == null
		var isOpen = audit.HasNotResolvedAtUnixMsIndex.Contains(2);
		#endregion

		Assert.That(open.Count, Is.EqualTo(1));
		Assert.That(open[0].EventId, Is.EqualTo(2));
		Assert.That(isOpen, Is.True);
	}

	[Test]
	public void ValueIndex() {
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, stock: 5));
		products.AddOrUpdate(NewProduct(2, stock: 0));
		var audit = new AuditLog();
		audit.AddOrUpdate(NewAuditEvent(1, "ORD-001", attempts: 1));
		audit.AddOrUpdate(NewAuditEvent(2, "ORD-001", attempts: 3));

		#region value-index
		var inStock = products.Query().WithInStock().Execute();                       // IndexName = "InStock": field InStock, method WithInStock()
		var retried = audit.Query().WithAttemptsGreaterThanOrEqual3().Execute();     // default name: {Property}{Op}{Value}
		var isInStock = products.InStock.Contains(1);
		#endregion

		Assert.That(inStock.Count, Is.EqualTo(1));
		Assert.That(retried.Count, Is.EqualTo(1));
		Assert.That(retried[0].EventId, Is.EqualTo(2));
		Assert.That(isInStock, Is.True);
	}

	[Test]
	public void NoValueIndex() {
		var audit = new AuditLog();
		audit.AddOrUpdate(NewAuditEvent(1, "ORD-001", severity: "info"));
		audit.AddOrUpdate(NewAuditEvent(2, "ORD-001", severity: "error"));

		#region no-value-index
		var notInfo = audit.Query().WithNotInfo().Execute();      // NOT (Severity == "info"); IndexName = "NotInfoIndex" → WithNotInfo()
		#endregion

		Assert.That(notInfo.Count, Is.EqualTo(1));
		Assert.That(notInfo[0].Severity, Is.EqualTo("error"));
		Assert.That(audit.NotInfoIndex.Contains(2), Is.True);
	}

	[Test]
	public void GlobalLastUpdateIndex() {
		var t1 = Now - DayMs;
		var t2 = Now;

		#region global-last-update-index
		var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // pulls CustomerCache in; one shared CustomerActivityIndex
		var customers = registry.GetCache<CustomerCache>();
		var orders = registry.GetCache<OrderCache>();

		customers.AddOrUpdate(NewCustomer(42, "alice@example.com"), timestamp: t1);
		orders.AddOrUpdate(NewOrder("ORD-001", 42, t2), timestamp: t2);              // stamps customer 42 at t2 as well

		var activity = registry.GetGlobalIndex<CustomerActivityIndex>();
		var any = activity.TryGetMax(out var lastChangeMs, out var customerId);        // (t2, 42)

		using var changed = customers.Query().UpdatedAfter(t1).ExecutePooled();         // group key == PK → UpdatedAfter on CustomerCache
		#endregion

		Assert.That(any, Is.True);
		Assert.That(lastChangeMs, Is.EqualTo(t2));
		Assert.That(customerId, Is.EqualTo(42));
		Assert.That(changed.Count, Is.EqualTo(1));
		Assert.That(customers.Query().UpdatedAfter(t2).Count(), Is.Zero);
	}

	[Test]
	public void GlobalLastUpdateIndexFilteredAndTimestampProperty() {
		var registry = new DataCacheRegistryBuilder().Register<AuditLog>().Build();
		var audit = registry.GetCache<AuditLog>();

		#region global-index-filtered
		audit.AddOrUpdate(NewAuditEvent(1, "ORD-001", customerId: 7, occurredAt: 1_000, severity: "info"));
		audit.AddOrUpdate(NewAuditEvent(2, "ORD-001", customerId: 7, occurredAt: 2_000, severity: "error"));

		var all = registry.GetGlobalIndex<AuditActivityIndex>();     // timestamp = OccurredAtUnixMs, not the AddOrUpdate time
		var errors = registry.GetGlobalIndex<AuditErrorIndex>();     // only rows ErrorsOnly.Include(...) admits
		#endregion

		Assert.That(all.TryGetMax(out var allMax, out var allKey) && allMax == 2_000 && allKey == 7);
		Assert.That(all.GetEntitiesCount(7), Is.EqualTo(2));
		Assert.That(errors.GetEntitiesCount(7), Is.EqualTo(1));
		Assert.That(errors.TryGetMin(out var errorsMin) && errorsMin == 2_000);
	}

	[Test]
	public void Sort() {
		var products = new ProductCache();
		products.AddOrUpdate(NewProduct(1, price: 30m));
		products.AddOrUpdate(NewProduct(2, price: 10m));
		products.AddOrUpdate(NewProduct(3, price: 20m));

		#region sort
		var cheapestFirst = products.Query().Sort(ProductCache.ByPriceComparer).Execute();   // [DataCacheSort("ByPrice", "Price:1", "Id:1")]
		#endregion

		Assert.That(cheapestFirst.Select(p => p.Id), Is.EqualTo(new[] { 2, 3, 1 }));
	}

	[Test]
	public void IgnoreEquality() {
		var products = new ProductCache();
		var stored = NewProduct(1, lastSeenAt: 1_000);
		products.AddOrUpdate(stored);

		#region ignore-equality
		var seenAgain = NewProduct(1, lastSeenAt: 2_000);                    // identical except LastSeenAtUnixMs, marked [DataCacheIgnoreEquality]

		var equal = stored.CacheEquals(seenAgain);                           // generated structural equality
		var changed = products.AddOrUpdate(seenAgain, out var previous);     // false when equal: nothing swapped, reported as Same
		#endregion

		// The generated CacheEqualityHelper skips [DataCacheIgnoreEquality] properties, so the two
		// instances are equal and the write is reported as Same: nothing is swapped.
		Assert.That(equal, Is.True);
		Assert.That(changed, Is.False);
		Assert.That(previous, Is.Null);
	}

	[Test]
	public void Topic() {
		#region topic
		var template = OrderCache.TopicNameTemplate;      // "orders.[v:env]" — [v:env] resolved from KafkaCachesOptions.Vars at startup
		var defaulted = ProductCache.TopicNameTemplate;   // "Cache.Product" — no [DataCacheTopic]
		var onInstance = new OrderCache().TopicTemplate;  // the same string through the IDataCache instance
		#endregion

		Assert.That(template, Is.EqualTo("orders.[v:env]"));
		Assert.That(defaulted, Is.EqualTo("Cache.Product"));
		Assert.That(onInstance, Is.EqualTo(template));
		Assert.That(LegacyProductCache.TopicNameTemplate, Is.EqualTo("legacy.products"));
	}

	[Test]
	public void HeaderAndTimestamp() {
		var evt = NewAuditEvent(1, "ORD-001");
		evt.TenantId = 9;

		#region header-and-timestamp
		var headers = new Headers();
		AuditEvent.Derich(evt, headers);                                        // generated: [DataCacheHeader] properties → Kafka headers

		var present = headers.TryGetLastBytes("tenant-id", out var bytes);
		HeadersSerDe.TryDeserializeMessagePackExact<int>(bytes, out var tenantId);   // the consumer side runs the inverse (Enrich)
		#endregion

		Assert.That(present, Is.True);
		Assert.That(tenantId, Is.EqualTo(9));
		Assert.That(headers.Count, Is.EqualTo(1));                                  // [DataCacheFromTimestamp] is consume-only: no header
	}

	[Test]
	public void ExternalCache() {
		#region external-cache-usage
		var legacy = new LegacyProductCache();
		legacy.AddOrUpdate(new LegacyProductRecord { Code = 1, Category = "tools", Price = 9.5m });
		legacy.AddOrUpdate(new LegacyProductRecord { Code = 2, Category = "tools", Price = 19m });

		var tools = legacy.Query().WithCategory("tools").Execute();
		var under10 = legacy.Query().WithPriceBand(q => q.Lt(10m)).Execute();   // named index → WithPriceBand
		#endregion

		Assert.That(tools.Count, Is.EqualTo(2));
		Assert.That(under10.Count, Is.EqualTo(1));
		Assert.That(under10[0].Code, Is.EqualTo(1));
	}

	[Test]
	public void DataCacheFrom() {
		#region datacache-from-usage
		var suppliers = new SupplierCache();                       // Supplier is an ordinary [DataCache] once its properties exist
		suppliers.AddOrUpdate(new Supplier { Id = 1, Name = "Acme" });
		#endregion

		Assert.That(suppliers.TryGet(1, out var acme), Is.True);
		Assert.That(acme!.Name, Is.EqualTo("Acme"));
	}

	[Test]
	public void JsonResolver() {
		#region json-resolver
		var resolver = new DataCachesResolver();                   // generated into {RootNamespace}.Serialization for every [DataCache] type
		var unknown = resolver.GetTypeInfo(typeof(string), new JsonSerializerOptions());   // null: not a QueryResults<T> of a cache type
		#endregion

		Assert.That(unknown, Is.Null);
		// Asking it for a QueryResults<T> it does know (typeof(QueryResults<Order>)) currently throws
		// ArgumentNullException from JsonMetadataServices.CreateValueInfo(options, converter: null).
		Assert.That(() => resolver.GetTypeInfo(typeof(QueryResults<Order>), new JsonSerializerOptions()),
			Throws.ArgumentNullException);
	}

	[Test]
	public void GeneratedSurface() {
		var now = Now;

		#region generated-surface
		var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Build();   // ICacheRegisterable<OrderCache>: pulls Customer, OrderLine, Shipment and the index in
		var orders = registry.GetCache<OrderCache>();

		// writes — the key comes from [DataCacheKey]
		orders.AddOrUpdate(NewOrder("ORD-001", 42, now));
		orders.AddOrUpdate(NewOrder("ORD-002", 42, now - DayMs), timestamp: now);
		var changed = orders.AddOrUpdate(NewOrder("ORD-002", 42, now - DayMs), out var previous);   // false: CacheEquals says Same
		var removed = orders.Remove("ORD-002", out var gone);

		// reads
		var hit = orders.TryGet("ORD-001", out var order);
		var raw = orders.Cache;                                                        // InMemoryDataCache<string, Order>
		var statistics = orders.Statistics;

		// one index field per attribute
		var byCustomer = orders.CustomerIdIndex.GetValues(42);                         // [DataCacheForeignKey] ManyToOne → CacheSymmetricKeyValueListIndex
		var recent = orders.CreatedAtUnixMsIndex.GetValuesGte(now - DayMs);            // [DataCacheIndex(Range)]          → CacheRangeIndex
		var pending = orders.StatusIndex.GetValues(OrderStatus.Pending);               // [DataCacheIndex(Many)]           → CacheKeyValueListIndex
		var settled = orders.HasSettledAtUnixMsIndex.Contains("ORD-001");              // [DataCacheHasValueIndex]         → CacheKeySetIndex

		// one With{Property} per index, one JoinWith{Other} per foreign key, one comparer per [DataCacheSort]
		using var rows = orders.Query()
			.WithOrderId("ORD-001")                                                    // [DataCacheKey]  (WithKey(...) is the same lane)
			.WithCustomerId(42)                                                        // [DataCacheForeignKey]
			.WithStatus(OrderStatus.Pending)                                           // [DataCacheIndex(Many)]
			.WithCreatedAtUnixMs(q => q.Gte(now - DayMs))                              // [DataCacheIndex(Range)]
			.JoinWithCustomer()                                                        // forward 1:1 → Right  is Customer?
			.JoinWithOrderLine()                                                       // reverse 1:N → Right2 is QueryResults<OrderLine>
			.JoinWithShipment()                                                        // reverse 1:1 → Right3 is Shipment?
			.ExecutePooled();
		var unsettled = orders.Query().WithSettledAtUnixMs().Count();                  // [DataCacheHasValueIndex]
		var newestFirst = orders.Query().Sort(OrderCache.ByCreatedAtComparer).Execute();   // [DataCacheSort("ByCreatedAt", "CreatedAtUnixMs:-1", "OrderId:1")]
		var withShipment = orders.Query().InnerJoinWithShipment().Count();            // inner flavour of every JoinWith

		// the prepared twin — same With{Property} names, planned once
		var byCustomerPrepared = orders.Prepare<int>().WithCustomerId(static customerId => customerId).BuildFrozen();
		using var forCustomer42 = byCustomerPrepared.ExecutePooled(42);

		// Kafka and global-index surface
		var topic = OrderCache.TopicNameTemplate;                                      // [DataCacheTopic("orders.[v:env]")]
		var activity = registry.GetGlobalIndex<CustomerActivityIndex>();              // [DataCacheGlobalLastUpdateIndex<CustomerActivityIndex>] on CustomerId
		#endregion

		Assert.That(changed, Is.False);
		Assert.That(previous, Is.Null);
		Assert.That(removed, Is.True);
		Assert.That(gone!.OrderId, Is.EqualTo("ORD-002"));
		Assert.That(hit, Is.True);
		Assert.That(order!.OrderId, Is.EqualTo("ORD-001"));
		Assert.That(raw.TryGet("ORD-001", out _), Is.True);
		Assert.That(statistics, Is.Not.Null);
		Assert.That(byCustomer, Is.EquivalentTo(new[] { "ORD-001" }));
		Assert.That(recent, Is.EquivalentTo(new[] { "ORD-001" }));
		Assert.That(pending, Is.EquivalentTo(new[] { "ORD-001" }));
		Assert.That(settled, Is.False);
		Assert.That(rows.Count, Is.EqualTo(1));
		Assert.That(rows[0].Right, Is.Null);                                           // no customer loaded: outer join keeps the row
		Assert.That(rows[0].Right2.Count, Is.Zero);
		Assert.That(rows[0].Right3, Is.Null);
		Assert.That(unsettled, Is.Zero);
		Assert.That(newestFirst.Count, Is.EqualTo(1));
		Assert.That(withShipment, Is.Zero);
		Assert.That(forCustomer42.Count, Is.EqualTo(1));
		Assert.That(topic, Is.EqualTo("orders.[v:env]"));
		Assert.That(activity.GetEntitiesCount(42), Is.EqualTo(1));
	}

	private const long DayMs = 24 * 60 * 60 * 1000L;

	private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	private static Order NewOrder(string orderId, int customerId, long createdAtUnixMs,
		OrderStatus status = OrderStatus.Pending, long? settledAt = null) => new() {
		OrderId = orderId,
		CustomerId = customerId,
		CreatedAtUnixMs = createdAtUnixMs,
		Status = status,
		SettledAtUnixMs = settledAt,
		Total = 99.99m,
	};

	private static Customer NewCustomer(int id, string email) => new() {
		Id = id,
		Email = email,
		Country = "CZ",
		Tier = CustomerTier.Standard,
		Name = $"Customer {id}",
	};

	private static Product NewProduct(int id, decimal price = 10m, int categoryId = 1, string brand = "Acme",
		int stock = 1, bool isPublished = true, long lastSeenAt = 0) => new() {
		Id = id,
		Sku = $"SKU-{id}",
		CategoryId = categoryId,
		Brand = brand,
		Price = price,
		Stock = stock,
		IsPublished = isPublished,
		Name = $"Product {id}",
		LastSeenAtUnixMs = lastSeenAt,
	};

	private static AuditEvent NewAuditEvent(long eventId, string orderRef, int customerId = 42, long occurredAt = 1_000,
		string severity = "info", int attempts = 1, long? resolvedAt = null) => new() {
		EventId = eventId,
		OrderRef = orderRef,
		CustomerId = customerId,
		OccurredAtUnixMs = occurredAt,
		Severity = severity,
		Attempts = attempts,
		ResolvedAtUnixMs = resolvedAt,
	};
}

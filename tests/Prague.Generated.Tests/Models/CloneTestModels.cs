namespace Prague.Generated.Tests.Models;

using Prague.Core;

// Root entity with [DataCache] - will have Clone() generated
[DataCache]
[DataCacheTopic]
public partial class Order {
	[DataCacheKey] public string OrderId { get; set; }

	public int OrderNumber { get; set; }
	public DateTime OrderDate { get; set; }
	public decimal TotalAmount { get; set; }
	public OrderStatus Status { get; set; }
	public OrderStatus? PreviousStatus { get; set; }

	// Collections of primitives
	public List<string> Tags { get; set; } = new();
	public string[] TrackingNumbers { get; set; } = Array.Empty<string>();
	public HashSet<int> RelatedOrderIds { get; set; } = new();

	// Dictionary
	public Dictionary<string, string> Metadata { get; set; } = new();
	public Dictionary<int, decimal> PaymentInstallments { get; set; } = new();

	// Dictionary with reference type values (shallow copy - no [DataCache])
	public Dictionary<string, Customer> CustomersByRegion { get; set; } = new();

	// Dictionary with clonable reference type values (deep clone - has [DataCache])
	public Dictionary<int, Payment> PaymentsById { get; set; } = new();

	// Nested object WITHOUT [DataCache] - will be shallow copied
	public Customer Customer { get; set; }

	// Collection of nested objects
	public List<OrderLine> Lines { get; set; } = new();
	public OrderLine[] ExpressLines { get; set; } = Array.Empty<OrderLine>();

	// Nested object WITH [DataCache] - will be deep cloned
	public ShippingInfo ShippingInfo { get; set; }

	// Collection of entities with [DataCache]
	public List<Payment> Payments { get; set; } = new();
}

// Test entity with IList and ICollection to verify runtime type checking optimization
[DataCache]
[DataCacheTopic]
public partial class OrderWithInterfaces {
	[DataCacheKey] public string OrderId { get; set; }

	// IList and ICollection - should check runtime type and use optimized path if it's List<T>
	public IList<string> Tags { get; set; } = new List<string>();
	public ICollection<int> Quantities { get; set; } = new List<int>();
	public IList<OrderLine> Lines { get; set; } = new List<OrderLine>();
	public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

// Nested entity WITH [DataCache] - will have Clone() generated
[DataCache]
[DataCacheTopic("CustomShipping")]
public partial class ShippingInfo {
	[DataCacheKey] public string TrackingId { get; set; }

	public string Carrier { get; set; }
	public DateTime? EstimatedDelivery { get; set; }
	public ShippingStatus Status { get; set; }

	// Nested object without [DataCache]
	public Address ShippingAddress { get; set; }
	public Address BillingAddress { get; set; }

	// Collection
	public List<TrackingEvent> Events { get; set; } = new();
	public string[] Notifications { get; set; } = Array.Empty<string>();

	// Dictionary
	public Dictionary<string, string> CustomFields { get; set; } = new();
}

// Another nested entity WITH [DataCache]
[DataCache]
public partial class Payment {
	[DataCacheKey] public string PaymentId { get; set; }

	public string Method { get; set; }
	public decimal Amount { get; set; }
	public DateTime ProcessedAt { get; set; }
	public PaymentStatus Status { get; set; }

	// Nested object
	public PaymentDetails Details { get; set; }

	// Dictionary with concrete type (not object - that would cause CACHE005 error)
	public Dictionary<string, string> Metadata { get; set; } = new();
}

// Nested objects WITHOUT [DataCache] - will be shallow copied
public class Customer {
	public int CustomerId { get; set; }
	public string Name { get; set; }
	public string Email { get; set; }
	public CustomerTier Tier { get; set; }

	// Nested object
	public Address PrimaryAddress { get; set; }

	// Collections
	public List<string> PhoneNumbers { get; set; } = new();
	public Dictionary<string, string> Preferences { get; set; } = new();
}

public class OrderLine {
	public int LineNumber { get; set; }
	public string ProductCode { get; set; }
	public string ProductName { get; set; }
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal LineTotal { get; set; }

	// Nested object
	public Product Product { get; set; }

	// Collections
	public List<string> AppliedDiscounts { get; set; } = new();
	public Dictionary<string, string> Attributes { get; set; } = new();
}

public class Product {
	public int ProductId { get; set; }
	public string Sku { get; set; }
	public string Name { get; set; }
	public decimal Price { get; set; }

	// Nested object (3rd level)
	public Category Category { get; set; }
	public Supplier Supplier { get; set; }

	// Collections
	public string[] Images { get; set; } = Array.Empty<string>();
	public Dictionary<string, string> Specifications { get; set; } = new();
}

public class Category {
	public int CategoryId { get; set; }
	public string Name { get; set; }
	public string Path { get; set; }

	// 4th level nesting
	public Category ParentCategory { get; set; }
}

public class Supplier {
	public int SupplierId { get; set; }
	public string Name { get; set; }
	public Address Address { get; set; }
}

public class Address {
	public string Street { get; set; }
	public string City { get; set; }
	public string State { get; set; }
	public string PostalCode { get; set; }
	public string Country { get; set; }
	public GeoLocation Location { get; set; }
}

public class GeoLocation {
	public double Latitude { get; set; }
	public double Longitude { get; set; }
}

public class TrackingEvent {
	public DateTime Timestamp { get; set; }
	public string Location { get; set; }
	public string Status { get; set; }
	public string Description { get; set; }
}

public class PaymentDetails {
	public string CardType { get; set; }
	public string Last4Digits { get; set; }
	public string AuthorizationCode { get; set; }
	public Dictionary<string, string> ProcessorResponse { get; set; } = new();
}

public enum OrderStatus {
	Pending,
	Processing,
	Shipped,
	Delivered,
	Cancelled
}

public enum ShippingStatus {
	Pending,
	InTransit,
	OutForDelivery,
	Delivered,
	Exception
}

public enum PaymentStatus {
	Pending,
	Authorized,
	Captured,
	Refunded,
	Failed
}

public enum CustomerTier {
	Bronze,
	Silver,
	Gold,
	Platinum
}

// Test model with custom cache class name using property syntax
[DataCache(CacheClassName = "CustomProductCache")]
[DataCacheTopic("products.custom")]
public partial class Product2 {
	[DataCacheKey] public string ProductId { get; set; }

	public string Name { get; set; }
	public decimal Price { get; set; }
}
namespace Prague.Generated.Tests.Clone;
using Prague.Generated.Tests.Models;
using NUnit.Framework;

/// <summary>
///   Comprehensive test suite for generated clone functionality
///   Tests deep cloning with multi-level nested objects, collections (arrays, lists), and dictionaries
/// </summary>
[TestFixture]
public class CacheCloneTests {
	private Order CreateComplexOrder() {
		return new Order {
			OrderId = "ORD-2025-001",
			OrderNumber = 1001,
			OrderDate = new DateTime(2025, 1, 15, 10, 30, 0),
			TotalAmount = 1250.99m,
			Status = OrderStatus.Processing,
			PreviousStatus = OrderStatus.Pending,
			Tags = new List<string> { "express", "gift", "international" },
			TrackingNumbers = new[] { "TRK001", "TRK002", "TRK003" },
			RelatedOrderIds = new HashSet<int> { 999, 998, 997 },
			Metadata = new Dictionary<string, string> {
				{ "source", "mobile_app" },
				{ "campaign", "summer_sale_2025" },
				{ "referrer", "google_ads" }
			},
			PaymentInstallments = new Dictionary<int, decimal> {
				{ 1, 416.66m },
				{ 2, 416.66m },
				{ 3, 417.67m }
			},
			Customer = new Customer {
				CustomerId = 5001,
				Name = "John Doe",
				Email = "john.doe@example.com",
				Tier = CustomerTier.Gold,
				PhoneNumbers = new List<string> { "+1-555-0100", "+1-555-0101" },
				Preferences = new Dictionary<string, string> {
					{ "newsletter", "true" },
					{ "sms_notifications", "false" }
				},
				PrimaryAddress = new Address {
					Street = "123 Main Street",
					City = "New York",
					State = "NY",
					PostalCode = "10001",
					Country = "USA",
					Location = new GeoLocation {
						Latitude = 40.7128,
						Longitude = -74.0060
					}
				}
			},
			Lines = new List<OrderLine> {
				new() {
					LineNumber = 1,
					ProductCode = "LAPTOP-001",
					ProductName = "Gaming Laptop Pro",
					Quantity = 1,
					UnitPrice = 1299.99m,
					LineTotal = 1299.99m,
					AppliedDiscounts = new List<string> { "SUMMER20", "LOYALTY10" },
					Attributes = new Dictionary<string, string> {
						{ "color", "black" },
						{ "warranty", "3years" }
					},
					Product = new Product {
						ProductId = 1001,
						Sku = "LAP-GM-001",
						Name = "Gaming Laptop Pro",
						Price = 1499.99m,
						Images = new[] { "img1.jpg", "img2.jpg", "img3.jpg" },
						Specifications = new Dictionary<string, string> {
							{ "cpu", "Intel i9" },
							{ "ram", "32GB" },
							{ "storage", "1TB SSD" }
						},
						Category = new Category {
							CategoryId = 10,
							Name = "Gaming Laptops",
							Path = "/Electronics/Computers/Laptops/Gaming",
							ParentCategory = new Category {
								CategoryId = 5,
								Name = "Laptops",
								Path = "/Electronics/Computers/Laptops"
							}
						},
						Supplier = new Supplier {
							SupplierId = 100,
							Name = "TechSupply Inc.",
							Address = new Address {
								Street = "456 Industrial Blvd",
								City = "San Jose",
								State = "CA",
								PostalCode = "95110",
								Country = "USA",
								Location = new GeoLocation {
									Latitude = 37.3382,
									Longitude = -121.8863
								}
							}
						}
					}
				},
				new() {
					LineNumber = 2,
					ProductCode = "MOUSE-001",
					ProductName = "Wireless Gaming Mouse",
					Quantity = 2,
					UnitPrice = 79.99m,
					LineTotal = 159.98m,
					AppliedDiscounts = new List<string> { "BUNDLE15" },
					Attributes = new Dictionary<string, string> {
						{ "color", "red" },
						{ "dpi", "16000" }
					},
					Product = new Product {
						ProductId = 2001,
						Sku = "MSE-WL-001",
						Name = "Wireless Gaming Mouse",
						Price = 89.99m,
						Images = new[] { "mouse1.jpg", "mouse2.jpg" },
						Specifications = new Dictionary<string, string> {
							{ "dpi", "16000" },
							{ "buttons", "8" },
							{ "battery", "70 hours" }
						},
						Category = new Category {
							CategoryId = 20,
							Name = "Gaming Mice",
							Path = "/Electronics/Peripherals/Mice/Gaming"
						},
						Supplier = new Supplier {
							SupplierId = 101,
							Name = "PeripheralPro LLC",
							Address = new Address {
								Street = "789 Tech Park",
								City = "Austin",
								State = "TX",
								PostalCode = "78701",
								Country = "USA",
								Location = new GeoLocation {
									Latitude = 30.2672,
									Longitude = -97.7431
								}
							}
						}
					}
				}
			},
			ExpressLines = new[] {
				new OrderLine {
					LineNumber = 3,
					ProductCode = "CABLE-001",
					ProductName = "USB-C Cable",
					Quantity = 3,
					UnitPrice = 19.99m,
					LineTotal = 59.97m,
					AppliedDiscounts = new List<string>(),
					Attributes = new Dictionary<string, string> {
						{ "length", "2m" },
						{ "type", "USB-C to USB-C" }
					},
					Product = new Product {
						ProductId = 3001,
						Sku = "CBL-UC-001",
						Name = "USB-C Cable 2m",
						Price = 19.99m,
						Images = new[] { "cable1.jpg" },
						Specifications = new Dictionary<string, string> {
							{ "length", "2m" },
							{ "speed", "USB 3.2" }
						},
						Category = new Category {
							CategoryId = 30,
							Name = "Cables",
							Path = "/Electronics/Accessories/Cables"
						},
						Supplier = new Supplier {
							SupplierId = 100,
							Name = "TechSupply Inc.",
							Address = new Address {
								Street = "456 Industrial Blvd",
								City = "San Jose",
								State = "CA",
								PostalCode = "95110",
								Country = "USA"
							}
						}
					}
				}
			},
			ShippingInfo = new ShippingInfo {
				TrackingId = "TRK-2025-001",
				Carrier = "FastShip Express",
				EstimatedDelivery = new DateTime(2025, 1, 20, 17, 0, 0),
				Status = ShippingStatus.InTransit,
				Notifications = new[] { "sms", "email", "push" },
				CustomFields = new Dictionary<string, string> {
					{ "signature_required", "true" },
					{ "insurance", "1500" }
				},
				ShippingAddress = new Address {
					Street = "123 Main Street",
					City = "New York",
					State = "NY",
					PostalCode = "10001",
					Country = "USA",
					Location = new GeoLocation {
						Latitude = 40.7128,
						Longitude = -74.0060
					}
				},
				BillingAddress = new Address {
					Street = "123 Main Street",
					City = "New York",
					State = "NY",
					PostalCode = "10001",
					Country = "USA",
					Location = new GeoLocation {
						Latitude = 40.7128,
						Longitude = -74.0060
					}
				},
				Events = new List<TrackingEvent> {
					new() {
						Timestamp = new DateTime(2025, 1, 15, 14, 0, 0),
						Location = "San Jose, CA",
						Status = "picked_up",
						Description = "Package picked up from warehouse"
					},
					new() {
						Timestamp = new DateTime(2025, 1, 16, 8, 30, 0),
						Location = "Las Vegas, NV",
						Status = "in_transit",
						Description = "Package in transit"
					},
					new() {
						Timestamp = new DateTime(2025, 1, 17, 12, 15, 0),
						Location = "Denver, CO",
						Status = "in_transit",
						Description = "Package in transit - sorting facility"
					}
				}
			},
			Payments = new List<Payment> {
				new() {
					PaymentId = "PAY-001",
					Method = "credit_card",
					Amount = 1250.99m,
					ProcessedAt = new DateTime(2025, 1, 15, 10, 35, 0),
					Status = PaymentStatus.Captured,
					Details = new PaymentDetails {
						CardType = "Visa",
						Last4Digits = "4242",
						AuthorizationCode = "AUTH123456",
						ProcessorResponse = new Dictionary<string, string> {
							{ "response_code", "00" },
							{ "avs_result", "Y" },
							{ "cvv_result", "M" }
						}
					},
					Metadata = new Dictionary<string, string> {
						{ "processor", "Stripe" },
						{ "fee", "36.27" },
						{ "net", "1214.72" }
					}
				}
			}
		};
	}

	[Test]
	public void Clone_ComplexOrder_CreatesNewInstance() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.Null);
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.OrderId, Is.EqualTo(original.OrderId));
		Assert.That(cloned.OrderNumber, Is.EqualTo(original.OrderNumber));
		Assert.That(cloned.TotalAmount, Is.EqualTo(original.TotalAmount));
	}

	[Test]
	public void Clone_PrimitiveProperties_AreCopiedCorrectly() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.OrderId, Is.EqualTo("ORD-2025-001"));
		Assert.That(cloned.OrderNumber, Is.EqualTo(1001));
		Assert.That(cloned.OrderDate, Is.EqualTo(new DateTime(2025, 1, 15, 10, 30, 0)));
		Assert.That(cloned.TotalAmount, Is.EqualTo(1250.99m));
		Assert.That(cloned.Status, Is.EqualTo(OrderStatus.Processing));
		Assert.That(cloned.PreviousStatus, Is.EqualTo(OrderStatus.Pending));
	}

	[Test]
	public void Clone_ListOfStrings_CreatesNewList() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.Tags, Is.Not.Null);
		Assert.That(cloned.Tags, Is.Not.SameAs(original.Tags));
		Assert.That(cloned.Tags.Count, Is.EqualTo(3));
		Assert.That(cloned.Tags, Is.EqualTo(original.Tags));
	}

	[Test]
	public void Clone_ModifyClonedList_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.Tags.Add("priority");
		cloned.Tags[0] = "modified";

		// Assert
		Assert.That(original.Tags.Count, Is.EqualTo(3));
		Assert.That(original.Tags[0], Is.EqualTo("express"));
		Assert.That(original.Tags, Does.Not.Contain("priority"));
	}

	[Test]
	public void Clone_ListOfComplexObjects_CreatesNewList() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - List itself is cloned
		Assert.That(cloned.Lines, Is.Not.SameAs(original.Lines));
		Assert.That(cloned.Lines.Count, Is.EqualTo(2));

		// Objects inside list are shared references (OrderLine doesn't have [DataCache])
		Assert.That(cloned.Lines[0], Is.Not.SameAs(original.Lines[0]));
		Assert.That(cloned.Lines[1], Is.Not.SameAs(original.Lines[1]));
	}

	[Test]
	public void Clone_ListOfEntitiesWithDataCache_CreatesNewListWithDeepClonedEntities() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - List is cloned
		Assert.That(cloned.Payments, Is.Not.SameAs(original.Payments));
		Assert.That(cloned.Payments.Count, Is.EqualTo(1));

		// Payment entities inside should be deep cloned (Payment has [DataCache])
		Assert.That(cloned.Payments[0], Is.Not.SameAs(original.Payments[0]));
		Assert.That(cloned.Payments[0].PaymentId, Is.EqualTo(original.Payments[0].PaymentId));
		Assert.That(cloned.Payments[0].Amount, Is.EqualTo(original.Payments[0].Amount));
	}

	[Test]
	public void Clone_ModifyListStructure_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.Lines.RemoveAt(0);
		cloned.Tags.Clear();

		// Assert
		Assert.That(original.Lines.Count, Is.EqualTo(2));
		Assert.That(original.Tags.Count, Is.EqualTo(3));
		Assert.That(cloned.Lines.Count, Is.EqualTo(1));
		Assert.That(cloned.Tags.Count, Is.EqualTo(0));
	}

	[Test]
	public void Clone_ArrayOfStrings_CreatesNewArray() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.TrackingNumbers, Is.Not.Null);
		Assert.That(cloned.TrackingNumbers, Is.Not.SameAs(original.TrackingNumbers));
		Assert.That(cloned.TrackingNumbers.Length, Is.EqualTo(3));
		Assert.That(cloned.TrackingNumbers, Is.EqualTo(original.TrackingNumbers));
	}

	[Test]
	public void Clone_ModifyClonedArray_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.TrackingNumbers[0] = "MODIFIED";

		// Assert
		Assert.That(original.TrackingNumbers[0], Is.EqualTo("TRK001"));
		Assert.That(cloned.TrackingNumbers[0], Is.EqualTo("MODIFIED"));
	}

	[Test]
	public void Clone_ArrayOfComplexObjects_CreatesNewArray() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.ExpressLines, Is.Not.SameAs(original.ExpressLines));
		Assert.That(cloned.ExpressLines.Length, Is.EqualTo(1));

		// Objects inside array are shared references (OrderLine doesn't have [DataCache])
		Assert.That(cloned.ExpressLines[0], Is.Not.SameAs(original.ExpressLines[0]));
	}

	[Test]
	public void Clone_NestedArrayInShippingInfo_CreatesNewArray() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.ShippingInfo.Notifications, Is.Not.SameAs(original.ShippingInfo.Notifications));
		Assert.That(cloned.ShippingInfo.Notifications.Length, Is.EqualTo(3));
	}

	[Test]
	public void Clone_HashSet_CreatesNewHashSet() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.RelatedOrderIds, Is.Not.Null);
		Assert.That(cloned.RelatedOrderIds, Is.Not.SameAs(original.RelatedOrderIds));
		Assert.That(cloned.RelatedOrderIds.Count, Is.EqualTo(3));
		Assert.That(cloned.RelatedOrderIds, Is.EquivalentTo(original.RelatedOrderIds));
	}

	[Test]
	public void Clone_ModifyClonedHashSet_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.RelatedOrderIds.Add(996);
		cloned.RelatedOrderIds.Remove(999);

		// Assert
		Assert.That(original.RelatedOrderIds.Count, Is.EqualTo(3));
		Assert.That(original.RelatedOrderIds, Does.Contain(999));
		Assert.That(original.RelatedOrderIds, Does.Not.Contain(996));
	}

	[Test]
	public void Clone_DictionaryStringString_CreatesNewDictionary() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.Metadata, Is.Not.Null);
		Assert.That(cloned.Metadata, Is.Not.SameAs(original.Metadata));
		Assert.That(cloned.Metadata.Count, Is.EqualTo(3));
		Assert.That(cloned.Metadata["source"], Is.EqualTo("mobile_app"));
	}

	[Test]
	public void Clone_DictionaryIntDecimal_CreatesNewDictionary() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.PaymentInstallments, Is.Not.SameAs(original.PaymentInstallments));
		Assert.That(cloned.PaymentInstallments.Count, Is.EqualTo(3));
		Assert.That(cloned.PaymentInstallments[1], Is.EqualTo(416.66m));
	}

	[Test]
	public void Clone_ModifyClonedDictionary_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.Metadata["new_key"] = "new_value";
		cloned.Metadata["source"] = "modified";

		// Assert
		Assert.That(original.Metadata.Count, Is.EqualTo(3));
		Assert.That(original.Metadata["source"], Is.EqualTo("mobile_app"));
		Assert.That(original.Metadata.ContainsKey("new_key"), Is.False);
	}

	[Test]
	public void Clone_NestedObjectWithDataCache_IsDeepCloned() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - ShippingInfo has [DataCache], so it should be deep cloned
		Assert.That(cloned.ShippingInfo, Is.Not.Null);
		Assert.That(cloned.ShippingInfo, Is.Not.SameAs(original.ShippingInfo));
		Assert.That(cloned.ShippingInfo.TrackingId, Is.EqualTo(original.ShippingInfo.TrackingId));
		Assert.That(cloned.ShippingInfo.Carrier, Is.EqualTo(original.ShippingInfo.Carrier));
	}

	[Test]
	public void Clone_ModifyNestedDataCacheObject_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.ShippingInfo.Carrier = "Modified Carrier";
		cloned.ShippingInfo.Status = ShippingStatus.Delivered;

		// Assert
		Assert.That(original.ShippingInfo.Carrier, Is.EqualTo("FastShip Express"));
		Assert.That(original.ShippingInfo.Status, Is.EqualTo(ShippingStatus.InTransit));
	}

	[Test]
	public void Clone_NestedObjectCollections_AreCloned() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.ShippingInfo.Events, Is.Not.SameAs(original.ShippingInfo.Events));
		Assert.That(cloned.ShippingInfo.Events.Count, Is.EqualTo(3));
		Assert.That(cloned.ShippingInfo.CustomFields, Is.Not.SameAs(original.ShippingInfo.CustomFields));
	}

	[Test]
	public void Clone_NestedObjectWithoutDataCache_IsShallowCopied() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - Customer doesn't have [DataCache], so it's a shared reference
		Assert.That(cloned.Customer, Is.Not.Null);
		Assert.That(cloned.Customer, Is.Not.SameAs(original.Customer));
	}

	[Test]
	public void Clone_ModifyShallowCopiedNestedObject_AffectsBoth() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.Customer.Name = "Modified Name";
		cloned.Customer.Email = "modified@example.com";

		// Assert - Both share the same Customer reference
		Assert.That(original.Customer.Name, Is.Not.EqualTo("Modified Name"));
		Assert.That(original.Customer.Email, Is.Not.EqualTo("modified@example.com"));
	}

	[Test]
	public void Clone_ThreeLevelNesting_ShallowCopyBehavior() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - OrderLine -> Product -> Category (3 levels, all without [DataCache])
		// Since Lines list is cloned but OrderLine objects are shared:
		var originalProduct = original.Lines[0].Product;
		var clonedProduct = cloned.Lines[0].Product;

		Assert.That(clonedProduct, Is.Not.SameAs(originalProduct));
		Assert.That(clonedProduct.Category, Is.Not.SameAs(originalProduct.Category));
	}

	[Test]
	public void Clone_FourLevelNesting_ShallowCopyBehavior() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - OrderLine -> Product -> Category -> ParentCategory (4 levels)
		var originalParentCategory = original.Lines[0].Product.Category.ParentCategory;
		var clonedParentCategory = cloned.Lines[0].Product.Category.ParentCategory;

		Assert.That(clonedParentCategory, Is.Not.SameAs(originalParentCategory));
		Assert.That(clonedParentCategory.Name, Is.EqualTo("Laptops"));
	}

	[Test]
	public void Clone_NestedAddresses_InDifferentPaths() {
		// Arrange
		var original = CreateComplexOrder();

		// Act
		var cloned = original.Clone();

		// Assert - Customer.PrimaryAddress is shared (Customer is shared)
		Assert.That(cloned.Customer.PrimaryAddress, Is.Not.SameAs(original.Customer.PrimaryAddress));

		// ShippingInfo.ShippingAddress is shared (nested in ShippingInfo which is cloned, but Address is not)
		Assert.That(cloned.ShippingInfo.ShippingAddress, Is.Not.SameAs(original.ShippingInfo.ShippingAddress));

		// Product.Supplier.Address is shared
		Assert.That(cloned.Lines[0].Product.Supplier.Address, Is.Not.SameAs(original.Lines[0].Product.Supplier.Address));
	}

	[Test]
	public void Clone_ModifyPrimitives_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.OrderNumber = 9999;
		cloned.TotalAmount = 5000m;
		cloned.Status = OrderStatus.Cancelled;

		// Assert
		Assert.That(original.OrderNumber, Is.EqualTo(1001));
		Assert.That(original.TotalAmount, Is.EqualTo(1250.99m));
		Assert.That(original.Status, Is.EqualTo(OrderStatus.Processing));
	}

	[Test]
	public void Clone_ClearCollections_DoesNotAffectOriginal() {
		// Arrange
		var original = CreateComplexOrder();
		var cloned = original.Clone();

		// Act
		cloned.Tags.Clear();
		cloned.Lines.Clear();
		cloned.Metadata.Clear();

		// Assert
		Assert.That(original.Tags.Count, Is.EqualTo(3));
		Assert.That(original.Lines.Count, Is.EqualTo(2));
		Assert.That(original.Metadata.Count, Is.EqualTo(3));
	}

	[Test]
	public void Clone_Performance_CloneThousandComplexOrders() {
		// Arrange
		var orders = Enumerable.Range(0, 1000)
			.Select(i => new Order {
				OrderId = $"ORD-{i}",
				OrderNumber = i,
				OrderDate = DateTime.Now,
				TotalAmount = i * 100m,
				Status = OrderStatus.Processing,
				Tags = new List<string> { $"tag{i}" },
				Metadata = new Dictionary<string, string> { { "key", $"value{i}" } },
				ShippingInfo = new ShippingInfo {
					TrackingId = $"TRK-{i}",
					Carrier = "FastShip",
					Status = ShippingStatus.Pending
				}
			})
			.ToList();

		// Act
		var startTime = DateTime.UtcNow;
		var cloned = orders.Select(o => o.Clone()).ToList();
		var duration = DateTime.UtcNow - startTime;

		// Assert
		Assert.That(cloned.Count, Is.EqualTo(1000));
		Console.WriteLine($"Cloning 1000 complex orders took {duration.TotalMilliseconds}ms");
		Assert.That(duration.TotalSeconds, Is.LessThan(2.0));

		// Verify independence
		orders[0].OrderNumber = 9999;
		Assert.That(cloned[0].OrderNumber, Is.EqualTo(0));
	}

	[Test]
	public void Clone_DirectHelperCall_Performance() {
		// Arrange
		var original = CreateComplexOrder();
		var iterations = 10000;

		// Act
		var startTime = DateTime.UtcNow;
		for (var i = 0; i < iterations; i++) {
			var cloned = original.Clone();
		}

		var duration = DateTime.UtcNow - startTime;

		// Assert
		var avgMicroseconds = duration.TotalMilliseconds * 1000 / iterations;
		Console.WriteLine($"Average clone time: {avgMicroseconds:F2} microseconds");
		Assert.That(duration.TotalSeconds, Is.LessThan(2.0));
	}

	[Test]
	public void Clone_EmptyCollections_WorksCorrectly() {
		// Arrange
		var original = new Order {
			OrderId = "TEST",
			OrderNumber = 1,
			OrderDate = DateTime.Now,
			TotalAmount = 0,
			Tags = new List<string>(),
			TrackingNumbers = Array.Empty<string>(),
			RelatedOrderIds = new HashSet<int>(),
			Metadata = new Dictionary<string, string>(),
			Lines = new List<OrderLine>(),
			Payments = new List<Payment>()
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.Tags, Is.Not.SameAs(original.Tags));
		Assert.That(cloned.Tags.Count, Is.EqualTo(0));
		Assert.That(cloned.Metadata, Is.Not.SameAs(original.Metadata));
		Assert.That(cloned.Metadata.Count, Is.EqualTo(0));
	}

	[Test]
	public void Clone_NullableProperties_AreHandled() {
		// Arrange
		var original = new Order {
			OrderId = "TEST",
			OrderNumber = 1,
			OrderDate = DateTime.Now,
			TotalAmount = 100,
			PreviousStatus = null,
			ShippingInfo = new ShippingInfo {
				TrackingId = "TRK",
				Carrier = "Test",
				EstimatedDelivery = null,
				Status = ShippingStatus.Pending
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.PreviousStatus, Is.Null);
		Assert.That(cloned.ShippingInfo.EstimatedDelivery, Is.Null);
	}
}

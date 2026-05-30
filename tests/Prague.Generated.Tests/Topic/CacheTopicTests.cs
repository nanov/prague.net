namespace Prague.Generated.Tests.Topic;
using Prague.Generated.Tests.Models;

using NUnit.Framework;

[TestFixture]
public class CacheTopicTests {
	// [Test]
	// public void Topic_DefaultName_GeneratesCorrectValue() {
	// 	// Arrange & Act
	// 	var topic = OrderCache.Topic;
	//
	// 	// Assert
	// 	Assert.That(topic, Is.EqualTo("Cache.Order"));
	// }

	[Test]
	public void Topic_CustomName_GeneratesCorrectValue() {
		// Arrange & Act
		var topic = ShippingInfoCache.TopicNameTemplate;

		// Assert
		Assert.That(topic, Is.EqualTo("CustomShipping"));
	}

	[Test]
	public void Topic_IsConst_CanBeUsedAtCompileTime() {
		// Arrange & Act - This will only compile if TopicNameTemplate is a const
		const string orderTopic = OrderCache.TopicNameTemplate;
		const string shippingTopic = ShippingInfoCache.TopicNameTemplate;

		// Assert
		Assert.That(orderTopic, Is.EqualTo("Cache.Order"));
		Assert.That(shippingTopic, Is.EqualTo("CustomShipping"));
	}

	[Test]
	public void Topic_CanBeUsedInSwitchStatement() {
		// Arrange
		var topic = "Cache.Order";

		// Act & Assert - This will only compile if TopicNameTemplate is a const
		var result = topic switch {
			OrderCache.TopicNameTemplate => "Order cache",
			ShippingInfoCache.TopicNameTemplate => "Shipping cache",
			_ => "Unknown"
		};

		Assert.That(result, Is.EqualTo("Order cache"));
	}

	[Test]
	public void Topic_ValidCharacters_GeneratesCorrectly() {
		// Arrange & Act
		var topic = ValidTopic1Cache.TopicNameTemplate;
		var s = new ValidTopic1Cache();

		// Assert
		Assert.That(topic, Is.EqualTo("valid.topic-name_123"));
	}

	[Test]
	public void Topic_WithVarPlaceholder_GeneratesCorrectly() {
		// Arrange & Act
		var topic = ValidTopicWithVarPlaceholderCache.TopicNameTemplate;

		// Assert
		Assert.That(topic, Is.EqualTo("Cache.[v:tenant].Orders"));
	}

	[Test]
	public void Topic_WithEnvPlaceholder_GeneratesCorrectly() {
		// Arrange & Act
		var topic = ValidTopicWithEnvPlaceholderCache.TopicNameTemplate;

		// Assert
		Assert.That(topic, Is.EqualTo("Cache.[e:environment].Orders"));
	}

	[Test]
	public void Topic_WithMultiplePlaceholders_GeneratesCorrectly() {
		// Arrange & Act
		var topic = ValidTopicWithMultiplePlaceholdersCache.TopicNameTemplate;

		// Assert
		Assert.That(topic, Is.EqualTo("[v:tenant].[e:env].Orders"));
	}

	[Test]
	public void CustomCacheClassName_UsingConstructor_GeneratesCorrectClassName() {
		// Arrange & Act
		var cache = new CustomProductCache();

		// Assert - verify the cache class name is CustomProductCache, not Product2Cache
		Assert.That(cache, Is.Not.Null);
		Assert.That(CustomProductCache.TopicNameTemplate, Is.EqualTo("products.custom"));
	}

	[Test]
	public void CustomCacheClassName_CanAddAndRetrieveItems() {
		// Arrange
		var cache = new CustomProductCache();
		var product = new Product2 {
			ProductId = "PROD-001",
			Name = "Test Product",
			Price = 99.99m
		};

		// Act
		cache.AddOrUpdate(product);
		var result = cache.TryGet("PROD-001", out var retrieved);

		// Assert
		Assert.That(result, Is.True);
		Assert.That(retrieved, Is.Not.Null);
		Assert.That(retrieved.Name, Is.EqualTo("Test Product"));
		Assert.That(retrieved.Price, Is.EqualTo(99.99m));
	}
}
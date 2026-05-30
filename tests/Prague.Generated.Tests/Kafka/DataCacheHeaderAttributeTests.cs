namespace Prague.Generated.Tests.Kafka;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class DataCacheHeaderAttributeTests {
	private class TestEntity {
		[DataCacheHeader] public int TenantId { get; set; }

		[DataCacheHeader] public string EventType { get; set; } = string.Empty;

		[DataCacheHeader("custom-header")] public int CustomProperty { get; set; }

		[DataCacheHeader("x-status")] public string Status { get; set; } = string.Empty;
	}

	[Test]
	public void Attribute_WithoutParameter_HasNullHeaderName() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.TenantId))!;
		var attribute = (DataCacheHeaderAttribute)property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false)[0];

		// Assert
		Assert.That(attribute.HeaderName, Is.Null);
	}

	[Test]
	public void Attribute_WithoutParameter_CodeGeneratorCanGenerateHeaderName() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.EventType))!;
		var attribute = (DataCacheHeaderAttribute)property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false)[0];

		// Simulate what code generator would do: if HeaderName is null, use X-{PropertyName}
		var generatedHeaderName = attribute.HeaderName ?? $"X-{property.Name}";

		// Assert
		Assert.That(generatedHeaderName, Is.EqualTo("X-EventType"));
	}

	[Test]
	public void Attribute_WithCustomHeaderName_UsesProvidedName() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.CustomProperty))!;
		var attribute = (DataCacheHeaderAttribute)property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false)[0];

		// Assert
		Assert.That(attribute.HeaderName, Is.EqualTo("custom-header"));
	}

	[Test]
	public void Attribute_WithCustomHeaderName_DoesNotAddXPrefix() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.Status))!;
		var attribute = (DataCacheHeaderAttribute)property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false)[0];

		// Assert
		Assert.That(attribute.HeaderName, Is.EqualTo("x-status"));
	}

	[Test]
	public void Attribute_CanBeAppliedToIntProperty() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.TenantId))!;
		var hasAttribute = property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false).Length > 0;

		// Assert
		Assert.That(hasAttribute, Is.True);
	}

	[Test]
	public void Attribute_CanBeAppliedToStringProperty() {
		// Arrange
		var property = typeof(TestEntity).GetProperty(nameof(TestEntity.EventType))!;
		var hasAttribute = property.GetCustomAttributes(typeof(DataCacheHeaderAttribute), false).Length > 0;

		// Assert
		Assert.That(hasAttribute, Is.True);
	}
}
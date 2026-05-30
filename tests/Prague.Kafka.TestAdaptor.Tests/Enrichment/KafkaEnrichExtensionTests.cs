namespace Prague.Kafka.TestAdaptor.Tests.Enrichment;

using System.Text;
using Prague.Kafka.TestAdaptor.Tests.TestEntities;
using Confluent.Kafka;
using NUnit.Framework;

[TestFixture]
public class KafkaEnrichExtensionTests {
	[Test]
	public void Enrich_WithLongTimestamp_ShouldSetUnixTimestampMs() {
		// Arrange
		var entity = new KafkaEntityWithLongTimestamp { Id = 1, Name = "Test" };
		var enricher = KafkaEntityWithLongTimestamp.GetEnricher();
		var headers = new Headers();
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890123L));
		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.Name, Is.EqualTo("Test"));
	}

	[Test]
	public void _WithNullableLongTimestamp_ShouldSetUnixTimestampMs() {
		// Arrange
		var entity = new KafkaEntityWithNullableLongTimestamp { Id = 1, Name = "Test" };
		var er = KafkaEntityWithNullableLongTimestamp.GetEnricher();
		var headers = new Headers();
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		er.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890123L));
	}

	[Test]
	public void _WithDateTime_ShouldSetUtcDateTime() {
		// Arrange
		var entity = new KafkaEntityWithDateTime { Id = 1, Name = "Test" };
		var er = KafkaEntityWithDateTime.GetEnricher();
		var headers = new Headers();
		var expectedDateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		var unixTimestamp = new DateTimeOffset(expectedDateTime).ToUnixTimeMilliseconds();
		var kafkaTimestamp = new Timestamp(unixTimestamp, TimestampType.CreateTime);

		// Act
		er.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.CreatedAt, Is.EqualTo(expectedDateTime));
		Assert.That(entity.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
	}

	[Test]
	public void _WithNullableDateTime_ShouldSetUtcDateTime() {
		// Arrange
		var entity = new KafkaEntityWithNullableDateTime { Id = 1, Name = "Test" };
		var er = KafkaEntityWithNullableDateTime.GetEnricher();
		var headers = new Headers();
		var expectedDateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		var unixTimestamp = new DateTimeOffset(expectedDateTime).ToUnixTimeMilliseconds();
		var kafkaTimestamp = new Timestamp(unixTimestamp, TimestampType.CreateTime);

		// Act
		er.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.CreatedAt, Is.EqualTo(expectedDateTime));
	}

	[Test]
	public void Enrich_WithDateTimeOffset_ShouldSetDateTimeOffsetInUtc() {
		// Arrange
		var entity = new KafkaEntityWithDateTimeOffset { Id = 1, Name = "Test" };
		var enricher = KafkaEntityWithDateTimeOffset.GetEnricher();
		var headers = new Headers();
		var expectedDateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		var expectedOffset = new DateTimeOffset(expectedDateTime, TimeSpan.Zero);
		var unixTimestamp = expectedOffset.ToUnixTimeMilliseconds();
		var kafkaTimestamp = new Timestamp(unixTimestamp, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.CreatedAt, Is.EqualTo(expectedOffset));
		Assert.That(entity.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
	}

	[Test]
	public void Enrich_WithNullableDateTimeOffset_ShouldSetDateTimeOffsetInUtc() {
		// Arrange
		var entity = new KafkaEntityWithNullableDateTimeOffset { Id = 1, Name = "Test" };
		var enricher = KafkaEntityWithNullableDateTimeOffset.GetEnricher();
		var headers = new Headers();
		var expectedDateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		var expectedOffset = new DateTimeOffset(expectedDateTime, TimeSpan.Zero);
		var unixTimestamp = expectedOffset.ToUnixTimeMilliseconds();
		var kafkaTimestamp = new Timestamp(unixTimestamp, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.CreatedAt, Is.EqualTo(expectedOffset));
	}

	[Test]
	public void Enrich_WithMultipleTimestamps_ShouldSetAllTimestampProperties() {
		// Arrange
		var entity = new KafkaEntityWithMultipleTimestamps { Id = 1, Name = "Test" };
		var enricher = KafkaEntityWithMultipleTimestamps.GetEnricher();
		var headers = new Headers();
		var expectedDateTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
		var unixTimestamp = new DateTimeOffset(expectedDateTime).ToUnixTimeMilliseconds();
		var kafkaTimestamp = new Timestamp(unixTimestamp, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Timestamp, Is.EqualTo(unixTimestamp));
		Assert.That(entity.CreatedAt, Is.EqualTo(expectedDateTime));
		Assert.That(entity.UpdatedAt, Is.EqualTo(new DateTimeOffset(expectedDateTime, TimeSpan.Zero)));
	}

	[Test]
	public void Enrich_PreservesOtherProperties() {
		// Arrange
		var entity = new KafkaEntityWithLongTimestamp {
			Id = 42,
			Name = "OriginalName",
			Timestamp = 999L
		};
		var enricher = KafkaEntityWithLongTimestamp.GetEnricher();
		var headers = new Headers();
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Id, Is.EqualTo(42), "Id should be preserved");
		Assert.That(entity.Name, Is.EqualTo("OriginalName"), "Name should be preserved");
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890123L), "Timestamp should be updated");
	}

	[Test]
	public void Enrich_WithLogProducerTimestamp_ShouldWork() {
		// Arrange
		var entity = new KafkaEntityWithLongTimestamp { Id = 1, Name = "Test" };
		var enricher = KafkaEntityWithLongTimestamp.GetEnricher();
		var headers = new Headers();
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.LogAppendTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890123L));
	}

	[Test]
	public void Enrich_WithHeaders_IntProperty_ShouldParseCorrectly() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "TenantId", BitConverter.GetBytes(12345) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.TenantId, Is.EqualTo(12345));
		Assert.That(entity.CreatedAt, Is.EqualTo(1234567890123L)); // Timestamp should also be set
	}

	[Test]
	public void Enrich_WithHeaders_LongProperty_ShouldParseCorrectly() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "Timestamp", BitConverter.GetBytes(9876543210L) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Timestamp, Is.EqualTo(9876543210L));
	}

	[Test]
	public void Enrich_WithHeaders_StringProperty_ShouldSetCorrectly() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "EventType", Encoding.UTF8.GetBytes("UserCreated") }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.EventType, Is.EqualTo("UserCreated"));
	}

	[Test]
	public void Enrich_WithHeaders_CustomHeaderName_ShouldSetCorrectly() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "custom-header", Encoding.UTF8.GetBytes("CustomValue123") }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.CustomValue, Is.EqualTo("CustomValue123"));
	}

	[Test]
	public void Enrich_WithMultipleHeaders_ShouldSetAllProperties() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "TenantId", BitConverter.GetBytes(999) },
			{ "EventType", Encoding.UTF8.GetBytes("OrderPlaced") },
			{ "custom-header", Encoding.UTF8.GetBytes("ABC") },
			{ "Timestamp", BitConverter.GetBytes(5555555555L) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.TenantId, Is.EqualTo(999));
		Assert.That(entity.EventType, Is.EqualTo("OrderPlaced"));
		Assert.That(entity.CustomValue, Is.EqualTo("ABC"));
		Assert.That(entity.Timestamp, Is.EqualTo(5555555555L));
		Assert.That(entity.CreatedAt, Is.EqualTo(1234567890123L)); // From Kafka timestamp
	}

	[Test]
	public void Enrich_WithInvalidIntHeader_ShouldNotSetProperty() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1, TenantId = 42 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "TenantId", Encoding.UTF8.GetBytes("not-a-number") }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.TenantId, Is.EqualTo(42), "Should preserve original value when parsing fails");
	}

	[Test]
	public void Enrich_WithUnknownHeader_ShouldIgnore() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "UnknownHeader", Encoding.UTF8.GetBytes("SomeValue") },
			{ "TenantId", BitConverter.GetBytes(123) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act & Assert - Should not throw
		Assert.DoesNotThrow(() => enricher.Enrich(entity, headers, kafkaTimestamp));
		Assert.That(entity.TenantId, Is.EqualTo(123));
	}

	[Test]
	public void Derich_IntProperty_ShouldWriteMessagePackFormat() {
		var entity = new EntityWithHeaders { Id = 1, TenantId = 12345 };
		var headers = new Headers();

		EntityWithHeaders.Derich(entity, headers);

		var tenantIdHeader = headers.FirstOrDefault(h => h.Key == "TenantId");
		Assert.That(tenantIdHeader, Is.Not.Null);

		// MessagePack int encoding for 12345 is NOT 4 raw little-endian bytes.
		var bytes = tenantIdHeader!.GetValueBytes();
		Assert.That(bytes, Is.Not.EqualTo(BitConverter.GetBytes(12345)),
			"Should not be raw BitConverter format (post-fix)");

		// And the bytes round-trip via MessagePack:
		var roundTripped = MessagePack.MessagePackSerializer.Deserialize<int>(bytes);
		Assert.That(roundTripped, Is.EqualTo(12345));
	}

	[Test]
	public void Enrich_WithEmptyHeaders_ShouldOnlySetTimestamp() {
		// Arrange
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers();
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.TenantId, Is.EqualTo(0));
		Assert.That(entity.EventType, Is.Null);
		Assert.That(entity.CustomValue, Is.Null);
		Assert.That(entity.Timestamp, Is.EqualTo(0));
		Assert.That(entity.CreatedAt, Is.EqualTo(1234567890123L));
	}

	[Test]
	public void Enrich_PreservesPropertiesNotInHeaders() {
		// Arrange
		var entity = new EntityWithHeaders {
			Id = 42,
			TenantId = 100,
			EventType = "OriginalType"
		};
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "Timestamp", BitConverter.GetBytes(9999L) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		// Act
		enricher.Enrich(entity, headers, kafkaTimestamp);

		// Assert
		Assert.That(entity.Id, Is.EqualTo(42));
		Assert.That(entity.TenantId, Is.EqualTo(100), "TenantId not in headers should be preserved");
		Assert.That(entity.EventType, Is.EqualTo("OriginalType"), "EventType not in headers should be preserved");
		Assert.That(entity.Timestamp, Is.EqualTo(9999L), "Timestamp from header should be set");
		Assert.That(entity.CreatedAt, Is.EqualTo(1234567890123L), "CreatedAt from Kafka timestamp should be set");
	}

	[Test]
	public void Enrich_WithHeaders_IntProperty_MessagePack_ShouldParseCorrectly() {
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "TenantId", MessagePack.MessagePackSerializer.Serialize(12345) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		enricher.Enrich(entity, headers, kafkaTimestamp);

		Assert.That(entity.TenantId, Is.EqualTo(12345));
	}

	[Test]
	public void Enrich_WithHeaders_LongProperty_MessagePack_ShouldParseCorrectly() {
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "Timestamp", MessagePack.MessagePackSerializer.Serialize(9876543210L) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		enricher.Enrich(entity, headers, kafkaTimestamp);

		Assert.That(entity.Timestamp, Is.EqualTo(9876543210L));
	}

	[Test]
	public void Enrich_RoundTrip_IntProperty_ShouldRoundtripViaDericher() {
		var sent = new EntityWithHeaders { Id = 1, TenantId = 42, EventType = "x", CustomValue = "y", Timestamp = 100L };
		var headers = new Headers();
		EntityWithHeaders.Derich(sent, headers);

		var received = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		enricher.Enrich(received, headers, new Timestamp(0, TimestampType.CreateTime));

		Assert.That(received.TenantId, Is.EqualTo(42));
		Assert.That(received.Timestamp, Is.EqualTo(100L));
		Assert.That(received.EventType, Is.EqualTo("x"));
		Assert.That(received.CustomValue, Is.EqualTo("y"));
	}

	[Test]
	public void Enrich_RoundTrip_LargeLong_ShouldRoundtripViaDericher() {
		// Verify MessagePack handles large longs correctly (was broken with native-endian raw bytes on big-endian hardware).
		var sent = new EntityWithHeaders { Id = 1, Timestamp = long.MaxValue };
		var headers = new Headers();
		EntityWithHeaders.Derich(sent, headers);

		var received = new EntityWithHeaders { Id = 1 };
		EntityWithHeaders.GetEnricher().Enrich(received, headers, new Timestamp(0, TimestampType.CreateTime));

		Assert.That(received.Timestamp, Is.EqualTo(long.MaxValue));
	}
}
namespace Prague.Kafka.TestAdaptor.Tests.HeaderFilters;

using System.Text;
using Prague.Kafka;
using Prague.Kafka.SerDe;
using Prague.Kafka.TestAdaptor;
using Prague.Kafka.TestAdaptor.Tests.TestEntities;
using Confluent.Kafka;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class KafkaHeaderFiltersIntegrationTests {
	private const string TestTopic = "test-topic";

	private static IServiceProvider CreateServiceProvider(Action<KafkaCacheHandlersBuilder> configure) {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { { "KafkaConfig:BootstrapServers", "localhost:9092" } })
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCacheTestCluster();
		services.AddKafkaCaches("KafkaConfig", configure);

		return services.BuildServiceProvider();
	}

	[Test]
	public void Deserializer_WithNoFilters_AcceptsAllMessages() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		var headers = new Headers { { "some-header", Encoding.UTF8.GetBytes("some-value") } };

		var context = new SerializationContext(MessageComponentType.Key, TestTopic, headers);

		// Act
		var result = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context);

		// Assert
		Assert.That(result.IsFiltered, Is.False);
		Assert.That(result.Handler, Is.Not.Null);
	}

	[Test]
	public void Deserializer_WithProducerInstanceIdMatch_FiltersMessage() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		var headers = new Headers {
			{ KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes } // Same instance ID
		};

		var context = new SerializationContext(MessageComponentType.Key, TestTopic, headers);

		// Act
		var result = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context);

		// Assert
		Assert.That(result.IsFiltered, Is.True);
	}

	[Test]
	public void Deserializer_WithHeaderExistsFilter_RequiresHeader() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderExistsFilter("required-header");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test without the required header
		var headersWithout = new Headers();
		var contextWithout = new SerializationContext(MessageComponentType.Key, TestTopic, headersWithout);
		var resultWithout = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextWithout);

		// Test with the required header
		var headersWith = new Headers { { "required-header", Encoding.UTF8.GetBytes("any-value") } };
		var contextWith = new SerializationContext(MessageComponentType.Key, TestTopic, headersWith);
		var resultWith = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextWith);

		// Assert
		Assert.That(resultWithout.IsFiltered, Is.True, "Message without required header should be filtered");
		Assert.That(resultWith.IsFiltered, Is.False, "Message with required header should not be filtered");
	}

	[Test]
	public void Deserializer_WithHeaderEqualsFilter_OnlyAcceptsMatchingValue() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("event-type", "user-created");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with matching value
		var headersMatch = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-created") } };
		var contextMatch = new SerializationContext(MessageComponentType.Key, TestTopic, headersMatch);
		var resultMatch = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextMatch);

		// Test with non-matching value
		var headersNoMatch = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-updated") } };
		var contextNoMatch = new SerializationContext(MessageComponentType.Key, TestTopic, headersNoMatch);
		var resultNoMatch = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextNoMatch);

		// Test without header (header value will be null, which equals anything per the filter logic)
		var headersWithout = new Headers();
		var contextWithout = new SerializationContext(MessageComponentType.Key, TestTopic, headersWithout);
		var resultWithout = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextWithout);

		// Assert
		Assert.That(resultMatch.IsFiltered, Is.False, "Message with matching header should not be filtered");
		Assert.That(resultNoMatch.IsFiltered, Is.True, "Message with non-matching header should be filtered");
		Assert.That(resultWithout.IsFiltered, Is.False,
			"Message without header should not be filtered (null equals anything)");
	}

	[Test]
	public void Deserializer_WithHeaderNotEqualsFilter_ExcludesSpecificValue() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderNotEqualsFilter("status", "deleted");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with excluded value
		var headersExcluded = new Headers { { "status", Encoding.UTF8.GetBytes("deleted") } };
		var contextExcluded = new SerializationContext(MessageComponentType.Key, TestTopic, headersExcluded);
		var resultExcluded = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextExcluded);

		// Test with allowed value
		var headersAllowed = new Headers { { "status", Encoding.UTF8.GetBytes("active") } };
		var contextAllowed = new SerializationContext(MessageComponentType.Key, TestTopic, headersAllowed);
		var resultAllowed = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextAllowed);

		// Test without header
		var headersWithout = new Headers();
		var contextWithout = new SerializationContext(MessageComponentType.Key, TestTopic, headersWithout);
		var resultWithout = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextWithout);

		// Assert
		Assert.That(resultExcluded.IsFiltered, Is.True, "Message with excluded value should be filtered");
		Assert.That(resultAllowed.IsFiltered, Is.False, "Message with allowed value should not be filtered");
		Assert.That(resultWithout.IsFiltered, Is.False,
			"Message without header should not be filtered (null is not equal)");
	}

	[Test]
	public void Deserializer_WithMultipleFiltersOnSameHeader_AllMustPass() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderNotEqualsFilter("status", "deleted")
				.WithHeaderNotEqualsFilter("status", "archived");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with first excluded value
		var headersDeleted = new Headers { { "status", Encoding.UTF8.GetBytes("deleted") } };
		var contextDeleted = new SerializationContext(MessageComponentType.Key, TestTopic, headersDeleted);
		var resultDeleted = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextDeleted);

		// Test with second excluded value
		var headersArchived = new Headers { { "status", Encoding.UTF8.GetBytes("archived") } };
		var contextArchived = new SerializationContext(MessageComponentType.Key, TestTopic, headersArchived);
		var resultArchived = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextArchived);

		// Test with allowed value
		var headersActive = new Headers { { "status", Encoding.UTF8.GetBytes("active") } };
		var contextActive = new SerializationContext(MessageComponentType.Key, TestTopic, headersActive);
		var resultActive = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextActive);

		// Assert
		Assert.That(resultDeleted.IsFiltered, Is.True, "Message with 'deleted' status should be filtered");
		Assert.That(resultArchived.IsFiltered, Is.True, "Message with 'archived' status should be filtered");
		Assert.That(resultActive.IsFiltered, Is.False, "Message with 'active' status should not be filtered");
	}

	[Test]
	public void Deserializer_WithCombinedExistsAndEqualsFilters_BothMustPass() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderExistsFilter("tenant-id")
				.WithHeaderEqualsFilter("event-type", "user-created");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with both headers present and matching
		var headersBoth = new Headers {
			{ "tenant-id", Encoding.UTF8.GetBytes("tenant-123") }, { "event-type", Encoding.UTF8.GetBytes("user-created") }
		};
		var contextBoth = new SerializationContext(MessageComponentType.Key, TestTopic, headersBoth);
		var resultBoth = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextBoth);

		// Test with tenant-id missing
		var headersNoTenant = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-created") } };
		var contextNoTenant = new SerializationContext(MessageComponentType.Key, TestTopic, headersNoTenant);
		var resultNoTenant = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextNoTenant);

		// Test with event-type not matching
		var headersWrongEvent = new Headers {
			{ "tenant-id", Encoding.UTF8.GetBytes("tenant-123") }, { "event-type", Encoding.UTF8.GetBytes("user-updated") }
		};
		var contextWrongEvent = new SerializationContext(MessageComponentType.Key, TestTopic, headersWrongEvent);
		var resultWrongEvent = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextWrongEvent);

		// Test with no headers
		var headersNone = new Headers();
		var contextNone = new SerializationContext(MessageComponentType.Key, TestTopic, headersNone);
		var resultNone = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextNone);

		// Assert
		Assert.That(resultBoth.IsFiltered, Is.False, "Message with both headers correct should not be filtered");
		Assert.That(resultNoTenant.IsFiltered, Is.True, "Message without tenant-id should be filtered");
		Assert.That(resultWrongEvent.IsFiltered, Is.True, "Message with wrong event-type should be filtered");
		Assert.That(resultNone.IsFiltered, Is.True, "Message without any headers should be filtered");
	}

	[Test]
	public void Deserializer_WithUnknownTopic_ReturnsNotFound() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		var headers = new Headers();
		var context = new SerializationContext(MessageComponentType.Key, "unknown-topic", headers);

		// Act
		var result = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context);

		// Assert
		Assert.That(result.IsFound(), Is.False);
	}

	[Test]
	public void Deserializer_WithIntegerHeaderValues_WorksCorrectly() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("partition-key", 42);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with matching integer
		var headersMatch = new Headers { { "partition-key", MessagePackSerializer.Serialize(42) } };
		var contextMatch = new SerializationContext(MessageComponentType.Key, TestTopic, headersMatch);
		var resultMatch = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextMatch);

		// Test with non-matching integer
		var headersNoMatch = new Headers { { "partition-key", MessagePackSerializer.Serialize(99) } };
		var contextNoMatch = new SerializationContext(MessageComponentType.Key, TestTopic, headersNoMatch);
		var resultNoMatch = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextNoMatch);

		// Assert
		Assert.That(resultMatch.IsFiltered, Is.False, "Message with matching integer header should not be filtered");
		Assert.That(resultNoMatch.IsFiltered, Is.True, "Message with non-matching integer header should be filtered");
	}

	[Test]
	public void Deserializer_WithMultipleDifferentHeaders_FiltersCorrectly() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderExistsFilter("tenant-id")
				.WithHeaderEqualsFilter("event-type", "user-created")
				.WithHeaderNotEqualsFilter("status", "deleted");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with all conditions met
		var headersValid = new Headers {
			{ "tenant-id", Encoding.UTF8.GetBytes("tenant-123") },
			{ "event-type", Encoding.UTF8.GetBytes("user-created") },
			{ "status", Encoding.UTF8.GetBytes("active") }
		};
		var contextValid = new SerializationContext(MessageComponentType.Key, TestTopic, headersValid);
		var resultValid = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextValid);

		// Test with deleted status
		var headersDeleted = new Headers {
			{ "tenant-id", Encoding.UTF8.GetBytes("tenant-123") },
			{ "event-type", Encoding.UTF8.GetBytes("user-created") },
			{ "status", Encoding.UTF8.GetBytes("deleted") }
		};
		var contextDeleted = new SerializationContext(MessageComponentType.Key, TestTopic, headersDeleted);
		var resultDeleted = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextDeleted);

		// Assert
		Assert.That(resultValid.IsFiltered, Is.False, "Message meeting all filter criteria should not be filtered");
		Assert.That(resultDeleted.IsFiltered, Is.True, "Message with deleted status should be filtered");
	}

	[Test]
	public void Deserializer_WithMultipleStringValues_AcceptsAnyMatch() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("event-type", "user-created", "user-updated", "user-activated");
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with first value
		var headersCreated = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-created") } };
		var contextCreated = new SerializationContext(MessageComponentType.Key, TestTopic, headersCreated);
		var resultCreated = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextCreated);

		// Test with second value
		var headersUpdated = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-updated") } };
		var contextUpdated = new SerializationContext(MessageComponentType.Key, TestTopic, headersUpdated);
		var resultUpdated = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextUpdated);

		// Test with third value
		var headersActivated = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-activated") } };
		var contextActivated = new SerializationContext(MessageComponentType.Key, TestTopic, headersActivated);
		var resultActivated = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextActivated);

		// Test with non-matching value
		var headersDeleted = new Headers { { "event-type", Encoding.UTF8.GetBytes("user-deleted") } };
		var contextDeleted = new SerializationContext(MessageComponentType.Key, TestTopic, headersDeleted);
		var resultDeleted = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextDeleted);

		// Assert
		Assert.That(resultCreated.IsFiltered, Is.False, "Message with user-created should not be filtered");
		Assert.That(resultUpdated.IsFiltered, Is.False, "Message with user-updated should not be filtered");
		Assert.That(resultActivated.IsFiltered, Is.False, "Message with user-activated should not be filtered");
		Assert.That(resultDeleted.IsFiltered, Is.True, "Message with user-deleted should be filtered");
	}

	[Test]
	public void Deserializer_WithMultipleIntValues_AcceptsAnyMatch() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("tenant-id", 1, 2, 3);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with first value
		var headers1 = new Headers { { "tenant-id", MessagePackSerializer.Serialize(1) } };
		var context1 = new SerializationContext(MessageComponentType.Key, TestTopic, headers1);
		var result1 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context1);

		// Test with second value
		var headers2 = new Headers { { "tenant-id", MessagePackSerializer.Serialize(2) } };
		var context2 = new SerializationContext(MessageComponentType.Key, TestTopic, headers2);
		var result2 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context2);

		// Test with third value
		var headers3 = new Headers { { "tenant-id", MessagePackSerializer.Serialize(3) } };
		var context3 = new SerializationContext(MessageComponentType.Key, TestTopic, headers3);
		var result3 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context3);

		// Test with non-matching value
		var headers4 = new Headers { { "tenant-id", MessagePackSerializer.Serialize(4) } };
		var context4 = new SerializationContext(MessageComponentType.Key, TestTopic, headers4);
		var result4 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context4);

		// Assert
		Assert.That(result1.IsFiltered, Is.False, "Message with tenant-id 1 should not be filtered");
		Assert.That(result2.IsFiltered, Is.False, "Message with tenant-id 2 should not be filtered");
		Assert.That(result3.IsFiltered, Is.False, "Message with tenant-id 3 should not be filtered");
		Assert.That(result4.IsFiltered, Is.True, "Message with tenant-id 4 should be filtered");
	}

	[Test]
	public void Deserializer_WithMultipleLongValues_AcceptsAnyMatch() {
		// Arrange
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("timestamp", 1000L, 2000L, 3000L);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with first value
		var headers1 = new Headers { { "timestamp", MessagePackSerializer.Serialize(1000L) } };
		var context1 = new SerializationContext(MessageComponentType.Key, TestTopic, headers1);
		var result1 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context1);

		// Test with second value
		var headers2 = new Headers { { "timestamp", MessagePackSerializer.Serialize(2000L) } };
		var context2 = new SerializationContext(MessageComponentType.Key, TestTopic, headers2);
		var result2 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context2);

		// Test with third value
		var headers3 = new Headers { { "timestamp", MessagePackSerializer.Serialize(3000L) } };
		var context3 = new SerializationContext(MessageComponentType.Key, TestTopic, headers3);
		var result3 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context3);

		// Test with non-matching value
		var headers4 = new Headers { { "timestamp", MessagePackSerializer.Serialize(4000L) } };
		var context4 = new SerializationContext(MessageComponentType.Key, TestTopic, headers4);
		var result4 = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, context4);

		// Assert
		Assert.That(result1.IsFiltered, Is.False, "Message with timestamp 1000 should not be filtered");
		Assert.That(result2.IsFiltered, Is.False, "Message with timestamp 2000 should not be filtered");
		Assert.That(result3.IsFiltered, Is.False, "Message with timestamp 3000 should not be filtered");
		Assert.That(result4.IsFiltered, Is.True, "Message with timestamp 4000 should be filtered");
	}

	[Test]
	public void Deserializer_WithIntLongCompatibility_AcceptsAnyMatch() {
		// Arrange - filter with int values
		var sp = CreateServiceProvider(builder => {
			builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
				.WithHeaderEqualsFilter("tenant-id", 42, 43);
		});
		var handlers = sp.GetRequiredService<KafkaCacheHandlers>();

		var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName,
			KafkaCaches.InstanceIdBytes,
			handlers.Handlers);

		// Test with int value
		var headersInt = new Headers { { "tenant-id", MessagePackSerializer.Serialize(42) } };
		var contextInt = new SerializationContext(MessageComponentType.Key, TestTopic, headersInt);
		var resultInt = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextInt);

		// Test with long value (same numeric value)
		var headersLong = new Headers { { "tenant-id", MessagePackSerializer.Serialize(42L) } };
		var contextLong = new SerializationContext(MessageComponentType.Key, TestTopic, headersLong);
		var resultLong = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, contextLong);

		// Assert
		Assert.That(resultInt.IsFiltered, Is.False, "Message with int 42 should not be filtered");
		Assert.That(resultLong.IsFiltered, Is.False,
			"Message with long 42 should not be filtered (int/long compatibility)");
	}

		[Test]
		public void Deserializer_WithPredicateFilter_IntGreaterOrEqual_FiltersCorrectly() {
				// Arrange
				const string headerName = "priority";

				var sp = CreateServiceProvider(builder => {
						builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
							.WithHeaderFilter<int>(headerName, it => it >= 12);
				});

				var handlers = sp.GetRequiredService<KafkaCacheHandlers>();
				var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
					KafkaCaches.ProducerInstanceIdHeaderName,
					KafkaCaches.InstanceIdBytes,
					handlers.Handlers);

				// less => filtered
				var headersLess = new Headers { { headerName, MessagePackSerializer.Serialize<int?>(11) } };
				var ctxLess = new SerializationContext(MessageComponentType.Key, TestTopic, headersLess);

				// equal => not filtered
				var headersEqual = new Headers { { headerName, MessagePackSerializer.Serialize<int?>(12) } };
				var ctxEqual = new SerializationContext(MessageComponentType.Key, TestTopic, headersEqual);

				// greater => not filtered
				var headersGreater = new Headers { { headerName, MessagePackSerializer.Serialize<int?>(20) } };
				var ctxGreater = new SerializationContext(MessageComponentType.Key, TestTopic, headersGreater);

				// Act
				var resultLess = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxLess);
				var resultEqual = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxEqual);
				var resultGreater = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxGreater);

				// Assert
				Assert.That(resultLess.IsFiltered, Is.True, "Value < 12 should be filtered");
				Assert.That(resultEqual.IsFiltered, Is.False, "Value == 12 should not be filtered");
				Assert.That(resultGreater.IsFiltered, Is.False, "Value > 12 should not be filtered");
		}

		[Test]
		public void Deserializer_WithPredicateFilter_DynamicDateTime_FiltersCorrectly() {
				// Arrange
				const string headerName = "timestamp";
				var threshold = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);

				var sp = CreateServiceProvider(builder => {
						builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
							.WithHeaderFilter<DateTime>(headerName, it => it >= threshold);
				});

				var handlers = sp.GetRequiredService<KafkaCacheHandlers>();
				var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
					KafkaCaches.ProducerInstanceIdHeaderName,
					KafkaCaches.InstanceIdBytes,
					handlers.Handlers);

				// earlier => filtered
				var headersEarlier = new Headers { { headerName, MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(-1)) } };
				var ctxEarlier = new SerializationContext(MessageComponentType.Key, TestTopic, headersEarlier);

				// equal => not filtered
				var headersEqual = new Headers { { headerName, MessagePackSerializer.Serialize<DateTime?>(threshold) } };
				var ctxEqual = new SerializationContext(MessageComponentType.Key, TestTopic, headersEqual);

				// later => not filtered
				var headersLater = new Headers { { headerName, MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(1)) } };
				var ctxLater = new SerializationContext(MessageComponentType.Key, TestTopic, headersLater);

				// Act
				var resultEarlier = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxEarlier);
				var resultEqual = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxEqual);
				var resultLater = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxLater);

				// Assert
				Assert.That(resultEarlier.IsFiltered, Is.True, "DateTime before threshold should be filtered");
				Assert.That(resultEqual.IsFiltered, Is.False, "DateTime equal to threshold should not be filtered");
				Assert.That(resultLater.IsFiltered, Is.False, "DateTime after threshold should not be filtered");
		}

		[Test]
		public void Deserializer_WithPredicateFilter_NullHeaderValue_PassOnNullTrue_DoesNotFilter() {
				// Arrange
				const string headerName = "priority";

				var sp = CreateServiceProvider(builder => {
						builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
							.WithHeaderFilter<int>(headerName, it => it >= 12);
				});

				var handlers = sp.GetRequiredService<KafkaCacheHandlers>();
				var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
					KafkaCaches.ProducerInstanceIdHeaderName,
					KafkaCaches.InstanceIdBytes,
					handlers.Handlers);

				var headers = new Headers { { headerName, MessagePackSerializer.Serialize<int?>(null) } };
				var ctx = new SerializationContext(MessageComponentType.Key, TestTopic, headers);

				// Act
				var result = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctx);

				// Assert
				Assert.That(result.IsFiltered, Is.False, "Null header value should not be filtered when passOnNull is true (default)");
		}

		[Test]
		public void Deserializer_WithPredicateFilter_NullHeaderValue_PassOnNullFalse_Filters() {
				// Arrange
				const string headerName = "priority";

				var sp = CreateServiceProvider(builder => {
						builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
							.WithHeaderFilter<int>(headerName, it => it >= 12, passOnNull: false);
				});

				var handlers = sp.GetRequiredService<KafkaCacheHandlers>();
				var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
					KafkaCaches.ProducerInstanceIdHeaderName,
					KafkaCaches.InstanceIdBytes,
					handlers.Handlers);

				var headers = new Headers { { headerName, MessagePackSerializer.Serialize<int?>(null) } };
				var ctx = new SerializationContext(MessageComponentType.Key, TestTopic, headers);

				// Act
				var result = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctx);

				// Assert
				Assert.That(result.IsFiltered, Is.True, "Null header value should be filtered when passOnNull is false");
		}

		[Test]
		public void Deserializer_WithPredicateFilter_CombinedWithOtherFilters_AllMustPass() {
				// Arrange
				var sp = CreateServiceProvider(builder => {
						builder.AddCache<TestEntityWithLongTimestampCache, int, TestEntityWithLongTimestamp>(TestTopic)
							.WithHeaderExistsFilter("tenant-id")
							.WithHeaderFilter<int>("priority", static it => it >= 5 && it < 100);
				});

				var handlers = sp.GetRequiredService<KafkaCacheHandlers>();
				var deserializer = new HeadersFilteringWithHandlerRentedBytesDeserializer(
					KafkaCaches.ProducerInstanceIdHeaderName,
					KafkaCaches.InstanceIdBytes,
					handlers.Handlers);

				// Both conditions met
				var headersValid = new Headers {
						{ "tenant-id", MessagePackSerializer.Serialize("tenant-123") },
						{ "priority", MessagePackSerializer.Serialize<int?>(50) }
				};
				var ctxValid = new SerializationContext(MessageComponentType.Key, TestTopic, headersValid);

				// tenant-id missing
				var headersNoTenant = new Headers {
						{ "priority", MessagePackSerializer.Serialize<int?>(50) }
				};
				var ctxNoTenant = new SerializationContext(MessageComponentType.Key, TestTopic, headersNoTenant);

				// priority out of range
				var headersOutOfRange = new Headers {
						{ "tenant-id", MessagePackSerializer.Serialize("tenant-123") },
						{ "priority", MessagePackSerializer.Serialize<int?>(200) }
				};
				var ctxOutOfRange = new SerializationContext(MessageComponentType.Key, TestTopic, headersOutOfRange);

				// Act
				var resultValid = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxValid);
				var resultNoTenant = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxNoTenant);
				var resultOutOfRange = deserializer.Deserialize(ReadOnlySpan<byte>.Empty, false, ctxOutOfRange);

				// Assert
				Assert.That(resultValid.IsFiltered, Is.False, "Message meeting all criteria should not be filtered");
				Assert.That(resultNoTenant.IsFiltered, Is.True, "Message without tenant-id should be filtered");
				Assert.That(resultOutOfRange.IsFiltered, Is.True, "Message with priority out of range should be filtered");
		}
}
namespace Prague.Kafka.TestAdaptor.Tests.HeaderFilters;

using Prague.Kafka.Filters;
using Prague.Kafka.SerDe;
using MessagePack;
using NUnit.Framework;

[TestFixture]
public class KafkaHeaderFiltersTests {
	private byte[] SerializeValue<T>(T value) {
		return MessagePackSerializer.Serialize(value);
	}

	[TestFixture]
	public class KafkaHeaderExistsFilterTests {
		[Test]
		public void ShouldProcess_Always_ReturnsTrue_AndSetsSearchStateToTrue() {
			// Arrange
			var filter = new KafkaHeaderExistsFilter();
			var searchState = false;
			var headerBytes = Array.Empty<byte>();

			// Act
			var result = filter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.True);
			Assert.That(searchState, Is.True);
		}

		[Test]
		public void IsInitialFalse_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderExistsFilter();

			// Act & Assert
			Assert.That(filter.IsInitialFalse, Is.True);
		}

		[Test]
		public void ShouldProcess_WithExistingSearchStateTrue_StillSetsToTrue() {
			// Arrange
			var filter = new KafkaHeaderExistsFilter();
			var searchState = true;
			var headerBytes = Array.Empty<byte>();

			// Act
			var result = filter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.True);
			Assert.That(searchState, Is.True);
		}
	}

	[TestFixture]
	public class KafkaHeaderNotExistsFilterTests {
		[Test]
		public void ShouldProcess_Always_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderNotExistsFilter();
			var headerBytes = Array.Empty<byte>();

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void IsInitialFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderNotExistsFilter();

			// Act & Assert
			Assert.That(filter.IsInitialFalse, Is.False);
		}
	}

	[TestFixture]
	public class KafkaHeaderEqualsFilterTests {
		[Test]
		public void ShouldProcess_WithMatchingStringValue_ReturnsTrue() {
			// Arrange
			var expectedValue = "test-value";
			var filter = new KafkaHeaderEqualsFilter<string>(expectedValue);
			var headerBytes = MessagePackSerializer.Serialize(expectedValue);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithDifferentStringValue_ReturnsFalse() {
			// Arrange
			var expectedValue = "test-value";
			var filter = new KafkaHeaderEqualsFilter<string>(expectedValue);
			var headerBytes = MessagePackSerializer.Serialize("different-value");

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithMatchingIntValue_ReturnsTrue() {
			// Arrange
			var expectedValue = 42;
			var filter = new KafkaHeaderEqualsFilter<int>(expectedValue);
			var headerBytes = MessagePackSerializer.Serialize(expectedValue);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithDifferentIntValue_ReturnsFalse() {
			// Arrange
			var expectedValue = 42;
			var filter = new KafkaHeaderEqualsFilter<int>(expectedValue);
			var headerBytes = MessagePackSerializer.Serialize(100);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithNullValue_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderEqualsFilter<string>("test");
			var headerBytes = MessagePackSerializer.Serialize<string?>(null);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert - null equals anything in this implementation
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMatchingBoolValue_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderEqualsFilter<bool>(true);
			var headerBytes = MessagePackSerializer.Serialize(true);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void IsInitialFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderEqualsFilter<string>("test");

			// Act & Assert
			Assert.That(filter.IsInitialFalse, Is.False);
		}
	}

	[TestFixture]
	public class KafkaHeaderNotEqualsFilterTests {
		[Test]
		public void ShouldProcess_WithDifferentStringValue_ReturnsTrue() {
			// Arrange
			var excludedValue = "excluded-value";
			var filter = new KafkaHeaderNotEqualsFilter<string>(excludedValue);
			var headerBytes = MessagePackSerializer.Serialize("different-value");

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMatchingStringValue_ReturnsFalse() {
			// Arrange
			var excludedValue = "excluded-value";
			var filter = new KafkaHeaderNotEqualsFilter<string>(excludedValue);
			var headerBytes = MessagePackSerializer.Serialize(excludedValue);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithDifferentIntValue_ReturnsTrue() {
			// Arrange
			var excludedValue = 42;
			var filter = new KafkaHeaderNotEqualsFilter<int>(excludedValue);
			var headerBytes = MessagePackSerializer.Serialize(100);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMatchingIntValue_ReturnsFalse() {
			// Arrange
			var excludedValue = 42;
			var filter = new KafkaHeaderNotEqualsFilter<int>(excludedValue);
			var headerBytes = MessagePackSerializer.Serialize(excludedValue);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithNullValue_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderNotEqualsFilter<string>("test");
			var headerBytes = MessagePackSerializer.Serialize<string?>(null);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert - null is not equal to anything
			Assert.That(result, Is.True);
		}

		[Test]
		public void IsInitialFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderNotEqualsFilter<string>("test");

			// Act & Assert
			Assert.That(filter.IsInitialFalse, Is.False);
		}
	}

	[TestFixture]
	public class KafkaCombinedHeaderFilterTests {
		[Test]
		public void ShouldProcess_WithAllFiltersReturningTrue_ReturnsTrue() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderNotEqualsFilter<string>("excluded"), new KafkaHeaderNotEqualsFilter<string>("also-excluded")
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("allowed-value");

			// Act
			var result = combinedFilter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithFirstFilterReturningFalse_ReturnsFalse() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderEqualsFilter<string>("required-value"), new KafkaHeaderNotEqualsFilter<string>("excluded")
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("different-value");

			// Act
			var result = combinedFilter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithSecondFilterReturningFalse_ReturnsFalse() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderNotEqualsFilter<string>("excluded-1"), new KafkaHeaderEqualsFilter<string>("required-value")
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("different-value");

			// Act
			var result = combinedFilter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithExistsFilterAndOtherFilters_UpdatesSearchState() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderExistsFilter(), new KafkaHeaderEqualsFilter<string>("test-value")
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);
			var searchState = false;
			var headerBytes = MessagePackSerializer.Serialize("test-value");

			// Act
			var result = combinedFilter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.True);
			Assert.That(searchState, Is.True);
		}

		[Test]
		public void IsInitialFalse_WithAllFiltersHavingIsInitialFalseTrue_ReturnsTrue() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderExistsFilter(), new KafkaHeaderExistsFilter()
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);

			// Act & Assert
			Assert.That(combinedFilter.IsInitialFalse, Is.True);
		}

		[Test]
		public void IsInitialFalse_WithSomeFiltersHavingIsInitialFalseFalse_ReturnsFalse() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderExistsFilter(), new KafkaHeaderEqualsFilter<string>("test")
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);

			// Act & Assert
			Assert.That(combinedFilter.IsInitialFalse, Is.False);
		}

		[Test]
		public void ShouldProcess_ShortCircuits_WhenFirstFilterReturnsFalse() {
			// Arrange
			var filters = new List<KafkaHeaderFilterExecutor> {
				new KafkaHeaderNotExistsFilter(), // Always returns false
				new KafkaHeaderExistsFilter() // Should not be called
			};
			var combinedFilter = new KafkaCombinedHeaderFilter(filters);
			var searchState = false; // ExistsFilter would set this to true if called
			var headerBytes = MessagePackSerializer.Serialize("value");

			// Act
			var result = combinedFilter.ShouldProcess(ref searchState, headerBytes);

			// Assert
			Assert.That(result, Is.False);
			Assert.That(searchState, Is.False); // Proves ExistsFilter was not called
		}
	}

	[TestFixture]
	public class KafkaHeaderFiltersIntegrationTests {
		[Test]
		public void Create_WithNullFilters_ReturnsEmptyFilters() {
			// Arrange & Act
			var filters = KafkaHeaderFilters.Create(null);

			// Assert
			Assert.That(filters, Is.Not.Null);
			Assert.That(filters.InitialState, Is.True);
		}

		[Test]
		public void Create_WithEmptyDictionary_ReturnsEmptyFilters() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>>();

			// Act
			var filters = KafkaHeaderFilters.Create(filterDict);

			// Assert
			Assert.That(filters, Is.Not.Null);
			Assert.That(filters.InitialState, Is.True);
		}

		[Test]
		public void ShouldProcess_WithEmptyFilters_AlwaysReturnsTrue() {
			// Arrange
			var filters = KafkaHeaderFilters.Create(null);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("value");

			// Act
			var result = filters.ShouldProcess(ref searchState, "any-header", headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithUnregisteredHeader_ReturnsTrue() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["registered-header"] = new() { new KafkaHeaderEqualsFilter<string>("test") }
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("value");

			// Act
			var result = filters.ShouldProcess(ref searchState, "unregistered-header", headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithRegisteredHeaderAndMatchingValue_ReturnsTrue() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["event-type"] = new() { new KafkaHeaderEqualsFilter<string>("user-created") }
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("user-created");

			// Act
			var result = filters.ShouldProcess(ref searchState, "event-type", headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithRegisteredHeaderAndNonMatchingValue_ReturnsFalse() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["event-type"] = new() { new KafkaHeaderEqualsFilter<string>("user-created") }
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = true;
			var headerBytes = MessagePackSerializer.Serialize("user-updated");

			// Act
			var result = filters.ShouldProcess(ref searchState, "event-type", headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithExistsFilter_UpdatesSearchState() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["required-header"] = new() { new KafkaHeaderExistsFilter() }
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = false;
			var headerBytes = MessagePackSerializer.Serialize("any-value");

			// Act
			var result = filters.ShouldProcess(ref searchState, "required-header", headerBytes);

			// Assert
			Assert.That(result, Is.True);
			Assert.That(searchState, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMultipleFiltersOnSameHeader_AllMustPass() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["status"] = new() {
					new KafkaHeaderNotEqualsFilter<string>("deleted"), new KafkaHeaderNotEqualsFilter<string>("archived")
				}
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = true;

			// Act - should pass
			var result1 = filters.ShouldProcess(ref searchState, "status", MessagePackSerializer.Serialize("active"));

			// Act - should fail (matches first filter's excluded value)
			var result2 = filters.ShouldProcess(ref searchState, "status", MessagePackSerializer.Serialize("deleted"));

			// Act - should fail (matches second filter's excluded value)
			var result3 = filters.ShouldProcess(ref searchState, "status", MessagePackSerializer.Serialize("archived"));

			// Assert
			Assert.That(result1, Is.True);
			Assert.That(result2, Is.False);
			Assert.That(result3, Is.False);
		}

		[Test]
		public void InitialState_WithExistsFilter_IsFalse() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["required-header"] = new() { new KafkaHeaderExistsFilter() }
			};

			// Act
			var filters = KafkaHeaderFilters.Create(filterDict);

			// Assert
			Assert.That(filters.InitialState, Is.False);
		}

		[Test]
		public void InitialState_WithNonExistsFilters_IsTrue() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["event-type"] = new() { new KafkaHeaderEqualsFilter<string>("user-created") }
			};

			// Act
			var filters = KafkaHeaderFilters.Create(filterDict);

			// Assert
			Assert.That(filters.InitialState, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMultipleHeadersIncludingExists_WorksCorrectly() {
			// Arrange
			var filterDict = new Dictionary<string, List<KafkaHeaderFilterExecutor>> {
				["tenant-id"] = new() { new KafkaHeaderExistsFilter() },
				["event-type"] = new() { new KafkaHeaderEqualsFilter<string>("user-created") }
			};
			var filters = KafkaHeaderFilters.Create(filterDict);
			var searchState = filters.InitialState; // Should be false due to ExistsFilter

			// Act - process tenant-id header (sets searchState to true)
			var result1 = filters.ShouldProcess(ref searchState, "tenant-id", MessagePackSerializer.Serialize("tenant-123"));

			// Act - process event-type header with matching value
			var result2 =
				filters.ShouldProcess(ref searchState, "event-type", MessagePackSerializer.Serialize("user-created"));

			// Assert
			Assert.That(result1, Is.True);
			Assert.That(result2, Is.True);
			Assert.That(searchState, Is.True);
		}
	}

	[TestFixture]
	public class KafkaHeaderPredicateFilterTests {
		[Test]
		public void ShouldProcess_WithMatchingPredicate_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = MessagePackSerializer.Serialize<int?>(15);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithNonMatchingPredicate_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = MessagePackSerializer.Serialize<int?>(5);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithEqualBoundary_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = MessagePackSerializer.Serialize<int?>(12);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithDynamicDateTimePredicate_EvaluatesOnEachCall() {
			// Arrange
			var threshold = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);
			var filter = new KafkaHeaderPredicateFilter<DateTime>(it => it < threshold);

			var earlier = MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(-1));
			var later = MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(1));

			// Act + Assert
			Assert.That(filter.ShouldProcess(earlier), Is.True);
			Assert.That(filter.ShouldProcess(later), Is.False);
		}

		[Test]
		public void ShouldProcess_WithNullValue_DefaultPassOnNull_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = MessagePackSerializer.Serialize<int?>(null);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithNullValue_PassOnNullFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12, passOnNull: false);
			var headerBytes = MessagePackSerializer.Serialize<int?>(null);

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithMessagePackNil_DefaultPassOnNull_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = new byte[] { 0xC0 }; // MessagePack nil

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithMessagePackNil_PassOnNullFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12, passOnNull: false);
			var headerBytes = new byte[] { 0xC0 }; // MessagePack nil

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.False);
		}

		[Test]
		public void ShouldProcess_WithInvalidPayload_ReturnsTrue() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);
			var headerBytes = MessagePackSerializer.Serialize("not-an-int");

			// Act
			var result = filter.ShouldProcess(headerBytes);

			// Assert
			Assert.That(result, Is.True);
		}

		[Test]
		public void ShouldProcess_WithLessThanPredicate_WorksCorrectly() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it < 10);

			// Act + Assert
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(9)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(10)), Is.False);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(11)), Is.False);
		}

		[Test]
		public void ShouldProcess_WithComplexPredicate_WorksCorrectly() {
			// Arrange - range filter: 5 <= x < 15
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 5 && it < 15);

			// Act + Assert
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(4)), Is.False);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(5)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(10)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize<int?>(15)), Is.False);
		}

		[Test]
		public void IsInitialFalse_ReturnsFalse() {
			// Arrange
			var filter = new KafkaHeaderPredicateFilter<int>(it => it >= 12);

			// Act & Assert
			Assert.That(filter.IsInitialFalse, Is.False);
		}
	}

	// Covers the wire format that codegen Derich emits via HeadersSerDe — raw little-endian bytes
	// for int/long/Guid, UTF-8 for string. Without these tests, the consumer-side filters silently
	// misread codegen-produced headers (long → 0L from MessagePack misinterpretation).
	[TestFixture]
	public class CodegenWireFormatCompatibilityTests {
		[Test]
		public void PredicateFilter_OnRawLongBytes_FromHeadersSerDe_ObservesActualValue() {
			const long expected = 1735689600000L;
			long? observed = null;
			var filter = new KafkaHeaderPredicateFilter<long>(it => {
				observed = it;
				return true;
			});

			var result = filter.ShouldProcess(HeadersSerDe.SerializeLong(expected));

			Assert.That(result, Is.True);
			Assert.That(observed, Is.EqualTo(expected),
				"Filter must read the long value from codegen's raw-byte wire format, not misinterpret it as MessagePack");
		}

		[Test]
		public void PredicateFilter_OnRawIntBytes_FromHeadersSerDe_ObservesActualValue() {
			const int expected = 123456789;
			int? observed = null;
			var filter = new KafkaHeaderPredicateFilter<int>(it => {
				observed = it;
				return true;
			});

			var result = filter.ShouldProcess(HeadersSerDe.SerializeInt(expected));

			Assert.That(result, Is.True);
			Assert.That(observed, Is.EqualTo(expected));
		}

		[Test]
		public void PredicateFilter_OnRawGuidBytes_FromHeadersSerDe_ObservesActualValue() {
			var expected = Guid.NewGuid();
			Guid? observed = null;
			var filter = new KafkaHeaderPredicateFilter<Guid>(it => {
				observed = it;
				return true;
			});

			var result = filter.ShouldProcess(HeadersSerDe.SerializeGuid(expected));

			Assert.That(result, Is.True);
			Assert.That(observed, Is.EqualTo(expected));
		}

		[Test]
		public void EqualsFilter_OnRawLongBytes_FromHeadersSerDe_MatchesByValue() {
			const long value = 9_876_543_210L;
			var filter = new KafkaHeaderEqualsFilter<long>(value);

			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeLong(value)), Is.True);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeLong(value + 1)), Is.False);
		}

		[Test]
		public void EqualsFilter_OnRawGuidBytes_FromHeadersSerDe_MatchesByValue() {
			var guid = Guid.NewGuid();
			var filter = new KafkaHeaderEqualsFilter<Guid>(guid);

			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeGuid(guid)), Is.True);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeGuid(Guid.NewGuid())), Is.False);
		}

		[Test]
		public void NotEqualsFilter_OnRawIntBytes_FromHeadersSerDe_MatchesByValue() {
			const int excluded = 42;
			var filter = new KafkaHeaderNotEqualsFilter<int>(excluded);

			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeInt(excluded)), Is.False);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeInt(43)), Is.True);
		}

		[Test]
		public void NumericEqualsFilter_OnRawLongBytes_FromHeadersSerDe_MatchesByValue() {
			const long value = 1_000_000_000L;
			var filter = new KafkaHeaderEqualsFilter<long>(value);

			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeLong(value)), Is.True);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeLong(value + 1)), Is.False);
		}

		[Test]
		public void PredicateFilter_StillAcceptsMessagePackBytes_ForBackwardsCompat() {
			// Even though codegen emits raw bytes, headers produced manually via MessagePack
			// (e.g. by a producer that doesn't use this library's codegen) must continue to work.
			const long expected = 1735689600000L;
			long? observed = null;
			var filter = new KafkaHeaderPredicateFilter<long>(it => {
				observed = it;
				return true;
			});

			var mpBytes = MessagePackSerializer.Serialize<long?>(expected);
			Assert.That(mpBytes.Length, Is.Not.EqualTo(8),
				"sanity: MessagePack-encoded long is never exactly 8 bytes, so raw-byte fast-path doesn't falsely match");

			var result = filter.ShouldProcess(mpBytes);

			Assert.That(result, Is.True);
			Assert.That(observed, Is.EqualTo(expected));
		}
	}

	[TestFixture]
	public class MessagePackInputCoverageTests {
		[Test]
		public void KafkaHeaderEqualsFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsFilter<int>(42);
			var bytes = MessagePackSerializer.Serialize(42);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsFilter_Int_AcceptsLegacyRawInput() {
			var filter = new KafkaHeaderEqualsFilter<int>(42);
			var bytes = HeadersSerDe.SerializeInt(42);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsFilter_Long_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsFilter<long>(9876543210L);
			var bytes = MessagePackSerializer.Serialize(9876543210L);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsFilter_Long_AcceptsLegacyRawInput() {
			var filter = new KafkaHeaderEqualsFilter<long>(9876543210L);
			var bytes = HeadersSerDe.SerializeLong(9876543210L);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderNotEqualsFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderNotEqualsFilter<int>(42);
			var bytes = MessagePackSerializer.Serialize(43);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsNumericFilter_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsNumericFilter(42);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(42)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(43)), Is.False);
		}

		[Test]
		public void KafkaHeaderEqualsNumericFilter_AcceptsLegacyRawInput() {
			var filter = new KafkaHeaderEqualsNumericFilter(42);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeInt(42)), Is.True);
			Assert.That(filter.ShouldProcess(HeadersSerDe.SerializeLong(42L)), Is.True);
		}

		[Test]
		public void KafkaHeaderNotEqualsNumericFilter_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderNotEqualsNumericFilter(42L);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(43L)), Is.True);
		}

		[Test]
		public void KafkaHeaderPredicateFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderPredicateFilter<int>(x => x > 100);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(150)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(50)), Is.False);
		}

		[Test]
		public void KafkaHeaderPredicateFilter_Long_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderPredicateFilter<long>(x => x > 1000L);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(5000L)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePackSerializer.Serialize(500L)), Is.False);
		}
	}
}
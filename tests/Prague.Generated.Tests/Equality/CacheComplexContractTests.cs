namespace Prague.Generated.Tests.Equality;

using global::TestModels;
using NUnit.Framework;

[TestFixture]
public class CacheComplexContractTests {
	[Test]
	public void CacheEquals_DifferentEventIds_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem { EventId = 1 };
		var item2 = new ClassLiveObjectCacheItem { EventId = 2 };

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void CacheEquals_SameEventIds_NullLiveObjects_ReturnsTrue() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem { EventId = 1, LiveObject = null };
		var item2 = new ClassLiveObjectCacheItem { EventId = 1, LiveObject = null };

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);
	}

	[Test]
	public void CacheEquals_SameEventIds_OneLiveObjectNull_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem { EventId = 1, LiveObject = new OldBaseTelemetry() };
		var item2 = new ClassLiveObjectCacheItem { EventId = 1, LiveObject = null };

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void CacheEquals_SameBaseLiveData_ReturnsTrue() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				DeviceKind = DeviceType.Sensor,
				PrimaryValue = 2,
				SecondaryValue = 1,
				SourceUrl = "http://tracker.com",
				PeriodId = ReadingPhase.Phase8
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				DeviceKind = DeviceType.Sensor,
				PrimaryValue = 2,
				SecondaryValue = 1,
				SourceUrl = "http://tracker.com",
				PeriodId = ReadingPhase.Phase8
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentBaseLiveDataScores_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry { PrimaryValue = 2, SecondaryValue = 1 }
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry { PrimaryValue = 3, SecondaryValue = 1 }
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void CacheEquals_SameSensorTelemetry_ReturnsTrue() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30,
				PrimaryValue = 6,
				SecondaryValue = 4
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30,
				PrimaryValue = 6,
				SecondaryValue = 4
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentSensorTelemetry_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player2",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void CacheEquals_BaseTelemetryVsSensorTelemetry_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry { DeviceKind = DeviceType.Sensor }
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldSensorTelemetry { DeviceKind = DeviceType.Sensor }
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void CacheEquals_ComplexBaseLiveDataWithCollections_ReturnsTrue() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 },
					new() { PrimaryValue = 3, SecondaryValue = 6, PeriodId = ReadingPhase.Phase9 }
				},
				PeriodScore = new List<PeriodReading> {
					new() {
						PeriodId = ReadingPhase.Phase8,
						PeriodNumber = "1",
						PeriodDisplayName = "Set 1"
					}
				}
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 },
					new() { PrimaryValue = 3, SecondaryValue = 6, PeriodId = ReadingPhase.Phase9 }
				},
				PeriodScore = new List<PeriodReading> {
					new() {
						PeriodId = ReadingPhase.Phase8,
						PeriodNumber = "1",
						PeriodDisplayName = "Set 1"
					}
				}
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);
	}

	[Test]
	public void CacheEquals_DifferentCollectionCounts_ReturnsFalse() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 }
				}
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 1,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 },
					new() { PrimaryValue = 3, SecondaryValue = 6, PeriodId = ReadingPhase.Phase9 }
				}
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void Clone_BaseLiveData_CreatesDeepCopy() {
		// Arrange
		var original = new ClassLiveObjectCacheItem {
			EventId = 123,
			LiveObject = new OldBaseTelemetry {
				DeviceKind = DeviceType.Sensor,
				PrimaryValue = 2,
				SecondaryValue = 1,
				SourceUrl = "http://tracker.com",
				Minute = "45",
				PeriodDisplayName = "First Half",
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 1, SecondaryValue = 0, PeriodId = ReadingPhase.Phase6 }
				}
			}
		};

		// Act
		var clone = original.Clone();

		// Assert
		Assert.That(clone.CacheEquals(original), Is.True);
		Assert.That(ReferenceEquals(original, clone), Is.False);
		Assert.That(ReferenceEquals(original.LiveObject, clone.LiveObject), Is.False);
		Assert.That(ReferenceEquals(original.LiveObject.ReadingSamples, clone.LiveObject.ReadingSamples), Is.False);
	}

	[Test]
	public void Clone_SensorTelemetry_CreatesDeepCopyWithCorrectType() {
		// Arrange
		var original = new ClassLiveObjectCacheItem {
			EventId = 456,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30,
				PrimaryValue = 6,
				SecondaryValue = 4,
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 }
				}
			}
		};

		// Act
		var clone = original.Clone();

		// Assert
		Assert.That(clone.CacheEquals(original), Is.True);
		Assert.That(ReferenceEquals(original, clone), Is.False);
		Assert.That(ReferenceEquals(original.LiveObject, clone.LiveObject), Is.False);
		Assert.That(clone.LiveObject, Is.TypeOf<OldSensorTelemetry>());

		var clonedSensor = (OldSensorTelemetry)clone.LiveObject;
		var originalSensor = (OldSensorTelemetry)original.LiveObject;
		Assert.That(clonedSensor.ActiveNode, Is.EqualTo(originalSensor.ActiveNode));
		Assert.That(clonedSensor.PrimaryReadingValue, Is.EqualTo(originalSensor.PrimaryReadingValue));
		Assert.That(clonedSensor.SecondaryReadingValue, Is.EqualTo(originalSensor.SecondaryReadingValue));
	}

	[Test]
	public void Clone_ModifyClone_DoesNotAffectOriginal() {
		// Arrange
		var original = new ClassLiveObjectCacheItem {
			EventId = 789,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 30,
				PrimaryValue = 6,
				SecondaryValue = 4
			}
		};

		// Act
		var clone = original.Clone();
		clone.EventId = 999;
		clone.LiveObject.PrimaryValue = 7;
		((OldSensorTelemetry)clone.LiveObject).ActiveNode = "Player2";

		// Assert
		Assert.That(original.EventId, Is.EqualTo(789));
		Assert.That(original.LiveObject.PrimaryValue, Is.EqualTo(6));
		Assert.That(((OldSensorTelemetry)original.LiveObject).ActiveNode, Is.EqualTo("Player1"));
	}

	[Test]
	public void Clone_NullLiveObject_ClonesCorrectly() {
		// Arrange
		var original = new ClassLiveObjectCacheItem {
			EventId = 100,
			LiveObject = null
		};

		// Act
		var clone = original.Clone();

		// Assert
		Assert.That(clone.CacheEquals(original), Is.True);
		Assert.That(clone.EventId, Is.EqualTo(100));
		Assert.That(clone.LiveObject, Is.Null);
	}

	[Test]
	public void Clone_ComplexCollections_CreatesDeepCopy() {
		// Arrange
		var original = new ClassLiveObjectCacheItem {
			EventId = 200,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample> {
					new() { PrimaryValue = 6, SecondaryValue = 4, PeriodId = ReadingPhase.Phase8 },
					new() { PrimaryValue = 3, SecondaryValue = 6, PeriodId = ReadingPhase.Phase9 }
				},
				PeriodScore = new List<PeriodReading> {
					new() {
						PeriodId = ReadingPhase.Phase8,
						PeriodNumber = "1",
						PeriodDisplayName = "Set 1"
					}
				}
			}
		};

		// Act
		var clone = original.Clone();
		clone.LiveObject.ReadingSamples[0].PrimaryValue = 7;
		clone.LiveObject.PeriodScore[0].PeriodNumber = "2";

		// Assert
		Assert.That(original.LiveObject.ReadingSamples[0].PrimaryValue, Is.EqualTo(6));
		Assert.That(original.LiveObject.PeriodScore[0].PeriodNumber, Is.EqualTo("1"));
	}

	[Test]
	public void CacheOperations_AddAndRetrieve_BaseLiveData() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();
		var item = new ClassLiveObjectCacheItem {
			EventId = 1001,
			LiveObject = new OldBaseTelemetry {
				PrimaryValue = 2,
				SecondaryValue = 1,
				PeriodId = ReadingPhase.Phase6
			}
		};

		// Act
		cache.AddOrUpdate(item);
		var retrieved = cache.TryGet(1001, out var result);

		// Assert
		Assert.That(retrieved, Is.True);
		Assert.That(result.CacheEquals(item), Is.True);
	}

	[Test]
	public void CacheOperations_AddAndRetrieve_SensorTelemetry() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();
		var item = new ClassLiveObjectCacheItem {
			EventId = 2001,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Federer",
				PrimaryReadingValue = 40,
				SecondaryReadingValue = 15,
				PrimaryValue = 6,
				SecondaryValue = 3
			}
		};

		// Act
		cache.AddOrUpdate(item);
		var retrieved = cache.TryGet(2001, out var result);

		// Assert
		Assert.That(retrieved, Is.True);
		Assert.That(result.CacheEquals(item), Is.True);
		Assert.That(result.LiveObject, Is.TypeOf<OldSensorTelemetry>());
	}

	[Test]
	public void CacheOperations_Update_ModifiesExistingItem() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 3001,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player1",
				PrimaryValue = 6,
				SecondaryValue = 4
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 3001,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Player2",
				PrimaryValue = 6,
				SecondaryValue = 5
			}
		};

		// Act
		cache.AddOrUpdate(item1);
		cache.AddOrUpdate(item2);
		var retrieved = cache.TryGet(3001, out var result);

		// Assert
		Assert.That(retrieved, Is.True);
		Assert.That(result.CacheEquals(item2), Is.True);
		Assert.That(((OldSensorTelemetry)result.LiveObject).ActiveNode, Is.EqualTo("Player2"));
		Assert.That(result.LiveObject.SecondaryValue, Is.EqualTo(5));
	}

	[Test]
	public void CacheOperations_Remove_DeletesItem() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();
		var item = new ClassLiveObjectCacheItem {
			EventId = 4001,
			LiveObject = new OldBaseTelemetry { PrimaryValue = 1, SecondaryValue = 0 }
		};

		// Act
		cache.AddOrUpdate(item);
		cache.Remove(4001);
		var retrieved = cache.TryGet(4001, out var result);

		// Assert
		Assert.That(retrieved, Is.False);
		Assert.That(result, Is.Null);
	}

	[Test]
	public void CacheOperations_TryGetNonExistent_ReturnsFalse() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();

		// Act
		var retrieved = cache.TryGet(9999, out var result);

		// Assert
		Assert.That(retrieved, Is.False);
		Assert.That(result, Is.Null);
	}

	[Test]
	public void CacheOperations_MultipleItemsWithPolymorphicTypes() {
		// Arrange
		var cache = new ClassLiveObjectCacheItemCache();
		var baseItem = new ClassLiveObjectCacheItem {
			EventId = 5001,
			LiveObject = new OldBaseTelemetry { PrimaryValue = 2, SecondaryValue = 1 }
		};
		var tennisItem = new ClassLiveObjectCacheItem {
			EventId = 5002,
			LiveObject = new OldSensorTelemetry {
				ActiveNode = "Nadal",
				PrimaryValue = 6,
				SecondaryValue = 4
			}
		};

		// Act
		cache.AddOrUpdate(baseItem);
		cache.AddOrUpdate(tennisItem);

		var retrieved1 = cache.TryGet(5001, out var result1);
		var retrieved2 = cache.TryGet(5002, out var result2);

		// Assert
		Assert.That(retrieved1, Is.True);
		Assert.That(retrieved2, Is.True);
		Assert.That(result1.LiveObject, Is.TypeOf<OldBaseTelemetry>());
		Assert.That(result2.LiveObject, Is.TypeOf<OldSensorTelemetry>());
	}

	[Test]
	public void EdgeCase_EmptyCollections_HandledCorrectly() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 6001,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample>(),
				PeriodScore = new List<PeriodReading>()
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 6001,
			LiveObject = new OldBaseTelemetry {
				ReadingSamples = new List<ReadingSample>(),
				PeriodScore = new List<PeriodReading>()
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);

		var clone = item1.Clone();
		Assert.That(clone.CacheEquals(item1), Is.True);
	}

	[Test]
	public void EdgeCase_NullCollections_HandledCorrectly() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 7001,
			LiveObject = new OldBaseTelemetry {
				PeriodScore = null
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 7001,
			LiveObject = new OldBaseTelemetry {
				PeriodScore = null
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.True);

		var clone = item1.Clone();
		Assert.That(clone.CacheEquals(item1), Is.True);
		Assert.That(clone.LiveObject.PeriodScore, Is.Null);
	}

	[Test]
	public void EdgeCase_NullableEnumValues() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 8001,
			LiveObject = new OldBaseTelemetry {
				PeriodId = null
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 8001,
			LiveObject = new OldBaseTelemetry {
				PeriodId = ReadingPhase.Phase8
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}

	[Test]
	public void EdgeCase_SensorTelemetryNullableScores() {
		// Arrange
		var item1 = new ClassLiveObjectCacheItem {
			EventId = 9001,
			LiveObject = new OldSensorTelemetry {
				PrimaryReadingValue = null,
				SecondaryReadingValue = null
			}
		};
		var item2 = new ClassLiveObjectCacheItem {
			EventId = 9001,
			LiveObject = new OldSensorTelemetry {
				PrimaryReadingValue = 0,
				SecondaryReadingValue = 0
			}
		};

		// Act & Assert
		Assert.That(item1.CacheEquals(item2), Is.False);
	}
}
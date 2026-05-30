namespace Prague.Generated.Tests.Kafka;

using NUnit.Framework;
using Prague.Generated.Tests.TestEntities;

[TestFixture]
public class DataCacheFromTimestampTests {
	[Test]
	public void TestEntityWithLongTimestamp_ShouldCompile() {
		var entity = new TestEntityWithLongTimestamp {
			Id = 1,
			Timestamp = 1234567890L,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890L));
	}

	[Test]
	public void TestEntityWithNullableLongTimestamp_ShouldCompile() {
		var entity = new TestEntityWithNullableLongTimestamp {
			Id = 1,
			Timestamp = 1234567890L,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.Timestamp, Is.EqualTo(1234567890L));

		entity.Timestamp = null;
		Assert.That(entity.Timestamp, Is.Null);
	}

	[Test]
	public void TestEntityWithDateTimeTimestamp_ShouldCompile() {
		var now = DateTime.UtcNow;
		var entity = new TestEntityWithDateTimeTimestamp {
			Id = 1,
			CreatedAt = now,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.CreatedAt, Is.EqualTo(now));
	}

	[Test]
	public void TestEntityWithNullableDateTimeTimestamp_ShouldCompile() {
		var now = DateTime.UtcNow;
		var entity = new TestEntityWithNullableDateTimeTimestamp {
			Id = 1,
			CreatedAt = now,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.CreatedAt, Is.EqualTo(now));

		entity.CreatedAt = null;
		Assert.That(entity.CreatedAt, Is.Null);
	}

	[Test]
	public void TestEntityWithDateTimeOffsetTimestamp_ShouldCompile() {
		var now = DateTimeOffset.UtcNow;
		var entity = new TestEntityWithDateTimeOffsetTimestamp {
			Id = 1,
			CreatedAt = now,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.CreatedAt, Is.EqualTo(now));
	}

	[Test]
	public void TestEntityWithNullableDateTimeOffsetTimestamp_ShouldCompile() {
		var now = DateTimeOffset.UtcNow;
		var entity = new TestEntityWithNullableDateTimeOffsetTimestamp {
			Id = 1,
			CreatedAt = now,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.CreatedAt, Is.EqualTo(now));

		entity.CreatedAt = null;
		Assert.That(entity.CreatedAt, Is.Null);
	}

	[Test]
	public void TestEntityWithMultipleTimestamps_ShouldCompile() {
		var now = DateTime.UtcNow;
		var entity = new TestEntityWithMultipleTimestamps {
			Id = 1,
			CreatedTimestamp = 1234567890L,
			UpdatedAt = now,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.CreatedTimestamp, Is.EqualTo(1234567890L));
		Assert.That(entity.UpdatedAt, Is.EqualTo(now));
	}

	[Test]
	public void TestEntityWithoutTimestamp_ShouldCompile() {
		var entity = new TestEntityWithoutTimestamp {
			Id = 1,
			Name = "Test"
		};

		Assert.That(entity.Id, Is.EqualTo(1));
		Assert.That(entity.Name, Is.EqualTo("Test"));
	}

	[Test]
	public void Cache_ShouldBeGenerated_ForAllEntities() {
		// Verify cache classes are generated
		var cache1 = new TestEntityWithLongTimestampCache();
		Assert.That(cache1, Is.Not.Null);
		Assert.That(cache1.Cache, Is.Not.Null);

		var cache2 = new TestEntityWithDateTimeTimestampCache();
		Assert.That(cache2, Is.Not.Null);
		Assert.That(cache2.Cache, Is.Not.Null);

		var cache3 = new TestEntityWithMultipleTimestampsCache();
		Assert.That(cache3, Is.Not.Null);
		Assert.That(cache3.Cache, Is.Not.Null);

		var cache4 = new TestEntityWithoutTimestampCache();
		Assert.That(cache4, Is.Not.Null);
		Assert.That(cache4.Cache, Is.Not.Null);
	}
}
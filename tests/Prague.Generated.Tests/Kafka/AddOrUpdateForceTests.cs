namespace Prague.Generated.Tests.Kafka;

using NUnit.Framework;
using Prague.Generated.Tests.TestEntities;
using Prague.Kafka;

/// <summary>
///   The generator emits the forced <c>AddOrUpdate</c> overload on the cache and the <c>force</c> flag on the
///   Kafka extension. Broker-free: no producer is configured, so on the forced path the produce step is the
///   throw — and it comes after the local write, which is the ordering the extension promises.
/// </summary>
[TestFixture]
public class AddOrUpdateForceTests {
	private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	[Test]
	public void GeneratedCache_ForcedAddOrUpdate_ReplacesAnEqualDocument() {
		var cache = new TestEntityWithoutTimestampCache();
		var first = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		var second = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);

		Assert.That(cache.AddOrUpdate(second, Now(), force: false, out _), Is.False, "precondition: the documents are cache-equal");
		Assert.That(cache.AddOrUpdate(second, Now(), force: true, out var old), Is.True);
		Assert.Multiple(() => {
			Assert.That(old, Is.SameAs(first));
			Assert.That(cache.TryGet(1, out var resident), Is.True);
			Assert.That(resident, Is.SameAs(second));
		});
	}

	[Test]
	public void AddOrUpdateAndProduce_Force_WritesLocallyThenProduces() {
		var cache = new TestEntityWithoutTimestampCache();
		var first = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		var second = new TestEntityWithoutTimestamp { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);

		// Unforced and equal: nothing changes and nothing is produced, so the missing producer is never reached.
		Assert.DoesNotThrow(() => cache.AddOrUpdateAndProduce(second));
		Assert.That(cache.TryGet(1, out var kept), Is.True);
		Assert.That(kept, Is.SameAs(first));

		// Forced: the local write lands first, then the produce step reaches the unconfigured producer.
		Assert.Throws<InvalidOperationException>(() => cache.AddOrUpdateAndProduce(second, force: true));
		Assert.That(cache.TryGet(1, out var replaced), Is.True);
		Assert.That(replaced, Is.SameAs(second));
	}
}

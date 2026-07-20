namespace Prague.Core.Tests.Cache;

using Prague.Core.Collections;
using NUnit.Framework;

/// <summary>
/// Pins the hashing/equality contract of <see cref="ConcurrentCacheStore{TKey,TValue}"/>:
/// hash and equals are dispatched exclusively through <see cref="DefaultKeyComparer{T}"/> on
/// every read and write path, so keys equal by <c>Equals</c> always find each other, chains of
/// hash-colliding keys stay correct without any rehash escape hatch, and resizes preserve every
/// entry by reusing stored hashcodes.
/// </summary>
[TestFixture]
public class ConcurrentCacheStoreHashingTests {
	private static void Upsert<TKey, TValue>(ConcurrentCacheStore<TKey, TValue> cache, TKey key, TValue value)
		where TKey : notnull
		=> cache.AddOrUpdate(key, value, static (_, _, _) => true);

	[Test]
	[TestCase(12345)]
	[TestCase(67890)]
	[TestCase(24680)]
	public void StringKeys_RandomOps_MatchDictionaryOracle(int seed) {
		var cache = new ConcurrentCacheStore<string, int>();
		var oracle = new Dictionary<string, int>();
		var random = new Random(seed);

		for (var op = 0; op < 20_000; ++op) {
			var key = "key-" + random.Next(0, 500);
			switch (random.Next(0, 10)) {
				case < 5: {
					var value = random.Next();
					Upsert(cache, key, value);
					oracle[key] = value;
					break;
				}
				case < 7: {
					var removedFromCache = cache.TryRemove(key, out var cacheValue);
					var removedFromOracle = oracle.Remove(key, out var oracleValue);
					Assert.That(removedFromCache, Is.EqualTo(removedFromOracle), $"op {op}: TryRemove({key})");
					if (removedFromOracle)
						Assert.That(cacheValue, Is.EqualTo(oracleValue), $"op {op}: removed value for {key}");
					break;
				}
				case < 9: {
					var foundInCache = cache.TryGetValue(key, out var cacheValue);
					var foundInOracle = oracle.TryGetValue(key, out var oracleValue);
					Assert.That(foundInCache, Is.EqualTo(foundInOracle), $"op {op}: TryGetValue({key})");
					if (foundInOracle)
						Assert.That(cacheValue, Is.EqualTo(oracleValue), $"op {op}: value for {key}");
					break;
				}
				default:
					Assert.That(cache.ContainsKey(key), Is.EqualTo(oracle.ContainsKey(key)), $"op {op}: ContainsKey({key})");
					break;
			}
		}

		Assert.That(cache.Count, Is.EqualTo(oracle.Count));
		foreach (var kvp in cache.GetKeyValues()) {
			Assert.That(oracle.TryGetValue(kvp.Key, out var oracleValue), Is.True, $"stray key {kvp.Key}");
			Assert.That(kvp.Value, Is.EqualTo(oracleValue), $"final value for {kvp.Key}");
		}
	}

	[Test]
	[TestCase(13579)]
	[TestCase(97531)]
	public void IntKeys_RandomOps_MatchDictionaryOracle(int seed) {
		var cache = new ConcurrentCacheStore<int, string>();
		var oracle = new Dictionary<int, string>();
		var random = new Random(seed);

		for (var op = 0; op < 20_000; ++op) {
			var key = random.Next(0, 500);
			switch (random.Next(0, 10)) {
				case < 5: {
					var value = "v" + random.Next();
					Upsert(cache, key, value);
					oracle[key] = value;
					break;
				}
				case < 7: {
					var removedFromCache = cache.TryRemove(key, out var cacheValue);
					var removedFromOracle = oracle.Remove(key, out var oracleValue);
					Assert.That(removedFromCache, Is.EqualTo(removedFromOracle), $"op {op}: TryRemove({key})");
					if (removedFromOracle)
						Assert.That(cacheValue, Is.EqualTo(oracleValue), $"op {op}: removed value for {key}");
					break;
				}
				default: {
					var foundInCache = cache.TryGetValue(key, out var cacheValue);
					var foundInOracle = oracle.TryGetValue(key, out var oracleValue);
					Assert.That(foundInCache, Is.EqualTo(foundInOracle), $"op {op}: TryGetValue({key})");
					if (foundInOracle)
						Assert.That(cacheValue, Is.EqualTo(oracleValue), $"op {op}: value for {key}");
					break;
				}
			}
		}

		Assert.That(cache.Count, Is.EqualTo(oracle.Count));
	}

	[Test]
	public void StringKeys_GrowthAcrossManyResizes_AllEntriesRetrievable() {
		// Default capacity is 31; 10k entries force many doublings. GrowTable must relocate
		// every node into the new table by its stored hashcode without losing or duplicating any.
		var cache = new ConcurrentCacheStore<string, int>();
		const int count = 10_000;
		for (var i = 0; i < count; ++i)
			Upsert(cache, "grow-" + i, i);

		Assert.That(cache.Count, Is.EqualTo(count));
		for (var i = 0; i < count; ++i) {
			Assert.That(cache.TryGetValue("grow-" + i, out var value), Is.True, $"missing grow-{i}");
			Assert.That(value, Is.EqualTo(i));
		}

		for (var i = 0; i < count; i += 2)
			Assert.That(cache.TryRemove("grow-" + i, out _), Is.True, $"failed to remove grow-{i}");

		Assert.That(cache.Count, Is.EqualTo(count / 2));
		for (var i = 0; i < count; ++i)
			Assert.That(cache.ContainsKey("grow-" + i), Is.EqualTo(i % 2 == 1), $"post-remove state of grow-{i}");
	}

	[Test]
	public void ConstantHashKeys_ChainBeyondHundredEntries_StaysCorrect() {
		// Every key hashes identically, so all entries share one bucket chain. The BCL-inherited
		// flood-rehash escape hatch (chain > 100 → swap comparer + rehash) is gone; this pins
		// that lookups, updates, and removes stay correct on a 150-deep chain without it.
		var cache = new ConcurrentCacheStore<CollidingKey, int>();
		const int count = 150;
		for (var i = 0; i < count; ++i)
			Upsert(cache, new CollidingKey(i), i);

		Assert.That(cache.Count, Is.EqualTo(count));
		for (var i = 0; i < count; ++i) {
			Assert.That(cache.TryGetValue(new CollidingKey(i), out var value), Is.True, $"missing id {i}");
			Assert.That(value, Is.EqualTo(i));
		}

		for (var i = 0; i < count; ++i)
			Upsert(cache, new CollidingKey(i), i * 10);
		Assert.That(cache.Count, Is.EqualTo(count), "updates must not duplicate chain entries");

		for (var i = 0; i < count; i += 2) {
			Assert.That(cache.TryRemove(new CollidingKey(i), out var removed), Is.True, $"failed to remove id {i}");
			Assert.That(removed, Is.EqualTo(i * 10));
		}

		Assert.That(cache.Count, Is.EqualTo(count / 2));
		for (var i = 1; i < count; i += 2) {
			Assert.That(cache.TryGetValue(new CollidingKey(i), out var value), Is.True, $"survivor id {i} missing");
			Assert.That(value, Is.EqualTo(i * 10));
		}
	}

	[Test]
	public void StringKeys_DistinctEqualInstances_FindEachOtherOnEveryReadPath() {
		// Distinct (non-interned) string instances that are equal by value must resolve to the
		// same entry on every read overload — the exact property the removed comparer-mismatch
		// code path could violate.
		var cache = new ConcurrentCacheStore<string, int>();
		var written = string.Concat("con", "sistency");
		Upsert(cache, written, 7);

		var probe = new string(['c', 'o', 'n', 's', 'i', 's', 't', 'e', 'n', 'c', 'y']);
		Assert.That(ReferenceEquals(written, probe), Is.False, "probe must be a distinct instance");

		Assert.That(cache.ContainsKey(probe), Is.True);
		Assert.That(cache.TryGetValue(probe, out var single), Is.True);
		Assert.That(single, Is.EqualTo(7));

		Span<int> values = stackalloc int[1];
		Span<bool> found = stackalloc bool[1];
		var spanCount = cache.TryGetValues([probe], values, found);
		Assert.That(spanCount, Is.EqualTo(1));
		Assert.That(found[0], Is.True);
		Assert.That(values[0], Is.EqualTo(7));

		var list = cache.TryGetValues(new List<string> { probe });
		Assert.That(list, Is.EqualTo(new[] { 7 }));

		Span<int> collectionValues = stackalloc int[1];
		var collectionCount = cache.TryGetValues(new List<string> { probe }, collectionValues);
		Assert.That(collectionCount, Is.EqualTo(1));
		Assert.That(collectionValues[0], Is.EqualTo(7));

		Assert.That(cache.TryRemove(probe, out var removed), Is.True);
		Assert.That(removed, Is.EqualTo(7));
		Assert.That(cache.Count, Is.EqualTo(0));
	}

	[Test]
	public void CollectionLookup_MixedHitsAndMisses_CompactsValuesAndCountsOnce() {
		// TryGetValues(ICollection, Span) must write each hit exactly once into the next free
		// slot and count it exactly once, regardless of whether the hit is at the bucket head
		// or deeper in the chain.
		var cache = new ConcurrentCacheStore<CollidingKey, int>();
		for (var i = 0; i < 10; ++i)
			Upsert(cache, new CollidingKey(i), i + 100);

		var keys = new List<CollidingKey> {
			new(0), new(42_000), new(5), new(9), new(77_000), new(1),
		};
		Span<int> values = stackalloc int[keys.Count];
		var count = cache.TryGetValues(keys, values);

		Assert.That(count, Is.EqualTo(4));
		Assert.That(values[..count].ToArray(), Is.EqualTo(new[] { 100, 105, 109, 101 }));
	}

	private sealed class CollidingKey : IEquatable<CollidingKey> {
		private readonly int _id;

		public CollidingKey(int id) => _id = id;

		public bool Equals(CollidingKey? other) => other is not null && _id == other._id;

		public override bool Equals(object? obj) => obj is CollidingKey other && Equals(other);

		public override int GetHashCode() => 42;
	}
}

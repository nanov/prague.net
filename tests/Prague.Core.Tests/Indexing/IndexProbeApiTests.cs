namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using Prague.Core.Collections;
using Prague.Core.Tests.Prepared;
using static Prague.Core.Tests.Prepared.PreparedQueryDifferentialTests;

// The internal probe / cardinality APIs the frozen pipeline reads (stage 3, step 1): they are one
// lookup or one delegate call each and must agree with the public surface they shadow.
[TestFixture]
public class IndexProbeApiTests {
	private const int N = 1000;

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTags = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTags = _cache.CacheCollectionKeyValueListIndex<int>(static (_, v) => [v.Group, v.Group + 100]);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 }, 1_000_000L + i);
	}

	[Test]
	public void ListIndex_HasKeySelector_AndTryGetBucket() {
		Assert.Multiple(() => {
			Assert.That(_byGroup.HasKeySelector, Is.True, "scalar");
			Assert.That(_byTags.HasKeySelector, Is.False, "collection-backed");
			Assert.That(_byGroup.TryGetBucket(3, out var bucket), Is.True);
			Assert.That(bucket!.Count, Is.EqualTo(_byGroup.TryGetCount(3)));
			Assert.That(bucket.Contains(3), Is.True);
			Assert.That(bucket.Contains(4), Is.False);
			Assert.That(_byGroup.TryGetBucket(99, out _), Is.False, "absent key");
			Assert.That(_byTags.TryGetBucket(103, out var tagBucket), Is.True);
			Assert.That(tagBucket!.Contains(3), Is.True);
			Assert.That(_byGroup.KeySelector(0, new PqItem { Group = 5 }), Is.EqualTo(5));
		});
	}

	[Test]
	public void RangeIndex_KeyOf_AndEstimateCount() {
		Assert.That(_codeRange.KeyOf(42, new PqItem { Id = 42, Code = 1042 }), Is.EqualTo(1042));
		Assert.Multiple(() => {
			Assert.That(_codeRange.EstimateCount(default, default), Is.EqualTo(N), "unbounded = Length");
			Assert.That(_codeRange.EstimateCount(new(RangeValueType.ThanOrEqual, 1042), new(RangeValueType.ThanOrEqual, 1042)), Is.EqualTo(1), "[1042, 1042]");
			Assert.That(_codeRange.EstimateCount(new(RangeValueType.Than, 1042), new(RangeValueType.ThanOrEqual, 1042)), Is.EqualTo(0), "(1042, 1042]");
			var wide = _codeRange.EstimateCount(new(RangeValueType.ThanOrEqual, 1100), new(RangeValueType.Than, 1400));
			Assert.That(wide, Is.InRange(150, 600), "[1100, 1400) is 300 rows");
			Assert.That(_codeRange.EstimateCount(new(RangeValueType.ThanOrEqual, 1500), default), Is.InRange(250, 1000), "[1500, +inf) is 500 rows");
			Assert.That(_codeRange.EstimateCount(default, new(RangeValueType.Than, 1500)), Is.InRange(250, 1000), "(-inf, 1500) is 500 rows");
		});
	}

	[Test]
	public void KeySetIndex_Matches_AndCount() {
		Assert.Multiple(() => {
			Assert.That(_flagged.Count, Is.EqualTo((int)_flagged.ApproximateCount));
			Assert.That(_flagged.Count, Is.EqualTo(334));
			Assert.That(_flagged.Matches(3, new PqItem { Id = 3, Flag = true }), Is.True);
			Assert.That(_flagged.Matches(4, new PqItem { Id = 4, Flag = false }), Is.False);
		});
		var sink = new ListSink { Keys = [] };
		_flagged.CopyKeysTo(ref sink);
		Assert.That(sink.Keys, Has.Count.EqualTo(334));
		var eager = _cache.Query().UseIndex(_flagged).Execute();
		var eagerIds = new int[eager.Count];
		for (var i = 0; i < eager.Count; i++) eagerIds[i] = eager[i].Id;
		eager.Dispose();
		Assert.That(sink.Keys, Is.EqualTo(eagerIds).AsCollection, "copy order is the eager seed order");
	}

	private struct ListSink : IKeySink<int> {
		public List<int> Keys;
		public void Add(int key) => Keys.Add(key);
	}

	[Test]
	public void LastUpdatedIndex_EstimateCount() {
		Assert.Multiple(() => {
			Assert.That(_lastUpdated.EstimateCount(1_000_000L + N), Is.EqualTo(0), "after the newest");
			Assert.That(_lastUpdated.EstimateCount(1_000_000L + N - 2), Is.EqualTo(1), "one newer");
			Assert.That(_lastUpdated.EstimateCount(1_000_000L + 499), Is.InRange(250, 1000), "half");
			Assert.That(_lastUpdated.EstimateCount(1_000_000L + 99, 1_000_000L + 399), Is.InRange(150, 600), "(99, 399] = 300");
			Assert.That(_lastUpdated.EstimateCount(1_000_000L + 10, 1_000_000L + 10), Is.EqualTo(0), "empty window");
		});
	}

	// A writer moving rows across buckets (buckets emptied and disposed, new ones created) while
	// readers TryGetBucket + Contains: no exception, no torn read.
	[Test]
	[NonParallelizable]
	public void ListIndex_TryGetBucket_UnderConcurrentWriter_NeverThrows() {
		var cache = new InMemoryDataCache<int, PqItem>();
		var byGroup = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		for (var i = 0; i < 512; i++)
			cache.AddOrUpdate(i, new PqItem { Id = i, Code = i, Group = i % 4 });
		var stop = false;
		Exception? failure = null;
		var writer = new Thread(() => {
			try {
				var round = 1;
				while (!Volatile.Read(ref stop)) {
					// every round moves every row to a fresh group: old buckets drain and dispose
					for (var i = 0; i < 512; i++)
						cache.AddOrUpdate(i, new PqItem { Id = i, Code = i, Group = round * 4 + i % 4 });
					if (round % 5 == 0)
						for (var i = 0; i < 512; i += 7) cache.Remove(i);
					round++;
				}
			} catch (Exception ex) {
				failure = ex;
			}
		});
		var readers = new Thread[4];
		for (var r = 0; r < readers.Length; r++)
			readers[r] = new Thread(() => {
				try {
					var probes = 0;
					while (!Volatile.Read(ref stop)) {
						for (var g = 0; g < 64; g++) {
							if (!byGroup.TryGetBucket(g, out var bucket))
								continue;
							for (var k = 0; k < 512; k += 5) {
								if (bucket.Contains(k)) probes++;
							}
						}
					}
				} catch (Exception ex) {
					failure = ex;
				}
			});
		writer.Start();
		foreach (var t in readers) t.Start();
		Thread.Sleep(1000);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers) t.Join();
		Assert.That(failure, Is.Null);
	}
}

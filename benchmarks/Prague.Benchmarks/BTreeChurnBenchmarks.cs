namespace Prague.Benchmarks;

	using Prague.Core.Collections;
	using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class BTreeChurnBenchmarks {
	// Batch-stamped workload sizing: runs of BatchSize equal keys span ~16 leaves,
	// and the prefill is large enough that a per-remove run scan costs ~BatchSize
	// comparisons — the incident shape (feed producers stamp whole batches with one
	// timestamp, every cache update removes a pair from inside such a run).
	// Note: iterations churn the tree (each re-stamps ~1% of entries into a fresh
	// Items-sized run), so warmup drains the pristine prefill and measured
	// iterations run on the self-generated steady-state distribution — which is the
	// production shape anyway (a mix of run sizes); tree size stays exactly
	// BatchItems throughout.
	private const int BatchItems = 100_000;
	private const int BatchSize = 1024;

	private PooledBTree<long, long> _uniqueTree = null!;
	private PooledBTree<long, long> _sharedTree = null!;
	private PooledBTree<long, long> _monoTree = null!;
	private PooledBTree<long, long> _batchTree = null!;
	private long[] _batchCurrentKey = null!;
	private long _round;

	[Params(1000)] public int Items { get; set; }

	private struct CountingAggregator : PooledBTree<long, long>.IResultAggregator {
		public long Sum;

		public void Add(long index, long value) => Sum += value;

		public void Dispose() { }
	}

	[GlobalSetup]
	public void Setup() {
		_uniqueTree = new PooledBTree<long, long>();
		_sharedTree = new PooledBTree<long, long>();
		_monoTree = new PooledBTree<long, long>();
		for (long i = 0; i < Items; i++) {
			_uniqueTree.Add(i * 1000, i); // unique keys — the fast-path-only workload
			_sharedTree.Add(1000, i); // one shared key — cross-leaf run workload
			_monoTree.Add(1_000_000_000 + i, i); // strictly ascending — append pattern
		}

		_batchTree = new PooledBTree<long, long>();
		_batchCurrentKey = new long[BatchItems];
		for (long i = 0; i < BatchItems; i++) {
			var key = i / BatchSize; // quantized: runs of BatchSize equal keys
			_batchTree.Add(key, i);
			_batchCurrentKey[i] = key;
		}

		_round = 1;
	}

	[GlobalCleanup]
	public void Cleanup() {
		_uniqueTree.Dispose();
		_sharedTree.Dispose();
		_monoTree.Dispose();
		_batchTree.Dispose();
	}

	// The dominant production write pattern: strictly-ascending timestamp keys append
	// via the O(1) fast path; removes drain the oldest (leftmost) leaves.
	[Benchmark]
	public void AppendChurn_MonotonicKeys() {
		var round = _round++;
		for (long i = 0; i < Items; i++)
			_monoTree.Add(1_000_000_000 + round * Items + i, i);
		for (long i = 0; i < Items; i++)
			_monoTree.Remove(1_000_000_000 + (round - 1) * Items + i, i);
	}

	// No-regression proof: unique keys never enter any slow path.
	[Benchmark]
	public void UpdateChurn_UniqueKeys() {
		var round = _round++;
		for (long i = 0; i < Items; i++) {
			_uniqueTree.Add(i * 1000 + round, i);
			_uniqueTree.Remove(i * 1000 + round - 1, i);
		}
	}

	// The incident workload — broken (silently leaking) before the fix, so this row
	// has no meaningful baseline; it documents the cost of correct behavior.
	[Benchmark]
	public void UpdateChurn_SharedKey() {
		var round = _round++;
		for (long i = 0; i < Items; i++) {
			_sharedTree.Add(1000 + round, i);
			_sharedTree.Remove(1000 + round - 1, i);
		}
	}

	// The cache-load incident shape: keys quantized into batches of BatchSize,
	// each update removes a pair from inside a multi-leaf run of equal keys and adds
	// it under the current round's key. O(run length) removes turn this quadratic;
	// composite (key, value) ordering keeps it O(log n) per op.
	[Benchmark]
	public void UpdateChurn_BatchStampedKeys() {
		var round = _round++;
		var newKey = 1_000_000_000 + round;
		for (long op = 0; op < Items; op++) {
			// Deterministic scatter over the prefilled ids; collisions inside one
			// invocation are guarded by the Add result.
			var id = (round * 7919 + op * 104_729) % BatchItems;
			if (_batchTree.Add(newKey, id)) {
				_batchTree.Remove(_batchCurrentKey[id], id);
				_batchCurrentKey[id] = newKey;
			}
		}
	}

	// Same workload through the single-lock Update: what CacheRangeIndex.Update
	// actually issues per cache update — one lock round-trip instead of two.
	[Benchmark]
	public void UpdateChurn_BatchStampedKeys_CombinedUpdate() {
		var round = _round++;
		var newKey = 1_000_000_000 + round;
		for (long op = 0; op < Items; op++) {
			var id = (round * 7919 + op * 104_729) % BatchItems;
			_batchTree.Update(_batchCurrentKey[id], newKey, id);
			_batchCurrentKey[id] = newKey;
		}
	}

	[Benchmark]
	public long RangeFrom_ScanAll() {
		var agg = new CountingAggregator();
		_uniqueTree.RangeFrom(0, ref agg);
		return agg.Sum;
	}

	[Benchmark]
	public long RangeFromExclusive_ScanAll() {
		var agg = new CountingAggregator();
		_uniqueTree.RangeFromExclusive(0, ref agg);
		return agg.Sum;
	}

	[Benchmark]
	public int Contains_HitAndMiss() {
		var hits = 0;
		for (long i = 0; i < 128; i++) {
			if (_uniqueTree.Contains(i * 1000, i))
				hits++;
			if (_uniqueTree.Contains(i * 1000 + 1, i))
				hits++;
		}

		return hits;
	}
}

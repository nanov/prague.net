namespace Prague.Benchmarks;

	using Prague.Core.Collections;
	using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class BTreeChurnBenchmarks {
	private PooledBTree<long, long> _uniqueTree = null!;
	private PooledBTree<long, long> _sharedTree = null!;
	private PooledBTree<long, long> _monoTree = null!;
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

		_round = 1;
	}

	[GlobalCleanup]
	public void Cleanup() {
		_uniqueTree.Dispose();
		_sharedTree.Dispose();
		_monoTree.Dispose();
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

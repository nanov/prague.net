namespace Prague.Benchmarks;

	using Prague.Core.Collections;
	using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class PooledSetBenchmarks {
	private PooledSet<long, DefaultKeyComparer<long>> _set = null!;

	[Params(100, 5000)] public int ItemCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		_set = new PooledSet<long, DefaultKeyComparer<long>>();
		for (long i = 0; i < ItemCount; i++)
			_set.Add(i);
	}

	[GlobalCleanup]
	public void Cleanup() => _set.Dispose();

	[Benchmark]
	public void AddRemoveChurn() {
		for (long i = 0; i < 128; i++)
			_set.Add(1_000_000 + i);
		for (long i = 0; i < 128; i++)
			_set.Remove(1_000_000 + i);
	}

	[Benchmark]
	public int Contains_HitAndMiss() {
		var hits = 0;
		for (long i = 0; i < 128; i++) {
			if (_set.Contains(i))
				hits++;
			if (_set.Contains(-1 - i))
				hits++;
		}

		return hits;
	}

	[Benchmark]
	public long EnumerateStruct() {
		long sum = 0;
		foreach (var v in _set)
			sum += v;
		return sum;
	}

	[Benchmark]
	public long EnumerateBoxed() {
		long sum = 0;
		IEnumerable<long> view = _set;
		foreach (var v in view)
			sum += v;
		return sum;
	}

	// The per-message Many-index bucket lifecycle: a key appears (set created, one
	// item), then leaves (bucket empties, set disposed). This is THE pooling
	// round-trip the fix must not regress.
	[Benchmark]
	public void CreateAddDispose_SingletonBucket() {
		for (var i = 0; i < 16; i++) {
			var set = new PooledSet<long, DefaultKeyComparer<long>>();
			set.Add(42);
			set.Remove(42);
			set.Dispose();
		}
	}
}

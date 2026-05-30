namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Core;

/// <summary>
///   Benchmarks specifically focused on measuring memory allocation patterns
///   to validate the thread-local pooling optimizations
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class MemoryAllocationBenchmarks {
	private InMemoryDataCache<int, CacheEquatable<int>>? _cache;
	private CacheRangeIndex<int, CacheEquatable<int>, int>? _rangeIndex;

	[GlobalSetup]
	public void Setup() {
		_cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		_rangeIndex = _cache.CacheRangeIndex((key, val) => val.Value);

		// Pre-populate
		for (var i = 0; i < 100_000; i++) _cache.AddOrUpdate(i, i);
	}

	[Benchmark(Description = "Single Add - measures Node allocation only")]
	public void SingleAdd() {
		_cache!.AddOrUpdate(50_000, 50_000);
	}

	[Benchmark(Description = "Single Range Query - should show zero allocations with pooling")]
	public int SingleRangeQuery() {
		var results = _rangeIndex!.GetValuesBetween(10_000, 20_000);
		return results.Count;
	}

	[Benchmark(Description = "100 Range Queries - validates array pooling effectiveness")]
	public int HundredRangeQueries() {
		var total = 0;
		for (var i = 0; i < 100; i++) {
			var start = i * 1000;
			var results = _rangeIndex!.GetValuesBetween(start, start + 100);
			total += results.Count;
		}

		return total;
	}

	[Benchmark(Description = "Range Query GTE - tests specific operation allocation")]
	public int RangeQueryGte() {
		var results = _rangeIndex!.GetValuesGte(50_000);
		return results.Count;
	}

	[Benchmark(Description = "Range Query LTE - tests specific operation allocation")]
	public int RangeQueryLte() {
		var results = _rangeIndex!.GetValuesLte(50_000);
		return results.Count;
	}

	[Benchmark(Description = "Range Query GT - tests strict comparison allocation")]
	public int RangeQueryGt() {
		var results = _rangeIndex!.GetValuesGt(50_000);
		return results.Count;
	}

	[Benchmark(Description = "Range Query LT - tests strict comparison allocation")]
	public int RangeQueryLt() {
		var results = _rangeIndex!.GetValuesLt(50_000);
		return results.Count;
	}

	[Benchmark(Description = "Add/Remove cycle - measures temporary allocation overhead")]
	public void AddRemoveCycle() {
		for (var i = 0; i < 100; i++) {
			_cache!.AddOrUpdate(200_000 + i, i);
			_cache.Remove(200_000 + i);
		}
	}

	[Benchmark(Description = "Contains check - should have minimal allocation")]
	public bool ContainsCheck() {
		// This uses FindNode internally, testing array pooling
		var result = false;
		for (var i = 0; i < 100; i++) _cache!.TryGet(50_000 + i, out _);
		return result;
	}
}

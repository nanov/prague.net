namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Core;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConcurrentSortedListBenchmarks {
	private InMemoryDataCache<int, CacheEquatable<int>>? _cache;
	private CacheRangeIndex<int, CacheEquatable<int>, int>? _rangeIndex;

	[Params(1000, 10_000, 100_000)] public int ItemCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		_cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		_rangeIndex = _cache.CacheRangeIndex((key, val) => val.Value);

		// Pre-populate for read benchmarks
		for (var i = 0; i < ItemCount; i++) _cache.AddOrUpdate(i, i);
	}

	[Benchmark]
	public void Add_Sequential() {
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		for (var i = 0; i < ItemCount; i++) cache.AddOrUpdate(i, i);
	}

	[Benchmark]
	public void Remove_Sequential() {
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Pre-populate
		for (var i = 0; i < ItemCount; i++) cache.AddOrUpdate(i, i);

		// Remove all
		for (var i = 0; i < ItemCount; i++) cache.Remove(i);
	}

	[Benchmark]
	public int RangeQuery_Small() {
		var results = _rangeIndex!.GetValuesBetween(100, 200);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Medium() {
		var results = _rangeIndex!.GetValuesBetween(1000, 2000);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Large() {
		var results = _rangeIndex!.GetValuesBetween(0, ItemCount / 2);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Gte() {
		var results = _rangeIndex!.GetValuesGte(ItemCount / 2);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Lte() {
		var results = _rangeIndex!.GetValuesLte(ItemCount / 2);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Gt() {
		var results = _rangeIndex!.GetValuesGt(ItemCount / 2);
		return results.Count;
	}

	[Benchmark]
	public int RangeQuery_Lt() {
		var results = _rangeIndex!.GetValuesLt(ItemCount / 2);
		return results.Count;
	}

	[Benchmark]
	public void Update_Random() {
		var random = new Random(42);
		for (var i = 0; i < 1000; i++) {
			var key = random.Next(ItemCount);
			_cache!.AddOrUpdate(key, key * 2);
		}
	}

	[Benchmark]
	public void Mixed_Operations() {
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);
		var random = new Random(42);

		for (var i = 0; i < 10_000; i++) {
			var op = random.Next(3);
			var key = random.Next(ItemCount * 2);

			switch (op) {
				case 0: // Add
					cache.AddOrUpdate(key, key);
					break;
				case 1: // Remove
					cache.Remove(key);
					break;
				case 2: // Range query
					_ = rangeIndex.GetValuesBetween(key, key + 100);
					break;
			}
		}
	}
}

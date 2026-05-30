namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Core;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConcurrentBenchmarks {
	[Params(2, 4, 8)] public int ThreadCount { get; set; }

	[Params(10_000)] public int ItemsPerThread { get; set; }

	[Benchmark]
	public void Concurrent_Add() {
		var cache = new InMemoryDataCache<string, CacheEquatable<int>>();

		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < ItemsPerThread; i++) {
					var key = $"thread{threadId}_item{i}";
					cache.AddOrUpdate(key, threadId * ItemsPerThread + i);
				}
			});
		}

		Task.WaitAll(tasks);
	}

	[Benchmark]
	public void Concurrent_RangeQuery() {
		// Pre-populate
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		for (var i = 0; i < 100_000; i++) cache.AddOrUpdate(i, i);

		// Concurrent queries
		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < ItemsPerThread; i++) {
					var start = (threadId * ItemsPerThread + i) % 90_000;
					var end = start + 1000;
					_ = rangeIndex.GetValuesBetween(start, end);
				}
			});
		}

		Task.WaitAll(tasks);
	}

	[Benchmark]
	public void Concurrent_Mixed() {
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		// Pre-populate
		for (var i = 0; i < 10_000; i++) cache.AddOrUpdate(i, i);

		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				var random = new Random(threadId);
				for (var i = 0; i < ItemsPerThread; i++) {
					var op = random.Next(3);
					var key = random.Next(20_000);

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
			});
		}

		Task.WaitAll(tasks);
	}

	[Benchmark]
	public void Concurrent_AddRemove() {
		var cache = new InMemoryDataCache<int, CacheEquatable<int>>();
		var rangeIndex = cache.CacheRangeIndex((key, val) => val.Value);

		var tasks = new Task[ThreadCount * 2];

		// Half threads add
		for (var t = 0; t < ThreadCount; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < ItemsPerThread; i++) cache.AddOrUpdate(threadId * ItemsPerThread + i, i);
			});
		}

		// Half threads remove
		for (var t = 0; t < ThreadCount; t++) {
			var threadId = t;
			tasks[ThreadCount + t] = Task.Run(() => {
				for (var i = 0; i < ItemsPerThread; i++) cache.Remove(threadId * ItemsPerThread + i);
			});
		}

		Task.WaitAll(tasks);
	}
}

namespace Prague.Benchmarks;

using Prague.Core.Collections;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

/// <summary>
///   Direct comparison of PooledBTree vs ConcurrentSortedList.
///   Same operations on both structures, same data, same aggregator pattern.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PooledBTreeVsSkipListBenchmarks {
	private PooledBTree<int, int>? _tree;
	private ConcurrentSortedList<int, int>? _skipList;

	[Params(1000, 10_000, 100_000)] public int ItemCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		_tree = new PooledBTree<int, int>();
		_skipList = new ConcurrentSortedList<int, int>();
		for (var i = 0; i < ItemCount; i++) {
			_tree.Add(i, i);
			_skipList.Add(i, i);
		}
	}

	[GlobalCleanup]
	public void Cleanup() {
		_tree?.Dispose();
	}

	// ───────────────────── Add Sequential ─────────────────────

	[BenchmarkCategory("Add_Sequential"), Benchmark(Baseline = true)]
	public void Add_Sequential_SkipList() {
		var list = new ConcurrentSortedList<int, int>();
		for (var i = 0; i < ItemCount; i++)
			list.Add(i, i);
	}

	[BenchmarkCategory("Add_Sequential"), Benchmark]
	public void Add_Sequential_BTree() {
		var tree = new PooledBTree<int, int>();
		for (var i = 0; i < ItemCount; i++)
			tree.Add(i, i);
		tree.Dispose();
	}

	// ───────────────────── Remove Sequential ─────────────────────

	[BenchmarkCategory("Remove_Sequential"), Benchmark(Baseline = true)]
	public void Remove_Sequential_SkipList() {
		var list = new ConcurrentSortedList<int, int>();
		for (var i = 0; i < ItemCount; i++)
			list.Add(i, i);
		for (var i = 0; i < ItemCount; i++)
			list.Remove(i, i);
	}

	[BenchmarkCategory("Remove_Sequential"), Benchmark]
	public void Remove_Sequential_BTree() {
		var tree = new PooledBTree<int, int>();
		for (var i = 0; i < ItemCount; i++)
			tree.Add(i, i);
		for (var i = 0; i < ItemCount; i++)
			tree.Remove(i, i);
		tree.Dispose();
	}

	// ───────────────────── Range Query Small (100-200) ─────────────────────

	[BenchmarkCategory("RangeQuery_Small"), Benchmark(Baseline = true)]
	public int RangeQuery_Small_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.Range(100, 200, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Small"), Benchmark]
	public int RangeQuery_Small_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.Range(100, 200, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Medium (1000-2000) ─────────────────────

	[BenchmarkCategory("RangeQuery_Medium"), Benchmark(Baseline = true)]
	public int RangeQuery_Medium_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.Range(1000, 2000, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Medium"), Benchmark]
	public int RangeQuery_Medium_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.Range(1000, 2000, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Large (0 to half) ─────────────────────

	[BenchmarkCategory("RangeQuery_Large"), Benchmark(Baseline = true)]
	public int RangeQuery_Large_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.Range(0, ItemCount / 2, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Large"), Benchmark]
	public int RangeQuery_Large_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.Range(0, ItemCount / 2, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Gte ─────────────────────

	[BenchmarkCategory("RangeQuery_Gte"), Benchmark(Baseline = true)]
	public int RangeQuery_Gte_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.RangeFrom(ItemCount / 2, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Gte"), Benchmark]
	public int RangeQuery_Gte_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.RangeFrom(ItemCount / 2, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Lte ─────────────────────

	[BenchmarkCategory("RangeQuery_Lte"), Benchmark(Baseline = true)]
	public int RangeQuery_Lte_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.RangeTo(ItemCount / 2, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Lte"), Benchmark]
	public int RangeQuery_Lte_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.RangeTo(ItemCount / 2, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Gt ─────────────────────

	[BenchmarkCategory("RangeQuery_Gt"), Benchmark(Baseline = true)]
	public int RangeQuery_Gt_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.RangeFromExclusive(ItemCount / 2, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Gt"), Benchmark]
	public int RangeQuery_Gt_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.RangeFromExclusive(ItemCount / 2, ref agg);
		return agg.Count;
	}

	// ───────────────────── Range Query Lt ─────────────────────

	[BenchmarkCategory("RangeQuery_Lt"), Benchmark(Baseline = true)]
	public int RangeQuery_Lt_SkipList() {
		var agg = new SkipListCountAggregator();
		_skipList!.RangeToExclusive(ItemCount / 2, ref agg);
		return agg.Count;
	}

	[BenchmarkCategory("RangeQuery_Lt"), Benchmark]
	public int RangeQuery_Lt_BTree() {
		var agg = new BTreeCountAggregator();
		_tree!.RangeToExclusive(ItemCount / 2, ref agg);
		return agg.Count;
	}

	// ───────────────────── Update Random ─────────────────────

	[BenchmarkCategory("Update_Random"), Benchmark(Baseline = true)]
	public void Update_Random_SkipList() {
		var random = new Random(42);
		for (var i = 0; i < 1000; i++) {
			var key = random.Next(ItemCount);
			_skipList!.Remove(key, key);
			_skipList.Add(key, key * 2);
		}
	}

	[BenchmarkCategory("Update_Random"), Benchmark]
	public void Update_Random_BTree() {
		var random = new Random(42);
		for (var i = 0; i < 1000; i++) {
			var key = random.Next(ItemCount);
			_tree!.Remove(key, key);
			_tree.Add(key, key * 2);
		}
	}

	// ───────────────────── Mixed Operations ─────────────────────

	[BenchmarkCategory("Mixed_Operations"), Benchmark(Baseline = true)]
	public void Mixed_Operations_SkipList() {
		var list = new ConcurrentSortedList<int, int>();
		var random = new Random(42);

		for (var i = 0; i < 10_000; i++) {
			var op = random.Next(3);
			var key = random.Next(ItemCount * 2);

			switch (op) {
				case 0:
					list.Add(key, key);
					break;
				case 1:
					list.Remove(key, key);
					break;
				case 2:
					var agg = new SkipListCountAggregator();
					list.Range(key, key + 100, ref agg);
					break;
			}
		}
	}

	[BenchmarkCategory("Mixed_Operations"), Benchmark]
	public void Mixed_Operations_BTree() {
		var tree = new PooledBTree<int, int>();
		var random = new Random(42);

		for (var i = 0; i < 10_000; i++) {
			var op = random.Next(3);
			var key = random.Next(ItemCount * 2);

			switch (op) {
				case 0:
					tree.Add(key, key);
					break;
				case 1:
					tree.Remove(key, key);
					break;
				case 2:
					var agg = new BTreeCountAggregator();
					tree.Range(key, key + 100, ref agg);
					break;
			}
		}

		tree.Dispose();
	}

	// ───────────────────── Aggregators ─────────────────────

	private struct SkipListCountAggregator : ConcurrentSortedList<int, int>.IResultAggregator {
		public int Count;
		public void Add(int index, int value) => Count++;
		public void Dispose() { }
	}

	private struct BTreeCountAggregator : PooledBTree<int, int>.IResultAggregator {
		public int Count;
		public void Add(int index, int value) => Count++;
		public void Dispose() { }
	}
}

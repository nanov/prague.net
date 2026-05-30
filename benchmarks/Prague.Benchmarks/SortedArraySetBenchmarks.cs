namespace Prague.Benchmarks;

using System.Collections.Immutable;
using Prague.Core.Collections;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

/// <summary>
///   Benchmarks SortedArraySet vs PooledSet vs ImmutableHashSet for the CacheKeyValueListIndex use case.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[RankColumn]
public class SortedArraySetBenchmarks {
	private int[] _addOrder = null!;
	private int[] _removeOrder = null!;
	private ImmutableHashSet<int> _populatedImmutable = null!;
	private SortedArraySet<int> _populatedSorted = null!;
	private PooledSet<int, DefaultKeyComparer<int>> _populatedPooled = null!;

	[Params(10, 100, 1000)]
	public int Size { get; set; }

	[GlobalSetup]
	public void Setup() {
		var rng = new Random(42);
		_addOrder = Enumerable.Range(0, Size).OrderBy(_ => rng.Next()).ToArray();
		_removeOrder = _addOrder.OrderBy(_ => rng.Next()).ToArray();

		_populatedImmutable = ImmutableHashSet.CreateRange(_addOrder);
		_populatedSorted = new SortedArraySet<int>();
		foreach (var item in _addOrder)
			_populatedSorted.Add(item);
		_populatedPooled = new PooledSet<int, DefaultKeyComparer<int>>();
		foreach (var item in _addOrder)
			_populatedPooled.Add(item);
	}

	// --- Add one by one ---

	[BenchmarkCategory("AddOneByOne"), Benchmark(Baseline = true)]
	public ImmutableHashSet<int> ImmutableHashSet_AddOneByOne() {
		var set = ImmutableHashSet.Create(_addOrder[0]);
		for (var i = 1; i < _addOrder.Length; i++)
			set = set.Add(_addOrder[i]);
		return set;
	}

	[BenchmarkCategory("AddOneByOne"), Benchmark]
	public void SortedArraySet_AddOneByOne() {
		var set = new SortedArraySet<int>();
		for (var i = 0; i < _addOrder.Length; i++)
			set.Add(_addOrder[i]);
		set.Dispose();
	}

	[BenchmarkCategory("AddOneByOne"), Benchmark]
	public void PooledSet_AddOneByOne() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < _addOrder.Length; i++)
			set.Add(_addOrder[i]);
		set.Dispose();
	}

	// --- Add duplicate ---

	[BenchmarkCategory("AddDuplicate"), Benchmark(Baseline = true)]
	public ImmutableHashSet<int> ImmutableHashSet_AddDuplicate() {
		var set = _populatedImmutable;
		for (var i = 0; i < _addOrder.Length; i++)
			set = set.Add(_addOrder[i]);
		return set;
	}

	[BenchmarkCategory("AddDuplicate"), Benchmark]
	public void SortedArraySet_AddDuplicate() {
		for (var i = 0; i < _addOrder.Length; i++)
			_populatedSorted.Add(_addOrder[i]);
	}

	[BenchmarkCategory("AddDuplicate"), Benchmark]
	public void PooledSet_AddDuplicate() {
		for (var i = 0; i < _addOrder.Length; i++)
			_populatedPooled.Add(_addOrder[i]);
	}

	// --- Remove one by one ---

	[BenchmarkCategory("RemoveOneByOne"), Benchmark(Baseline = true)]
	public ImmutableHashSet<int> ImmutableHashSet_RemoveOneByOne() {
		var set = _populatedImmutable;
		for (var i = 0; i < _removeOrder.Length; i++)
			set = set.Remove(_removeOrder[i]);
		return set;
	}

	[BenchmarkCategory("RemoveOneByOne"), Benchmark]
	public void SortedArraySet_RemoveOneByOne() {
		var set = new SortedArraySet<int>();
		for (var i = 0; i < _addOrder.Length; i++)
			set.Add(_addOrder[i]);
		for (var i = 0; i < _removeOrder.Length; i++)
			set.Remove(_removeOrder[i]);
		set.Dispose();
	}

	[BenchmarkCategory("RemoveOneByOne"), Benchmark]
	public void PooledSet_RemoveOneByOne() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < _addOrder.Length; i++)
			set.Add(_addOrder[i]);
		for (var i = 0; i < _removeOrder.Length; i++)
			set.Remove(_removeOrder[i]);
		set.Dispose();
	}

	// --- Iterate ---

	[BenchmarkCategory("Iterate"), Benchmark(Baseline = true)]
	public int ImmutableHashSet_Iterate() {
		var sum = 0;
		foreach (var item in _populatedImmutable)
			sum += item;
		return sum;
	}

	[BenchmarkCategory("Iterate"), Benchmark]
	public int SortedArraySet_Iterate() {
		var sum = 0;
		foreach (var item in _populatedSorted)
			sum += item;
		return sum;
	}

	[BenchmarkCategory("Iterate"), Benchmark]
	public int PooledSet_Iterate() {
		var sum = 0;
		foreach (var item in _populatedPooled)
			sum += item;
		return sum;
	}

	// --- Contains ---

	[BenchmarkCategory("Contains"), Benchmark(Baseline = true)]
	public int ImmutableHashSet_Contains() {
		var found = 0;
		for (var i = 0; i < Size; i++)
			if (_populatedImmutable.Contains(i))
				found++;
		return found;
	}

	[BenchmarkCategory("Contains"), Benchmark]
	public int SortedArraySet_Contains() {
		var found = 0;
		for (var i = 0; i < Size; i++)
			if (_populatedSorted.Contains(i))
				found++;
		return found;
	}

	[BenchmarkCategory("Contains"), Benchmark]
	public int PooledSet_Contains() {
		var found = 0;
		for (var i = 0; i < Size; i++)
			if (_populatedPooled.Contains(i))
				found++;
		return found;
	}
}

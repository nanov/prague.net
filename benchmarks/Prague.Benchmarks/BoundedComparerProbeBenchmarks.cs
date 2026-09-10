namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Prague.Core;
using Prague.Core.Collections;
using Sorter = Prague.Core.SortResolver<int, Prague.Benchmarks.PqbRecord, Prague.Benchmarks.PqbRecord, Prague.Benchmarks.PqbRecordByScoreTies>;
using Link = Prague.Core.BaseResolver<int, Prague.Benchmarks.PqbRecord>;

/// <summary>
///   Diagnostic probe for the frozen pipeline's step 7: the same bounded top-k workload (N candidates
///   into a heap of K, then the ascending drain) driven through the three comparers the bounded
///   containers use — the simple container's <see cref="TopKValueComparer{TValue,TResolver}" /> (the
///   sorter as a struct type parameter, one hop), the joined container's
///   <see cref="TopKPairComparer{TKey,TValue,TChain}" /> over a chain of one, two and three links (the
///   sorter reached through <c>IResolvers.CompareLeftValues</c>, one hop per link), and a pair comparer
///   that holds the sorter itself. Isolates the per-comparison chain dispatch from every other cost in
///   the query.
/// </summary>
[MemoryDiagnoser]
public class BoundedComparerProbeBenchmarks {
	// Shape B's page over its ~100 matched rows; shape A's is 111 rows over the same page.
	private const int N = 100;
	private const int K = 40;

	private PqbRecord[] _items = null!;
	private (PqbRecord Left, int Ordinal)[] _valueHeap = null!;
	private (int Key, PqbRecord Left, int Ordinal)[] _pairHeap = null!;
	private Sorter _sorter;
	private Resolvers<Sorter> _chain1;
	private Resolvers<Resolvers<Sorter>, Link> _chain2;
	private Resolvers<Resolvers<Resolvers<Sorter>, Link>, Link> _chain3;

	[GlobalSetup]
	public void Setup() {
		var rng = new Random(1234);
		_items = new PqbRecord[N];
		for (var i = 0; i < N; i++)
			_items[i] = new PqbRecord { Id = i, Score = rng.Next(1 << 20) };
		_valueHeap = new (PqbRecord, int)[K];
		_pairHeap = new (int, PqbRecord, int)[K];
		_sorter = new Sorter(new PqbRecordByScoreTies(), true);
		_chain1 = new Resolvers<Sorter>(_sorter);
		_chain2 = new Resolvers<Resolvers<Sorter>, Link>(_chain1, default);
		_chain3 = new Resolvers<Resolvers<Resolvers<Sorter>, Link>, Link>(_chain2, default);
	}

	/// <summary>The simple bounded container's comparer: the sorter is the type parameter.</summary>
	[Benchmark(Baseline = true)]
	public int ValueComparer_Sorter() {
		var comparer = new TopKValueComparer<PqbRecord, Sorter>(_sorter);
		return RunValue(comparer);
	}

	/// <summary>The proposed frozen joined comparer: the (key, left, ordinal) triple, the sorter by value.</summary>
	[Benchmark]
	public int PairComparer_Sorter() => RunPair(new SorterPairComparer<Sorter>(_sorter));

	[Benchmark]
	public int PairComparer_Chain1() {
		var chain = _chain1;
		return RunPair(new TopKPairComparer<int, PqbRecord, Resolvers<Sorter>>(ref chain));
	}

	/// <summary>Shape A's chain: the sorter plus one <c>JoinOne</c>.</summary>
	[Benchmark]
	public int PairComparer_Chain2() {
		var chain = _chain2;
		return RunPair(new TopKPairComparer<int, PqbRecord, Resolvers<Resolvers<Sorter>, Link>>(ref chain));
	}

	/// <summary>Shape B's chain: the sorter plus two <c>JoinOne</c>s.</summary>
	[Benchmark]
	public int PairComparer_Chain3() {
		var chain = _chain3;
		return RunPair(new TopKPairComparer<int, PqbRecord, Resolvers<Resolvers<Resolvers<Sorter>, Link>, Link>>(ref chain));
	}

	private int RunValue<TComparer>(TComparer comparer) where TComparer : struct, IComparer<(PqbRecord Left, int Ordinal)> {
		var heap = _valueHeap;
		var items = _items;
		var count = 0;
		var heapified = false;
		for (var i = 0; i < items.Length; i++)
			TopKSelect.Push(heap, ref count, ref heapified, K, (items[i], i), comparer);
		return TopKSelect.DrainAscending(heap, ref count, ref heapified, comparer);
	}

	private int RunPair<TComparer>(TComparer comparer) where TComparer : struct, IComparer<(int Key, PqbRecord Left, int Ordinal)> {
		var heap = _pairHeap;
		var items = _items;
		var count = 0;
		var heapified = false;
		for (var i = 0; i < items.Length; i++)
			TopKSelect.Push(heap, ref count, ref heapified, K, (i, items[i], i), comparer);
		return TopKSelect.DrainAscending(heap, ref count, ref heapified, comparer);
	}

	private readonly struct SorterPairComparer<TResolver> : IComparer<(int Key, PqbRecord Left, int Ordinal)>
		where TResolver : struct, IJoinResolver {
		private readonly TResolver _sorter;

		public SorterPairComparer(TResolver sorter) => _sorter = sorter;

		public int Compare((int Key, PqbRecord Left, int Ordinal) x, (int Key, PqbRecord Left, int Ordinal) y) {
			var order = _sorter.CompareLeftValues(x.Left, y.Left);
			return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
		}
	}
}

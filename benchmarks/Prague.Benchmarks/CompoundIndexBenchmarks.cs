namespace Prague.Benchmarks;

using System.Buffers;
using Prague.Core.Collections;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

/// <summary>
///   Benchmarks compound index (seek + walk + take) vs traditional (filter to ValueSet + sort).
///
///   Scenario: we have N entities with a "Category" (int) and a "Price" (int).
///   Query: "Give me the cheapest K items in category X".
///
///   Approach A — Compound Index:
///     Compound BTree keyed by (Category, Price, EntityKey).
///     Seek to (category, 0, 0), walk forward, take K. O(log N + K).
///
///   Approach B — Filter then Sort:
///     1. Use a KeyValueList index to get all keys for category → ValueSet
///     2. For each candidate, look up entity price from a separate array (simulating cache lookup)
///     3. Sort candidates by price
///     4. Take first K
///
///   Approach C — Filter then Heap (top-K selection):
///     1. Same filter as B
///     2. Use a max-heap of size K to select top-K by price without full sort
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CompoundIndexBenchmarks {
	// Total entities in the cache
	[Params(10_000, 100_000, 1_000_000)]
	public int TotalEntities { get; set; }

	// Number of categories (controls selectivity: items per category = Total / Categories)
	[Params(10, 100)]
	public int CategoryCount { get; set; }

	// How many results to take
	[Params(20, 100)]
	public int Take { get; set; }

	// The compound index
	private CompoundIndex<int, int, int> _compoundIndex = null!;

	// Traditional index: category → set of entity keys
	private Dictionary<int, List<int>> _categoryToKeys = null!;

	// Simulated entity data: entityKey → price
	private int[] _prices = null!;

	// Which category to query (fixed for determinism)
	private int _queryCategory;

	[GlobalSetup]
	public void Setup() {
		var rng = new Random(42);

		_compoundIndex = new CompoundIndex<int, int, int>();
		_categoryToKeys = new Dictionary<int, List<int>>();
		_prices = new int[TotalEntities];

		for (var cat = 0; cat < CategoryCount; cat++)
			_categoryToKeys[cat] = new List<int>();

		// Populate data
		for (var i = 0; i < TotalEntities; i++) {
			var category = i % CategoryCount;
			var price = rng.Next(1, 1_000_000);
			_prices[i] = price;

			// Add to compound index
			_compoundIndex.Add(category, price, i);

			// Add to traditional category index
			_categoryToKeys[category].Add(i);
		}

		// Query a category that has a known number of items
		_queryCategory = 0;
	}

	[GlobalCleanup]
	public void Cleanup() {
		_compoundIndex.Dispose();
	}

	/// <summary>
	///   Compound index: seek to prefix, walk in sort order, take K.
	///   O(log N + K) — no filtering, no sorting needed.
	/// </summary>
	[Benchmark(Baseline = true)]
	public int CompoundIndex_SeekAndTake() {
		var buffer = ArrayPool<int>.Shared.Rent(Take);
		var count = _compoundIndex.SeekAndTake(_queryCategory, 0, Take, buffer);

		// Consume results to prevent dead code elimination
		var sum = 0;
		for (var i = 0; i < count; i++)
			sum += buffer[i];

		ArrayPool<int>.Shared.Return(buffer);
		return sum;
	}

	/// <summary>
	///   Compound index with skip: simulates page 3 (skip 40, take 20).
	/// </summary>
	[Benchmark]
	public int CompoundIndex_SkipAndTake() {
		var buffer = ArrayPool<int>.Shared.Rent(Take);
		var count = _compoundIndex.SeekAndTake(_queryCategory, Take * 2, Take, buffer);

		var sum = 0;
		for (var i = 0; i < count; i++)
			sum += buffer[i];

		ArrayPool<int>.Shared.Return(buffer);
		return sum;
	}

	/// <summary>
	///   Traditional: filter by category into ValueSet, extract prices, sort, take K.
	///   O(M) for filter + O(M log M) for sort + O(K) for take.
	/// </summary>
	[Benchmark]
	public int FilterThenSort() {
		var candidates = _categoryToKeys[_queryCategory];
		var m = candidates.Count;

		// Build (price, key) pairs for sorting
		var pairs = ArrayPool<(int price, int key)>.Shared.Rent(m);
		for (var i = 0; i < m; i++) {
			var key = candidates[i];
			pairs[i] = (_prices[key], key);
		}

		// Sort by price
		Array.Sort(pairs, 0, m, PriceComparer.Instance);

		// Take K
		var take = Math.Min(Take, m);
		var sum = 0;
		for (var i = 0; i < take; i++)
			sum += pairs[i].key;

		ArrayPool<(int price, int key)>.Shared.Return(pairs);
		return sum;
	}

	/// <summary>
	///   Traditional with heap selection: filter by category, use a max-heap of size K
	///   to find the K smallest prices without full sort.
	///   O(M log K) — better than O(M log M) when K &lt;&lt; M.
	/// </summary>
	[Benchmark]
	public int FilterThenHeapSelect() {
		var candidates = _categoryToKeys[_queryCategory];
		var m = candidates.Count;
		var k = Math.Min(Take, m);

		// Max-heap of size K (we keep the K smallest; evict the largest from heap)
		var heap = ArrayPool<(int price, int key)>.Shared.Rent(k);
		var heapSize = 0;

		for (var i = 0; i < m; i++) {
			var candidateKey = candidates[i];
			var price = _prices[candidateKey];

			if (heapSize < k) {
				heap[heapSize] = (price, candidateKey);
				heapSize++;
				if (heapSize == k)
					BuildMaxHeap(heap, k);
			}
			else if (price < heap[0].price) {
				heap[0] = (price, candidateKey);
				SiftDown(heap, 0, k);
			}
		}

		var sum = 0;
		for (var i = 0; i < heapSize; i++)
			sum += heap[i].key;

		ArrayPool<(int price, int key)>.Shared.Return(heap);
		return sum;
	}

	/// <summary>
	///   Compound index with additional filter via ValueSet intersection.
	///   Simulates: compound index provides sorted order, but we also need to check
	///   membership in a candidate set from another index.
	/// </summary>
	[Benchmark]
	public int CompoundIndex_WithCandidateFilter() {
		// Build a ValueSet of candidates (simulate another filter reducing candidates to 50%)
		var allCandidates = _categoryToKeys[_queryCategory];
		var candidates = new ValueSet<int, DefaultKeyComparer<int>>(allCandidates.Count);
		for (var i = 0; i < allCandidates.Count; i += 2) // take every other = 50%
			candidates.Add(allCandidates[i]);

		var buffer = ArrayPool<int>.Shared.Rent(Take);
		var count = _compoundIndex.SeekFilterAndTake(_queryCategory, ref candidates, 0, Take, buffer);

		var sum = 0;
		for (var i = 0; i < count; i++)
			sum += buffer[i];

		ArrayPool<int>.Shared.Return(buffer);
		candidates.Dispose();
		return sum;
	}

	// --- Heap helpers ---

	private static void BuildMaxHeap((int price, int key)[] heap, int size) {
		for (var i = size / 2 - 1; i >= 0; i--)
			SiftDown(heap, i, size);
	}

	private static void SiftDown((int price, int key)[] heap, int i, int size) {
		while (true) {
			var largest = i;
			var left = 2 * i + 1;
			var right = 2 * i + 2;

			if (left < size && heap[left].price > heap[largest].price)
				largest = left;
			if (right < size && heap[right].price > heap[largest].price)
				largest = right;

			if (largest == i) break;

			(heap[i], heap[largest]) = (heap[largest], heap[i]);
			i = largest;
		}
	}

	private class PriceComparer : IComparer<(int price, int key)> {
		public static readonly PriceComparer Instance = new();
		public int Compare((int price, int key) x, (int price, int key) y) => x.price.CompareTo(y.price);
	}
}

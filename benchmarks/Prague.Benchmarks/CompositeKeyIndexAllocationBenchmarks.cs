namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Core;

/// <summary>
///   A 7-value enum used as the second half of the composite cache key.
/// </summary>
public enum Category {
	Electronics,
	Grocery,
	Apparel,
	Home,
	Toys,
	Sports,
	Books,
}

/// <summary>
///   Model keyed by the composite <c>(Id, Category)</c> value tuple, with an unhinted
///   Many index on <see cref="Id" />. Id is unique; each Id fans out to up to 7 rows
///   (one per Category), so the Id index builds exactly one PooledSet bucket per Id
///   holding &lt;= 7 keys, yet every bucket pre-sizes its first generation to the
///   library default — an over-allocation for buckets this small.
/// </summary>
[DataCache]
public partial class CategorizedProduct {
	[DataCacheIndex(DataCacheIndexType.Many)]
	public int Id { get; set; }

	public Category Category { get; set; }

	// Composite primary key — a value tuple, computed from the two indexed fields.
	[DataCacheKey] public (int, Category) Key => (Id, Category);

	public long Payload { get; set; }
}

/// <summary>
///   The same shape with the known fan-out declared up front:
///   <c>[DataCacheIndex(DataCacheIndexType.Many, 7)]</c> sizes each bucket's first
///   generation to the actual maximum instead of the library default.
/// </summary>
[DataCache]
public partial class HintedCategorizedProduct {
	[DataCacheIndex(DataCacheIndexType.Many, 7)]
	public int Id { get; set; }

	public Category Category { get; set; }

	[DataCacheKey] public (int, Category) Key => (Id, Category);

	public long Payload { get; set; }
}

/// <summary>
///   Measures the allocation footprint of inserting a composite-key dataset into the
///   cache. Interest is the Many index on Id: it allocates one PooledSet bucket per Id,
///   each holding at most 7 keys. The unhinted baseline pre-sizes every bucket's rented
///   tables to the library default first generation (59 slots on 64-slot pooled
///   arrays); the hinted variant declares the fan-out via
///   <c>[DataCacheIndex(DataCacheIndexType.Many, 7)]</c> (7 slots on 8-slot arrays), so
///   the ratio column shows what the sizing hint buys. Building a fresh cache inside
///   the benchmark attributes every bucket's rented arrays to the op.
///   Do NOT dispose — pool recycling would hide the rented bytes from MemoryDiagnoser
/// </summary>
[MemoryDiagnoser]
public class CompositeKeyIndexAllocationBenchmarks {
	private const long Timestamp = 1_600_000_000_000L;

	private CategorizedProduct[] _dataset = null!;
	private HintedCategorizedProduct[] _hintedDataset = null!;

	// Number of unique Ids. Rows = ~7x this (most Ids get all 7 categories).
	[Params(1_000, 10_000)] public int ProductCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		var categories = Enum.GetValues<Category>();
		var rows = new List<CategorizedProduct>(ProductCount * categories.Length);
		var hintedRows = new List<HintedCategorizedProduct>(ProductCount * categories.Length);

		for (var id = 0; id < ProductCount; id++) {
			// Most Ids fan out to all 7 categories; every 5th Id gets fewer (1..6),
			// so the bucket sizes spread but stay well under the default capacity.
			var count = id % 5 == 0 ? 1 + id % 6 : categories.Length;
			for (var c = 0; c < count; c++) {
				rows.Add(new CategorizedProduct { Id = id, Category = categories[c], Payload = id * 31L + c });
				hintedRows.Add(new HintedCategorizedProduct { Id = id, Category = categories[c], Payload = id * 31L + c });
			}
		}

		_dataset = rows.ToArray();
		_hintedDataset = hintedRows.ToArray();
	}

	[Benchmark(Baseline = true)]
	public CategorizedProductCache InsertDataset() {
		var cache = new CategorizedProductCache();
		var rows = _dataset;
		for (var i = 0; i < rows.Length; i++) {
			cache.AddOrUpdate(rows[i], Timestamp);
		}

		return cache;
	}

	[Benchmark]
	public HintedCategorizedProductCache InsertDatasetHinted() {
		var cache = new HintedCategorizedProductCache();
		var rows = _hintedDataset;
		for (var i = 0; i < rows.Length; i++) {
			cache.AddOrUpdate(rows[i], Timestamp);
		}

		return cache;
	}
}

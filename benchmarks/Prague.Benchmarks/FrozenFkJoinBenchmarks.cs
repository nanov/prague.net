namespace Prague.Benchmarks;

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   The generated FK join path under <c>BuildFrozen()</c>. <see cref="FrozenQueryBenchmarks" /> measures
///   hand-written <c>JoinOne</c> / <c>JoinMany</c> on raw <see cref="InMemoryDataCache{TKey,TValue}" />;
///   nothing measured the <c>JoinWith{T}</c> the source generator emits from
///   <c>[DataCacheForeignKey]</c>, which is what users actually write. Each category is an
///   <c>_Eager</c> (baseline) / <c>_Frozen</c> pair over one resolver family, every one of them after a
///   generated list-index narrowing of ~1k rows:
///   <list type="bullet">
///     <item><c>Fk_ManyToOne</c> — forward <c>JoinWithFfkCustomer</c>, the left-symmetric resolver.</item>
///     <item><c>Fk_InnerManyToOne</c> — the same, inner; it fuses (the fan-out regroups, which the ordering contract allows).</item>
///     <item><c>Fk_OneToOneReverse</c> — <c>JoinWithFfkProfile</c>, the right-unique resolver.</item>
///     <item><c>Fk_OneToManyReverse</c> — <c>JoinWithFfkLine</c>, the right-list <c>JoinMany</c>.</item>
///     <item><c>Fk_Collection</c> — <c>JoinWithFfkTag</c> over a <c>List&lt;int&gt;</c> FK, the collection <c>JoinMany</c>.</item>
///     <item><c>Fk_SortBounded_OneToOneReverse</c> — list → <c>SortBounded</c> page → reverse one-to-one join, the bounded joined container.</item>
///     <item><c>Fk_ManyToOne_Filtered</c> — a right-side filter callback; the replay fallback today.</item>
///   </list>
///   Data: 100k customers (segments of 1k), 100k orders (groups of 1k; a quarter point at a customer that
///   does not exist, so the inner join drops them), a profile for three customers in four, two lines per
///   order for three orders in four, and 10k documents (buckets of 1k) with three tags each, a quarter
///   untagged. Every body executes pooled and disposes.
///   The bounded category joins reverse one-to-one, not forward: the forward and inner <c>JoinWith{T}</c>
///   bind on <c>ICacheCarrier</c>, which a <c>SortedQuery</c> discriminator does not carry — eager too —
///   so <c>SortBounded().JoinWithFfkCustomer()</c> does not compile. The reverse outer one-to-one join
///   after a <c>SortBounded</c> is the generated bounded shape.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrozenFkJoinBenchmarks {
	private const int N = 100_000;
	private const int Buckets = 100;
	private const int DocCount = 10_000;
	private const int TagCount = 500;

	private DataCacheRegistry _registry = null!;
	private FfkCustomerCache _customers = null!;
	private FfkOrderCache _orders = null!;
	private FfkProfileCache _profiles = null!;
	private FfkLineCache _lines = null!;
	private FfkTagCache _tags = null!;
	private FfkDocCache _docs = null!;

	private FrozenQuery<int, JoinResult<FfkOrder, FfkCustomer?>> _manyToOneFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkOrder, FfkCustomer?>> _innerManyToOneFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkCustomer, FfkProfile?>> _oneToOneReverseFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkOrder, QueryResults<FfkLine>>> _oneToManyReverseFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkDoc, QueryResults<FfkTag>>> _collectionFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkCustomer, FfkProfile?>> _sortBoundedOneToOneReverseFrozen = null!;
	private FrozenQuery<int, JoinResult<FfkOrder, FfkCustomer?>> _manyToOneFilteredFrozen = null!;

	// Arguments are fields, not constants, so no side gets a constant folded into the query.
	private int _group = 13;
	private int _segment = 13;
	private int _bucket = 3;

	[GlobalSetup]
	public void Setup() {
		_registry = new DataCacheRegistryBuilder()
			.Register<FfkCustomerCache>()
			.Register<FfkOrderCache>()
			.Register<FfkProfileCache>()
			.Register<FfkLineCache>()
			.Register<FfkTagCache>()
			.Register<FfkDocCache>()
			.Build();
		_customers = _registry.GetCache<FfkCustomerCache>();
		_orders = _registry.GetCache<FfkOrderCache>();
		_profiles = _registry.GetCache<FfkProfileCache>();
		_lines = _registry.GetCache<FfkLineCache>();
		_tags = _registry.GetCache<FfkTagCache>();
		_docs = _registry.GetCache<FfkDocCache>();

		var rng = new Random(1234);
		for (var i = 0; i < N; i++) {
			// The "every fourth" rows count WITHIN a bucket: i % 4 is constant inside a group of stride
			// Buckets, so the misses have to key off the position in the bucket or no bucket ever sees one.
			var slot = i / Buckets % 4;
			_customers.AddOrUpdate(new FfkCustomer { Id = i, Segment = i % Buckets, Region = i % 2 == 0 ? "EU" : "US", Score = rng.Next(int.MaxValue) });
			// A quarter of the orders point at a customer id nobody holds: the inner join drops them.
			_orders.AddOrUpdate(new FfkOrder { Id = i, Group = i % Buckets, CustomerId = slot == 3 ? i + N * 10 : i, Score = rng.Next(int.MaxValue) });
			if (slot != 0)
				_profiles.AddOrUpdate(new FfkProfile { Id = i, CustomerId = i, Bio = "b" });
			if (slot == 0)
				continue;
			_lines.AddOrUpdate(new FfkLine { Id = 2 * i, OrderId = i, Qty = i % 7 });
			_lines.AddOrUpdate(new FfkLine { Id = 2 * i + 1, OrderId = i, Qty = i % 11 });
		}

		for (var t = 0; t < TagCount; t++)
			_tags.AddOrUpdate(new FfkTag { Id = t, Name = "t" + t });
		for (var d = 0; d < DocCount; d++)
			_docs.AddOrUpdate(new FfkDoc {
				Id = d, Bucket = d % 10,
				TagIds = d / 10 % 4 == 0 ? [] : [d % TagCount, d * 7 % TagCount, d * 13 % TagCount],
			});

		_manyToOneFrozen = _orders.Prepare<int>().WithGroup(static g => g).JoinWithFfkCustomer().BuildFrozen();
		_innerManyToOneFrozen = _orders.Prepare<int>().WithGroup(static g => g).InnerJoinWithFfkCustomer().BuildFrozen();
		_oneToOneReverseFrozen = _customers.Prepare<int>().WithSegment(static s => s).JoinWithFfkProfile().BuildFrozen();
		_oneToManyReverseFrozen = _orders.Prepare<int>().WithGroup(static g => g).JoinWithFfkLine().BuildFrozen();
		_collectionFrozen = _docs.Prepare<int>().WithBucket(static b => b).JoinWithFfkTag().BuildFrozen();
		_sortBoundedOneToOneReverseFrozen = _customers.Prepare<int>().WithSegment(static s => s)
			.SortBounded(new FfkCustomerByScoreTies()).JoinWithFfkProfile().BuildFrozen();
		_manyToOneFilteredFrozen = _orders.Prepare<int>().WithGroup(static g => g)
			.JoinWithFfkCustomer(static q => q.Where(static c => c.Region == "EU")).BuildFrozen();
	}

	// ── Fk_ManyToOne: list (1k) → forward JoinWith, the left-symmetric resolver ──────────────────────

	[BenchmarkCategory("Fk_ManyToOne"), Benchmark(Baseline = true)]
	public int Fk_ManyToOne_Eager() {
		using var r = _orders.Query().WithGroup(_group).JoinWithFfkCustomer().ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_ManyToOne"), Benchmark]
	public int Fk_ManyToOne_Frozen() {
		using var r = _manyToOneFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// ── Fk_InnerManyToOne: the same chain, inner — a quarter of the lefts have no customer ───────────

	[BenchmarkCategory("Fk_InnerManyToOne"), Benchmark(Baseline = true)]
	public int Fk_InnerManyToOne_Eager() {
		using var r = _orders.Query().WithGroup(_group).InnerJoinWithFfkCustomer().ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_InnerManyToOne"), Benchmark]
	public int Fk_InnerManyToOne_Frozen() {
		using var r = _innerManyToOneFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// ── Fk_OneToOneReverse: list (1k) → reverse one-to-one, the right-unique resolver ────────────────

	[BenchmarkCategory("Fk_OneToOneReverse"), Benchmark(Baseline = true)]
	public int Fk_OneToOneReverse_Eager() {
		using var r = _customers.Query().WithSegment(_segment).JoinWithFfkProfile().ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_OneToOneReverse"), Benchmark]
	public int Fk_OneToOneReverse_Frozen() {
		using var r = _oneToOneReverseFrozen.ExecutePooled(_segment);
		return r.Count;
	}

	// ── Fk_OneToManyReverse: list (1k) → reverse one-to-many, the right-list JoinMany ────────────────

	[BenchmarkCategory("Fk_OneToManyReverse"), Benchmark(Baseline = true)]
	public int Fk_OneToManyReverse_Eager() {
		using var r = _orders.Query().WithGroup(_group).JoinWithFfkLine().ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_OneToManyReverse"), Benchmark]
	public int Fk_OneToManyReverse_Frozen() {
		using var r = _oneToManyReverseFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// ── Fk_Collection: list (1k) → collection FK JoinMany (three tags per doc, a quarter untagged) ───

	[BenchmarkCategory("Fk_Collection"), Benchmark(Baseline = true)]
	public int Fk_Collection_Eager() {
		using var r = _docs.Query().WithBucket(_bucket).JoinWithFfkTag().ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_Collection"), Benchmark]
	public int Fk_Collection_Frozen() {
		using var r = _collectionFrozen.ExecutePooled(_bucket);
		return r.Count;
	}

	// ── Fk_SortBounded_OneToOneReverse: list (1k) → SortBounded page of 20 → reverse one-to-one ──────

	[BenchmarkCategory("Fk_SortBounded_OneToOneReverse"), Benchmark(Baseline = true)]
	public int Fk_SortBounded_OneToOneReverse_Eager() {
		using var r = _customers.Query().WithSegment(_segment).SortBounded(new FfkCustomerByScoreTies()).JoinWithFfkProfile().ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("Fk_SortBounded_OneToOneReverse"), Benchmark]
	public int Fk_SortBounded_OneToOneReverse_Frozen() {
		using var r = _sortBoundedOneToOneReverseFrozen.ExecutePooled(_segment, 20, 20);
		return r.Count;
	}

	// ── Fk_ManyToOne_Filtered: a right-side filter callback — the replay fallback today ──────────────

	[BenchmarkCategory("Fk_ManyToOne_Filtered"), Benchmark(Baseline = true)]
	public int Fk_ManyToOne_Filtered_Eager() {
		using var r = _orders.Query().WithGroup(_group).JoinWithFfkCustomer(static q => q.Where(static c => c.Region == "EU")).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Fk_ManyToOne_Filtered"), Benchmark]
	public int Fk_ManyToOne_Filtered_Frozen() {
		using var r = _manyToOneFilteredFrozen.ExecutePooled(_group);
		return r.Count;
	}
}

// ── Models: the generated FK join path, one model per resolver family ─────────────────────────────

[DataCache]
public partial class FfkCustomer {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)] public int Segment { get; set; }

	public string Region { get; set; } = string.Empty;
	public int Score { get; set; }
}

/// <summary>Forward many-to-one FK — the left-symmetric list index and <c>JoinWithFfkCustomer</c>.</summary>
[DataCache]
public partial class FfkOrder {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)] public int Group { get; set; }

	[DataCacheForeignKey<FfkCustomer>(DataCacheJoinType.ManyToOne)]
	public int CustomerId { get; set; }

	public int Score { get; set; }
}

/// <summary>Reverse one-to-one FK — the right-unique index and <c>FfkCustomer.JoinWithFfkProfile</c>.</summary>
[DataCache]
public partial class FfkProfile {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<FfkCustomer>(DataCacheJoinType.OneToOne)]
	public int CustomerId { get; set; }

	public string Bio { get; set; } = string.Empty;
}

/// <summary>Reverse one-to-many FK — the right-list index and <c>FfkOrder.JoinWithFfkLine</c>.</summary>
[DataCache]
public partial class FfkLine {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<FfkOrder>(DataCacheJoinType.OneToMany)]
	public int OrderId { get; set; }

	public int Qty { get; set; }
}

[DataCache]
public partial class FfkTag {
	[DataCacheKey] public int Id { get; set; }

	public string Name { get; set; } = string.Empty;
}

/// <summary>Collection FK — the symmetric collection index and <c>JoinWithFfkTag</c>.</summary>
[DataCache]
public partial class FfkDoc {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)] public int Bucket { get; set; }

	[DataCacheForeignKey<FfkTag>(DataCacheJoinType.ManyToOne)]
	public List<int> TagIds { get; set; } = [];
}

/// <summary>Struct comparer with ties, carried as a type parameter by both sort plans.</summary>
public readonly struct FfkCustomerByScoreTies : IComparer<FfkCustomer> {
	public int Compare(FfkCustomer? x, FfkCustomer? y) => ((x?.Score ?? 0) & 7).CompareTo((y?.Score ?? 0) & 7);
}

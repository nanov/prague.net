namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   Eager builder (baseline) vs <c>Build()</c> (replay) vs <c>BuildFrozen()</c>, shape by shape, on
///   the raw <see cref="InMemoryDataCache{TKey,TValue}" />. The first four categories are the shapes
///   the frozen planner binds to the point-lookup executor; the last three are deliberately
///   ineligible, so their <c>_Frozen</c> rows must sit on top of the <c>_Prepared</c> rows — the
///   fallback is the same replay. Every body executes pooled and disposes. Same data as
///   <see cref="PreparedQueryBenchmarks" />: 100k rows, list buckets of 1k, range windows of 1k codes.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrozenQueryBenchmarks {
	private const int N = 100_000;
	private const int Buckets = 100;

	private InMemoryDataCache<int, PqbItem> _items = null!;
	private CacheUniqueIndex<int, PqbItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqbItem, int> _codeRange = null!;

	private InMemoryDataCache<int, PqbOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PqbCustomer> _customers = null!;

	private PreparedQuery<NoArgs, PqbItem> _uniqueBoundPrepared = null!;
	private FrozenQuery<NoArgs, PqbItem> _uniqueBoundFrozen = null!;
	private PreparedQuery<int, PqbItem> _uniqueArgPrepared = null!;
	private FrozenQuery<int, PqbItem> _uniqueArgFrozen = null!;
	private PreparedQuery<int, PqbItem> _uniqueWherePrepared = null!;
	private FrozenQuery<int, PqbItem> _uniqueWhereFrozen = null!;
	private PreparedQuery<(int code, int min), PqbItem> _uniqueArgWherePrepared = null!;
	private FrozenQuery<(int code, int min), PqbItem> _uniqueArgWhereFrozen = null!;
	private PreparedQuery<int, PqbItem> _listWherePrepared = null!;
	private FrozenQuery<int, PqbItem> _listWhereFrozen = null!;
	private PreparedQuery<(int lo, int hi), PqbItem> _rangePrepared = null!;
	private FrozenQuery<(int lo, int hi), PqbItem> _rangeFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOnePrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFrozen = null!;

	// Arguments are fields, not constants, so no side gets a constant folded into the query.
	private int _code = 1000 + 42_042;
	private (int code, int min) _uniqueArgWhereArgs = (1000 + 42_042, 0);
	private int _group = 13;
	private (int lo, int hi) _rangeArgs = (1000 + 50_000, 1000 + 51_000);
	private int _customer = 7;

	[GlobalSetup]
	public void Setup() {
		_items = new InMemoryDataCache<int, PqbItem>();
		_byCode = _items.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _items.CacheRangeIndex<int>(static (_, v) => v.Code);
		var rng = new Random(1234);
		for (var i = 0; i < N; i++)
			_items.AddOrUpdate(i, new PqbItem { Id = i, Code = 1000 + i, Group = i % Buckets, Flag = i % 3 == 0, Score = rng.Next(int.MaxValue) });

		_orders = new InMemoryDataCache<int, PqbOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_customers = new InMemoryDataCache<int, PqbCustomer>();
		for (var c = 0; c < Buckets; c++)
			_customers.AddOrUpdate(c, new PqbCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < N; i++)
			_orders.AddOrUpdate(i, new PqbOrder { Id = i, CustomerId = i % Buckets, Qty = i % 13 });

		_uniqueBoundPrepared = _items.Prepare().UseIndex(_byCode, _code).Build();
		_uniqueBoundFrozen = _items.Prepare().UseIndex(_byCode, _code).BuildFrozen();
		_uniqueArgPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Build();
		_uniqueArgFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).BuildFrozen();
		_uniqueWherePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Where(static v => v.Flag).Build();
		_uniqueWhereFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Where(static v => v.Flag).BuildFrozen();
		_uniqueArgWherePrepared = _items.Prepare<int, PqbItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Score >= a.min).Build();
		_uniqueArgWhereFrozen = _items.Prepare<int, PqbItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Score >= a.min).BuildFrozen();
		_listWherePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		_listWhereFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen();
		_rangePrepared = _items.Prepare<int, PqbItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		_rangeFrozen = _items.Prepare<int, PqbItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		_joinOnePrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).Build();
		_joinOneFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).BuildFrozen();
	}

	// ── 1. unique lookup, value bound at build ────────────────────────────────────

	[BenchmarkCategory("Unique"), Benchmark(Baseline = true)]
	public int Unique_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_Prepared() {
		using var r = _uniqueBoundPrepared.ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_Frozen() {
		using var r = _uniqueBoundFrozen.ExecutePooled();
		return r.Count;
	}

	// ── 2. unique lookup, parameterized ───────────────────────────────────────────

	[BenchmarkCategory("UniqueArg"), Benchmark(Baseline = true)]
	public int UniqueArg_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("UniqueArg"), Benchmark]
	public int UniqueArg_Prepared() {
		using var r = _uniqueArgPrepared.ExecutePooled(_code);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArg"), Benchmark]
	public int UniqueArg_Frozen() {
		using var r = _uniqueArgFrozen.ExecutePooled(_code);
		return r.Count;
	}

	// ── 3. unique + constant Where ────────────────────────────────────────────────

	[BenchmarkCategory("UniqueWhere"), Benchmark(Baseline = true)]
	public int UniqueWhere_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).Where(static v => v.Flag).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("UniqueWhere"), Benchmark]
	public int UniqueWhere_Prepared() {
		using var r = _uniqueWherePrepared.ExecutePooled(_code);
		return r.Count;
	}

	[BenchmarkCategory("UniqueWhere"), Benchmark]
	public int UniqueWhere_Frozen() {
		using var r = _uniqueWhereFrozen.ExecutePooled(_code);
		return r.Count;
	}

	// ── 4. unique + parameterized Where ───────────────────────────────────────────

	// The eager twin is what a caller writes: the per-call argument captured by the lambda, which
	// Roslyn hoists into a display class allocated per call.
	private QueryResults<PqbItem> EagerUniqueArgWhere((int code, int min) a)
		=> _items.Query().UseIndex(_byCode, a.code).Where(v => v.Score >= a.min).ExecutePooled();

	[BenchmarkCategory("UniqueArgWhere"), Benchmark(Baseline = true)]
	public int UniqueArgWhere_Eager() {
		using var r = EagerUniqueArgWhere(_uniqueArgWhereArgs);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArgWhere"), Benchmark]
	public int UniqueArgWhere_Prepared() {
		using var r = _uniqueArgWherePrepared.ExecutePooled(_uniqueArgWhereArgs);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArgWhere"), Benchmark]
	public int UniqueArgWhere_Frozen() {
		using var r = _uniqueArgWhereFrozen.ExecutePooled(_uniqueArgWhereArgs);
		return r.Count;
	}

	// ── 5. list index + constant Where (not eligible: replay) ─────────────────────

	[BenchmarkCategory("ListWhere"), Benchmark(Baseline = true)]
	public int ListWhere_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Flag).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_Prepared() {
		using var r = _listWherePrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_Frozen() {
		using var r = _listWhereFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// ── 6. range, parameterized (not eligible: replay) ────────────────────────────

	[BenchmarkCategory("Range"), Benchmark(Baseline = true)]
	public int Range_Eager() {
		using var r = _items.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), _rangeArgs).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Range"), Benchmark]
	public int Range_Prepared() {
		using var r = _rangePrepared.ExecutePooled(_rangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("Range"), Benchmark]
	public int Range_Frozen() {
		using var r = _rangeFrozen.ExecutePooled(_rangeArgs);
		return r.Count;
	}

	// ── 7. JoinOne, parameterized (not eligible: replay) ──────────────────────────

	[BenchmarkCategory("JoinOne"), Benchmark(Baseline = true)]
	public int JoinOne_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinOne(_byCustomer, _customers).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOne"), Benchmark]
	public int JoinOne_Prepared() {
		using var r = _joinOnePrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinOne"), Benchmark]
	public int JoinOne_Frozen() {
		using var r = _joinOneFrozen.ExecutePooled(_customer);
		return r.Count;
	}
}

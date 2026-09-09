namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   Eager builder (baseline) vs <c>Build()</c> (replay) vs <c>BuildFrozen()</c>, shape by shape, on
///   the raw <see cref="InMemoryDataCache{TKey,TValue}" />. The first four categories are the shapes
///   the frozen planner binds to the point-lookup executor; the rest (list, range, join, <c>Match</c>,
///   optional-bounds range) are deliberately ineligible, so their <c>_Frozen</c> rows must sit on top of
///   the <c>_Prepared</c> rows — the fallback is the same replay. Every body executes pooled and disposes. Same data as
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
	private CacheKeyValueListIndex<int, PqbItem, int> _byTier = null!;
	private CacheRangeIndex<int, PqbItem, int> _codeRange = null!;

	private InMemoryDataCache<int, PqbOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PqbCustomer> _customers = null!;

	private PreparedQuery<NoArgs, PqbItem> _uniqueBoundPrepared = null!;
	private FrozenQuery<NoArgs, PqbItem> _uniqueBoundFrozen = null!;
	private FrozenQuery<int, PqbItem> _uniqueBoundIntArgsFrozen = null!;
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
	private PreparedQuery<(int mode, int group, int code), PqbItem> _matchPrepared = null!;
	private FrozenQuery<(int mode, int group, int code), PqbItem> _matchFrozen = null!;
	private PreparedQuery<(int? lo, int? hi), PqbItem> _optionalRangePrepared = null!;
	private FrozenQuery<(int? lo, int? hi), PqbItem> _optionalRangeFrozen = null!;

	// Stage 2 shapes.
	private PreparedQuery<int, PqbItem> _listTwoWheresPrepared = null!;
	private FrozenQuery<int, PqbItem> _listTwoWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listTwoWheresFrozenFixedOrder = null!;
	private PreparedQuery<int, PqbItem> _listThreeWheresPrepared = null!;
	private FrozenQuery<int, PqbItem> _listThreeWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listThreeWheresFrozenFixedOrder = null!;
	private PreparedQuery<(int group, int min, int max), PqbItem> _listTwoArgWheresPrepared = null!;
	private FrozenQuery<(int group, int min, int max), PqbItem> _listTwoArgWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listWhereFrozenNoHints = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _listListPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListFrozen = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListFrozenEagerIntersection = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListFrozenReorder = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _listListReversedPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListReversedFrozen = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListReversedFrozenEagerIntersection = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListReversedFrozenReorder = null!;
	private PreparedQuery<(int group, int lo, int hi), PqbItem> _listRangePrepared = null!;
	private FrozenQuery<(int group, int lo, int hi), PqbItem> _listRangeFrozen = null!;

	// Arguments are fields, not constants, so no side gets a constant folded into the query.
	private int _code = 1000 + 42_042;
	private (int code, int min) _uniqueArgWhereArgs = (1000 + 42_042, 0);
	private int _group = 13;
	private (int lo, int hi) _rangeArgs = (1000 + 50_000, 1000 + 51_000);
	private int _customer = 7;
	private (int mode, int group, int code) _matchArgs = (2, 13, 1000 + 13 + 100 * 420);
	private (int? lo, int? hi) _optionalRangeArgs = (1000 + 50_000, 1000 + 51_000);
	private (int group, int min, int max) _listTwoArgWheresArgs = (13, 20_000, 80_000);
	private (int group, int tier) _listListArgs = (13, 13);
	private (int group, int lo, int hi) _listRangeArgs = (13, 1000 + 20_000, 1000 + 80_000);

	[GlobalSetup]
	public void Setup() {
		_items = new InMemoryDataCache<int, PqbItem>();
		_byCode = _items.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Tier);
		_codeRange = _items.CacheRangeIndex<int>(static (_, v) => v.Code);
		var rng = new Random(1234);
		for (var i = 0; i < N; i++)
			_items.AddOrUpdate(i, new PqbItem { Id = i, Code = 1000 + i, Group = i % Buckets, Tier = i % (Buckets * 10), Flag = i % 3 == 0, Score = rng.Next(int.MaxValue) });

		_orders = new InMemoryDataCache<int, PqbOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_customers = new InMemoryDataCache<int, PqbCustomer>();
		for (var c = 0; c < Buckets; c++)
			_customers.AddOrUpdate(c, new PqbCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < N; i++)
			_orders.AddOrUpdate(i, new PqbOrder { Id = i, CustomerId = i % Buckets, Qty = i % 13 });

		_uniqueBoundPrepared = _items.Prepare().UseIndex(_byCode, _code).Build();
		_uniqueBoundFrozen = _items.Prepare().UseIndex(_byCode, _code).BuildFrozen();
		_uniqueBoundIntArgsFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, _code).BuildFrozen();
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
		_matchPrepared = _items.Prepare<int, PqbItem, (int mode, int group, int code)>().Match(static a => a.mode, m => m
			.Case(0, b => b.UseIndex(_byCode, static a => a.code))
			.Case(1, b => b.UseIndex(_byGroup, static a => a.group))
			.Case(2, b => b.UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag))).Build();
		_matchFrozen = _items.Prepare<int, PqbItem, (int mode, int group, int code)>().Match(static a => a.mode, m => m
			.Case(0, b => b.UseIndex(_byCode, static a => a.code))
			.Case(1, b => b.UseIndex(_byGroup, static a => a.group))
			.Case(2, b => b.UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag))).BuildFrozen();
		_optionalRangePrepared = _items.Prepare<int, PqbItem, (int? lo, int? hi)>().UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false).Build();
		_optionalRangeFrozen = _items.Prepare<int, PqbItem, (int? lo, int? hi)>().UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false).BuildFrozen();

		var fixedOrder = new FrozenOptions { AdaptiveFilterOrdering = false };
		var noHints = new FrozenOptions { CapacityHints = false };
		var eagerIntersection = new FrozenOptions { AdaptiveIntersection = false };
		var reorder = new FrozenOptions { ReorderIndexNarrowers = true };
		_listTwoWheresPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).Build();
		_listTwoWheresFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).BuildFrozen();
		_listTwoWheresFrozenFixedOrder = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).BuildFrozen(fixedOrder);
		_listThreeWheresPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).Build();
		_listThreeWheresFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).BuildFrozen();
		_listThreeWheresFrozenFixedOrder = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).BuildFrozen(fixedOrder);
		_listTwoArgWheresPrepared = _items.Prepare<int, PqbItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min).Where(static (v, a) => v.Id <= a.max).Build();
		_listTwoArgWheresFrozen = _items.Prepare<int, PqbItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min).Where(static (v, a) => v.Id <= a.max).BuildFrozen();
		_listWhereFrozenNoHints = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen(noHints);
		_listListPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build();
		_listListFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		_listListFrozenEagerIntersection = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(eagerIntersection);
		_listListFrozenReorder = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(reorder);
		_listListReversedPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).Build();
		_listListReversedFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		_listListReversedFrozenEagerIntersection = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen(eagerIntersection);
		_listListReversedFrozenReorder = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen(reorder);
		_listRangePrepared = _items.Prepare<int, PqbItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		_listRangeFrozen = _items.Prepare<int, PqbItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
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

	// Optimization 5 probes (the stage-1 "bound is ~8 ns slower than parameterized" note): the same
	// bound plan called through the virtual terminal directly instead of the NoArgs extension, and a
	// bound plan whose TArgs is int rather than the empty NoArgs struct.
	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_FrozenDirectCall() {
		using var r = _uniqueBoundFrozen.ExecutePooled(default(NoArgs));
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_FrozenBoundIntArgs() {
		using var r = _uniqueBoundIntArgsFrozen.ExecutePooled(0);
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

	// Stage 2, optimization 2 control: the same frozen plan with capacity hints off.
	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_FrozenNoHints() {
		using var r = _listWhereFrozenNoHints.ExecutePooled(_group);
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

	// ── 8. Match, three parameterized arms (not eligible: replay) ─────────────────

	private QueryResults<PqbItem> EagerMatch((int mode, int group, int code) a) {
		var q = _items.Query();
		switch (a.mode) {
			case 0: q = q.UseIndex(_byCode, a.code); break;
			case 1: q = q.UseIndex(_byGroup, a.group); break;
			case 2: q = q.UseIndex(_byGroup, a.group).Where(static v => v.Flag); break;
		}

		return q.ExecutePooled();
	}

	[BenchmarkCategory("Match"), Benchmark(Baseline = true)]
	public int Match_Eager() {
		using var r = EagerMatch(_matchArgs);
		return r.Count;
	}

	[BenchmarkCategory("Match"), Benchmark]
	public int Match_Prepared() {
		using var r = _matchPrepared.ExecutePooled(_matchArgs);
		return r.Count;
	}

	[BenchmarkCategory("Match"), Benchmark]
	public int Match_Frozen() {
		using var r = _matchFrozen.ExecutePooled(_matchArgs);
		return r.Count;
	}

	// ── 9. Optional-bounds range, both bounds (not eligible: replay) ──────────────

	private QueryResults<PqbItem> EagerOptionalRange((int? lo, int? hi) a) {
		var q = _items.Query();
		if (a.lo is not null && a.hi is not null) q = q.UseIndex(_codeRange, static (rb, b) => rb.Gte(b.lo!.Value).Lt(b.hi!.Value), a);
		else if (a.lo is not null) q = q.UseIndex(_codeRange, static (rb, lo) => rb.Gte(lo), a.lo.Value);
		else if (a.hi is not null) q = q.UseIndex(_codeRange, static (rb, hi) => rb.Lt(hi), a.hi.Value);
		return q.ExecutePooled();
	}

	[BenchmarkCategory("OptionalRange"), Benchmark(Baseline = true)]
	public int OptionalRange_Eager() {
		using var r = EagerOptionalRange(_optionalRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("OptionalRange"), Benchmark]
	public int OptionalRange_Prepared() {
		using var r = _optionalRangePrepared.ExecutePooled(_optionalRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("OptionalRange"), Benchmark]
	public int OptionalRange_Frozen() {
		using var r = _optionalRangeFrozen.ExecutePooled(_optionalRangeArgs);
		return r.Count;
	}

	// ── Stage 2 ───────────────────────────────────────────────────────────────────
	// Optimization 1 (fused filters + adaptive order). Bucket of 1k rows. Flag passes a third,
	// Score % 10 a tenth, Score % 100 a hundredth; Id >= 0 passes everything. The eager `&&` order is
	// the declared one; the frozen plan learns to ask the most selective predicate first. The
	// `_FixedOrder` rows isolate fusion from ordering.

	// 10. list + 2 constant Wheres, the more selective declared last
	[BenchmarkCategory("ListTwoWheres"), Benchmark(Baseline = true)]
	public int ListTwoWheres_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_Prepared() {
		using var r = _listTwoWheresPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_Frozen() {
		using var r = _listTwoWheresFrozen.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_FrozenFixedOrder() {
		using var r = _listTwoWheresFrozenFixedOrder.ExecutePooled(_group);
		return r.Count;
	}

	// 11. list + 3 constant Wheres, the most selective declared LAST
	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark(Baseline = true)]
	public int ListThreeWheresSelectiveLast_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_Prepared() {
		using var r = _listThreeWheresPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_Frozen() {
		using var r = _listThreeWheresFrozen.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_FrozenFixedOrder() {
		using var r = _listThreeWheresFrozenFixedOrder.ExecutePooled(_group);
		return r.Count;
	}

	// 12. list + 2 parameterized Wheres. The eager twin is what a caller writes: two lambdas capturing
	// the per-call arguments (a display class per call) plus the core's `&&` closure.
	private QueryResults<PqbItem> EagerListTwoArgWheres((int group, int min, int max) a)
		=> _items.Query().UseIndex(_byGroup, a.group).Where(v => v.Id >= a.min).Where(v => v.Id <= a.max).ExecutePooled();

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark(Baseline = true)]
	public int ListTwoArgWheres_Eager() {
		using var r = EagerListTwoArgWheres(_listTwoArgWheresArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark]
	public int ListTwoArgWheres_Prepared() {
		using var r = _listTwoArgWheresPrepared.ExecutePooled(_listTwoArgWheresArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark]
	public int ListTwoArgWheres_Frozen() {
		using var r = _listTwoArgWheresFrozen.ExecutePooled(_listTwoArgWheresArgs);
		return r.Count;
	}

	// Optimizations 2–4 on two list steps. Group buckets hold 1k rows, Tier buckets 100; group 13 ∩
	// tier 13 is the 100 tier-13 rows. Declared large-then-small (13) and small-then-large (14).
	// `_Frozen` is the default plan (capacity hints + adaptive intersection, optimizations 2 and 3),
	// `_FrozenEagerIntersection` the same with adaptive intersection off (hints only), `_FrozenReorder`
	// optimization 4 (seeds from the smaller bucket whatever the declared order).

	// 13. list(1k) ∩ list(100)
	[BenchmarkCategory("ListList"), Benchmark(Baseline = true)]
	public int ListList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listListArgs.group).UseIndex(_byTier, _listListArgs.tier).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_Prepared() {
		using var r = _listListPrepared.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_Frozen() {
		using var r = _listListFrozen.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_FrozenEagerIntersection() {
		using var r = _listListFrozenEagerIntersection.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_FrozenReorder() {
		using var r = _listListFrozenReorder.ExecutePooled(_listListArgs);
		return r.Count;
	}

	// 14. list(100) ∩ list(1k) — the declared order is already the best one
	[BenchmarkCategory("ListListReversed"), Benchmark(Baseline = true)]
	public int ListListReversed_Eager() {
		using var r = _items.Query().UseIndex(_byTier, _listListArgs.tier).UseIndex(_byGroup, _listListArgs.group).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_Prepared() {
		using var r = _listListReversedPrepared.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_Frozen() {
		using var r = _listListReversedFrozen.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_FrozenEagerIntersection() {
		using var r = _listListReversedFrozenEagerIntersection.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_FrozenReorder() {
		using var r = _listListReversedFrozenReorder.ExecutePooled(_listListArgs);
		return r.Count;
	}

	// 15. list(1k) ∩ range(60k codes) — a range step is neither an equality step nor hint-safe: the frozen plan is the plain replay
	[BenchmarkCategory("ListRange"), Benchmark(Baseline = true)]
	public int ListRange_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listRangeArgs.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), _listRangeArgs).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListRange"), Benchmark]
	public int ListRange_Prepared() {
		using var r = _listRangePrepared.ExecutePooled(_listRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListRange"), Benchmark]
	public int ListRange_Frozen() {
		using var r = _listRangeFrozen.ExecutePooled(_listRangeArgs);
		return r.Count;
	}
}

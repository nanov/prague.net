namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   Eager builder vs prepared query, shape by shape, on the raw <see cref="InMemoryDataCache{TKey,TValue}" />
///   (no codegen). Each category pairs the eager spelling a caller would write (baseline) with the
///   prepared twin built once in <see cref="Setup" /> and executed with the same arguments. Every body
///   executes pooled and disposes the result, so the per-op figure is the execution cost alone.
///
///   Expected: ratios within noise of 1.00 and equal allocations, except where the eager side needs a
///   closure to carry the per-call argument (parameterized <c>Where</c>, <c>If</c> with a captured
///   value) — there the prepared side binds the argument into a per-thread pooled predicate box and
///   allocates less. <c>Match</c> (three parameterized arms vs the eager <c>switch</c>) and the
///   optional-bounds range (vs the eager four-arm <c>if</c>) replay the selected arm's eager work plus
///   the tag / bound selector calls. <c>Build_FourNarrowers</c> measures the one-time cost of building a command.
///
///   100k rows, list buckets of 1k (Group = Id % 100), range windows of ~1k codes.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PreparedQueryBenchmarks {
	private const int N = 100_000;
	private const int Buckets = 100;

	private InMemoryDataCache<int, PqbItem> _items = null!;
	private CacheUniqueIndex<int, PqbItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqbItem, int> _codeRange = null!;

	private InMemoryDataCache<int, PqbOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PqbCustomer> _customers = null!;

	private PreparedQuery<int, PqbItem> _uniquePrepared = null!;
	private PreparedQuery<int, PqbItem> _listWherePrepared = null!;
	private PreparedQuery<(int group, int min), PqbItem> _listArgWherePrepared = null!;
	private PreparedQuery<(int lo, int hi), PqbItem> _rangePrepared = null!;
	private PreparedQuery<(int g1, int g2), PqbItem> _orPrepared = null!;
	private PreparedQuery<(bool cond, int group, int code), PqbItem> _ifPrepared = null!;
	private PreparedQuery<(int mode, int group, int code), PqbItem> _matchPrepared = null!;
	private PreparedQuery<(int? lo, int? hi), PqbItem> _optionalRangePrepared = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOnePrepared = null!;
	private PreparedQuery<int, PqbItem> _sortBoundedPrepared = null!;

	// Arguments are fields, not constants, so neither side gets a constant folded into the query.
	private int _code = 1000 + 42_042;
	private int _group = 13;
	private (int group, int min) _listArgWhereArgs = (13, 50_000);
	private (int lo, int hi) _rangeArgs = (1000 + 50_000, 1000 + 51_000);
	private (int g1, int g2) _orArgs = (13, 41);
	private (bool cond, int group, int code) _ifTaken = (true, 13, 1000 + 13 + 100 * 420);
	private (bool cond, int group, int code) _ifSkipped = (false, 13, 1000 + 13 + 100 * 420);
	private (int mode, int group, int code) _matchArgs = (2, 13, 1000 + 13 + 100 * 420);
	private (int? lo, int? hi) _optionalRangeArgs = (1000 + 50_000, 1000 + 51_000);
	private int _customer = 7;
	private int _take = 20;

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

		_uniquePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Build();
		_listWherePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		_listArgWherePrepared = _items.Prepare<int, PqbItem, (int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.Build();
		_rangePrepared = _items.Prepare<int, PqbItem, (int lo, int hi)>()
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi))
			.Build();
		_orPrepared = _items.Prepare<int, PqbItem, (int g1, int g2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Build();
		_ifPrepared = _items.Prepare<int, PqbItem, (bool cond, int group, int code)>()
			.UseIndex(_byGroup, static a => a.group)
			.If(static a => a.cond, b => b.UseIndex(_byCode, static a => a.code))
			.Build();
		_matchPrepared = _items.Prepare<int, PqbItem, (int mode, int group, int code)>()
			.Match(static a => a.mode, m => m
				.Case(0, b => b.UseIndex(_byCode, static a => a.code))
				.Case(1, b => b.UseIndex(_byGroup, static a => a.group))
				.Case(2, b => b.UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag)))
			.Build();
		_optionalRangePrepared = _items.Prepare<int, PqbItem, (int? lo, int? hi)>()
			.UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false)
			.Build();
		_joinOnePrepared = _orders.Prepare<int, PqbOrder, int>()
			.UseIndex(_byCustomer, static c => c)
			.JoinOne(_byCustomer, _customers)
			.Build();
		_sortBoundedPrepared = _items.Prepare<int, PqbItem, int>()
			.UseIndex(_byGroup, static g => g)
			.SortBounded(new PqbByScore())
			.Build();
	}

	// ── 1. unique lookup ──────────────────────────────────────────────────────────

	[BenchmarkCategory("Unique"), Benchmark(Baseline = true)]
	public int Unique_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_Prepared() {
		using var r = _uniquePrepared.ExecutePooled(_code);
		return r.Count;
	}

	// ── 2. list index + constant Where ────────────────────────────────────────────

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

	// ── 3. list index + parameterized Where ───────────────────────────────────────

	// The eager twin is what a caller writes: the per-call argument captured by the lambda, which
	// Roslyn hoists into a display class allocated per call.
	private QueryResults<PqbItem> EagerListArgWhere((int group, int min) a)
		=> _items.Query().UseIndex(_byGroup, a.group).Where(v => v.Id >= a.min).ExecutePooled();

	[BenchmarkCategory("ListArgWhere"), Benchmark(Baseline = true)]
	public int ListArgWhere_Eager() {
		using var r = EagerListArgWhere(_listArgWhereArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListArgWhere"), Benchmark]
	public int ListArgWhere_Prepared() {
		using var r = _listArgWherePrepared.ExecutePooled(_listArgWhereArgs);
		return r.Count;
	}

	// ── 4. range, parameterized ───────────────────────────────────────────────────

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

	// ── 5. Or with two parameterized list branches ────────────────────────────────

	[BenchmarkCategory("Or"), Benchmark(Baseline = true)]
	public int Or_Eager() {
		var state = (idx: _byGroup, g1: _orArgs.g1, g2: _orArgs.g2);
		using var r = _items.Query().Or(static (b, s) => b.UseIndex(s.idx, s.g1), static (b, s) => b.UseIndex(s.idx, s.g2), state).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Or"), Benchmark]
	public int Or_Prepared() {
		using var r = _orPrepared.ExecutePooled(_orArgs);
		return r.Count;
	}

	// ── 6. If taken / If skipped ──────────────────────────────────────────────────

	private QueryResults<PqbItem> EagerIf((bool cond, int group, int code) a) {
		var q = _items.Query().UseIndex(_byGroup, a.group);
		if (a.cond) q = q.UseIndex(_byCode, a.code);
		return q.ExecutePooled();
	}

	[BenchmarkCategory("IfTaken"), Benchmark(Baseline = true)]
	public int IfTaken_Eager() {
		using var r = EagerIf(_ifTaken);
		return r.Count;
	}

	[BenchmarkCategory("IfTaken"), Benchmark]
	public int IfTaken_Prepared() {
		using var r = _ifPrepared.ExecutePooled(_ifTaken);
		return r.Count;
	}

	[BenchmarkCategory("IfSkipped"), Benchmark(Baseline = true)]
	public int IfSkipped_Eager() {
		using var r = EagerIf(_ifSkipped);
		return r.Count;
	}

	[BenchmarkCategory("IfSkipped"), Benchmark]
	public int IfSkipped_Prepared() {
		using var r = _ifPrepared.ExecutePooled(_ifSkipped);
		return r.Count;
	}

	// ── 6b. Match: three parameterized arms, the third selected ───────────────────

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

	// ── 6c. Optional-bounds range, both bounds present ────────────────────────────

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

	// ── 7. JoinOne, parameterized ─────────────────────────────────────────────────

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

	// ── 8. SortBounded page over a list index, struct comparer ────────────────────

	[BenchmarkCategory("SortBounded"), Benchmark(Baseline = true)]
	public int SortBounded_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).SortBounded(new PqbByScore()).ExecutePooled(0, _take);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded"), Benchmark]
	public int SortBounded_Prepared() {
		using var r = _sortBoundedPrepared.ExecutePooled(_group, 0, _take);
		return r.Count;
	}

	// ── build cost: the one-time allocation of a 4-narrower parameterized command ─

	[BenchmarkCategory("Build"), Benchmark]
	public PreparedQuery<(int group, int lo, int hi, int min), PqbItem> Build_FourNarrowers()
		=> _items.Prepare<int, PqbItem, (int group, int lo, int hi, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi))
			.Where(static (v, a) => v.Id >= a.min)
			.Where(static v => v.Flag)
			.Build();
}

public sealed class PqbItem : ICacheEquatable<PqbItem>, ICacheClonable<PqbItem> {
	public int Id { get; init; }
	public int Code { get; init; }
	public int Group { get; init; }
	public int Tier { get; init; }
	public int Band { get; init; }
	public int Lane { get; init; }
	public bool Flag { get; init; }
	public int Score { get; init; }

	public bool CacheEquals(PqbItem? other)
		=> other is not null && other.Id == Id && other.Code == Code && other.Group == Group && other.Tier == Tier && other.Band == Band && other.Lane == Lane && other.Flag == Flag && other.Score == Score;

	public int CacheGetHashCode() => HashCode.Combine(Id, Code, Group, Tier, Band, Lane, Flag, Score);

	public PqbItem Clone() => new() { Id = Id, Code = Code, Group = Group, Tier = Tier, Band = Band, Lane = Lane, Flag = Flag, Score = Score };
}

public sealed class PqbOrder : ICacheEquatable<PqbOrder>, ICacheClonable<PqbOrder> {
	public int Id { get; init; }
	public int CustomerId { get; init; }
	public int Qty { get; init; }

	public bool CacheEquals(PqbOrder? other) => other is not null && other.Id == Id && other.CustomerId == CustomerId && other.Qty == Qty;

	public int CacheGetHashCode() => HashCode.Combine(Id, CustomerId, Qty);

	public PqbOrder Clone() => new() { Id = Id, CustomerId = CustomerId, Qty = Qty };
}

public sealed class PqbCustomer : ICacheEquatable<PqbCustomer>, ICacheClonable<PqbCustomer> {
	public int Id { get; init; }
	public string Region { get; init; } = "";

	public bool CacheEquals(PqbCustomer? other) => other is not null && other.Id == Id && other.Region == Region;

	public int CacheGetHashCode() => HashCode.Combine(Id, Region);

	public PqbCustomer Clone() => new() { Id = Id, Region = Region };
}

// Struct comparer: the sort plans carry it as a type parameter, so neither side boxes it.
public readonly struct PqbByScore : IComparer<PqbItem> {
	public int Compare(PqbItem? x, PqbItem? y) => (x?.Score ?? 0).CompareTo(y?.Score ?? 0);
}

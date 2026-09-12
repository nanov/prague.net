namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryDifferentialTests;

// Step-2 narrowers (multi-value, range, key-set, last-updated): the same eager-vs-prepared
// differential as PreparedQueryDifferentialTests, one test per new overload. Items carry explicit
// timestamps so the last-updated shapes have something to cut on.
[TestFixture]
public class PreparedQueryNarrowerDifferentialTests {
	private const int N = 240;
	private const long BaseMs = 1_700_000_000_000;

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;
	private GlobalLastUpdated _global = null!;

	// The eager core only reads .Index off the global interface; the rest is not on the query path.
	private sealed class GlobalLastUpdated(LastUpdatedIndex<int> index) : IDataCacheGlobalLastUpdateIndex<int> {
		public LastUpdatedIndex<int> Index => index;
		public bool TryGetMin(out long timestampMs, out int key) => throw new NotSupportedException();
		public bool TryGetMax(out long timestampMs, out int key) => throw new NotSupportedException();
		public bool TryGetMin(out long timestampMs) => throw new NotSupportedException();
		public bool TryGetMax(out long timestampMs) => throw new NotSupportedException();
		public int GetEntitiesCount(int key) => throw new NotSupportedException();
		public bool TryGetMax(ReadOnlySpan<int> keys, out long timestampMs) => throw new NotSupportedException();
	}

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		_global = new GlobalLastUpdated(_lastUpdated);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i), Ms(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static long Ms(int i) => BaseMs + i * 1000L;

	private static DateTimeOffset Dto(int i) => DateTimeOffset.FromUnixTimeMilliseconds(Ms(i));

	private static DateTime Dt(int i) => Dto(i).UtcDateTime;

	// ── Multi-value: unique ───────────────────────────────────────────────────────

	[TestCase(new int[0])]
	[TestCase(new[] { 1042 })]
	[TestCase(new[] { 1000, 1042, 1239, -1 })]
	public void UniqueIn_BoundArray_LikeEager(int[] codes) {
		var prepared = _cache.Prepare().UseIndex(_byCode, codes).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, codes).Execute(), prepared.Execute());
	}

	[Test]
	public void UniqueIn_BoundMemory_LikeEager() {
		var codes = new[] { 0, 1001, 1002, 1003, 1005, 9999 }.AsMemory(1, 3);
		var prepared = _cache.Prepare().UseIndex(_byCode, codes).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, codes.Span).Execute(), prepared.Execute());
	}

	[Test]
	public void UniqueIn_Parameterized_ReusedAcrossArgSets_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).Build();
		foreach (var codes in new[] { Array.Empty<int>(), [1007], [1000, 1100, 1200], [-1, -2] })
			AssertSame(_cache.Query().UseIndex(_byCode, codes).Execute(), prepared.Execute(codes));
	}

	[Test]
	public void UniqueIn_AfterListIndex_IntersectsLikeEager() {
		// Non-first position exercises the retain / multi-value prune paths of the eager core.
		var codes = new[] { 1000, 1007, 1008, 1014, 1021 }; // group 0 holds 1000, 1007, 1014, 1021
		var prepared = _cache.Prepare().UseIndex(_byGroup, 0).UseIndex(_byCode, codes).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 0).UseIndex(_byCode, codes).Execute(), prepared.Execute());

		var preparedEmpty = _cache.Prepare().UseIndex(_byGroup, 0).UseIndex(_byCode, Array.Empty<int>()).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 0).UseIndex(_byCode, ReadOnlySpan<int>.Empty).Execute(), preparedEmpty.Execute());
	}

	// ── Multi-value: list ─────────────────────────────────────────────────────────

	[TestCase(new int[0])]
	[TestCase(new[] { 3 })]
	[TestCase(new[] { 0, 3, 6, 42 })]
	public void ListIn_BoundArray_LikeEager(int[] groups) {
		var prepared = _cache.Prepare().UseIndex(_byGroup, groups).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, groups).Execute(), prepared.Execute());
	}

	[Test]
	public void ListIn_BoundMemory_WithWhere_LikeEager() {
		var groups = new[] { 1, 5 }.AsMemory();
		var prepared = _cache.Prepare().UseIndex(_byGroup, groups).Where(static v => v.Flag).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, groups.Span).Where(static v => v.Flag).Execute(), prepared.Execute());
	}

	[Test]
	public void ListIn_Parameterized_ReusedAcrossArgSets_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (ReadOnlyMemory<int> groups, int skip)>().UseIndex(_byGroup, static a => a.groups).Build();
		foreach (var groups in new[] { Array.Empty<int>(), [2], [1, 2, 3], [99] }) {
			var args = (groups: (ReadOnlyMemory<int>)groups, skip: groups.Length);
			AssertSame(_cache.Query().UseIndex(_byGroup, groups).Execute(skip: args.skip, take: 20), prepared.Execute(args, skip: args.skip, take: 20));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byGroup, groups).Count()));
		}
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(3)]
	public void ListIn_ProjectedForeignValues_LikeEager(int count) {
		// ReadOnlyMemory<TOtherValue> is spelled out: TOtherValue is inferred from it, not from the lambda,
		// exactly as the eager ReadOnlySpan<TOtherValue> overload requires.
		ReadOnlyMemory<PqItem> foreign = new[] { new PqItem { Group = 4 }, new PqItem { Group = 6 }, new PqItem { Group = 4 } }.AsMemory(0, count);
		var prepared = _cache.Prepare().UseIndex(_byGroup, foreign, static o => o.Group).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, foreign.Span, static o => o.Group).Execute(), prepared.Execute());

		var preparedSecond = _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, foreign, static o => o.Group).Build();
		AssertSame(_cache.Query().UseIndex(_flagged).UseIndex(_byGroup, foreign.Span, static o => o.Group).Execute(), preparedSecond.Execute());
	}

	// ── Range ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Range_BoundBounds_EveryOperatorCombination_LikeEager() {
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1100)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1100)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Lte(1010)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Lte(1010)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1010)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(1010)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1060)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1060)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1060)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1060)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1050).Lte(1060)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1050).Lte(1060)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1050).Lt(1060)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1050).Lt(1060)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1060).Gt(1050)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(1060).Gt(1050)).Build().Execute());
	}

	[Test]
	public void Range_BoundEmptyAndOutOfRange_LikeEager() {
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1060).Lt(1061)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1060).Lt(1061)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(5000)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(5000)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Lt(0)).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(0)).Build().Execute());
	}

	[Test]
	public void Range_Parameterized_ReusedAcrossArgSets_LikeEager() {
		var between = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		var atLeast = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo)).Build();
		var atMost = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Lte(a.hi)).Build();

		foreach (var args in new[] { (lo: 1000, hi: 1010), (lo: 1100, hi: 1105), (lo: 1230, hi: 1300), (lo: 1050, hi: 1050), (lo: 2000, hi: 3000) }) {
			AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args).Execute(), between.Execute(args));
			AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo), args).Execute(), atLeast.Execute(args));
			AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Lte(a.hi), args).Execute(), atMost.Execute(args));
			Assert.That(between.Count(args), Is.EqualTo(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args).Count()));
		}
	}

	[Test]
	public void Range_AfterAndBeforeOtherIndexes_IntersectsLikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1150))
			.Where(static v => v.Flag)
			.Build();
		var preparedReversed = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1150))
			.UseIndex(_byGroup, static g => g)
			.Where(static v => v.Flag)
			.Build();
		for (var group = 0; group < 7; group++) {
			var eager = _cache.Query().UseIndex(_byGroup, group).UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1150)).Where(static v => v.Flag);
			AssertSame(eager.Execute(), prepared.Execute(group));
			AssertSame(
				_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1150)).UseIndex(_byGroup, group).Where(static v => v.Flag).Execute(),
				preparedReversed.Execute(group));
		}
	}

	// ── Key-set ───────────────────────────────────────────────────────────────────

	[Test]
	public void KeySet_Alone_AndAfterListIndex_LikeEager() {
		AssertSame(_cache.Query().UseIndex(_flagged).Execute(), _cache.Prepare().UseIndex(_flagged).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_flagged).Execute(),
			_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_flagged).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_flagged).UseIndex(_byGroup, 3).Execute(),
			_cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, 3).Build().Execute());
	}

	// ── Last-updated: raw LastUpdatedIndex<TKey> ──────────────────────────────────

	[TestCase(-1)]
	[TestCase(0)]
	[TestCase(100)]
	[TestCase(N - 1)]
	[TestCase(N + 5)]
	public void LastUpdated_After_AllTimeTypes_LikeEager(int i) {
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Ms(i)).Execute(), _cache.Prepare().UseIndex(_lastUpdated, Ms(i)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Dt(i)).Execute(), _cache.Prepare().UseIndex(_lastUpdated, Dt(i)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Dto(i)).Execute(), _cache.Prepare().UseIndex(_lastUpdated, Dto(i)).Build().Execute());
	}

	[TestCase(10, 20)]
	[TestCase(0, 0)]
	[TestCase(50, 40)]
	[TestCase(-10, 5)]
	[TestCase(230, N + 10)]
	public void LastUpdated_Between_AllTimeTypes_LikeEager(int from, int to) {
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Ms(from), Ms(to)).Execute(),
			_cache.Prepare().UseIndex(_lastUpdated, Ms(from), Ms(to)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Dt(from), Dt(to)).Execute(),
			_cache.Prepare().UseIndex(_lastUpdated, Dt(from), Dt(to)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Dto(from), Dto(to)).Execute(),
			_cache.Prepare().UseIndex(_lastUpdated, Dto(from), Dto(to)).Build().Execute());
	}

	[Test]
	public void LastUpdated_Parameterized_AfterAndBetween_LikeEager() {
		var after = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from).Build();
		var between = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from, static a => a.to).Build();
		foreach (var (f, t) in new[] { (0, 10), (100, 120), (200, 300), (30, 20) }) {
			var args = (from: Ms(f), to: Ms(t));
			AssertSame(_cache.Query().UseIndex(_lastUpdated, args.from).Execute(), after.Execute(args));
			AssertSame(_cache.Query().UseIndex(_lastUpdated, args.from, args.to).Execute(), between.Execute(args));
		}
	}

	[Test]
	public void LastUpdated_AfterListIndex_IntersectsLikeEager() {
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100)).Execute(),
			_cache.Prepare().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100), Ms(150)).Execute(),
			_cache.Prepare().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100), Ms(150)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Ms(100)).UseIndex(_byGroup, 2).Execute(),
			_cache.Prepare().UseIndex(_lastUpdated, Ms(100)).UseIndex(_byGroup, 2).Build().Execute());
	}

	// ── Last-updated: IDataCacheGlobalLastUpdateIndex<TKey> ───────────────────────

	[TestCase(-1)]
	[TestCase(100)]
	[TestCase(N + 5)]
	public void GlobalLastUpdated_After_AllTimeTypes_LikeEager(int i) {
		AssertSame(_cache.Query().UseIndex(_global, Ms(i)).Execute(), _cache.Prepare().UseIndex(_global, Ms(i)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_global, Dt(i)).Execute(), _cache.Prepare().UseIndex(_global, Dt(i)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_global, Dto(i)).Execute(), _cache.Prepare().UseIndex(_global, Dto(i)).Build().Execute());
	}

	[TestCase(10, 20)]
	[TestCase(50, 40)]
	[TestCase(230, N + 10)]
	public void GlobalLastUpdated_Between_AllTimeTypes_LikeEager(int from, int to) {
		AssertSame(_cache.Query().UseIndex(_global, Ms(from), Ms(to)).Execute(),
			_cache.Prepare().UseIndex(_global, Ms(from), Ms(to)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_global, Dt(from), Dt(to)).Execute(),
			_cache.Prepare().UseIndex(_global, Dt(from), Dt(to)).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_global, Dto(from), Dto(to)).Execute(),
			_cache.Prepare().UseIndex(_global, Dto(from), Dto(to)).Build().Execute());
	}

	[Test]
	public void GlobalLastUpdated_Parameterized_AfterAndBetween_LikeEager() {
		var after = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_global, static a => a.from).Build();
		var between = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_global, static a => a.from, static a => a.to).Build();
		foreach (var (f, t) in new[] { (0, 10), (100, 120), (30, 20) }) {
			var args = (from: Ms(f), to: Ms(t));
			AssertSame(_cache.Query().UseIndex(_global, args.from).Execute(), after.Execute(args));
			AssertSame(_cache.Query().UseIndex(_global, args.from, args.to).Execute(), between.Execute(args));
		}
	}

	// ── Reuse across mutations ────────────────────────────────────────────────────

	[Test]
	public void Reuse_RangeAndLastUpdated_TrackTheLiveCache() {
		var range = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi)).Build();
		var recent = _cache.Prepare<int, PqItem, long>().UseIndex(_lastUpdated, static ms => ms).Build();
		var args = (lo: 1000, hi: 1020);

		AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), args).Execute(), range.Execute(args));
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Ms(N - 3)).Execute(), recent.Execute(Ms(N - 3)));

		_cache.Remove(5);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 1010, Group = 0 }, Ms(N + 1)); // duplicate code, newest row
		_cache.AddOrUpdate(7, new PqItem { Id = 7, Code = 1007, Group = 0 }, Ms(N + 2));           // re-touched, moves in time

		AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), args).Execute(), range.Execute(args));
		AssertSame(_cache.Query().UseIndex(_lastUpdated, Ms(N - 3)).Execute(), recent.Execute(Ms(N - 3)));
	}
}

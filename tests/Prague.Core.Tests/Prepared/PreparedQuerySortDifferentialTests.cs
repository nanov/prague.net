namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryDifferentialTests;

// Sort / SortBounded on the prepared builder: the same eager-vs-prepared differential as the other
// prepared fixtures. The prepared sorted command routes through the eager sorted terminals' core, so
// rows, paging, TotalCount / Truncated and the bounded plan's encounter-order tie rule must all agree.
[TestFixture]
public class PreparedQuerySortDifferentialTests {
	private const int N = 240;

	// Class comparer, total order, descending so the sort visibly differs from encounter order.
	private sealed class ByCodeDesc : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (y?.Code ?? 0).CompareTo(x?.Code ?? 0);
	}

	// Struct comparer, total order — the zero-alloc comparer shape.
	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	// Struct comparer with many ties: Group takes only 7 values over 240 rows.
	private readonly struct ByGroup : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0).CompareTo(y?.Group ?? 0);
	}

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	// Pages: small, crossing the end of a ~34-row group, skip beyond count, unbounded (classic fallback).
	private static readonly (int skip, int take)[] Pages = [(0, 5), (30, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue)];

	// ── Sort (classic, unbounded) ─────────────────────────────────────────────────

	[Test]
	public void Sort_ClassComparer_EveryShape_LikeEager() {
		var cmp = new ByCodeDesc();
		AssertSame(_cache.Query().Sort(cmp).Execute(), _cache.Prepare().Sort(cmp).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Sort(cmp).Execute(), _cache.Prepare().UseIndex(_byGroup, 2).Sort(cmp).Build().Execute());
		AssertSame(
			_cache.Query().UseIndex(_byGroup, 2).Where(static v => v.Flag).Sort(cmp).Execute(),
			_cache.Prepare().UseIndex(_byGroup, 2).Where(static v => v.Flag).Sort(cmp).Build().Execute());
		AssertSame(
			_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1120)).Sort(cmp).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1120)).Sort(cmp).Build().Execute());

		var parameterized = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Sort(cmp).Build();
		for (var group = 0; group < 7; group++)
			AssertSame(_cache.Query().UseIndex(_byGroup, group).Sort(cmp).Execute(), parameterized.Execute(group));
	}

	[Test]
	public void Sort_StructComparer_EveryShape_LikeEager() {
		AssertSame(_cache.Query().Sort(new ByCode()).Execute(), _cache.Prepare().Sort(new ByCode()).Build().Execute());
		AssertSame(_cache.Query().UseIndex(_byGroup, 4).Sort(new ByCode()).Execute(), _cache.Prepare().UseIndex(_byGroup, 4).Sort(new ByCode()).Build().Execute());
		AssertSame(
			_cache.Query().UseIndex(_byGroup, 4).Where(static v => !v.Flag).Sort(new ByCode()).Execute(),
			_cache.Prepare().UseIndex(_byGroup, 4).Where(static v => !v.Flag).Sort(new ByCode()).Build().Execute());
		AssertSame(
			_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1200)).Sort(new ByCode()).Execute(),
			_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1200)).Sort(new ByCode()).Build().Execute());

		var parameterized = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi)).Sort(new ByCode()).Build();
		foreach (var args in new[] { (lo: 1000, hi: 1030), (lo: 1100, hi: 1105), (lo: 1230, hi: 1300), (lo: 2000, hi: 3000) })
			AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), args).Sort(new ByCode()).Execute(), parameterized.Execute(args));
	}

	[Test]
	public void Sort_WithSkipTake_AndEveryExecuteVariant_LikeEager() {
		var prepared = _cache.Prepare().UseIndex(_byGroup, 1).Sort(new ByCode()).Build();
		foreach (var (skip, take) in Pages) {
			AssertSame(_cache.Query().UseIndex(_byGroup, 1).Sort(new ByCode()).Execute(skip, take), prepared.Execute(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 1).Sort(new ByCode()).ExecutePooled(skip, take), prepared.ExecutePooled(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 1).Sort(new ByCode()).ExecuteCloned(skip, take), prepared.ExecuteCloned(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 1).Sort(new ByCode()).ExecutePooledCloned(skip, take), prepared.ExecutePooledCloned(skip, take));
		}
	}

	[Test]
	public void Sort_ManyTies_StableLikeEager() {
		AssertSame(_cache.Query().Sort(new ByGroup()).Execute(), _cache.Prepare().Sort(new ByGroup()).Build().Execute());
		AssertSame(
			_cache.Query().Where(static v => v.Flag).Sort(new ByGroup()).Execute(10, 20),
			_cache.Prepare().Where(static v => v.Flag).Sort(new ByGroup()).Build().Execute(10, 20));
	}

	// ── SortBounded ───────────────────────────────────────────────────────────────

	[Test]
	public void SortBounded_ClassComparer_EveryShapeAndPage_LikeEager() {
		var cmp = new ByCodeDesc();
		var all = _cache.Prepare().SortBounded(cmp).Build();
		var list = _cache.Prepare().UseIndex(_byGroup, 2).SortBounded(cmp).Build();
		var listWhere = _cache.Prepare().UseIndex(_byGroup, 2).Where(static v => v.Flag).SortBounded(cmp).Build();
		var range = _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1120)).SortBounded(cmp).Build();
		var parameterized = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(cmp).Build();

		foreach (var (skip, take) in Pages) {
			AssertSame(_cache.Query().SortBounded(cmp).Execute(skip, take), all.Execute(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 2).SortBounded(cmp).Execute(skip, take), list.Execute(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 2).Where(static v => v.Flag).SortBounded(cmp).Execute(skip, take), listWhere.Execute(skip, take));
			AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1120)).SortBounded(cmp).Execute(skip, take), range.Execute(skip, take));
			for (var group = 0; group < 7; group++)
				AssertSame(_cache.Query().UseIndex(_byGroup, group).SortBounded(cmp).Execute(skip, take), parameterized.Execute(group, skip, take));
		}
	}

	[Test]
	public void SortBounded_StructComparer_EveryShapeAndPage_LikeEager() {
		var all = _cache.Prepare().SortBounded(new ByCode()).Build();
		var list = _cache.Prepare().UseIndex(_byGroup, 5).SortBounded(new ByCode()).Build();
		var listWhere = _cache.Prepare().UseIndex(_byGroup, 5).Where(static v => !v.Flag).SortBounded(new ByCode()).Build();
		var range = _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lte(1080)).SortBounded(new ByCode()).Build();
		var parameterized = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi)).SortBounded(new ByCode()).Build();

		foreach (var (skip, take) in Pages) {
			AssertSame(_cache.Query().SortBounded(new ByCode()).ExecutePooled(skip, take), all.ExecutePooled(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 5).SortBounded(new ByCode()).ExecutePooled(skip, take), list.ExecutePooled(skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, 5).Where(static v => !v.Flag).SortBounded(new ByCode()).ExecutePooled(skip, take), listWhere.ExecutePooled(skip, take));
			AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Lte(1080)).SortBounded(new ByCode()).ExecutePooled(skip, take), range.ExecutePooled(skip, take));
			foreach (var args in new[] { (lo: 1000, hi: 1030), (lo: 1100, hi: 1105), (lo: 2000, hi: 3000) })
				AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), args).SortBounded(new ByCode()).ExecutePooled(skip, take), parameterized.ExecutePooled(args, skip, take));
		}
	}

	[Test]
	public void SortBounded_ManyTies_PagesMatchEager() {
		var all = _cache.Prepare().SortBounded(new ByGroup()).Build();
		var flagged = _cache.Prepare().Where(static v => v.Flag).SortBounded(new ByGroup()).Build();
		foreach (var (skip, take) in new[] { (0, 8), (8, 8), (33, 8), (100, 40), (230, 20), (0, int.MaxValue) }) {
			AssertSame(_cache.Query().SortBounded(new ByGroup()).ExecutePooled(skip, take), all.ExecutePooled(skip, take));
			AssertSame(_cache.Query().Where(static v => v.Flag).SortBounded(new ByGroup()).ExecutePooled(skip, take), flagged.ExecutePooled(skip, take));
		}
	}

	// The bounded plan's promise, through the prepared command: consecutive pages of an unchanged
	// result partition it — no duplicates, no gaps — even when the comparer ties almost everything.
	[Test]
	public void SortBounded_ManyTies_PreparedPagesConcatenateToTheEagerWholeResult() {
		using var whole = _cache.Query().SortBounded(new ByGroup()).ExecutePooled(0, N);
		var expected = new int[whole.Count];
		for (var i = 0; i < whole.Count; i++) expected[i] = whole[i].Id;

		var prepared = _cache.Prepare().SortBounded(new ByGroup()).Build();
		var paged = new List<int>();
		for (var skip = 0; skip < N; skip += 8) {
			using var page = prepared.ExecutePooled(skip, 8);
			for (var i = 0; i < page.Count; i++) paged.Add(page[i].Id);
		}

		Assert.That(paged.ToArray(), Is.EqualTo(expected));
	}

	// ── Count ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Count_OnSortedShapes_LikeEager() {
		var parameterized = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).SortBounded(new ByCode()).Build();
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().Sort(new ByCode()).Build().Count(), Is.EqualTo(_cache.Query().Sort(new ByCode()).Count()));
			Assert.That(_cache.Prepare().SortBounded(new ByGroup()).Build().Count(), Is.EqualTo(_cache.Query().SortBounded(new ByGroup()).Count()));
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCodeDesc()).Build().Count(), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 3).Sort(new ByCodeDesc()).Count()));
			Assert.That(_cache.Prepare().UseIndex(_byCode, -1).SortBounded(new ByCode()).Build().Count(), Is.EqualTo(0));
			for (var group = 0; group < 7; group++)
				Assert.That(parameterized.Count(group), Is.EqualTo(_cache.Query().UseIndex(_byGroup, group).Where(static v => v.Flag).SortBounded(new ByCode()).Count()));
		});
	}

	// ── Reuse ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Reuse_SortedBounded_ThreeArgsAndPages_ThenAcrossMutations_TracksTheLiveCache() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int minCode)>()
			.UseIndex(_byGroup, static a => a.group)
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.minCode))
			.SortBounded(new ByCodeDesc())
			.Build();
		var runs = new[] { (args: (group: 6, minCode: 1000), skip: 0, take: 10), (args: (group: 1, minCode: 1100), skip: 5, take: 7), (args: (group: 3, minCode: 1230), skip: 0, take: int.MaxValue) };

		foreach (var (args, skip, take) in runs)
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.minCode), args).SortBounded(new ByCodeDesc()).Execute(skip, take), prepared.Execute(args, skip, take));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6 });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved out of group 6
		_cache.AddOrUpdate(27, new PqItem { Id = 27, Code = 999, Group = 6 });  // group 6 row now below every minCode

		foreach (var (args, skip, take) in runs)
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.minCode), args).SortBounded(new ByCodeDesc()).Execute(skip, take), prepared.Execute(args, skip, take));
	}
}

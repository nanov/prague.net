namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using Prague.Core.TypeSystem;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// BuildFrozen() stage 3, step 6: the SortBounded feed (design §8). A simple plan narrowers →
// SortBounded runs the pipeline pass (fixed seed, so each row's encounter ordinal — the bounded
// tie-breaker — is eager's) into the eager TopKSimpleResultContainer behind exactly the eager
// ExecuteCoreSimpleTop gate, the classic container when the gate fails (take = int.MaxValue, a negative
// page); a joined plan narrowers → SortBounded → outer JoinOnes runs the same pass into the eager
// TopKJoinedBaseContainer, the page is materialized and the join resolvers fill only its rows (the eager
// ExecuteCoreJoinedTop, its base walk replaced). Pinned here: (a) byte-identical eager == prepared ==
// frozen on every shape × every Execute variant × every page with clone identity, with a total comparer
// and with a tie comparer (the fixed seed makes the ordinals eager's, so ties are byte-identical too),
// pages partition; (b) the opt-in free seed keeps the set, the page contract and a total comparer's
// sequence; (c) executor selection and Explain for the simple and the joined rule; (d) the joined shapes
// A / B on rows and joined values, every variant and page, clone identity of both sides; (e) rented
// arrays balanced under a throwing comparer / selector / Clone() on every container path; (f) 8 readers
// against a churning writer. Model: 240 items, Code = 1000 + Id (unique), Group = Id % 7 (~34 per
// bucket), Band = Id % 12 (20), Tier = Id % 40 (6), Flag = Id % 3 == 0, last-updated = 1_000_000 + Id *
// 1000; details keyed by Id for three ids in four; customers keyed by group, group 6 missing.
[TestFixture]
public class FrozenPipelineSortBoundedTests {
	private const int N = 240;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];

	// Small page, a page crossing the end of a ~34-row group, skip beyond the end, unbounded (the classic
	// container), unbounded with a skip, an empty page, one row, a middle page, everything but the first.
	private static readonly (int skip, int take)[] Pages = [(0, 5), (30, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue), (0, 0), (0, 1), (5, 5), (1, int.MaxValue), (2, 100)];
	private static readonly FrozenOptions Reorder = new() { ReorderIndexNarrowers = true };
	private static readonly FrozenOptions NoPipeline = new() { Pipeline = false };

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byBand = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;
	private CacheSymmetricKeyValueListIndex<int, PqItem, int> _bySym = null!;
	private InMemoryDataCache<int, PqCustomer> _details = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByItem = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byBand = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 12);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 40);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		_bySym = _cache.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Group);
		_details = new InMemoryDataCache<int, PqCustomer>();
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByItem = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		for (var i = 0; i < N; i++) {
			_cache.AddOrUpdate(i, Make(i), Ms(i));
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
			if (i % 3 != 0)
				_lines.AddOrUpdate(1000 + i, new PqLine { Id = 1000 + i, OrderId = i });
		}

		for (var g = 0; g < 6; g++)
			_customers.AddOrUpdate(g, new PqCustomer { Id = g, Region = g % 2 == 0 ? "EU" : "US" });
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static long Ms(int i) => 1_000_000L + i * 1000L;

	// Total orders (a struct and a class comparer) and two tying orders: 7 groups and 2 flag values.
	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	private sealed class ByCodeDesc : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (y?.Code ?? 0).CompareTo(x?.Code ?? 0);
	}

	private readonly struct ByGroup : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0).CompareTo(y?.Group ?? 0);
	}

	private readonly struct ByFlag : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Flag ?? false).CompareTo(y?.Flag ?? false);
	}

	// A comparer over the joined row, for the sort-after-join shape (never innermost: replays).
	private readonly struct ByLeftCode : IComparer<JoinResult<PqItem, PqCustomer?>> {
		public int Compare(JoinResult<PqItem, PqCustomer?> x, JoinResult<PqItem, PqCustomer?> y) => x.Left.Code.CompareTo(y.Left.Code);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static QueryResults<T> Run<TArgs, T>(PreparedQuery<TArgs, T> q, in TArgs args, Variant v, int skip, int take) => v switch {
		Variant.Execute => q.Execute(in args, skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(in args, skip, take),
		Variant.ExecutePooled => q.ExecutePooled(in args, skip, take),
		_ => q.ExecutePooledCloned(in args, skip, take),
	};

	private static QueryResults<PqItem> RunSortedEager<TComparer>(CacheQueryBuilderCombined<SortedQuery<ExecutableQuery<InMemoryDataCache<int, PqItem>>>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<SortResolver<int, PqItem, PqItem, TComparer>>, PqItem> q, Variant v, int skip, int take) where TComparer : IComparer<PqItem> => v switch {
		Variant.Execute => q.Execute(skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
		Variant.ExecutePooled => q.ExecutePooled(skip, take),
		_ => q.ExecutePooledCloned(skip, take),
	};

	private static bool IsClone(Variant v) => v is Variant.ExecuteCloned or Variant.ExecutePooledCloned;

	private void AssertSameAndIdentity(QueryResults<PqItem> eager, QueryResults<PqItem> frozen, bool clone, string label) {
		try {
			Assert.Multiple(() => {
				Assert.That(frozen.Count, Is.EqualTo(eager.Count), label + " Count");
				Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), label + " TotalCount");
				Assert.That(frozen.Truncated, Is.False, label + " Truncated");
			});
			for (var i = 0; i < eager.Count; i++) {
				Assert.That(frozen[i].Id, Is.EqualTo(eager[i].Id), label + " row " + i);
				Assert.That(_cache.TryGet(frozen[i].Id, out var cached), Is.True);
				if (clone) {
					Assert.That(ReferenceEquals(frozen[i], cached), Is.False, label + " clone must be a fresh instance");
					Assert.That(frozen[i].CacheEquals(cached), Is.True, label + " clone must equal the cached value");
				} else {
					Assert.That(ReferenceEquals(frozen[i], cached), Is.True, label + " non-clone must be the cached instance");
				}
			}
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>(a): every variant × page byte-identical to eager (with identity) and to prepared; Count.</summary>
	private void AssertBounded<TArgs, TComparer>(Func<CacheQueryBuilderCombined<SortedQuery<ExecutableQuery<InMemoryDataCache<int, PqItem>>>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<SortResolver<int, PqItem, PqItem, TComparer>>, PqItem>> eager,
		PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label)
		where TComparer : IComparer<PqItem> {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				AssertSameAndIdentity(RunSortedEager(eager(), v, skip, take), Run(frozen, in args, v, skip, take), IsClone(v), tag);
				AssertSame(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take));
			}

		var expected = eager().Count();
		Assert.That(frozen.Count(in args), Is.EqualTo(expected), label + " Count");
		Assert.That(prepared.Count(in args), Is.EqualTo(expected), label + " prepared Count");
	}

	/// <summary>Consecutive frozen pages of an unchanged result concatenate to the eager whole — the bounded plan's contract — for every page size.</summary>
	private static void AssertPagesPartition<TArgs>(QueryResults<PqItem> whole, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label) {
		try {
			var expected = new int[whole.Count];
			for (var i = 0; i < whole.Count; i++) expected[i] = whole[i].Id;
			foreach (var size in new[] { 1, 3, 8 }) {
				var paged = new List<int>();
				for (var skip = 0; skip < whole.Count + size; skip += size) {
					using var page = frozen.ExecutePooled(in args, skip, size);
					Assert.That(page.TotalCount, Is.EqualTo(whole.Count), label + " TotalCount");
					for (var i = 0; i < page.Count; i++) paged.Add(page[i].Id);
				}

				Assert.That(paged, Is.EqualTo(expected).AsCollection, $"{label} pages of {size} partition the whole");
			}
		} finally {
			whole.Dispose();
		}
	}

	// ── (a) Total comparer: byte-identical on every shape ─────────────────────────

	[Test]
	public void TotalComparer_SingleList_ListWhere_Bound_AndParameterized_ByteIdentical() {
		var list = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).Build();
		var listFrozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).BuildFrozen();
		var listWhere = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).SortBounded(new ByCodeDesc()).Build();
		var listWhereFrozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).SortBounded(new ByCodeDesc()).BuildFrozen();
		var cmp = new ByCodeDesc();
		for (var g = 0; g < 8; g++) {
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).SortBounded(new ByCode()), list, listFrozen, g, "list " + g);
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).Where(static v => v.Flag).SortBounded(cmp), listWhere, listWhereFrozen, g, "list + where " + g);
		}

		AssertBounded(() => _cache.Query().UseIndex(_byGroup, 3).Where(static v => v.Id > 100).Where(static v => !v.Flag).SortBounded(new ByCode()),
			_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Id > 100).Where(static v => !v.Flag).SortBounded(new ByCode()).Build(),
			_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Id > 100).Where(static v => !v.Flag).SortBounded(new ByCode()).BuildFrozen(), default(NoArgs), "list + two wheres, bound");
		AssertBounded(() => _cache.Query().Where(static v => v.Flag).UseIndex(_byGroup, 3).SortBounded(new ByCode()),
			_cache.Prepare().Where(static v => v.Flag).UseIndex(_byGroup, 3).SortBounded(new ByCode()).Build(),
			_cache.Prepare().Where(static v => v.Flag).UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen(), default(NoArgs), "where before list, bound");
	}

	[Test]
	public void TotalComparer_SmallProbeSeeds_ListList_ListListList_ListUnique_ByteIdentical() {
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).Build();
		var listListFrozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).BuildFrozen();
		var three = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).Build();
		var threeFrozen = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).BuildFrozen();
		var listUnique = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).SortBounded(new ByCode()).Build();
		var listUniqueFrozen = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).SortBounded(new ByCode()).BuildFrozen();
		foreach (var (g, t) in new[] { (0, 0), (3, 3), (6, 39), (2, 17), (9, 1) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).SortBounded(new ByCode()), listList, listListFrozen, (g, t), $"list∩list {g}/{t}");
		foreach (var (g, b, t) in new[] { (3, 3, 3), (3, 3, 43), (0, 0, 0), (5, 5, 5), (4, 3, 3) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByCode()), three, threeFrozen, (g, b, t), $"list∩list∩list {g}/{b}/{t}");
		foreach (var (g, c) in new[] { (3, 1003), (3, 1004), (0, 1007), (2, 5000) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, c).SortBounded(new ByCode()), listUnique, listUniqueFrozen, (g, c), $"list∩unique {g}/{c}");
		listListFrozen.ExecutePooled((3, 3), 0, 2).Dispose();
		Assert.That(listListFrozen.Explain(), Does.Contain("probe: slot-sorted into step 0 ListEq").And.Contain("sort: bounded"));
	}

	[Test]
	public void TotalComparer_Range_ListRange_KeySet_LastUpdated_ByteIdentical() {
		var range = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).SortBounded(new ByCodeDesc()).Build();
		var rangeFrozen = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).SortBounded(new ByCodeDesc()).BuildFrozen();
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi)).SortBounded(new ByCode()).Build();
		var listRangeFrozen = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi)).SortBounded(new ByCode()).BuildFrozen();
		var listAfter = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, static a => a.after).SortBounded(new ByCode()).Build();
		var listAfterFrozen = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, static a => a.after).SortBounded(new ByCode()).BuildFrozen();
		var cmp = new ByCodeDesc();
		foreach (var (lo, hi) in new[] { (1000, 1240), (1050, 1120), (1100, 1105), (1239, 1240), (2000, 3000) })
			AssertBounded(() => _cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo, hi)).SortBounded(cmp), range, rangeFrozen, (lo, hi), $"range [{lo},{hi})");
		foreach (var (g, lo, hi) in new[] { (3, 1000, 1240), (3, 1050, 1120), (0, 1100, 1105), (5, 2000, 3000) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi), (lo, hi)).SortBounded(new ByCode()), listRange, listRangeFrozen, (g, lo, hi), $"list∩range {g} ({lo},{hi}]");
		foreach (var (g, i) in new[] { (3, 0), (3, 120), (3, 239), (6, 200) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_lastUpdated, Ms(i)).SortBounded(new ByCode()), listAfter, listAfterFrozen, (g, Ms(i)), $"list∩after {g}/{i}");
		AssertBounded(() => _cache.Query().UseIndex(_flagged).SortBounded(new ByCodeDesc()), _cache.Prepare().UseIndex(_flagged).SortBounded(cmp).Build(), _cache.Prepare().UseIndex(_flagged).SortBounded(cmp).BuildFrozen(), default(NoArgs), "keyset");
		AssertBounded(() => _cache.Query().UseIndex(_byGroup, 2).UseIndex(_flagged).SortBounded(new ByCode()), _cache.Prepare().UseIndex(_byGroup, 2).UseIndex(_flagged).SortBounded(new ByCode()).Build(), _cache.Prepare().UseIndex(_byGroup, 2).UseIndex(_flagged).SortBounded(new ByCode()).BuildFrozen(), default(NoArgs), "list∩keyset");
	}

	// ── (a) Tie comparer: the fixed seed makes the ordinals eager's ────────────────

	[Test]
	public void TieComparer_EveryShape_ByteIdenticalToEager_AndPagesPartition() {
		var list = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByFlag()).Build();
		var listFrozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByFlag()).BuildFrozen();
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByFlag()).Build();
		var listListFrozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByFlag()).BuildFrozen();
		var wide = _cache.Prepare<int, PqItem, (int group, int band)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).SortBounded(new ByFlag()).Build();
		var wideFrozen = _cache.Prepare<int, PqItem, (int group, int band)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).SortBounded(new ByFlag()).BuildFrozen();
		var range = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).SortBounded(new ByGroup()).Build();
		var rangeFrozen = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).SortBounded(new ByGroup()).BuildFrozen();
		var keySet = _cache.Prepare().UseIndex(_flagged).SortBounded(new ByGroup()).Build();
		var keySetFrozen = _cache.Prepare().UseIndex(_flagged).SortBounded(new ByGroup()).BuildFrozen();
		for (var g = 0; g < 7; g++) {
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).SortBounded(new ByFlag()), list, listFrozen, g, "ties list " + g);
			AssertPagesPartition(_cache.Query().UseIndex(_byGroup, g).SortBounded(new ByFlag()).ExecutePooled(0, N), listFrozen, g, "ties list " + g);
			foreach (var t in new[] { 0, 3, 17, 39 }) {
				AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).SortBounded(new ByFlag()), listList, listListFrozen, (g, t), $"ties list∩list {g}/{t}");
				AssertPagesPartition(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).SortBounded(new ByFlag()).ExecutePooled(0, N), listListFrozen, (g, t), $"ties list∩list {g}/{t}");
			}

			foreach (var b in new[] { 0, 5, 11 }) {
				AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()), wide, wideFrozen, (g, b), $"ties list∩band {g}/{b}");
				AssertPagesPartition(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).ExecutePooled(0, N), wideFrozen, (g, b), $"ties list∩band {g}/{b}");
			}
		}

		foreach (var (lo, hi) in new[] { (1000, 1240), (1050, 1120), (1100, 1105) }) {
			AssertBounded(() => _cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo, hi)).SortBounded(new ByGroup()), range, rangeFrozen, (lo, hi), $"ties range [{lo},{hi})");
			AssertPagesPartition(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo, hi)).SortBounded(new ByGroup()).ExecutePooled(0, N), rangeFrozen, (lo, hi), $"ties range [{lo},{hi})");
		}

		AssertBounded(() => _cache.Query().UseIndex(_flagged).SortBounded(new ByGroup()), keySet, keySetFrozen, default(NoArgs), "ties keyset");
		AssertPagesPartition(_cache.Query().UseIndex(_flagged).SortBounded(new ByGroup()).ExecutePooled(0, N), keySetFrozen, default(NoArgs), "ties keyset");
	}

	// ── (b) The opt-in free seed ──────────────────────────────────────────────────

	// ReorderIndexNarrowers seeds from the smaller tier bucket: a total comparer's pages are still eager's
	// byte for byte (the order is the comparer's), a tie comparer's pages hold the same rows per tie
	// group, are sorted, and partition the frozen whole; Count is unchanged.
	[Test]
	public void ReorderIndexNarrowers_TotalComparerByteIdentical_TieComparerKeepsTheSetAndTheContract() {
		var total = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).Build();
		var totalFrozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).BuildFrozen(Reorder);
		var ties = _cache.Prepare<int, PqItem, (int group, int band)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).SortBounded(new ByFlag()).BuildFrozen(Reorder);
		Assert.That(totalFrozen.Plan.Optimizations, Does.Contain("ReorderIndexNarrowers"));
		Assert.That(totalFrozen.Explain(), Does.Contain("pipeline: seed = free for Execute"));
		foreach (var (g, t) in new[] { (0, 0), (3, 3), (6, 39), (2, 17) })
			AssertBounded(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).SortBounded(new ByCode()), total, totalFrozen, (g, t), $"reorder total {g}/{t}");
		for (var g = 0; g < 7; g++)
			foreach (var b in new[] { 0, 5, 11 }) {
				using var expected = _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).ExecutePooled(0, N);
				using var all = ties.ExecutePooled((g, b), 0, N);
				Assert.That(all.Count, Is.EqualTo(expected.Count));
				Assert.That(all.TotalCount, Is.EqualTo(expected.TotalCount));
				for (var i = 1; i < all.Count; i++)
					Assert.That(all[i - 1].Flag.CompareTo(all[i].Flag), Is.LessThanOrEqualTo(0), "sorted");
				Assert.That(all.Where(static v => v.Flag).Select(static v => v.Id).OrderBy(static x => x), Is.EqualTo(expected.Where(static v => v.Flag).Select(static v => v.Id).OrderBy(static x => x)).AsCollection);
				Assert.That(all.Where(static v => !v.Flag).Select(static v => v.Id).OrderBy(static x => x), Is.EqualTo(expected.Where(static v => !v.Flag).Select(static v => v.Id).OrderBy(static x => x)).AsCollection);
				AssertPagesPartition(ties.ExecutePooled((g, b), 0, N), ties, (g, b), $"reorder ties {g}/{b}");
				Assert.That(ties.Count((g, b)), Is.EqualTo(expected.Count));
			}
	}

	// ── (c) Executor selection and Explain ────────────────────────────────────────

	[Test]
	public void Executor_SimpleAndJoinedSortBounded_TakeThePipeline_TheRestReplays() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list SortBounded");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCodeDesc()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "class comparer");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique SortBounded (sorted: never the point lookup)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen(NoPipeline).Plan.Executor, Is.EqualTo("Replay"), "pipeline off");
			Assert.That(_cache.Prepare().SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filter-only / all rows: no seed source");
			Assert.That(_cache.Prepare().Where(static v => v.Flag).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filter-only");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "composite (step 4)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).JoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "SortBounded → outer JoinOne by PK (shape A)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).JoinOne(_bySym, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "SortBounded → outer JoinOne LeftSym");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).SortBounded(new ByCode()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "SortBounded → two outer JoinOnes (shape B)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).JoinOne(_details).BuildFrozen(NoPipeline).Plan.Executor, Is.EqualTo("Replay"), "joined, pipeline off");
			// Step 5: fused JoinOnes open the inner, classic-Sort, sort-after-join and unsorted joined shapes
			// (FrozenPipelineJoinTests pins them); step 8 admits JoinMany (FrozenPipelineJoinManyTests); the seedless chain still replays.
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).InnerJoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "an inner fused join probes the right per left (step 5)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).InnerJoinOne(_bySym, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "an inner left-symmetric join regroups its rows (opt in with FuseSymmetricInnerJoins)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).JoinMany(_lines, _lineByItem).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "SortBounded → outer JoinMany: the fan-out fills the bounded page (step 8)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).JoinOne(_details).SortBounded(new ByLeftCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a sort after the fused join runs in the classic joined container (step 5)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCode()).JoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "classic Sort → fused join (step 5)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).JoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unsorted fused join (step 5)");
			Assert.That(_cache.Prepare().SortBounded(new ByCode()).JoinOne(_details).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "joined, no seed source");
		});
		var simple = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Where(static v => v.Id > 0).SortBounded(new ByCode()).BuildFrozen();
		Assert.That(simple.Explain(), Does.Contain("executor: Pipeline").And.Contain("pipeline: seed = fixed for Execute").And.Contain("sort: bounded").And.Contain("filters: 1 (direct)").And.Not.Contain("joins:"));
		var joined = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		Assert.That(joined.Explain(), Does.Contain("executor: Pipeline").And.Contain("sort: bounded").And.Contain("joins: 2 (fused: 2, unfused: 0").And.Contain("resolvers: yes, sorted: yes"));
		var classic = _cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCode()).BuildFrozen();
		Assert.That(classic.Explain(), Does.Contain("sort: bounded").And.Contain("pipeline: seed = free for Execute"), "a classic Sort takes the bounded flow for a finite page (step 8) and keeps its free seed");
	}

	// ── (d) Joined: shapes A / B on rows and joined values ─────────────────────────

	private static string Detail(JoinResult<PqItem, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static string Two(JoinResult<PqItem, PqCustomer?, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";

	private void AssertJoinedIdentity(JoinResult<PqItem, PqCustomer?> row, bool clone, string label) {
		Assert.That(_cache.TryGet(row.Left.Id, out var left), Is.True);
		Assert.That(ReferenceEquals(row.Left, left), Is.EqualTo(!clone), label + " left identity");
		if (row.Right is null)
			return;
		Assert.That(_details.TryGet(row.Right.Id, out var right), Is.True);
		Assert.That(ReferenceEquals(row.Right, right), Is.EqualTo(!clone), label + " right identity");
	}

	[Test]
	public void ShapeA_ListListList_SortBounded_JoinOnePk_EveryVariant_EveryPage_LikeEagerAndPrepared() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByGroup()).JoinOne(_details).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByGroup()).JoinOne(_details).BuildFrozen();
		var two = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).SortBounded(new ByFlag()).JoinOne(_details).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		foreach (var args in new[] { (3, 3, 3), (3, 3, 43), (0, 0, 0), (5, 5, 5), (4, 3, 3), (1, 1, 1) }) {
			var (g, b, t) = args;
			foreach (var v in Variants)
				foreach (var (skip, take) in Pages) {
					var tag = $"A {args} {v} skip={skip} take={take}";
					var eager = v switch {
						Variant.Execute => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByGroup()).JoinOne(_details).Execute(skip, take),
						Variant.ExecuteCloned => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByGroup()).JoinOne(_details).ExecuteCloned(skip, take),
						Variant.ExecutePooled => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByGroup()).JoinOne(_details).ExecutePooled(skip, take),
						_ => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByGroup()).JoinOne(_details).ExecutePooledCloned(skip, take),
					};
					using (var rows = Run(frozen, in args, v, skip, take)) {
						for (var i = 0; i < rows.Count; i++)
							AssertJoinedIdentity(rows[i], IsClone(v), tag);
					}

					AssertSameJoined(eager, Run(frozen, in args, v, skip, take), Detail);
					AssertSameJoined(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), Detail);
					var wideEager = v switch {
						Variant.Execute => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_details).Execute(skip, take),
						Variant.ExecuteCloned => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_details).ExecuteCloned(skip, take),
						Variant.ExecutePooled => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_details).ExecutePooled(skip, take),
						_ => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_details).ExecutePooledCloned(skip, take),
					};
					AssertSameJoined(wideEager, Run(two, in args, v, skip, take), Detail);
				}

			var count = _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t).SortBounded(new ByGroup()).JoinOne(_details).Count();
			Assert.That(frozen.Count(args), Is.EqualTo(count), "Count " + args);
			Assert.That(prepared.Count(args), Is.EqualTo(count));
		}
	}

	[Test]
	public void ShapeB_LastUpdated_ListList_SortBounded_TwoJoinOnes_EveryVariant_EveryPage_LikeEagerAndPrepared() {
		var prepared = _cache.Prepare<int, PqItem, (long after, int group, int band)>().UseIndex(_lastUpdated, static a => a.after).UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band)
			.SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).Build();
		var frozen = _cache.Prepare<int, PqItem, (long after, int group, int band)>().UseIndex(_lastUpdated, static a => a.after).UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band)
			.SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		var byRange = _cache.Prepare<int, PqItem, (long after, int group, int band)>().UseIndex(_codeRange, static (rb, a) => rb.Gt((int)((a.after - 1_000_000L) / 1000L) + 1000)).UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band)
			.SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(byRange.Plan.Executor, Is.EqualTo("Pipeline"));
		foreach (var args in new[] { (Ms(0), 3, 3), (Ms(120), 3, 3), (Ms(200), 6, 6), (Ms(0), 2, 5), (Ms(239), 3, 3), (Ms(60), 1, 1) }) {
			var (after, g, b) = args;
			foreach (var v in Variants)
				foreach (var (skip, take) in Pages) {
					var tag = $"B {args} {v} skip={skip} take={take}";
					var eager = v switch {
						Variant.Execute => _cache.Query().UseIndex(_lastUpdated, after).UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).Execute(skip, take),
						Variant.ExecuteCloned => _cache.Query().UseIndex(_lastUpdated, after).UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).ExecuteCloned(skip, take),
						Variant.ExecutePooled => _cache.Query().UseIndex(_lastUpdated, after).UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).ExecutePooled(skip, take),
						_ => _cache.Query().UseIndex(_lastUpdated, after).UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).ExecutePooledCloned(skip, take),
					};
					using (var rows = Run(frozen, in args, v, skip, take)) {
						for (var i = 0; i < rows.Count; i++) {
							Assert.That(_cache.TryGet(rows[i].Left.Id, out var left), Is.True);
							Assert.That(ReferenceEquals(rows[i].Left, left), Is.EqualTo(!IsClone(v)), tag + " left identity");
							if (rows[i].Right is { } customer) {
								Assert.That(_customers.TryGet(customer.Id, out var cached), Is.True);
								Assert.That(ReferenceEquals(customer, cached), Is.EqualTo(!IsClone(v)), tag + " right identity");
							}

							if (rows[i].Right2 is { } detail) {
								Assert.That(_details.TryGet(detail.Id, out var cached), Is.True);
								Assert.That(ReferenceEquals(detail, cached), Is.EqualTo(!IsClone(v)), tag + " right2 identity");
							}
						}
					}

					AssertSameJoined(eager, Run(frozen, in args, v, skip, take), Two);
					AssertSameJoined(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), Two);
					AssertSameJoined(Run(prepared, in args, v, skip, take), Run(byRange, in args, v, skip, take), Two);
				}

			var count = _cache.Query().UseIndex(_lastUpdated, after).UseIndex(_byGroup, g).UseIndex(_byBand, b).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).Count();
			Assert.That(frozen.Count(args), Is.EqualTo(count), "Count " + args);
			Assert.That(byRange.Count(args), Is.EqualTo(count), "Count by range " + args);
			Assert.That(prepared.Count(args), Is.EqualTo(count));
		}
	}

	[Test]
	public void Joined_Pages_ConcatenateToTheEagerWhole_AndTrackMutations() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		for (var round = 0; round < 3; round++) {
			for (var g = 0; g < 7; g++) {
				using var whole = _cache.Query().UseIndex(_byGroup, g).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).ExecutePooled(0, N);
				var expected = new string[whole.Count];
				for (var i = 0; i < whole.Count; i++) expected[i] = Two(whole[i]);
				foreach (var size in new[] { 1, 4, 7 }) {
					var paged = new List<string>();
					for (var skip = 0; skip < whole.Count + size; skip += size) {
						using var page = frozen.ExecutePooled(g, skip, size);
						Assert.That(page.TotalCount, Is.EqualTo(whole.Count));
						for (var i = 0; i < page.Count; i++) paged.Add(Two(page[i]));
					}

					Assert.That(paged, Is.EqualTo(expected).AsCollection, $"group {g} pages of {size}");
				}
			}

			// Rows leave and enter the buckets, a detail and a customer disappear, a customer appears.
			_cache.Remove(round * 7 + 3);
			_cache.AddOrUpdate(N + round, new PqItem { Id = N + round, Code = 5000 + round, Group = round, Flag = true }, Ms(N + round));
			_cache.AddOrUpdate(round * 7 + 4, new PqItem { Id = round * 7 + 4, Code = 1000 + round * 7 + 4, Group = (round + 1) % 7, Flag = false }, Ms(N + 10 + round));
			_details.Remove(round * 7 + 5);
			_customers.Remove(round);
			_customers.AddOrUpdate(6, new PqCustomer { Id = 6, Region = "APAC" });
		}
	}

	// ── (e) Leaks ─────────────────────────────────────────────────────────────────

	private readonly struct Bomb : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0) == 0 ? throw new InvalidOperationException("compare boom") : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	private sealed class PqBomb : ICacheEquatable<PqBomb>, ICacheClonable<PqBomb> {
		public int Id { get; init; }
		public int Group { get; init; }
		public bool CacheEquals(PqBomb? other) => other is not null && other.Id == Id && other.Group == Group;
		public int CacheGetHashCode() => HashCode.Combine(Id, Group);
		public PqBomb Clone() => Id == 30 ? throw new InvalidOperationException("clone boom") : new PqBomb { Id = Id, Group = Group };
	}

	private readonly struct ByBombId : IComparer<PqBomb> {
		public int Compare(PqBomb? x, PqBomb? y) => (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	[Test]
	public void Throwing_Comparer_Selector_Clone_LeaveNoRentedArrays_OnTheHeap_CollectAll_AndClassicPaths() {
		var big = new InMemoryDataCache<int, PqItem>();
		var byGroup = big.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byTier = big.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 5);
		var details = new InMemoryDataCache<int, PqCustomer>();
		for (var i = 0; i < 3000; i++) {
			big.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 2, Flag = i % 3 == 0 });
			if (i % 4 != 0) details.AddOrUpdate(i, new PqCustomer { Id = i, Region = "EU" });
		}

		// Group 0 makes the comparer throw: in the heap push (small page), in the in-place select (a page
		// near the full size: collect-all), in the classic container's sort (unbounded); group 1 runs.
		var comparer = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).SortBounded(new Bomb()).BuildFrozen();
		var selector = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier < 0 ? throw new InvalidOperationException("boom") : a.tier).SortBounded(new ByCode()).BuildFrozen();
		var predicate = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier)
			.Where(static (v, a) => v.Id > 2000 && a.group == 0 ? throw new InvalidOperationException("boom") : true).SortBounded(new ByCode()).BuildFrozen();
		var joined = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).SortBounded(new Bomb()).JoinOne(details).BuildFrozen();
		var joinedSelector = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier < 0 ? throw new InvalidOperationException("boom") : a.tier).SortBounded(new ByCode()).JoinOne(details).BuildFrozen();
		Assert.That(comparer.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(joined.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled((0, 1), 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooledCloned((0, 1), 2, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled((0, 1), 0, 250).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.Execute((0, 1), 3, int.MaxValue).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((0, -1), 0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.Count((0, -1)));
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled((0, 1), 0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled((0, 1), 0, 250).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooledCloned((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => joined.ExecutePooled((0, 1), 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => joined.ExecutePooledCloned((0, 1), 2, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => joined.ExecutePooled((0, 1), 0, 250).Dispose());
			Assert.Throws<InvalidOperationException>(() => joined.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => joinedSelector.ExecutePooled((0, -1), 0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => joinedSelector.Count((0, -1)));
			comparer.ExecutePooled((1, 3), 0, 3).Dispose();
			comparer.ExecutePooled((1, 3), 0, 250).Dispose();
			comparer.ExecutePooled((1, 3)).Dispose();
			joined.ExecutePooled((1, 3), 0, 3).Dispose();
			joined.ExecutePooledCloned((1, 3), 0, 250).Dispose();
			joined.ExecutePooled((1, 3)).Dispose();
			predicate.ExecutePooled((1, 1), 0, 5).Dispose();
		});
		AssertSame(big.Query().UseIndex(byGroup, 1).UseIndex(byTier, 1).SortBounded(new ByCode()).Execute(0, 5), selector.Execute((1, 1), 0, 5));

		// A throwing Clone() on the pooled, cloned page: the page buffer and the heap go back — simple,
		// joined (the joined container clones after the fill), and the classic fallback of both.
		var bombs = new InMemoryDataCache<int, PqBomb>();
		var bombGroup = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var bombTier = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 4);
		var bombDetails = new InMemoryDataCache<int, PqCustomer>();
		for (var i = 0; i < 100; i++) {
			bombs.AddOrUpdate(i, new PqBomb { Id = i, Group = i % 3 });
			bombDetails.AddOrUpdate(i, new PqCustomer { Id = i, Region = "EU" });
		}

		var cloned = bombs.Prepare().UseIndex(bombGroup, 0).UseIndex(bombTier, 2).SortBounded(new ByBombId()).BuildFrozen();
		var clonedJoined = bombs.Prepare().UseIndex(bombGroup, 0).UseIndex(bombTier, 2).SortBounded(new ByBombId()).JoinOne(bombDetails).BuildFrozen();
		Assert.That(cloned.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(clonedJoined.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned(0, 8).Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecuteCloned(1, 2).Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedJoined.ExecutePooledCloned(0, 8).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedJoined.ExecuteCloned(1, 2).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedJoined.ExecutePooledCloned().Dispose());
			// The eager twin (JoinedResultContaier hands its buffer off only after the clone).
			Assert.Throws<InvalidOperationException>(() => bombs.Query().UseIndex(bombGroup, 0).UseIndex(bombTier, 2).SortBounded(new ByBombId()).JoinOne(bombDetails).ExecutePooledCloned(0, 8).Dispose());
			Assert.Throws<InvalidOperationException>(() => bombs.Query().UseIndex(bombGroup, 0).UseIndex(bombTier, 2).SortBounded(new ByBombId()).JoinOne(bombDetails).ExecutePooledCloned().Dispose());
			cloned.ExecutePooled(0, 8).Dispose();
			cloned.ExecutePooledCloned(0, 2).Dispose();
			clonedJoined.ExecutePooled(0, 8).Dispose();
			clonedJoined.ExecutePooledCloned(0, 2).Dispose();
		});
	}

	// ── (f) Concurrency ───────────────────────────────────────────────────────────

	[Test]
	public void EightReaders_AgainstAChurningWriter_NeverThrow_NoDuplicates_SimpleAndJoined() {
		var simple = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByFlag()).BuildFrozen();
		var wide = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Id >= 0).SortBounded(new ByCode()).BuildFrozen();
		var joined = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.tier % 12).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				// Moves rows across groups (and so across customers), flips flags, removes and re-adds rows and details.
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 1000 + id, Group = (id + i) % 7, Flag = i % 5 == 0 }, Ms(id) + i);
				if (i % 11 == 0) _cache.Remove((id * 13) % N);
				if (i % 11 == 5) _cache.AddOrUpdate((id * 13) % N, Make((id * 13) % N), Ms((id * 13) % N));
				if (i % 17 == 0) _details.Remove((id * 7) % N);
				if (i % 17 == 8) _details.AddOrUpdate((id * 7) % N, new PqCustomer { Id = (id * 7) % N, Region = "EU" });
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				var seen = new HashSet<int>();
				for (var i = 0; i < 1_500; i++) {
					var group = (seed + i) % 7;
					var tier = (seed * 5 + i) % 40;
					var skip = i % 4;
					using (var rows = simple.ExecutePooled((group, tier), skip, 3)) {
						seen.Clear();
						Assert.That(rows.Count, Is.LessThanOrEqualTo(3));
						Assert.That(rows.Count, Is.LessThanOrEqualTo(Math.Max(rows.TotalCount - skip, 0)));
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(rows[r].Id % 40, Is.EqualTo(tier), "the small step is value-judged");
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
						}
					}

					using (var rows = wide.ExecutePooledCloned(group, skip, 10)) {
						seen.Clear();
						for (var r = 1; r < rows.Count; r++)
							Assert.That(rows[r - 1].Code, Is.LessThan(rows[r].Code), "sorted");
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
					}

					using (var rows = joined.ExecutePooled((group, tier), skip, 5)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							if (rows[r].Right2 is { } detail)
								Assert.That(detail.Id, Is.EqualTo(rows[r].Left.Id), "the detail is the row's");
						}
					}

					if (i % 50 == 0) {
						using var all = simple.ExecutePooled((group, tier));
						Assert.That(all.Count, Is.EqualTo(all.TotalCount));
					}

					Assert.That(joined.Count((group, tier)), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}
}

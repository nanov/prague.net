namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// Stage-3 pipeline allocation pins: a pooled execution and a Count allocate nothing in Release on
// every non-composite shape — the seed buffer is a stack span or a pooled rental, the bindings live in
// the frame, the filters are called directly. Frozen never allocates more than prepared.
[TestFixture]
[NonParallelizable]
public class FrozenPipelineAllocationTests {
	private const int Iterations = 20_000;
	private const int N = 5_000;

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

	[OneTimeSetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		// Band 13 ⊂ group 13 (291 = 3 × 97): ~17 rows against the group's 52, so the three-list plan small-probes.
		_byBand = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 291);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 10);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		_bySym = _cache.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Group);
		_details = new InMemoryDataCache<int, PqCustomer>();
		_customers = new InMemoryDataCache<int, PqCustomer>();
		for (var i = 0; i < N; i++) {
			_cache.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 97, Flag = i % 3 == 0 }, 1_000_000L + i);
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		for (var g = 0; g < 97; g += 2)
			_customers.AddOrUpdate(g, new PqCustomer { Id = g, Region = "EU" });
	}

	private static long Measure(Action body) {
		for (var i = 0; i < 1_000; i++) body();
		var best = long.MaxValue;
		for (var pass = 0; pass < 3; pass++) {
			var before = GC.GetAllocatedBytesForCurrentThread();
			for (var i = 0; i < Iterations; i++) body();
			best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
		}

		return best;
	}

	private static void Pin(string label, long prepared, long frozen, long count) {
		TestContext.Out.WriteLine($"{label}: prepared {(double)prepared / Iterations:F1} B/op, frozen {(double)frozen / Iterations:F1} B/op, count {(double)count / Iterations:F1} B/op");
		Assert.That(frozen, Is.LessThanOrEqualTo(prepared + Iterations / 100), label + " frozen must not allocate more than prepared");
#if !DEBUG
		Assert.That(frozen, Is.EqualTo(0), label + " frozen pooled execution must allocate nothing in Release");
		Assert.That(count, Is.EqualTo(0), label + " frozen Count must allocate nothing in Release");
#endif
	}

	[Test]
	public void ListWhere() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var group = 13;
		Pin("list+where", Measure(() => prepared.ExecutePooled(group).Dispose()), Measure(() => frozen.ExecutePooled(group).Dispose()), Measure(() => frozen.Count(group)));
	}

	[Test]
	public void ListList() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (group: 13, tier: 3);
		Pin("list∩list", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
	}

	// Step 3: the small-probe seed (list ∩ list declared large-then-small, list ∩ list ∩ list smallest last),
	// the free seed (Count, ReorderIndexNarrowers) and the classic Sort feed with a struct comparer.
	[Test]
	public void ListList_Reversed_ListListList_Reorder() {
		var reversedPrepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).Build();
		var reversed = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		var threePrepared = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).Build();
		var three = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var reorder = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(new FrozenOptions { ReorderIndexNarrowers = true });
		Assert.That(three.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (group: 13, tier: 3);
		var threeArgs = (group: 13, band: 13, tier: 3);
		Pin("list(small)∩list", Measure(() => reversedPrepared.ExecutePooled(args).Dispose()), Measure(() => reversed.ExecutePooled(args).Dispose()), Measure(() => reversed.Count(args)));
		Pin("list∩list∩list", Measure(() => threePrepared.ExecutePooled(threeArgs).Dispose()), Measure(() => three.ExecutePooled(threeArgs).Dispose()), Measure(() => three.Count(threeArgs)));
		Pin("list∩list reorder", Measure(() => reversedPrepared.ExecutePooled(args).Dispose()), Measure(() => reorder.ExecutePooled(args).Dispose()), Measure(() => reorder.Count(args)));
		three.ExecutePooled(threeArgs).Dispose();
		Assert.That(three.Explain(), Does.Contain("probe: slot-sorted into step 0 ListEq"));
	}

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	[Test]
	public void Sort_ListList_StructComparer() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (group: 13, tier: 3);
		Pin("sort list∩list", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
		Pin("sort list∩list page", Measure(() => prepared.ExecutePooled(args, 2, 3).Dispose()), Measure(() => frozen.ExecutePooled(args, 2, 3).Dispose()), Measure(() => frozen.Count(args)));
	}

	// Step 6: SortBounded — the fixed seed into the eager top-k container (a page) and into the classic
	// container (take = int.MaxValue); the joined shapes A / B, whose join resolvers fill the page rows.
	[Test]
	public void SortBounded_List_ListList_Page_AndClassicFallback() {
		var listPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).Build();
		var list = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).BuildFrozen();
		var prepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).BuildFrozen();
		Assert.That(list.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var group = 13;
		var args = (group: 13, tier: 3);
		Pin("sort-bounded list page", Measure(() => listPrepared.ExecutePooled(group, 0, 20).Dispose()), Measure(() => list.ExecutePooled(group, 0, 20).Dispose()), Measure(() => list.Count(group)));
		Pin("sort-bounded list∩list page", Measure(() => prepared.ExecutePooled(args, 2, 3).Dispose()), Measure(() => frozen.ExecutePooled(args, 2, 3).Dispose()), Measure(() => frozen.Count(args)));
		Pin("sort-bounded list∩list collect-all page", Measure(() => prepared.ExecutePooled(args, 0, 40).Dispose()), Measure(() => frozen.ExecutePooled(args, 0, 40).Dispose()), Measure(() => frozen.Count(args)));
		Pin("sort-bounded list∩list unbounded (classic)", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
	}

	[Test]
	public void SortBounded_ShapeA_ThreeLists_JoinOnePk_AndShapeB_LastUpdated_TwoLists_TwoJoinOnes() {
		var aPrepared = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).JoinOne(_details).Build();
		var a = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).JoinOne(_details).BuildFrozen();
		var bPrepared = _cache.Prepare<int, PqItem, (long after, int group, int tier)>().UseIndex(_lastUpdated, static a => a.after).UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).JoinOne(_bySym, _customers).JoinOne(_details).Build();
		var b = _cache.Prepare<int, PqItem, (long after, int group, int tier)>().UseIndex(_lastUpdated, static a => a.after).UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new ByCode()).JoinOne(_bySym, _customers).JoinOne(_details).BuildFrozen();
		Assert.That(a.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(b.Plan.Executor, Is.EqualTo("Pipeline"));
		var aArgs = (group: 13, band: 13, tier: 3);
		var bArgs = (after: 1_000_000L + N / 2, group: 13, tier: 3);
		Pin("shape A page", Measure(() => aPrepared.ExecutePooled(aArgs, 0, 5).Dispose()), Measure(() => a.ExecutePooled(aArgs, 0, 5).Dispose()), Measure(() => a.Count(aArgs)));
		Pin("shape A unbounded (classic)", Measure(() => aPrepared.ExecutePooled(aArgs).Dispose()), Measure(() => a.ExecutePooled(aArgs).Dispose()), Measure(() => a.Count(aArgs)));
		Pin("shape B page", Measure(() => bPrepared.ExecutePooled(bArgs, 0, 5).Dispose()), Measure(() => b.ExecutePooled(bArgs, 0, 5).Dispose()), Measure(() => b.Count(bArgs)));
	}

	[Test]
	public void ListRange() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (group: 13, lo: 2000, hi: 5000);
		Pin("list∩range", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
	}

	[Test]
	public void Range_AndOptionalRange() {
		var prepared = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		var frozen = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var optional = _cache.Prepare<int, PqItem, (int? lo, int? hi)>().UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (lo: 2000, hi: 2400);
		(int? lo, int? hi) optionalArgs = (2000, 2400);
		Pin("range", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
		Pin("optional range", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => optional.ExecutePooled(optionalArgs).Dispose()), Measure(() => optional.Count(optionalArgs)));
	}

	[Test]
	public void KeySet_AfterList() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var group = 13;
		Pin("list∩keyset", Measure(() => prepared.ExecutePooled(group).Dispose()), Measure(() => frozen.ExecutePooled(group).Dispose()), Measure(() => frozen.Count(group)));
	}

	[Test]
	public void LastUpdated_AfterList_AndAlone() {
		var prepared = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, static a => a.after).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, static a => a.after).BuildFrozen();
		var alone = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_lastUpdated, static a => a.after).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (group: 13, after: 1_000_000L + N / 2);
		Pin("list∩last-updated", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
		Pin("last-updated alone", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => alone.ExecutePooled((13, 1_000_000L + N - 300)).Dispose()), Measure(() => alone.Count((13, 1_000_000L + N - 300))));
	}

	[Test]
	public void MultiValue_UniqueIn_ListIn_Parameterized() {
		var codes = new[] { 1003, 1100, 1250, 1777, 2042, 3000, 4999 };
		var groups = new[] { 3, 17, 50 };
		var uniquePrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).Build();
		var uniqueFrozen = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).BuildFrozen();
		var listPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).UseIndex(_flagged).Build();
		var listFrozen = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).UseIndex(_flagged).BuildFrozen();
		Assert.That(uniqueFrozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(listFrozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Pin("unique in", Measure(() => uniquePrepared.ExecutePooled(codes).Dispose()), Measure(() => uniqueFrozen.ExecutePooled(codes).Dispose()), Measure(() => uniqueFrozen.Count(codes)));
		Pin("list in ∩ keyset", Measure(() => listPrepared.ExecutePooled(groups).Dispose()), Measure(() => listFrozen.ExecutePooled(groups).Dispose()), Measure(() => listFrozen.Count(groups)));
	}

	[Test]
	public void Fused_ThreeFilters_AndArgFilter() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int min)>().UseIndex(_byGroup, static a => a.group).Where(static v => v.Id >= 0).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Flag).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int min)>().UseIndex(_byGroup, static a => a.group).Where(static v => v.Id >= 0).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Flag).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(frozen.Plan.Optimizations, Does.Contain("FusedFilters"));
		var args = (group: 13, min: 500);
		Pin("list + 3 filters (fused)", Measure(() => prepared.ExecutePooled(args).Dispose()), Measure(() => frozen.ExecutePooled(args).Dispose()), Measure(() => frozen.Count(args)));
	}

	private readonly struct ByJoinedId : IComparer<JoinResult<PqItem, PqCustomer?>> {
		public int Compare(JoinResult<PqItem, PqCustomer?> x, JoinResult<PqItem, PqCustomer?> y) => x.Left.Id.CompareTo(y.Left.Id);
	}

	// Step 5: the fused JoinOne fill — an outer left-symmetric join, an inner PK join, two chained joins
	// (outer + inner), a classic Sort after the join over the joined row (struct comparer) and a SortBounded
	// page before an inner join: 0 B per pooled execution and per Count.
	[Test]
	public void FusedJoinOne_Outer_Inner_Chained_SortAfterJoin_SortBoundedInner() {
		var outerPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_bySym, _customers).Build();
		var outer = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_bySym, _customers).BuildFrozen();
		var innerPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).InnerJoinOne(_details).Build();
		var inner = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).InnerJoinOne(_details).BuildFrozen();
		var chainedPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_bySym, _customers).InnerJoinOne(_details).Build();
		var chained = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_bySym, _customers).InnerJoinOne(_details).BuildFrozen();
		var sortedPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_details).Sort(new ByJoinedId()).Build();
		var sorted = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).JoinOne(_details).Sort(new ByJoinedId()).BuildFrozen();
		var boundedPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).InnerJoinOne(_details).Build();
		var bounded = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).InnerJoinOne(_details).BuildFrozen();
		Assert.Multiple(() => {
			Assert.That(outer.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(inner.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(chained.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(sorted.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(bounded.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(chained.Explain(), Does.Contain("joins: 2 (fused: 2, unfused: 0"));
		});
		var group = 13;
		Pin("fused outer left-sym", Measure(() => outerPrepared.ExecutePooled(group).Dispose()), Measure(() => outer.ExecutePooled(group).Dispose()), Measure(() => outer.Count(group)));
		Pin("fused inner pk", Measure(() => innerPrepared.ExecutePooled(group).Dispose()), Measure(() => inner.ExecutePooled(group).Dispose()), Measure(() => inner.Count(group)));
		Pin("fused chained outer+inner", Measure(() => chainedPrepared.ExecutePooled(group).Dispose()), Measure(() => chained.ExecutePooled(group).Dispose()), Measure(() => chained.Count(group)));
		Pin("sort after fused join", Measure(() => sortedPrepared.ExecutePooled(group).Dispose()), Measure(() => sorted.ExecutePooled(group).Dispose()), Measure(() => sorted.Count(group)));
		Pin("sort-bounded page → fused inner", Measure(() => boundedPrepared.ExecutePooled(group, 0, 20).Dispose()), Measure(() => bounded.ExecutePooled(group, 0, 20).Dispose()), Measure(() => bounded.Count(group)));
	}
}

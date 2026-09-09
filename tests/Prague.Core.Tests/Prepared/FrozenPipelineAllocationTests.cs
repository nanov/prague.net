namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryDifferentialTests;

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
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	[OneTimeSetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 10);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 97, Flag = i % 3 == 0 }, 1_000_000L + i);
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
}

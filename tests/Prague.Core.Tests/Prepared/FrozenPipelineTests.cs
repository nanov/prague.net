namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using EagerItems = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryDifferentialTests.PqItem>, int, PreparedQueryDifferentialTests.PqItem,
	Resolvers<BaseResolver<int, PreparedQueryDifferentialTests.PqItem>>, PreparedQueryDifferentialTests.PqItem>;

// BuildFrozen() stage 3, steps 1–2: the pipeline executor for simple, unsorted plans of non-composite
// index steps. The first active index step seeds (copied out under one gate pin), every later step is
// a probe on the key or on the fetched value, the predicates run directly, and the eager
// SimpleResultContainer is driven exactly as the eager core drives it. Pinned here: (a) byte-identical
// eager == prepared == frozen on every non-composite shape × every Execute variant × the five pages
// with clone identity; (c) the value-side staleness window in both directions, deterministically, and
// its key-side twin reproducing the eager window; (d) rented arrays balanced on every path; (f) the
// executor selection and the Explain output. Model: 240 items, Code = 1000 + Id, Group = Id % 7 (~34
// per bucket), Tier = Id % 40 (6 per bucket), Flag = Id % 3 == 0, last-updated = 1_000_000 + Id * 1000.
[TestFixture]
public class FrozenPipelineTests {
	private const int N = 240;
	private const long BaseMs = 1_000_000L;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];
	private static readonly (int skip, int take)[] Pages = [(0, int.MaxValue), (0, 1), (1, int.MaxValue), (0, 0), (5, 5)];

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTags = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheRangeIndex<int, PqItem, string> _codeText = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 40);
		_byTags = _cache.CacheCollectionKeyValueListIndex<int>(static (_, v) => [v.Group, v.Group + 100]);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_codeText = _cache.CacheRangeIndex<string>(static (_, v) => v.Code.ToString("D6"));
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i), Ms(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static long Ms(int i) => BaseMs + i * 1000L;

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static QueryResults<PqItem> Run<TArgs>(PreparedQuery<TArgs, PqItem> q, in TArgs args, Variant v, int skip, int take) => v switch {
		Variant.Execute => q.Execute(in args, skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(in args, skip, take),
		Variant.ExecutePooled => q.ExecutePooled(in args, skip, take),
		_ => q.ExecutePooledCloned(in args, skip, take),
	};

	private static QueryResults<PqItem> RunEager(EagerItems q, Variant v, int skip, int take) => v switch {
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
				Assert.That(eager.Truncated, Is.False, label + " eager Truncated");
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

	/// <summary>The (a) contract for one shape and one argument set: executor, every variant × page vs eager (with identity) and vs prepared, Count.</summary>
	private void AssertPipeline<TArgs>(Func<EagerItems> eager, PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				AssertSameAndIdentity(RunEager(eager(), v, skip, take), Run(frozen, in args, v, skip, take), IsClone(v), tag);
				AssertSame(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take));
			}

		var expected = eager().Count();
		Assert.That(frozen.Count(in args), Is.EqualTo(expected), label + " Count");
		Assert.That(prepared.Count(in args), Is.EqualTo(expected), label + " prepared Count");
	}

	// ── (a) Parity: list, unique ──────────────────────────────────────────────────

	[TestCase(3)]
	[TestCase(0)]
	[TestCase(6)]
	[TestCase(-1)]
	public void ListEq_Arg_WithConstantWhere(int group) {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen();
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, group).Where(static v => v.Flag), prepared, frozen, group, "list arg + Where " + group);
	}

	[Test]
	public void ListEq_Bound_NoFilter_OneFilter_ThreeFilters_WhereBefore() {
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3), _cache.Prepare().UseIndex(_byGroup, 3).Build(), _cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen(), default(NoArgs), "list bound");
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).Where(static v => v.Id > 100), _cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Id > 100).Build(),
			_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Id > 100).BuildFrozen(), default(NoArgs), "list bound + Where");
		var three = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, 3).Where(static v => v.Id >= 0).Where(static (v, min) => v.Id >= min).Where(static v => !v.Flag).BuildFrozen();
		Assert.That(three.Plan.Optimizations, Does.Contain("FusedFilters"));
		Assert.That(three.Explain(), Does.Contain("filters: 3 (fused"));
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).Where(static v => v.Id >= 0).Where(static v => v.Id >= 50).Where(static v => !v.Flag),
			_cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, 3).Where(static v => v.Id >= 0).Where(static (v, min) => v.Id >= min).Where(static v => !v.Flag).Build(), three, 50, "three filters");
		AssertPipeline(() => _cache.Query().Where(static v => v.Flag).UseIndex(_byGroup, 3), _cache.Prepare().Where(static v => v.Flag).UseIndex(_byGroup, 3).Build(),
			_cache.Prepare().Where(static v => v.Flag).UseIndex(_byGroup, 3).BuildFrozen(), default(NoArgs), "Where before list");
	}

	[Test]
	public void ListList_AndListListUnique_EveryArgSet() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var reversed = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		Assert.That(frozen.Explain(), Does.Contain("0 ListEq probe: value-side, 1 ListEq probe: value-side"));
		foreach (var (g, t) in new[] { (3, 3), (0, 0), (6, 39), (-1, 3), (3, 99), (2, 16) }) {
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t), prepared, frozen, (g, t), $"list∩list {g}/{t}");
			AssertSame(_cache.Query().UseIndex(_byTier, t).UseIndex(_byGroup, g).Execute(), reversed.Execute((g, t)));
		}

		var withUnique = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen();
		var uniqueFirst = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byCode, static a => a.code).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		Assert.That(withUnique.Explain(), Does.Contain("1 UniqueEq probe: key-side"));
		foreach (var (g, c) in new[] { (3, 1010), (3, 1011), (0, 1000), (-1, 1000), (3, -1) }) {
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, c),
				_cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).Build(), withUnique, (g, c), $"list∩unique {g}/{c}");
			AssertPipeline(() => _cache.Query().UseIndex(_byCode, c).UseIndex(_byGroup, g),
				_cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byCode, static a => a.code).UseIndex(_byGroup, static a => a.group).Build(), uniqueFirst, (g, c), $"unique∩list {g}/{c}");
		}
	}

	// ── (a) Parity: range ─────────────────────────────────────────────────────────

	[Test]
	public void Range_Alone_EveryBoundKind_AsSeed() {
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1100)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100)).BuildFrozen(), default(NoArgs), "gte");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1100)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100)).BuildFrozen(), default(NoArgs), "gt");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Lte(1010)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lte(1010)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lte(1010)).BuildFrozen(), default(NoArgs), "lte");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1010)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(1010)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(1010)).BuildFrozen(), default(NoArgs), "lt");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1100).Lt(1140)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100).Lt(1140)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100).Lt(1140)).BuildFrozen(), default(NoArgs), "gte lt");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1100).Lte(1140)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100).Lte(1140)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1100).Lte(1140)).BuildFrozen(), default(NoArgs), "gte lte");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1100).Lt(1140)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100).Lt(1140)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100).Lt(1140)).BuildFrozen(), default(NoArgs), "gt lt");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1100).Lte(1140)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100).Lte(1140)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gt(1100).Lte(1140)).BuildFrozen(), default(NoArgs), "gt lte");
		// Empty and inverted windows, windows past both ends.
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1140).Lt(1100)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1140).Lt(1100)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1140).Lt(1100)).BuildFrozen(), default(NoArgs), "inverted");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(5000)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(5000)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(5000)).BuildFrozen(), default(NoArgs), "past the end");
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Lt(0)), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(0)).Build(), _cache.Prepare().UseIndex(_codeRange, static rb => rb.Lt(0)).BuildFrozen(), default(NoArgs), "before the start");
	}

	[Test]
	public void ListRange_RangeList_Arg_EveryWindow() {
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var listRangePrepared = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		var rangeList = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi)).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		var rangeListPrepared = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi)).UseIndex(_byGroup, static a => a.group).Build();
		Assert.That(listRange.Explain(), Does.Contain("executor: Pipeline").And.Contain("0 ListEq probe: value-side, 1 Range probe: value-side"));
		foreach (var (g, lo, hi) in new[] { (3, 1050, 1200), (3, 1000, 1240), (0, 1239, 1240), (3, 1200, 1050), (5, 900, 1010), (-1, 1000, 1240), (3, 1050, 1051) }) {
			var args = (g, lo, hi);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args), listRangePrepared, listRange, args, $"list∩range {g} [{lo},{hi})");
			AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gt(a.lo).Lte(a.hi), args).UseIndex(_byGroup, g), rangeListPrepared, rangeList, args, $"range∩list {g} ({lo},{hi}]");
		}
	}

	[Test]
	public void Range_ReferenceKey_SeedAndProbe() {
		var frozen = _cache.Prepare<int, PqItem, (string lo, string hi)>().UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var probe = _cache.Prepare<int, PqItem, (string lo, string hi)>().UseIndex(_byGroup, 3).UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		foreach (var (lo, hi) in new[] { ("001050", "001200"), ("000000", "999999"), ("001200", "001100") }) {
			var args = (lo, hi);
			AssertPipeline(() => _cache.Query().UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args),
				_cache.Prepare<int, PqItem, (string lo, string hi)>().UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build(), frozen, args, "string range " + lo);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args),
				_cache.Prepare<int, PqItem, (string lo, string hi)>().UseIndex(_byGroup, 3).UseIndex(_codeText, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build(), probe, args, "list∩string range " + lo);
		}
	}

	[Test]
	public void OptionalRange_BothOneNoBound_AloneAndAfterList() {
		var alone = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).BuildFrozen();
		var alonePrepared = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).Build();
		var afterList = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_byGroup, 4).UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).BuildFrozen();
		var afterListPrepared = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_byGroup, 4).UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).Build();
		var beforeList = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).UseIndex(_byGroup, 4).Where(static v => v.Flag).BuildFrozen();
		var beforeListPrepared = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false).UseIndex(_byGroup, 4).Where(static v => v.Flag).Build();
		foreach (var (from, to) in new (int?, int?)[] { (1050, 1100), (1050, null), (null, 1100), (null, null), (1100, 1050) }) {
			var args = (from, to);
			AssertPipeline(() => EagerOptional(_cache.Query(), from, to), alonePrepared, alone, args, $"optional alone {from}/{to}");
			AssertPipeline(() => EagerOptional(_cache.Query().UseIndex(_byGroup, 4), from, to), afterListPrepared, afterList, args, $"optional after list {from}/{to}");
			// Unbounded first step: the list seeds instead — the eager `_first` rule.
			AssertPipeline(() => EagerOptional(_cache.Query(), from, to).UseIndex(_byGroup, 4).Where(static v => v.Flag), beforeListPrepared, beforeList, args, $"optional before list {from}/{to}");
		}

		// Bound optional forms, including the recorded no-op (all rows).
		AssertPipeline(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1050)), _cache.Prepare().UseIndex(_codeRange, 1050, null, fromInclusive: false).Build(), _cache.Prepare().UseIndex(_codeRange, 1050, null, fromInclusive: false).BuildFrozen(), default(NoArgs), "bound optional gt");
		AssertPipeline(() => _cache.Query(), _cache.Prepare().UseIndex(_codeRange, (int?)null, null).Build(), _cache.Prepare().UseIndex(_codeRange, (int?)null, null).BuildFrozen(), default(NoArgs), "bound optional unbounded = all rows");
		AssertPipeline(() => _cache.Query().Where(static v => v.Flag), _cache.Prepare().UseIndex(_codeRange, (int?)null, null).Where(static v => v.Flag).Build(),
			_cache.Prepare().UseIndex(_codeRange, (int?)null, null).Where(static v => v.Flag).BuildFrozen(), default(NoArgs), "all rows + Where");
	}

	private static EagerItems EagerOptional(EagerItems q, int? from, int? to) {
		if (from is not null && to is not null) return q.UseIndex(_codeRangeStatic!, static (rb, a) => rb.Gte(a.from!.Value).Lt(a.to!.Value), (from, to));
		if (from is not null) return q.UseIndex(_codeRangeStatic!, static (rb, lo) => rb.Gte(lo), from.Value);
		if (to is not null) return q.UseIndex(_codeRangeStatic!, static (rb, hi) => rb.Lt(hi), to.Value);
		return q;
	}

	// The eager optional twin needs the range index in a static helper; set alongside the fixture.
	private static CacheRangeIndex<int, PqItem, int>? _codeRangeStatic;

	[SetUp]
	public void PublishStatics() => _codeRangeStatic = _codeRange;

	// ── (a) Parity: key-set, last-updated ─────────────────────────────────────────

	[Test]
	public void KeySet_Alone_AfterList_BeforeList() {
		AssertPipeline(() => _cache.Query().UseIndex(_flagged), _cache.Prepare().UseIndex(_flagged).Build(), _cache.Prepare().UseIndex(_flagged).BuildFrozen(), default(NoArgs), "keyset");
		var afterList = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen();
		var beforeList = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).Where(static v => v.Id < 200).BuildFrozen();
		Assert.That(afterList.Explain(), Does.Contain("1 KeySet probe: value-side"));
		for (var g = -1; g < 7; g++) {
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_flagged), _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static x => x).UseIndex(_flagged).Build(), afterList, g, "list∩keyset " + g);
			AssertPipeline(() => _cache.Query().UseIndex(_flagged).UseIndex(_byGroup, g).Where(static v => v.Id < 200),
				_cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static x => x).Where(static v => v.Id < 200).Build(), beforeList, g, "keyset∩list " + g);
		}
	}

	[Test]
	public void LastUpdated_After_Between_AllTimeTypes_Arg_AsSeedAndProbe() {
		foreach (var i in new[] { -1, 0, 100, N - 1, N + 5 }) {
			var dto = DateTimeOffset.FromUnixTimeMilliseconds(Ms(i));
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, Ms(i)), _cache.Prepare().UseIndex(_lastUpdated, Ms(i)).Build(), _cache.Prepare().UseIndex(_lastUpdated, Ms(i)).BuildFrozen(), default(NoArgs), "after ms " + i);
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, dto), _cache.Prepare().UseIndex(_lastUpdated, dto).Build(), _cache.Prepare().UseIndex(_lastUpdated, dto).BuildFrozen(), default(NoArgs), "after dto " + i);
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, dto.UtcDateTime), _cache.Prepare().UseIndex(_lastUpdated, dto.UtcDateTime).Build(), _cache.Prepare().UseIndex(_lastUpdated, dto.UtcDateTime).BuildFrozen(), default(NoArgs), "after dt " + i);
		}

		foreach (var (f, t) in new[] { (10, 20), (0, 0), (50, 40), (-10, 5), (230, N + 10) })
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, Ms(f), Ms(t)), _cache.Prepare().UseIndex(_lastUpdated, Ms(f), Ms(t)).Build(), _cache.Prepare().UseIndex(_lastUpdated, Ms(f), Ms(t)).BuildFrozen(), default(NoArgs), $"between {f}..{t}");

		var after = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from).Build();
		var afterFrozen = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from).BuildFrozen();
		var listAfter = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, static a => a.from).BuildFrozen();
		var listBetween = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, static a => a.from, static a => a.to).BuildFrozen();
		var betweenList = _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from, static a => a.to).UseIndex(_byGroup, 2).BuildFrozen();
		Assert.That(listAfter.Explain(), Does.Contain("1 LastUpdatedAfter probe: key-side"));
		Assert.That(listBetween.Explain(), Does.Contain("1 LastUpdatedBetween probe: key-side"));
		foreach (var (f, t) in new[] { (0, 10), (100, 120), (200, 300), (30, 20), (-5, 1000) }) {
			var args = (from: Ms(f), to: Ms(t));
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, args.from), after, afterFrozen, args, "after arg " + f);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, args.from), _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, static a => a.from).Build(), listAfter, args, "list∩after " + f);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, args.from, args.to), _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, static a => a.from, static a => a.to).Build(), listBetween, args, $"list∩between {f}..{t}");
			AssertPipeline(() => _cache.Query().UseIndex(_lastUpdated, args.from, args.to).UseIndex(_byGroup, 2), _cache.Prepare<int, PqItem, (long from, long to)>().UseIndex(_lastUpdated, static a => a.from, static a => a.to).UseIndex(_byGroup, 2).Build(), betweenList, args, $"between∩list {f}..{t}");
		}
	}

	// ── (a) Parity: multi-value ───────────────────────────────────────────────────

	[Test]
	public void UniqueIn_EmptyOneManyMissing_AloneAndAfterList() {
		var alone = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).BuildFrozen();
		var alonePrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).Build();
		var afterList = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byCode, static a => a).BuildFrozen();
		var afterListPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byCode, static a => a).Build();
		var beforeList = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).UseIndex(_byGroup, 3).BuildFrozen();
		var beforeListPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).UseIndex(_byGroup, 3).Build();
		Assert.That(afterList.Explain(), Does.Contain("1 UniqueIn probe: key-side"));
		foreach (var codes in new int[][] { [], [1007], [1003, 1010, 1017, 1024], [1000, 1100, 1200, -1, 1000], [-1, -2] }) {
			ReadOnlyMemory<int> memory = codes;
			var offsetMemory = new int[] { 0, 1003, 1010, 0 }.AsMemory(1, 2);
			AssertPipeline(() => _cache.Query().UseIndex(_byCode, codes), alonePrepared, alone, memory, "unique in " + codes.Length);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byCode, codes), afterListPrepared, afterList, memory, "list∩unique in " + codes.Length);
			AssertPipeline(() => _cache.Query().UseIndex(_byCode, codes).UseIndex(_byGroup, 3), beforeListPrepared, beforeList, memory, "unique in∩list " + codes.Length);
			AssertPipeline(() => _cache.Query().UseIndex(_byCode, offsetMemory.Span), alonePrepared, alone, offsetMemory, "unique in, offset memory");
		}

		// Bound span.
		AssertPipeline(() => _cache.Query().UseIndex(_byCode, new[] { 1003, 1010 }), _cache.Prepare().UseIndex(_byCode, new[] { 1003, 1010 }).Build(), _cache.Prepare().UseIndex(_byCode, new[] { 1003, 1010 }).BuildFrozen(), default(NoArgs), "unique in bound");
		AssertPipeline(() => _cache.Query().UseIndex(_byCode, Array.Empty<int>()), _cache.Prepare().UseIndex(_byCode, Array.Empty<int>()).Build(), _cache.Prepare().UseIndex(_byCode, Array.Empty<int>()).BuildFrozen(), default(NoArgs), "unique in bound empty");
	}

	[Test]
	public void ListIn_EmptyOneManyDuplicatesMissing_AloneAndAfterList() {
		var alone = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).BuildFrozen();
		var alonePrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).Build();
		var afterTier = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byTier, 3).UseIndex(_byGroup, static a => a).BuildFrozen();
		var afterTierPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byTier, 3).UseIndex(_byGroup, static a => a).Build();
		var beforeTier = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).UseIndex(_byTier, 3).BuildFrozen();
		var beforeTierPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).UseIndex(_byTier, 3).Build();
		Assert.That(afterTier.Explain(), Does.Contain("1 ListIn probe: value-side"));
		foreach (var groups in new int[][] { [], [3], [1, 4, 6], [4, 1, 4, 9], [-1, 99] }) {
			ReadOnlyMemory<int> memory = groups;
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, groups), alonePrepared, alone, memory, "list in " + groups.Length);
			AssertPipeline(() => _cache.Query().UseIndex(_byTier, 3).UseIndex(_byGroup, groups), afterTierPrepared, afterTier, memory, "tier∩list in " + groups.Length);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, groups).UseIndex(_byTier, 3), beforeTierPrepared, beforeTier, memory, "list in∩tier " + groups.Length);
		}

		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, new[] { 6, 1 }), _cache.Prepare().UseIndex(_byGroup, new[] { 6, 1 }).Build(), _cache.Prepare().UseIndex(_byGroup, new[] { 6, 1 }).BuildFrozen(), default(NoArgs), "list in bound");
	}

	[Test]
	public void ListInProjected_AsSeedAndProbe_IncludingEmpty() {
		ReadOnlyMemory<PqItem> foreign = new[] { new PqItem { Group = 4 }, new PqItem { Group = 6 }, new PqItem { Group = 4 } };
		ReadOnlyMemory<PqItem> none = Array.Empty<PqItem>();
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, foreign.Span, static o => o.Group), _cache.Prepare().UseIndex(_byGroup, foreign, static o => o.Group).Build(),
			_cache.Prepare().UseIndex(_byGroup, foreign, static o => o.Group).BuildFrozen(), default(NoArgs), "projected seed");
		var probe = _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, foreign, static o => o.Group).BuildFrozen();
		Assert.That(probe.Explain(), Does.Contain("1 ListInProjected probe: value-side"));
		AssertPipeline(() => _cache.Query().UseIndex(_flagged).UseIndex(_byGroup, foreign.Span, static o => o.Group), _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, foreign, static o => o.Group).Build(),
			probe, default(NoArgs), "projected probe");
		AssertPipeline(() => _cache.Query().UseIndex(_flagged).UseIndex(_byGroup, none.Span, static o => o.Group), _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, none, static o => o.Group).Build(),
			_cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, none, static o => o.Group).BuildFrozen(), default(NoArgs), "projected empty = inactive");
	}

	// A collection-backed list index registers an entity under several keys: its buckets overlap, so a
	// multi-bucket seed must dedupe in bucket order (first occurrence wins), and its probe is key-side.
	[Test]
	public void CollectionBackedList_OverlappingBuckets_SeedDedupesAndProbesKeySide() {
		Assert.That(_byTags.HasKeySelector, Is.False);
		AssertPipeline(() => _cache.Query().UseIndex(_byTags, new[] { 3, 103, 5 }), _cache.Prepare().UseIndex(_byTags, new[] { 3, 103, 5 }).Build(), _cache.Prepare().UseIndex(_byTags, new[] { 3, 103, 5 }).BuildFrozen(), default(NoArgs), "tags in");
		AssertPipeline(() => _cache.Query().UseIndex(_byTags, 103), _cache.Prepare().UseIndex(_byTags, 103).Build(), _cache.Prepare().UseIndex(_byTags, 103).BuildFrozen(), default(NoArgs), "tags eq");
		var probe = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, 3).UseIndex(_byTags, static t => t).BuildFrozen();
		Assert.That(probe.Explain(), Does.Contain("1 ListEq probe: key-side"));
		foreach (var tag in new[] { 3, 103, 4, -1 })
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTags, tag), _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, 3).UseIndex(_byTags, static t => t).Build(), probe, tag, "group∩tags " + tag);
		var probeIn = _cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTags, new[] { 103, 4 }).BuildFrozen();
		Assert.That(probeIn.Explain(), Does.Contain("1 ListIn probe: key-side"));
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTags, new[] { 103, 4 }), _cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTags, new[] { 103, 4 }).Build(), probeIn, default(NoArgs), "group∩tags in");
	}

	// Seeds above the stack buffer (256 int keys) take the pool path; a dedupe set above 47 keys too.
	[Test]
	public void LargeSeeds_PoolPath_LikeEager() {
		var big = new InMemoryDataCache<int, PqItem>();
		var byGroup = big.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byCode = big.AddKeyValueIndex<int>(static (_, v) => v.Code);
		var range = big.CacheRangeIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < 3000; i++)
			big.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 3, Flag = i % 3 == 0 });
		var codes = new int[300];
		for (var i = 0; i < codes.Length; i++) codes[i] = 1000 + i * 7;
		AssertSame(big.Query().UseIndex(byGroup, 1).Where(static v => v.Id % 5 == 0).Execute(), big.Prepare().UseIndex(byGroup, 1).Where(static v => v.Id % 5 == 0).BuildFrozen().Execute());
		AssertSame(big.Query().UseIndex(range, static rb => rb.Gte(1100).Lt(2500)).Execute(), big.Prepare().UseIndex(range, static rb => rb.Gte(1100).Lt(2500)).BuildFrozen().Execute());
		AssertSame(big.Query().UseIndex(byCode, codes).Execute(), big.Prepare().UseIndex(byCode, codes).BuildFrozen().Execute());
		AssertSame(big.Query().UseIndex(byGroup, new[] { 2, 0, 2 }).Execute(), big.Prepare().UseIndex(byGroup, new[] { 2, 0, 2 }).BuildFrozen().Execute());
		Assert.That(big.Prepare().UseIndex(byGroup, 1).BuildFrozen().Count(), Is.EqualTo(1000));
	}

	// ── (c) Staleness under a paused writer, both directions of §14.1 ─────────────

	// An index registered before the real ones: the writer parks inside it after the store write and
	// before every index write, so a query observes store = NEW, indexes = OLD.
	private sealed class PausingIndex : ICacheIndex<int, PqItem> {
		private readonly ManualResetEventSlim _reached = new(false);
		private readonly ManualResetEventSlim _resume = new(true);
		private volatile bool _armed;

		public void Arm() {
			_reached.Reset();
			_resume.Reset();
			_armed = true;
		}

		public void WaitUntilParked() => Assert.That(_reached.Wait(TimeSpan.FromSeconds(10)), Is.True, "writer did not reach the pause");

		public void Resume() {
			_armed = false;
			_resume.Set();
		}

		private void Pause() {
			if (!_armed)
				return;
			_reached.Set();
			_resume.Wait();
		}

		void ICacheIndex<int, PqItem>.Add(int key, int keyHash, PqItem value, long timestampMs) => Pause();

		void ICacheIndex<int, PqItem>.Remove(int key, int keyHash, PqItem value, long timestampMs) => Pause();

		void ICacheIndex<int, PqItem>.Update(int key, int keyHash, PqItem originalValue, PqItem newValue, long timestampMs) => Pause();
	}

	private (InMemoryDataCache<int, PqItem> cache, PausingIndex pause, CacheKeyValueListIndex<int, PqItem, int> byGroup, CacheRangeIndex<int, PqItem, int> codeRange, CacheKeySetIndex<int, PqItem> flagged) PausableCache() {
		var cache = new InMemoryDataCache<int, PqItem>();
		var pause = new PausingIndex();
		cache.AddIndexForTests(pause);
		var byGroup = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var codeRange = cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		var flagged = cache.AddKeySetIndex(static (_, v) => v.Flag);
		for (var i = 0; i < N; i++)
			cache.AddOrUpdate(i, Make(i));
		return (cache, pause, byGroup, codeRange, flagged);
	}

	private static int[] Ids(QueryResults<PqItem> r) {
		try {
			var ids = new int[r.Count];
			for (var i = 0; i < r.Count; i++) ids[i] = r[i].Id;
			return ids;
		} finally {
			r.Dispose();
		}
	}

	private static void RunPaused(PausingIndex pause, Action write, Action whileParked) {
		pause.Arm();
		var writer = Task.Run(write);
		pause.WaitUntilParked();
		try {
			whileParked();
		} finally {
			pause.Resume();
			writer.Wait();
		}
	}

	[Test]
	public void Staleness_RangeProbe_BothDirections_ValueSideJudgesByTheReturnedValue_KeySideReproducesEager() {
		var (cache, pause, byGroup, codeRange, _) = PausableCache();
		// Row 24 (group 3, code 1024) inside the window [1020, 1030).
		var valueSide = cache.Prepare().UseIndex(byGroup, 3).UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).BuildFrozen();
		var keySide = cache.Prepare().UseIndex(byGroup, 3).UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		Assert.That(valueSide.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(valueSide.Explain(), Does.Contain("1 Range probe: value-side"));
		Assert.That(keySide.Plan.Executor, Is.EqualTo("Replay"), "the key-side range twin is a window walk: replay");
		Func<int[]> eager = () => Ids(cache.Query().UseIndex(byGroup, 3).UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).Execute());

		// Direction 1: store = NEW (outside), index still OLD (inside).
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 5024, Group = 3, Flag = true }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Contain(24), "eager returns the row through the stale index entry — with a value outside the window");
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows), "IndexSideProbes reproduces the eager window");
			using var rows = valueSide.Execute();
			for (var i = 0; i < rows.Count; i++)
				Assert.That(rows[i].Code, Is.InRange(1020, 1029), "every returned value satisfies the range");
			Assert.That(Ids(valueSide.Execute()), Does.Not.Contain(24));
			Assert.That(valueSide.Count(), Is.EqualTo(eagerRows.Length - 1));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()), "after the write lands, all agree");
		Assert.That(Ids(keySide.Execute()), Is.EqualTo(eager()));

		// Direction 2: store = NEW (inside), index still OLD (outside). Count seeds free (step 3): the range
		// window is the smaller signal, so its walk — the stale index — misses the row the fixed-seed
		// Execute returns through the group bucket; a free seed can miss a row whose index write has not
		// landed, never return one contradicting its value (both inside the contract).
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 3, Flag = true }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Not.Contain(24), "eager misses the row: the index still has it outside");
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows));
			var rows = Ids(valueSide.Execute());
			Assert.That(rows, Does.Contain(24), "the value is inside the window: the pipeline returns it");
			Assert.That(valueSide.Count(), Is.EqualTo(rows.Length - 1), "Count walks the range index (free seed) and misses the not-yet-indexed row");
			Assert.That(valueSide.Explain(), Does.Contain("last seed: step 1 Range").And.Contain("free: smallest signal"));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()));
	}

	[Test]
	public void Staleness_ListProbe_BothDirections() {
		var (cache, pause, byGroup, codeRange, _) = PausableCache();
		// Range seeds, the list probes. Row 24 is in group 3.
		var valueSide = cache.Prepare().UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).UseIndex(byGroup, 3).BuildFrozen();
		var keySide = cache.Prepare().UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).UseIndex(byGroup, 3).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		Assert.That(keySide.Plan.Executor, Is.EqualTo("Replay"), "a range step under IndexSideProbes replays");
		var keySideNoRange = cache.Prepare().UseIndex(byGroup, 3).UseIndex(byGroup, 3).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		Assert.That(keySideNoRange.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(keySideNoRange.Explain(), Does.Contain("1 ListEq probe: key-side"));
		Func<int[]> eager = () => Ids(cache.Query().UseIndex(codeRange, static rb => rb.Gte(1020).Lt(1030)).UseIndex(byGroup, 3).Execute());
		Func<int[]> eagerNoRange = () => Ids(cache.Query().UseIndex(byGroup, 3).UseIndex(byGroup, 3).Execute());

		// Direction 1: value moved out of group 3, index still lists it under 3.
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 5, Flag = true }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Contain(24));
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows));
			Assert.That(Ids(keySideNoRange.Execute()), Is.EqualTo(eagerNoRange()), "key-side list probe = eager window");
			using var rows = valueSide.Execute();
			for (var i = 0; i < rows.Count; i++)
				Assert.That(rows[i].Group, Is.EqualTo(3));
			Assert.That(Ids(valueSide.Execute()), Does.Not.Contain(24));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()));

		// Direction 2: value moved back into group 3, index still lists it under 5.
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 3, Flag = true }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Not.Contain(24));
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows));
			Assert.That(Ids(keySideNoRange.Execute()), Is.EqualTo(eagerNoRange()));
			Assert.That(Ids(valueSide.Execute()), Does.Contain(24));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()));
	}

	[Test]
	public void Staleness_KeySetProbe_BothDirections() {
		var (cache, pause, byGroup, _, flagged) = PausableCache();
		var valueSide = cache.Prepare().UseIndex(byGroup, 3).UseIndex(flagged).BuildFrozen();
		var keySide = cache.Prepare().UseIndex(byGroup, 3).UseIndex(flagged).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		Assert.That(keySide.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(keySide.Explain(), Does.Contain("1 KeySet probe: key-side"));
		Func<int[]> eager = () => Ids(cache.Query().UseIndex(byGroup, 3).UseIndex(flagged).Execute());

		// Row 24 is flagged (24 % 3 == 0). Direction 1: flag cleared in the store, key set still has it.
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 3, Flag = false }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Contain(24));
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows));
			using var rows = valueSide.Execute();
			for (var i = 0; i < rows.Count; i++)
				Assert.That(rows[i].Flag, Is.True);
			Assert.That(Ids(valueSide.Execute()), Does.Not.Contain(24));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()));

		// Direction 2: flag set in the store, key set does not have it yet.
		RunPaused(pause, () => cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 3, Flag = true }), () => {
			var eagerRows = eager();
			Assert.That(eagerRows, Does.Not.Contain(24));
			Assert.That(Ids(keySide.Execute()), Is.EqualTo(eagerRows));
			Assert.That(Ids(valueSide.Execute()), Does.Contain(24));
		});
		Assert.That(Ids(valueSide.Execute()), Is.EqualTo(eager()));
	}

	// ── (c) Concurrent writer ─────────────────────────────────────────────────────

	[Test]
	public void ConcurrentExecutions_AgainstAWriter_NeverThrow_ReturnConsistentRows_NoDuplicates() {
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var listKeySet = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen();
		var rangeList = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, 0L).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				// Moves rows across the range boundary, across buckets, in and out of the key set; removes and re-adds.
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 1000 + id + (i % 2 == 0 ? 0 : 500), Group = (id + i) % 7, Flag = i % 5 == 0 }, Ms(id) + i);
				if (i % 11 == 0) _cache.Remove((id * 13) % N);
				if (i % 11 == 5) _cache.AddOrUpdate((id * 13) % N, Make((id * 13) % N), Ms(id));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				var seen = new HashSet<int>();
				for (var i = 0; i < 2_000; i++) {
					var group = (seed + i) % 7;
					var lo = 1000 + i % 200;
					var hi = lo + 60;
					using (var rows = listRange.ExecutePooled((group, lo, hi))) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(rows[r].Code, Is.InRange(lo, hi - 1), "value-side range probe: the returned value satisfies the window");
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
						}
					}

					using (var rows = listKeySet.ExecutePooledCloned(group, 1, 5)) {
						for (var r = 0; r < rows.Count; r++)
							Assert.That(rows[r].Flag, Is.True, "value-side key-set probe");
					}

					using (var rows = rangeList.ExecutePooled((group, lo, hi)))
						Assert.That(rows.Count, Is.LessThanOrEqualTo(rows.TotalCount));
					Assert.That(listRange.Count((group, lo, hi)), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}

	// ── (d) Leaks ─────────────────────────────────────────────────────────────────

	[Test]
	public void Pooled_HitsMissesPages_EveryShape_LeaveNoRentedArrays() {
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var range = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Where(static v => v.Flag).BuildFrozen();
		var uniqueIn = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static a => a).BuildFrozen();
		var listIn = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, static a => a).UseIndex(_flagged).BuildFrozen();
		ReadOnlyMemory<PqItem> foreign = new[] { new PqItem { Group = 4 }, new PqItem { Group = 6 } };
		var projected = _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, foreign, static o => o.Group).BuildFrozen();
		var lastUpdated = _cache.Prepare<int, PqItem, long>().UseIndex(_byGroup, 2).UseIndex(_lastUpdated, static a => a).BuildFrozen();
		var allRows = _cache.Prepare().UseIndex(_codeRange, (int?)null, null).Where(static v => v.Flag).BuildFrozen();
		var codes = new int[100];
		for (var i = 0; i < codes.Length; i++) codes[i] = 1000 + i * 2;
		LeakAssert.Balanced(() => {
			listRange.ExecutePooled((3, 1000, 1240)).Dispose();
			listRange.ExecutePooled((3, 1000, 1240), 5, 5).Dispose();
			listRange.ExecutePooledCloned((3, 1000, 1240), 0, 1).Dispose();
			listRange.ExecutePooled((3, 1000, 1240), 500).Dispose();
			listRange.ExecutePooled((3, 1000, 1240), 0, 0).Dispose();
			listRange.ExecutePooled((-1, 1000, 1240)).Dispose();
			listRange.ExecutePooled((3, 1240, 1000)).Dispose();
			listRange.Count((3, 1000, 1240));
			range.ExecutePooled((1000, 1240)).Dispose();
			range.ExecutePooled((5000, 6000)).Dispose();
			uniqueIn.ExecutePooled(codes).Dispose();
			uniqueIn.ExecutePooled(Array.Empty<int>()).Dispose();
			uniqueIn.Count(codes);
			listIn.ExecutePooled(new[] { 1, 2, 3, 4, 5, 6, 0 }).Dispose();
			listIn.ExecutePooled(Array.Empty<int>()).Dispose();
			projected.ExecutePooled().Dispose();
			projected.Count();
			lastUpdated.ExecutePooled(Ms(10)).Dispose();
			allRows.ExecutePooled().Dispose();
			allRows.ExecutePooled(3, 7).Dispose();
			allRows.Count();
		});
	}

	[Test]
	public void ThrowingSelector_Predicate_AndClone_Propagate_LeaveNoRentedArrays_AndPlanStaysUsable() {
		var selector = _cache.Prepare<int, PqItem, (int group, int lo)>().UseIndex(_byGroup, static a => a.group)
			.UseIndex(_codeRange, static (rb, a) => a.lo < 0 ? throw new InvalidOperationException("boom") : rb.Gte(a.lo)).BuildFrozen();
		var predicate = _cache.Prepare<int, PqItem, (int group, int lo)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo))
			.Where(static (v, a) => v.Id > 20 && a.lo == -7 ? throw new InvalidOperationException("boom") : v.Flag).BuildFrozen();
		ReadOnlyMemory<PqItem> one = new[] { new PqItem { Group = 4 } };
		var projected = _cache.Prepare().UseIndex(_flagged).UseIndex(_byGroup, one, static o => o.Group < 0 ? 0 : throw new InvalidOperationException("boom")).BuildFrozen();
		Assert.That(selector.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(predicate.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((3, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.Count((3, -1)));
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled((3, -7)).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.Count((3, -7)));
			Assert.Throws<InvalidOperationException>(() => projected.ExecutePooled().Dispose());
		});
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1100)).Execute(), selector.Execute((3, 1100)));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1000)).Where(static v => v.Flag).Execute(), predicate.Execute((3, 1000)));

		// A throwing Clone on a pooled, cloned execution: the container's buffer goes back.
		var bombs = new InMemoryDataCache<int, PqBomb>();
		var byGroup = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var range = bombs.CacheRangeIndex<int>(static (_, v) => v.Id);
		for (var i = 0; i < 100; i++)
			bombs.AddOrUpdate(i, new PqBomb { Id = i, Group = i % 3 });
		var cloned = bombs.Prepare().UseIndex(byGroup, 0).UseIndex(range, static rb => rb.Gte(10)).BuildFrozen();
		Assert.That(cloned.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned(3, 5).Dispose()); // the page 21,24,27,30,33 holds the bomb
			cloned.ExecutePooledCloned(0, 5).Dispose(); // the page before it does not
			Assert.Throws<InvalidOperationException>(() => cloned.ExecuteCloned().Dispose());
		});
	}

	private sealed class PqBomb : ICacheEquatable<PqBomb>, ICacheClonable<PqBomb> {
		public int Id { get; init; }
		public int Group { get; init; }
		public bool CacheEquals(PqBomb? other) => other is not null && other.Id == Id && other.Group == Group;
		public int CacheGetHashCode() => HashCode.Combine(Id, Group);
		public PqBomb Clone() => Id == 30 ? throw new InvalidOperationException("clone boom") : new PqBomb { Id = Id, Group = Group };
	}

	// ── (f) Executor selection and Explain ────────────────────────────────────────

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	[Test]
	public void Pipeline_IsChosen_ForEveryNonCompositeKind_AndNotForTheRest() {
		var bigKeys = _cache.CacheRangeIndex<(long a, long b, long c)>(static (_, v) => (v.Code, v.Id, 0L));
		ReadOnlyMemory<PqItem> oneForeign = new[] { new PqItem { Group = 1 } };
		var refKeys = _cache.CacheRangeIndex<(string a, int b)>(static (_, v) => (v.Code.ToString(), v.Id));
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list eq");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, new[] { 1, 2 }).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list in");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, oneForeign, static o => o.Group).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list in projected");
			Assert.That(_cache.Prepare().UseIndex(_byCode, new[] { 1042 }).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique in");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).UseIndex(_byGroup, 3).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique eq + list");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"), "unique eq alone keeps the point lookup");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Where(static v => v.Flag).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"), "unique eq + filters keeps the point lookup");
			Assert.That(_cache.Prepare().UseIndex(_codeRange, static rb => rb.Gte(1)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "range");
			Assert.That(_cache.Prepare().UseIndex(_codeRange, (int?)null, null).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "optional range");
			Assert.That(_cache.Prepare().UseIndex(_flagged).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "key-set");
			Assert.That(_cache.Prepare().UseIndex(_lastUpdated, 0L).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "last-updated after");
			Assert.That(_cache.Prepare().UseIndex(_lastUpdated, 0L, 5L).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "last-updated between");
			Assert.That(_cache.Prepare().Where(static v => v.Flag).UseIndex(_byGroup, 3).Where(static v => v.Id > 0).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "filters around");
			Assert.That(_cache.Prepare().BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "no narrowing");
			Assert.That(_cache.Prepare().Where(static v => v.Flag).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filter only");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "Or (step 4)");
			Assert.That(_cache.Prepare<int, PqItem, bool>().UseIndex(_byGroup, 3).If(static c => c, b => b.UseIndex(_flagged)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "If (step 4)");
			Assert.That(_cache.Prepare<int, PqItem, int>().Match(static a => a, m => m.Case(0, b => b.UseIndex(_byGroup, 1))).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "Match (step 4)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "classic Sort (step 3: free seed into the sorting container)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "sort-bounded (step 6: fixed seed into the top-k container)");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen(new FrozenOptions { Pipeline = false }).Plan.Executor, Is.EqualTo("Replay"), "pipeline off");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen(new FrozenOptions { ReorderIndexNarrowers = true }).Plan.Executor, Is.EqualTo("Pipeline"), "reorder is the pipeline's free seed");
			Assert.That(_cache.Prepare<int, PqItem, (long a, long b, long c)>().UseIndex(_byGroup, 3).UseIndex(bigKeys, static (rb, a) => rb.Gte(a)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "an unmanaged key over 16 bytes replays");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(refKeys, static rb => rb.Gte(("1000", 0))).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a struct key with references replays");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_codeText, static rb => rb.Gte("001000")).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a reference key runs");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1)).BuildFrozen(new FrozenOptions { IndexSideProbes = true }).Plan.Executor, Is.EqualTo("Replay"), "index-side probes with a range step replay");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_flagged).BuildFrozen(new FrozenOptions { IndexSideProbes = true }).Plan.Executor, Is.EqualTo("Pipeline"), "index-side probes without a range step run");
		});
	}

	[Test]
	public void Explain_NamesTheExecutor_TheSeedRule_EachProbeSide_AndTheFusedOrder() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi))
			.UseIndex(_flagged).UseIndex(_lastUpdated, 0L).Where(static v => v.Id > 0).Where(static (v, a) => v.Id >= a.group).BuildFrozen();
		var text = frozen.Explain();
		Assert.Multiple(() => {
			Assert.That(text, Does.Contain("executor: Pipeline"));
			Assert.That(text, Does.Contain("pipeline: seed = fixed for Execute, free for Count"));
			Assert.That(text, Does.Contain("0 ListEq probe: value-side, 1 Range probe: value-side, 2 KeySet probe: value-side, 3 LastUpdatedAfter probe: key-side"));
			Assert.That(text, Does.Contain("filters: 2 (fused, order below)"));
			Assert.That(text, Does.Contain("fused filters: 2"));
			Assert.That(text, Does.Contain("optimizations: FusedFilters, AdaptiveFilterOrder"));
		});
		var keySide = _cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_flagged).UseIndex(_byTier, 3).Where(static v => v.Flag).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		Assert.That(keySide.Explain(), Does.Contain("1 KeySet probe: key-side, 2 ListEq probe: key-side").And.Contain("filters: 1 (direct)"));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_flagged).UseIndex(_byTier, 3).Where(static v => v.Flag).Execute(), keySide.Execute());
	}

	// The adaptive filter order is a pure permutation: rows and order are the eager ones on every
	// sampled and unsampled execution, and a run of 600 executions crosses two sampling points.
	[Test]
	public void FusedFilters_AdaptiveOrder_KeepsRowsAcrossSampling() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Id % 4 == 0).BuildFrozen();
		for (var round = 0; round < 600; round++) {
			var g = round % 7;
			AssertSame(_cache.Query().UseIndex(_byGroup, g).Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Id % 4 == 0).Execute(), frozen.Execute(g));
			Assert.That(frozen.Count(g), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Id % 4 == 0).Count()));
		}

		Assert.That(frozen.Explain(), Does.Contain("samples: "));
	}

	[Test]
	public void Reuse_AcrossMutations_SeesLiveData() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		Func<QueryResults<PqItem>> eager = () => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).Execute();
		AssertSame(eager(), frozen.Execute((3, 1000, 1100)));
		_cache.Remove(24);
		AssertSame(eager(), frozen.Execute((3, 1000, 1100)));
		_cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 3, Flag = false });
		AssertSame(eager(), frozen.Execute((3, 1000, 1100)));
		_cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1500, Group = 3, Flag = false });
		AssertSame(eager(), frozen.Execute((3, 1000, 1100)));
		_cache.AddOrUpdate(24, new PqItem { Id = 24, Code = 1024, Group = 5, Flag = false });
		AssertSame(eager(), frozen.Execute((3, 1000, 1100)));
		Assert.That(frozen.Count((3, 1000, 1100)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).Count()));
	}
}

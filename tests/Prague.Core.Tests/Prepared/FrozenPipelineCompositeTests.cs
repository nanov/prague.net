namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.TypeSystem;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// BuildFrozen() stage 3, step 4: composites as pipeline steps (design §5). If / IfElse / Match choose
// their arm once per execution at bind and the arm's steps and Where's take part like top-level ones;
// an Or probes as the OR of its branches' ANDs and, when it is the first narrowing, seeds by default
// from the union of its branches in branch order; under FrozenOptions.PreserveEagerOrder it reproduces
// the eager store walk kept to that union instead (byte-identical to eager), which Count and a classic
// Sort never need. Pinned here: (a) byte-identical eager == prepared == frozen through the opt-out on
// every composite shape × every Execute variant × the five pages with clone identity, unsorted, under
// Sort and SortBounded, and with fused joins; (b) Count and union-seed set parity on the default path;
// (d) leaks on every throwing path; (e) the executor selection, the fallbacks
// and the Explain output; (f) eight readers against a churning writer. Model: 240 items, Code = 1000 +
// Id, Group = Id % 7 (~34), Tier = Id % 40 (6), Tags = [Group, Group + 100] (collection index),
// Flag = Id % 3 == 0, last-updated = 1_000_000 + Id * 1000; customers 0..6 (even only), details for
// Id % 4 != 0.
[TestFixture]
public class FrozenPipelineCompositeTests {
	private const int N = 240;
	private const long BaseMs = 1_000_000L;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	public enum Mode { ByGroup, ByTier, ByCode, Unhandled }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];
	private static readonly (int skip, int take)[] Pages = [(0, int.MaxValue), (0, 1), (1, int.MaxValue), (0, 0), (5, 5)];
	// By default an Or-first seeds the union in branch order and every other plan seeds its smallest step;
	// EagerOrder is the opt-out that reproduces the eager sequence byte for byte — every sequence assertion
	// below uses it.
	private static readonly FrozenOptions EagerOrder = new() { PreserveEagerOrder = true };

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	private readonly struct ByFlag : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Flag ?? false).CompareTo(y?.Flag ?? false);
	}

	private readonly struct Bomb : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => throw new InvalidOperationException("compare boom");
	}

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTags = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;
	private CacheSymmetricKeyValueListIndex<int, PqItem, int> _bySym = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqCustomer> _details = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 40);
		_byTags = _cache.CacheCollectionKeyValueListIndex<int>(static (_, v) => [v.Group, v.Group + 100]);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		_bySym = _cache.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Group);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_details = new InMemoryDataCache<int, PqCustomer>();
		for (var i = 0; i < N; i++) {
			_cache.AddOrUpdate(i, Make(i), Ms(i));
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		for (var g = 0; g < 7; g += 2)
			_customers.AddOrUpdate(g, new PqCustomer { Id = g, Region = "EU" });
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static long Ms(int i) => BaseMs + i * 1000L;

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static QueryResults<T> Run<TArgs, T>(PreparedQuery<TArgs, T> q, in TArgs args, Variant v, int skip, int take)
		where TArgs : struct
		=> v switch {
		Variant.Execute => q.Execute(in args, skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(in args, skip, take),
		Variant.ExecutePooled => q.ExecutePooled(in args, skip, take),
		_ => q.ExecutePooledCloned(in args, skip, take),
	};

	private static QueryResults<PqItem> RunEager<TDisc, TResolver>(
		CacheQueryBuilderCombined<TDisc, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<TResolver>, PqItem> q, Variant v, int skip, int take)
		where TDisc : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		=> v switch {
			Variant.Execute => q.Execute(skip, take),
			Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
			Variant.ExecutePooled => q.ExecutePooled(skip, take),
			_ => q.ExecutePooledCloned(skip, take),
		};

	private static QueryResults<TResult> RunEagerJoined<TDisc, TChain, TResult>(
		CacheQueryBuilderCombined<TDisc, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, TChain, TResult> q, Variant v, int skip, int take)
		where TDisc : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TResult : struct, IJoinResult<PqItem>
		=> v switch {
			Variant.Execute => q.Execute(skip, take),
			Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
			Variant.ExecutePooled => q.ExecutePooled(skip, take),
			_ => q.ExecutePooledCloned(skip, take),
		};

	private static bool IsClone(Variant v) => v is Variant.ExecuteCloned or Variant.ExecutePooledCloned;

	// The sorted builders carry SortedQuery<TDisc> and bind a separate extension family; same body.
	private static QueryResults<PqItem> RunEager<TDisc, TResolver>(
		CacheQueryBuilderCombined<SortedQuery<TDisc>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<TResolver>, PqItem> q, Variant v, int skip, int take)
		where TDisc : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		=> v switch {
			Variant.Execute => q.Execute(skip, take),
			Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
			Variant.ExecutePooled => q.ExecutePooled(skip, take),
			_ => q.ExecutePooledCloned(skip, take),
		};

	private static QueryResults<TResult> RunEagerJoined<TDisc, TChain, TResult>(
		CacheQueryBuilderCombined<SortedQuery<TDisc>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, TChain, TResult> q, Variant v, int skip, int take)
		where TDisc : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TResult : struct, IJoinResult<PqItem>
		=> v switch {
			Variant.Execute => q.Execute(skip, take),
			Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
			Variant.ExecutePooled => q.ExecutePooled(skip, take),
			_ => q.ExecutePooledCloned(skip, take),
		};

	private void AssertPipeline<TArgs, TDisc, TResolver>(
		Func<CacheQueryBuilderCombined<SortedQuery<TDisc>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<TResolver>, PqItem>> eager,
		PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label)
		where TDisc : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TArgs : struct {
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

	private void AssertPipelineJoined<TArgs, TDisc, TChain, TResult>(
		Func<CacheQueryBuilderCombined<SortedQuery<TDisc>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, TChain, TResult>> eager,
		PreparedQuery<TArgs, TResult> prepared, FrozenQuery<TArgs, TResult> frozen, TArgs args, Func<TResult, string> row, string label)
		where TDisc : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TResult : struct, IJoinResult<PqItem>
		where TArgs : struct {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				AssertSameJoined(RunEagerJoined(eager(), v, skip, take), Run(frozen, in args, v, skip, take), row);
				AssertSameJoined(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), row);
			}

		Assert.That(frozen.Count(in args), Is.EqualTo(eager().Count()), label + " Count");
	}


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
				Assert.That(ReferenceEquals(frozen[i], cached), Is.EqualTo(!clone), label + (clone ? " clone must be a fresh instance" : " non-clone must be the cached instance"));
			}
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>The (a) contract: the pipeline executor, every variant × page byte-identical to eager (with identity) and to prepared, Count three ways.</summary>
	private void AssertPipeline<TArgs, TDisc, TResolver>(
		Func<CacheQueryBuilderCombined<TDisc, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<TResolver>, PqItem>> eager,
		PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label)
		where TDisc : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TArgs : struct {
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

	private void AssertPipelineJoined<TArgs, TDisc, TChain, TResult>(
		Func<CacheQueryBuilderCombined<TDisc, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, TChain, TResult>> eager,
		PreparedQuery<TArgs, TResult> prepared, FrozenQuery<TArgs, TResult> frozen, TArgs args, Func<TResult, string> row, string label)
		where TDisc : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TResult : struct, IJoinResult<PqItem>
		where TArgs : struct {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				AssertSameJoined(RunEagerJoined(eager(), v, skip, take), Run(frozen, in args, v, skip, take), row);
				AssertSameJoined(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), row);
			}

		Assert.That(frozen.Count(in args), Is.EqualTo(eager().Count()), label + " Count");
	}

	private static string Customer(JoinResult<PqItem, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static string CustomerDetail(JoinResult<PqItem, PqCustomer?, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";

	private static int[] Ids(QueryResults<PqItem> rows) {
		using (rows) {
			var ids = new int[rows.Count];
			for (var i = 0; i < rows.Count; i++) ids[i] = rows[i].Id;
			return ids;
		}
	}

	// ── (a) Or ────────────────────────────────────────────────────────────────────

	// Or as the first narrowing: the eager store walk in store order kept to the union. Includes the
	// same bucket twice (dedupe), a missing bucket (one branch empty in effect), a Where before (the
	// eager auto-seed applies it during the walk) and after.
	[TestCase(1, 4)]
	[TestCase(0, 0)]
	[TestCase(1, 99)]
	[TestCase(99, 98)]
	public void Or_First_TwoLists_StoreOrder_EveryVariantAndPage(int g1, int g2) {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		var frozen = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(EagerOrder);
		AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, g1), b => b.UseIndex(_byGroup, g2)), prepared, frozen, (g1, g2), "or first");

		var whereBefore = _cache.Prepare<int, PqItem, (int g1, int g2)>().Where(static v => v.Flag).Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(EagerOrder);
		var whereBeforeP = _cache.Prepare<int, PqItem, (int g1, int g2)>().Where(static v => v.Flag).Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		AssertPipeline(() => _cache.Query().Where(static v => v.Flag).Or(b => b.UseIndex(_byGroup, g1), b => b.UseIndex(_byGroup, g2)), whereBeforeP, whereBefore, (g1, g2), "where before");

		var whereAfter = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Where(static v => v.Flag).Where(static (v, in a) => v.Id > a.g1).BuildFrozen(EagerOrder);
		var whereAfterP = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Where(static v => v.Flag).Where(static (v, in a) => v.Id > a.g1).Build();
		AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, g1), b => b.UseIndex(_byGroup, g2)).Where(static v => v.Flag).Where(v => v.Id > g1), whereAfterP, whereAfter, (g1, g2), "where after");
		frozen.Execute((g1, g2)).Dispose();
		Assert.That(frozen.Explain(), Does.Contain("last seed: step 0 Or").And.Contain("fixed: first active step — the store walk kept to the branch union"));
		Assert.That(frozen.Plan.Optimizations, Does.Contain("PreserveEagerOrder"));
		// The default: the same rows and Count in the union's order.
		var union = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		Assert.That(union.Plan.Optimizations, Does.Not.Contain("PreserveEagerOrder"));
		Assert.That(Ids(union.Execute((g1, g2))), Is.EquivalentTo(Ids(_cache.Query().Or(b => b.UseIndex(_byGroup, g1), b => b.UseIndex(_byGroup, g2)).Execute())));
		Assert.That(union.Count((g1, g2)), Is.EqualTo(_cache.Query().Or(b => b.UseIndex(_byGroup, g1), b => b.UseIndex(_byGroup, g2)).Count()));
	}

	// Or after a narrowing is a probe: the seed's order, each candidate admitted by some branch.
	[Test]
	public void Or_AfterList_AfterUnique_Probe_HitAndMiss() {
		var afterList = _cache.Prepare<int, PqItem, (int g, int t1, int t2)>().UseIndex(_byGroup, static a => a.g).Or(b => b.UseIndex(_byTier, static a => a.t1), b => b.UseIndex(_byTier, static a => a.t2)).BuildFrozen();
		var afterListP = _cache.Prepare<int, PqItem, (int g, int t1, int t2)>().UseIndex(_byGroup, static a => a.g).Or(b => b.UseIndex(_byTier, static a => a.t1), b => b.UseIndex(_byTier, static a => a.t2)).Build();
		foreach (var args in new[] { (3, 3, 10), (3, 99, 10), (3, 99, 98), (0, 0, 0) })
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, args.Item1).Or(b => b.UseIndex(_byTier, args.Item2), b => b.UseIndex(_byTier, args.Item3)), afterListP, afterList, args, "list then or " + args);

		var afterUnique = _cache.Prepare<int, PqItem, (int code, int g1, int g2)>().UseIndex(_byCode, static a => a.code).Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var afterUniqueP = _cache.Prepare<int, PqItem, (int code, int g1, int g2)>().UseIndex(_byCode, static a => a.code).Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		foreach (var args in new[] { (1015, 1, 3), (1016, 1, 3), (1015, 1, 5), (99, 1, 3) })
			AssertPipeline(() => _cache.Query().UseIndex(_byCode, args.Item1).Or(b => b.UseIndex(_byGroup, args.Item2), b => b.UseIndex(_byGroup, args.Item3)), afterUniqueP, afterUnique, args, "unique then or " + args);

		// A range branch (value-side) with a list after it and a key-set branch (value-side) with a
		// collection-index leaf after it (key-side); then a last-updated-first branch (its own index) with a
		// unique leaf after it beside a list branch.
		var mixed = _cache.Prepare<int, PqItem, (int g, int lo, int hi, int tag, long after)>().UseIndex(_byGroup, static a => a.g)
			.Or(b => b.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byTags, static a => a.tag - 100), b => b.UseIndex(_flagged).UseIndex(_byTags, static a => a.tag)).BuildFrozen();
		var mixedP = _cache.Prepare<int, PqItem, (int g, int lo, int hi, int tag, long after)>().UseIndex(_byGroup, static a => a.g)
			.Or(b => b.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byTags, static a => a.tag - 100), b => b.UseIndex(_flagged).UseIndex(_byTags, static a => a.tag)).Build();
		var updated = _cache.Prepare<int, PqItem, (int g, int lo, int hi, int tag, long after)>().UseIndex(_byGroup, static a => a.g)
			.Or(b => b.UseIndex(_lastUpdated, static a => a.after).UseIndex(_byCode, 1177), b => b.UseIndex(_byTier, static a => a.lo)).BuildFrozen();
		var updatedP = _cache.Prepare<int, PqItem, (int g, int lo, int hi, int tag, long after)>().UseIndex(_byGroup, static a => a.g)
			.Or(b => b.UseIndex(_lastUpdated, static a => a.after).UseIndex(_byCode, 1177), b => b.UseIndex(_byTier, static a => a.lo)).Build();
		foreach (var args in new[] { (2, 1050, 1100, 102, Ms(100)), (2, 1050, 1100, 5, Ms(0)), (5, 1, 2, 105, Ms(300)), (2, 1050, 1100, 102, Ms(200)) }) {
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, args.Item1).Or(b => b.UseIndex(_codeRange, rb => rb.Gte(args.Item2).Lt(args.Item3)).UseIndex(_byTags, args.Item4 - 100), b => b.UseIndex(_flagged).UseIndex(_byTags, args.Item4)),
				mixedP, mixed, args, "mixed " + args);
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, args.Item1).Or(b => b.UseIndex(_lastUpdated, args.Item5).UseIndex(_byCode, 1177), b => b.UseIndex(_byTier, args.Item2)),
				updatedP, updated, args, "updated " + args);
		}
	}

	// Inside an Or branch the eager core marks (unions) a range / key-set / last-updated step that is
	// not the branch's first, where it intersects a unique / list one — and drops it when the branch was
	// already cleared: a set-level rule. Those shapes replay, and still agree with eager.
	[Test]
	public void Or_MarkingStepNotFirstInABranch_Replays_LikeEager() {
		var listThenRange = _cache.Prepare().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1100)), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		var listThenKeySet = _cache.Prepare().UseIndex(_byTier, 2).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_flagged), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		var listThenUpdated = _cache.Prepare().UseIndex(_byTier, 2).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100)), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		var underIf = _cache.Prepare<int, PqItem, bool>().UseIndex(_byTier, 2).Or(b => b.UseIndex(_byGroup, 2).If(static c => c, c => c.UseIndex(_flagged)), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		var rangeFirstThenList = _cache.Prepare().UseIndex(_byTier, 2).Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1100)).UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		var underIfFirst = _cache.Prepare<int, PqItem, bool>().UseIndex(_byTier, 2).Or(b => b.If(static c => c, c => c.UseIndex(_flagged)).UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		Assert.Multiple(() => {
			Assert.That(listThenRange.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(listThenKeySet.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(listThenUpdated.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(underIf.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(rangeFirstThenList.Plan.Executor, Is.EqualTo("Pipeline"), "first in its branch: the pipeline");
			Assert.That(underIfFirst.Plan.Executor, Is.EqualTo("Pipeline"), "first in its branch through a nested If: the pipeline");
		});
		AssertSame(_cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1100)), b => b.UseIndex(_byGroup, 5)).Execute(), listThenRange.Execute());
		AssertSame(_cache.Query().UseIndex(_byTier, 2).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_flagged), b => b.UseIndex(_byGroup, 5)).Execute(), listThenKeySet.Execute());
		AssertSame(_cache.Query().UseIndex(_byTier, 2).Or(b => b.UseIndex(_byGroup, 2).UseIndex(_lastUpdated, Ms(100)), b => b.UseIndex(_byGroup, 5)).Execute(), listThenUpdated.Execute());
		AssertSame(_cache.Query().UseIndex(_byTier, 2).Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1100)).UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 5)).Execute(), rangeFirstThenList.Execute());
		foreach (var c in new[] { true, false }) {
			AssertSame(_cache.Query().UseIndex(_byTier, 2).Or(b => c ? b.UseIndex(_byGroup, 2).UseIndex(_flagged) : b.UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 5)).Execute(), underIf.Execute(c));
			AssertSame(_cache.Query().UseIndex(_byTier, 2).Or(b => c ? b.UseIndex(_flagged).UseIndex(_byGroup, 2) : b.UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 5)).Execute(), underIfFirst.Execute(c));
		}
	}

	// A list then an Or of two uniques: the Or is the small step — its union (two keys at most) is walked,
	// the list's order under the opt-out, the branch order by default.
	[Test]
	public void Or_OfUniques_AfterList_SeedsTheOr() {
		var frozen = _cache.Prepare<int, PqItem, (int g, int c1, int c2)>().UseIndex(_byGroup, static a => a.g).Or(b => b.UseIndex(_byCode, static a => a.c1), b => b.UseIndex(_byCode, static a => a.c2)).BuildFrozen(EagerOrder);
		var prepared = _cache.Prepare<int, PqItem, (int g, int c1, int c2)>().UseIndex(_byGroup, static a => a.g).Or(b => b.UseIndex(_byCode, static a => a.c1), b => b.UseIndex(_byCode, static a => a.c2)).Build();
		foreach (var args in new[] { (3, 1010, 1003), (3, 1003, 1010), (3, 1010, 1010), (3, 1011, 1012), (3, 99, 98) })
			AssertPipeline(() => _cache.Query().UseIndex(_byGroup, args.Item1).Or(b => b.UseIndex(_byCode, args.Item2), b => b.UseIndex(_byCode, args.Item3)), prepared, frozen, args, "list then or of uniques " + args);
		frozen.Execute((3, 1010, 1003)).Dispose();
		Assert.That(frozen.Explain(), Does.Contain("last seed: step 0 ListEq (signal 0), fixed: first active step"), "the opt-out walks the list");
		Assert.That(Ids(frozen.Execute((3, 1010, 1003))), Is.EqualTo(new[] { 3, 10 }), "the list's order, not the branch order");
		// The default seeds the Or's union — the same two rows, in branch order.
		var free = _cache.Prepare<int, PqItem, (int g, int c1, int c2)>().UseIndex(_byGroup, static a => a.g).Or(b => b.UseIndex(_byCode, static a => a.c1), b => b.UseIndex(_byCode, static a => a.c2)).BuildFrozen();
		free.Execute((3, 1010, 1003)).Dispose();
		Assert.That(free.Explain(), Does.Contain("last seed: step 1 Or (signal 2), free: smallest signal"));
		Assert.That(Ids(free.Execute((3, 1010, 1003))), Is.EqualTo(new[] { 10, 3 }), "the branch order");
	}

	// A branch with two narrowers is their AND; the Or keeps its probe after seeding its union (the first
	// leaves are a superset). As the first narrowing and after a list.
	[Test]
	public void Or_BranchWithTwoNarrowers_First_AndAfterList() {
		var first = _cache.Prepare<int, PqItem, (int g1, int t, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_byTier, static a => a.t), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(EagerOrder);
		var firstP = _cache.Prepare<int, PqItem, (int g1, int t, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_byTier, static a => a.t), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		var after = _cache.Prepare<int, PqItem, (int g1, int t, int g2)>().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_byTier, static a => a.t), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var afterP = _cache.Prepare<int, PqItem, (int g1, int t, int g2)>().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_byTier, static a => a.t), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		foreach (var args in new[] { (1, 8, 6), (1, 99, 6), (1, 8, 1), (2, 2, 99) }) {
			AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1).UseIndex(_byTier, args.Item2), b => b.UseIndex(_byGroup, args.Item3)), firstP, first, args, "first " + args);
			AssertPipeline(() => _cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, args.Item1).UseIndex(_byTier, args.Item2), b => b.UseIndex(_byGroup, args.Item3)), afterP, after, args, "after " + args);
			Assert.That(first.Count(args), Is.EqualTo(_cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1).UseIndex(_byTier, args.Item2), b => b.UseIndex(_byGroup, args.Item3)).Count()), "the union seed keeps the probe");
		}
	}

	// No-op branches (q => q): one → the other branch alone; both and not first → the Or is inactive;
	// both and first → the eager OrWith has already auto-seeded the store and cleared `_first`, so the
	// next index step intersects the store walk: every row of that bucket in STORE order, not bucket
	// order — reproduced, not "fixed".
	[Test]
	public void Or_NoOpBranches_OneOrBoth_First_AndAfterList() {
		var oneAfter = _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byTier, 3), b => b).BuildFrozen();
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byTier, 3), b => b), _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byTier, 3), b => b).Build(), oneAfter, default(NoArgs), "one no-op after");
		var oneFirst = _cache.Prepare().Or(b => b, b => b.UseIndex(_byTier, 3)).UseIndex(_byGroup, 3).BuildFrozen(EagerOrder);
		AssertPipeline(() => _cache.Query().Or(b => b, b => b.UseIndex(_byTier, 3)).UseIndex(_byGroup, 3), _cache.Prepare().Or(b => b, b => b.UseIndex(_byTier, 3)).UseIndex(_byGroup, 3).Build(), oneFirst, default(NoArgs), "one no-op first");
		var bothAfter = _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b, b => b).BuildFrozen();
		AssertPipeline(() => _cache.Query().UseIndex(_byGroup, 3).Or(b => b, b => b), _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b, b => b).Build(), bothAfter, default(NoArgs), "both no-op after");
		var bothFirst = _cache.Prepare().Or(b => b, b => b).UseIndex(_byGroup, 3).BuildFrozen(EagerOrder);
		AssertPipeline(() => _cache.Query().Or(b => b, b => b).UseIndex(_byGroup, 3), _cache.Prepare().Or(b => b, b => b).UseIndex(_byGroup, 3).Build(), bothFirst, default(NoArgs), "both no-op first");
		var bothAlone = _cache.Prepare().Or(b => b, b => b).BuildFrozen();
		AssertPipeline(() => _cache.Query().Or(b => b, b => b), _cache.Prepare().Or(b => b, b => b).Build(), bothAlone, default(NoArgs), "both no-op alone");
		bothFirst.Execute().Dispose();
		Assert.That(bothFirst.Explain(), Does.Contain("step 0 Or → no branch narrowed (first: admits every row)").And.Contain("last seed: step 0 Or (signal 2147483647), fixed: first active step — the Or narrowed nothing: the store walk"));
		Assert.That(bothFirst.Count(), Is.EqualTo(34));
		Assert.That(bothFirst.Explain(), Does.Contain("last seed: step 1 ListEq (signal 34), free: smallest signal"), "Count leaves the no-op Or for the list");
		Assert.That(bothAfter.Explain(), Does.Contain("step 1 Or → no branch narrowed (inactive)"));
	}

	// Inside an Or branch an empty In span is not a no-op: the eager branch core clears its bitmap (the
	// branch admits nothing), for unique and list indexes alike; both branches empty → no rows. At the
	// top level (inside an If) an empty unique In still empties the whole query and an empty list In
	// is still a no-op.
	[Test]
	public void Or_EmptyInSpanInABranch_EmptiesTheBranch_TopLevelRulesUnchanged() {
		ReadOnlyMemory<int> none = Array.Empty<int>();
		ReadOnlyMemory<int> codes = new[] { 1003, 1010, 1017, -5 };
		ReadOnlyMemory<int> groups = new[] { 1, 4 };
		var uniqueIn = _cache.Prepare<int, PqItem, (ReadOnlyMemory<int> codes, int g)>().UseIndex(_flagged).Or(b => b.UseIndex(_byCode, static a => a.codes), b => b.UseIndex(_byGroup, static a => a.g)).BuildFrozen();
		var uniqueInP = _cache.Prepare<int, PqItem, (ReadOnlyMemory<int> codes, int g)>().UseIndex(_flagged).Or(b => b.UseIndex(_byCode, static a => a.codes), b => b.UseIndex(_byGroup, static a => a.g)).Build();
		foreach (var args in new[] { (codes, 2), (none, 2), (none, 99), (codes, 99) })
			AssertPipeline(() => _cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byCode, args.Item1.Span), b => b.UseIndex(_byGroup, args.Item2)), uniqueInP, uniqueIn, args, "unique In in a branch");

		var listIn = _cache.Prepare<int, PqItem, (ReadOnlyMemory<int> groups, int t)>().Or(b => b.UseIndex(_byGroup, static a => a.groups), b => b.UseIndex(_byTier, static a => a.t)).BuildFrozen(EagerOrder);
		var listInP = _cache.Prepare<int, PqItem, (ReadOnlyMemory<int> groups, int t)>().Or(b => b.UseIndex(_byGroup, static a => a.groups), b => b.UseIndex(_byTier, static a => a.t)).Build();
		foreach (var args in new[] { (groups, 5), (none, 5), (none, 99), (groups, 99) })
			AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1.Span), b => b.UseIndex(_byTier, args.Item2)), listInP, listIn, args, "list In in a branch");
		Assert.That(listIn.Count((none, 99)), Is.Zero, "both branches empty: no rows");
		Assert.That(listIn.Explain(), Does.Contain("step 0 Or → branches {"));

		var topIf = _cache.Prepare<int, PqItem, (bool cond, ReadOnlyMemory<int> codes, ReadOnlyMemory<int> groups)>().UseIndex(_flagged)
			.If(static a => a.cond, b => b.UseIndex(_byCode, static a => a.codes)).If(static a => !a.cond, b => b.UseIndex(_byGroup, static a => a.groups)).BuildFrozen();
		AssertSame(_cache.Query().UseIndex(_flagged).UseIndex(_byCode, none.Span).Execute(), topIf.Execute((true, none, groups)));
		Assert.That(topIf.Count((true, none, groups)), Is.Zero, "an empty unique In at the top level empties the query");
		AssertSame(_cache.Query().UseIndex(_flagged).Execute(), topIf.Execute((false, codes, none)));
		Assert.That(topIf.Count((false, codes, none)), Is.EqualTo(_cache.Query().UseIndex(_flagged).Count()), "an empty list In at the top level is a no-op");
	}

	// A nested Or that is its branch's only narrower flattens into the outer Or (its branches admit rows
	// but do not count as narrowing, as the eager nested OrWith leaves the branch's `_first` set); a
	// nested Or beside another narrower in the same branch replays.
	[Test]
	public void Or_Nested_SoleBranchNode_Flattens_BesideALeaf_Replays() {
		var nested = _cache.Prepare<int, PqItem, (int g, int t1, int t2)>().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, static a => a.g), b => b.Or(c => c.UseIndex(_byTier, static a => a.t1), c => c.UseIndex(_byTier, static a => a.t2))).BuildFrozen(EagerOrder);
		var nestedP = _cache.Prepare<int, PqItem, (int g, int t1, int t2)>().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, static a => a.g), b => b.Or(c => c.UseIndex(_byTier, static a => a.t1), c => c.UseIndex(_byTier, static a => a.t2))).Build();
		foreach (var args in new[] { (0, 2, 4), (99, 2, 4), (0, 99, 98), (99, 99, 98) })
			AssertPipeline(() => _cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, args.Item1), b => b.Or(c => c.UseIndex(_byTier, args.Item2), c => c.UseIndex(_byTier, args.Item3))), nestedP, nested, args, "nested " + args);
		Assert.That(nested.Explain(), Does.Contain("branch 2 (from a nested Or): [step 3 ListEq]; branch 3 (from a nested Or): [step 4 ListEq]"));

		var nestedFirst = _cache.Prepare().Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4))).BuildFrozen(EagerOrder);
		AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4))),
			_cache.Prepare().Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4))).Build(), nestedFirst, default(NoArgs), "nested first");
		// Every outer branch a nested Or: nothing counts as narrowing — the eager Or is a no-op (all rows).
		var allNested = _cache.Prepare().Or(b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4)), b => b.Or(c => c.UseIndex(_byGroup, 1), c => c.UseIndex(_byGroup, 3))).UseIndex(_byTier, 3).BuildFrozen(EagerOrder);
		AssertPipeline(() => _cache.Query().Or(b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4)), b => b.Or(c => c.UseIndex(_byGroup, 1), c => c.UseIndex(_byGroup, 3))).UseIndex(_byTier, 3),
			_cache.Prepare().Or(b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4)), b => b.Or(c => c.UseIndex(_byGroup, 1), c => c.UseIndex(_byGroup, 3))).UseIndex(_byTier, 3).Build(), allNested, default(NoArgs), "all nested");

		var beside = _cache.Prepare().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 0).Or(c => c.UseIndex(_byTier, 2), c => c.UseIndex(_byTier, 4)), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		Assert.That(beside.Plan.Executor, Is.EqualTo("Replay"), "a nested Or beside a leaf in its branch replays");
		AssertSame(_cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 0).Or(c => c.UseIndex(_byTier, 2), c => c.UseIndex(_byTier, 4)), b => b.UseIndex(_byGroup, 5)).Execute(), beside.Execute());
	}

	[Test]
	public void TwoOrs_AndAnOrOfEightBranches() {
		var two = _cache.Prepare<int, PqItem, (int g1, int g2, int t1, int t2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Or(b => b.UseIndex(_byTier, static a => a.t1), b => b.UseIndex(_byTier, static a => a.t2)).BuildFrozen(EagerOrder);
		var twoP = _cache.Prepare<int, PqItem, (int g1, int g2, int t1, int t2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Or(b => b.UseIndex(_byTier, static a => a.t1), b => b.UseIndex(_byTier, static a => a.t2)).Build();
		foreach (var args in new[] { (1, 2, 0, 4), (1, 2, 99, 4), (99, 98, 0, 4) })
			AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1), b => b.UseIndex(_byGroup, args.Item2)).Or(b => b.UseIndex(_byTier, args.Item3), b => b.UseIndex(_byTier, args.Item4)), twoP, two, args, "two ors " + args);

		// Eight branches after flattening (the limit): a three-level nesting of sole-node Ors.
		var eight = _cache.Prepare().UseIndex(_flagged).Or(
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 0), d => d.UseIndex(_byTier, 1)), c => c.Or(d => d.UseIndex(_byTier, 2), d => d.UseIndex(_byTier, 3))),
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 4), d => d.UseIndex(_byTier, 5)), c => c.Or(d => d.UseIndex(_byTier, 6), d => d.UseIndex(_byTier, 7)))).BuildFrozen();
		Assert.That(eight.Plan.Executor, Is.EqualTo("Pipeline"));
		AssertSame(_cache.Query().UseIndex(_flagged).Or(
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 0), d => d.UseIndex(_byTier, 1)), c => c.Or(d => d.UseIndex(_byTier, 2), d => d.UseIndex(_byTier, 3))),
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 4), d => d.UseIndex(_byTier, 5)), c => c.Or(d => d.UseIndex(_byTier, 6), d => d.UseIndex(_byTier, 7)))).Execute(), eight.Execute());
		var nine = _cache.Prepare().UseIndex(_flagged).Or(
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 0), d => d.UseIndex(_byTier, 1)), c => c.Or(d => d.UseIndex(_byTier, 2), d => d.UseIndex(_byTier, 3))),
			b => b.Or(c => c.Or(d => d.UseIndex(_byTier, 4), d => d.UseIndex(_byTier, 5)), c => c.Or(d => d.Or(e => e.UseIndex(_byTier, 6), e => e.UseIndex(_byTier, 8)), d => d.UseIndex(_byTier, 7)))).BuildFrozen();
		Assert.That(nine.Plan.Executor, Is.EqualTo("Replay"), "nine branches after flattening replay");
	}

	// ── (a) If / IfElse / Match ───────────────────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void If_First_AndAfterList_AndWithWhere_AndTwoOpBranch_BothOutcomes(bool cond) {
		var first = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g)).UseIndex(_byTier, static a => a.t).BuildFrozen();
		var firstP = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g)).UseIndex(_byTier, static a => a.t).Build();
		AssertPipeline(() => { var q = _cache.Query(); if (cond) q = q.UseIndex(_byGroup, 3); return q.UseIndex(_byTier, 3); }, firstP, first, (cond, 3, 3), "if first");
		Assert.That(first.Explain(), Does.Contain(cond ? "select#0 → arm 0" : "select#0 → arm 1"), "the skipped side is the empty Default arm");

		var alone = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g)).BuildFrozen();
		var aloneP = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g)).Build();
		AssertPipeline(() => { var q = _cache.Query(); if (cond) q = q.UseIndex(_byGroup, 3); return q; }, aloneP, alone, (cond, 3, 0), "if alone (skipped: all rows)");

		var afterList = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_byGroup, static a => a.g).If(static a => a.cond, b => b.UseIndex(_byCode, 1010)).BuildFrozen();
		var afterListP = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_byGroup, static a => a.g).If(static a => a.cond, b => b.UseIndex(_byCode, 1010)).Build();
		AssertPipeline(() => { var q = _cache.Query().UseIndex(_byGroup, 3); if (cond) q = q.UseIndex(_byCode, 1010); return q; }, afterListP, afterList, (cond, 3, 0), "list then if(unique)");
		afterList.Execute((cond, 3, 0)).Dispose();
		Assert.That(afterList.Explain(), cond ? Does.Contain("last seed: step 1 UniqueEq (signal 1), free: smallest signal") : Does.Contain("last seed: step 0 ListEq (signal").And.Contain("fixed: first active step"),
			"a taken unique arm is the smallest signal; a skipped one leaves the list the only active step");

		var where = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_byGroup, static a => a.g).If(static a => a.cond, b => b.Where(static v => v.Flag).Where(static (v, in a) => v.Id > a.t)).Where(static v => v.Id < 200).BuildFrozen();
		var whereP = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_byGroup, static a => a.g).If(static a => a.cond, b => b.Where(static v => v.Flag).Where(static (v, in a) => v.Id > a.t)).Where(static v => v.Id < 200).Build();
		AssertPipeline(() => { var q = _cache.Query().UseIndex(_byGroup, 3); if (cond) q = q.Where(static v => v.Flag).Where(v => v.Id > 40); return q.Where(static v => v.Id < 200); }, whereP, where, (cond, 3, 40), "if with where branch");
		Assert.That(where.Explain(), Does.Contain("branch filters: 2"));

		var twoOp = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_flagged).If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g).Where(static (v, in a) => v.Id % 40 != a.t)).BuildFrozen();
		var twoOpP = _cache.Prepare<int, PqItem, (bool cond, int g, int t)>().UseIndex(_flagged).If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g).Where(static (v, in a) => v.Id % 40 != a.t)).Build();
		AssertPipeline(() => { var q = _cache.Query().UseIndex(_flagged); if (cond) q = q.UseIndex(_byGroup, 3).Where(static v => v.Id % 40 != 3); return q; }, twoOpP, twoOp, (cond, 3, 3), "two-op branch");
	}

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void IfElse_NestedIf_OrInsideIf_IfInsideOr_AllOutcomes(bool outer, bool inner) {
		var ifElse = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().IfElse(static a => a.outer, b => b.UseIndex(_byGroup, static a => a.g), b => b.UseIndex(_byTier, static a => a.g)).BuildFrozen();
		var ifElseP = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().IfElse(static a => a.outer, b => b.UseIndex(_byGroup, static a => a.g), b => b.UseIndex(_byTier, static a => a.g)).Build();
		AssertPipeline(() => outer ? _cache.Query().UseIndex(_byGroup, 4) : _cache.Query().UseIndex(_byTier, 4), ifElseP, ifElse, (outer, inner, 4), "if-else");

		var nested = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().UseIndex(_flagged).If(static a => a.outer, b => b.UseIndex(_byGroup, static a => a.g).If(static a => a.inner, c => c.UseIndex(_byTier, 3))).BuildFrozen();
		var nestedP = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().UseIndex(_flagged).If(static a => a.outer, b => b.UseIndex(_byGroup, static a => a.g).If(static a => a.inner, c => c.UseIndex(_byTier, 3))).Build();
		AssertPipeline(() => { var q = _cache.Query().UseIndex(_flagged); if (outer) { q = q.UseIndex(_byGroup, 3); if (inner) q = q.UseIndex(_byTier, 3); } return q; }, nestedP, nested, (outer, inner, 3), "nested if");

		var orInIf = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().If(static a => a.outer, b => b.Or(c => c.UseIndex(_byGroup, static a => a.g), c => c.UseIndex(_byGroup, 5))).If(static a => a.inner, b => b.UseIndex(_flagged)).BuildFrozen(EagerOrder);
		var orInIfP = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().If(static a => a.outer, b => b.Or(c => c.UseIndex(_byGroup, static a => a.g), c => c.UseIndex(_byGroup, 5))).If(static a => a.inner, b => b.UseIndex(_flagged)).Build();
		AssertPipeline(() => { var q = _cache.Query(); if (outer) q = q.Or(c => c.UseIndex(_byGroup, 1), c => c.UseIndex(_byGroup, 5)); if (inner) q = q.UseIndex(_flagged); return q; }, orInIfP, orInIf, (outer, inner, 1), "or inside if (first when taken)");

		var ifInOr = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().UseIndex(_byTier, 2).Or(b => b.If(static a => a.outer, c => c.UseIndex(_byGroup, static a => a.g)), b => b.If(static a => a.inner, c => c.UseIndex(_byGroup, 5))).BuildFrozen();
		var ifInOrP = _cache.Prepare<int, PqItem, (bool outer, bool inner, int g)>().UseIndex(_byTier, 2).Or(b => b.If(static a => a.outer, c => c.UseIndex(_byGroup, static a => a.g)), b => b.If(static a => a.inner, c => c.UseIndex(_byGroup, 5))).Build();
		AssertPipeline(() => _cache.Query().UseIndex(_byTier, 2).Or(b => outer ? b.UseIndex(_byGroup, 1) : b, b => inner ? b.UseIndex(_byGroup, 5) : b), ifInOrP, ifInOr, (outer, inner, 1), "if inside or");
	}

	[TestCase(Mode.ByGroup)]
	[TestCase(Mode.ByTier)]
	[TestCase(Mode.ByCode)]
	[TestCase(Mode.Unhandled)]
	public void Match_EveryArm_WithDefault_AndEmptyDefault_First_AndAfterList(Mode mode) {
		var withDefault = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m
			.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, static a => a.t).Where(static (v, in a) => v.Id >= a.g))
			.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
			.Default(b => b.Where(static v => v.Flag))).UseIndex(_flagged).BuildFrozen();
		var withDefaultP = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m
			.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, static a => a.t).Where(static (v, in a) => v.Id >= a.g))
			.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
			.Default(b => b.Where(static v => v.Flag))).UseIndex(_flagged).Build();
		var args = (mode, g: 3, t: 2, code: 1042);
		AssertPipeline(() => {
			var q = _cache.Query();
			switch (mode) {
				case Mode.ByGroup: q = q.UseIndex(_byGroup, 3); break;
				case Mode.ByTier: q = q.UseIndex(_byTier, 2).Where(static v => v.Id >= 3); break;
				case Mode.ByCode: q = q.UseIndex(_byCode, 1042); break;
				default: q = q.Where(static v => v.Flag); break;
			}

			return q.UseIndex(_flagged);
		}, withDefaultP, withDefault, args, "match with default, first");
		Assert.That(withDefault.Explain(), Does.Contain(mode switch { Mode.ByGroup => "select#0 → arm 0", Mode.ByTier => "select#0 → arm 1", Mode.ByCode => "select#0 → arm 2", _ => "select#0 → arm 3" }));

		var emptyDefault = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().UseIndex(_byGroup, static a => a.g).Match(static a => a.mode, m => m
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, static a => a.t))
			.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, 99))
			.Default()).BuildFrozen();
		var emptyDefaultP = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().UseIndex(_byGroup, static a => a.g).Match(static a => a.mode, m => m
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, static a => a.t))
			.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
			.Case(Mode.ByTier, b => b.UseIndex(_byTier, 99))
			.Default()).Build();
		AssertPipeline(() => {
			var q = _cache.Query().UseIndex(_byGroup, 3);
			switch (mode) {
				case Mode.ByTier: q = q.UseIndex(_byTier, 2); break;
				case Mode.ByCode: q = q.UseIndex(_byCode, 1042); break;
			}

			return q;
		}, emptyDefaultP, emptyDefault, args, "match with an empty default after a list (duplicate tag: first arm wins; unmatched: no-op)");
		Assert.That(emptyDefault.Explain(), Does.Contain(mode switch { Mode.ByTier => "select#0 → arm 0", Mode.ByCode => "select#0 → arm 1", _ => "select#0 → arm 3" }));

		var alone = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g)).Default()).BuildFrozen();
		var aloneP = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g)).Default()).Build();
		AssertPipeline(() => mode == Mode.ByGroup ? _cache.Query().UseIndex(_byGroup, 3) : _cache.Query(), aloneP, alone, args, "match alone (empty default: all rows)");

		var inOr = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().UseIndex(_flagged).Or(b => b.Match(static a => a.mode, m => m.Case(Mode.ByGroup, c => c.UseIndex(_byGroup, static a => a.g)).Case(Mode.ByTier, c => c.UseIndex(_byTier, static a => a.t)).Default()), b => b.UseIndex(_byCode, static a => a.code)).BuildFrozen(EagerOrder);
		var inOrP = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().UseIndex(_flagged).Or(b => b.Match(static a => a.mode, m => m.Case(Mode.ByGroup, c => c.UseIndex(_byGroup, static a => a.g)).Case(Mode.ByTier, c => c.UseIndex(_byTier, static a => a.t)).Default()), b => b.UseIndex(_byCode, static a => a.code)).Build();
		AssertPipeline(() => _cache.Query().UseIndex(_flagged).Or(b => mode switch { Mode.ByGroup => b.UseIndex(_byGroup, 3), Mode.ByTier => b.UseIndex(_byTier, 2), _ => b }, b => b.UseIndex(_byCode, 1042)), inOrP, inOr, args, "match inside an or branch");

		var nestedMatch = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m
			.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g).Match(static a => a.t, n => n.Case(2, c => c.UseIndex(_byTier, 2)).Default(c => c.UseIndex(_flagged))))
			.Default(b => b.UseIndex(_byTier, static a => a.t))).BuildFrozen();
		var nestedMatchP = _cache.Prepare<int, PqItem, (Mode mode, int g, int t, int code)>().Match(static a => a.mode, m => m
			.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g).Match(static a => a.t, n => n.Case(2, c => c.UseIndex(_byTier, 2)).Default(c => c.UseIndex(_flagged))))
			.Default(b => b.UseIndex(_byTier, static a => a.t))).Build();
		AssertPipeline(() => mode == Mode.ByGroup ? _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTier, 2) : _cache.Query().UseIndex(_byTier, 2), nestedMatchP, nestedMatch, args, "nested match");
		AssertPipeline(() => mode == Mode.ByGroup ? _cache.Query().UseIndex(_byGroup, 3).UseIndex(_flagged) : _cache.Query().UseIndex(_byTier, 7), nestedMatchP, nestedMatch, (mode, 3, 7, 0), "nested match, inner default");
	}

	// ── (a) Sorted and joined composites ──────────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void Composites_UnderSort_SortBounded_AndFusedJoins_EveryVariantAndPage(bool cond) {
		var orSort = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Sort(new ByCode()).BuildFrozen();
		var orSortP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Sort(new ByCode()).Build();
		AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Sort(new ByCode()), orSortP, orSort, (cond, 1, 4), "or sort (total comparer: the free union seed sorts to the same sequence)");
		orSort.Execute((cond, 1, 4)).Dispose();
		Assert.That(orSort.Explain(), Does.Contain("last seed: step 0 Or (signal 69), fixed: first active step — the union of the branches"), "a classic Sort seeds free: the union");

		var orBounded = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).SortBounded(new ByFlag()).BuildFrozen(EagerOrder);
		var orBoundedP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).SortBounded(new ByFlag()).Build();
		AssertPipeline(() => _cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).SortBounded(new ByFlag()), orBoundedP, orBounded, (cond, 1, 4), "or sort-bounded (tie comparer: the store-order ordinals are eager's)");

		var ifBounded = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().UseIndex(_byGroup, static a => a.g1).If(static a => a.cond, b => b.UseIndex(_byTier, 3).Where(static v => v.Id > 10)).SortBounded(new ByFlag()).BuildFrozen();
		var ifBoundedP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().UseIndex(_byGroup, static a => a.g1).If(static a => a.cond, b => b.UseIndex(_byTier, 3).Where(static v => v.Id > 10)).SortBounded(new ByFlag()).Build();
		AssertPipeline(() => { var q = _cache.Query().UseIndex(_byGroup, 3); if (cond) q = q.UseIndex(_byTier, 3).Where(static v => v.Id > 10); return q.SortBounded(new ByFlag()); }, ifBoundedP, ifBounded, (cond, 3, 0), "if sort-bounded");

		var matchSort = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Match(static a => a.cond, m => m.Case(true, b => b.UseIndex(_byGroup, static a => a.g1)).Default(b => b.UseIndex(_byTier, static a => a.g2))).Sort(new ByCode()).BuildFrozen();
		var matchSortP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Match(static a => a.cond, m => m.Case(true, b => b.UseIndex(_byGroup, static a => a.g1)).Default(b => b.UseIndex(_byTier, static a => a.g2))).Sort(new ByCode()).Build();
		AssertPipeline(() => (cond ? _cache.Query().UseIndex(_byGroup, 2) : _cache.Query().UseIndex(_byTier, 5)).Sort(new ByCode()), matchSortP, matchSort, (cond, 2, 5), "match sort");

		// Fused joins: an outer left-symmetric JoinOne and a PK-to-PK inner one, classic flow; and under SortBounded (bounded flow).
		var orJoin = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).JoinOne(_bySym, _customers).InnerJoinOne(_details).BuildFrozen(EagerOrder);
		var orJoinP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).JoinOne(_bySym, _customers).InnerJoinOne(_details).Build();
		AssertPipelineJoined(() => _cache.Query().Or(b => b.UseIndex(_byGroup, 2), b => b.UseIndex(_byGroup, 3)).JoinOne(_bySym, _customers).InnerJoinOne(_details), orJoinP, orJoin, (cond, 2, 3), CustomerDetail, "or joined");
		Assert.That(orJoin.Explain(), Does.Contain("joins: 2 (fused: 2"));

		var ifJoin = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().UseIndex(_byGroup, static a => a.g1).If(static a => a.cond, b => b.UseIndex(_flagged)).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).BuildFrozen();
		var ifJoinP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().UseIndex(_byGroup, static a => a.g1).If(static a => a.cond, b => b.UseIndex(_flagged)).SortBounded(new ByFlag()).JoinOne(_bySym, _customers).Build();
		AssertPipelineJoined(() => { var q = _cache.Query().UseIndex(_byGroup, 2); if (cond) q = q.UseIndex(_flagged); return q.SortBounded(new ByFlag()).JoinOne(_bySym, _customers); }, ifJoinP, ifJoin, (cond, 2, 0), Customer, "if sort-bounded joined");

		var matchJoin = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Match(static a => a.cond, m => m.Case(true, b => b.Or(c => c.UseIndex(_byGroup, static a => a.g1), c => c.UseIndex(_byGroup, static a => a.g2))).Case(false, b => b.UseIndex(_byTier, static a => a.g2)).Default()).InnerJoinOne(_details).BuildFrozen(EagerOrder);
		var matchJoinP = _cache.Prepare<int, PqItem, (bool cond, int g1, int g2)>().Match(static a => a.cond, m => m.Case(true, b => b.Or(c => c.UseIndex(_byGroup, static a => a.g1), c => c.UseIndex(_byGroup, static a => a.g2))).Case(false, b => b.UseIndex(_byTier, static a => a.g2)).Default()).InnerJoinOne(_details).Build();
		AssertPipelineJoined(() => (cond ? _cache.Query().Or(c => c.UseIndex(_byGroup, 1), c => c.UseIndex(_byGroup, 6)) : _cache.Query().UseIndex(_byTier, 6)).InnerJoinOne(_details), matchJoinP, matchJoin, (cond, 1, 6), Customer, "match(or) inner joined");
	}

	// ── (b) Count and the union seed ─────────────────────────────────────────────

	[Test]
	public void Count_SeedsTheUnion_OnEveryOrShape_AndTheUnionSeed_KeepsTheSetAndCount() {
		var first = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var seeded = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var seededTwoOp = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_flagged), b => b.UseIndex(_byGroup, static a => a.g2)).Where(static v => v.Id > 5).BuildFrozen();
		var eagerOrder = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(EagerOrder);
		Assert.That(seeded.Plan.Optimizations, Does.Not.Contain("PreserveEagerOrder"), "the union seed is the default");
		Assert.That(eagerOrder.Plan.Optimizations, Does.Contain("PreserveEagerOrder"));
		foreach (var args in new[] { (1, 4), (4, 1), (0, 0), (1, 99), (99, 98) }) {
			var eager = Ids(_cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1), b => b.UseIndex(_byGroup, args.Item2)).Execute());
			Assert.That(first.Count(args), Is.EqualTo(eager.Length), "Count " + args);
			first.Count(args);
			Assert.That(first.Explain(), Does.Contain("fixed: first active step — the union of the branches"), "Count seeds the union (one active step: no choice to make, the walk is the union's)");
			var seededIds = Ids(seeded.Execute(args));
			Assert.That(seededIds, Is.EquivalentTo(eager), "union seed: same set " + args);
			Assert.That(seeded.Count(args), Is.EqualTo(eager.Length));
			// Branch 1's keys first in the bucket's order, then branch 2's new keys.
			var expected = new List<int>();
			foreach (var id in Ids(_cache.Query().UseIndex(_byGroup, args.Item1).Execute())) expected.Add(id);
			foreach (var id in Ids(_cache.Query().UseIndex(_byGroup, args.Item2).Execute())) if (!expected.Contains(id)) expected.Add(id);
			Assert.That(seededIds, Is.EqualTo(expected).AsCollection, "union seed: branch order " + args);
			Assert.That(Ids(first.Execute(args)), Is.EqualTo(expected).AsCollection, "the union seed is the default " + args);
			seeded.Execute(args).Dispose();
			Assert.That(seeded.Explain(), Does.Contain("fixed: first active step — the union of the branches"));
			AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1), b => b.UseIndex(_byGroup, args.Item2)).Execute(), eagerOrder.Execute(args));
			var eagerTwoOp = Ids(_cache.Query().Or(b => b.UseIndex(_byGroup, args.Item1).UseIndex(_flagged), b => b.UseIndex(_byGroup, args.Item2)).Where(static v => v.Id > 5).Execute());
			Assert.That(Ids(seededTwoOp.Execute(args)), Is.EquivalentTo(eagerTwoOp), "the union seed keeps the probe for a two-op branch " + args);
			Assert.That(seededTwoOp.Count(args), Is.EqualTo(eagerTwoOp.Length));
		}

		// The opt-out only moves an Or-first seed: after a key set the Or is a probe, byte-identical.
		var after = _cache.Prepare<int, PqItem, (int g1, int g2)>().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(EagerOrder);
		AssertSame(_cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Execute(), after.Execute((1, 4)));

		// Count on the other composite shapes.
		var ifCount = _cache.Prepare<int, PqItem, (bool cond, int g)>().If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g)).UseIndex(_flagged).BuildFrozen();
		Assert.That(ifCount.Count((true, 3)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_flagged).Count()));
		Assert.That(ifCount.Count((false, 3)), Is.EqualTo(_cache.Query().UseIndex(_flagged).Count()));
		var matchCount = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).Match(static t => t, m => m.Case(1, b => b.UseIndex(_byGroup, 1)).Case(2, b => b.Where(static v => v.Id > 100)).Default()).BuildFrozen();
		Assert.That(matchCount.Count(1), Is.EqualTo(_cache.Query().UseIndex(_flagged).UseIndex(_byGroup, 1).Count()));
		Assert.That(matchCount.Count(2), Is.EqualTo(_cache.Query().UseIndex(_flagged).Where(static v => v.Id > 100).Count()));
		Assert.That(matchCount.Count(3), Is.EqualTo(_cache.Query().UseIndex(_flagged).Count()));
	}

	// ── (e) Executor selection and Explain ────────────────────────────────────────

	[Test]
	public void Explain_ShowsTheShape_TheArmsTaken_TheOrBranches_AndTheSeed() {
		var frozen = _cache.Prepare<int, PqItem, (bool cond, int mode, int g)>()
			.If(static a => a.cond, b => b.UseIndex(_flagged).Where(static v => v.Id > 0))
			.Or(b => b.UseIndex(_byGroup, static a => a.g).UseIndex(_byTier, 3), b => b.UseIndex(_byCode, 1010))
			.Match(static a => a.mode, m => m.Case(1, b => b.UseIndex(_byTier, 2)).Default(b => b.Where(static v => v.Flag)))
			.BuildFrozen(EagerOrder);
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var text = frozen.Explain();
		Assert.That(text, Does.Contain("steps: [0 KeySet probe: value-side, 1 Or probe: value-side, 2 ListEq probe: value-side, 3 ListEq probe: value-side, 4 UniqueEq probe: key-side, 5 ListEq probe: value-side]"));
		Assert.That(text, Does.Contain("branch filters: 2"));
		Assert.That(text, Does.Contain("shape: [match#0 {guard#0: [step 0 KeySet, branch filter 0]; default: []}, step 1 Or {branch 1: [step 2 ListEq, step 3 ListEq]; branch 2: [step 4 UniqueEq]}, match#1 {case 1: [step 5 ListEq]; default: [branch filter 1]}]"),
			"an If is a one-guard Match: its skipped side is the empty Default arm, not a missing one");
		Assert.That(text, Does.Contain("seeds the store walk in store order kept to the union of its branches (PreserveEagerOrder"));
		Assert.That(text, Does.Not.Contain("last bind"), "nothing executed yet");

		frozen.Execute((false, 1, 3)).Dispose();
		text = frozen.Explain();
		Assert.That(text, Does.Contain("last bind: select#0 → arm 1, select#1 → arm 0, step 1 Or → branches {1, 2}"), "the skipped If takes its empty Default arm");
		Assert.That(text, Does.Contain("last seed: step 1 Or (signal 35), fixed: first active step — the store walk kept to the branch union"));

		frozen.Execute((true, 7, 99)).Dispose();
		text = frozen.Explain();
		Assert.That(text, Does.Contain("last bind: select#0 → arm 0, select#1 → arm 1, step 1 Or → branches {1, 2}"), "a missing bucket is still a narrowing branch (its leaf bound)");
		Assert.That(text, Does.Contain("last seed: step 0 KeySet (signal 0), fixed: first active step"), "the taken If arm's key set is the first active step");

		var orSeed = _cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).BuildFrozen();
		Assert.That(orSeed.Explain(), Does.Contain("seeds from the union of its branches in branch order (the default)"));
	}

	[Test]
	public void Fallbacks_Replay_AndTheRestTakeThePipeline() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.Where(static v => v.Flag)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "no index step anywhere: no seed source");
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.Where(static v => v.Flag)).Where(static v => v.Id > 0).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filters only");
			Assert.That(_cache.Prepare().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 0).Or(c => c.UseIndex(_byTier, 2), c => c.UseIndex(_byTier, 4)), b => b.UseIndex(_byGroup, 5)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a nested Or beside a leaf");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1100)), b => b.UseIndex(_byGroup, 5)).BuildFrozen(new FrozenOptions { IndexSideProbes = true }).Plan.Executor, Is.EqualTo("Replay"), "a range leaf under IndexSideProbes, inside a branch");
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_byGroup, 1)).BuildFrozen(new FrozenOptions { Pipeline = false }).Plan.Executor, Is.EqualTo("Replay"), "pipeline off");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).InnerJoinOne(_bySym, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "an inner left-symmetric join fuses by default");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).InnerJoinOne(_bySym, _customers).BuildFrozen(EagerOrder).Plan.Executor, Is.EqualTo("Replay"), "…and replays under the opt-out, whose fan-out order it cannot reproduce");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "Or first");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1100)), b => b.UseIndex(_lastUpdated, 0L)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "range and last-updated branches (an estimated signal)");
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.Where(static v => v.Flag)).UseIndex(_byGroup, 1).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a filter-only arm beside an index step");
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_byGroup, 1)).Sort(new ByCode()).JoinOne(_bySym, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "composite, sort, fused join");
			Assert.That(_cache.Prepare<int, PqItem, int>().Match(static t => t, m => m.Case(1, b => b.UseIndex(_byGroup, 1)).Default()).SortBounded(new ByCode()).JoinOne(_bySym, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "match, sort-bounded, join");
		});

		// The replayed shapes still agree with eager.
		var beside = _cache.Prepare().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 0).Or(c => c.UseIndex(_byTier, 2), c => c.UseIndex(_byTier, 4)), b => b.UseIndex(_byGroup, 5)).BuildFrozen();
		AssertSame(_cache.Query().UseIndex(_flagged).Or(b => b.UseIndex(_byGroup, 0).Or(c => c.UseIndex(_byTier, 2), c => c.UseIndex(_byTier, 4)), b => b.UseIndex(_byGroup, 5)).Execute(), beside.Execute());
		var keySide = _cache.Prepare().Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1100)), b => b.UseIndex(_byGroup, 5)).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		AssertSame(_cache.Query().Or(b => b.UseIndex(_codeRange, static rb => rb.Gte(1100)), b => b.UseIndex(_byGroup, 5)).Execute(), keySide.Execute());
	}

	// ── (d) Leaks ─────────────────────────────────────────────────────────────────

	[Test]
	public void Throwing_Condition_TagSelector_ArgSelector_BranchPredicate_Comparer_Clone_LeaveNoRentedArrays() {
		var big = new InMemoryDataCache<int, PqItem>();
		var byGroup = big.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byTier = big.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 5);
		var byCode = big.AddKeyValueIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < 3000; i++)
			big.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 2, Flag = i % 3 == 0 });
		var condition = big.Prepare<int, PqItem, (int g, int t)>().UseIndex(byGroup, static a => a.g).If(static a => a.t < 0 ? throw new InvalidOperationException("boom") : a.t > 0, b => b.UseIndex(byTier, static a => a.t)).BuildFrozen();
		var tag = big.Prepare<int, PqItem, (int g, int t)>().UseIndex(byGroup, static a => a.g).Match(static a => a.t < 0 ? throw new InvalidOperationException("boom") : a.t, m => m.Case(1, b => b.UseIndex(byTier, 1)).Default()).BuildFrozen();
		// The arm's selector throws at bind, inside the taken arm, after the list step bound.
		var armSelector = big.Prepare<int, PqItem, (int g, int t)>().UseIndex(byGroup, static a => a.g).If(static a => true, b => b.UseIndex(byTier, static a => a.t < 0 ? throw new InvalidOperationException("boom") : a.t)).BuildFrozen();
		// A branch selector throws inside an Or branch, after the first branch bound.
		var orSelector = big.Prepare<int, PqItem, (int g, int t)>().UseIndex(byGroup, static a => a.g).Or(b => b.UseIndex(byTier, 1), b => b.UseIndex(byTier, static a => a.t < 0 ? throw new InvalidOperationException("boom") : a.t)).BuildFrozen();
		// The Or-first union over 47 keys (the dedupe set's pool path) and the filtered store walk of 3000 rows, then a branch predicate that throws mid-walk.
		var orFirstPredicate = big.Prepare<int, PqItem, (int g, int t)>().Or(b => b.UseIndex(byGroup, static a => a.g), b => b.UseIndex(byTier, static a => a.t)).If(static a => true, b => b.Where(static (v, in a) => v.Id > 2500 && a.g == 0 ? throw new InvalidOperationException("boom") : true)).BuildFrozen();
		var orSeedPredicate = big.Prepare<int, PqItem, (int g, int t)>().Or(b => b.UseIndex(byGroup, static a => a.g), b => b.UseIndex(byTier, static a => a.t)).If(static a => true, b => b.Where(static (v, in a) => v.Id > 2500 && a.g == 0 ? throw new InvalidOperationException("boom") : true)).BuildFrozen();
		var comparer = big.Prepare<int, PqItem, (int g, int t)>().Or(b => b.UseIndex(byGroup, static a => a.g), b => b.UseIndex(byTier, static a => a.t)).Sort(new Bomb()).BuildFrozen();
		var bounded = big.Prepare<int, PqItem, (int g, int t)>().Or(b => b.UseIndex(byGroup, static a => a.g), b => b.UseIndex(byTier, static a => a.t)).SortBounded(new Bomb()).BuildFrozen();
		var uniqueOr = big.Prepare<int, PqItem, (int g, int t)>().UseIndex(byGroup, static a => a.g).Or(b => b.UseIndex(byCode, static a => a.t < 0 ? throw new InvalidOperationException("boom") : a.t), b => b.UseIndex(byCode, 1001)).BuildFrozen();
		Assert.Multiple(() => {
			Assert.That(condition.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(orFirstPredicate.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(comparer.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(bounded.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => condition.ExecutePooled((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => condition.Count((0, -1)));
			Assert.Throws<InvalidOperationException>(() => tag.ExecutePooled((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => armSelector.ExecutePooledCloned((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => orSelector.ExecutePooled((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => orSelector.Count((0, -1)));
			Assert.Throws<InvalidOperationException>(() => orFirstPredicate.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => orFirstPredicate.ExecutePooledCloned((0, 1), 5, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => orFirstPredicate.Count((0, 1)));
			Assert.Throws<InvalidOperationException>(() => orSeedPredicate.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => bounded.ExecutePooledCloned((0, 1), 2, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => uniqueOr.ExecutePooled((0, -1)).Dispose());
			// The same commands stay usable and balanced on their happy paths.
			condition.ExecutePooled((0, 1)).Dispose();
			tag.ExecutePooledCloned((1, 1)).Dispose();
			orFirstPredicate.ExecutePooled((1, 2), 10, 100).Dispose();
			orSeedPredicate.ExecutePooledCloned((1, 2)).Dispose();
			uniqueOr.ExecutePooled((0, 1002)).Dispose();
			Assert.That(orFirstPredicate.Count((1, 2)), Is.EqualTo(big.Query().Or(b => b.UseIndex(byGroup, 1), b => b.UseIndex(byTier, 2)).Count()));
		});
	}

	// A throwing Clone on the Or-first pooled cloned path (the filtered store walk and the union seed) and under a Match arm.
	[Test]
	public void Throwing_Clone_OnTheOrFirst_AndMatch_PooledClonedPaths_LeavesNoRentedArrays() {
		var bombs = new InMemoryDataCache<int, PqBomb>();
		var bombGroup = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var bombTier = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 4);
		for (var i = 0; i < 100; i++)
			bombs.AddOrUpdate(i, new PqBomb { Id = i, Group = i % 3 });
		var orFirst = bombs.Prepare().Or(b => b.UseIndex(bombGroup, 0), b => b.UseIndex(bombTier, 2)).BuildFrozen();
		var orSeed = bombs.Prepare().Or(b => b.UseIndex(bombGroup, 0), b => b.UseIndex(bombTier, 2)).BuildFrozen();
		var match = bombs.Prepare<int, PqBomb, int>().Match(static t => t, m => m.Case(1, b => b.UseIndex(bombGroup, 0)).Default(b => b.UseIndex(bombTier, 2))).Sort(new ByBombId()).BuildFrozen();
		Assert.That(orFirst.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => orFirst.ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => orFirst.ExecuteCloned(0, 60).Dispose());
			Assert.Throws<InvalidOperationException>(() => orSeed.ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => match.ExecutePooledCloned(2).Dispose());
			orFirst.ExecutePooled().Dispose();
			match.ExecutePooled(1).Dispose();
		});
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

	// ── (f) Concurrency ───────────────────────────────────────────────────────────

	[Test]
	public void EightReaders_AgainstAChurningWriter_NeverThrow_NoDuplicates_OrIfMatch() {
		var orFirst = _cache.Prepare<int, PqItem, (int g1, int g2, bool cond, int mode)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var orSeeded = _cache.Prepare<int, PqItem, (int g1, int g2, bool cond, int mode)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var ifOr = _cache.Prepare<int, PqItem, (int g1, int g2, bool cond, int mode)>().UseIndex(_flagged).If(static a => a.cond, b => b.Or(c => c.UseIndex(_byGroup, static a => a.g1), c => c.UseIndex(_byGroup, static a => a.g2))).SortBounded(new ByFlag()).BuildFrozen();
		var match = _cache.Prepare<int, PqItem, (int g1, int g2, bool cond, int mode)>().Match(static a => a.mode, m => m.Case(0, b => b.UseIndex(_byGroup, static a => a.g1)).Case(1, b => b.UseIndex(_byGroup, static a => a.g1).UseIndex(_byTier, static a => a.g2)).Default(b => b.UseIndex(_byTier, static a => a.g2))).JoinOne(_bySym, _customers).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 1000 + id, Group = (id + i) % 7, Flag = i % 5 == 0 }, Ms(id) + i);
				if (i % 11 == 0) _cache.Remove((id * 13) % N);
				if (i % 11 == 5) _cache.AddOrUpdate((id * 13) % N, Make((id * 13) % N), Ms((id * 13) % N));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				var seen = new HashSet<int>();
				for (var i = 0; i < 1_500; i++) {
					var args = (g1: (seed + i) % 7, g2: (seed * 3 + i) % 8, cond: i % 3 != 0, mode: i % 3);
					using (var rows = orFirst.ExecutePooled(args)) {
						seen.Clear();
						Assert.That(rows.Count, Is.EqualTo(rows.TotalCount));
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
					}

					using (var rows = orSeeded.ExecutePooledCloned(args, i % 4, 10)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
					}

					using (var rows = ifOr.ExecutePooled(args, i % 4, 5)) {
						seen.Clear();
						Assert.That(rows.Count, Is.LessThanOrEqualTo(5));
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
					}

					using (var rows = match.ExecutePooled(args)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							if (args.mode == 1)
								Assert.That(rows[r].Left.Id % 40, Is.EqualTo(args.g2), "the arm's second leaf is value-judged");
						}
					}

					Assert.That(orFirst.Count(args), Is.GreaterThanOrEqualTo(0));
					Assert.That(match.Count(args), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}
}

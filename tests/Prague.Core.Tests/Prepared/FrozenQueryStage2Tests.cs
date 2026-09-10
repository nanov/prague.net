namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;

// BuildFrozen() stage 2: fused filters with adaptive ordering, capacity hints and smallest-bucket
// seeding (ReorderIndexNarrowers). Optimizations 1–2 must return the eager rows in the eager order on
// every shape; the reorder is opt-in and pinned on set equality + Count. The stage-2 adaptive
// intersection (IndexStepsExecutor) was retired in stage 3 step 3: its plans take the pipeline, whose
// small-probe and free seeds cover both roles (FrozenPipelineSeedTests). The fixtures reuse the
// stage-1 model: 240 items, Code = 1000 + Id, Group = Id % 7 (buckets of ~34), Flag = Id % 3 == 0,
// plus Tier = Id % 40 (buckets of 6) for the two-list shapes.
[TestFixture]
public class FrozenQueryStage2Tests {
	private const int N = 240;

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 40);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static readonly FrozenOptions FixedOrder = new() { AdaptiveFilterOrdering = false };
	private static readonly FrozenOptions NoFusion = new() { FuseFilters = false };
	// Stage 3 binds every simple non-composite plan to the pipeline; the stage-2 replay optimizations are
	// exercised here with the pipeline switched off, exactly as they ran when this file was written.
	private static readonly FrozenOptions Stage2 = new() { Pipeline = false };
	private static readonly FrozenOptions NoHints = new() { CapacityHints = false, Pipeline = false };
	private static readonly FrozenOptions Reorder = new() { ReorderIndexNarrowers = true };

	private static void AssertSameSet(QueryResults<PqItem> eager, QueryResults<PqItem> frozen) {
		try {
			Assert.That(frozen.Count, Is.EqualTo(eager.Count), "Count");
			Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), "TotalCount");
			var e = new int[eager.Count];
			var f = new int[frozen.Count];
			for (var i = 0; i < eager.Count; i++) e[i] = eager[i].Id;
			for (var i = 0; i < frozen.Count; i++) f[i] = frozen[i].Id;
			Array.Sort(e);
			Array.Sort(f);
			Assert.That(f, Is.EqualTo(e).AsCollection, "row set");
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	// ── Optimization 1: fused filters ─────────────────────────────────────────────

	[Test]
	public void Fusion_IsAttached_ForTwoOrMoreTopLevelFilters_AndNotForOne() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Flag).BuildFrozen().Plan.Optimizations, Does.Not.Contain("FusedFilters"), "one filter");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Flag).Where(static v => v.Id > 0).BuildFrozen().Plan.Optimizations,
				Does.Contain("FusedFilters").And.Contain("AdaptiveFilterOrder"), "two constants");
			Assert.That(_cache.Prepare<int, PqItem, int>().Where(static (v, a) => v.Id >= a).Where(static v => v.Flag).BuildFrozen().Plan.Optimizations,
				Does.Contain("FusedFilters"), "filter-only, mixed");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Flag).Where(static v => v.Id > 0).BuildFrozen(FixedOrder).Plan.Optimizations,
				Does.Contain("FusedFilters").And.Not.Contain("AdaptiveFilterOrder"), "fixed order");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Flag).Where(static v => v.Id > 0).BuildFrozen(NoFusion).Plan.Optimizations,
				Does.Not.Contain("FusedFilters"), "fusion off");
			// Branch filters are not top-level filters: they stay in their sub-chain.
			Assert.That(_cache.Prepare<int, PqItem, bool>().UseIndex(_byGroup, 3).If(static c => c, b => b.Where(static v => v.Flag).Where(static v => v.Id > 0)).BuildFrozen().Plan.Optimizations,
				Does.Not.Contain("FusedFilters"), "filters inside If");
			// A point lookup keeps its own filter steps.
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Where(static v => v.Flag).Where(static v => v.Id > 0).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"));
		});
	}

	[Test]
	public void Fused_ListWithTwoConstantWheres_EveryOptionSet_LikeEagerAndPrepared() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Id % 2 == 0).Build();
		foreach (var options in new[] { FrozenOptions.Default, FixedOrder, NoFusion, NoHints }) {
			var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Id % 2 == 0).BuildFrozen(options);
			for (var g = 0; g < 8; g++)
				for (var round = 0; round < 3; round++) {
					AssertSame(_cache.Query().UseIndex(_byGroup, g).Where(static v => v.Flag).Where(static v => v.Id % 2 == 0).Execute(), frozen.Execute(g));
					AssertSame(prepared.ExecutePooled(g, 1, 3), frozen.ExecutePooled(g, 1, 3));
					Assert.That(frozen.Count(g), Is.EqualTo(prepared.Count(g)));
				}
		}
	}

	[Test]
	public void Fused_ListWithTwoArgWheres_LikeEagerAndPrepared() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min).Where(static (v, a) => v.Id <= a.max).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min).Where(static (v, a) => v.Id <= a.max).BuildFrozen();
		Assert.That(frozen.Plan.Optimizations, Does.Contain("FusedFilters"));
		foreach (var (g, min, max) in new[] { (3, 0, N), (3, 50, 150), (5, 100, 50), (0, -1, 5) })
			for (var round = 0; round < 3; round++) {
				AssertSame(_cache.Query().UseIndex(_byGroup, g).Where(v => v.Id >= min).Where(v => v.Id <= max).Execute(), frozen.Execute((g, min, max)));
				AssertSame(prepared.ExecutePooledCloned((g, min, max)), frozen.ExecutePooledCloned((g, min, max)));
				Assert.That(frozen.Count((g, min, max)), Is.EqualTo(prepared.Count((g, min, max))));
			}
	}

	[Test]
	public void Fused_MixedConstantAndArgWheres_FilterOnly_LikeEagerAndPrepared() {
		var prepared = _cache.Prepare<int, PqItem, int>().Where(static v => v.Flag).Where(static (v, a) => v.Id >= a).Where(static v => v.Group != 2).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().Where(static v => v.Flag).Where(static (v, a) => v.Id >= a).Where(static v => v.Group != 2).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		Assert.That(frozen.Plan.Optimizations, Does.Contain("FusedFilters").And.Not.Contain("CapacityHints"));
		for (var round = 0; round < 40; round++) {
			var min = round * 5;
			AssertSame(_cache.Query().Where(static v => v.Flag).Where(v => v.Id >= min).Where(static v => v.Group != 2).Execute(), frozen.Execute(min));
			AssertSame(prepared.ExecutePooled(min, 2, 7), frozen.ExecutePooled(min, 2, 7));
			Assert.That(frozen.Count(min), Is.EqualTo(prepared.Count(min)));
		}
	}

	[Test]
	public void Fused_WhereBeforeOr_And_OrBeforeWhere_LikeEager() {
		var frozenBefore = _cache.Prepare().Where(static v => v.Flag).Where(static v => v.Id < 200).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).BuildFrozen();
		var frozenAfter = _cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Where(static v => v.Flag).Where(static v => v.Id < 200).BuildFrozen();
		Assert.That(frozenBefore.Plan.Optimizations, Does.Contain("FusedFilters"));
		for (var round = 0; round < 3; round++) {
			AssertSame(_cache.Query().Where(static v => v.Flag).Where(static v => v.Id < 200).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Execute(), frozenBefore.Execute());
			AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Where(static v => v.Flag).Where(static v => v.Id < 200).Execute(), frozenAfter.Execute());
			Assert.That(frozenBefore.Count(), Is.EqualTo(_cache.Query().Where(static v => v.Flag).Where(static v => v.Id < 200).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Count()));
		}
	}

	[Test]
	public void Fused_WithIfBranchFilter_BranchFilterStillApplies_LikeEager() {
		var frozen = _cache.Prepare<int, PqItem, (bool cond, int group)>().UseIndex(_byGroup, static a => a.group)
			.Where(static v => v.Id > 3).If(static a => a.cond, b => b.Where(static v => v.Flag)).Where(static v => v.Id < 230).BuildFrozen();
		Assert.That(frozen.Plan.Optimizations, Does.Contain("FusedFilters"));
		for (var round = 0; round < 3; round++) {
			AssertSame(_cache.Query().UseIndex(_byGroup, 2).Where(static v => v.Id > 3).Where(static v => v.Flag).Where(static v => v.Id < 230).Execute(), frozen.Execute((true, 2)));
			AssertSame(_cache.Query().UseIndex(_byGroup, 2).Where(static v => v.Id > 3).Where(static v => v.Id < 230).Execute(), frozen.Execute((false, 2)));
		}
	}

	[Test]
	public void Fused_SortedAndJoined_LikeEager() {
		var sorted = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Id > 10).SortBounded(new ByCode()).BuildFrozen();
		Assert.That(sorted.Plan.Optimizations, Does.Contain("FusedFilters"));
		for (var round = 0; round < 3; round++)
			AssertSame(_cache.Query().UseIndex(_byGroup, 5).Where(static v => v.Flag).Where(static v => v.Id > 10).SortBounded(new ByCode()).ExecutePooled(1, 4), sorted.ExecutePooled(5, 1, 4));

		var orders = new InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqOrder>();
		var byCustomer = orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		var customers = new InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqCustomer>();
		for (var c = 0; c < 8; c++)
			customers.AddOrUpdate(c, new PreparedQueryJoinDifferentialTests.PqCustomer { Id = c, Region = "EU" });
		for (var i = 0; i < N; i++)
			orders.AddOrUpdate(i, new PreparedQueryJoinDifferentialTests.PqOrder { Id = i, CustomerId = i % 10, ProductId = i % 6, Qty = i % 13 });
		var joined = orders.Prepare().Where(static o => o.Qty % 2 == 1).Where(static o => o.ProductId != 3).InnerJoinOne(byCustomer, customers).BuildFrozen();
		Assert.That(joined.Plan.Optimizations, Does.Contain("FusedFilters"));
		for (var round = 0; round < 3; round++) {
			PreparedQueryJoinDifferentialTests.AssertSameJoined(
				orders.Query().Where(static o => o.Qty % 2 == 1).Where(static o => o.ProductId != 3).InnerJoinOne(byCustomer, customers).Execute(), joined.Execute(),
				static r => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}");
			Assert.That(joined.Count(), Is.EqualTo(orders.Query().Where(static o => o.Qty % 2 == 1).Where(static o => o.ProductId != 3).InnerJoinOne(byCustomer, customers).Count()));
		}
	}

	// A permissive, expensive-looking predicate declared first and a selective one second: after the
	// first (sampled) execution the fused filter asks the selective one first, so the permissive one
	// is consulted only for the rows that survive — while every execution returns the eager rows.
	[Test]
	public void Adaptive_ReordersTowardsTheSelectivePredicate_WithoutChangingResults() {
		var permissiveCalls = 0;
		var frozen = _cache.Prepare()
			.Where(v => { permissiveCalls++; return v.Id >= 0; })
			.Where(static v => v.Id % 10 == 0)
			.BuildFrozen();
		var eager = _cache.Query().Where(static v => v.Id >= 0).Where(static v => v.Id % 10 == 0).Execute();
		var expectedRows = eager.Count;

		AssertSame(eager, frozen.Execute()); // sampled: every row reaches the permissive predicate
		Assert.That(permissiveCalls, Is.EqualTo(N));
		Assert.That(frozen.Explain(), Does.Contain("order: [1, 0]").And.Contain("reorders: 1"));

		permissiveCalls = 0;
		for (var i = 0; i < 10; i++)
			AssertSame(_cache.Query().Where(static v => v.Id >= 0).Where(static v => v.Id % 10 == 0).Execute(), frozen.Execute());
		Assert.That(permissiveCalls, Is.EqualTo(10 * expectedRows), "only survivors of the selective predicate reach the permissive one");

		var fixedOrder = _cache.Prepare()
			.Where(v => { permissiveCalls++; return v.Id >= 0; })
			.Where(static v => v.Id % 10 == 0)
			.BuildFrozen(FixedOrder);
		permissiveCalls = 0;
		fixedOrder.Execute().Dispose();
		fixedOrder.Execute().Dispose();
		Assert.That(permissiveCalls, Is.EqualTo(2 * N), "with adaptive ordering off the declared order is kept");
		Assert.That(fixedOrder.Explain(), Does.Contain("adaptive: off"));
	}

	[Test]
	public void Adaptive_ArgFilters_ReorderAndStayCorrect_AcrossArguments() {
		var frozen = _cache.Prepare<int, PqItem, (int min, int mod)>()
			.Where(static (v, a) => v.Id >= a.min)
			.Where(static (v, a) => v.Id % a.mod == 0)
			.BuildFrozen();
		for (var round = 0; round < 3 * FusedFilter<PqItem, (int min, int mod)>.SampleEvery; round++) {
			var args = (min: round % 7, mod: 2 + round % 5);
			AssertSame(_cache.Query().Where(v => v.Id >= args.min).Where(v => v.Id % args.mod == 0).Execute(), frozen.Execute(args));
		}

		Assert.That(frozen.Explain(), Does.Contain("order: [1, 0]"));
	}

	[Test]
	public void Adaptive_ConcurrentExecutions_AgainstAWriter_AreEachConsistent() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int min)>().UseIndex(_byGroup, static a => a.group)
			.Where(static v => v.Flag).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Id % 2 == 0).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = N + (i++ % 50);
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 1000 + id, Group = id % 7, Flag = true });
				if (i % 3 == 0) _cache.Remove(N + ((i * 7) % 50));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (group: (seed + i) % 7, min: i % 2 == 0 ? 0 : 120);
					using var rows = frozen.ExecutePooled(args);
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Group, Is.EqualTo(args.group));
						Assert.That(rows[r].Flag, Is.True);
						Assert.That(rows[r].Id, Is.GreaterThanOrEqualTo(args.min));
						Assert.That(rows[r].Id % 2, Is.EqualTo(0));
					}
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}

	[Test]
	public void Fused_ThrowingArgFilter_Propagates_LeavesNoRentedArrays_AndCommandStaysUsable() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int min)>().UseIndex(_byGroup, static a => a.group)
			.Where(static v => v.Flag)
			.Where(static (v, a) => a.min < 0 ? throw new InvalidOperationException("boom") : v.Id >= a.min)
			.BuildFrozen();
		Assert.That(frozen.Plan.Optimizations, Does.Contain("FusedFilters"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => frozen.ExecutePooled((3, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => frozen.Count((3, -1)));
			frozen.ExecutePooled((3, 0)).Dispose();
		});
		Assert.That(ArgPredicatePool<PqItem, (int group, int min)>.DepthForTests, Is.EqualTo(0));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Where(static v => v.Flag).Where(static v => v.Id >= 0).Execute(), frozen.Execute((3, 0)));
	}

	// ── Optimization 2: capacity hints ────────────────────────────────────────────

	[Test]
	public void Hints_AttachedToIndexPlans_NotToFilterOnlyPlans_AndLearnTheSeedSize() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).BuildFrozen(Stage2);
		Assert.That(frozen.Plan.Optimizations, Does.Contain("CapacityHints"));
		Assert.That(_cache.Prepare().Where(static v => v.Flag).BuildFrozen(Stage2).Plan.Optimizations, Does.Not.Contain("CapacityHints"));
		Assert.That(_cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).BuildFrozen().Plan.Optimizations, Does.Not.Contain("CapacityHints"), "the pipeline has no candidate set to size");
		Assert.That(_cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).BuildFrozen(NoHints).Plan.Optimizations, Does.Not.Contain("CapacityHints"));
		Assert.That(frozen.Explain(), Does.Contain("capacity hint: 0"));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Execute(), frozen.Execute(3));
		var bucket = _cache.Query().UseIndex(_byGroup, 3).Count();
		Assert.That(frozen.Explain(), Does.Contain("capacity hint: " + bucket));
		// The hinted execution returns the same rows in the same order.
		for (var g = 0; g < 8; g++)
			AssertSame(_cache.Query().UseIndex(_byGroup, g).Where(static v => v.Flag).Execute(), _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static x => x).Where(static v => v.Flag).BuildFrozen().Execute(g));
	}

	[Test]
	public void Hints_GrowToTheLargestSeed_AndShrinkAfterASustainedDrop() {
		var hints = new FrozenHints();
		hints.Observe(1_000);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(1_000));
		hints.Observe(100);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(1_000), "one small execution does not shrink");
		hints.Observe(50_000);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(50_000));
		for (var i = 0; i < FrozenHints.ShrinkAfter - 1; i++)
			hints.Observe(100);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(50_000), "not yet");
		hints.Observe(100);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(100), "shrunk to the observed size");
		hints.Observe(60);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(100), "within half: no streak");
		hints.Observe(int.MaxValue);
		Assert.That(hints.CandidateCapacity, Is.EqualTo(FrozenHints.MaxHint), "bounded");
	}

	[Test]
	public void Hints_LargeBucket_ThenSmallBucket_ThenMissingKey_LikeEager_AndNoLeak() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byTier, static t => t).UseIndex(_byGroup, static g => g % 7).BuildFrozen();
		LeakAssert.Balanced(() => {
			for (var round = 0; round < 12; round++) {
				frozen.ExecutePooled(3).Dispose();
				frozen.ExecutePooled(-1).Dispose();
				frozen.Count(5);
			}
		});
		for (var t = -1; t < 41; t++)
			AssertSame(_cache.Query().UseIndex(_byTier, t).UseIndex(_byGroup, t % 7).Execute(), frozen.Execute(t));
	}

	// ── Optimization 3 / 4: index steps ───────────────────────────────────────────

	[Test]
	public void Reorder_IsThePipelinesFreeSeed_AndPipelineOffReplays() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "default options: the stage-3 pipeline");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen(Stage2).Plan.Executor, Is.EqualTo("Replay"), "pipeline off: the replay with hints");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen(Stage2).Plan.Optimizations, Does.Contain("CapacityHints"));
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Pipeline"), "reorder");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).BuildFrozen(Reorder).Plan.Optimizations, Does.Contain("ReorderIndexNarrowers"));
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Pipeline"), "one step: the free seed is that step");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1100)).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Pipeline"), "a range step signals an estimate");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byCode, new[] { 1042 }).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Pipeline"), "multi-value");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byTier, 3), b => b.UseIndex(_byTier, 4)).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Replay"), "composite");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_flagged).BuildFrozen(Reorder).Plan.Optimizations, Does.Contain("ReorderIndexNarrowers"), "key-set reorders");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).SortBounded(new ByCode()).BuildFrozen(Reorder).Plan.Executor, Is.EqualTo("Replay"), "SortBounded replays until the bounded feed (step 6); the reorder does not apply");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "classic Sort: pipeline, free seed");
			var explain = _cache.Prepare().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).Where(static v => v.Flag).Where(static v => v.Id > 0).BuildFrozen(Reorder).Explain();
			Assert.That(explain, Does.Contain("executor: Pipeline").And.Contain("FusedFilters").And.Contain("ReorderIndexNarrowers").And.Contain("pipeline: seed = free for Execute"));
		});
	}

	[Test]
	public void Reorder_SeedsFromTheSmallestBucket_SameSetAndCount_AsEager() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(Reorder);
		var withUnique = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen(Reorder);
		var withKeySet = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen(Reorder);
		var withFilters = _cache.Prepare<int, PqItem, (int group, int tier, int min)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier)
			.Where(static v => v.Flag).Where(static (v, a) => v.Id >= a.min).BuildFrozen(Reorder);
		for (var g = -1; g < 8; g++) {
			for (var t = -1; t < 41; t++) {
				AssertSameSet(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Execute(), frozen.Execute((g, t)));
				AssertSameSet(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Where(static v => v.Flag).Where(v => v.Id >= 7).Execute(), withFilters.Execute((g, t, 7)));
				Assert.That(frozen.Count((g, t)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Count()));
			}

			AssertSameSet(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, 1000 + g * 7).Execute(), withUnique.Execute((g, 1000 + g * 7)));
			AssertSameSet(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, -1).Execute(), withUnique.Execute((g, -1)));
			AssertSameSet(_cache.Query().UseIndex(_byGroup, g).UseIndex(_flagged).Execute(), withKeySet.Execute(g));
			Assert.That(withKeySet.Count(g), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).UseIndex(_flagged).Count()));
		}
	}

	// The seeding bucket decides encounter order: with the small Tier bucket seeding, rows come out in
	// Tier-bucket order, which is the eager order of the reversed spelling.
	[Test]
	public void Reorder_RowOrder_FollowsTheSeedingBucket() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(Reorder);
		AssertSame(_cache.Query().UseIndex(_byTier, 3).UseIndex(_byGroup, 3).Execute(), frozen.Execute((3, 3)));
	}

	[Test]
	public void Reorder_ThrowingSelector_Propagates_LeavesNoRentedArrays() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>()
			.UseIndex(_byGroup, static a => a.group)
			.UseIndex(_byTier, static a => a.tier < 0 ? throw new InvalidOperationException("boom") : a.tier)
			.BuildFrozen(Reorder);
		LeakAssert.Balanced(() => Assert.Throws<InvalidOperationException>(() => frozen.ExecutePooled((3, -1)).Dispose()));
		AssertSameSet(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).Execute(), frozen.Execute((3, 3)));
	}

	// ── Defaults ──────────────────────────────────────────────────────────────────

	[Test]
	public void DefaultOptions_KeepEncounterOrder_OnEveryStage2Shape() {
		Assert.That(FrozenOptions.Default.ReorderIndexNarrowers, Is.False);
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		Assert.That(listList.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(listRange.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(listRange.Plan.Optimizations, Is.Empty, "no filters to fuse; the pipeline needs no hint");
		Assert.That(_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(Stage2).Plan.Executor, Is.EqualTo("Replay"));
		Assert.That(_cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen(Stage2).Plan.Executor, Is.EqualTo("Replay"));
		for (var g = -1; g < 8; g++)
			for (var t = -1; t < 41; t++) {
				AssertSame(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Execute(), listList.Execute((g, t)));
				AssertSame(_cache.Query().UseIndex(_byTier, t).UseIndex(_byGroup, g).Execute(),
					_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen().Execute((g, t)));
			}

		for (var round = 0; round < 3; round++)
			AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo: 1050, hi: 1200)).Execute(), listRange.Execute((3, 1050, 1200)));
	}
}

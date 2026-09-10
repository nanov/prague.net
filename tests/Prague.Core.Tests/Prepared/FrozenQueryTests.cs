namespace Prague.Core.Tests.Prepared;

using System.Text;
using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;
using EagerItems = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryDifferentialTests.PqItem>, int, PreparedQueryDifferentialTests.PqItem,
	Resolvers<BaseResolver<int, PreparedQueryDifferentialTests.PqItem>>, PreparedQueryDifferentialTests.PqItem>;

// BuildFrozen(): the recorded chain is flattened into plan metadata at build time and, for the
// point-lookup shape (unique-index equality followed only by filters), bound to a specialized
// executor that never constructs the eager core. Every other shape replays exactly as Build(). The
// tests pin three things: which executor is chosen for which shape, that the fast path's
// QueryResults is indistinguishable from the eager one on every axis (count, total, truncated,
// rows, clone identity, paging, pooling), and that the replay fallback is the prepared path.
[TestFixture]
public class FrozenQueryTests {
	private const int N = 240;
	private const int Customers = 10;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];
	private static readonly (int skip, int take)[] Pages = [(0, int.MaxValue), (0, 1), (1, int.MaxValue), (0, 0), (5, 5)];

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByOrder = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i), 1_000_000L + i * 1000L);

		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		for (var c = 0; c < Customers - 2; c++)
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, new PqOrder { Id = i, CustomerId = i % Customers, ProductId = i % 6, Qty = i % 13 });
			for (var k = 0; k < i % 4; k++)
				_lines.AddOrUpdate(1000 + i * 4 + k, new PqLine { Id = 1000 + i * 4 + k, OrderId = i });
		}
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

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

	// AssertSame plus the identity axis: a clone variant must hand back a fresh, equal instance, a
	// non-clone variant the very instance the cache holds.
	private void AssertSameAndIdentity(QueryResults<PqItem> eager, QueryResults<PqItem> frozen, bool clone, string label) {
		try {
			Assert.Multiple(() => {
				Assert.That(frozen.Count, Is.EqualTo(eager.Count), label + " Count");
				Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), label + " TotalCount");
				Assert.That(frozen.Truncated, Is.EqualTo(eager.Truncated), label + " Truncated");
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

	private void AssertAllVariantsAndPages<TArgs>(Func<EagerItems> eager, PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("PointLookup"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				AssertSameAndIdentity(RunEager(eager(), v, skip, take), Run(frozen, in args, v, skip, take), IsClone(v), tag);
				AssertSame(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take));
			}

		Assert.That(frozen.Count(in args), Is.EqualTo(eager().Count()), label + " Count");
		Assert.That(frozen.Count(in args), Is.EqualTo(prepared.Count(in args)), label + " Count vs prepared");
	}

	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static string Lines(JoinResult<PqOrder, QueryResults<PqLine>> r) {
		var sb = new StringBuilder().Append(r.Left.Id).Append('|');
		for (var i = 0; i < r.Right.Count; i++) sb.Append(r.Right[i].Id).Append(',');
		return sb.ToString();
	}

	// ── Executor selection ────────────────────────────────────────────────────────

	[Test]
	public void PointLookup_IsChosen_ForUniqueEq_BoundAndParameterized_WithAnyFilters() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"), "bound");
			Assert.That(_cache.Prepare<int, PqItem, int>().UseIndex(_byCode, static c => c).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"), "arg");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Where(static v => v.Flag).BuildFrozen().Plan.Executor, Is.EqualTo("PointLookup"), "bound + Where");
			Assert.That(_cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Id >= a.min).BuildFrozen().Plan.Executor,
				Is.EqualTo("PointLookup"), "arg + arg Where");
			Assert.That(_cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static v => v.Flag).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Id > 0).BuildFrozen().Plan.Executor,
				Is.EqualTo("PointLookup"), "arg + three filters");
		});
	}

	// Stage 3: every simple plan of non-composite index steps — unsorted, under a classic Sort or under
	// SortBounded — is the pipeline's; a Where before the unique step is one too (the unique step seeds,
	// the filter is a predicate). Composites, joins (unless SortBounded before outer joins, step 6) and
	// filter-only plans replay, as does everything with Pipeline = false.
	[Test]
	public void PipelineOrReplay_IsChosen_ForEveryOtherShape() {
		var stage2 = new FrozenOptions { Pipeline = false };
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "no narrowing");
			Assert.That(_cache.Prepare().Where(static v => v.Flag).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filter only");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list eq");
			Assert.That(_cache.Prepare().Where(static v => v.Flag).UseIndex(_byCode, 1042).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "Where before unique");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 0).UseIndex(_byCode, 1042).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "list before unique");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).UseIndex(_byGroup, 0).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique then list");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 0).UseIndex(_byCode, 1042).BuildFrozen(stage2).Plan.Executor, Is.EqualTo("Replay"), "list before unique, pipeline off");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).UseIndex(_codeRange, static rb => rb.Gte(0)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique then range");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).UseIndex(_byCode, new[] { 1042 }).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique then unique-in");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Where(static v => v.Flag).UseIndex(_flagged).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique, Where, key-set");
			Assert.That(_cache.Prepare().UseIndex(_byCode, new[] { 1042 }).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unique-in alone");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).BuildFrozen(stage2).Plan.Executor, Is.EqualTo("Replay"), "list eq, stage 2");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Or(b => b.UseIndex(_byGroup, 0), b => b.UseIndex(_byGroup, 1)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "unique then Or");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byCode, 1042), b => b.UseIndex(_byCode, 1043)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "Or of uniques");
			Assert.That(_cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_byCode, 1042)).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "If around unique");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "classic Sort: the pipeline drives the sorting container");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "SortBounded: the pipeline feeds the top-k container (step 6)");
			Assert.That(_cache.Prepare().UseIndex(_byCode, 1042).SortBounded(new ByCode()).BuildFrozen(stage2).Plan.Executor, Is.EqualTo("Replay"), "sort-bounded, stage 2");
			Assert.That(_orders.Prepare().UseIndex(_byCustomer, 3).JoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "joined: a fusable JoinOne after a pipeline narrowing (step 5)");
			Assert.That(_orders.Prepare().UseIndex(_byCustomer, 3).JoinOne(_byCustomer, _customers).BuildFrozen(stage2).Plan.Executor, Is.EqualTo("Replay"), "joined, pipeline off");
			Assert.That(_orders.Prepare().JoinOne(_byCustomer, _customers).Sort(new ByQty()).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "sorted joined");
		});
	}

	private readonly struct ByQty : IComparer<JoinResult<PqOrder, PqCustomer?>> {
		public int Compare(JoinResult<PqOrder, PqCustomer?> x, JoinResult<PqOrder, PqCustomer?> y) => x.Left.Qty.CompareTo(y.Left.Qty);
	}

	[Test]
	public void Explain_NamesTheExecutorAndTheOps() {
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Id >= a.min).BuildFrozen();
		var text = frozen.Explain();
		Assert.Multiple(() => {
			Assert.That(text, Does.Contain("executor: PointLookup"));
			Assert.That(text, Does.Contain("UniqueEq (arg)"));
			Assert.That(text, Does.Contain("FilterArg (arg)"));
			Assert.That(text, Does.Contain("resolvers: none, sorted: no"));
		});

		var sorted = _cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen();
		Assert.Multiple(() => {
			Assert.That(sorted.Explain(), Does.Contain("executor: Pipeline").And.Contain("sort: bounded"));
			Assert.That(sorted.Explain(), Does.Contain("ListEq (bound)"));
			Assert.That(sorted.Explain(), Does.Contain("resolvers: yes, sorted: yes"));
		});
	}

	// ── Describe / PlanInfo shape ─────────────────────────────────────────────────

	[Test]
	public void Describe_EveryNarrowerKind_InBuildOrder_WithNestedComposites() {
		ReadOnlyMemory<PqItem> foreign = new[] { new PqItem { Group = 4 }, new PqItem { Group = 6 } };
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>()
			.UseIndex(_byCode, static a => a.code)
			.UseIndex(_byCode, new[] { 1042, 1043 })
			.UseIndex(_byGroup, 3)
			.UseIndex(_byGroup, static a => a.min)
			.UseIndex(_byGroup, new[] { 1, 2 })
			.UseIndex(_byGroup, foreign, static o => o.Group)
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.min))
			.UseIndex(_codeRange, static rb => rb.Lte(2000))
			.UseIndex(_flagged)
			.UseIndex(_lastUpdated, 0L)
			.UseIndex(_lastUpdated, static a => (long)a.min, static a => (long)a.min + 5)
			.Where(static v => v.Flag)
			.Where(static (v, a) => v.Id >= a.min)
			.Or(b => b.UseIndex(_byGroup, 1), b => b.If(static a => a.min > 0, c => c.UseIndex(_byGroup, 2)))
			.If(static a => a.min > 0, b => b.UseIndex(_byCode, 1042).Where(static v => v.Flag))
			.IfElse(static a => a.min > 0, b => b.UseIndex(_byGroup, 1), b => b)
			.BuildFrozen();

		var ops = frozen.Plan.Narrowers;
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		var kinds = new NarrowerKind[ops.Count];
		for (var i = 0; i < ops.Count; i++) kinds[i] = ops[i].Kind;
		Assert.That(kinds, Is.EqualTo(new[] {
			NarrowerKind.UniqueEq, NarrowerKind.UniqueIn, NarrowerKind.ListEq, NarrowerKind.ListEq, NarrowerKind.ListIn, NarrowerKind.ListInProjected,
			NarrowerKind.Range, NarrowerKind.Range, NarrowerKind.KeySet, NarrowerKind.LastUpdatedAfter, NarrowerKind.LastUpdatedBetween,
			NarrowerKind.Filter, NarrowerKind.FilterArg, NarrowerKind.Or, NarrowerKind.If, NarrowerKind.IfElse,
		}).AsCollection);

		Assert.Multiple(() => {
			Assert.That(ops[0].IsParameterized, Is.True);
			Assert.That(ops[0].Index, Is.SameAs(_byCode));
			Assert.That(ops[0].Selector, Is.Not.Null);
			Assert.That(ops[1].IsParameterized, Is.False);
			Assert.That(ops[1].Value, Is.InstanceOf<ReadOnlyMemory<int>>());
			Assert.That(ops[2].Value, Is.EqualTo(3));
			Assert.That(ops[3].IsParameterized, Is.True);
			Assert.That(ops[5].Selector, Is.Not.Null);
			Assert.That(ops[6].IsParameterized, Is.True);
			Assert.That(ops[7].IsParameterized, Is.False);
			Assert.That(ops[8].Index, Is.SameAs(_flagged));
			Assert.That(ops[9].Index, Is.SameAs(_lastUpdated));
			Assert.That(ops[9].Value, Is.EqualTo(0L));
			Assert.That(ops[10].IsParameterized, Is.True);
			Assert.That(ops[11].Filter, Is.InstanceOf<Predicate<PqItem>>());
			Assert.That(ops[12].Filter, Is.InstanceOf<Func<PqItem, (int code, int min), bool>>());
			Assert.That(ops[12].IsParameterized, Is.True);

			var or = ops[13];
			Assert.That(or.Children, Has.Count.EqualTo(2));
			Assert.That(or.Children[0], Has.Count.EqualTo(1));
			Assert.That(or.Children[0][0].Kind, Is.EqualTo(NarrowerKind.ListEq));
			Assert.That(or.Children[1], Has.Count.EqualTo(1));
			Assert.That(or.Children[1][0].Kind, Is.EqualTo(NarrowerKind.If));
			Assert.That(or.Children[1][0].Children[0][0].Kind, Is.EqualTo(NarrowerKind.ListEq));

			var @if = ops[14];
			Assert.That(@if.IsParameterized, Is.True);
			Assert.That(@if.Selector, Is.InstanceOf<Func<(int code, int min), bool>>());
			Assert.That(@if.Children, Has.Count.EqualTo(1));
			Assert.That(@if.Children[0].Select(static d => d.Kind), Is.EqualTo(new[] { NarrowerKind.UniqueEq, NarrowerKind.Filter }).AsCollection);

			var ifElse = ops[15];
			Assert.That(ifElse.Children, Has.Count.EqualTo(2));
			Assert.That(ifElse.Children[0][0].Kind, Is.EqualTo(NarrowerKind.ListEq));
			Assert.That(ifElse.Children[1], Is.Empty);
		});

		Assert.That(frozen.Explain(), Does.Contain("branch 2:").And.Contain("(empty)"));
	}

	// ── Differential: point-lookup fast path vs eager vs prepared ─────────────────

	[TestCase(1042, TestName = "UniqueBound_Found")]
	[TestCase(-1, TestName = "UniqueBound_NotFound")]
	public void UniqueBound_EveryVariantAndPage_LikeEagerAndPrepared(int code) {
		var prepared = _cache.Prepare().UseIndex(_byCode, code).Build();
		var frozen = _cache.Prepare().UseIndex(_byCode, code).BuildFrozen();
		AssertAllVariantsAndPages(() => _cache.Query().UseIndex(_byCode, code), prepared, frozen, default(NoArgs), "unique bound " + code);
	}

	[TestCase(1042, TestName = "UniqueArg_Found")]
	[TestCase(1000, TestName = "UniqueArg_FoundFirst")]
	[TestCase(1239, TestName = "UniqueArg_FoundLast")]
	[TestCase(-1, TestName = "UniqueArg_NotFound")]
	[TestCase(5000, TestName = "UniqueArg_NotFoundHigh")]
	public void UniqueArg_EveryVariantAndPage_LikeEagerAndPrepared(int code) {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, static c => c).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, static c => c).BuildFrozen();
		AssertAllVariantsAndPages(() => _cache.Query().UseIndex(_byCode, code), prepared, frozen, code, "unique arg " + code);
	}

	[TestCase(1042, TestName = "UniqueWhere_FoundPasses")] // 42 % 3 == 0 → Flag
	[TestCase(1043, TestName = "UniqueWhere_FoundFails")]
	[TestCase(-1, TestName = "UniqueWhere_NotFound")]
	public void UniqueBound_ConstantWhere_EveryVariantAndPage_LikeEagerAndPrepared(int code) {
		var prepared = _cache.Prepare().UseIndex(_byCode, code).Where(static v => v.Flag).Build();
		var frozen = _cache.Prepare().UseIndex(_byCode, code).Where(static v => v.Flag).BuildFrozen();
		AssertAllVariantsAndPages(() => _cache.Query().UseIndex(_byCode, code).Where(static v => v.Flag), prepared, frozen, default(NoArgs), "unique + Where " + code);
	}

	[TestCase(1042, 0, TestName = "UniqueArgWhere_FoundPasses")]
	[TestCase(1042, 42, TestName = "UniqueArgWhere_FoundPassesAtBoundary")]
	[TestCase(1042, 43, TestName = "UniqueArgWhere_FoundFails")]
	[TestCase(-1, 0, TestName = "UniqueArgWhere_NotFound")]
	public void UniqueArg_ArgWhere_EveryVariantAndPage_LikeEagerAndPrepared(int code, int min) {
		var prepared = _cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Id >= a.min).Build();
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Id >= a.min).BuildFrozen();
		var args = (code, min);
		AssertAllVariantsAndPages(() => _cache.Query().UseIndex(_byCode, code).Where(v => v.Id >= min), prepared, frozen, args, $"unique arg + arg Where {code}/{min}");
	}

	[TestCase(1042, 0, TestName = "UniqueMixedFilters_AllPass")]
	[TestCase(1042, 100, TestName = "UniqueMixedFilters_ArgFails")]
	[TestCase(1043, 0, TestName = "UniqueMixedFilters_ConstantFails")]
	[TestCase(-1, 0, TestName = "UniqueMixedFilters_NotFound")]
	public void UniqueArg_ConstantAndArgWheres_EveryVariantAndPage_LikeEagerAndPrepared(int code, int min) {
		var prepared = _cache.Prepare<int, PqItem, (int code, int min)>()
			.UseIndex(_byCode, static a => a.code).Where(static v => v.Flag).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Id >= 0).Build();
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>()
			.UseIndex(_byCode, static a => a.code).Where(static v => v.Flag).Where(static (v, a) => v.Id >= a.min).Where(static v => v.Id >= 0).BuildFrozen();
		var args = (code, min);
		AssertAllVariantsAndPages(
			() => _cache.Query().UseIndex(_byCode, code).Where(static v => v.Flag).Where(v => v.Id >= min).Where(static v => v.Id >= 0),
			prepared, frozen, args, $"unique + mixed filters {code}/{min}");
	}

	// Filters run in build order and short-circuit like the eager `&&` composition: a later filter is
	// never consulted once an earlier one rejected the row.
	[Test]
	public void Filters_ApplyInOrder_AndShortCircuit() {
		var calls = 0;
		var frozen = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byCode, static c => c)
			.Where(static v => !v.Flag)
			.Where((v, _) => { calls++; return v.Id > 0; })
			.BuildFrozen();

		using (var rejected = frozen.Execute(1042)) Assert.That(rejected.Count, Is.EqualTo(0)); // Flag → first filter rejects
		Assert.That(calls, Is.EqualTo(0));
		using (var accepted = frozen.Execute(1043)) Assert.That(accepted.Count, Is.EqualTo(1));
		Assert.That(calls, Is.EqualTo(1));
	}

	// ── Fallback parity ───────────────────────────────────────────────────────────

	[Test]
	public void Fallback_ListWithWhere_FrozenEqualsPreparedEqualsEager() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Where(static v => v.Flag).Execute(), frozen.Execute(3));
		AssertSame(prepared.ExecutePooled(3, 2, 5), frozen.ExecutePooled(3, 2, 5));
		Assert.That(frozen.Count(3), Is.EqualTo(prepared.Count(3)));
	}

	[Test]
	public void Fallback_Range_FrozenEqualsPreparedEqualsEager() {
		var prepared = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		var frozen = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var args = (lo: 1100, hi: 1140);
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		AssertSame(_cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args).Execute(), frozen.Execute(args));
		AssertSame(prepared.ExecutePooledCloned(args), frozen.ExecutePooledCloned(args));
		Assert.That(frozen.Count(args), Is.EqualTo(prepared.Count(args)));
	}

	[Test]
	public void Fallback_MultiValue_FrozenEqualsPreparedEqualsEager() {
		ReadOnlyMemory<int> codes = new[] { 1001, 1042, 1177, -1 };
		var prepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static c => c).Build();
		var frozen = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byCode, static c => c).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		AssertSame(_cache.Query().UseIndex(_byCode, codes.Span).Execute(), frozen.Execute(codes));
		AssertSame(prepared.Execute(codes), frozen.Execute(codes));
	}

	[Test]
	public void Fallback_Or_FrozenEqualsPreparedEqualsEager() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		var frozen = _cache.Prepare<int, PqItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var args = (g1: 1, g2: 4);
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Execute(), frozen.Execute(args));
		AssertSame(prepared.Execute(args), frozen.Execute(args));
		Assert.That(frozen.Count(args), Is.EqualTo(prepared.Count(args)));
	}

	[Test]
	public void Fallback_If_FrozenEqualsPreparedEqualsEager() {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int group)>().UseIndex(_byGroup, static a => a.group).If(static a => a.cond, b => b.Where(static v => v.Flag)).Build();
		var frozen = _cache.Prepare<int, PqItem, (bool cond, int group)>().UseIndex(_byGroup, static a => a.group).If(static a => a.cond, b => b.Where(static v => v.Flag)).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Where(static v => v.Flag).Execute(), frozen.Execute((true, 2)));
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(), frozen.Execute((false, 2)));
		AssertSame(prepared.Execute((true, 2)), frozen.Execute((true, 2)));
		AssertSame(prepared.Execute((false, 2)), frozen.Execute((false, 2)));
	}

	[Test]
	public void SortBounded_FrozenEqualsPreparedEqualsEager() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).Build();
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(frozen.Plan.IsSorted, Is.True);
		AssertSame(_cache.Query().UseIndex(_byGroup, 5).SortBounded(new ByCode()).ExecutePooled(3, 8), frozen.ExecutePooled(5, 3, 8));
		AssertSame(prepared.ExecutePooled(5, 3, 8), frozen.ExecutePooled(5, 3, 8));
		Assert.That(frozen.Count(5), Is.EqualTo(prepared.Count(5)));
	}

	[Test]
	public void Fallback_JoinOne_FrozenEqualsPreparedEqualsEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).Build();
		// Since step 5 a fusable JoinOne after a pipeline narrowing takes the pipeline; the same rows either way.
		var frozen = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(frozen.Plan.HasResolvers, Is.True);
		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 3).JoinOne(_byCustomer, _customers).Execute(), frozen.Execute(3), Customer);
		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 8).JoinOne(_byCustomer, _customers).Execute(), frozen.Execute(8), Customer); // missing customer
		AssertSameJoined(prepared.ExecutePooled(3), frozen.ExecutePooled(3), Customer);
		Assert.That(frozen.Count(3), Is.EqualTo(prepared.Count(3)));
	}

	[Test]
	public void Fallback_InnerJoinOne_FrozenEqualsPreparedEqualsEager() {
		var prepared = _orders.Prepare().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).Build();
		var frozen = _orders.Prepare().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		AssertSameJoined(_orders.Query().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).Execute(), frozen.Execute(), Customer);
		AssertSameJoined(prepared.Execute(), frozen.Execute(), Customer);
		Assert.That(frozen.Count(), Is.EqualTo(prepared.Count()));
	}

	[Test]
	public void Fallback_JoinMany_FrozenEqualsPreparedEqualsEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_lines, _lineByOrder).Build();
		var frozen = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_lines, _lineByOrder).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 2).JoinMany(_lines, _lineByOrder).Execute(), frozen.Execute(2), Lines);
		AssertSameJoined(prepared.ExecutePooled(2), frozen.ExecutePooled(2), Lines);
		Assert.That(frozen.Count(2), Is.EqualTo(prepared.Count(2)));
	}

	// ── Reuse across mutations ────────────────────────────────────────────────────

	[Test]
	public void Reuse_AcrossMutations_SeesLiveData() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, static c => c).BuildFrozen();

		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), frozen.Execute(1042));

		_cache.Remove(42);
		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), frozen.Execute(1042));
		Assert.That(frozen.Count(1042), Is.EqualTo(0));

		_cache.AddOrUpdate(42, new PqItem { Id = 42, Code = 1042, Group = 99, Flag = false });
		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), frozen.Execute(1042));
		using (var rows = frozen.Execute(1042)) {
			Assert.That(rows[0].Group, Is.EqualTo(99));
			Assert.That(_cache.TryGet(42, out var live) && ReferenceEquals(rows[0], live), Is.True);
		}

		_cache.AddOrUpdate(42, new PqItem { Id = 42, Code = 7777, Group = 99, Flag = false });
		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), frozen.Execute(1042));
		AssertSame(_cache.Query().UseIndex(_byCode, 7777).Execute(), frozen.Execute(7777));
		Assert.That(frozen.Count(1042), Is.EqualTo(0));
		Assert.That(frozen.Count(7777), Is.EqualTo(1));
	}

	// ── Concurrency ───────────────────────────────────────────────────────────────

	[Test]
	public void ConcurrentExecutions_OfOneFrozenPointLookup_AgainstAWriter_AreEachConsistent() {
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, a) => v.Id >= a.min).BuildFrozen();
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
					var code = 1000 + ((seed * 31 + i) % (N + 50));
					var min = i % 2 == 0 ? 0 : 120;
					using var rows = frozen.ExecutePooled((code, min));
					Assert.That(rows.Count, Is.LessThanOrEqualTo(1));
					if (rows.Count == 1) {
						Assert.That(rows[0].Code, Is.EqualTo(code));
						Assert.That(rows[0].Id, Is.GreaterThanOrEqualTo(min));
					}
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}

	// ── Leaks ─────────────────────────────────────────────────────────────────────

	[Test]
	public void PooledFastPath_FoundAndNotFound_LeavesNoRentedArrays() {
		var frozen = _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, static c => c).Where(static v => v.Id >= 0).BuildFrozen();
		LeakAssert.Balanced(() => {
			frozen.ExecutePooled(1042).Dispose();
			frozen.ExecutePooled(-1).Dispose();
			frozen.ExecutePooledCloned(1042, 0, 1).Dispose();
			frozen.ExecutePooled(1042, 1).Dispose();
			frozen.ExecutePooled(1042, 0, 0).Dispose();
			frozen.ExecutePooled(1042, 5, 5).Dispose();
			frozen.ExecutePooled(1043).Dispose();
		});
	}

	[Test]
	public void ThrowingArgFilter_Propagates_LeavesNoRentedArrays_AndCommandStaysUsable() {
		var frozen = _cache.Prepare<int, PqItem, (int code, int min)>()
			.UseIndex(_byCode, static a => a.code)
			.Where(static (v, a) => a.min < 0 ? throw new InvalidOperationException("boom") : v.Id >= a.min)
			.BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("PointLookup"));

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => frozen.ExecutePooled((1042, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => frozen.Count((1042, -1)));
		});

		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), frozen.Execute((1042, 0)));
	}

	[Test]
	public void ThrowingSelector_Propagates_LeavesNoRentedArrays() {
		var frozen = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byCode, static code => code < 0 ? throw new InvalidOperationException("boom") : code)
			.BuildFrozen();
		LeakAssert.Balanced(() => Assert.Throws<InvalidOperationException>(() => frozen.ExecutePooled(-1).Dispose()));
		AssertSame(_cache.Query().UseIndex(_byCode, 1009).Execute(), frozen.Execute(1009));
	}
}

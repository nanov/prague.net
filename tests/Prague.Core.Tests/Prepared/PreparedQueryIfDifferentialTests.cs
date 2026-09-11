namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;
using EagerItems = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryDifferentialTests.PqItem>, int, PreparedQueryDifferentialTests.PqItem,
	Resolvers<BaseResolver<int, PreparedQueryDifferentialTests.PqItem>>, PreparedQueryDifferentialTests.PqItem>;
using EagerOrders = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqOrder>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryJoinDifferentialTests.PqOrder>, int, PreparedQueryJoinDifferentialTests.PqOrder,
	Resolvers<BaseResolver<int, PreparedQueryJoinDifferentialTests.PqOrder>>, PreparedQueryJoinDifferentialTests.PqOrder>;

// If / IfElse on the prepared builder, eager vs prepared from the same inputs, for BOTH condition
// outcomes. The eager twin is what a caller writes today: a C# `if` around a type-preserving
// UseIndex / Where reassignment of the eager builder. Every shape must agree on Count, TotalCount,
// Truncated and the exact row sequence, and Count() must agree too.
[TestFixture]
public class PreparedQueryIfDifferentialTests {
	private const int N = 240;
	private const long BaseMs = 1_700_000_000_000;

	private readonly struct ByGroup : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0).CompareTo(y?.Group ?? 0);
	}

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byBucket = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	// Joined model: orders 0..119 over customers 0..9, of which 8 and 9 do not exist.
	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byBucket = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 5);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i), Ms(i));

		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		for (var c = 0; c < 8; c++)
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < 120; i++)
			_orders.AddOrUpdate(i, new PqOrder { Id = i, CustomerId = i % 10, ProductId = i % 6, Qty = i % 13 });
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	private static long Ms(int i) => BaseMs + i * 1000L;

	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static int[] Ids(QueryResults<PqItem> rows) {
		using (rows) {
			var ids = new int[rows.Count];
			for (var i = 0; i < rows.Count; i++) ids[i] = rows[i].Id;
			Array.Sort(ids);
			return ids;
		}
	}

	// Rows and Count from the same eager description, built twice because Execute consumes the builder.
	private static void AssertParity(Func<EagerItems> eager, Func<QueryResults<PqItem>> rows, Func<int> count) {
		AssertSame(eager().Execute(), rows());
		Assert.That(count(), Is.EqualTo(eager().Count()), "Count()");
	}

	// ── Placement and seeding ─────────────────────────────────────────────────────

	// The only narrower: taken → the group; skipped → the core is never narrowed, so every row.
	[TestCase(true)]
	[TestCase(false)]
	public void If_AsOnlyNarrower_TakenNarrows_SkippedIsAllRows(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_byGroup, 3)).Build();

		EagerItems Eager() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byGroup, 3);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));
		Assert.That(prepared.Count(cond), Is.EqualTo(cond ? N / 7 + (N % 7 > 3 ? 1 : 0) : N));
	}

	// Skipped-first seeding: when the If is the first narrower and skipped, the UseIndex after it must
	// seed the candidate set (the eager `_first` path) rather than intersect with an empty one.
	[TestCase(true)]
	[TestCase(false)]
	public void If_First_ThenUseIndex_SkippedFirstLetsTheNextNarrowerSeed(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.If(static c => c, b => b.UseIndex(_byGroup, 3))
			.UseIndex(_byBucket, 2)
			.Build();

		EagerItems Eager() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byGroup, 3);
			return q.UseIndex(_byBucket, 2);
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));

		using var rows = prepared.Execute(cond);
		Assert.That(rows.Count, Is.EqualTo(cond ? 7 : N / 5));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Id % 5, Is.EqualTo(2));
			if (cond) Assert.That(rows[i].Group, Is.EqualTo(3));
		}
	}

	// Every narrower inside a skipped If: the eager equivalent never called UseIndex, so it is the
	// all-rows scan; the constant Where afterwards still applies to every row.
	[Test]
	public void AllNarrowersInsideSkippedIfs_IsAnAllRowsScan_AndAFollowingWhereStillApplies() {
		var prepared = _cache.Prepare<int, PqItem, (bool a, bool b)>()
			.If(static x => x.a, b => b.UseIndex(_byGroup, 3))
			.If(static x => x.b, b => b.UseIndex(_byBucket, 2))
			.Build();
		var filtered = _cache.Prepare<int, PqItem, (bool a, bool b)>()
			.If(static x => x.a, b => b.UseIndex(_byGroup, 3))
			.If(static x => x.b, b => b.UseIndex(_byBucket, 2))
			.Where(static v => v.Flag)
			.Build();

		AssertSame(_cache.Query().Execute(), prepared.Execute((false, false)));
		Assert.That(prepared.Count((false, false)), Is.EqualTo(N));
		AssertSame(_cache.Query().Where(static v => v.Flag).Execute(), filtered.Execute((false, false)));
		Assert.That(filtered.Count((false, false)), Is.EqualTo(N / 3));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void UseIndex_ThenIf_UseIndex_LikeEager(bool cond) {
		// Group 1 ∩ code 1015 (15 % 7 == 1) — taken: exactly the one row; skipped: the whole group.
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.UseIndex(_byGroup, 1)
			.If(static c => c, b => b.UseIndex(_byCode, 1015))
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byGroup, 1);
			if (cond) q = q.UseIndex(_byCode, 1015);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));
		Assert.That(Ids(prepared.Execute(cond)), cond ? Is.EqualTo(new[] { 15 }) : Has.Length.EqualTo(N / 7 + (N % 7 > 1 ? 1 : 0)));
	}

	// ── Branch contents ───────────────────────────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void If_ConstantWhereBranch_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.UseIndex(_byGroup, 2)
			.If(static c => c, b => b.Where(static v => v.Flag))
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byGroup, 2);
			if (cond) q = q.Where(static v => v.Flag);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));
	}

	[TestCase(true, 100)]
	[TestCase(false, 100)]
	[TestCase(true, 1_000)]
	[TestCase(false, -5)]
	public void If_ParameterizedWhereBranch_LikeEager(bool cond, int min) {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int min)>()
			.UseIndex(_byGroup, 4)
			.If(static a => a.cond, b => b.Where(static (v, in a) => v.Id >= a.min))
			.Build();
		var args = (cond, min);

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byGroup, 4);
			if (cond) q = q.Where(v => v.Id >= min);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void If_TwoOpBranch_IndexAndWhere_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.UseIndex(_codeRange, static rb => rb.Lt(1200))
			.If(static c => c, b => b.UseIndex(_byBucket, 1).Where(static v => !v.Flag))
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1200));
			if (cond) q = q.UseIndex(_byBucket, 1).Where(static v => !v.Flag);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));

		using var rows = prepared.Execute(cond);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Code, Is.LessThan(1200));
			if (cond) Assert.That(rows[i].Id % 5 == 1 && !rows[i].Flag);
		}
	}

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void TwoIndependentIfs_AllFourOutcomes_LikeEager(bool a, bool b) {
		var prepared = _cache.Prepare<int, PqItem, (bool a, bool b)>()
			.If(static x => x.a, i => i.UseIndex(_byGroup, 5))
			.If(static x => x.b, i => i.UseIndex(_byBucket, 0))
			.Build();
		var args = (a, b);

		EagerItems Eager() {
			var q = _cache.Query();
			if (a) q = q.UseIndex(_byGroup, 5);
			if (b) q = q.UseIndex(_byBucket, 0);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));

		using var rows = prepared.Execute(args);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			if (a) Assert.That(rows[i].Group, Is.EqualTo(5));
			if (b) Assert.That(rows[i].Id % 5, Is.EqualTo(0));
		}
	}

	[TestCase(true)]
	[TestCase(false)]
	public void IfElse_DifferentBranches_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int g, int code)>()
			.IfElse(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g), b => b.UseIndex(_byCode, static a => a.code).Where(static v => v.Flag))
			.Build();
		var args = (cond, g: 6, code: 1042);

		EagerItems Eager() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byGroup, args.g);
			else q = q.UseIndex(_byCode, args.code).Where(static v => v.Flag);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
		Assert.That(Ids(prepared.Execute(args)), cond ? Has.Length.EqualTo(N / 7 + (N % 7 > 6 ? 1 : 0)) : Is.EqualTo(new[] { 42 }));
	}

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void NestedIf_InsideIf_LikeEager(bool outer, bool inner) {
		var prepared = _cache.Prepare<int, PqItem, (bool outer, bool inner)>()
			.If(static a => a.outer, b => b.UseIndex(_byGroup, 2).If(static a => a.inner, c => c.UseIndex(_byBucket, 3)))
			.Where(static v => v.Id < 200)
			.Build();
		var args = (outer, inner);

		EagerItems Eager() {
			var q = _cache.Query();
			if (outer) {
				q = q.UseIndex(_byGroup, 2);
				if (inner) q = q.UseIndex(_byBucket, 3);
			}

			return q.Where(static v => v.Id < 200);
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void Or_InsideIf_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.UseIndex(_byBucket, 4)
			.If(static c => c, b => b.Or(x => x.UseIndex(_byGroup, 1), x => x.UseIndex(_byGroup, 5)))
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byBucket, 4);
			if (cond) q = q.Or(x => x.UseIndex(_byGroup, 1), x => x.UseIndex(_byGroup, 5));
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));

		using var rows = prepared.Execute(cond);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Id % 5, Is.EqualTo(4));
			if (cond) Assert.That(rows[i].Group is 1 or 5);
		}
	}

	// If inside an Or branch (narrow-only). The eager branch builder is type-preserving too, so the
	// eager twin is a ternary: the skipped branch is the eager no-op branch and drops out of the union.
	[TestCase(true)]
	[TestCase(false)]
	public void If_InsideOrBranch_UseIndexOnly_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>()
			.UseIndex(_byGroup, 3)
			.Or(b => b.UseIndex(_byBucket, 1), b => b.If(static c => c, i => i.UseIndex(_byBucket, 4)))
			.Build();

		EagerItems Eager()
			=> _cache.Query().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byBucket, 1), b => cond ? b.UseIndex(_byBucket, 4) : b);

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));

		using var rows = prepared.Execute(cond);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group, Is.EqualTo(3));
			Assert.That(cond ? rows[i].Id % 5 is 1 or 4 : rows[i].Id % 5 == 1);
		}
	}

	// ── Every narrower family inside a branch ─────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void If_RangeBranch_BoundAndParameterized_LikeEager(bool cond) {
		var bound = _cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_codeRange, static rb => rb.Gte(1100).Lt(1150))).Build();
		var parameterized = _cache.Prepare<int, PqItem, (bool cond, int lo, int hi)>()
			.UseIndex(_byGroup, 0)
			.If(static a => a.cond, b => b.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)))
			.Build();
		var args = (cond, lo: 1050, hi: 1180);

		EagerItems EagerBound() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_codeRange, static rb => rb.Gte(1100).Lt(1150));
			return q;
		}

		EagerItems EagerParameterized() {
			var q = _cache.Query().UseIndex(_byGroup, 0);
			if (cond) q = q.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args);
			return q;
		}

		AssertParity(EagerBound, () => bound.Execute(cond), () => bound.Count(cond));
		AssertParity(EagerParameterized, () => parameterized.Execute(args), () => parameterized.Count(args));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void If_MultiValueBranch_UniqueAndList_LikeEager(bool cond) {
		var codes = new[] { 1001, 1042, 1239, -1 };
		var unique = _cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_byCode, codes)).Build();
		var list = _cache.Prepare<int, PqItem, (bool cond, ReadOnlyMemory<int> groups)>()
			.UseIndex(_byBucket, 2)
			.If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.groups))
			.Build();
		var args = (cond, groups: (ReadOnlyMemory<int>)new[] { 1, 5 });

		EagerItems EagerUnique() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byCode, codes.AsSpan());
			return q;
		}

		EagerItems EagerList() {
			var q = _cache.Query().UseIndex(_byBucket, 2);
			if (cond) q = q.UseIndex(_byGroup, args.groups.Span);
			return q;
		}

		AssertParity(EagerUnique, () => unique.Execute(cond), () => unique.Count(cond));
		AssertParity(EagerList, () => list.Execute(args), () => list.Count(args));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void If_KeySetBranch_LikeEager(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, bool>().UseIndex(_byGroup, 1).If(static c => c, b => b.UseIndex(_flagged)).Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byGroup, 1);
			if (cond) q = q.UseIndex(_flagged);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(cond), () => prepared.Count(cond));
	}

	// Task B: the last-updated overloads bind inside a conditional branch (bound and parameterized).
	[TestCase(true)]
	[TestCase(false)]
	public void If_LastUpdatedBranch_BoundAndParameterized_LikeEager(bool cond) {
		var bound = _cache.Prepare<int, PqItem, bool>().If(static c => c, b => b.UseIndex(_lastUpdated, Ms(100), Ms(150))).Build();
		var parameterized = _cache.Prepare<int, PqItem, (bool cond, long from)>()
			.UseIndex(_byGroup, 2)
			.If(static a => a.cond, b => b.UseIndex(_lastUpdated, static a => a.from))
			.Build();
		var args = (cond, from: Ms(120));

		EagerItems EagerBound() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_lastUpdated, Ms(100), Ms(150));
			return q;
		}

		EagerItems EagerParameterized() {
			var q = _cache.Query().UseIndex(_byGroup, 2);
			if (cond) q = q.UseIndex(_lastUpdated, args.from);
			return q;
		}

		AssertParity(EagerBound, () => bound.Execute(cond), () => bound.Count(cond));
		AssertParity(EagerParameterized, () => parameterized.Execute(args), () => parameterized.Count(args));

		using var rows = parameterized.Execute(args);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group, Is.EqualTo(2));
			if (cond) Assert.That(rows[i].Id, Is.GreaterThan(120));
		}
	}

	// Task B: the last-updated overloads bind inside an Or branch.
	[Test]
	public void LastUpdated_InsideOrBranch_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, long>()
			.UseIndex(_byBucket, 3)
			.Or(b => b.UseIndex(_byGroup, 0), b => b.UseIndex(_lastUpdated, static ms => ms))
			.Build();

		foreach (var from in new[] { Ms(0), Ms(200), Ms(N + 5) }) {
			AssertSame(_cache.Query().UseIndex(_byBucket, 3).Or(b => b.UseIndex(_byGroup, 0), b => b.UseIndex(_lastUpdated, from)).Execute(), prepared.Execute(from));
			Assert.That(prepared.Count(from), Is.EqualTo(_cache.Query().UseIndex(_byBucket, 3).Or(b => b.UseIndex(_byGroup, 0), b => b.UseIndex(_lastUpdated, from)).Count()));
		}

		using var rows = prepared.Execute(Ms(200));
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Id % 5, Is.EqualTo(3));
			Assert.That(rows[i].Group == 0 || rows[i].Id > 200);
		}
	}

	// ── Parameterized condition and branch over the same arguments ────────────────

	[TestCase(null, 0)]
	[TestCase(2, 0)]
	[TestCase(2, 100)]
	[TestCase(null, 150)]
	[TestCase(-1, 0)]
	public void ParameterizedCondition_AndParameterizedBranch_ReadTheSameArgs_LikeEager(int? city, int minQty) {
		var prepared = _cache.Prepare<int, PqItem, (int? city, int minQty)>()
			.If(static a => a.city is not null, b => b.UseIndex(_byGroup, static a => a.city!.Value))
			.Where(static (v, in a) => v.Id >= a.minQty)
			.Build();
		var args = (city, minQty);

		EagerItems Eager() {
			var q = _cache.Query();
			if (city is not null) q = q.UseIndex(_byGroup, city.Value);
			return q.Where(v => v.Id >= minQty);
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));

		using var rows = prepared.Execute(args);
		Assert.That(rows.Count, Is.EqualTo(city is null ? N - minQty : city < 0 ? 0 : rows.Count));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Id, Is.GreaterThanOrEqualTo(minQty));
			if (city is not null) Assert.That(rows[i].Group, Is.EqualTo(city.Value));
		}
	}

	// ── Joined (the join sits outside the If) ─────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void If_BeforeJoinOne_Outer_LikeEager(bool cond) {
		// Customer 8 does not exist: with the If skipped, product-2 orders of customer 8 join to null.
		var prepared = _orders.Prepare<int, PqOrder, bool>()
			.UseIndex(_byProduct, 2)
			.If(static c => c, b => b.UseIndex(_byCustomer, 2))
			.JoinOne(_byCustomer, _customers)
			.Build();

		EagerOrders Eager() {
			var q = _orders.Query().UseIndex(_byProduct, 2);
			if (cond) q = q.UseIndex(_byCustomer, 2);
			return q;
		}

		AssertSameJoined(Eager().JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(cond), Customer);
		Assert.That(prepared.Count(cond), Is.EqualTo(Eager().JoinOne(_byCustomer, _customers).Count()));

		using var rows = prepared.Execute(cond);
		Assert.That(rows.Count, Is.GreaterThan(0));
		var nulls = 0;
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Left.ProductId, Is.EqualTo(2));
			if (cond) Assert.That(rows[i].Left.CustomerId, Is.EqualTo(2));
			if (rows[i].Right is null) nulls++;
		}

		Assert.That(nulls, cond ? Is.EqualTo(0) : Is.GreaterThan(0));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void If_BeforeInnerJoinOne_Parameterized_LikeEager(bool cond) {
		var prepared = _orders.Prepare<int, PqOrder, (bool cond, int customer)>()
			.If(static a => a.cond, b => b.UseIndex(_byCustomer, static a => a.customer))
			.InnerJoinOne(_byCustomer, _customers)
			.Build();

		foreach (var customer in new[] { 1, 8, 4 }) {
			var args = (cond, customer);

			EagerOrders Eager() {
				var q = _orders.Query();
				if (cond) q = q.UseIndex(_byCustomer, customer);
				return q;
			}

			AssertSameJoined(Eager().InnerJoinOne(_byCustomer, _customers).Execute(), prepared.Execute(args), Customer);
			Assert.That(prepared.Count(args), Is.EqualTo(Eager().InnerJoinOne(_byCustomer, _customers).Count()));

			using var rows = prepared.Execute(args);
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right, Is.Not.Null);
				if (cond) Assert.That(rows[i].Left.CustomerId, Is.EqualTo(customer));
			}
		}
	}

	// ── Sorted ────────────────────────────────────────────────────────────────────

	[TestCase(true)]
	[TestCase(false)]
	public void If_BeforeSortBounded_TiedPages_LikeEager_AndPagesConcatenate(bool cond) {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int bucket)>()
			.If(static a => a.cond, b => b.UseIndex(_byBucket, static a => a.bucket))
			.SortBounded(new ByGroup())
			.Build();
		var args = (cond, bucket: 2);

		EagerItems Eager() {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byBucket, args.bucket);
			return q;
		}

		foreach (var (skip, take) in new[] { (0, 5), (30, 10), (60, 20), (300, 5), (0, int.MaxValue), (3, int.MaxValue) })
			AssertSame(Eager().SortBounded(new ByGroup()).Execute(skip, take), prepared.Execute(args, skip, take));

		var whole = new List<int>();
		using (var all = prepared.Execute(args))
			for (var i = 0; i < all.Count; i++) whole.Add(all[i].Id);

		var concatenated = new List<int>();
		for (var skip = 0; ; skip += 7) {
			using var page = prepared.Execute(args, skip, 7);
			for (var i = 0; i < page.Count; i++) concatenated.Add(page[i].Id);
			if (page.Count < 7) break;
		}

		Assert.That(concatenated, Is.EqualTo(whole).AsCollection, "bounded pages partition the whole result");
	}

	// ── Reuse, leaks, concurrency ─────────────────────────────────────────────────

	[Test]
	public void Reuse_AcrossMutations_AndFlippingConditions_TracksTheLiveCache() {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int g)>()
			.If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g))
			.Build();

		EagerItems Eager(bool cond, int g) {
			var q = _cache.Query();
			if (cond) q = q.UseIndex(_byGroup, g);
			return q;
		}

		AssertSame(Eager(true, 6).Execute(), prepared.Execute((true, 6)));
		AssertSame(Eager(false, 6).Execute(), prepared.Execute((false, 6)));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6 });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved from group 6 to group 1
		_cache.AddOrUpdate(8, new PqItem { Id = 8, Code = 1008, Group = 3 });   // moved out of group 1

		AssertSame(Eager(true, 6).Execute(), prepared.Execute((true, 6)));
		AssertSame(Eager(true, 1).Execute(), prepared.Execute((true, 1)));
		AssertSame(Eager(false, 1).Execute(), prepared.Execute((false, 1)));
		Assert.That(prepared.Count((false, 0)), Is.EqualTo(N - 1));
	}

	[Test]
	public void PooledIf_TakenAndSkipped_LeavesNoRentedArrays() {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int g)>()
			.UseIndex(_byBucket, 2)
			.If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g).Or(x => x.UseIndex(_byBucket, 2), x => x.If(static a => a.g > 3, i => i.UseIndex(_byBucket, 4))))
			.Where(static (v, in a) => v.Id >= a.g)
			.Build();

		LeakAssert.Balanced(() => {
			prepared.ExecutePooled((true, 1)).Dispose();
			prepared.ExecutePooled((true, 5)).Dispose();
			prepared.ExecutePooled((false, 1)).Dispose();
			prepared.ExecutePooledCloned((true, 4), 2, 5).Dispose();
			_ = prepared.Count((true, 4));
			_ = prepared.Count((false, 4));
		});
	}

	// A throwing condition and a throwing selector inside a taken branch both propagate, strand no
	// rented array (the replay's finally releases the outer candidates), and leave the command usable.
	[Test]
	public void ThrowingCondition_AndThrowingSelectorInsideATakenBranch_Propagate_LeaveNoRentedArrays_AndCommandStaysUsable() {
		var condition = _cache.Prepare<int, PqItem, (int mode, int g)>()
			.UseIndex(_byBucket, 2)
			.If(static a => a.mode < 0 ? throw new InvalidOperationException("condition") : a.mode == 1, b => b.UseIndex(_byGroup, static a => a.g))
			.Build();
		var selector = _cache.Prepare<int, PqItem, (int mode, int g)>()
			.UseIndex(_byBucket, 2)
			.If(static a => a.mode == 1, b => b.UseIndex(_byGroup, static a => a.g < 0 ? throw new InvalidOperationException("selector") : a.g))
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => condition.ExecutePooled((-1, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => condition.Count((-1, 1)));
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((1, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.Count((1, -1)));
		});

		// A skipped branch never runs its selector, so a would-throw selector is harmless when skipped.
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).Execute(), selector.Execute((0, -1)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), condition.Execute((1, 4)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), selector.Execute((1, 4)));
	}

	[Test]
	public void ConcurrentExecutions_OfOneCommand_EachThreadFlipsTheConditionDifferently_RowsSatisfyTheirPredicate() {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int g)>()
			.If(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.g))
			.Where(static v => v.Id < 200)
			.Build();

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (cond: (seed + i) % 3 != 0, g: (seed + i) % 7);
					using var rows = prepared.ExecutePooled(args);
					Assert.That(rows.Count, Is.EqualTo(args.cond ? Expected(args.g) : 200));
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Id, Is.LessThan(200));
						if (args.cond) Assert.That(rows[r].Group, Is.EqualTo(args.g));
					}
				}
			});
		}

		Task.WaitAll(readers);

		static int Expected(int g) {
			var n = 0;
			for (var i = 0; i < 200; i++)
				if (i % 7 == g) n++;
			return n;
		}
	}
}

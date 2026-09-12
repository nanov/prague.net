namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// Or on the prepared builder, eager vs prepared from the same inputs. The prepared Or replays through
// the eager core's OrWith with branches that replay the recorded sub-chains, so every shape the eager
// Or supports — auto-seeded first Or, Or after a unique or list index, narrowing and filtering after
// it, two Ors, no-op and multi-narrower branches, nesting, parameterized branches, joins and sorted
// pages downstream — must agree on Count, TotalCount, Truncated and the exact row sequence.
[TestFixture]
public class PreparedQueryOrDifferentialTests {
	private const int N = 240;

	// Struct comparer with many ties: Group takes only 7 values over 240 rows.
	private readonly struct ByGroup : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0).CompareTo(y?.Group ?? 0);
	}

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byBucket = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;

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
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i));

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

	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static int[] Ids(QueryResults<PqItem> rows) {
		using (rows) {
			var ids = new int[rows.Count];
			for (var i = 0; i < rows.Count; i++) ids[i] = rows[i].Id;
			Array.Sort(ids);
			return ids;
		}
	}

	// ── Placement ─────────────────────────────────────────────────────────────────

	// No narrowing before the Or: the core auto-seeds its candidates from the whole cache and the Or
	// becomes the sole narrowing, so the result is the plain union.
	[Test]
	public void Or_AsFirstNarrowing_TwoListBranches_IsTheUnion_LikeEager() {
		var prepared = _cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Build();
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Execute(), prepared.Execute());
		Assert.That(prepared.Count(), Is.EqualTo(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Count()));

		using var rows = prepared.Execute();
		Assert.That(rows.Count, Is.EqualTo(N / 7 + N / 7 + (N % 7 > 1 ? 1 : 0) + (N % 7 > 4 ? 1 : 0)));
		for (var i = 0; i < rows.Count; i++) Assert.That(rows[i].Group is 1 or 4);
	}

	[Test]
	public void Or_AfterUniqueIndex_HitAndMiss_LikeEager() {
		// 15 % 7 == 1: the unique hit survives the first branch; 16 % 7 == 2 survives neither.
		var hit = _cache.Prepare().UseIndex(_byCode, 1015).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 3)).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, 1015).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 3)).Execute(), hit.Execute());
		Assert.That(Ids(hit.Execute()), Is.EqualTo(new[] { 15 }));

		var miss = _cache.Prepare().UseIndex(_byCode, 1016).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 3)).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, 1016).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 3)).Execute(), miss.Execute());
		Assert.That(miss.Count(), Is.EqualTo(0));
	}

	// The OrClauseUnpairedCoreTests scenario on the PqItem model: Group plays the country, a list index
	// over Code plays the city (Code is not registered as unique on this cache).
	//   country 12 → {1,2,3,4}, city 1 → {1,5}, city 2 → {2,6}; {1,2,3,4} ∩ ({1,5} ∪ {2,6}) = {1,2}
	[Test]
	public void Or_AfterListIndex_IntersectsWithTheUnion_LikeEager() {
		var cache = new InMemoryDataCache<int, PqItem>();
		var country = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var city = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Code);
		cache.AddOrUpdate(1, new PqItem { Id = 1, Group = 12, Code = 1 });
		cache.AddOrUpdate(2, new PqItem { Id = 2, Group = 12, Code = 2 });
		cache.AddOrUpdate(3, new PqItem { Id = 3, Group = 12, Code = 9 });
		cache.AddOrUpdate(4, new PqItem { Id = 4, Group = 12, Code = 9 });
		cache.AddOrUpdate(5, new PqItem { Id = 5, Group = 99, Code = 1 });
		cache.AddOrUpdate(6, new PqItem { Id = 6, Group = 99, Code = 2 });

		var prepared = cache.Prepare().UseIndex(country, 12).Or(b => b.UseIndex(city, 1), b => b.UseIndex(city, 2)).Build();
		AssertSame(cache.Query().UseIndex(country, 12).Or(b => b.UseIndex(city, 1), b => b.UseIndex(city, 2)).Execute(), prepared.Execute());
		Assert.That(Ids(prepared.Execute()), Is.EqualTo(new[] { 1, 2 }));

		// No-op second branch: {1,2,3,4} ∩ ({1,5} ∪ ∅) = {1}
		var noOp = cache.Prepare().UseIndex(country, 12).Or(b => b.UseIndex(city, 1), b => b).Build();
		AssertSame(cache.Query().UseIndex(country, 12).Or(b => b.UseIndex(city, 1), b => b).Execute(), noOp.Execute());
		Assert.That(Ids(noOp.Execute()), Is.EqualTo(new[] { 1 }));

		// Nested: {1,2,3,4} ∩ ({1,5} ∪ ({2,6} ∪ {9s})) = {1,2,3,4}
		var nested = cache.Prepare().UseIndex(country, 12)
			.Or(b => b.UseIndex(city, 1), b => b.Or(c => c.UseIndex(city, 2), c => c.UseIndex(city, 9)))
			.Build();
		AssertSame(
			cache.Query().UseIndex(country, 12).Or(b => b.UseIndex(city, 1), b => b.Or(c => c.UseIndex(city, 2), c => c.UseIndex(city, 9))).Execute(),
			nested.Execute());
		Assert.That(Ids(nested.Execute()), Is.EqualTo(new[] { 1, 2, 3, 4 }));
	}

	[Test]
	public void Or_ThenUseIndex_LikeEager() {
		var prepared = _cache.Prepare()
			.UseIndex(_byGroup, 2)
			.Or(b => b.UseIndex(_byBucket, 1), b => b.UseIndex(_byBucket, 3))
			.UseIndex(_codeRange, static rb => rb.Lt(1150))
			.Build();
		var eager = _cache.Query()
			.UseIndex(_byGroup, 2)
			.Or(b => b.UseIndex(_byBucket, 1), b => b.UseIndex(_byBucket, 3))
			.UseIndex(_codeRange, static rb => rb.Lt(1150))
			.Execute();
		AssertSame(eager, prepared.Execute());
		Assert.That(prepared.Count(), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 2).Or(b => b.UseIndex(_byBucket, 1), b => b.UseIndex(_byBucket, 3)).UseIndex(_codeRange, static rb => rb.Lt(1150)).Count()));
	}

	[Test]
	public void Or_ThenWhere_EveryExecuteVariant_LikeEager() {
		var prepared = _cache.Prepare()
			.Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0))
			.Where(static v => v.Flag)
			.Build();
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).Execute(), prepared.Execute());
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).ExecutePooled(), prepared.ExecutePooled());
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).ExecuteCloned(), prepared.ExecuteCloned());
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).ExecutePooledCloned(), prepared.ExecutePooledCloned());
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).Execute(3, 7), prepared.Execute(3, 7));
		Assert.That(prepared.Count(), Is.EqualTo(_cache.Query().Or(b => b.UseIndex(_byGroup, 5), b => b.UseIndex(_byBucket, 0)).Where(static v => v.Flag).Count()));
	}

	[Test]
	public void TwoOrs_InOneQuery_LikeEager() {
		var prepared = _cache.Prepare()
			.Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2))
			.Or(b => b.UseIndex(_byBucket, 0), b => b.UseIndex(_byBucket, 4))
			.Build();
		var eager = _cache.Query()
			.Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2))
			.Or(b => b.UseIndex(_byBucket, 0), b => b.UseIndex(_byBucket, 4))
			.Execute();
		AssertSame(eager, prepared.Execute());

		using var rows = prepared.Execute();
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group is 1 or 2);
			Assert.That(rows[i].Id % 5 is 0 or 4);
		}
	}

	[Test]
	public void Or_OneBranchNoOp_LikeEager() {
		var second = _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byBucket, 2), b => b).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byBucket, 2), b => b).Execute(), second.Execute());

		var first = _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b, b => b.UseIndex(_byBucket, 2)).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Or(b => b, b => b.UseIndex(_byBucket, 2)).Execute(), first.Execute());

		var both = _cache.Prepare().UseIndex(_byGroup, 3).Or(b => b, b => b).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Or(b => b, b => b).Execute(), both.Execute());
	}

	// Branch-internal AND: the branch narrows twice before the union.
	[Test]
	public void Or_BranchWithTwoNarrowers_LikeEager() {
		var prepared = _cache.Prepare()
			.UseIndex(_codeRange, static rb => rb.Lt(1200))
			.Or(b => b.UseIndex(_byGroup, 1).UseIndex(_byBucket, 2), b => b.UseIndex(_byGroup, 6))
			.Build();
		var eager = _cache.Query()
			.UseIndex(_codeRange, static rb => rb.Lt(1200))
			.Or(b => b.UseIndex(_byGroup, 1).UseIndex(_byBucket, 2), b => b.UseIndex(_byGroup, 6))
			.Execute();
		AssertSame(eager, prepared.Execute());

		using var rows = prepared.Execute();
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Code, Is.LessThan(1200));
			Assert.That((rows[i].Group == 1 && rows[i].Id % 5 == 2) || rows[i].Group == 6);
		}
	}

	[Test]
	public void Or_NestedInsideABranch_ThreeWayUnion_LikeEager() {
		var prepared = _cache.Prepare()
			.UseIndex(_byBucket, 3)
			.Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4)))
			.Build();
		var eager = _cache.Query()
			.UseIndex(_byBucket, 3)
			.Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4)))
			.Execute();
		AssertSame(eager, prepared.Execute());
		Assert.That(prepared.Count(), Is.EqualTo(_cache.Query().UseIndex(_byBucket, 3).Or(b => b.UseIndex(_byGroup, 0), b => b.Or(c => c.UseIndex(_byGroup, 2), c => c.UseIndex(_byGroup, 4))).Count()));

		using var rows = prepared.Execute();
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Id % 5, Is.EqualTo(3));
			Assert.That(rows[i].Group is 0 or 2 or 4);
		}
	}

	[Test]
	public void Or_MultiValueBranch_AndRangeBranch_LikeEager() {
		var groups = new[] { 1, 5 };
		var prepared = _cache.Prepare()
			.Or(b => b.UseIndex(_byGroup, groups), b => b.UseIndex(_codeRange, static rb => rb.Gte(1200).Lt(1210)))
			.Build();
		var eager = _cache.Query()
			.Or(b => b.UseIndex(_byGroup, groups.AsSpan()), b => b.UseIndex(_codeRange, static rb => rb.Gte(1200).Lt(1210)))
			.Execute();
		AssertSame(eager, prepared.Execute());

		using var rows = prepared.Execute();
		for (var i = 0; i < rows.Count; i++)
			Assert.That(rows[i].Group is 1 or 5 || rows[i].Code is >= 1200 and < 1210);
	}

	// ── Parameterized ─────────────────────────────────────────────────────────────

	[Test]
	public void Or_BothBranchesParameterized_FourArgSets_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int city1, int city2)>()
			.UseIndex(_byBucket, 1)
			.Or(b => b.UseIndex(_byGroup, static a => a.city1), b => b.UseIndex(_byGroup, static a => a.city2))
			.Build();

		foreach (var args in new[] { (city1: 0, city2: 6), (city1: 3, city2: 3), (city1: 2, city2: 5), (city1: -1, city2: 4) }) {
			AssertSame(
				_cache.Query().UseIndex(_byBucket, 1).Or(b => b.UseIndex(_byGroup, args.city1), b => b.UseIndex(_byGroup, args.city2)).Execute(),
				prepared.Execute(args));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byBucket, 1).Or(b => b.UseIndex(_byGroup, args.city1), b => b.UseIndex(_byGroup, args.city2)).Count()));

			using var rows = prepared.Execute(args);
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Id % 5, Is.EqualTo(1));
				Assert.That(rows[i].Group == args.city1 || rows[i].Group == args.city2);
			}
		}
	}

	[Test]
	public void Or_OneBoundAndOneParameterizedBranch_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.Or(b => b.UseIndex(_byGroup, 6), b => b.UseIndex(_byBucket, static bucket => bucket))
			.Build();

		for (var bucket = -1; bucket < 5; bucket++) {
			var eager = _cache.Query().Or(b => b.UseIndex(_byGroup, 6), b => b.UseIndex(_byBucket, bucket)).Execute();
			AssertSame(eager, prepared.Execute(bucket));
			Assert.That(prepared.Count(bucket), Is.EqualTo(_cache.Query().Or(b => b.UseIndex(_byGroup, 6), b => b.UseIndex(_byBucket, bucket)).Count()));
		}
	}

	// ── Joined ────────────────────────────────────────────────────────────────────

	[Test]
	public void Or_ThenJoinOne_Outer_LikeEager() {
		// Customer 8 does not exist: the second branch's orders join to null.
		var prepared = _orders.Prepare()
			.UseIndex(_byProduct, 2)
			.Or(b => b.UseIndex(_byCustomer, 2), b => b.UseIndex(_byCustomer, 8))
			.JoinOne(_byCustomer, _customers)
			.Build();
		AssertSameJoined(
			_orders.Query().UseIndex(_byProduct, 2).Or(b => b.UseIndex(_byCustomer, 2), b => b.UseIndex(_byCustomer, 8)).JoinOne(_byCustomer, _customers).Execute(),
			prepared.Execute(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 2).Or(b => b.UseIndex(_byCustomer, 2), b => b.UseIndex(_byCustomer, 8)).JoinOne(_byCustomer, _customers).Count()));

		using var rows = prepared.Execute();
		Assert.That(rows.Count, Is.GreaterThan(0));
		var nulls = 0;
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Left.ProductId, Is.EqualTo(2));
			Assert.That(rows[i].Left.CustomerId is 2 or 8);
			if (rows[i].Right is null) nulls++;
		}

		Assert.That(nulls, Is.GreaterThan(0), "customer 8 rows join to null");
	}

	[Test]
	public void Or_ThenInnerJoinOne_Parameterized_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, (int c1, int c2)>()
			.Or(b => b.UseIndex(_byCustomer, static a => a.c1), b => b.UseIndex(_byCustomer, static a => a.c2))
			.InnerJoinOne(_byCustomer, _customers)
			.Build();

		foreach (var args in new[] { (c1: 1, c2: 8), (c1: 9, c2: 8), (c1: 0, c2: 7), (c1: 4, c2: 4) }) {
			AssertSameJoined(
				_orders.Query().Or(b => b.UseIndex(_byCustomer, args.c1), b => b.UseIndex(_byCustomer, args.c2)).InnerJoinOne(_byCustomer, _customers).Execute(),
				prepared.Execute(args), Customer);
			Assert.That(prepared.Count(args), Is.EqualTo(_orders.Query().Or(b => b.UseIndex(_byCustomer, args.c1), b => b.UseIndex(_byCustomer, args.c2)).InnerJoinOne(_byCustomer, _customers).Count()));

			using var rows = prepared.Execute(args);
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right, Is.Not.Null);
				Assert.That(rows[i].Left.CustomerId == args.c1 || rows[i].Left.CustomerId == args.c2);
			}
		}
	}

	// ── Sorted ────────────────────────────────────────────────────────────────────

	[Test]
	public void Or_ThenSortBounded_TiedPages_LikeEager_AndPagesConcatenate() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.SortBounded(new ByGroup())
			.Build();
		var args = (g1: 2, g2: 5);

		foreach (var (skip, take) in new[] { (0, 5), (30, 10), (60, 20), (300, 5), (0, int.MaxValue), (3, int.MaxValue) })
			AssertSame(
				_cache.Query().Or(b => b.UseIndex(_byGroup, args.g1), b => b.UseIndex(_byGroup, args.g2)).SortBounded(new ByGroup()).Execute(skip, take),
				prepared.Execute(args, skip, take));

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
	public void Reuse_AcrossMutations_TracksTheLiveCache() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Build();
		var args = (g1: 6, g2: 1);

		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 6), b => b.UseIndex(_byGroup, 1)).Execute(), prepared.Execute(args));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6 });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved from group 6 to group 1
		_cache.AddOrUpdate(8, new PqItem { Id = 8, Code = 1008, Group = 3 });   // moved out of both

		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 6), b => b.UseIndex(_byGroup, 1)).Execute(), prepared.Execute(args));
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 3), b => b.UseIndex(_byGroup, 1)).Execute(), prepared.Execute((3, 1)));
	}

	[Test]
	public void PooledOr_LeavesNoRentedArrays() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>()
			.UseIndex(_byBucket, 2)
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.Or(c => c.UseIndex(_byGroup, static a => a.g2), c => c.UseIndex(_byBucket, 4)))
			.Build();
		var args = (g1: 1, g2: 4);

		LeakAssert.Balanced(() => {
			prepared.ExecutePooled(args).Dispose();
			prepared.ExecutePooledCloned(args, 2, 5).Dispose();
			_ = prepared.Count(args);
		});
	}

	// OrWith's own try/finally releases the child intersecters when a branch throws, and the replay
	// releases the outer core's candidates; neither may strand a rented array, and the command must
	// still be usable afterwards.
	[Test]
	public void ThrowingSelector_InsideABranch_Propagates_LeavesNoRentedArrays_AndCommandStaysUsable() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>()
			.UseIndex(_byBucket, 2)
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2 < 0 ? throw new InvalidOperationException("boom") : a.g2))
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => prepared.ExecutePooled((1, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => prepared.Count((1, -1)));
		});

		AssertSame(_cache.Query().UseIndex(_byBucket, 2).Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 4)).Execute(), prepared.Execute((1, 4)));
	}

	[Test]
	public void ConcurrentExecutions_OfOneCommand_DifferentArgsPerThread_SatisfyTheDisjunction() {
		var prepared = _cache.Prepare<int, PqItem, (int g1, int g2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Where(static v => v.Id < 200)
			.Build();

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (g1: (seed + i) % 7, g2: (seed + 3 * i) % 7);
					using var rows = prepared.ExecutePooled(args);
					Assert.That(rows.Count, Is.GreaterThan(0));
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Group == args.g1 || rows[r].Group == args.g2);
						Assert.That(rows[r].Id, Is.LessThan(200));
					}
				}
			});
		}

		Task.WaitAll(readers);
	}
}

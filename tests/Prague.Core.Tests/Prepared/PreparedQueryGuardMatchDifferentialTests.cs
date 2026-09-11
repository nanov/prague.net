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

// The guard form of Match — Match(arms) with Case(predicate, arm) — eager vs prepared vs frozen from
// the same inputs, for every arm and the default. The eager twin is what a caller writes today: a C#
// `if / else if / else` chain over type-preserving UseIndex / Where reassignments of the eager
// builder. Every shape must agree on Count, TotalCount, Truncated and the exact row sequence, and
// Count() must agree too. The tag form's own tests are PreparedQueryMatchDifferentialTests; these
// mirror them arm for arm.
[TestFixture]
public class PreparedQueryGuardMatchDifferentialTests {
	private const int N = 240;

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

	// Rows and Count from the same eager description, built twice because Execute consumes the builder.
	private static void AssertParity(Func<EagerItems> eager, Func<QueryResults<PqItem>> rows, Func<int> count) {
		AssertSame(eager().Execute(), rows());
		Assert.That(count(), Is.EqualTo(eager().Count()), "Count()");
	}

	// The three-guard-plus-default command most tests dispatch on, and its eager `if / else if` twin.
	// The guards are deliberately not disjoint — g >= 0 also holds when bucket >= 0 — so the
	// declaration-order rule is exercised by every argument that reaches the second or third arm.
	private PreparedQuery<(int mode, int g, int bucket, int code, int min), PqItem> ThreeGuardsWithDefault()
		=> _cache.Prepare<int, PqItem, (int mode, int g, int bucket, int code, int min)>()
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(static a => a.mode == 1, b => b.UseIndex(_byBucket, static a => a.bucket).Where(static (v, in a) => v.Id >= a.min))
				.Case(static a => a.mode == 2, b => b.UseIndex(_byCode, static a => a.code))
				.Default(b => b.Where(static v => v.Flag)))
			.Build();

	private EagerItems EagerThreeGuardsWithDefault((int mode, int g, int bucket, int code, int min) a) {
		var q = _cache.Query();
		if (a.mode == 0)
			q = q.UseIndex(_byGroup, a.g);
		else if (a.mode == 1)
			q = q.UseIndex(_byBucket, a.bucket).Where(v => v.Id >= a.min);
		else if (a.mode == 2)
			q = q.UseIndex(_byCode, a.code);
		else
			q = q.Where(static v => v.Flag);
		return q;
	}

	// ── Every arm, the default ────────────────────────────────────────────────────

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	public void EveryGuardAndTheDefault_LikeEagerIfElseChain(int mode) {
		var prepared = ThreeGuardsWithDefault();
		var args = (mode, g: 3, bucket: 2, code: 1042, min: 100);

		AssertParity(() => EagerThreeGuardsWithDefault(args), () => prepared.Execute(args), () => prepared.Count(args));

		using var rows = prepared.Execute(args);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			var v = rows[i];
			switch (mode) {
				case 0: Assert.That(v.Group, Is.EqualTo(3)); break;
				case 1: Assert.That(v.Id % 5 == 2 && v.Id >= 100); break;
				case 2: Assert.That(v.Code, Is.EqualTo(1042)); break;
				default: Assert.That(v.Flag); break;
			}
		}
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(7)]
	public void EmptyDefault_NoGuardHolds_IsANoOp_LikeTheEagerFallThrough(int tag) {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byBucket, 1)
			.Match(m => m
				.Case(static t => t == 0, b => b.UseIndex(_byGroup, 0))
				.Case(static t => t == 1, b => b.UseIndex(_byGroup, 1))
				.Default())
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byBucket, 1);
			if (tag == 0)
				q = q.UseIndex(_byGroup, 0);
			else if (tag == 1)
				q = q.UseIndex(_byGroup, 1);
			return q;
		}

		AssertParity(Eager, () => prepared.Execute(tag), () => prepared.Count(tag));
		Assert.That(prepared.Count(7), Is.EqualTo(N / 5), "an empty Default() narrows nothing further");
	}

	// ── Guard ordering ────────────────────────────────────────────────────────────

	// The contract: guards are tried in declaration order and the first that holds wins, so overlapping
	// guards resolve to the earlier arm and the later ones never run. Both halves are asserted — the arm
	// chosen, and (through a guard that would throw) that the guards after the winner are not called.
	[Test]
	public void OverlappingGuards_FirstDeclaredWins_AndLaterGuardsAreNeverCalled() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.Match(m => m
				.Case(static t => t > 0, b => b.UseIndex(_byGroup, 2))
				.Case(static t => t > 1, b => b.UseIndex(_byGroup, 5))
				.Default(b => b.UseIndex(_byGroup, 6)))
			.Build();

		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(), prepared.Execute(1));
		// The second guard holds for 9 too, and loses: the first declared arm is the one that replays.
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(), prepared.Execute(9));
		AssertSame(_cache.Query().UseIndex(_byGroup, 6).Execute(), prepared.Execute(0));

		var shortCircuit = _cache.Prepare<int, PqItem, int>()
			.Match(m => m
				.Case(static t => t > 0, b => b.UseIndex(_byGroup, 2))
				.Case(static t => throw new InvalidOperationException("second guard"), b => b.UseIndex(_byGroup, 5))
				.Default())
			.Build();

		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(), shortCircuit.Execute(1));
		Assert.That(shortCircuit.Count(1), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 2).Count()));
		Assert.Throws<InvalidOperationException>(() => shortCircuit.Count(0), "with the first guard false the second is reached — and throws");
	}

	// ── Placement and seeding ─────────────────────────────────────────────────────

	// Seeding falls out of the eager `_first` logic: a guard Match whose selected arm narrows nothing —
	// either spelling of the empty default — as the FIRST narrower must let the following UseIndex seed
	// the candidate set rather than intersect with an empty one. This is the invariant that makes a
	// skipped If correct, since an If is exactly this shape.
	[TestCase(0)]
	[TestCase(5)]
	public void GuardMatch_First_NoOpArmSelected_LetsTheNextNarrowerSeed(int tag) {
		var bareDefault = _cache.Prepare<int, PqItem, int>()
			.Match(m => m.Case(static t => t == 0, b => b.UseIndex(_byGroup, 3)).Default())
			.UseIndex(_byBucket, 2)
			.Build();
		var emptyDefault = _cache.Prepare<int, PqItem, int>()
			.Match(m => m.Case(static t => t == 0, b => b.UseIndex(_byGroup, 3)).Default(b => b))
			.UseIndex(_byBucket, 2)
			.Build();

		EagerItems Eager() {
			var q = _cache.Query();
			if (tag == 0) q = q.UseIndex(_byGroup, 3);
			return q.UseIndex(_byBucket, 2);
		}

		AssertParity(Eager, () => bareDefault.Execute(tag), () => bareDefault.Count(tag));
		AssertParity(Eager, () => emptyDefault.Execute(tag), () => emptyDefault.Count(tag));
		Assert.That(bareDefault.Count(tag), Is.EqualTo(tag == 0 ? 7 : N / 5));
		Assert.That(emptyDefault.Count(tag), Is.EqualTo(bareDefault.Count(tag)), "Default() and Default(b => b) are the same arm");
	}

	[Test]
	public void GuardMatch_AsOnlyNarrower_EmptyDefaultIsAnAllRowsScan() {
		var prepared = _cache.Prepare<int, PqItem, int>().Match(m => m.Case(static t => t == 1, b => b.UseIndex(_byGroup, 1)).Default()).Build();
		AssertSame(_cache.Query().Execute(), prepared.Execute(99));
		Assert.That(prepared.Count(99), Is.EqualTo(N));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Execute(), prepared.Execute(1));
	}

	// ── Nesting ───────────────────────────────────────────────────────────────────

	[TestCase(0, 0)]
	[TestCase(0, 1)]
	[TestCase(0, 9)]
	[TestCase(1, 0)]
	public void NestedGuardMatch_InsideAnArm_LikeEager(int mode, int inner) {
		var prepared = _cache.Prepare<int, PqItem, (int mode, int inner)>()
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, 2).Match(n => n
					.Case(static a => a.inner == 0, c => c.UseIndex(_byBucket, 0))
					.Case(static a => a.inner == 1, c => c.Where(static v => v.Flag))
					.Default()))
				.Case(static a => a.mode == 1, b => b.UseIndex(_byBucket, 3))
				.Default())
			.Where(static v => v.Id < 200)
			.Build();
		var args = (mode, inner);

		EagerItems Eager() {
			var q = _cache.Query();
			if (mode == 0) {
				q = q.UseIndex(_byGroup, 2);
				if (inner == 0)
					q = q.UseIndex(_byBucket, 0);
				else if (inner == 1)
					q = q.Where(static v => v.Flag);
			} else if (mode == 1) {
				q = q.UseIndex(_byBucket, 3);
			}

			return q.Where(static v => v.Id < 200);
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	[TestCase(true, 0)]
	[TestCase(true, 1)]
	[TestCase(false, 0)]
	public void GuardMatch_InsideIf_LikeEager(bool cond, int mode) {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, int mode)>()
			.UseIndex(_codeRange, static rb => rb.Lt(1200))
			.If(static a => a.cond, b => b.Match(m => m
				.Case(static a => a.mode == 0, c => c.UseIndex(_byGroup, 1))
				.Case(static a => a.mode == 1, c => c.UseIndex(_byBucket, 1).Where(static v => !v.Flag))
				.Default()))
			.Build();
		var args = (cond, mode);

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1200));
			if (cond) {
				if (mode == 0)
					q = q.UseIndex(_byGroup, 1);
				else if (mode == 1)
					q = q.UseIndex(_byBucket, 1).Where(static v => !v.Flag);
			}

			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	// A guard Match inside a tag Match's arm: the two forms compose, and the outer arm chain is the tag
	// one while the inner is the guard one.
	[TestCase(1, 0)]
	[TestCase(1, 4)]
	[TestCase(9, 0)]
	public void GuardMatch_InsideATagMatchArm_LikeEager(int tag, int inner) {
		var prepared = _cache.Prepare<int, PqItem, (int tag, int inner)>()
			.Match(static a => a.tag, m => m
				.Case(1, b => b.UseIndex(_byGroup, 2).Match(n => n
					.Case(static a => a.inner == 0, c => c.UseIndex(_byBucket, 2))
					.Default(c => c.Where(static v => v.Flag))))
				.Default())
			.Build();
		var args = (tag, inner);

		EagerItems Eager() {
			var q = _cache.Query();
			if (tag == 1) {
				q = q.UseIndex(_byGroup, 2);
				q = inner == 0 ? q.UseIndex(_byBucket, 2) : q.Where(static v => v.Flag);
			}

			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	// A guard Match inside an Or branch (narrow-only arms). The eager branch builder is type-preserving,
	// so the eager twin is a switch expression over the branch; no guard holding is the eager no-op branch.
	[TestCase(0)]
	[TestCase(1)]
	[TestCase(9)]
	public void GuardMatch_InsideOrBranch_UseIndexOnlyArms_LikeEager(int mode) {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, 3)
			.Or(b => b.UseIndex(_byBucket, 1), b => b.Match(m => m
				.Case(static t => t == 0, c => c.UseIndex(_byBucket, 4))
				.Case(static t => t == 1, c => c.UseIndex(_byBucket, 0))
				.Default()))
			.Build();

		EagerItems Eager()
			=> _cache.Query().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byBucket, 1), b => mode switch {
				0 => b.UseIndex(_byBucket, 4),
				1 => b.UseIndex(_byBucket, 0),
				_ => b,
			});

		AssertParity(Eager, () => prepared.Execute(mode), () => prepared.Count(mode));

		using var rows = prepared.Execute(mode);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group, Is.EqualTo(3));
			var bucket = rows[i].Id % 5;
			Assert.That(mode switch { 0 => bucket is 1 or 4, 1 => bucket is 1 or 0, _ => bucket == 1 });
		}
	}

	// ── Joined and sorted (the Match sits before the resolver) ────────────────────

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	public void GuardMatch_BeforeJoinOne_AndBeforeInnerJoinOne_LikeEager(int tag) {
		// Customer 8 does not exist: the unmatched tag leaves product-2 orders of customer 8 joining to null.
		var outer = _orders.Prepare<int, PqOrder, (int tag, int customer)>()
			.UseIndex(_byProduct, 2)
			.Match(m => m
				.Case(static a => a.tag == 0, b => b.UseIndex(_byCustomer, static a => a.customer))
				.Case(static a => a.tag == 1, b => b.UseIndex(_byCustomer, 8))
				.Default())
			.JoinOne(_byCustomer, _customers)
			.Build();
		var inner = _orders.Prepare<int, PqOrder, (int tag, int customer)>()
			.UseIndex(_byProduct, 2)
			.Match(m => m
				.Case(static a => a.tag == 0, b => b.UseIndex(_byCustomer, static a => a.customer))
				.Case(static a => a.tag == 1, b => b.UseIndex(_byCustomer, 8))
				.Default())
			.InnerJoinOne(_byCustomer, _customers)
			.Build();
		var args = (tag, customer: 2);

		EagerOrders Eager() {
			var q = _orders.Query().UseIndex(_byProduct, 2);
			if (tag == 0)
				q = q.UseIndex(_byCustomer, args.customer);
			else if (tag == 1)
				q = q.UseIndex(_byCustomer, 8);
			return q;
		}

		AssertSameJoined(Eager().JoinOne(_byCustomer, _customers).Execute(), outer.Execute(args), Customer);
		Assert.That(outer.Count(args), Is.EqualTo(Eager().JoinOne(_byCustomer, _customers).Count()));
		AssertSameJoined(Eager().InnerJoinOne(_byCustomer, _customers).Execute(), inner.Execute(args), Customer);
		Assert.That(inner.Count(args), Is.EqualTo(Eager().InnerJoinOne(_byCustomer, _customers).Count()));
		Assert.That(inner.Count(args), tag == 1 ? Is.EqualTo(0) : Is.GreaterThan(0));
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(9)]
	public void GuardMatch_BeforeSortBounded_TiedPages_LikeEager_AndPagesConcatenate(int mode) {
		var prepared = _cache.Prepare<int, PqItem, (int mode, int g, int bucket)>()
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(static a => a.mode == 1, b => b.UseIndex(_byBucket, static a => a.bucket))
				.Default())
			.SortBounded(new ByGroup())
			.Build();
		var args = (mode, g: 4, bucket: 2);

		EagerItems Eager() {
			var q = _cache.Query();
			if (mode == 0)
				q = q.UseIndex(_byGroup, args.g);
			else if (mode == 1)
				q = q.UseIndex(_byBucket, args.bucket);
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
	public void Reuse_AcrossMutations_AndSwitchingArms_TracksTheLiveCache() {
		var prepared = ThreeGuardsWithDefault();
		(int mode, int g, int bucket, int code, int min) Args(int mode) => (mode, 6, 1, 1020, 0);

		foreach (var mode in new[] { 0, 1, 2, 3 })
			AssertSame(EagerThreeGuardsWithDefault(Args(mode)).Execute(), prepared.Execute(Args(mode)));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6, Flag = true });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved from group 6 to group 1, still code 1020
		_cache.AddOrUpdate(8, new PqItem { Id = 8, Code = 7000, Group = 3, Flag = false });

		foreach (var mode in new[] { 0, 1, 2, 3 }) {
			AssertSame(EagerThreeGuardsWithDefault(Args(mode)).Execute(), prepared.Execute(Args(mode)));
			Assert.That(prepared.Count(Args(mode)), Is.EqualTo(EagerThreeGuardsWithDefault(Args(mode)).Count()));
		}
	}

	[Test]
	public void PooledGuardMatch_EveryArm_LeavesNoRentedArrays() {
		var prepared = _cache.Prepare<int, PqItem, (int mode, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, static a => a.g).Or(x => x.UseIndex(_byBucket, 2), x => x.Match(n => n.Case(static a => a.g == 4, i => i.UseIndex(_byBucket, 4)).Default())))
				.Case(static a => a.mode == 1, b => b.If(static a => a.g > 3, i => i.UseIndex(_byGroup, 1)))
				.Default(b => b.Where(static (v, in a) => v.Id >= a.g)))
			.Where(static (v, in a) => v.Id >= a.g)
			.Build();

		LeakAssert.Balanced(() => {
			prepared.ExecutePooled((0, 1)).Dispose();
			prepared.ExecutePooled((0, 4)).Dispose();
			prepared.ExecutePooled((1, 5)).Dispose();
			prepared.ExecutePooled((1, 1)).Dispose();
			prepared.ExecutePooled((9, 100)).Dispose();
			prepared.ExecutePooledCloned((0, 4), 2, 5).Dispose();
			_ = prepared.Count((0, 4));
			_ = prepared.Count((9, 4));
		});
	}

	// A throwing guard and a throwing selector inside the selected arm both propagate, strand no rented
	// array (the replay's finally releases the outer candidates), and leave the command usable.
	[Test]
	public void ThrowingGuard_AndThrowingArmSelector_Propagate_LeaveNoRentedArrays_AndCommandStaysUsable() {
		var guard = _cache.Prepare<int, PqItem, (int tag, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(m => m.Case(static a => a.tag < 0 ? throw new InvalidOperationException("guard") : a.tag == 1, b => b.UseIndex(_byGroup, static a => a.g)).Default())
			.Build();
		var arm = _cache.Prepare<int, PqItem, (int tag, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(m => m.Case(static a => a.tag == 1, b => b.UseIndex(_byGroup, static a => a.g < 0 ? throw new InvalidOperationException("arm") : a.g)).Default())
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => guard.ExecutePooled((-1, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => guard.Count((-1, 1)));
			Assert.Throws<InvalidOperationException>(() => arm.ExecutePooled((1, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => arm.Count((1, -1)));
		});

		// An unselected arm never runs its selector, so a would-throw selector is harmless when not selected.
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).Execute(), arm.Execute((0, -1)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), guard.Execute((1, 4)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), arm.Execute((1, 4)));
	}

	[Test]
	public void ConcurrentExecutions_OfOneCommand_EachThreadDispatchesDifferently_RowsSatisfyTheirArm() {
		var prepared = _cache.Prepare<int, PqItem, (int mode, int g)>()
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(static a => a.mode == 1, b => b.UseIndex(_byBucket, static a => a.g % 5))
				.Default(b => b.Where(static v => v.Flag)))
			.Where(static v => v.Id < 200)
			.Build();

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (mode: (seed + i) % 4, g: (seed + i) % 7);
					using var rows = prepared.ExecutePooled(args);
					Assert.That(rows.Count, Is.EqualTo(Expected(args.mode, args.g)));
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Id, Is.LessThan(200));
						switch (args.mode) {
							case 0: Assert.That(rows[r].Group, Is.EqualTo(args.g)); break;
							case 1: Assert.That(rows[r].Id % 5, Is.EqualTo(args.g % 5)); break;
							default: Assert.That(rows[r].Flag); break;
						}
					}
				}
			});
		}

		Task.WaitAll(readers);

		static int Expected(int mode, int g) {
			var n = 0;
			for (var i = 0; i < 200; i++)
				if (mode switch { 0 => i % 7 == g, 1 => i % 5 == g % 5, _ => i % 3 == 0 }) n++;
			return n;
		}
	}

	// ── Frozen ────────────────────────────────────────────────────────────────────

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	public void Frozen_GuardMatch_PipelinesAndExplainsTheArms_FrozenEqualsPreparedEqualsEager(int mode) {
		var frozen = _cache.Prepare<int, PqItem, (int mode, int g, int bucket, int code, int min)>()
			.Match(m => m
				.Case(static a => a.mode == 0, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(static a => a.mode == 1, b => b.UseIndex(_byBucket, static a => a.bucket).Where(static (v, in a) => v.Id >= a.min))
				.Case(static a => a.mode == 2, b => b.UseIndex(_byCode, static a => a.code))
				.Default(b => b.Where(static v => v.Flag)))
			.BuildFrozen();
		var prepared = ThreeGuardsWithDefault();
		var args = (mode, g: 3, bucket: 2, code: 1042, min: 100);

		AssertSame(EagerThreeGuardsWithDefault(args).Execute(), frozen.Execute(args));
		AssertSame(prepared.Execute(args), frozen.Execute(args));
		Assert.That(frozen.Count(args), Is.EqualTo(prepared.Count(args)));

		var plan = frozen.Plan;
		var match = plan.Narrowers[0];
		Assert.Multiple(() => {
			Assert.That(plan.Executor, Is.EqualTo("Pipeline"), "step 4: the arm is chosen at bind");
			Assert.That(plan.Narrowers, Has.Count.EqualTo(1));
			Assert.That(match.Kind, Is.EqualTo(NarrowerKind.Match));
			Assert.That(match.IsParameterized, Is.True);
			Assert.That(match.Selector, Is.Null, "the guard form has no tag selector");
			Assert.That(match.Children, Has.Count.EqualTo(4), "three guarded arms and the default");
			Assert.That(match.Children[0][0].Kind, Is.EqualTo(NarrowerKind.ListEq));
			Assert.That(match.Children[1].Select(static d => d.Kind), Is.EqualTo(new[] { NarrowerKind.ListEq, NarrowerKind.FilterArg }).AsCollection);
			Assert.That(match.Children[2][0].Kind, Is.EqualTo(NarrowerKind.UniqueEq));
			Assert.That(match.Children[3][0].Kind, Is.EqualTo(NarrowerKind.Filter));
			var guards = (MatchArmGuards)match.Value!;
			Assert.That(guards.Guards, Has.Count.EqualTo(3));
			for (var i = 0; i < guards.Guards.Count; i++)
				Assert.That(guards.Guards[i], Is.InstanceOf<Func<(int mode, int g, int bucket, int code, int min), bool>>());
			Assert.That(guards.ToString(), Is.EqualTo("[guard#0, guard#1, guard#2, default]"));
		});

		var text = frozen.Explain();
		Assert.That(text, Does.Contain("executor: Pipeline").And.Contain("Match (arg)").And.Contain("guard#0:").And.Contain("guard#2:").And.Contain("default:")
			.And.Contain("match#0 {guard#0: [step 0 ListEq]; guard#1: [step 1 ListEq, branch filter 0]; guard#2: [step 2 UniqueEq]; default: [branch filter 1]}")
			.And.Contain("select#0 → arm " + (mode is 0 or 1 or 2 ? mode : 3)));
	}

	// The empty Default() is a real, described arm — an empty sub-chain the plan carries and explains.
	[Test]
	public void Frozen_GuardMatch_EmptyDefault_DescribesAnEmptyDefaultArm() {
		var frozen = _cache.Prepare<int, PqItem, int>().Match(m => m.Case(static t => t == 1, b => b.UseIndex(_byGroup, 1)).Default()).BuildFrozen();
		var guards = (MatchArmGuards)frozen.Plan.Narrowers[0].Value!;
		Assert.Multiple(() => {
			Assert.That(guards.Guards, Has.Count.EqualTo(1));
			Assert.That(guards.ToString(), Is.EqualTo("[guard#0, default]"));
			Assert.That(frozen.Plan.Narrowers[0].Children, Has.Count.EqualTo(2), "the guarded arm and the empty default");
			Assert.That(frozen.Plan.Narrowers[0].Children[1], Is.Empty);
			Assert.That(frozen.Explain(), Does.Contain("guard#0:").And.Contain("default:"));
		});

		AssertSame(_cache.Query().Execute(), frozen.Execute(99));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Execute(), frozen.Execute(1));
	}
}

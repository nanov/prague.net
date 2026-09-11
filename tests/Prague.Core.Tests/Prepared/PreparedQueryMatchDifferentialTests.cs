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

// Match on the prepared builder, eager vs prepared from the same inputs, for every arm, the default
// and the unmatched-no-default case. The eager twin is what a caller writes today: a C# `switch` over
// type-preserving UseIndex / Where reassignments of the eager builder. Every shape must agree on
// Count, TotalCount, Truncated and the exact row sequence, and Count() must agree too.
[TestFixture]
public class PreparedQueryMatchDifferentialTests {
	private const int N = 240;

	public enum Mode { ByGroup, ByBucket, ByCode, Unhandled }

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

	// The three-arm-plus-default command most tests dispatch on, and its eager `switch` twin.
	private PreparedQuery<(Mode mode, int g, int bucket, int code, int min), PqItem> ThreeArmsWithDefault()
		=> _cache.Prepare<int, PqItem, (Mode mode, int g, int bucket, int code, int min)>()
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(Mode.ByBucket, b => b.UseIndex(_byBucket, static a => a.bucket).Where(static (v, in a) => v.Id >= a.min))
				.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
				.Default(b => b.Where(static v => v.Flag)))
			.Build();

	private EagerItems EagerThreeArmsWithDefault((Mode mode, int g, int bucket, int code, int min) a) {
		var q = _cache.Query();
		switch (a.mode) {
			case Mode.ByGroup:
				q = q.UseIndex(_byGroup, a.g);
				break;
			case Mode.ByBucket:
				q = q.UseIndex(_byBucket, a.bucket).Where(v => v.Id >= a.min);
				break;
			case Mode.ByCode:
				q = q.UseIndex(_byCode, a.code);
				break;
			default:
				q = q.Where(static v => v.Flag);
				break;
		}

		return q;
	}

	// ── Every arm, the default, unmatched ─────────────────────────────────────────

	[TestCase(Mode.ByGroup)]
	[TestCase(Mode.ByBucket)]
	[TestCase(Mode.ByCode)]
	[TestCase(Mode.Unhandled)]
	public void EnumTag_EveryArmAndTheDefault_LikeEagerSwitch(Mode mode) {
		var prepared = ThreeArmsWithDefault();
		var args = (mode, g: 3, bucket: 2, code: 1042, min: 100);

		AssertParity(() => EagerThreeArmsWithDefault(args), () => prepared.Execute(args), () => prepared.Count(args));

		using var rows = prepared.Execute(args);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			var v = rows[i];
			switch (mode) {
				case Mode.ByGroup: Assert.That(v.Group, Is.EqualTo(3)); break;
				case Mode.ByBucket: Assert.That(v.Id % 5 == 2 && v.Id >= 100); break;
				case Mode.ByCode: Assert.That(v.Code, Is.EqualTo(1042)); break;
				default: Assert.That(v.Flag); break;
			}
		}
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(7)]
	public void IntTag_EmptyDefault_UnmatchedIsANoOp_LikeEagerSwitch(int tag) {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byBucket, 1)
			.Match(static t => t, m => m
				.Case(0, b => b.UseIndex(_byGroup, 0))
				.Case(1, b => b.UseIndex(_byGroup, 1))
				.Default())
			.Build();

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_byBucket, 1);
			switch (tag) {
				case 0: q = q.UseIndex(_byGroup, 0); break;
				case 1: q = q.UseIndex(_byGroup, 1); break;
			}

			return q;
		}

		AssertParity(Eager, () => prepared.Execute(tag), () => prepared.Count(tag));
		Assert.That(prepared.Count(7), Is.EqualTo(N / 5), "an empty Default() narrows nothing further");
	}

	// ── Placement and seeding ─────────────────────────────────────────────────────

	// Seeding falls out of the eager `_first` logic: a Match whose selected arm narrows nothing — either
	// spelling of the empty default — as the FIRST narrower must let the following UseIndex seed the
	// candidate set rather than intersect with an empty one.
	[TestCase(0)]
	[TestCase(5)]
	public void Match_First_NoOpArmSelected_LetsTheNextNarrowerSeed(int tag) {
		var bareDefault = _cache.Prepare<int, PqItem, int>()
			.Match(static t => t, m => m.Case(0, b => b.UseIndex(_byGroup, 3)).Default())
			.UseIndex(_byBucket, 2)
			.Build();
		var emptyDefault = _cache.Prepare<int, PqItem, int>()
			.Match(static t => t, m => m.Case(0, b => b.UseIndex(_byGroup, 3)).Default(b => b))
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
	public void Match_AsOnlyNarrower_EmptyDefaultIsAnAllRowsScan() {
		var prepared = _cache.Prepare<int, PqItem, int>().Match(static t => t, m => m.Case(1, b => b.UseIndex(_byGroup, 1)).Default()).Build();
		AssertSame(_cache.Query().Execute(), prepared.Execute(99));
		Assert.That(prepared.Count(99), Is.EqualTo(N));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Execute(), prepared.Execute(1));
	}

	// ── Arm ordering ──────────────────────────────────────────────────────────────

	[Test]
	public void DuplicateTag_FirstDeclaredArmWins() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.Match(static t => t, m => m
				.Case(1, b => b.UseIndex(_byGroup, 2))
				.Case(1, b => b.UseIndex(_byGroup, 5))
				.Default(b => b.UseIndex(_byGroup, 6)))
			.Build();

		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(), prepared.Execute(1));
		AssertSame(_cache.Query().UseIndex(_byGroup, 6).Execute(), prepared.Execute(2));
	}

	// ── Nesting ───────────────────────────────────────────────────────────────────

	[TestCase(Mode.ByGroup, 0)]
	[TestCase(Mode.ByGroup, 1)]
	[TestCase(Mode.ByGroup, 9)]
	[TestCase(Mode.ByBucket, 0)]
	public void NestedMatch_InsideAnArm_LikeEager(Mode mode, int inner) {
		var prepared = _cache.Prepare<int, PqItem, (Mode mode, int inner)>()
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, 2).Match(static a => a.inner, n => n
					.Case(0, c => c.UseIndex(_byBucket, 0))
					.Case(1, c => c.Where(static v => v.Flag))
					.Default()))
				.Case(Mode.ByBucket, b => b.UseIndex(_byBucket, 3))
				.Default())
			.Where(static v => v.Id < 200)
			.Build();
		var args = (mode, inner);

		EagerItems Eager() {
			var q = _cache.Query();
			switch (mode) {
				case Mode.ByGroup:
					q = q.UseIndex(_byGroup, 2);
					switch (inner) {
						case 0: q = q.UseIndex(_byBucket, 0); break;
						case 1: q = q.Where(static v => v.Flag); break;
					}

					break;
				case Mode.ByBucket:
					q = q.UseIndex(_byBucket, 3);
					break;
			}

			return q.Where(static v => v.Id < 200);
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	[TestCase(true, Mode.ByGroup)]
	[TestCase(true, Mode.ByBucket)]
	[TestCase(false, Mode.ByGroup)]
	public void Match_InsideIf_LikeEager(bool cond, Mode mode) {
		var prepared = _cache.Prepare<int, PqItem, (bool cond, Mode mode)>()
			.UseIndex(_codeRange, static rb => rb.Lt(1200))
			.If(static a => a.cond, b => b.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, c => c.UseIndex(_byGroup, 1))
				.Case(Mode.ByBucket, c => c.UseIndex(_byBucket, 1).Where(static v => !v.Flag))
				.Default()))
			.Build();
		var args = (cond, mode);

		EagerItems Eager() {
			var q = _cache.Query().UseIndex(_codeRange, static rb => rb.Lt(1200));
			if (cond) {
				switch (mode) {
					case Mode.ByGroup: q = q.UseIndex(_byGroup, 1); break;
					case Mode.ByBucket: q = q.UseIndex(_byBucket, 1).Where(static v => !v.Flag); break;
				}
			}

			return q;
		}

		AssertParity(Eager, () => prepared.Execute(args), () => prepared.Count(args));
	}

	// Match inside an Or branch (narrow-only arms). The eager branch builder is type-preserving, so the
	// eager twin is a switch expression over the branch; an unmatched tag is the eager no-op branch.
	[TestCase(Mode.ByGroup)]
	[TestCase(Mode.ByBucket)]
	[TestCase(Mode.Unhandled)]
	public void Match_InsideOrBranch_UseIndexOnlyArms_LikeEager(Mode mode) {
		var prepared = _cache.Prepare<int, PqItem, Mode>()
			.UseIndex(_byGroup, 3)
			.Or(b => b.UseIndex(_byBucket, 1), b => b.Match(static m => m, m => m
				.Case(Mode.ByGroup, c => c.UseIndex(_byBucket, 4))
				.Case(Mode.ByBucket, c => c.UseIndex(_byBucket, 0))
				.Default()))
			.Build();

		EagerItems Eager()
			=> _cache.Query().UseIndex(_byGroup, 3).Or(b => b.UseIndex(_byBucket, 1), b => mode switch {
				Mode.ByGroup => b.UseIndex(_byBucket, 4),
				Mode.ByBucket => b.UseIndex(_byBucket, 0),
				_ => b,
			});

		AssertParity(Eager, () => prepared.Execute(mode), () => prepared.Count(mode));

		using var rows = prepared.Execute(mode);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group, Is.EqualTo(3));
			var bucket = rows[i].Id % 5;
			Assert.That(mode switch { Mode.ByGroup => bucket is 1 or 4, Mode.ByBucket => bucket is 1 or 0, _ => bucket == 1 });
		}
	}

	// ── Joined and sorted (the Match sits before the resolver) ────────────────────

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	public void Match_BeforeJoinOne_AndBeforeInnerJoinOne_LikeEager(int tag) {
		// Customer 8 does not exist: the unmatched tag leaves product-2 orders of customer 8 joining to null.
		var outer = _orders.Prepare<int, PqOrder, (int tag, int customer)>()
			.UseIndex(_byProduct, 2)
			.Match(static a => a.tag, m => m
				.Case(0, b => b.UseIndex(_byCustomer, static a => a.customer))
				.Case(1, b => b.UseIndex(_byCustomer, 8))
				.Default())
			.JoinOne(_byCustomer, _customers)
			.Build();
		var inner = _orders.Prepare<int, PqOrder, (int tag, int customer)>()
			.UseIndex(_byProduct, 2)
			.Match(static a => a.tag, m => m
				.Case(0, b => b.UseIndex(_byCustomer, static a => a.customer))
				.Case(1, b => b.UseIndex(_byCustomer, 8))
				.Default())
			.InnerJoinOne(_byCustomer, _customers)
			.Build();
		var args = (tag, customer: 2);

		EagerOrders Eager() {
			var q = _orders.Query().UseIndex(_byProduct, 2);
			switch (tag) {
				case 0: q = q.UseIndex(_byCustomer, args.customer); break;
				case 1: q = q.UseIndex(_byCustomer, 8); break;
			}

			return q;
		}

		AssertSameJoined(Eager().JoinOne(_byCustomer, _customers).Execute(), outer.Execute(args), Customer);
		Assert.That(outer.Count(args), Is.EqualTo(Eager().JoinOne(_byCustomer, _customers).Count()));
		AssertSameJoined(Eager().InnerJoinOne(_byCustomer, _customers).Execute(), inner.Execute(args), Customer);
		Assert.That(inner.Count(args), Is.EqualTo(Eager().InnerJoinOne(_byCustomer, _customers).Count()));

		using var rows = outer.Execute(args);
		var nulls = 0;
		for (var i = 0; i < rows.Count; i++)
			if (rows[i].Right is null) nulls++;
		Assert.That(nulls, tag switch { 0 => Is.EqualTo(0), 1 => Is.EqualTo(rows.Count), _ => Is.GreaterThan(0) });
		Assert.That(inner.Count(args), tag == 1 ? Is.EqualTo(0) : Is.GreaterThan(0));
	}

	[TestCase(Mode.ByGroup)]
	[TestCase(Mode.ByBucket)]
	[TestCase(Mode.Unhandled)]
	public void Match_BeforeSortBounded_TiedPages_LikeEager_AndPagesConcatenate(Mode mode) {
		var prepared = _cache.Prepare<int, PqItem, (Mode mode, int g, int bucket)>()
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(Mode.ByBucket, b => b.UseIndex(_byBucket, static a => a.bucket))
				.Default())
			.SortBounded(new ByGroup())
			.Build();
		var args = (mode, g: 4, bucket: 2);

		EagerItems Eager() {
			var q = _cache.Query();
			switch (mode) {
				case Mode.ByGroup: q = q.UseIndex(_byGroup, args.g); break;
				case Mode.ByBucket: q = q.UseIndex(_byBucket, args.bucket); break;
			}

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
		var prepared = ThreeArmsWithDefault();
		(Mode mode, int g, int bucket, int code, int min) Args(Mode mode) => (mode, 6, 1, 1020, 0);

		foreach (var mode in new[] { Mode.ByGroup, Mode.ByBucket, Mode.ByCode, Mode.Unhandled })
			AssertSame(EagerThreeArmsWithDefault(Args(mode)).Execute(), prepared.Execute(Args(mode)));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6, Flag = true });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved from group 6 to group 1, still code 1020
		_cache.AddOrUpdate(8, new PqItem { Id = 8, Code = 7000, Group = 3, Flag = false });

		foreach (var mode in new[] { Mode.ByGroup, Mode.ByBucket, Mode.ByCode, Mode.Unhandled }) {
			AssertSame(EagerThreeArmsWithDefault(Args(mode)).Execute(), prepared.Execute(Args(mode)));
			Assert.That(prepared.Count(Args(mode)), Is.EqualTo(EagerThreeArmsWithDefault(Args(mode)).Count()));
		}
	}

	[Test]
	public void PooledMatch_EveryArm_LeavesNoRentedArrays() {
		var prepared = _cache.Prepare<int, PqItem, (Mode mode, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g).Or(x => x.UseIndex(_byBucket, 2), x => x.Match(static a => a.g, n => n.Case(4, i => i.UseIndex(_byBucket, 4)).Default())))
				.Case(Mode.ByBucket, b => b.If(static a => a.g > 3, i => i.UseIndex(_byGroup, 1)))
				.Default(b => b.Where(static (v, in a) => v.Id >= a.g)))
			.Where(static (v, in a) => v.Id >= a.g)
			.Build();

		LeakAssert.Balanced(() => {
			prepared.ExecutePooled((Mode.ByGroup, 1)).Dispose();
			prepared.ExecutePooled((Mode.ByGroup, 4)).Dispose();
			prepared.ExecutePooled((Mode.ByBucket, 5)).Dispose();
			prepared.ExecutePooled((Mode.ByBucket, 1)).Dispose();
			prepared.ExecutePooled((Mode.Unhandled, 100)).Dispose();
			prepared.ExecutePooledCloned((Mode.ByGroup, 4), 2, 5).Dispose();
			_ = prepared.Count((Mode.ByGroup, 4));
			_ = prepared.Count((Mode.Unhandled, 4));
		});
	}

	// A throwing tag selector and a throwing selector inside the selected arm both propagate, strand no
	// rented array (the replay's finally releases the outer candidates), and leave the command usable.
	[Test]
	public void ThrowingTagSelector_AndThrowingArmSelector_Propagate_LeaveNoRentedArrays_AndCommandStaysUsable() {
		var selector = _cache.Prepare<int, PqItem, (int tag, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(static a => a.tag < 0 ? throw new InvalidOperationException("tag") : a.tag, m => m.Case(1, b => b.UseIndex(_byGroup, static a => a.g)).Default())
			.Build();
		var arm = _cache.Prepare<int, PqItem, (int tag, int g)>()
			.UseIndex(_byBucket, 2)
			.Match(static a => a.tag, m => m.Case(1, b => b.UseIndex(_byGroup, static a => a.g < 0 ? throw new InvalidOperationException("arm") : a.g)).Default())
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((-1, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.Count((-1, 1)));
			Assert.Throws<InvalidOperationException>(() => arm.ExecutePooled((1, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => arm.Count((1, -1)));
		});

		// An unselected arm never runs its selector, so a would-throw selector is harmless when not selected.
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).Execute(), arm.Execute((0, -1)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), selector.Execute((1, 4)));
		AssertSame(_cache.Query().UseIndex(_byBucket, 2).UseIndex(_byGroup, 4).Execute(), arm.Execute((1, 4)));
	}

	[Test]
	public void ConcurrentExecutions_OfOneCommand_EachThreadDispatchesDifferently_RowsSatisfyTheirArm() {
		var prepared = _cache.Prepare<int, PqItem, (Mode mode, int g)>()
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(Mode.ByBucket, b => b.UseIndex(_byBucket, static a => a.g % 5))
				.Default(b => b.Where(static v => v.Flag)))
			.Where(static v => v.Id < 200)
			.Build();

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (mode: (Mode)((seed + i) % 4), g: (seed + i) % 7);
					using var rows = prepared.ExecutePooled(args);
					Assert.That(rows.Count, Is.EqualTo(Expected(args.mode, args.g)));
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Id, Is.LessThan(200));
						switch (args.mode) {
							case Mode.ByGroup: Assert.That(rows[r].Group, Is.EqualTo(args.g)); break;
							case Mode.ByBucket: Assert.That(rows[r].Id % 5, Is.EqualTo(args.g % 5)); break;
							default: Assert.That(rows[r].Flag); break;
						}
					}
				}
			});
		}

		Task.WaitAll(readers);

		static int Expected(Mode mode, int g) {
			var n = 0;
			for (var i = 0; i < 200; i++)
				if (mode switch { Mode.ByGroup => i % 7 == g, Mode.ByBucket => i % 5 == g % 5, _ => i % 3 == 0 }) n++;
			return n;
		}
	}

	// ── Frozen ────────────────────────────────────────────────────────────────────

	[TestCase(Mode.ByGroup)]
	[TestCase(Mode.ByBucket)]
	[TestCase(Mode.ByCode)]
	[TestCase(Mode.Unhandled)]
	public void Frozen_Match_PipelinesAndExplainsTheArms_FrozenEqualsPreparedEqualsEager(Mode mode) {
		var frozen = _cache.Prepare<int, PqItem, (Mode mode, int g, int bucket, int code, int min)>()
			.Match(static a => a.mode, m => m
				.Case(Mode.ByGroup, b => b.UseIndex(_byGroup, static a => a.g))
				.Case(Mode.ByBucket, b => b.UseIndex(_byBucket, static a => a.bucket).Where(static (v, in a) => v.Id >= a.min))
				.Case(Mode.ByCode, b => b.UseIndex(_byCode, static a => a.code))
				.Default(b => b.Where(static v => v.Flag)))
			.BuildFrozen();
		var prepared = ThreeArmsWithDefault();
		var args = (mode, g: 3, bucket: 2, code: 1042, min: 100);

		AssertSame(EagerThreeArmsWithDefault(args).Execute(), frozen.Execute(args));
		AssertSame(prepared.Execute(args), frozen.Execute(args));
		Assert.That(frozen.Count(args), Is.EqualTo(prepared.Count(args)));

		var plan = frozen.Plan;
		var match = plan.Narrowers[0];
		Assert.Multiple(() => {
			Assert.That(plan.Executor, Is.EqualTo("Pipeline"), "step 4: the arm is chosen at bind");
			Assert.That(plan.Narrowers, Has.Count.EqualTo(1));
			Assert.That(match.Kind, Is.EqualTo(NarrowerKind.Match));
			Assert.That(match.IsParameterized, Is.True);
			Assert.That(match.Selector, Is.InstanceOf<Func<(Mode mode, int g, int bucket, int code, int min), Mode>>());
			Assert.That(match.Children, Has.Count.EqualTo(4), "three arms and the default");
			Assert.That(match.Children[0][0].Kind, Is.EqualTo(NarrowerKind.ListEq));
			Assert.That(match.Children[1].Select(static d => d.Kind), Is.EqualTo(new[] { NarrowerKind.ListEq, NarrowerKind.FilterArg }).AsCollection);
			Assert.That(match.Children[2][0].Kind, Is.EqualTo(NarrowerKind.UniqueEq));
			Assert.That(match.Children[3][0].Kind, Is.EqualTo(NarrowerKind.Filter));
			var tags = (MatchArmTags)match.Value!;
			Assert.That(tags.Tags, Is.EqualTo(new object[] { Mode.ByGroup, Mode.ByBucket, Mode.ByCode }).AsCollection);
			Assert.That(tags.ToString(), Is.EqualTo("[ByGroup, ByBucket, ByCode, default]"));
		});

		var text = frozen.Explain();
		Assert.That(text, Does.Contain("executor: Pipeline").And.Contain("Match (arg)").And.Contain("case ByGroup:").And.Contain("case ByCode:").And.Contain("default:")
			.And.Contain("match#0 {case ByGroup: [step 0 ListEq]; case ByBucket: [step 1 ListEq, branch filter 0]; case ByCode: [step 2 UniqueEq]; default: [branch filter 1]}")
			.And.Contain(mode switch { Mode.ByGroup => "select#0 → arm 0", Mode.ByBucket => "select#0 → arm 1", Mode.ByCode => "select#0 → arm 2", _ => "select#0 → arm 3" }));
	}

	// The empty Default() is a real, described arm — an empty sub-chain the plan carries and explains,
	// not the absent one the old fall-through left implicit.
	[Test]
	public void Frozen_Match_EmptyDefault_DescribesAnEmptyDefaultArm() {
		var frozen = _cache.Prepare<int, PqItem, int>().Match(static t => t, m => m.Case(1, b => b.UseIndex(_byGroup, 1)).Default()).BuildFrozen();
		var tags = (MatchArmTags)frozen.Plan.Narrowers[0].Value!;
		Assert.Multiple(() => {
			Assert.That(tags.Tags, Is.EqualTo(new object[] { 1 }).AsCollection);
			Assert.That(tags.ToString(), Is.EqualTo("[1, default]"));
			Assert.That(frozen.Plan.Narrowers[0].Children, Has.Count.EqualTo(2), "the case and the empty default");
			Assert.That(frozen.Plan.Narrowers[0].Children[1], Is.Empty);
			Assert.That(frozen.Explain(), Does.Contain("case 1:").And.Contain("default:"));
		});

		AssertSame(_cache.Query().Execute(), frozen.Execute(99));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Execute(), frozen.Execute(1));
	}
}

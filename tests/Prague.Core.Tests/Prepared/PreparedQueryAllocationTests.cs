namespace Prague.Core.Tests.Prepared;

using Prague.Core;

// A prepared execution must cost no more bytes than the equivalent eager query: the replay rents
// the same candidate set the eager builder rents and the argument selector is a cached static
// delegate. Measured relative to the eager builder rather than as an absolute zero so the pin
// tracks whatever the shared execution core costs, and stays green if that floor moves.
[TestFixture]
[NonParallelizable]
public class PreparedQueryAllocationTests {
	private const int Iterations = 20_000;
	private const int N = 5_000;

	private InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem> _cache = null!;
	private InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PreparedQueryJoinDifferentialTests.PqOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PreparedQueryJoinDifferentialTests.PqLine, int> _lineByOrder = null!;
	private CacheUniqueIndex<int, PreparedQueryDifferentialTests.PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PreparedQueryDifferentialTests.PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PreparedQueryDifferentialTests.PqItem, int> _codeRange = null!;

	// Struct comparer: the sort plans carry it as a type parameter, so neither side boxes it.
	private readonly struct ByCode : IComparer<PreparedQueryDifferentialTests.PqItem> {
		public int Compare(PreparedQueryDifferentialTests.PqItem? x, PreparedQueryDifferentialTests.PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	private readonly struct ByQty : IComparer<PreparedQueryJoinDifferentialTests.PqOrder> {
		public int Compare(PreparedQueryJoinDifferentialTests.PqOrder? x, PreparedQueryJoinDifferentialTests.PqOrder? y) => (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
	}

	[OneTimeSetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, new PreparedQueryDifferentialTests.PqItem { Id = i, Code = 1000 + i, Group = i % 97, Flag = i % 3 == 0 });

		// Joined model: ~50 orders per customer, customers 0..39 exist, 3 lines per order.
		_orders = new InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_customers = new InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqCustomer>();
		_lines = new InMemoryDataCache<int, PreparedQueryJoinDifferentialTests.PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		for (var c = 0; c < 40; c++)
			_customers.AddOrUpdate(c, new PreparedQueryJoinDifferentialTests.PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < 2_000; i++) {
			_orders.AddOrUpdate(i, new PreparedQueryJoinDifferentialTests.PqOrder { Id = i, CustomerId = i % 40, ProductId = i % 6, Qty = i % 13 });
			for (var k = 0; k < 3; k++)
				_lines.AddOrUpdate(i * 3 + k, new PreparedQueryJoinDifferentialTests.PqLine { Id = i * 3 + k, OrderId = i });
		}
	}

	// Three measured windows, minimum taken. Debug builds allocate per call (closures capturing the
	// per-call argument, boxed comparers), so a window runs collections, and a gen2 collection lets
	// ArrayPool<T>.Shared trim its cached arrays — the next Rent then allocates a fresh array inside
	// the window. That is one-off, positive noise on whichever side it lands; the minimum over the
	// windows is the steady-state per-call cost either side actually has.
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

	// The sort shapes run long enough per call that tiered compilation of the freshly closed generic
	// instantiations lands inside a single Measure window (and, in Debug, the boxed comparer's garbage
	// forces collections mid-loop); a full discarded pass first lets both settle before the pin is taken.
	private static long MeasureSettled(Action body) {
		Measure(body);
		return Measure(body);
	}

	[Test]
	public void UniqueLookup_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>().UseIndex(_byCode, static c => c).Build();
		var code = 1042;

		var eager = Measure(() => _cache.Query().UseIndex(_byCode, code).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(code).Dispose());

		TestContext.Out.WriteLine($"unique: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void ListScan_PooledParameterizedWithWhere_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.Where(static v => v.Flag)
			.Build();
		var group = 13;

		var eager = Measure(() => _cache.Query().UseIndex(_byGroup, group).Where(static v => v.Flag).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(group).Dispose());

		TestContext.Out.WriteLine($"list+where: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
#if !DEBUG
		// A single Where is free on both sides: WhereInternal composes through a separate non-inlined
		// helper, so the first filter no longer pays for the `&&` closure's display class (32 B before).
		Assert.That(command, Is.EqualTo(0), "prepared list+where must allocate nothing in Release");
		Assert.That(eager, Is.EqualTo(0), "eager list+where must allocate nothing in Release");
#endif
	}

	// Parameterized Where. The eager twin is what a caller writes: a method taking the per-call
	// argument and a lambda capturing it — Roslyn hoists the capture into a display class allocated
	// per call and creates the delegate per call (88 B in Release). The prepared command binds the
	// argument into a per-thread pooled predicate box instead and a single filter costs the eager core
	// nothing, so the prepared side is pinned at an absolute zero-ish (< 8 B/op) in Release. Debug keeps
	// the relative pin against the constant-Where prepared command, whose per-call cost is Debug noise.
	[Test]
	public void ListScan_PooledParameterizedArgWhere_AllocatesLessThanEager_AndNothingAboveTheFilterFloor() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.Build();
		var floorPrepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 2_000)
			.Build();
		var args = (group: 13, min: 2_000);

		QueryResults<PreparedQueryDifferentialTests.PqItem> Eager((int group, int min) a)
			=> _cache.Query().UseIndex(_byGroup, a.group).Where(v => v.Id >= a.min).ExecutePooled();

		var eager = Measure(() => Eager(args).Dispose());
		var floor = Measure(() => floorPrepared.ExecutePooled(args.group).Dispose());
		var command = Measure(() => prepared.ExecutePooled(args).Dispose());

		TestContext.Out.WriteLine($"list+arg-where: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op (constant-where floor {(double)floor / Iterations:F1} B/op)");
		Assert.That(command, Is.LessThan(eager));
#if DEBUG
		Assert.That((double)(command - floor) / Iterations, Is.LessThan(8.0));
#else
		Assert.That((double)command / Iterations, Is.LessThan(8.0));
#endif
	}

	// Two parameterized Wheres: the eager core ANDs them through a closure in both paths, so the pin
	// is relative only.
	[Test]
	public void ListScan_PooledTwoParameterizedArgWheres_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (int group, int min, int max)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.Where(static (v, a) => v.Id <= a.max)
			.Build();
		var args = (group: 13, min: 1_000, max: 4_000);

		var eager = Measure(() => _cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).Where(v => v.Id <= args.max).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(args).Dispose());

		TestContext.Out.WriteLine($"list+2x arg-where: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void Count_Parameterized_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>().UseIndex(_byGroup, static g => g).Build();
		var group = 13;

		var eager = Measure(() => _cache.Query().UseIndex(_byGroup, group).Count());
		var command = Measure(() => prepared.Count(group));

		TestContext.Out.WriteLine($"count: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void Range_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (int lo, int hi)>()
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi))
			.Build();
		var args = (lo: 1500, hi: 1580);

		var eager = Measure(() => _cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), args).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(args).Dispose());

		TestContext.Out.WriteLine($"range: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void MultiValue_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, ReadOnlyMemory<int>>()
			.UseIndex(_byCode, static codes => codes)
			.Build();
		ReadOnlyMemory<int> codes = new[] { 1001, 1042, 1777, 2500, 5999 };

		var eager = Measure(() => _cache.Query().UseIndex(_byCode, codes.Span).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(codes).Dispose());

		TestContext.Out.WriteLine($"multi-value: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void ListMultiValue_PooledParameterizedWithRange_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (ReadOnlyMemory<int> groups, int lo, int hi)>()
			.UseIndex(_byGroup, static a => a.groups)
			.UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi))
			.Build();
		var args = (groups: (ReadOnlyMemory<int>)new[] { 3, 17, 41 }, lo: 1200, hi: 3200);

		var eager = Measure(() => _cache.Query().UseIndex(_byGroup, args.groups.Span).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), args).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(args).Dispose());

		TestContext.Out.WriteLine($"list-in+range: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void SortBounded_PooledStructComparerParameterizedList_SmallPage_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.SortBounded(new ByCode())
			.Build();
		var group = 13;

		var eager = MeasureSettled(() => _cache.Query().UseIndex(_byGroup, group).SortBounded(new ByCode()).ExecutePooled(3, 8).Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled(group, 3, 8).Dispose());

		TestContext.Out.WriteLine($"sort-bounded: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void Sort_PooledStructComparerParameterizedList_Unbounded_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.Sort(new ByCode())
			.Build();
		var group = 13;

		var eager = MeasureSettled(() => _cache.Query().UseIndex(_byGroup, group).Sort(new ByCode()).ExecutePooled().Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled(group).Dispose());

		TestContext.Out.WriteLine($"sort: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	// ── Joined ────────────────────────────────────────────────────────────────────

	[Test]
	public void JoinOne_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _orders.Prepare<int, PreparedQueryJoinDifferentialTests.PqOrder, int>()
			.UseIndex(_byCustomer, static c => c)
			.JoinOne(_byCustomer, _customers)
			.Build();
		var customer = 7;

		var eager = MeasureSettled(() => _orders.Query().UseIndex(_byCustomer, customer).JoinOne(_byCustomer, _customers).ExecutePooled().Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled(customer).Dispose());

		TestContext.Out.WriteLine($"join-one: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void JoinOneJoinMany_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _orders.Prepare<int, PreparedQueryJoinDifferentialTests.PqOrder, int>()
			.UseIndex(_byCustomer, static c => c)
			.JoinOne(_byCustomer, _customers)
			.JoinMany(_lines, _lineByOrder)
			.Build();
		var customer = 7;

		var eager = MeasureSettled(() => _orders.Query().UseIndex(_byCustomer, customer).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).ExecutePooled().Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled(customer).Dispose());

		TestContext.Out.WriteLine($"join-one+many: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void SortBoundedJoinOne_PooledStructComparerParameterized_SmallPage_AllocatesNoMoreThanEager() {
		var prepared = _orders.Prepare<int, PreparedQueryJoinDifferentialTests.PqOrder, int>()
			.UseIndex(_byCustomer, static c => c)
			.SortBounded(new ByQty())
			.JoinOne(_byCustomer, _customers)
			.Build();
		var customer = 7;

		var eager = MeasureSettled(() => _orders.Query().UseIndex(_byCustomer, customer).SortBounded(new ByQty()).JoinOne(_byCustomer, _customers).ExecutePooled(3, 8).Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled(customer, 3, 8).Dispose());

		TestContext.Out.WriteLine($"sort-bounded+join-one: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	// ── Or ────────────────────────────────────────────────────────────────────────

	// The eager twin uses the state-passing Or overload with static lambdas, the zero-allocation eager
	// spelling; the prepared branches read the same two groups from the execution arguments.
	[Test]
	public void Or_PooledParameterizedTwoListBranches_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (int g1, int g2)>()
			.Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2))
			.Build();
		var state = (idx: _byGroup, g1: 13, g2: 41);

		var eager = Measure(() => _cache.Query().Or(static (b, s) => b.UseIndex(s.idx, s.g1), static (b, s) => b.UseIndex(s.idx, s.g2), state).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled((state.g1, state.g2)).Dispose());

		TestContext.Out.WriteLine($"or: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void OrJoinOne_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _orders.Prepare<int, PreparedQueryJoinDifferentialTests.PqOrder, (int c1, int c2)>()
			.Or(b => b.UseIndex(_byCustomer, static a => a.c1), b => b.UseIndex(_byCustomer, static a => a.c2))
			.JoinOne(_byCustomer, _customers)
			.Build();
		var state = (idx: _byCustomer, c1: 7, c2: 21);

		var eager = MeasureSettled(() => _orders.Query().Or(static (b, s) => b.UseIndex(s.idx, s.c1), static (b, s) => b.UseIndex(s.idx, s.c2), state).JoinOne(_byCustomer, _customers).ExecutePooled().Dispose());
		var command = MeasureSettled(() => prepared.ExecutePooled((state.c1, state.c2)).Dispose());

		TestContext.Out.WriteLine($"or+join-one: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	// ── If / IfElse ───────────────────────────────────────────────────────────────

	// The eager twin is what a caller writes: a method taking the per-call arguments and a C# `if`
	// around a type-preserving UseIndex reassignment. Measured for both outcomes: the taken branch does
	// the eager work of the extra narrowing; the skipped one costs one delegate call.
	[Test]
	public void If_PooledParameterized_TakenAndSkipped_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (bool cond, int group, int code)>()
			.UseIndex(_byGroup, static a => a.group)
			.If(static a => a.cond, b => b.UseIndex(_byCode, static a => a.code))
			.Build();

		QueryResults<PreparedQueryDifferentialTests.PqItem> Eager((bool cond, int group, int code) a) {
			var q = _cache.Query().UseIndex(_byGroup, a.group);
			if (a.cond) q = q.UseIndex(_byCode, a.code);
			return q.ExecutePooled();
		}

		foreach (var cond in new[] { true, false }) {
			var args = (cond, group: 13, code: 1013 + 97 * 4);
			var eager = Measure(() => Eager(args).Dispose());
			var command = Measure(() => prepared.ExecutePooled(args).Dispose());

			TestContext.Out.WriteLine($"if ({(cond ? "taken" : "skipped")}): eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
			Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
		}
	}

	[Test]
	public void IfElse_PooledParameterized_BothOutcomes_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (bool cond, int group, int code)>()
			.IfElse(static a => a.cond, b => b.UseIndex(_byGroup, static a => a.group), b => b.UseIndex(_byCode, static a => a.code))
			.Build();

		QueryResults<PreparedQueryDifferentialTests.PqItem> Eager((bool cond, int group, int code) a) {
			var q = _cache.Query();
			if (a.cond) q = q.UseIndex(_byGroup, a.group);
			else q = q.UseIndex(_byCode, a.code);
			return q.ExecutePooled();
		}

		foreach (var cond in new[] { true, false }) {
			var args = (cond, group: 13, code: 1042);
			var eager = Measure(() => Eager(args).Dispose());
			var command = Measure(() => prepared.ExecutePooled(args).Dispose());

			TestContext.Out.WriteLine($"if-else ({(cond ? "then" : "else")}): eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
			Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
		}
	}

	// Parameterized Where inside the branch: the eager twin's capturing lambda allocates a display
	// class and delegate per call when taken; the prepared side binds the arguments into the pooled
	// predicate box, so it stays at or below the eager cost for both outcomes.
	[Test]
	public void If_PooledParameterizedWhereInsideTheBranch_TakenAndSkipped_AllocatesNoMoreThanEager() {
		var prepared = _cache.Prepare<int, PreparedQueryDifferentialTests.PqItem, (bool cond, int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.If(static a => a.cond, b => b.Where(static (v, a) => v.Id >= a.min))
			.Build();

		QueryResults<PreparedQueryDifferentialTests.PqItem> Eager((bool cond, int group, int min) a) {
			var q = _cache.Query().UseIndex(_byGroup, a.group);
			if (a.cond) q = q.Where(v => v.Id >= a.min);
			return q.ExecutePooled();
		}

		foreach (var cond in new[] { true, false }) {
			var args = (cond, group: 13, min: 2_000);
			var eager = Measure(() => Eager(args).Dispose());
			var command = Measure(() => prepared.ExecutePooled(args).Dispose());

			TestContext.Out.WriteLine($"if+arg-where ({(cond ? "taken" : "skipped")}): eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
			Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
		}
	}
}

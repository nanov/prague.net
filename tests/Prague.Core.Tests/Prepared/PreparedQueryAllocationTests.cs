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

	// Three measured windows, minimum taken. Debug builds allocate per call (the eager builder's
	// closure composition, boxed comparers), so a window runs collections, and a gen2 collection lets
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
}

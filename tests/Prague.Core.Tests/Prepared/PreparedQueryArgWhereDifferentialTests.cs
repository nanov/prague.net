namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// Parameterized Where: a Func<TValue, TArgs, bool> bound to the execution's args through a
// per-thread pooled predicate box (ArgPredicatePool). Every shape is compared against the eager
// builder given a capturing lambda over the same value; the pool's stack discipline is then pinned
// directly — concurrency, re-entrancy, hygiene on pop, reset on throw.
[TestFixture]
public class PreparedQueryArgWhereDifferentialTests {
	private const int N = 240;
	private const int Customers = 10;
	private const int Products = 6;

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	// Same-TArgs re-entrancy context: a struct that carries the inner command it hands its own type to.
	private readonly record struct Ctx(PreparedQuery<Ctx, PqItem>? Inner, int Group, int Min, int Threshold);

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqInvoice> _invoices = null!;

	private static readonly int[] Mins = [0, 77, 150, 239, 1000];
	private static readonly (int skip, int take)[] Pages = [(0, 5), (10, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue)];

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 });

		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_invoices = new InMemoryDataCache<int, PqInvoice>();
		for (var c = 0; c < Customers - 2; c++)
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, new PqOrder { Id = i, CustomerId = i % Customers, ProductId = i % Products, Qty = i % 13 });
			if (i % 3 != 0)
				_invoices.AddOrUpdate(i, new PqInvoice { Id = i, Amount = 100 + i });
		}
	}

	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";
	private static string Invoice(JoinResult<PqOrder, PqInvoice?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	// ── Simple shapes ─────────────────────────────────────────────────────────────

	[Test]
	public void ArgWhere_Alone_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>().Where(static (v, min) => v.Id >= min).Build();
		foreach (var min in Mins) {
			AssertSame(_cache.Query().Where(v => v.Id >= min).Execute(), prepared.Execute(min));
			Assert.That(prepared.Count(min), Is.EqualTo(_cache.Query().Where(v => v.Id >= min).Count()));
		}
	}

	[Test]
	public void ArgWhere_AfterListIndex_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.Build();
		foreach (var min in Mins) {
			var args = (group: 3, min);
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).Execute(), prepared.Execute(args));
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).ExecutePooled(), prepared.ExecutePooled(args));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).Count()));
		}
	}

	[Test]
	public void ArgWhere_AfterRange_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1200))
			.Where(static (v, min) => v.Id >= min)
			.Build();
		foreach (var min in Mins) {
			AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1200)).Where(v => v.Id >= min).Execute(), prepared.Execute(min));
			Assert.That(prepared.Count(min), Is.EqualTo(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lt(1200)).Where(v => v.Id >= min).Count()));
		}
	}

	[Test]
	public void ArgWhere_BetweenConstantWheres_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.Where(static v => v.Flag)
			.Where(static (v, min) => v.Id >= min)
			.Where(static v => v.Group != 2)
			.Build();
		foreach (var min in Mins) {
			AssertSame(_cache.Query().Where(static v => v.Flag).Where(v => v.Id >= min).Where(static v => v.Group != 2).Execute(), prepared.Execute(min));
			Assert.That(prepared.Count(min), Is.EqualTo(_cache.Query().Where(static v => v.Flag).Where(v => v.Id >= min).Where(static v => v.Group != 2).Count()));
		}
	}

	[Test]
	public void TwoArgWheres_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int min, int max)>()
			.UseIndex(_byGroup, 4)
			.Where(static (v, a) => v.Id >= a.min)
			.Where(static (v, a) => v.Id <= a.max)
			.Build();
		foreach (var (min, max) in new[] { (0, 239), (50, 100), (150, 149), (77, 77) }) {
			var args = (min, max);
			AssertSame(_cache.Query().UseIndex(_byGroup, 4).Where(v => v.Id >= args.min).Where(v => v.Id <= args.max).Execute(), prepared.Execute(args));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 4).Where(v => v.Id >= args.min).Where(v => v.Id <= args.max).Count()));
		}
	}

	// Overload resolution: lambda arity decides. A two-arg lambda on a NoArgs builder is legal.
	[Test]
	public void Overloads_ArityDecides_AndNoArgsAdmitsTwoArgLambda() {
		var bound = _cache.Prepare().Where(static v => v.Flag).Build();
		var twoArg = _cache.Prepare().Where(static (v, _) => v.Flag).Build();
		var parameterized = _cache.Prepare<int, PqItem, int>().Where(static v => v.Flag).Where(static (v, min) => v.Id >= min).Build();
		AssertSame(_cache.Query().Where(static v => v.Flag).Execute(), bound.Execute());
		AssertSame(_cache.Query().Where(static v => v.Flag).Execute(), twoArg.Execute());
		AssertSame(_cache.Query().Where(static v => v.Flag && v.Id >= 100).Execute(), parameterized.Execute(100));
	}

	// ── Joined ───────────────────────────────────────────────────────────────────

	[Test]
	public void ArgWhere_BeforeOuterJoinOne_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, (int product, int minQty)>()
			.UseIndex(_byProduct, static a => a.product)
			.Where(static (o, a) => o.Qty >= a.minQty)
			.JoinOne(_byCustomer, _customers)
			.Build();
		foreach (var minQty in new[] { 0, 5, 12, 13 }) {
			var args = (product: 2, minQty);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, args.product).Where(o => o.Qty >= args.minQty).JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(args), Customer);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, args.product).Where(o => o.Qty >= args.minQty).JoinOne(_byCustomer, _customers).ExecutePooled(), prepared.ExecutePooled(args), Customer);
			Assert.That(prepared.Count(args), Is.EqualTo(_orders.Query().UseIndex(_byProduct, args.product).Where(o => o.Qty >= args.minQty).JoinOne(_byCustomer, _customers).Count()));
		}
	}

	// PK-to-PK inner: PrepareIndexedInner calls GetCandidates -> FilterSeededCandidates with the
	// pooled predicate, before base execution applies it again. The box must still be bound for both.
	[Test]
	public void ArgWhere_BeforeInnerJoinOne_PkToPk_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, (int customer, int minQty)>()
			.UseIndex(_byCustomer, static a => a.customer)
			.Where(static (o, a) => o.Qty >= a.minQty)
			.InnerJoinOne(_invoices)
			.Build();
		for (var customer = 0; customer < Customers; customer++)
		foreach (var minQty in new[] { 0, 6, 12 }) {
			var args = (customer, minQty);
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, args.customer).Where(o => o.Qty >= args.minQty).InnerJoinOne(_invoices).Execute(), prepared.Execute(args), Invoice);
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, args.customer).Where(o => o.Qty >= args.minQty).InnerJoinOne(_invoices).ExecutePooled(), prepared.ExecutePooled(args), Invoice);
			Assert.That(prepared.Count(args), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, args.customer).Where(o => o.Qty >= args.minQty).InnerJoinOne(_invoices).Count()));
		}
	}

	[Test]
	public void ArgWhere_NoIndex_BeforeInnerJoinOne_LeftSym_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>()
			.Where(static (o, minQty) => o.Qty >= minQty)
			.InnerJoinOne(_byCustomer, _customers)
			.Build();
		foreach (var minQty in new[] { 0, 7, 12 }) {
			AssertSameJoined(_orders.Query().Where(o => o.Qty >= minQty).InnerJoinOne(_byCustomer, _customers).Execute(), prepared.Execute(minQty), Customer);
			Assert.That(prepared.Count(minQty), Is.EqualTo(_orders.Query().Where(o => o.Qty >= minQty).InnerJoinOne(_byCustomer, _customers).Count()));
		}
	}

	// ── Sorted ───────────────────────────────────────────────────────────────────

	[Test]
	public void ArgWhere_ThenSortBounded_EveryPage_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.SortBounded(new ByCode())
			.Build();
		foreach (var min in Mins)
		foreach (var (skip, take) in Pages) {
			var args = (group: 5, min);
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).SortBounded(new ByCode()).Execute(skip, take), prepared.Execute(args, skip, take));
			AssertSame(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).SortBounded(new ByCode()).ExecutePooled(skip, take), prepared.ExecutePooled(args, skip, take));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byGroup, args.group).Where(v => v.Id >= args.min).SortBounded(new ByCode()).Count()));
		}
	}

	// ── Pool discipline ──────────────────────────────────────────────────────────

	// Eight threads, each with its own arg, on one command: a shared (non-thread-local) box would
	// let one thread's args leak into another's rows.
	[Test]
	public void ConcurrentExecutions_WithDifferentArgs_EachSeeOwnPredicate() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int min)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static (v, a) => v.Id >= a.min)
			.Build();

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var args = (group: (seed + i) % 7, min: seed * 30 + i % 5);
					using var rows = prepared.ExecutePooled(args);
					var expected = 0;
					for (var id = args.group; id < N; id += 7)
						if (id >= args.min) expected++;
					Assert.That(rows.Count, Is.EqualTo(expected));
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Group, Is.EqualTo(args.group));
						Assert.That(rows[r].Id, Is.GreaterThanOrEqualTo(args.min));
					}
				}

				Assert.That(ArgPredicatePool<PqItem, (int group, int min)>.DepthForTests, Is.Zero);
			});
		}

		Task.WaitAll(readers);
	}

	// Different TArgs: the inner command has its own pool, so the outer frame is untouched by design;
	// both depths must still return to zero.
	[Test]
	public void Reentrant_InnerCommandWithDifferentArgs_LikeEager_AndDepthReturnsToZero() {
		var inner = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.Where(static (v, g) => v.Id >= g * 10)
			.Build();
		var outer = _cache.Prepare<int, PqItem, (PreparedQuery<int, PqItem> inner, int threshold)>()
			.Where(static (v, a) => v.Flag && a.inner.Count(v.Group) > a.threshold)
			.Build();

		for (var g = 0; g < 7; g++)
			AssertSame(_cache.Query().UseIndex(_byGroup, g).Where(v => v.Id >= g * 10).Execute(), inner.Execute(g));

		foreach (var threshold in new[] { 0, 30, 33, 40 }) {
			var eager = _cache.Query()
				.Where(v => v.Flag && _cache.Query().UseIndex(_byGroup, v.Group).Where(x => x.Id >= v.Group * 10).Count() > threshold)
				.Execute();
			AssertSame(eager, outer.Execute((inner, threshold)));
			Assert.That(outer.Count((inner, threshold)), Is.EqualTo(_cache.Query().Where(v => v.Flag && _cache.Query().UseIndex(_byGroup, v.Group).Where(x => x.Id >= v.Group * 10).Count() > threshold).Count()));
		}

		Assert.That(ArgPredicatePool<PqItem, int>.DepthForTests, Is.Zero);
		Assert.That(ArgPredicatePool<PqItem, (PreparedQuery<int, PqItem> inner, int threshold)>.DepthForTests, Is.Zero);
	}

	// Same TArgs: inner and outer share one pool. The inner execution pushes above the outer's box
	// and pops back to it; the outer box must stay bound to the outer args throughout.
	[Test]
	public void Reentrant_InnerCommandWithSameArgs_LikeEager_AndDepthReturnsToZero() {
		var inner = _cache.Prepare<int, PqItem, Ctx>()
			.UseIndex(_byGroup, static c => c.Group)
			.Where(static (v, c) => v.Id >= c.Min)
			.Build();
		var outer = _cache.Prepare<int, PqItem, Ctx>()
			.Where(static (v, c) => v.Id >= c.Min && c.Inner!.Count(c with { Group = v.Group }) > c.Threshold)
			.Build();

		foreach (var (min, threshold) in new[] { (0, 30), (50, 25), (100, 20), (200, 3) }) {
			var ctx = new Ctx(inner, 0, min, threshold);
			var eager = _cache.Query()
				.Where(v => v.Id >= min && _cache.Query().UseIndex(_byGroup, v.Group).Where(x => x.Id >= min).Count() > threshold)
				.Execute();
			AssertSame(eager, outer.Execute(ctx));
			Assert.That(outer.Count(ctx), Is.EqualTo(_cache.Query().Where(v => v.Id >= min && _cache.Query().UseIndex(_byGroup, v.Group).Where(x => x.Id >= min).Count() > threshold).Count()));
			Assert.That(ArgPredicatePool<PqItem, Ctx>.DepthForTests, Is.Zero);
		}
	}

	// A popped box must not keep the execution's args alive: TArgs holds a string here.
	[Test]
	public void AfterExecution_PoppedBoxIsCleared() {
		var prepared = _cache.Prepare<int, PqItem, (string tag, int min)>()
			.Where(static (v, a) => a.tag.Length > 0 && v.Id >= a.min)
			.Build();
		var args = (tag: "hello", min: 100);

		AssertSame(_cache.Query().Where(v => v.Id >= args.min).Execute(), prepared.Execute(args));

		var box = ArgPredicatePool<PqItem, (string tag, int min)>.BoxForTests(0);
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.FuncForTests, Is.Null);
		Assert.That(box.ArgsForTests, Is.EqualTo(default((string tag, int min))));
		Assert.That(ArgPredicatePool<PqItem, (string tag, int min)>.DepthForTests, Is.Zero);
	}

	[Test]
	public void ThrowingArgPredicate_Propagates_LeavesNoRentedArrays_ResetsDepth_AndCommandStaysUsable() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, 1)
			.Where(static (v, poison) => v.Id == poison ? throw new InvalidOperationException("boom") : v.Id > 10)
			.Build();
		var mark = ArgPredicatePool<PqItem, int>.DepthForTests;

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => prepared.ExecutePooled(22).Dispose());
			Assert.Throws<InvalidOperationException>(() => prepared.Count(22));
			Assert.That(ArgPredicatePool<PqItem, int>.DepthForTests, Is.EqualTo(mark));
		});

		Assert.That(ArgPredicatePool<PqItem, int>.DepthForTests, Is.EqualTo(mark));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Where(static v => v.Id > 10).Execute(), prepared.Execute(-1));
	}
}

namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using Prague.Core.TypeSystem;
using static PreparedQueryJoinDifferentialTests;

// BuildFrozen() stage 3, step 5: JoinOne fusion (design §7.1, §7.3). A joined plan whose narrowing is a
// pipeline plan and whose joins are fusable JoinOnes — the four families (PK to PK, right-unique,
// left-unique, left-symmetric), identity or key selector, outer or inner, chained, with or without one
// classic Sort / SortBounded before or after them — takes the joined pipeline: the pass fills every fused
// right slot per row right after the row is formed (one point lookup, no pair set, no paired read), an
// inner miss drops the row (not emitted, not counted — the eager CountCoreJoined narrowing), a
// SortBounded innermost keeps the step-6 bounded flow with the per-left lookup over the page rows only.
// Pinned here: (a) eager == prepared == frozen on rows and every joined slot across the families ×
// outer / inner × identity / selector × unsorted / Sort / SortBounded × the four Execute variants × pages
// incl. take = int.MaxValue, with clone identity of both sides; (b) missing rights: outer → null slot,
// inner → row dropped and Count matches eager; (c) chained fused pairs and triples; (d) Sort after a fused
// join over a joined field; (e) executor selection and Explain incl. the replay fallbacks (a filtered
// JoinOne; a JoinMany is admitted since step 8, see FrozenPipelineJoinManyTests) and the mixed step-6 shape (a fused and an unfused outer join after a SortBounded);
// (f) rented arrays balanced under a throwing selector / predicate / comparer / Clone() on the fused paths,
// eager twins included; (g) 8 readers against a writer churning the rights. An INNER left-symmetric join is
// the one shape that does not fuse by default: its eager fan-out creates the rows grouped by right key,
// which a per-left lookup cannot reproduce, so such a chain replays and stays byte-identical unless the
// caller opts into FrozenOptions.FuseSymmetricInnerJoins — both halves are pinned below.
// Model: 120 orders, CustomerId = Id % 10 (customers 0..7 exist), ProductId = Id % 6 (products 0..4),
// Qty = Id % 13; invoices keyed by order id for two orders in three; shipments keyed 5000 + id for four
// orders in five; customer codes keyed 100 + customer for customers 0..7 except 2.
[TestFixture]
public class FrozenPipelineJoinTests {
	internal sealed class PqShipment : ICacheEquatable<PqShipment>, ICacheClonable<PqShipment> {
		public int Id { get; init; }
		public int OrderId { get; init; }
		public string Carrier { get; init; } = "";

		public bool CacheEquals(PqShipment? other) => other is not null && other.Id == Id && other.OrderId == OrderId && other.Carrier == Carrier;

		public int CacheGetHashCode() => HashCode.Combine(Id, OrderId, Carrier);

		public PqShipment Clone() => new() { Id = Id, OrderId = OrderId, Carrier = Carrier };
	}

	private const int N = 120;
	private const int Customers = 10;
	private const int Products = 6;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];

	// A small page, a page inside a ~20-row product, skip beyond the end, unbounded (the classic container), unbounded with a skip, an empty page, one row, everything but two.
	private static readonly (int skip, int take)[] Pages = [(0, 5), (3, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue), (0, 0), (0, 1), (2, 100)];
	private static readonly FrozenOptions NoPipeline = new() { Pipeline = false };
	private static readonly FrozenOptions FuseSym = new() { FuseSymmetricInnerJoins = true };

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private CacheKeyValueListIndex<int, PqOrder, int> _byQty = null!;
	private CacheSymmetricUniqueIndex<int, PqOrder, int> _orderShipKey = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqCustomer> _customerCodes = null!;
	private CacheUniqueIndex<int, PqCustomer, int> _codeByCustomer = null!;
	private InMemoryDataCache<int, PqProduct> _products = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByOrder = null!;
	private InMemoryDataCache<int, PqInvoice> _invoices = null!;
	private InMemoryDataCache<int, PqShipment> _shipments = null!;
	private CacheUniqueIndex<int, PqShipment, int> _shipmentByOrder = null!;
	private CacheUniqueIndex<int, PqShipment, int> _shipmentByCode = null!;

	// Total orders over the left value (the bounded plan can drive them) and one with ties.
	private readonly struct ByQtyThenId : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) {
			var c = (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
			return c != 0 ? c : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
		}
	}

	private readonly struct ByCustomer : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) => (x?.CustomerId ?? 0).CompareTo(y?.CustomerId ?? 0);
	}

	private sealed class ByRegionThenIdDescOverInvoice : IComparer<JoinResult<PqOrder, PqInvoice?>> {
		public int Compare(JoinResult<PqOrder, PqInvoice?> x, JoinResult<PqOrder, PqInvoice?> y) => y.Left.Id.CompareTo(x.Left.Id);
	}

	// Total orders over the joined row: a post-join sorter over a joined field.
	private sealed class ByRegionThenIdDesc : IComparer<JoinResult<PqOrder, PqCustomer?>> {
		public int Compare(JoinResult<PqOrder, PqCustomer?> x, JoinResult<PqOrder, PqCustomer?> y) {
			var c = string.CompareOrdinal(x.Right?.Region ?? "", y.Right?.Region ?? "");
			return c != 0 ? c : y.Left.Id.CompareTo(x.Left.Id);
		}
	}

	private readonly struct ByShipmentCarrierThenId : IComparer<JoinResult<PqOrder, PqShipment?>> {
		public int Compare(JoinResult<PqOrder, PqShipment?> x, JoinResult<PqOrder, PqShipment?> y) {
			var c = string.CompareOrdinal(x.Right?.Carrier ?? "", y.Right?.Carrier ?? "");
			return c != 0 ? c : x.Left.Id.CompareTo(y.Left.Id);
		}
	}

	[SetUp]
	public void SetUp() {
		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_byQty = _orders.CacheKeyValueListIndex<int>(static (_, v) => v.Qty);
		_orderShipKey = _orders.AddSymmetricKeyValueIndex<int>(static (_, v) => 5000 + v.Id);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_customerCodes = new InMemoryDataCache<int, PqCustomer>();
		_codeByCustomer = _customerCodes.AddKeyValueIndex<int>(static (_, c) => c.Id - 100);
		_products = new InMemoryDataCache<int, PqProduct>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		_invoices = new InMemoryDataCache<int, PqInvoice>();
		_shipments = new InMemoryDataCache<int, PqShipment>();
		_shipmentByOrder = _shipments.AddKeyValueIndex<int>(static (_, s) => s.OrderId);
		_shipmentByCode = _shipments.AddKeyValueIndex<int>(static (_, s) => 9000 + s.OrderId);

		for (var c = 0; c < Customers - 2; c++) {
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 3 == 0 ? "EU" : c % 3 == 1 ? "US" : "APAC" });
			if (c != 2)
				_customerCodes.AddOrUpdate(100 + c, new PqCustomer { Id = 100 + c, Region = c % 2 == 0 ? "EU" : "US" });
		}

		for (var p = 0; p < Products - 1; p++)
			_products.AddOrUpdate(p, new PqProduct { Id = p, Category = p % 2 == 0 ? "hard" : "soft" });
		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, MakeOrder(i));
			for (var k = 0; k < i % 4; k++)
				_lines.AddOrUpdate(1000 + i * 4 + k, new PqLine { Id = 1000 + i * 4 + k, OrderId = i });
			if (i % 3 != 0)
				_invoices.AddOrUpdate(i, new PqInvoice { Id = i, Amount = 100 + i });
			if (i % 5 != 0)
				_shipments.AddOrUpdate(5000 + i, new PqShipment { Id = 5000 + i, OrderId = i, Carrier = i % 2 == 0 ? "DHL" : "UPS" });
		}
	}

	private static PqOrder MakeOrder(int i) => new() { Id = i, CustomerId = i % Customers, ProductId = i % Products, Qty = i % 13 };

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static bool IsClone(Variant v) => v is Variant.ExecuteCloned or Variant.ExecutePooledCloned;

	private static QueryResults<T> Run<TArgs, T>(PreparedQuery<TArgs, T> q, in TArgs args, Variant v, int skip, int take) => v switch {
		Variant.Execute => q.Execute(in args, skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(in args, skip, take),
		Variant.ExecutePooled => q.ExecutePooled(in args, skip, take),
		_ => q.ExecutePooledCloned(in args, skip, take),
	};

	private static QueryResults<TResult> Eager<TDiscriminator, TExecutor, TKey, TValue, TChain, TResult>(in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TChain, TResult> q, Variant v, int skip, int take)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue> => v switch {
		Variant.Execute => q.Execute(skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
		Variant.ExecutePooled => q.ExecutePooled(skip, take),
		_ => q.ExecutePooledCloned(skip, take),
	};

	private static QueryResults<TResult> EagerSorted<TDiscriminator, TExecutor, TKey, TValue, TChain, TResult>(in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TChain, TResult> q, Variant v, int skip, int take)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue> => v switch {
		Variant.Execute => q.Execute(skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
		Variant.ExecutePooled => q.ExecutePooled(skip, take),
		_ => q.ExecutePooledCloned(skip, take),
	};

	private static string One<TRight>(JoinResult<PqOrder, TRight?> r, Func<TRight, int> id) where TRight : class => $"{r.Left.Id}|{(r.Right is null ? "-" : id(r.Right))}";
	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => One(r, static c => c.Id);
	private static string Invoice(JoinResult<PqOrder, PqInvoice?> r) => One(r, static c => c.Id);
	private static string Shipment(JoinResult<PqOrder, PqShipment?> r) => One(r, static c => c.Id);
	private static string CustomerProduct(JoinResult<PqOrder, PqCustomer?, PqProduct?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";
	private static string InvoiceCustomer(JoinResult<PqOrder, PqInvoice?, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";
	private static string CustomerInvoice(JoinResult<PqOrder, PqCustomer?, PqInvoice?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";
	private static string Three(JoinResult<PqOrder, PqCustomer?, PqInvoice?, PqShipment?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}|{(r.Right3 is null ? "-" : r.Right3.Id)}";
	private static string ThreeShip(JoinResult<PqOrder, PqShipment?, PqInvoice?, PqShipment?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}|{(r.Right3 is null ? "-" : r.Right3.Id)}";

	// The same rows regardless of order: an inner left-symmetric join's eager sequence is its fan-out grouping.
	private static void AssertSameJoinedSet<TResult>(QueryResults<TResult> eager, QueryResults<TResult> frozen, Func<TResult, string> row) {
		try {
			Assert.Multiple(() => {
				Assert.That(frozen.Count, Is.EqualTo(eager.Count), "Count");
				Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), "TotalCount");
				Assert.That(frozen.Truncated, Is.EqualTo(eager.Truncated), "Truncated");
			});
			var eagerRows = new string[eager.Count];
			var frozenRows = new string[frozen.Count];
			for (var i = 0; i < eager.Count; i++) eagerRows[i] = row(eager[i]);
			for (var i = 0; i < frozen.Count; i++) frozenRows[i] = row(frozen[i]);
			Array.Sort(eagerRows, StringComparer.Ordinal);
			Array.Sort(frozenRows, StringComparer.Ordinal);
			Assert.That(frozenRows, Is.EqualTo(eagerRows).AsCollection, "row set");
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>Clone identity of the left and the one right of every row.</summary>
	private void AssertIdentity<TRight>(QueryResults<JoinResult<PqOrder, TRight?>> rows, bool clone, InMemoryDataCache<int, TRight> rights, Func<TRight, int> key, string tag)
		where TRight : class, ICacheEquatable<TRight>, ICacheClonable<TRight> {
		try {
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(_orders.TryGet(rows[i].Left.Id, out var left), Is.True, tag);
				Assert.That(ReferenceEquals(rows[i].Left, left), Is.EqualTo(!clone), tag + " left identity");
				if (rows[i].Right is not { } right)
					continue;
				Assert.That(rights.TryGet(key(right), out var cached), Is.True, tag + " the right is a cached row");
				Assert.That(ReferenceEquals(right, cached), Is.EqualTo(!clone), tag + " right identity");
			}
		} finally {
			rows.Dispose();
		}
	}

	/// <summary>
	///   A page of the opt-in shape whose eager order is not the seed's (an inner left-symmetric join under
	///   <see cref="FrozenOptions.FuseSymmetricInnerJoins" />): the page's Count / TotalCount / Truncated are
	///   eager's, the page is the frozen whole's slice, and the whole is eager's whole as a set.
	/// </summary>
	private static void AssertSamePageOfSet<TResult>(QueryResults<TResult> eager, QueryResults<TResult> frozen, QueryResults<TResult> whole, int skip, int take, Func<TResult, string> row, string tag) {
		try {
			Assert.Multiple(() => {
				Assert.That(frozen.Count, Is.EqualTo(eager.Count), tag + " Count");
				Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), tag + " TotalCount");
				Assert.That(frozen.Truncated, Is.EqualTo(eager.Truncated), tag + " Truncated");
			});
			for (var i = 0; i < frozen.Count; i++)
				Assert.That(row(frozen[i]), Is.EqualTo(row(whole[skip + i])), tag + " page row " + i + " is the whole's");
			if (skip != 0 || take != int.MaxValue)
				return;
			var eagerRows = new string[eager.Count];
			var frozenRows = new string[frozen.Count];
			for (var i = 0; i < eager.Count; i++) eagerRows[i] = row(eager[i]);
			for (var i = 0; i < frozen.Count; i++) frozenRows[i] = row(frozen[i]);
			Array.Sort(eagerRows, StringComparer.Ordinal);
			Array.Sort(frozenRows, StringComparer.Ordinal);
			Assert.That(frozenRows, Is.EqualTo(eagerRows).AsCollection, tag + " row set");
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>(a): frozen takes the pipeline; every variant × page equals eager (sequence, or per <see cref="AssertSamePageOfSet{TResult}" /> when asked) and prepared; Count agrees three ways.</summary>
	private static void AssertParity<TArgs, TResult>(Func<Variant, int, int, QueryResults<TResult>> eager, Func<int> eagerCount, PreparedQuery<TArgs, TResult> prepared, FrozenQuery<TArgs, TResult> frozen,
		TArgs args, Func<TResult, string> row, string label, bool sequence = true, Action<QueryResults<TResult>, bool, string>? identity = null) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		using var whole = frozen.Execute(in args);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				if (sequence) {
					AssertSameJoined(eager(v, skip, take), Run(frozen, in args, v, skip, take), row);
					AssertSameJoined(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), row);
				} else {
					AssertSamePageOfSet(eager(v, skip, take), Run(frozen, in args, v, skip, take), whole, skip, take, row, tag);
					AssertSamePageOfSet(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take), whole, skip, take, row, tag + " prepared");
				}

				identity?.Invoke(Run(frozen, in args, v, skip, take), IsClone(v), tag);
			}

		var expected = eagerCount();
		Assert.That(frozen.Count(in args), Is.EqualTo(expected), label + " Count");
		Assert.That(prepared.Count(in args), Is.EqualTo(expected), label + " prepared Count");
	}

	// ── (a) Outer: the four families, identity and selector, unsorted ─────────────

	[Test]
	public void Outer_FourFamilies_Identity_Selector_Unsorted_EveryVariant_EveryPage_LikeEagerAndPrepared() {
		var leftSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers);
		var leftSymSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, static c => 100 + c, _customerCodes);
		var leftSymB = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customerCodes, _codeByCustomer);
		var pk = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_invoices);
		var pkSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(static id => 5000 + id, _shipments);
		var rightUnique = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_shipments, _shipmentByOrder);
		var rightUniqueSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(static id => 9000 + id, _shipments, _shipmentByCode);
		var leftUnique = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_orderShipKey, _shipments);
		var leftUniqueSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_orderShipKey, static k => k - 5000, _invoices);
		var (leftSymP, leftSymF) = (leftSym.Build(), leftSym.BuildFrozen());
		var (leftSymSelP, leftSymSelF) = (leftSymSel.Build(), leftSymSel.BuildFrozen());
		var (leftSymBP, leftSymBF) = (leftSymB.Build(), leftSymB.BuildFrozen());
		var (pkP, pkF) = (pk.Build(), pk.BuildFrozen());
		var (pkSelP, pkSelF) = (pkSel.Build(), pkSel.BuildFrozen());
		var (rightUniqueP, rightUniqueF) = (rightUnique.Build(), rightUnique.BuildFrozen());
		var (rightUniqueSelP, rightUniqueSelF) = (rightUniqueSel.Build(), rightUniqueSel.BuildFrozen());
		var (leftUniqueP, leftUniqueF) = (leftUnique.Build(), leftUnique.BuildFrozen());
		var (leftUniqueSelP, leftUniqueSelF) = (leftUniqueSel.Build(), leftUniqueSel.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5, 7 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).Count(),
				leftSymP, leftSymF, p, Customer, "left-sym " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _customers, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, static c => 100 + c, _customerCodes), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, static c => 100 + c, _customerCodes).Count(),
				leftSymSelP, leftSymSelF, p, Customer, "left-sym selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _customerCodes, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customerCodes, _codeByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customerCodes, _codeByCustomer).Count(),
				leftSymBP, leftSymBF, p, Customer, "left-sym via right index " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _customerCodes, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).Count(),
				pkP, pkF, p, Invoice, "pk " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _invoices, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(static id => 5000 + id, _shipments), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(static id => 5000 + id, _shipments).Count(),
				pkSelP, pkSelF, p, Shipment, "pk selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_shipments, _shipmentByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_shipments, _shipmentByOrder).Count(),
				rightUniqueP, rightUniqueF, p, Shipment, "right-unique " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(static id => 9000 + id, _shipments, _shipmentByCode), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(static id => 9000 + id, _shipments, _shipmentByCode).Count(),
				rightUniqueSelP, rightUniqueSelF, p, Shipment, "right-unique selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, _shipments), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, _shipments).Count(),
				leftUniqueP, leftUniqueF, p, Shipment, "left-unique " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, static k => k - 5000, _invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, static k => k - 5000, _invoices).Count(),
				leftUniqueSelP, leftUniqueSelF, p, Invoice, "left-unique selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _invoices, static c => c.Id, tag));
		}

		Assert.That(leftSymF.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0").And.Not.Contain("sort:"));
	}

	// ── (a, b) Inner: the families that fuse by default; a left without a right is dropped and not counted ──

	[Test]
	public void Inner_FusableFamilies_Identity_Selector_Unsorted_DropsUnmatched_CountMatchesEager() {
		var pk = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_invoices);
		var pkSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(static id => 5000 + id, _shipments);
		var rightUnique = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_shipments, _shipmentByOrder);
		var rightUniqueSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(static id => 9000 + id, _shipments, _shipmentByCode);
		var leftUnique = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_orderShipKey, _shipments);
		var (pkP, pkF) = (pk.Build(), pk.BuildFrozen());
		var (pkSelP, pkSelF) = (pkSel.Build(), pkSel.BuildFrozen());
		var (rightUniqueP, rightUniqueF) = (rightUnique.Build(), rightUnique.BuildFrozen());
		var (rightUniqueSelP, rightUniqueSelF) = (rightUniqueSel.Build(), rightUniqueSel.BuildFrozen());
		var (leftUniqueP, leftUniqueF) = (leftUnique.Build(), leftUnique.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5, 7 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).Count(),
				pkP, pkF, p, Invoice, "inner pk " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _invoices, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(static id => 5000 + id, _shipments), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(static id => 5000 + id, _shipments).Count(),
				pkSelP, pkSelF, p, Shipment, "inner pk selector " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_shipments, _shipmentByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_shipments, _shipmentByOrder).Count(),
				rightUniqueP, rightUniqueF, p, Shipment, "inner right-unique " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(static id => 9000 + id, _shipments, _shipmentByCode), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(static id => 9000 + id, _shipments, _shipmentByCode).Count(),
				rightUniqueSelP, rightUniqueSelF, p, Shipment, "inner right-unique selector " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_orderShipKey, _shipments), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_orderShipKey, _shipments).Count(),
				leftUniqueP, leftUniqueF, p, Shipment, "inner left-unique " + p);

			// Every inner row carries its right, the count is the rows, and the rows are the matched lefts of the product.
			using var rows = pkF.ExecutePooled(p);
			Assert.That(rows.Count, Is.EqualTo(pkF.Count(p)));
			Assert.That(rows.TotalCount, Is.EqualTo(rows.Count));
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right, Is.Not.Null);
				Assert.That(rows[i].Right!.Id, Is.EqualTo(rows[i].Left.Id));
				Assert.That(rows[i].Left.Id % 3, Is.Not.Zero, "an order without an invoice is dropped");
			}
		}
	}

	// ── (a, b) Inner left-symmetric: byte-identical by default (replay), fused and reordered on request ──

	// The eager fan-out creates an inner left-symmetric join's rows grouped by right key, which a per-left
	// lookup cannot reproduce. By default such a chain replays, so every variant × page is byte-identical to
	// eager — the rule the rest of stage 3 keeps. FuseSymmetricInnerJoins opts into the fused form: the same
	// rows and the same Count in the seed's order, and its pages still partition its own whole.
	[Test]
	public void InnerLeftSym_ReplaysByDefault_ByteIdentical_AndFusesOnlyOnRequest() {
		var plain = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_byCustomer, _customers);
		var sel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_byCustomer, static c => 100 + c, _customerCodes);
		var viaIndex = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_byCustomer, _customerCodes, _codeByCustomer);
		var (plainP, plainF) = (plain.Build(), plain.BuildFrozen());
		var (selP, selF) = (sel.Build(), sel.BuildFrozen());
		var (viaIndexP, viaIndexF) = (viaIndex.Build(), viaIndex.BuildFrozen());
		var fused = plain.BuildFrozen(FuseSym);
		Assert.Multiple(() => {
			Assert.That(plainF.Plan.Executor, Is.EqualTo("Replay"), "an inner left-symmetric join regroups: byte-identical by default");
			Assert.That(selF.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(viaIndexF.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(fused.Plan.Executor, Is.EqualTo("Pipeline"), "opted in");
			Assert.That(fused.Explain(), Does.Contain("joins: 1 (fused: 1, unfused: 0"));
		});

		foreach (var p in new[] { 0, 3, 5, 7 }) {
			// Default: the sequence, not just the set, across every variant and page.
			foreach (var v in Variants)
				foreach (var (skip, take) in Pages) {
					AssertSameJoined(Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customers), v, skip, take), Run(plainF, in p, v, skip, take), Customer);
					AssertSameJoined(Run(plainP, in p, v, skip, take), Run(plainF, in p, v, skip, take), Customer);
					AssertSameJoined(Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, static c => 100 + c, _customerCodes), v, skip, take), Run(selF, in p, v, skip, take), Customer);
					AssertSameJoined(Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customerCodes, _codeByCustomer), v, skip, take), Run(viaIndexF, in p, v, skip, take), Customer);
				}

			var expected = _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customers).Count();
			Assert.That(plainF.Count(p), Is.EqualTo(expected), "default Count " + p);
			Assert.That(selF.Count(p), Is.EqualTo(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, static c => 100 + c, _customerCodes).Count()));
			Assert.That(viaIndexF.Count(p), Is.EqualTo(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customerCodes, _codeByCustomer).Count()));

			// Opted in: the same rows and Count, its own order; every row carries its right; pages partition its whole.
			Assert.That(fused.Count(p), Is.EqualTo(expected), "fused Count " + p);
			using var whole = fused.Execute(p);
			foreach (var v in Variants)
				foreach (var (skip, take) in Pages)
					AssertSamePageOfSet(Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customers), v, skip, take), Run(fused, in p, v, skip, take), whole, skip, take, Customer, $"fused left-sym {p} {v} {skip}/{take}");
			for (var i = 0; i < whole.Count; i++)
				Assert.That(whole[i].Right, Is.Not.Null, "an inner row always carries its right");
		}
	}

	[Test]
	public void MissingRights_Outer_NullSlot_Inner_Dropped_AndCounts() {
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).BuildFrozen();
		// The inner left-symmetric join fuses only on request; the missing-right behaviour is pinned on both paths.
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(_byCustomer, _customers).BuildFrozen(FuseSym);
		var innerReplay = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(_byCustomer, _customers).BuildFrozen();
		var innerPk = _orders.Prepare<int, PqOrder, int>().UseIndex(_byQty, static q => q).InnerJoinOne(_invoices).BuildFrozen();
		Assert.That(outer.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(inner.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(innerReplay.Plan.Executor, Is.EqualTo("Replay"));
		Assert.That(innerPk.Plan.Executor, Is.EqualTo("Pipeline"));
		// Customers 8 and 9 do not exist: the outer join keeps every order of theirs with a null right, the inner drops them all.
		foreach (var missing in new[] { 8, 9 }) {
			using (var rows = outer.ExecutePooled(missing)) {
				Assert.That(rows.Count, Is.EqualTo(N / Customers));
				Assert.That(rows.TotalCount, Is.EqualTo(N / Customers));
				for (var i = 0; i < rows.Count; i++) Assert.That(rows[i].Right, Is.Null);
			}

			Assert.That(outer.Count(missing), Is.EqualTo(N / Customers));
			using (var rows = inner.ExecutePooled(missing)) {
				Assert.That(rows.Count, Is.Zero);
				Assert.That(rows.TotalCount, Is.Zero);
			}

			Assert.That(inner.Count(missing), Is.Zero);
			Assert.That(inner.Count(missing), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, missing).InnerJoinOne(_byCustomer, _customers).Count()));
			using (var rows = innerReplay.ExecutePooled(missing)) {
				Assert.That(rows.Count, Is.Zero);
				Assert.That(rows.TotalCount, Is.Zero);
			}

			Assert.That(innerReplay.Count(missing), Is.Zero);
		}

		// Customer 3 exists: the inner join keeps all its orders, every one with the right.
		using (var rows = inner.ExecutePooled(3)) {
			Assert.That(rows.Count, Is.EqualTo(N / Customers));
			for (var i = 0; i < rows.Count; i++) Assert.That(rows[i].Right?.Id, Is.EqualTo(3));
		}

		for (var q = 0; q < 13; q++) {
			var expected = _orders.Query().UseIndex(_byQty, q).InnerJoinOne(_invoices).Count();
			Assert.That(innerPk.Count(q), Is.EqualTo(expected), "Count qty " + q);
			using var rows = innerPk.ExecutePooled(q);
			Assert.That(rows.Count, Is.EqualTo(expected));
			Assert.That(rows.TotalCount, Is.EqualTo(expected));
			AssertSameJoined(_orders.Query().UseIndex(_byQty, q).InnerJoinOne(_invoices).Execute(), innerPk.Execute(q), Invoice);
			AssertSameJoined(_orders.Query().UseIndex(_byQty, q).InnerJoinOne(_invoices).Execute(1, 3), innerPk.Execute(q, 1, 3), Invoice);
		}
	}

	// ── (c) Chained ───────────────────────────────────────────────────────────────

	[Test]
	public void Chained_TwoAndThreeFusedJoins_OuterInnerMixes_EveryVariant_EveryPage() {
		var outerOuter = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products);
		var innerOuter = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers);
		var outerInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices);
		var three = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder);
		var threeInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_orderShipKey, _shipments).InnerJoinOne(_invoices).InnerJoinOne(_shipments, _shipmentByOrder);
		// The same triple with an inner left-symmetric head: replays by default, fuses on request.
		var threeSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).InnerJoinOne(_shipments, _shipmentByOrder);
		var threeSymF = threeSym.BuildFrozen();
		var threeSymFused = threeSym.BuildFrozen(FuseSym);
		Assert.Multiple(() => {
			Assert.That(threeSymF.Plan.Executor, Is.EqualTo("Replay"), "an inner left-symmetric join anywhere in the chain replays by default");
			Assert.That(threeSymFused.Plan.Executor, Is.EqualTo("Pipeline"), "opted in");
		});
		var (outerOuterP, outerOuterF) = (outerOuter.Build(), outerOuter.BuildFrozen());
		var (innerOuterP, innerOuterF) = (innerOuter.Build(), innerOuter.BuildFrozen());
		var (outerInnerP, outerInnerF) = (outerInner.Build(), outerInner.BuildFrozen());
		var (threeP, threeF) = (three.Build(), three.BuildFrozen());
		var (threeInnerP, threeInnerF) = (threeInner.Build(), threeInner.BuildFrozen());
		foreach (var p in new[] { 0, 2, 5 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Count(),
				outerOuterP, outerOuterF, p, CustomerProduct, "outer+outer " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers).Count(),
				innerOuterP, innerOuterF, p, InvoiceCustomer, "inner+outer " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).Count(),
				outerInnerP, outerInnerF, p, CustomerInvoice, "outer+inner " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder).Count(),
				threeP, threeF, p, Three, "outer+inner+outer " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_orderShipKey, _shipments).InnerJoinOne(_invoices).InnerJoinOne(_shipments, _shipmentByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_orderShipKey, _shipments).InnerJoinOne(_invoices).InnerJoinOne(_shipments, _shipmentByOrder).Count(),
				threeInnerP, threeInnerF, p, ThreeShip, "inner×3 " + p);
			// The left-symmetric head: byte-identical on the default path, same rows and Count when opted in.
			foreach (var (skip, take) in Pages)
				AssertSameJoined(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).InnerJoinOne(_shipments, _shipmentByOrder).ExecutePooled(skip, take), threeSymF.ExecutePooled(p, skip, take), Three);
			Assert.That(threeSymFused.Count(p), Is.EqualTo(threeSymF.Count(p)), "fused left-sym head Count " + p);
			using var rows = threeInnerF.ExecutePooled(p);
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right, Is.Not.Null);
				Assert.That(rows[i].Right2, Is.Not.Null);
				Assert.That(rows[i].Right3, Is.Not.Null);
			}
		}

		Assert.That(threeF.Explain(), Does.Contain("joins: 3 (fused: 3, unfused: 0"));
	}

	// ── (d) Sort before and after the fused joins ─────────────────────────────────

	[Test]
	public void Sort_AfterFusedJoin_OverJoinedField_AndBeforeIt_EveryVariant_EveryPage() {
		var after = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).Sort(new ByRegionThenIdDesc());
		var afterBounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).SortBounded(new ByRegionThenIdDesc());
		var afterStruct = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_orderShipKey, _shipments).Sort(new ByShipmentCarrierThenId());
		var before = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers);
		var beforeInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers);
		var (afterP, afterF) = (after.Build(), after.BuildFrozen());
		var (afterBoundedP, afterBoundedF) = (afterBounded.Build(), afterBounded.BuildFrozen());
		var (afterStructP, afterStructF) = (afterStruct.Build(), afterStruct.BuildFrozen());
		var (beforeP, beforeF) = (before.Build(), before.BuildFrozen());
		var (beforeInnerP, beforeInnerF) = (beforeInner.Build(), beforeInner.BuildFrozen());
		var cmp = new ByRegionThenIdDesc();
		foreach (var p in new[] { 0, 1, 4, 5 }) {
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).Sort(cmp), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).Sort(cmp).Count(),
				afterP, afterF, p, Customer, "sort after join " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _customers, static c => c.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).SortBounded(cmp), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_byCustomer, _customers).SortBounded(cmp).Count(),
				afterBoundedP, afterBoundedF, p, Customer, "sort-bounded after join " + p);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, _shipments).Sort(new ByShipmentCarrierThenId()), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_orderShipKey, _shipments).Sort(new ByShipmentCarrierThenId()).Count(),
				afterStructP, afterStructF, p, Shipment, "struct sort after join " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Count(),
				beforeP, beforeF, p, Customer, "sort before join " + p);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers).Count(),
				beforeInnerP, beforeInnerF, p, InvoiceCustomer, "sort before inner+outer " + p);
		}

		Assert.That(afterF.Explain(), Does.Contain("sort: classic").And.Contain("joins: 1 (fused: 1, unfused: 0"));
		Assert.That(afterBoundedF.Explain(), Does.Contain("sort: classic"), "a SortBounded after the join is the classic container's sort, as in eager");
		Assert.That(beforeF.Explain(), Does.Contain("pipeline: seed = free for Execute").And.Contain("sort: bounded"), "a classic Sort over the left value before the joins: a finite page takes the bounded flow (step 8), the seed stays free");
	}

	[Test]
	public void SortBounded_BeforeFusedJoins_Inner_Chained_TieComparer_EveryVariant_EveryPage_AndPagesPartition() {
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinOne(_orderShipKey, _shipments);
		var chained = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers);
		var ties = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByCustomer()).InnerJoinOne(_shipments, _shipmentByOrder).JoinOne(_orderShipKey, static k => k - 5000, _invoices);
		var (innerP, innerF) = (inner.Build(), inner.BuildFrozen());
		var (chainedP, chainedF) = (chained.Build(), chained.BuildFrozen());
		var (tiesP, tiesF) = (ties.Build(), ties.BuildFrozen());
		foreach (var p in new[] { 0, 2, 5 }) {
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_orderShipKey, _shipments), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_orderShipKey, _shipments).Count(),
				innerP, innerF, p, Shipment, "bounded inner " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _shipments, static c => c.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers).Count(),
				chainedP, chainedF, p, InvoiceCustomer, "bounded inner+outer " + p);
			// A tie comparer: the fixed seed makes the ordinals eager's, so the page is byte-identical even so.
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByCustomer()).InnerJoinOne(_shipments, _shipmentByOrder).JoinOne(_orderShipKey, static k => k - 5000, _invoices), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByCustomer()).InnerJoinOne(_shipments, _shipmentByOrder).JoinOne(_orderShipKey, static k => k - 5000, _invoices).Count(),
				tiesP, tiesF, p, static r => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}", "bounded ties inner+outer " + p);

			using var whole = _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByCustomer()).InnerJoinOne(_shipments, _shipmentByOrder).JoinOne(_orderShipKey, static k => k - 5000, _invoices).ExecutePooled(0, N);
			var expected = new string[whole.Count];
			for (var i = 0; i < whole.Count; i++) expected[i] = $"{whole[i].Left.Id}|{whole[i].Right?.Id}";
			foreach (var size in new[] { 1, 3, 7 }) {
				var paged = new List<string>();
				for (var skip = 0; skip < whole.Count + size; skip += size) {
					using var page = tiesF.ExecutePooled(p, skip, size);
					Assert.That(page.TotalCount, Is.EqualTo(whole.Count), "TotalCount counts matched lefts only");
					for (var i = 0; i < page.Count; i++) paged.Add($"{page[i].Left.Id}|{page[i].Right?.Id}");
				}

				Assert.That(paged, Is.EqualTo(expected).AsCollection, $"product {p} pages of {size} partition the whole");
			}
		}

		Assert.That(innerF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 1 (fused: 1, unfused: 0"));
	}

	// ── (h) Step 7: the frozen bounded joined container ───────────────────────────

	// A class comparer: the sorter reaches the frozen bounded container as its own struct type, and its
	// comparer is a reference type — a different instantiation from the struct-comparer shapes above.
	private sealed class ByQtyThenIdClass : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) {
			var c = (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
			return c != 0 ? c : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
		}
	}

	// Ties on purpose: the page is byte-identical only because the encounter ordinals are eager's.
	private sealed class ByQtyClass : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) => (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
	}

	private sealed class BombLeft : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y)
			=> x?.CustomerId == 7 || y?.CustomerId == 7 ? throw new InvalidOperationException("compare boom") : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	/// <summary>
	///   Step 7: a joined <c>SortBounded</c> page runs through <c>FrozenTopKJoinedContainer</c>, which the
	///   chain hands its sorter through <c>IResolvers.WithSorter</c> instead of comparing through the chain.
	///   The dispatch has to find the same sorter whatever the chain's depth and whatever the comparer's
	///   own kind, and the page it produces has to stay eager's byte for byte — rows, joined slots,
	///   <c>TotalCount</c>, <c>Truncated</c>, ties, paging, clone identity — on the heap plan (a small
	///   page), the collect-all plan (a page near the bucket size) and the classic fallback
	///   (<c>take = int.MaxValue</c>), with the pooled heap balanced when a user comparer throws on any of
	///   the three.
	/// </summary>
	[Test]
	public void Step7_BoundedJoinedContainer_ClassComparer_DeepChain_MixedMask_ByteIdentical_AndLeakBalanced() {
		var one = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers);
		var ties = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyClass()).JoinOne(_byCustomer, _customers);
		var deep = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder);
		var mixed = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyClass()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU"));
		var (oneP, oneF) = (one.Build(), one.BuildFrozen());
		var (tiesP, tiesF) = (ties.Build(), ties.BuildFrozen());
		var (deepP, deepF) = (deep.Build(), deep.BuildFrozen());
		var (mixedP, mixedF) = (mixed.Build(), mixed.BuildFrozen());
		Assert.Multiple(() => {
			Assert.That(deepF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 3 (fused: 3, unfused: 0"));
			Assert.That(mixedF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 2 (fused: 1, unfused: 1"));
		});
		foreach (var p in new[] { 0, 2, 5 }) {
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers).Count(),
				oneP, oneF, p, Customer, "class comparer, one join " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _customers, static c => c.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyClass()).JoinOne(_byCustomer, _customers), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyClass()).JoinOne(_byCustomer, _customers).Count(),
				tiesP, tiesF, p, Customer, "class tie comparer, one join " + p);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenIdClass()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).JoinOne(_shipments, _shipmentByOrder).Count(),
				deepP, deepF, p, Three, "class comparer, three joins " + p);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyClass()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyClass()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Count(),
				mixedP, mixedF, p, InvoiceCustomer, "class tie comparer, fused + unfused " + p);

			// Consecutive pages of the tying deep chain partition the eager whole — the bounded contract.
			using var whole = _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyClass()).JoinOne(_byCustomer, _customers).ExecutePooled(0, N);
			var expected = new string[whole.Count];
			for (var i = 0; i < whole.Count; i++) expected[i] = Customer(whole[i]);
			foreach (var size in new[] { 1, 3, 7 }) {
				var paged = new List<string>();
				for (var skip = 0; skip < whole.Count + size; skip += size) {
					using var page = tiesF.ExecutePooled(p, skip, size);
					Assert.That(page.TotalCount, Is.EqualTo(whole.Count), "TotalCount");
					for (var i = 0; i < page.Count; i++) paged.Add(Customer(page[i]));
				}

				Assert.That(paged, Is.EqualTo(expected).AsCollection, $"product {p} pages of {size} partition the whole");
			}
		}

		// A throwing class comparer on all three container plans plus the eager twin: the heap and the
		// page buffer go back either way. Customer 7 owns orders 7, 17, … so every product bucket has one.
		var bomb = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new BombLeft()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).BuildFrozen();
		Assert.That(bomb.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => bomb.ExecutePooled(1, 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => bomb.ExecutePooledCloned(1, 2, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => bomb.ExecutePooled(1, 2, 100).Dispose());
			Assert.Throws<InvalidOperationException>(() => bomb.ExecutePooled(1).Dispose());
			Assert.Throws<InvalidOperationException>(() => bomb.Execute(1, 3, int.MaxValue).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byProduct, 1).SortBounded(new BombLeft()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).ExecutePooled(0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byProduct, 1).SortBounded(new BombLeft()).JoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).ExecutePooled(2, 100).Dispose());
			Assert.That(bomb.Count(1), Is.GreaterThanOrEqualTo(0), "Count never compares");
		});
	}

	// ── (e) Executor selection and Explain ────────────────────────────────────────

	[Test]
	public void Executor_FusedShapesTakeThePipeline_FilteredReplays_JoinManyAdmitted_MixedStepSixShapeKeepsTheMask() {
		const string region = "EU";
		Assert.Multiple(() => {
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unsorted fused");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).InnerJoinOne(_invoices).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "unsorted inner fused");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).InnerJoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "an inner left-symmetric join regroups its rows");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).InnerJoinOne(_byCustomer, _customers).BuildFrozen(FuseSym).Plan.Executor, Is.EqualTo("Pipeline"), "…unless the caller opts in");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "the same under a SortBounded");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "an OUTER left-symmetric join fuses: the fan-out only fills existing rows");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers).BuildFrozen(NoPipeline).Plan.Executor, Is.EqualTo("Replay"), "pipeline off");
			Assert.That(_orders.Prepare().JoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "no seed source");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a filtered JoinOne cannot fuse (unsorted)");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers, static (q, r) => q.Where(c => c.Region == r), region).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a filtered JoinOne with an arg cannot fuse");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "one unfusable join outside the step-6 shape replays the chain");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a classic Sort over the left value is the step-6 shape too since step 8 (its finite pages take the bounded flow)");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_invoices).Sort(new ByRegionThenIdDescOverInvoice()).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a Sort over the joined row is not");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "the step-6 shape keeps its unfused paired read");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "an unfusable inner join replays");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "JoinMany: admitted, its fan-out after the pass (step 8; FrozenPipelineJoinManyTests)");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_invoices).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a JoinMany anywhere in the chain");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "an inner left-symmetric JoinOne still replays, JoinMany or not");
			Assert.That(_orders.Prepare().Or(b => b.UseIndex(_byProduct, 1), b => b.UseIndex(_byProduct, 2)).JoinOne(_invoices).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a composite narrowing (step 4)");
		});

		// The mixed step-6 shape: the PK join is fused in the pass, the filtered left-symmetric join keeps
		// its paired read over the page rows (the mask), on the bounded and the classic (unbounded) page.
		var mixed = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen();
		var mixedPrepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Build();
		Assert.That(mixed.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(mixed.Explain(), Does.Contain("joins: 2 (fused: 1, unfused: 1"));
		foreach (var p in new[] { 0, 3, 5 })
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")), v, s, t),
				() => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinOne(_invoices).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Count(),
				mixedPrepared, mixed, p, InvoiceCustomer, "mixed step-6 " + p);

		var unsorted = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_invoices).JoinOne(_byCustomer, _customers).BuildFrozen();
		Assert.That(unsorted.Explain(), Does.Contain("executor: Pipeline").And.Contain("pipeline: seed = fixed for Execute").And.Contain("joins: 2 (fused: 2, unfused: 0").And.Contain("resolvers: yes, sorted: no").And.Not.Contain("sort:"));
	}

	// ── (f) Leaks ─────────────────────────────────────────────────────────────────

	private sealed class BombJoined : IComparer<JoinResult<PqOrder, PqCustomer?>> {
		public int Compare(JoinResult<PqOrder, PqCustomer?> x, JoinResult<PqOrder, PqCustomer?> y)
			=> x.Left.CustomerId == 7 || y.Left.CustomerId == 7 ? throw new InvalidOperationException("compare boom") : x.Left.Id.CompareTo(y.Left.Id);
	}

	private sealed class PqBombRight : ICacheEquatable<PqBombRight>, ICacheClonable<PqBombRight> {
		public int Id { get; init; }
		public bool CacheEquals(PqBombRight? other) => other is not null && other.Id == Id;
		public int CacheGetHashCode() => Id;
		// Order 117 (customer 7, Qty 0) is on every page of customer 7, sorted or not.
		public PqBombRight Clone() => Id == 5117 ? throw new InvalidOperationException("clone boom") : new PqBombRight { Id = Id };
	}

	[Test]
	public void Throwing_Selector_Predicate_Comparer_Clone_LeaveNoRentedArrays_FusedPaths_EagerTwins() {
		var bombs = new InMemoryDataCache<int, PqBombRight>();
		for (var i = 0; i < N; i++) bombs.AddOrUpdate(5000 + i, new PqBombRight { Id = 5000 + i });

		// Customer 7 owns order 7: the PK selector throws there (outer: in the fill; inner: in the fill and the count).
		var selector = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(static id => id == 7 ? throw new InvalidOperationException("selector boom") : 5000 + id, _shipments).BuildFrozen();
		var innerSelector = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(static id => id == 7 ? throw new InvalidOperationException("selector boom") : 5000 + id, _shipments).BuildFrozen();
		var boundedSelector = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).SortBounded(new ByQtyThenId()).InnerJoinOne(static id => id == 7 ? throw new InvalidOperationException("selector boom") : 5000 + id, _shipments).BuildFrozen();
		var predicate = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).Where(static (o, c) => o.Id == 17 && c == 7 ? throw new InvalidOperationException("predicate boom") : true).JoinOne(_byCustomer, _customers).BuildFrozen();
		var comparer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).Sort(new BombJoined()).BuildFrozen();
		var cloned = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(static id => 5000 + id, bombs).BuildFrozen();
		var clonedBounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).SortBounded(new ByQtyThenId()).JoinOne(static id => 5000 + id, bombs).BuildFrozen();
		Assert.Multiple(() => {
			foreach (var q in new FrozenQuery<int, JoinResult<PqOrder, PqShipment?>>[] { selector, innerSelector, boundedSelector })
				Assert.That(q.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(predicate.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(comparer.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(cloned.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(clonedBounded.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooledCloned(7, 1, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => innerSelector.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => innerSelector.Count(7));
			Assert.Throws<InvalidOperationException>(() => boundedSelector.ExecutePooled(7, 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => boundedSelector.ExecutePooledCloned(7, 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => boundedSelector.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => boundedSelector.Count(7));
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooledCloned(7, 0, 4).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.Count(7));
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooledCloned(7, 2, 3).Dispose());
			// Clone-on-add (no slice) and clone-after-slice / after the bounded page, frozen and eager.
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecuteCloned(7, 7, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedBounded.ExecutePooledCloned(7, 0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedBounded.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinOne(static id => 5000 + id, bombs).ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinOne(static id => 5000 + id, bombs).ExecutePooledCloned(7, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).SortBounded(new ByQtyThenId()).JoinOne(static id => 5000 + id, bombs).ExecutePooledCloned(0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinOne(_byCustomer, _customers).Sort(new BombJoined()).ExecutePooled().Dispose());
			// The same plans on a customer that does not trip them.
			selector.ExecutePooled(3).Dispose();
			innerSelector.ExecutePooledCloned(3, 1, 3).Dispose();
			boundedSelector.ExecutePooled(3, 0, 3).Dispose();
			predicate.ExecutePooled(3).Dispose();
			comparer.ExecutePooled(3, 0, 4).Dispose();
			cloned.ExecutePooledCloned(3).Dispose();
			clonedBounded.ExecutePooledCloned(3, 0, 5).Dispose();
			Assert.That(innerSelector.Count(3), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, 3).InnerJoinOne(static id => 5000 + id, _shipments).Count()));
		});
		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 3).JoinOne(static id => 5000 + id, _shipments).Execute(), selector.Execute(3), Shipment);
	}

	// ── (g) Concurrency ───────────────────────────────────────────────────────────

	[Test]
	public void EightReaders_AgainstAWriterChurningTheRights_NeverThrow_NoDuplicates_InnerRowsCarryTheirRight() {
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).JoinOne(_invoices).BuildFrozen();
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_byCustomer, _customers).InnerJoinOne(_invoices).BuildFrozen(FuseSym);
		var bounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinOne(_orderShipKey, _shipments).JoinOne(_shipments, _shipmentByOrder).BuildFrozen();
		var sorted = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).Sort(new ByRegionThenIdDesc()).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				// Rights leave and come back: customers, invoices, shipments; lefts move across products and customers.
				if (i % 3 == 0) _customers.Remove(id % Customers);
				if (i % 3 == 1) _customers.AddOrUpdate(id % Customers, new PqCustomer { Id = id % Customers, Region = i % 2 == 0 ? "EU" : "US" });
				if (i % 5 == 0) _invoices.Remove(id);
				if (i % 5 == 2) _invoices.AddOrUpdate(id, new PqInvoice { Id = id, Amount = 100 + id });
				if (i % 7 == 0) _shipments.Remove(5000 + id);
				if (i % 7 == 3) _shipments.AddOrUpdate(5000 + id, new PqShipment { Id = 5000 + id, OrderId = id, Carrier = "X" });
				_orders.AddOrUpdate(id, new PqOrder { Id = id, CustomerId = (id + i) % Customers, ProductId = (id + i) % Products, Qty = i % 13 });
				if (i % 11 == 0) _orders.Remove((id * 13) % N);
				if (i % 11 == 5) _orders.AddOrUpdate((id * 13) % N, MakeOrder((id * 13) % N));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				var seen = new HashSet<int>();
				for (var i = 0; i < 1_500; i++) {
					var product = (seed + i) % Products;
					var skip = i % 3;
					using (var rows = outer.ExecutePooled(product)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							if (rows[r].Right2 is { } invoice)
								Assert.That(invoice.Id, Is.EqualTo(rows[r].Left.Id), "the invoice is the row's");
						}
					}

					using (var rows = inner.ExecutePooledCloned(product, skip, 10)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right, Is.Not.Null, "an inner row always carries its right");
							Assert.That(rows[r].Right2, Is.Not.Null, "an inner row always carries its right");
						}
					}

					using (var rows = bounded.ExecutePooled(product, skip, 5)) {
						seen.Clear();
						Assert.That(rows.Count, Is.LessThanOrEqualTo(5));
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right, Is.Not.Null, "an inner row always carries its right");
							if (rows[r].Right2 is { } shipment)
								Assert.That(shipment.OrderId, Is.EqualTo(rows[r].Left.Id), "the shipment is the row's");
						}
					}

					using (var rows = sorted.ExecutePooled(product, skip, 6)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
					}

					Assert.That(inner.Count(product), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}
}

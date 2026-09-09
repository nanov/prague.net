namespace Prague.Core.Tests.Prepared;

using System.Text;
using Prague.Core;
using Prague.Core.Tests.Infrastructure;

// Joined shapes, eager vs prepared from the same inputs. The prepared joined command replays the
// left chain into the eager core and then runs the eager joined pipeline over it, so Count,
// TotalCount, Truncated and the exact (left id, right id(s)) row sequence must agree on every
// shape: outer / inner JoinOne, right-side filters, chained JoinOnes, JoinMany fan-out, sorted.
[TestFixture]
public class PreparedQueryJoinDifferentialTests {
	internal sealed class PqOrder : ICacheEquatable<PqOrder>, ICacheClonable<PqOrder> {
		public int Id { get; init; }
		public int CustomerId { get; init; }
		public int ProductId { get; init; }
		public int Qty { get; init; }

		public bool CacheEquals(PqOrder? other)
			=> other is not null && other.Id == Id && other.CustomerId == CustomerId && other.ProductId == ProductId && other.Qty == Qty;

		public int CacheGetHashCode() => HashCode.Combine(Id, CustomerId, ProductId, Qty);

		public PqOrder Clone() => new() { Id = Id, CustomerId = CustomerId, ProductId = ProductId, Qty = Qty };
	}

	internal sealed class PqCustomer : ICacheEquatable<PqCustomer>, ICacheClonable<PqCustomer> {
		public int Id { get; init; }
		public string Region { get; init; } = "";

		public bool CacheEquals(PqCustomer? other) => other is not null && other.Id == Id && other.Region == Region;

		public int CacheGetHashCode() => HashCode.Combine(Id, Region);

		public PqCustomer Clone() => new() { Id = Id, Region = Region };
	}

	internal sealed class PqProduct : ICacheEquatable<PqProduct>, ICacheClonable<PqProduct> {
		public int Id { get; init; }
		public string Category { get; init; } = "";

		public bool CacheEquals(PqProduct? other) => other is not null && other.Id == Id && other.Category == Category;

		public int CacheGetHashCode() => HashCode.Combine(Id, Category);

		public PqProduct Clone() => new() { Id = Id, Category = Category };
	}

	// 1:N right side of the orders: several lines per order, none for every fourth order.
	internal sealed class PqLine : ICacheEquatable<PqLine>, ICacheClonable<PqLine> {
		public int Id { get; init; }
		public int OrderId { get; init; }

		public bool CacheEquals(PqLine? other) => other is not null && other.Id == Id && other.OrderId == OrderId;

		public int CacheGetHashCode() => HashCode.Combine(Id, OrderId);

		public PqLine Clone() => new() { Id = Id, OrderId = OrderId };
	}

	// PK-to-PK right side of the orders (Id == order id), present for two orders in three: the
	// JoinOneResolver family whose PrepareIndexedInner reads the executor's candidates.
	internal sealed class PqInvoice : ICacheEquatable<PqInvoice>, ICacheClonable<PqInvoice> {
		public int Id { get; init; }
		public int Amount { get; init; }

		public bool CacheEquals(PqInvoice? other) => other is not null && other.Id == Id && other.Amount == Amount;

		public int CacheGetHashCode() => HashCode.Combine(Id, Amount);

		public PqInvoice Clone() => new() { Id = Id, Amount = Amount };
	}

	private const int N = 120;
	private const int Customers = 10; // ids 0..7 exist; 8 and 9 are referenced but missing
	private const int Products = 6;   // ids 0..4 exist; 5 is referenced but missing

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqProduct> _products = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByOrder = null!;
	private InMemoryDataCache<int, PqInvoice> _invoices = null!;

	// Struct comparers over the left value: the shape the bounded joined plan can drive.
	private readonly struct ByQtyThenId : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) {
			var c = (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
			return c != 0 ? c : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
		}
	}

	private readonly struct ByCustomer : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) => (x?.CustomerId ?? 0).CompareTo(y?.CustomerId ?? 0);
	}

	// Comparer over the joined row: a post-join sorter, which the bounded plan cannot drive.
	private sealed class ByRegionThenIdDesc : IComparer<JoinResult<PqOrder, PqCustomer?>> {
		public int Compare(JoinResult<PqOrder, PqCustomer?> x, JoinResult<PqOrder, PqCustomer?> y) {
			var c = string.CompareOrdinal(x.Right?.Region ?? "", y.Right?.Region ?? "");
			return c != 0 ? c : y.Left.Id.CompareTo(x.Left.Id);
		}
	}

	[SetUp]
	public void SetUp() {
		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_products = new InMemoryDataCache<int, PqProduct>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		_invoices = new InMemoryDataCache<int, PqInvoice>();

		for (var c = 0; c < Customers - 2; c++)
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 3 == 0 ? "EU" : c % 3 == 1 ? "US" : "APAC" });
		for (var p = 0; p < Products - 1; p++)
			_products.AddOrUpdate(p, new PqProduct { Id = p, Category = p % 2 == 0 ? "hard" : "soft" });
		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, MakeOrder(i));
			for (var k = 0; k < i % 4; k++)
				_lines.AddOrUpdate(1000 + i * 4 + k, new PqLine { Id = 1000 + i * 4 + k, OrderId = i });
			if (i % 3 != 0)
				_invoices.AddOrUpdate(i, new PqInvoice { Id = i, Amount = 100 + i });
		}
	}

	private static PqOrder MakeOrder(int i) => new() { Id = i, CustomerId = i % Customers, ProductId = i % Products, Qty = i % 13 };

	// ── Assertion helpers ──────────────────────────────────────────────────────────

	// One line per row: the left id and the right value(s)' ids, in result order. A JoinMany right is
	// spelled as its ids in slot order, so fan-out order is part of the comparison too.
	internal static void AssertSameJoined<TResult>(QueryResults<TResult> eager, QueryResults<TResult> prepared, Func<TResult, string> row) {
		try {
			Assert.Multiple(() => {
				Assert.That(prepared.Count, Is.EqualTo(eager.Count), "Count");
				Assert.That(prepared.TotalCount, Is.EqualTo(eager.TotalCount), "TotalCount");
				Assert.That(prepared.Truncated, Is.EqualTo(eager.Truncated), "Truncated");
			});
			var eagerRows = new string[eager.Count];
			var preparedRows = new string[prepared.Count];
			for (var i = 0; i < eager.Count; i++) eagerRows[i] = row(eager[i]);
			for (var i = 0; i < prepared.Count; i++) preparedRows[i] = row(prepared[i]);
			Assert.That(preparedRows, Is.EqualTo(eagerRows).AsCollection, "row sequence");
		} finally {
			eager.Dispose();
			prepared.Dispose();
		}
	}

	private static string One<TRight>(JoinResult<PqOrder, TRight?> r, Func<TRight, int> id) where TRight : class
		=> $"{r.Left.Id}|{(r.Right is null ? "-" : id(r.Right))}";

	private static string Many(int leftId, QueryResults<PqLine> rights) {
		var sb = new StringBuilder().Append(leftId).Append('|');
		for (var i = 0; i < rights.Count; i++) sb.Append(rights[i].Id).Append(',');
		return sb.ToString();
	}

	private static string Customer(JoinResult<PqOrder, PqCustomer?> r) => One(r, static c => c.Id);
	private static string Invoice(JoinResult<PqOrder, PqInvoice?> r) => One(r, static c => c.Id);
	private static string CustomerProduct(JoinResult<PqOrder, PqCustomer?, PqProduct?> r)
		=> $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";
	private static string Lines(JoinResult<PqOrder, QueryResults<PqLine>> r) => Many(r.Left.Id, r.Right);
	private static string CustomerLines(JoinResult<PqOrder, PqCustomer?, QueryResults<PqLine>> r)
		=> $"{(r.Right is null ? "-" : r.Right.Id)}|" + Many(r.Left.Id, r.Right2);

	// ── JoinOne outer ─────────────────────────────────────────────────────────────

	[Test]
	public void JoinOne_Outer_NoNarrowing_LikeEager() {
		var prepared = _orders.Prepare().JoinOne(_byCustomer, _customers).Build();
		AssertSameJoined(_orders.Query().JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().JoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void JoinOne_Outer_AfterListIndex_LikeEager() {
		var prepared = _orders.Prepare().UseIndex(_byProduct, 2).JoinOne(_byCustomer, _customers).Build();
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 2).JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 2).JoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void JoinOne_Outer_AfterListIndexAndWhere_EveryExecuteVariant_LikeEager() {
		var prepared = _orders.Prepare().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).Build();
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(), Customer);
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).ExecutePooled(), prepared.ExecutePooled(), Customer);
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).ExecuteCloned(), prepared.ExecuteCloned(), Customer);
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).ExecutePooledCloned(), prepared.ExecutePooledCloned(), Customer);
		AssertSameJoined(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).Execute(2, 5), prepared.Execute(2, 5), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 3).Where(static o => o.Qty > 4).JoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void JoinOne_Outer_ParameterizedLeft_ThreeArgs_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).Build();
		foreach (var product in new[] { 0, 4, 5 }) {
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).Execute(), prepared.Execute(product), Customer);
			Assert.That(prepared.Count(product), Is.EqualTo(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).Count()));
		}
	}

	// ── JoinOne inner ─────────────────────────────────────────────────────────────

	// Customers 8 and 9 do not exist, so the inner LeftSym join drops every fifth order.
	[Test]
	public void InnerJoinOne_LeftSym_NoWhere_DropsUnmatched_LikeEager() {
		var prepared = _orders.Prepare().InnerJoinOne(_byCustomer, _customers).Build();
		using (var rows = prepared.Execute()) {
			Assert.That(rows.Count, Is.EqualTo(N * (Customers - 2) / Customers));
			for (var i = 0; i < rows.Count; i++) Assert.That(rows[i].Right, Is.Not.Null);
		}

		AssertSameJoined(_orders.Query().InnerJoinOne(_byCustomer, _customers).Execute(), prepared.Execute(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().InnerJoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void InnerJoinOne_LeftSym_WithLeftWhere_LikeEager() {
		var prepared = _orders.Prepare().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).Build();
		AssertSameJoined(_orders.Query().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).Execute(), prepared.Execute(), Customer);
		AssertSameJoined(_orders.Query().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).ExecutePooled(), prepared.ExecutePooled(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().Where(static o => o.Qty % 2 == 1).InnerJoinOne(_byCustomer, _customers).Count()));
	}

	// PK-to-PK inner (JoinOneResolver): PrepareIndexedInner calls GetCandidates on the executor
	// before base execution. With the recorder in that slot the call would throw; with an
	// un-replayed core it would auto-populate every order. The rows below being exactly the
	// replayed customer-3 narrowing (and never a phantom default Left) is the direct evidence
	// that the executor the joined core saw was the replayed eager core.
	[Test]
	public void InnerJoinOne_PkToPk_AfterListIndex_PrepareIndexedInnerSeesReplayedCandidates() {
		var prepared = _orders.Prepare().UseIndex(_byCustomer, 3).InnerJoinOne(_invoices).Build();
		using (var rows = prepared.Execute()) {
			Assert.That(rows.Count, Is.GreaterThan(0));
			Assert.That(rows.Count, Is.EqualTo(8), "customer 3 has 12 orders, 8 of them invoiced");
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Left, Is.Not.Null);
				Assert.That(rows[i].Left.CustomerId, Is.EqualTo(3));
				Assert.That(rows[i].Right, Is.Not.Null);
				Assert.That(rows[i].Right!.Id, Is.EqualTo(rows[i].Left.Id));
			}
		}

		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 3).InnerJoinOne(_invoices).Execute(), prepared.Execute(), Invoice);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, 3).InnerJoinOne(_invoices).Count()));
	}

	// Filtered seed: UseIndex + Where + inner. The classic pipeline's phantom-row rule is whatever
	// the eager core does — the prepared command must reproduce it exactly, not improve on it.
	[Test]
	public void InnerJoinOne_PkToPk_AfterListIndexAndWhere_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).Where(static o => o.Qty > 6).InnerJoinOne(_invoices).Build();
		for (var customer = 0; customer < Customers; customer++) {
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty > 6).InnerJoinOne(_invoices).Execute(), prepared.Execute(customer), Invoice);
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty > 6).InnerJoinOne(_invoices).ExecutePooled(), prepared.ExecutePooled(customer), Invoice);
			Assert.That(prepared.Count(customer), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty > 6).InnerJoinOne(_invoices).Count()));
		}
	}

	[Test]
	public void InnerJoinOne_PkToPk_NoNarrowing_AutoPopulates_LikeEager() {
		var prepared = _orders.Prepare().InnerJoinOne(_invoices).Build();
		AssertSameJoined(_orders.Query().InnerJoinOne(_invoices).Execute(), prepared.Execute(), Invoice);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().InnerJoinOne(_invoices).Count()));
	}

	// ── Right-side filters ────────────────────────────────────────────────────────

	[Test]
	public void JoinOne_WithRightFilter_Bound_LikeEager() {
		var prepared = _orders.Prepare().JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Build();
		AssertSameJoined(_orders.Query().JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Execute(), prepared.Execute(), Customer);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Count()));
	}

	[Test]
	public void JoinOne_WithRightFilterAndArg_LikeEager() {
		const string region = "US";
		var prepared = _orders.Prepare<int, PqOrder, int>()
			.UseIndex(_byProduct, static p => p)
			.JoinOne(_byCustomer, _customers, static (q, r) => q.Where(c => c.Region == r), region)
			.Build();
		for (var product = 0; product < Products; product++) {
			AssertSameJoined(
				_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers, static (q, r) => q.Where(c => c.Region == r), region).Execute(),
				prepared.Execute(product), Customer);
			Assert.That(prepared.Count(product), Is.EqualTo(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers, static (q, r) => q.Where(c => c.Region == r), region).Count()));
		}
	}

	// ── Chained ───────────────────────────────────────────────────────────────────

	[Test]
	public void JoinOne_Chained_CustomerThenProduct_LikeEager() {
		var prepared = _orders.Prepare().JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Build();
		AssertSameJoined(_orders.Query().JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Execute(), prepared.Execute(), CustomerProduct);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Count()));

		var narrowed = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).Where(static o => o.Qty < 9).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Build();
		for (var customer = 0; customer < Customers; customer++) {
			AssertSameJoined(
				_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty < 9).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).ExecutePooled(),
				narrowed.ExecutePooled(customer), CustomerProduct);
			Assert.That(narrowed.Count(customer), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty < 9).JoinOne(_byCustomer, _customers).JoinOne(_byProduct, _products).Count()));
		}
	}

	// ── JoinMany ──────────────────────────────────────────────────────────────────

	[Test]
	public void JoinMany_Outer_NoNarrowing_LikeEager() {
		var prepared = _orders.Prepare().JoinMany(_lines, _lineByOrder).Build();
		AssertSameJoined(_orders.Query().JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(), Lines);
		AssertSameJoined(_orders.Query().JoinMany(_lines, _lineByOrder).ExecutePooled(), prepared.ExecutePooled(), Lines);
		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().JoinMany(_lines, _lineByOrder).Count()));
	}

	[Test]
	public void JoinMany_AfterNarrower_Parameterized_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).Where(static o => o.Qty != 7).JoinMany(_lines, _lineByOrder).Build();
		for (var customer = 0; customer < Customers; customer++) {
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty != 7).JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(customer), Lines);
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty != 7).JoinMany(_lines, _lineByOrder).ExecutePooled(1, 4), prepared.ExecutePooled(customer, 1, 4), Lines);
			Assert.That(prepared.Count(customer), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, customer).Where(static o => o.Qty != 7).JoinMany(_lines, _lineByOrder).Count()));
		}
	}

	[Test]
	public void JoinOne_Then_JoinMany_Chained_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Build();
		for (var product = 0; product < Products; product++) {
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(product), CustomerLines);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).ExecutePooled(), prepared.ExecutePooled(product), CustomerLines);
			Assert.That(prepared.Count(product), Is.EqualTo(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Count()));
		}
	}

	// ── Sorted ────────────────────────────────────────────────────────────────────

	private static readonly (int skip, int take)[] Pages = [(0, 5), (7, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue)];

	[Test]
	public void Sort_BeforeJoinOne_EveryPage_LikeEager() {
		var prepared = _orders.Prepare().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Build();
		foreach (var (skip, take) in Pages) {
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Execute(skip, take), prepared.Execute(skip, take), Customer);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers).ExecutePooled(skip, take), prepared.ExecutePooled(skip, take), Customer);
		}

		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void SortBounded_BeforeJoinOne_Parameterized_EveryPage_LikeEager() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Build();
		foreach (var (skip, take) in Pages)
			for (var product = 0; product < Products; product++) {
				AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Execute(skip, take), prepared.Execute(product, skip, take), Customer);
				AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).ExecutePooled(skip, take), prepared.ExecutePooled(product, skip, take), Customer);
			}

		for (var product = 0; product < Products; product++)
			Assert.That(prepared.Count(product), Is.EqualTo(_orders.Query().UseIndex(_byProduct, product).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Count()));
	}

	[Test]
	public void SortBounded_BeforeInnerJoinOneAndJoinMany_EveryPage_LikeEager() {
		var prepared = _orders.Prepare().Where(static o => o.Qty > 2).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Build();
		foreach (var (skip, take) in Pages)
			AssertSameJoined(
				_orders.Query().Where(static o => o.Qty > 2).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).ExecutePooled(skip, take),
				prepared.ExecutePooled(skip, take), CustomerLines);

		Assert.That(prepared.Count(), Is.EqualTo(_orders.Query().Where(static o => o.Qty > 2).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Count()));
	}

	[Test]
	public void SortBounded_ManyTies_BeforeJoinOne_PagesMatchEager_AndConcatenateToTheWhole() {
		var prepared = _orders.Prepare().SortBounded(new ByCustomer()).JoinOne(_byCustomer, _customers).Build();
		foreach (var (skip, take) in new[] { (0, 8), (8, 8), (33, 8), (60, 40), (110, 20) })
			AssertSameJoined(_orders.Query().SortBounded(new ByCustomer()).JoinOne(_byCustomer, _customers).ExecutePooled(skip, take), prepared.ExecutePooled(skip, take), Customer);

		using var whole = _orders.Query().SortBounded(new ByCustomer()).JoinOne(_byCustomer, _customers).ExecutePooled(0, N);
		var expected = new string[whole.Count];
		for (var i = 0; i < whole.Count; i++) expected[i] = Customer(whole[i]);

		var paged = new List<string>();
		for (var skip = 0; skip < N; skip += 8) {
			using var page = prepared.ExecutePooled(skip, 8);
			for (var i = 0; i < page.Count; i++) paged.Add(Customer(page[i]));
		}

		Assert.That(paged.ToArray(), Is.EqualTo(expected));
	}

	// Sorter after the join, over the joined row: the classic joined pipeline on both sides.
	[Test]
	public void Sort_AfterJoinOne_OverJoinedRow_LikeEager() {
		var cmp = new ByRegionThenIdDesc();
		var prepared = _orders.Prepare<int, PqOrder, (int n, string s)>().UseIndex(_byProduct, static p => p.n).JoinOne(_byCustomer, _customers).Sort(cmp).Build();
		var bounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_byCustomer, _customers).SortBounded(cmp).Build();
		foreach (var (skip, take) in Pages)
			for (var product = 0; product < Products; product++) {
				AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).Sort(cmp).Execute(skip, take), prepared.Execute(( n: product, s: string.Empty), skip, take), Customer);
				AssertSameJoined(_orders.Query().UseIndex(_byProduct, product).JoinOne(_byCustomer, _customers).SortBounded(cmp).ExecutePooled(skip, take), bounded.ExecutePooled(product, skip, take), Customer);
			}

		Assert.That(prepared.Count((n: 1, s: string.Empty)), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers).Sort(cmp).Count()));
		Assert.That(bounded.Count(1), Is.EqualTo(_orders.Query().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers).SortBounded(cmp).Count()));
	}

	// ── Reuse ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Reuse_ThreeArgs_ThenMutateLeftAndRight_TracksTheLiveCaches() {
		var prepared = _orders.Prepare<int, PqOrder, (int customer, int minQty)>()
			.UseIndex(_byCustomer, static a => a.customer)
			.JoinOne(_byProduct, _products)
			.JoinMany(_lines, _lineByOrder)
			.Build();
		var runs = new[] { (customer: 0, minQty: 0), (customer: 3, minQty: 5), (customer: 8, minQty: 0) };

		foreach (var args in runs) {
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, args.customer).JoinOne(_byProduct, _products).JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(args), ProductLines);
			Assert.That(prepared.Count(args), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, args.customer).JoinOne(_byProduct, _products).JoinMany(_lines, _lineByOrder).Count()));
		}

		// Left: drop one, move one into customer 3, add one. Right: a product vanishes, one appears,
		// lines are removed from one order and added to another.
		_orders.Remove(3);
		_orders.AddOrUpdate(10, new PqOrder { Id = 10, CustomerId = 3, ProductId = 5, Qty = 12 });
		_orders.AddOrUpdate(N + 1, new PqOrder { Id = N + 1, CustomerId = 8, ProductId = 0, Qty = 1 });
		_products.Remove(0);
		_products.AddOrUpdate(5, new PqProduct { Id = 5, Category = "new" });
		_lines.Remove(1000 + 13 * 4);
		_lines.AddOrUpdate(9001, new PqLine { Id = 9001, OrderId = 0 });
		_lines.AddOrUpdate(9002, new PqLine { Id = 9002, OrderId = N + 1 });

		foreach (var args in runs) {
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, args.customer).JoinOne(_byProduct, _products).JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(args), ProductLines);
			AssertSameJoined(_orders.Query().UseIndex(_byCustomer, args.customer).JoinOne(_byProduct, _products).JoinMany(_lines, _lineByOrder).ExecutePooled(), prepared.ExecutePooled(args), ProductLines);
			Assert.That(prepared.Count(args), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, args.customer).JoinOne(_byProduct, _products).JoinMany(_lines, _lineByOrder).Count()));
		}
	}

	private static string ProductLines(JoinResult<PqOrder, PqProduct?, QueryResults<PqLine>> r)
		=> $"{(r.Right is null ? "-" : r.Right.Id)}|" + Many(r.Left.Id, r.Right2);

	// ── Leaks ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Pooled_JoinOneAndJoinMany_Parameterized_Balanced() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Build();
		LeakAssert.Balanced(() => {
			using var rows = prepared.ExecutePooled(2);
			Assert.That(rows.Count, Is.EqualTo(N / Customers));
			using var empty = prepared.ExecutePooled(9); // customer 9 does not exist: inner drops every left
			Assert.That(empty.Count, Is.Zero);
			Assert.That(prepared.Count(2), Is.EqualTo(N / Customers));
		});
	}

	[Test]
	public void Pooled_SortBoundedJoinOne_Balanced() {
		var prepared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).Build();
		LeakAssert.Balanced(() => {
			using var page = prepared.ExecutePooled(1, 2, 5);
			Assert.That(page.Count, Is.EqualTo(5));
		});
	}

	[Test]
	public void ThrowingSelector_OnJoinedChain_Propagates_LeavesNoRentedArrays_AndCommandStaysUsable() {
		var prepared = _orders.Prepare<int, PqOrder, int>()
			.UseIndex(_byCustomer, 2)
			.UseIndex(_byProduct, static p => p < 0 ? throw new InvalidOperationException("boom") : p)
			.InnerJoinOne(_byCustomer, _customers)
			.JoinMany(_lines, _lineByOrder)
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => prepared.ExecutePooled(-1).Dispose());
			Assert.Throws<InvalidOperationException>(() => prepared.Count(-1));
		});

		AssertSameJoined(_orders.Query().UseIndex(_byCustomer, 2).UseIndex(_byProduct, 2).InnerJoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).Execute(), prepared.Execute(2), CustomerLines);
	}

	// A right-side filter that throws runs inside the joined pipeline, after the replayed candidates
	// were rented: ReleaseUnconsumedCandidates must return them exactly once.
	[Test]
	public void ThrowingRightFilter_OnJoinedChain_LeavesNoRentedArrays() {
		var prepared = _orders.Prepare().UseIndex(_byCustomer, 2).JoinOne(_byCustomer, _customers, static q => throw new InvalidOperationException("hostile filter")).Build();
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => prepared.ExecutePooled().Dispose());
		});
	}
}

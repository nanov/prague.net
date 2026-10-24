namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static FrozenPipelineJoinManyTests;
using static PreparedQueryJoinDifferentialTests;

// CountJoin / InnerCountJoin through the frozen pipeline: the count resolver is admitted as a JoinMany
// (IsMany), fused in the fill walk when it has no filter callback, its own execute after the pass
// otherwise. Pinned: eager == Build() == BuildFrozen() on Execute / ExecutePooled / Count for the four
// families (right-list, left-symmetric, collection reverse, collection forward — whose dangling tag
// references 10 and 11 are not counted), outer and inner, unfiltered and filtered; chains with JoinOne on
// either side and with a JoinMany; Sort before and after (over the count) byte-identical on every page.
// Model: 120 orders, CustomerId = Id % 10 (customers 0..7 exist), ProductId = Id % 6, Qty = Id % 13;
// Id % 4 lines per order; customers 0..7 hold Id % 3 notes; 60 documents in four groups carrying two or
// three of twelve tag ids (tags 10 and 11 missing, every fifth document untagged); invoices for two orders in three.
[TestFixture]
public class FrozenPipelineCountJoinTests {
	private const int N = 120;
	private const int Customers = 10;
	private const int Products = 6;
	private const int Docs = 60;
	private const int TagIds = 12;

	private static readonly (int skip, int take)[] Pages = [(0, 5), (3, 10), (300, 5), (0, int.MaxValue), (0, 0), (2, 100)];

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private InMemoryDataCache<int, PqInvoice> _invoices = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByOrder = null!;
	private InMemoryDataCache<int, PqNote> _notes = null!;
	private CacheKeyValueListIndex<int, PqNote, int> _noteByCustomer = null!;
	private InMemoryDataCache<int, PqDoc> _docs = null!;
	private CacheKeyValueListIndex<int, PqDoc, int> _docByGroup = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, PqDoc, int> _docTags = null!;
	private InMemoryDataCache<int, PqTag> _tags = null!;
	private CacheKeyValueListIndex<int, PqTag, int> _tagByGroup = null!;

	private readonly struct ByQtyThenId : IComparer<PqOrder> {
		public int Compare(PqOrder? x, PqOrder? y) {
			var c = (x?.Qty ?? 0).CompareTo(y?.Qty ?? 0);
			return c != 0 ? c : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
		}
	}

	// A total comparer over the joined row: the count, then the id descending.
	private sealed class ByCountThenIdDesc : IComparer<JoinResult<PqOrder, int>> {
		public int Compare(JoinResult<PqOrder, int> x, JoinResult<PqOrder, int> y) {
			var c = x.Right.CompareTo(y.Right);
			return c != 0 ? c : y.Left.Id.CompareTo(x.Left.Id);
		}
	}

	[SetUp]
	public void SetUp() {
		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_invoices = new InMemoryDataCache<int, PqInvoice>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		_notes = new InMemoryDataCache<int, PqNote>();
		_noteByCustomer = _notes.CacheKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_docs = new InMemoryDataCache<int, PqDoc>();
		_docByGroup = _docs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_docTags = _docs.CacheCollectionSymmetricKeyValueListIndex<int>(static (_, v) => v.TagIds);
		_tags = new InMemoryDataCache<int, PqTag>();
		_tagByGroup = _tags.CacheKeyValueListIndex<int>(static (_, v) => v.Group);

		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, new PqOrder { Id = i, CustomerId = i % Customers, ProductId = i % Products, Qty = i % 13 });
			for (var k = 0; k < i % 4; k++)
				_lines.AddOrUpdate(1000 + i * 4 + k, new PqLine { Id = 1000 + i * 4 + k, OrderId = i });
			if (i % 3 != 0)
				_invoices.AddOrUpdate(i, new PqInvoice { Id = i, Amount = 100 + i });
		}

		for (var c = 0; c < Customers - 2; c++)
			for (var k = 0; k < c % 3; k++)
				_notes.AddOrUpdate(c * 10 + k, new PqNote { Id = c * 10 + k, CustomerId = c, Text = "n" + k });

		for (var t = 0; t < TagIds - 2; t++)
			_tags.AddOrUpdate(t, new PqTag { Id = t, Group = t % 3 });
		for (var d = 0; d < Docs; d++)
			_docs.AddOrUpdate(d, new PqDoc { Id = d, Group = d % 4, TagIds = d % 5 == 0 ? [] : d % 3 == 0 ? [d % TagIds, (d + 5) % TagIds, (d + 9) % TagIds] : [d % TagIds, (d + 5) % TagIds] });
	}

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static string Row(JoinResult<PqOrder, int> r) => r.Left.Id + ":" + r.Right;

	private static string Row(JoinResult<PqDoc, int> r) => r.Left.Id + ":" + r.Right;

	private static string Row(JoinResult<PqTag, int> r) => r.Left.Id + ":" + r.Right;

	private static string Row(JoinResult<PqOrder, PqInvoice?, int> r) => r.Left.Id + ":" + (r.Right?.Id.ToString() ?? "-") + ":" + r.Right2;

	private static string Row(JoinResult<PqOrder, int, PqInvoice?> r) => r.Left.Id + ":" + r.Right + ":" + (r.Right2?.Id.ToString() ?? "-");

	private static string Row(JoinResult<PqOrder, QueryResults<PqLine>, int> r) => r.Left.Id + ":" + r.Right.Count + ":" + r.Right2;

	private static List<string> Rows<T>(QueryResults<T> results, Func<T, string> row, bool sequence) {
		var list = new List<string>(results.Count);
		foreach (var r in results)
			list.Add(row(r));
		if (!sequence)
			list.Sort(StringComparer.Ordinal);
		return list;
	}

	// eager(pooled, skip, take), eager Count, the prepared and frozen twins of the same chain; unsorted chains compare multisets.
	private static void AssertParity<T>(Func<bool, int, int, QueryResults<T>> eager, Func<int> eagerCount, PreparedQuery<int, T> prepared, FrozenQuery<int, T> frozen,
		int p, Func<T, string> row, string label, bool sequence = false) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label + " executor");
		var expected = Rows(eager(false, 0, int.MaxValue), row, sequence);
		Assert.That(Rows(prepared.Execute(in p), row, sequence), Is.EqualTo(expected), label + " prepared");
		Assert.That(Rows(frozen.Execute(in p), row, sequence), Is.EqualTo(expected), label + " frozen");
		using (var pooled = frozen.ExecutePooled(in p))
			Assert.That(Rows(pooled, row, sequence), Is.EqualTo(expected), label + " frozen pooled");
		using (var pooledEager = eager(true, 0, int.MaxValue))
			Assert.That(Rows(pooledEager, row, sequence), Is.EqualTo(expected), label + " eager pooled");
		Assert.That(eagerCount(), Is.EqualTo(expected.Count), label + " eager Count");
		Assert.That(prepared.Count(in p), Is.EqualTo(expected.Count), label + " prepared Count");
		Assert.That(frozen.Count(in p), Is.EqualTo(expected.Count), label + " frozen Count");
		if (!sequence)
			return;
		foreach (var (skip, take) in Pages)
			Assert.That(Rows(frozen.Execute(in p, skip, take), row, true), Is.EqualTo(Rows(eager(false, skip, take), row, true)), label + " page " + skip + "/" + take);
	}

	// ── Families: outer and inner, unfiltered (fused) and filtered ──────────────

	[Test]
	public void RightList_OuterInner_UnfilteredFused_Filtered_LikeEagerAndPrepared() {
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).CountJoin(_lines, _lineByOrder);
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerCountJoin(_lines, _lineByOrder);
		var filtered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).CountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0));
		var innerFiltered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0));
		var selector = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).CountJoin(static id => id - id % 2, _lines, _lineByOrder);
		var (outerP, outerF) = (outer.Build(), outer.BuildFrozen());
		var (innerP, innerF) = (inner.Build(), inner.BuildFrozen());
		var (filteredP, filteredF) = (filtered.Build(), filtered.BuildFrozen());
		var (innerFilteredP, innerFilteredF) = (innerFiltered.Build(), innerFiltered.BuildFrozen());
		var (selectorP, selectorF) = (selector.Build(), selector.BuildFrozen());

		Assert.That(outerF.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"));
		Assert.That(innerF.Explain(), Does.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"));
		Assert.That(filteredF.Explain(), Does.Contain("joins: 1 (fused: 0, unfused: 1, many: 1"), "a filter callback keeps the fan-out");
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).Count(), outerP, outerF, p, Row, "outer " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).Count(), innerP, innerF, p, Row, "inner " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Count(), filteredP, filteredF, p, Row, "filtered " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Count(), innerFilteredP, innerFilteredF, p, Row, "inner filtered " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).CountJoin(static id => id - id % 2, _lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).CountJoin(static id => id - id % 2, _lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).CountJoin(static id => id - id % 2, _lines, _lineByOrder).Count(), selectorP, selectorF, p, Row, "selector " + p);
		}
	}

	[Test]
	public void LeftSym_And_Collections_OuterInner_Filtered_LikeEagerAndPrepared() {
		var leftSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerCountJoin(_byCustomer, _notes, _noteByCustomer);
		var leftSymFiltered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).CountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1));
		var forward = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).CountJoinCollectionForward(_tags, _docTags);
		var forwardInner = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).InnerCountJoinCollectionForward(_tags, _docTags);
		var forwardFiltered = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).CountJoinCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1));
		var reverse = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).CountJoinCollection(_docs, _docTags);
		var reverseInner = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).InnerCountJoinCollection(_docs, _docTags, static q => q.Where(static d => d.Group == 2));
		var (leftSymP, leftSymF) = (leftSym.Build(), leftSym.BuildFrozen());
		var (leftSymFilteredP, leftSymFilteredF) = (leftSymFiltered.Build(), leftSymFiltered.BuildFrozen());
		var (forwardP, forwardF) = (forward.Build(), forward.BuildFrozen());
		var (forwardInnerP, forwardInnerF) = (forwardInner.Build(), forwardInner.BuildFrozen());
		var (forwardFilteredP, forwardFilteredF) = (forwardFiltered.Build(), forwardFiltered.BuildFrozen());
		var (reverseP, reverseF) = (reverse.Build(), reverse.BuildFrozen());
		var (reverseInnerP, reverseInnerF) = (reverseInner.Build(), reverseInner.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_byCustomer, _notes, _noteByCustomer).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_byCustomer, _notes, _noteByCustomer).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_byCustomer, _notes, _noteByCustomer).Count(), leftSymP, leftSymF, p, Row, "left-sym inner " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).CountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1)).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).CountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1)).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).CountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1)).Count(), leftSymFilteredP, leftSymFilteredF, p, Row, "left-sym filtered " + p);
		}

		foreach (var g in new[] { 0, 1, 2, 3 }) {
			AssertParity((pool, s, t) => pool ? _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags).ExecutePooled(s, t) : _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags).Execute(s, t),
				() => _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags).Count(), forwardP, forwardF, g, Row, "forward " + g);
			AssertParity((pool, s, t) => pool ? _docs.Query().UseIndex(_docByGroup, g).InnerCountJoinCollectionForward(_tags, _docTags).ExecutePooled(s, t) : _docs.Query().UseIndex(_docByGroup, g).InnerCountJoinCollectionForward(_tags, _docTags).Execute(s, t),
				() => _docs.Query().UseIndex(_docByGroup, g).InnerCountJoinCollectionForward(_tags, _docTags).Count(), forwardInnerP, forwardInnerF, g, Row, "forward inner " + g);
			AssertParity((pool, s, t) => pool ? _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1)).ExecutePooled(s, t) : _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1)).Execute(s, t),
				() => _docs.Query().UseIndex(_docByGroup, g).CountJoinCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1)).Count(), forwardFilteredP, forwardFilteredF, g, Row, "forward filtered " + g);
			// The forward count never includes the dangling tag references (10, 11): it equals JoinMany's right count.
			foreach (var r in forwardF.Execute(in g))
				Assert.That(r.Right, Is.EqualTo(_docs.Query().Where(d => d.Id == r.Left.Id).JoinManyCollectionForward(_tags, _docTags).Execute()[0].Right.Count), "forward residency " + r.Left.Id);
		}

		foreach (var g in new[] { 0, 1, 2 }) {
			AssertParity((pool, s, t) => pool ? _tags.Query().UseIndex(_tagByGroup, g).CountJoinCollection(_docs, _docTags).ExecutePooled(s, t) : _tags.Query().UseIndex(_tagByGroup, g).CountJoinCollection(_docs, _docTags).Execute(s, t),
				() => _tags.Query().UseIndex(_tagByGroup, g).CountJoinCollection(_docs, _docTags).Count(), reverseP, reverseF, g, Row, "reverse " + g);
			AssertParity((pool, s, t) => pool ? _tags.Query().UseIndex(_tagByGroup, g).InnerCountJoinCollection(_docs, _docTags, static q => q.Where(static d => d.Group == 2)).ExecutePooled(s, t) : _tags.Query().UseIndex(_tagByGroup, g).InnerCountJoinCollection(_docs, _docTags, static q => q.Where(static d => d.Group == 2)).Execute(s, t),
				() => _tags.Query().UseIndex(_tagByGroup, g).InnerCountJoinCollection(_docs, _docTags, static q => q.Where(static d => d.Group == 2)).Count(), reverseInnerP, reverseInnerF, g, Row, "reverse inner filtered " + g);
		}
	}

	// ── Chains: JoinOne on either side, a JoinMany before ────────────────────────

	[Test]
	public void Chained_JoinOne_Before_And_After_JoinMany_Before_LikeEagerAndPrepared() {
		var oneThenCount = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_invoices).CountJoin(_lines, _lineByOrder);
		var innerOneThenInnerCount = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_invoices).InnerCountJoin(_lines, _lineByOrder);
		var innerCountThenOne = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerCountJoin(_lines, _lineByOrder).JoinOne(_invoices);
		var manyThenInnerCount = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).InnerCountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 0));
		var (oneThenCountP, oneThenCountF) = (oneThenCount.Build(), oneThenCount.BuildFrozen());
		var (innerOneThenInnerCountP, innerOneThenInnerCountF) = (innerOneThenInnerCount.Build(), innerOneThenInnerCount.BuildFrozen());
		var (innerCountThenOneP, innerCountThenOneF) = (innerCountThenOne.Build(), innerCountThenOne.BuildFrozen());
		var (manyThenInnerCountP, manyThenInnerCountF) = (manyThenInnerCount.Build(), manyThenInnerCount.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).CountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).CountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).CountJoin(_lines, _lineByOrder).Count(), oneThenCountP, oneThenCountF, p, Row, "one then count " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).InnerCountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).InnerCountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).InnerCountJoin(_lines, _lineByOrder).Count(), innerOneThenInnerCountP, innerOneThenInnerCountF, p, Row, "inner one then inner count " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).JoinOne(_invoices).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).JoinOne(_invoices).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder).JoinOne(_invoices).Count(), innerCountThenOneP, innerCountThenOneF, p, Row, "inner count then one " + p);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerCountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 0)).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerCountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 0)).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerCountJoin(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 0)).Count(), manyThenInnerCountP, manyThenInnerCountF, p, Row, "many then inner count " + p);
		}
	}

	// ── Sort before and after (over the count): byte-identical on every page ─────

	[Test]
	public void Sort_Before_And_After_CountJoin_ByteIdentical_EveryPage() {
		var before = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).CountJoin(_lines, _lineByOrder);
		var beforeInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).InnerCountJoin(_lines, _lineByOrder);
		var after = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).CountJoin(_lines, _lineByOrder).Sort(new ByCountThenIdDesc());
		var afterInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 1)).Sort(new ByCountThenIdDesc());
		var (beforeP, beforeF) = (before.Build(), before.BuildFrozen());
		var (beforeInnerP, beforeInnerF) = (beforeInner.Build(), beforeInner.BuildFrozen());
		var (afterP, afterF) = (after.Build(), after.BuildFrozen());
		var (afterInnerP, afterInnerF) = (afterInner.Build(), afterInner.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).CountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).CountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).CountJoin(_lines, _lineByOrder).Count(), beforeP, beforeF, p, Row, "sort before " + p, sequence: true);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerCountJoin(_lines, _lineByOrder).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerCountJoin(_lines, _lineByOrder).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerCountJoin(_lines, _lineByOrder).Count(), beforeInnerP, beforeInnerF, p, Row, "sort before, inner " + p, sequence: true);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).Sort(new ByCountThenIdDesc()).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).Sort(new ByCountThenIdDesc()).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).CountJoin(_lines, _lineByOrder).Sort(new ByCountThenIdDesc()).Count(), afterP, afterF, p, Row, "sort after " + p, sequence: true);
			AssertParity((pool, s, t) => pool ? _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 1)).Sort(new ByCountThenIdDesc()).ExecutePooled(s, t) : _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 1)).Sort(new ByCountThenIdDesc()).Execute(s, t),
				() => _orders.Query().UseIndex(_byProduct, p).InnerCountJoin(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 1)).Sort(new ByCountThenIdDesc()).Count(), afterInnerP, afterInnerF, p, Row, "sort after, inner filtered " + p, sequence: true);
		}
	}
}

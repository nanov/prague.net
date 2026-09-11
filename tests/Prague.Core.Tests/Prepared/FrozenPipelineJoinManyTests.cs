namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using Prague.Core.TypeSystem;
using static PreparedQueryJoinDifferentialTests;

// BuildFrozen() stage 3, step 8: JoinMany chains take the joined pipeline (design §7.2 as implemented). A
// JoinMany is never fused — its rows are slots of one shared buffer the fan-out partitions once every
// left's right count is known — but the chain is admitted: the narrowing is the pipeline pass, and the
// resolver's own two-pass fan-out runs afterwards over the rows the pass formed (an outer one in the
// ordinary post-pass walk, an inner one in the fill walk, which then drops the lefts whose slot stayed
// empty — not emitted, not counted, eager's RetainNonEmptyManySlots). Pinned here: (a) the three families
// (right-list, left-symmetric, collection forward and reverse), identity / selector / filter callback,
// outer and inner, unsorted — same rows, same Count / TotalCount / Truncated as eager and prepared on
// every Execute* variant × page, pages partitioning the frozen whole (an unsorted sequence is not a
// contract), with clone identity of the left and every right; (b) lefts with zero, one and many rights,
// rights shared across lefts (a non-injective selector, a shared left-symmetric bucket, overlapping
// collection buckets: the fan-out's chain path); (c) chains with fused JoinOnes and two JoinManys;
// (d) Sort before / after the join with a total comparer and SortBounded pages — byte-identical; an inner
// JoinMany under SortBounded takes the classic flow, as eager's AllInnerNarrowable gate does; (e) executor
// selection and Explain; (f) rented arrays balanced under a throwing filter callback / selector /
// predicate / comparer / Clone() with eager twins; (g) 8 readers against a writer churning the rights.
// Model: 120 orders, CustomerId = Id % 10 (customers 0..7 exist), ProductId = Id % 6, Qty = Id % 13;
// Id % 4 lines per order (zero / one / two / three); customers 0..7 hold Id % 3 notes (0, 1, 2); 60
// documents in four groups carrying two or three of twelve tag ids (tags 10 and 11 missing, every fifth
// document untagged); invoices by order id for two orders in three.
[TestFixture]
public class FrozenPipelineJoinManyTests {
	internal sealed class PqNote : ICacheEquatable<PqNote>, ICacheClonable<PqNote> {
		public int Id { get; init; }
		public int CustomerId { get; init; }
		public string Text { get; init; } = "";

		public bool CacheEquals(PqNote? other) => other is not null && other.Id == Id && other.CustomerId == CustomerId && other.Text == Text;

		public int CacheGetHashCode() => HashCode.Combine(Id, CustomerId, Text);

		public PqNote Clone() => new() { Id = Id, CustomerId = CustomerId, Text = Text };
	}

	internal sealed class PqDoc : ICacheEquatable<PqDoc>, ICacheClonable<PqDoc> {
		public int Id { get; init; }
		public int Group { get; init; }
		public List<int> TagIds { get; init; } = [];

		public bool CacheEquals(PqDoc? other) => other is not null && other.Id == Id && other.Group == Group && other.TagIds.SequenceEqual(TagIds);

		public int CacheGetHashCode() => HashCode.Combine(Id, Group, TagIds.Count);

		public PqDoc Clone() => new() { Id = Id, Group = Group, TagIds = [.. TagIds] };
	}

	internal sealed class PqTag : ICacheEquatable<PqTag>, ICacheClonable<PqTag> {
		public int Id { get; init; }
		public int Group { get; init; }

		public bool CacheEquals(PqTag? other) => other is not null && other.Id == Id && other.Group == Group;

		public int CacheGetHashCode() => HashCode.Combine(Id, Group);

		public PqTag Clone() => new() { Id = Id, Group = Group };
	}

	private sealed class PqBombLine : ICacheEquatable<PqBombLine>, ICacheClonable<PqBombLine> {
		public int Id { get; init; }
		public int OrderId { get; init; }
		public bool CacheEquals(PqBombLine? other) => other is not null && other.Id == Id && other.OrderId == OrderId;
		public int CacheGetHashCode() => HashCode.Combine(Id, OrderId);
		// Order 117 (customer 7, Qty 0: first under ByQtyThenId) has one line; it is on every page of customer 7.
		public PqBombLine Clone() => OrderId == 117 ? throw new InvalidOperationException("clone boom") : new PqBombLine { Id = Id, OrderId = OrderId };
	}

	private const int N = 120;
	private const int Customers = 10;
	private const int Products = 6;
	private const int Docs = 60;
	private const int TagIds = 12;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];

	private static readonly (int skip, int take)[] Pages = [(0, 5), (3, 10), (300, 5), (0, int.MaxValue), (3, int.MaxValue), (0, 0), (0, 1), (2, 100)];
	private static readonly FrozenOptions NoPipeline = new() { Pipeline = false };

	private InMemoryDataCache<int, PqOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqOrder, int> _byProduct = null!;
	private CacheKeyValueListIndex<int, PqOrder, int> _byQty = null!;
	private InMemoryDataCache<int, PqInvoice> _invoices = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqLine, int> _lineByOrder = null!;
	private InMemoryDataCache<int, PqNote> _notes = null!;
	private CacheKeyValueListIndex<int, PqNote, int> _noteByCustomer = null!;
	private CacheKeyValueListIndex<int, PqNote, int> _noteByCode = null!;
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

	private sealed class ByLineCountThenIdDescOverInvoice : IComparer<JoinResult<PqOrder, PqInvoice?>> {
		public int Compare(JoinResult<PqOrder, PqInvoice?> x, JoinResult<PqOrder, PqInvoice?> y) => y.Left.Id.CompareTo(x.Left.Id);
	}

	// A total post-join comparer over the joined row: the line count, then the id descending.
	private sealed class ByLineCountThenIdDesc : IComparer<JoinResult<PqOrder, QueryResults<PqLine>>> {
		public int Compare(JoinResult<PqOrder, QueryResults<PqLine>> x, JoinResult<PqOrder, QueryResults<PqLine>> y) {
			var c = x.Right.Count.CompareTo(y.Right.Count);
			return c != 0 ? c : y.Left.Id.CompareTo(x.Left.Id);
		}
	}

	[SetUp]
	public void SetUp() {
		_orders = new InMemoryDataCache<int, PqOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_byProduct = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_byQty = _orders.CacheKeyValueListIndex<int>(static (_, v) => v.Qty);
		_invoices = new InMemoryDataCache<int, PqInvoice>();
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_lines = new InMemoryDataCache<int, PqLine>();
		_lineByOrder = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		_notes = new InMemoryDataCache<int, PqNote>();
		_noteByCustomer = _notes.CacheKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_noteByCode = _notes.CacheKeyValueListIndex<int>(static (_, v) => 100 + v.CustomerId);
		_docs = new InMemoryDataCache<int, PqDoc>();
		_docByGroup = _docs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_docTags = _docs.CacheCollectionSymmetricKeyValueListIndex<int>(static (_, v) => v.TagIds);
		_tags = new InMemoryDataCache<int, PqTag>();
		_tagByGroup = _tags.CacheKeyValueListIndex<int>(static (_, v) => v.Group);

		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, MakeOrder(i));
			for (var k = 0; k < i % 4; k++)
				_lines.AddOrUpdate(1000 + i * 4 + k, new PqLine { Id = 1000 + i * 4 + k, OrderId = i });
			if (i % 3 != 0)
				_invoices.AddOrUpdate(i, new PqInvoice { Id = i, Amount = 100 + i });
		}

		for (var c = 0; c < Customers - 2; c++) {
			_customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
			for (var k = 0; k < c % 3; k++)
				_notes.AddOrUpdate(c * 10 + k, new PqNote { Id = c * 10 + k, CustomerId = c, Text = "n" + k });
		}

		for (var t = 0; t < TagIds - 2; t++)
			_tags.AddOrUpdate(t, new PqTag { Id = t, Group = t % 3 });
		for (var d = 0; d < Docs; d++)
			_docs.AddOrUpdate(d, MakeDoc(d));
	}

	private static PqOrder MakeOrder(int i) => new() { Id = i, CustomerId = i % Customers, ProductId = i % Products, Qty = i % 13 };

	private static PqDoc MakeDoc(int d) => new() { Id = d, Group = d % 4, TagIds = d % 5 == 0 ? [] : d % 3 == 0 ? [d % TagIds, (d + 5) % TagIds, (d + 9) % TagIds] : [d % TagIds, (d + 5) % TagIds] };

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

	// A slot's ids in slot order (the strict spelling) or sorted (the set spelling: a slot is an unordered set).
	private static string Slot<TRight>(QueryResults<TRight> rights, Func<TRight, int> id, bool sorted) {
		var ids = new int[rights.Count];
		for (var i = 0; i < ids.Length; i++) ids[i] = id(rights[i]);
		if (sorted)
			Array.Sort(ids);
		return string.Join(',', ids);
	}

	private static string Lines(JoinResult<PqOrder, QueryResults<PqLine>> r) => r.Left.Id + "|" + Slot(r.Right, static l => l.Id, sorted: true);
	private static string LinesStrict(JoinResult<PqOrder, QueryResults<PqLine>> r) => r.Left.Id + "|" + Slot(r.Right, static l => l.Id, sorted: false);
	private static string Notes(JoinResult<PqOrder, QueryResults<PqNote>> r) => r.Left.Id + "|" + Slot(r.Right, static n => n.Id, sorted: true);
	private static string NotesStrict(JoinResult<PqOrder, QueryResults<PqNote>> r) => r.Left.Id + "|" + Slot(r.Right, static n => n.Id, sorted: false);
	private static string DocTags(JoinResult<PqDoc, QueryResults<PqTag>> r) => r.Left.Id + "|" + Slot(r.Right, static t => t.Id, sorted: true);
	private static string DocTagsStrict(JoinResult<PqDoc, QueryResults<PqTag>> r) => r.Left.Id + "|" + Slot(r.Right, static t => t.Id, sorted: false);
	private static string TagDocs(JoinResult<PqTag, QueryResults<PqDoc>> r) => r.Left.Id + "|" + Slot(r.Right, static d => d.Id, sorted: true);
	private static string InvoiceLines(JoinResult<PqOrder, PqInvoice?, QueryResults<PqLine>> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{Slot(r.Right2, static l => l.Id, sorted: true)}";
	private static string LinesInvoice(JoinResult<PqOrder, QueryResults<PqLine>, PqInvoice?> r) => $"{r.Left.Id}|{Slot(r.Right, static l => l.Id, sorted: true)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";
	private static string LinesNotes(JoinResult<PqOrder, QueryResults<PqLine>, QueryResults<PqNote>> r) => $"{r.Left.Id}|{Slot(r.Right, static l => l.Id, sorted: true)}|{Slot(r.Right2, static n => n.Id, sorted: true)}";

	private static string[] Rows<TResult>(QueryResults<TResult> rows, Func<TResult, string> row, bool sort) {
		var strings = new string[rows.Count];
		for (var i = 0; i < strings.Length; i++) strings[i] = row(rows[i]);
		if (sort)
			Array.Sort(strings, StringComparer.Ordinal);
		return strings;
	}

	/// <summary>
	///   An unsorted page: Count / TotalCount / Truncated are eager's, the page is the frozen whole's slice
	///   (pages partition one sequence), and the whole is eager's whole as a set.
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
			Assert.That(Rows(frozen, row, sort: true), Is.EqualTo(Rows(eager, row, sort: true)).AsCollection, tag + " row set");
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>Clone identity of the left and of every right in the slot.</summary>
	private void AssertIdentity<TRight>(QueryResults<JoinResult<PqOrder, QueryResults<TRight>>> rows, bool clone, InMemoryDataCache<int, TRight> rights, Func<TRight, int> key, string tag)
		where TRight : class, ICacheEquatable<TRight>, ICacheClonable<TRight> {
		try {
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(_orders.TryGet(rows[i].Left.Id, out var left), Is.True, tag);
				Assert.That(ReferenceEquals(rows[i].Left, left), Is.EqualTo(!clone), tag + " left identity");
				var slot = rows[i].Right;
				for (var r = 0; r < slot.Count; r++) {
					Assert.That(rights.TryGet(key(slot[r]), out var cached), Is.True, tag + " the right is a cached row");
					Assert.That(ReferenceEquals(slot[r], cached), Is.EqualTo(!clone), tag + " right identity");
				}
			}
		} finally {
			rows.Dispose();
		}
	}

	/// <summary>Frozen takes the pipeline; every variant × page equals eager and prepared — the sequence for a sorted shape, the set and the page partition otherwise; Count agrees three ways.</summary>
	private static void AssertParity<TArgs, TResult>(Func<Variant, int, int, QueryResults<TResult>> eager, Func<int> eagerCount, PreparedQuery<TArgs, TResult> prepared, FrozenQuery<TArgs, TResult> frozen,
		TArgs args, Func<TResult, string> row, string label, bool sequence = false, Action<QueryResults<TResult>, bool, string>? identity = null) {
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

	// ── (a) Outer: the three families, identity / selector / filter callback, unsorted ──

	[Test]
	public void Outer_ThreeFamilies_Identity_Selector_Filtered_Unsorted_EveryVariant_EveryPage_LikeEagerAndPrepared() {
		var rightList = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder);
		// Non-injective selector: orders 2k and 2k + 1 fold onto order 2k's bucket — every right shared by two lefts.
		var rightListSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(static id => id - id % 2, _lines, _lineByOrder);
		var rightListFiltered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0));
		var leftSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_byCustomer, _notes, _noteByCustomer);
		var leftSymSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode);
		var leftSymFiltered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1));
		var forward = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).JoinManyCollectionForward(_tags, _docTags);
		var forwardFiltered = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).JoinManyCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1));
		var reverse = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).JoinManyCollection(_docs, _docTags);
		var (rightListP, rightListF) = (rightList.Build(), rightList.BuildFrozen());
		var (rightListSelP, rightListSelF) = (rightListSel.Build(), rightListSel.BuildFrozen());
		var (rightListFilteredP, rightListFilteredF) = (rightListFiltered.Build(), rightListFiltered.BuildFrozen());
		var (leftSymP, leftSymF) = (leftSym.Build(), leftSym.BuildFrozen());
		var (leftSymSelP, leftSymSelF) = (leftSymSel.Build(), leftSymSel.BuildFrozen());
		var (leftSymFilteredP, leftSymFilteredF) = (leftSymFiltered.Build(), leftSymFiltered.BuildFrozen());
		var (forwardP, forwardF) = (forward.Build(), forward.BuildFrozen());
		var (forwardFilteredP, forwardFilteredF) = (forwardFiltered.Build(), forwardFiltered.BuildFrozen());
		var (reverseP, reverseF) = (reverse.Build(), reverse.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).Count(),
				rightListP, rightListF, p, Lines, "right-list " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _lines, static l => l.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(static id => id - id % 2, _lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(static id => id - id % 2, _lines, _lineByOrder).Count(),
				rightListSelP, rightListSelF, p, Lines, "right-list selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _lines, static l => l.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Count(),
				rightListFilteredP, rightListFilteredF, p, Lines, "right-list filtered " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, _notes, _noteByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, _notes, _noteByCustomer).Count(),
				leftSymP, leftSymF, p, Notes, "left-sym " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _notes, static n => n.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode).Count(),
				leftSymSelP, leftSymSelF, p, Notes, "left-sym selector " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _notes, static n => n.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1)), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_byCustomer, _notes, _noteByCustomer, static q => q.Where(static n => n.Id % 10 == 1)).Count(),
				leftSymFilteredP, leftSymFilteredF, p, Notes, "left-sym filtered " + p);
		}

		foreach (var g in new[] { 0, 1, 2, 3 }) {
			AssertParity((v, s, t) => Eager(_docs.Query().UseIndex(_docByGroup, g).JoinManyCollectionForward(_tags, _docTags), v, s, t), () => _docs.Query().UseIndex(_docByGroup, g).JoinManyCollectionForward(_tags, _docTags).Count(),
				forwardP, forwardF, g, DocTags, "collection forward " + g);
			AssertParity((v, s, t) => Eager(_docs.Query().UseIndex(_docByGroup, g).JoinManyCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1)), v, s, t), () => _docs.Query().UseIndex(_docByGroup, g).JoinManyCollectionForward(_tags, _docTags, static q => q.Where(static t => t.Group != 1)).Count(),
				forwardFilteredP, forwardFilteredF, g, DocTags, "collection forward filtered " + g);
			if (g < 3)
				AssertParity((v, s, t) => Eager(_tags.Query().UseIndex(_tagByGroup, g).JoinManyCollection(_docs, _docTags), v, s, t), () => _tags.Query().UseIndex(_tagByGroup, g).JoinManyCollection(_docs, _docTags).Count(),
					reverseP, reverseF, g, TagDocs, "collection reverse " + g);
		}

		Assert.That(rightListF.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1").And.Not.Contain("sort:"));
		Assert.That(rightListFilteredF.Explain(), Does.Contain("joins: 1 (fused: 0, unfused: 1, many: 1"), "a filter callback keeps the fan-out");
	}

	// ── (a, b) Inner: the three families; a left without a right (or whose rights the filter rejected) is dropped and not counted ──

	[Test]
	public void Inner_ThreeFamilies_Identity_Selector_Filtered_Unsorted_DropsLeftsWithoutRights_CountMatchesEager() {
		var rightList = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder);
		var rightListFiltered = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0));
		var withWhere = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Where(static o => o.Qty > 4).InnerJoinMany(_lines, _lineByOrder);
		var leftSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_byCustomer, _notes, _noteByCustomer);
		var leftSymSel = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode);
		var forward = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).InnerJoinManyCollectionForward(_tags, _docTags);
		var reverse = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).InnerJoinManyCollection(_docs, _docTags);
		var (rightListP, rightListF) = (rightList.Build(), rightList.BuildFrozen());
		var (rightListFilteredP, rightListFilteredF) = (rightListFiltered.Build(), rightListFiltered.BuildFrozen());
		var (withWhereP, withWhereF) = (withWhere.Build(), withWhere.BuildFrozen());
		var (leftSymP, leftSymF) = (leftSym.Build(), leftSym.BuildFrozen());
		var (leftSymSelP, leftSymSelF) = (leftSymSel.Build(), leftSymSel.BuildFrozen());
		var (forwardP, forwardF) = (forward.Build(), forward.BuildFrozen());
		var (reverseP, reverseF) = (reverse.Build(), reverse.BuildFrozen());
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).Count(),
				rightListP, rightListF, p, Lines, "inner right-list " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _lines, static l => l.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).Count(),
				rightListFilteredP, rightListFilteredF, p, Lines, "inner right-list filtered " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).Where(static o => o.Qty > 4).InnerJoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).Where(static o => o.Qty > 4).InnerJoinMany(_lines, _lineByOrder).Count(),
				withWhereP, withWhereF, p, Lines, "inner right-list after a Where " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_byCustomer, _notes, _noteByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).Count(),
				leftSymP, leftSymF, p, Notes, "inner left-sym " + p, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _notes, static n => n.Id, tag));
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_byCustomer, static c => 100 + c, _notes, _noteByCode).Count(),
				leftSymSelP, leftSymSelF, p, Notes, "inner left-sym selector " + p);

			// Every inner row carries at least one right, the count is the rows, and only orders with a line survive.
			using var rows = rightListF.ExecutePooled(p);
			Assert.That(rows.Count, Is.EqualTo(rightListF.Count(p)));
			Assert.That(rows.TotalCount, Is.EqualTo(rows.Count));
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right.Count, Is.GreaterThan(0));
				Assert.That(rows[i].Left.Id % 4, Is.Not.Zero, "an order without a line is dropped");
				for (var r = 0; r < rows[i].Right.Count; r++)
					Assert.That(rows[i].Right[r].OrderId, Is.EqualTo(rows[i].Left.Id));
			}
		}

		foreach (var g in new[] { 0, 1, 2 }) {
			AssertParity((v, s, t) => Eager(_docs.Query().UseIndex(_docByGroup, g).InnerJoinManyCollectionForward(_tags, _docTags), v, s, t), () => _docs.Query().UseIndex(_docByGroup, g).InnerJoinManyCollectionForward(_tags, _docTags).Count(),
				forwardP, forwardF, g, DocTags, "inner collection forward " + g);
			AssertParity((v, s, t) => Eager(_tags.Query().UseIndex(_tagByGroup, g).InnerJoinManyCollection(_docs, _docTags), v, s, t), () => _tags.Query().UseIndex(_tagByGroup, g).InnerJoinManyCollection(_docs, _docTags).Count(),
				reverseP, reverseF, g, TagDocs, "inner collection reverse " + g);
		}

		Assert.That(rightListF.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"));
	}

	// ── (b) Zero / one / many rights, and rights shared across lefts ──

	[Test]
	public void ZeroOneManyRights_And_RightsSharedAcrossLefts_OuterKeepsEmptySlots_InnerDrops() {
		// Customer 1: orders 1, 11, 21, …, 111 — Id % 4 lines: 1, 3, 1, 3, … (never zero); customer 0: 0, 10, 20, … — 0, 2, 0, 2, … lines.
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_lines, _lineByOrder).BuildFrozen();
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(_lines, _lineByOrder).BuildFrozen();
		var shared = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(static id => id - id % 2, _lines, _lineByOrder).BuildFrozen();
		var sym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen();
		var innerSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen();
		using (var rows = outer.ExecutePooled(0)) {
			Assert.That(rows.Count, Is.EqualTo(12));
			for (var i = 0; i < rows.Count; i++)
				Assert.That(rows[i].Right.Count, Is.EqualTo(rows[i].Left.Id % 4), "zero or two lines, the slot present either way");
		}

		using (var rows = inner.ExecutePooled(0)) {
			Assert.That(rows.Count, Is.EqualTo(6), "the six orders with two lines");
			Assert.That(rows.TotalCount, Is.EqualTo(6));
			for (var i = 0; i < rows.Count; i++)
				Assert.That(rows[i].Right.Count, Is.EqualTo(2));
		}

		Assert.That(inner.Count(0), Is.EqualTo(6));
		Assert.That(outer.Count(0), Is.EqualTo(12));
		using (var rows = shared.ExecutePooled(1)) {
			// Orders 1, 11, 21, … fold onto 0, 10, 20, …: each left's slot is the even order's lines (0 or 2), the same right rows for the two lefts.
			for (var i = 0; i < rows.Count; i++) {
				var even = rows[i].Left.Id - rows[i].Left.Id % 2;
				Assert.That(rows[i].Right.Count, Is.EqualTo(even % 4), "the folded bucket's lines");
				for (var r = 0; r < rows[i].Right.Count; r++)
					Assert.That(rows[i].Right[r].OrderId, Is.EqualTo(even));
			}
		}

		using (var rows = sym.ExecutePooled(2)) {
			Assert.That(rows.Count, Is.EqualTo(12));
			for (var i = 0; i < rows.Count; i++) {
				Assert.That(rows[i].Right.Count, Is.EqualTo(2), "every order of customer 2 shares its two notes");
				Assert.That(ReferenceEquals(rows[i].Right[0], rows[0].Right[0]) || ReferenceEquals(rows[i].Right[0], rows[0].Right[1]), Is.True, "the same right rows, delivered through the chain");
			}
		}

		using (var rows = innerSym.ExecutePooled(3)) {
			Assert.That(rows.Count, Is.Zero, "customer 3 has no note: every left is dropped");
			Assert.That(rows.TotalCount, Is.Zero);
		}

		Assert.That(innerSym.Count(3), Is.Zero);
		Assert.That(innerSym.Count(2), Is.EqualTo(12));
		Assert.That(sym.Count(3), Is.EqualTo(12));
	}

	// ── (c) Chains: fused JoinOnes around a JoinMany, two JoinManys ──

	[Test]
	public void Chained_FusedJoinOne_And_JoinMany_TwoManys_OuterInnerMixes_EveryVariant_EveryPage() {
		var oneThenMany = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinOne(_invoices).JoinMany(_lines, _lineByOrder);
		var manyThenOne = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).JoinOne(_invoices);
		var innerOneThenInnerMany = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinOne(_invoices).InnerJoinMany(_lines, _lineByOrder);
		var innerManyThenInnerOne = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).InnerJoinOne(_invoices);
		var innerManyThenOuterOne = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).JoinOne(_invoices);
		var twoManys = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).JoinMany(_byCustomer, _notes, _noteByCustomer);
		var innerManyThenMany = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).InnerJoinMany(_byCustomer, _notes, _noteByCustomer);
		var (oneThenManyP, oneThenManyF) = (oneThenMany.Build(), oneThenMany.BuildFrozen());
		var (manyThenOneP, manyThenOneF) = (manyThenOne.Build(), manyThenOne.BuildFrozen());
		var (innerOneThenInnerManyP, innerOneThenInnerManyF) = (innerOneThenInnerMany.Build(), innerOneThenInnerMany.BuildFrozen());
		var (innerManyThenInnerOneP, innerManyThenInnerOneF) = (innerManyThenInnerOne.Build(), innerManyThenInnerOne.BuildFrozen());
		var (innerManyThenOuterOneP, innerManyThenOuterOneF) = (innerManyThenOuterOne.Build(), innerManyThenOuterOne.BuildFrozen());
		var (twoManysP, twoManysF) = (twoManys.Build(), twoManys.BuildFrozen());
		var (innerManyThenManyP, innerManyThenManyF) = (innerManyThenMany.Build(), innerManyThenMany.BuildFrozen());
		Assert.Multiple(() => {
			Assert.That(oneThenManyF.Explain(), Does.Contain("joins: 2 (fused: 2, unfused: 0, many: 1"));
			Assert.That(innerManyThenInnerOneF.Explain(), Does.Contain("joins: 2 (fused: 2, unfused: 0, many: 1"));
			Assert.That(twoManysF.Explain(), Does.Contain("joins: 2 (fused: 2, unfused: 0, many: 2"));
		});
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).JoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinOne(_invoices).JoinMany(_lines, _lineByOrder).Count(),
				oneThenManyP, oneThenManyF, p, InvoiceLines, "one then many " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).JoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).JoinOne(_invoices).Count(),
				manyThenOneP, manyThenOneF, p, LinesInvoice, "many then one " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).InnerJoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinOne(_invoices).InnerJoinMany(_lines, _lineByOrder).Count(),
				innerOneThenInnerManyP, innerOneThenInnerManyF, p, InvoiceLines, "inner one then inner many " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerJoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerJoinOne(_invoices).Count(),
				innerManyThenInnerOneP, innerManyThenInnerOneF, p, LinesInvoice, "inner many then inner one " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).JoinOne(_invoices), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).JoinOne(_invoices).Count(),
				innerManyThenOuterOneP, innerManyThenOuterOneF, p, LinesInvoice, "inner many then outer one " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).JoinMany(_byCustomer, _notes, _noteByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).JoinMany(_byCustomer, _notes, _noteByCustomer).Count(),
				twoManysP, twoManysF, p, LinesNotes, "two manys " + p);
			AssertParity((v, s, t) => Eager(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerJoinMany(_byCustomer, _notes, _noteByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).Count(),
				innerManyThenManyP, innerManyThenManyF, p, LinesNotes, "inner many then inner many " + p);
		}
	}

	// ── (d) Sorted with a total comparer: byte-identical. Sort before (the container sorts and crops, the fan-out fills the page) and after (over the joined row) ──

	[Test]
	public void Sort_Before_And_After_JoinMany_TotalComparer_ByteIdentical_EveryVariant_EveryPage() {
		var before = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).JoinMany(_lines, _lineByOrder);
		var beforeInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).Sort(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder);
		var after = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc());
		var afterInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc());
		var (beforeP, beforeF) = (before.Build(), before.BuildFrozen());
		var (beforeInnerP, beforeInnerF) = (beforeInner.Build(), beforeInner.BuildFrozen());
		var (afterP, afterF) = (after.Build(), after.BuildFrozen());
		var (afterInnerP, afterInnerF) = (afterInner.Build(), afterInner.BuildFrozen());
		Assert.That(beforeF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"), "a classic Sort over the left value takes the bounded flow (step 8), so the fill after it is fused over the page");
		Assert.That(afterF.Explain(), Does.Contain("sort: classic").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"), "a sorter over the joined row runs in the classic container after the fused fill");
		Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_invoices).Sort(new ByLineCountThenIdDescOverInvoice()).JoinMany(_lines, _lineByOrder).BuildFrozen().Explain(), Does.Contain("sort: classic").And.Contain("fused: 1, unfused: 1, many: 1"), "a JoinMany after a sorter over the joined row keeps its fan-out (the container crops first)");
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).JoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).JoinMany(_lines, _lineByOrder).Count(),
				beforeP, beforeF, p, LinesStrict, "sort before " + p, sequence: true, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _lines, static l => l.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).Sort(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder).Count(),
				beforeInnerP, beforeInnerF, p, LinesStrict, "sort before, inner " + p, sequence: true);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc()), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc()).Count(),
				afterP, afterF, p, LinesStrict, "sort after " + p, sequence: true);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc()), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc()).Count(),
				afterInnerP, afterInnerF, p, LinesStrict, "sort after, inner " + p, sequence: true);
		}
	}

	// ── (d) SortBounded: an outer JoinMany fills the bounded page; an inner one takes the classic flow (eager's AllInnerNarrowable gate); a fused inner JoinOne before an outer JoinMany stays bounded ──

	[Test]
	public void SortBounded_ThenJoinMany_Outer_Bounded_Inner_Classic_FusedInnerOneThenMany_ByteIdentical_PagesPartition() {
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinMany(_lines, _lineByOrder);
		var outerSym = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).JoinMany(_byCustomer, _notes, _noteByCustomer);
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder);
		var mixed = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinMany(_lines, _lineByOrder);
		var (outerP, outerF) = (outer.Build(), outer.BuildFrozen());
		var (outerSymP, outerSymF) = (outerSym.Build(), outerSym.BuildFrozen());
		var (innerP, innerF) = (inner.Build(), inner.BuildFrozen());
		var (mixedP, mixedF) = (mixed.Build(), mixed.BuildFrozen());
		Assert.Multiple(() => {
			Assert.That(outerF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"));
			Assert.That(innerF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 1 (fused: 1, unfused: 0, many: 1"), "a fused inner JoinMany narrows before the heap by a bucket probe (eager takes the classic flow; a total comparer makes the pages identical)");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id > 0)).BuildFrozen().Explain(), Does.Contain("sort: classic").And.Contain("fused: 0, unfused: 1, many: 1"), "an unfused inner JoinMany cannot: the classic flow, as eager");
			Assert.That(mixedF.Explain(), Does.Contain("sort: bounded").And.Contain("joins: 2 (fused: 2, unfused: 0, many: 1"));
		});
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinMany(_lines, _lineByOrder).Count(),
				outerP, outerF, p, LinesStrict, "bounded outer " + p, sequence: true, identity: (rows, clone, tag) => AssertIdentity(rows, clone, _lines, static l => l.Id, tag));
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinMany(_byCustomer, _notes, _noteByCustomer), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).JoinMany(_byCustomer, _notes, _noteByCustomer).Count(),
				outerSymP, outerSymF, p, NotesStrict, "bounded outer left-sym " + p, sequence: true);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinMany(_lines, _lineByOrder).Count(),
				innerP, innerF, p, LinesStrict, "bounded inner (classic) " + p, sequence: true);
			AssertParity((v, s, t) => EagerSorted(_orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinMany(_lines, _lineByOrder), v, s, t), () => _orders.Query().UseIndex(_byProduct, p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinMany(_lines, _lineByOrder).Count(),
				mixedP, mixedF, p, InvoiceLines, "bounded fused inner one then many " + p, sequence: true);

			// Consecutive pages partition the whole.
			using var whole = outerF.Execute(p);
			var seen = 0;
			foreach (var take in new[] { 1, 3, 7 })
				for (var skip = 0; skip < whole.Count; skip += take) {
					using var page = outerF.ExecutePooled(p, skip, take);
					Assert.That(page.TotalCount, Is.EqualTo(whole.Count));
					for (var i = 0; i < page.Count; i++) {
						Assert.That(LinesStrict(page[i]), Is.EqualTo(LinesStrict(whole[skip + i])), $"page {skip}/{take} row {i}");
						seen++;
					}
				}

			Assert.That(seen, Is.EqualTo(3 * whole.Count));
		}
	}

	// ── (e) Executor selection ──

	[Test]
	public void Executor_JoinManyChainsTakeThePipeline_NoSeedAndPipelineOffReplay() {
		Assert.Multiple(() => {
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "right-list");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).InnerJoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "inner right-list");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.Id % 2 == 0)).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a filter callback is the fan-out's, not the pass's: admitted");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "left-sym");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "inner left-sym (its rows are created in candidate order, no regrouping)");
			Assert.That(_docs.Prepare().UseIndex(_docByGroup, 1).JoinManyCollectionForward(_tags, _docTags).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "collection forward");
			Assert.That(_tags.Prepare().UseIndex(_tagByGroup, 1).InnerJoinManyCollection(_docs, _docTags).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "inner collection reverse");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_invoices).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "fused one then many");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "an inner left-symmetric JoinOne fuses by default");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).InnerJoinOne(_byCustomer, _customers).BuildFrozen(new FrozenOptions { PreserveEagerOrder = true }).Plan.Executor, Is.EqualTo("Replay"), "…and replays under the opt-out (it regroups)");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).Sort(new ByQtyThenId()).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "classic sort then many");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "bounded then many");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).SortBounded(new ByQtyThenId()).JoinOne(_byCustomer, _customers).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "the step-6 shape with a many");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinOne(_byCustomer, _customers, static q => q.Where(static n => n.Id > 0)).JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "a filtered JoinOne outside the step-6 shape still replays");
			Assert.That(_orders.Prepare().JoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "no seed source");
			Assert.That(_orders.Prepare().UseIndex(_byProduct, 1).JoinMany(_lines, _lineByOrder).BuildFrozen(NoPipeline).Plan.Executor, Is.EqualTo("Replay"), "pipeline off");
			Assert.That(_orders.Prepare().Or(b => b.UseIndex(_byProduct, 1), b => b.UseIndex(_byProduct, 2)).InnerJoinMany(_lines, _lineByOrder).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "a composite narrowing");
		});
	}

	// ── (f) Leaks ─────────────────────────────────────────────────────────────────

	private sealed class BombJoinedMany : IComparer<JoinResult<PqOrder, QueryResults<PqLine>>> {
		public int Compare(JoinResult<PqOrder, QueryResults<PqLine>> x, JoinResult<PqOrder, QueryResults<PqLine>> y)
			=> x.Left.CustomerId == 7 || y.Left.CustomerId == 7 ? throw new InvalidOperationException("compare boom") : x.Left.Id.CompareTo(y.Left.Id);
	}

	[Test]
	public void Throwing_FilterCallback_Selector_Predicate_Comparer_Clone_LeaveNoRentedArrays_EagerTwins() {
		var bombs = new InMemoryDataCache<int, PqBombLine>();
		var bombByOrder = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.OrderId);
		for (var i = 0; i < N; i++)
			for (var k = 0; k < i % 4; k++)
				bombs.AddOrUpdate(1000 + i * 4 + k, new PqBombLine { Id = 1000 + i * 4 + k, OrderId = i });

		// Customer 7 owns order 7, whose one line trips the join filter's predicate inside the paired execute (after the shared buffer was rented).
		var filter = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.OrderId == 7 ? throw new InvalidOperationException("filter boom") : true)).BuildFrozen();
		var innerFilter = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.OrderId == 7 ? throw new InvalidOperationException("filter boom") : true)).BuildFrozen();
		// The bounded page 0..3 of customer 7 is orders 117, 27, 67 (Qty 0, 1, 2): order 117's line trips this one.
		var boundedFilter = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).SortBounded(new ByQtyThenId()).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.OrderId == 117 ? throw new InvalidOperationException("filter boom") : true)).BuildFrozen();
		var selector = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(static id => id == 7 ? throw new InvalidOperationException("selector boom") : id, _lines, _lineByOrder).BuildFrozen();
		var predicate = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).Where(static (o, in c) => o.Id == 17 && c == 7 ? throw new InvalidOperationException("predicate boom") : true).InnerJoinMany(_lines, _lineByOrder).BuildFrozen();
		var comparer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_lines, _lineByOrder).Sort(new BombJoinedMany()).BuildFrozen();
		var cloned = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(bombs, bombByOrder).BuildFrozen();
		var clonedInner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(bombs, bombByOrder).BuildFrozen();
		var clonedBounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byCustomer, static c => c).SortBounded(new ByQtyThenId()).JoinMany(bombs, bombByOrder).BuildFrozen();
		Assert.Multiple(() => {
			foreach (var q in new FrozenQuery<int, JoinResult<PqOrder, QueryResults<PqLine>>>[] { filter, innerFilter, boundedFilter, selector, predicate, comparer })
				Assert.That(q.Plan.Executor, Is.EqualTo("Pipeline"));
			foreach (var q in new[] { cloned, clonedInner, clonedBounded })
				Assert.That(q.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => filter.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => filter.ExecutePooledCloned(7, 1, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => innerFilter.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => innerFilter.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => innerFilter.Count(7));
			Assert.Throws<InvalidOperationException>(() => boundedFilter.ExecutePooled(7, 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => boundedFilter.ExecutePooledCloned(7, 0, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => boundedFilter.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooledCloned(7, 1, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.ExecuteCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooledCloned(7, 0, 4).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.Count(7));
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooledCloned(7, 2, 3).Dispose());
			// Clone-on-add (no slice: the fan-out clones as it delivers) and clone-after-slice / after the bounded page, frozen and eager.
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecuteCloned(7, 7, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedInner.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedBounded.ExecutePooledCloned(7, 0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => clonedBounded.ExecutePooledCloned(7).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.OrderId == 7 ? throw new InvalidOperationException("filter boom") : true)).ExecutePooled().Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).InnerJoinMany(_lines, _lineByOrder, static q => q.Where(static l => l.OrderId == 7 ? throw new InvalidOperationException("filter boom") : true)).Count());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinMany(bombs, bombByOrder).ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinMany(bombs, bombByOrder).ExecutePooledCloned(7, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).SortBounded(new ByQtyThenId()).JoinMany(bombs, bombByOrder).ExecutePooledCloned(0, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => _orders.Query().UseIndex(_byCustomer, 7).JoinMany(_lines, _lineByOrder).Sort(new BombJoinedMany()).ExecutePooled().Dispose());
			// The same plans on a customer that does not trip them.
			filter.ExecutePooled(3).Dispose();
			innerFilter.ExecutePooledCloned(3, 1, 3).Dispose();
			boundedFilter.ExecutePooled(3, 0, 3).Dispose();
			selector.ExecutePooled(3).Dispose();
			predicate.ExecutePooled(3).Dispose();
			comparer.ExecutePooled(3, 0, 4).Dispose();
			cloned.ExecutePooledCloned(3).Dispose();
			clonedInner.ExecutePooledCloned(3).Dispose();
			clonedBounded.ExecutePooledCloned(3, 0, 5).Dispose();
			Assert.That(innerFilter.Count(3), Is.EqualTo(_orders.Query().UseIndex(_byCustomer, 3).InnerJoinMany(_lines, _lineByOrder).Count()));
		});
	}

	// ── (g) Concurrency ───────────────────────────────────────────────────────────

	[Test]
	public void EightReaders_AgainstAWriterChurningTheRights_AllFamilies_NeverThrow_NoDuplicates_InnerRowsCarryARight() {
		var outer = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).JoinOne(_invoices).BuildFrozen();
		var inner = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen();
		var bounded = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).SortBounded(new ByQtyThenId()).InnerJoinOne(_invoices).JoinMany(_lines, _lineByOrder).BuildFrozen();
		var sorted = _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).Sort(new ByLineCountThenIdDesc()).BuildFrozen();
		// The collection family both ways, outer and inner, while the documents' tag lists and the tags themselves churn.
		var forward = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).JoinManyCollectionForward(_tags, _docTags).BuildFrozen();
		var forwardInner = _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).InnerJoinManyCollectionForward(_tags, _docTags).BuildFrozen();
		var reverse = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).JoinManyCollection(_docs, _docTags).BuildFrozen();
		var reverseInner = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).InnerJoinManyCollection(_docs, _docTags).BuildFrozen();
		// The unfused fan-out under churn too: a filter callback keeps the eager pair set.
		var filteredInner = _tags.Prepare<int, PqTag, int>().UseIndex(_tagByGroup, static g => g).InnerJoinManyCollection(_docs, _docTags, static q => q.Where(static d => d.Id % 7 != 3)).BuildFrozen();
		Assert.Multiple(() => {
			foreach (var q in new FrozenQuery<int, JoinResult<PqOrder, QueryResults<PqLine>>>[] { sorted })
				Assert.That(q.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(outer.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(inner.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(bounded.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(forward.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(forwardInner.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(reverse.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(reverseInner.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(filteredInner.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(filteredInner.Explain(), Does.Contain("fused: 0, unfused: 1, many: 1"));
		});
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				// Lines leave and come back, notes flip, invoices churn; lefts move across products and customers.
				if (i % 3 == 0) _lines.Remove(1000 + id * 4);
				if (i % 3 == 1) _lines.AddOrUpdate(1000 + id * 4, new PqLine { Id = 1000 + id * 4, OrderId = id });
				if (i % 5 == 0) _notes.Remove(id % 10 * 10);
				if (i % 5 == 2) _notes.AddOrUpdate(id % 10 * 10, new PqNote { Id = id % 10 * 10, CustomerId = id % 10, Text = "x" });
				if (i % 7 == 0) _invoices.Remove(id);
				if (i % 7 == 3) _invoices.AddOrUpdate(id, new PqInvoice { Id = id, Amount = id });
				_orders.AddOrUpdate(id, new PqOrder { Id = id, CustomerId = (id + i) % Customers, ProductId = (id + i) % Products, Qty = i % 13 });
				if (i % 11 == 0) _orders.Remove((id * 13) % N);
				if (i % 11 == 5) _orders.AddOrUpdate((id * 13) % N, MakeOrder((id * 13) % N));
				// Documents change their tag lists (both index halves move), tags leave and come back, documents leave and come back.
				var doc = i % Docs;
				_docs.AddOrUpdate(doc, new PqDoc { Id = doc, Group = doc % 4, TagIds = i % 4 == 0 ? [] : [(doc + i) % TagIds, (doc + i + 5) % TagIds] });
				if (i % 13 == 0) _tags.Remove(i % TagIds);
				if (i % 13 == 6) _tags.AddOrUpdate(i % TagIds, new PqTag { Id = i % TagIds, Group = i % TagIds % 3 });
				if (i % 17 == 0) _docs.Remove((doc * 7) % Docs);
				if (i % 17 == 8) _docs.AddOrUpdate((doc * 7) % Docs, MakeDoc((doc * 7) % Docs));
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
							for (var l = 0; l < rows[r].Right.Count; l++)
								Assert.That(rows[r].Right[l].OrderId, Is.EqualTo(rows[r].Left.Id), "the line is the row's");
						}
					}

					using (var rows = inner.ExecutePooledCloned(product, skip, 10)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right.Count, Is.GreaterThan(0), "an inner row always carries a right");
							Assert.That(rows[r].Right2.Count, Is.GreaterThan(0), "an inner row always carries a right");
						}
					}

					using (var rows = bounded.ExecutePooled(product, skip, 5)) {
						seen.Clear();
						Assert.That(rows.Count, Is.LessThanOrEqualTo(5));
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right, Is.Not.Null, "an inner row always carries its right");
						}
					}

					using (var rows = sorted.ExecutePooled(product, skip, 6)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++)
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
					}

					Assert.That(inner.Count(product), Is.GreaterThanOrEqualTo(0));

					var group = (seed + i) % 4;
					using (var rows = forward.ExecutePooled(group)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							var tags = new HashSet<int>();
							for (var t = 0; t < rows[r].Right.Count; t++)
								Assert.That(tags.Add(rows[r].Right[t].Id), Is.True, "a tag is delivered once to a document");
						}
					}

					using (var rows = forwardInner.ExecutePooledCloned(group, skip, 8)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right.Count, Is.GreaterThan(0), "an inner row always carries a right");
						}
					}

					using (var rows = reverse.ExecutePooled(group % 3)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							var docs = new HashSet<int>();
							for (var d = 0; d < rows[r].Right.Count; d++)
								Assert.That(docs.Add(rows[r].Right[d].Id), Is.True, "a document is delivered once to a tag");
						}
					}

					using (var rows = reverseInner.ExecutePooled(group % 3, skip, 3)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right.Count, Is.GreaterThan(0), "an inner row always carries a right");
						}
					}

					using (var rows = filteredInner.ExecutePooledCloned(group % 3)) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(seen.Add(rows[r].Left.Id), Is.True, "no duplicate keys");
							Assert.That(rows[r].Right.Count, Is.GreaterThan(0), "an inner row always carries a right");
							for (var d = 0; d < rows[r].Right.Count; d++)
								Assert.That(rows[r].Right[d].Id % 7, Is.Not.EqualTo(3), "the filter callback ran");
						}
					}

					Assert.That(reverseInner.Count(group % 3), Is.GreaterThanOrEqualTo(0));
					Assert.That(forwardInner.Count(group), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
		Assert.That(QueryResultsDiagnostics.DroppedRows, Is.Zero, "no slot was under-sized");
	}

	// ── Informational: the unsorted sequence. Not a contract (an unsorted result's order is unspecified, and a
	// slot is an unordered set), kept while it holds. The rows come out in the seed's order on every family, and
	// a slot in its bucket's order — identical to eager when no right is shared across lefts (the FK shape) or
	// every left shares the whole bucket (left-symmetric). With rights shared across lefts in overlapping
	// buckets (the collection shapes, a non-injective selector) eager's fan-out interleaves a slot by the
	// order the distinct rights were first recorded ("9,0,5") where the per-left fill keeps the bucket's
	// ("0,5,9"): the same set, pinned as such in (a) / (b). ──

	[Test]
	public void Unsorted_Sequence_HappensToMatchEager_WhereNoRightIsSharedAcrossLefts_Informational() {
		foreach (var p in new[] { 0, 3, 5 }) {
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, p).JoinMany(_lines, _lineByOrder).Execute(), _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).JoinMany(_lines, _lineByOrder).BuildFrozen().Execute(p), LinesStrict);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_lines, _lineByOrder).Execute(), _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_lines, _lineByOrder).BuildFrozen().Execute(p), LinesStrict);
			AssertSameJoined(_orders.Query().UseIndex(_byProduct, p).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).Execute(), _orders.Prepare<int, PqOrder, int>().UseIndex(_byProduct, static p => p).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen().Execute(p), NotesStrict);
			// The collection shape: the rows' sequence is eager's, the slots agree as sets.
			AssertSameJoined(_docs.Query().UseIndex(_docByGroup, p % 4).InnerJoinManyCollectionForward(_tags, _docTags).Execute(), _docs.Prepare<int, PqDoc, int>().UseIndex(_docByGroup, static g => g).InnerJoinManyCollectionForward(_tags, _docTags).BuildFrozen().Execute(p % 4), DocTags);
		}
	}
}

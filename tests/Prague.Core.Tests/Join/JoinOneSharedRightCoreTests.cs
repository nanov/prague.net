namespace Prague.Core.Tests.Join;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// Several lefts on ONE right, through every JoinOne path that still seeds a pair set — a filtered outer
// join, an inner join, a filtered inner join and the bounded narrow-only inner phase. The pair set is
// keyed by the right key alone, so it holds one left per right; before JoinOneSharedRights the other
// lefts came back without the right (outer) or were dropped (inner). Families: PK-to-PK with a
// non-injective selector (SelLeft → SelRight) and right-unique with a non-injective selector
// (RusiBook → RusiBookInfo). The left-unique family is covered through codegen in
// Prague.Generated.Tests/Join/JoinOneRepeatedJoinKeyTests.cs (a OneToOne FK over repeated values).
[TestFixture]
public class JoinOneSharedRightCoreTests {
	private InMemoryDataCache<int, SelLeft> _left = null!;
	private InMemoryDataCache<long, SelRight> _right = null!;
	private InMemoryDataCache<int, RusiBook> _books = null!;
	private InMemoryDataCache<int, RusiBookInfo> _infos = null!;
	private CacheKeyValueIndex<int, RusiBookInfo, long> _infoByBookKey = null!;

	// Lefts 1..4 select right 1001 ("keep"), lefts 5..6 select right 1002 ("drop"), left 7 selects 1003 (absent).
	private static long ToRight(int id) => id <= 4 ? 1001L : id <= 6 ? 1002L : 1003L;

	[SetUp]
	public void SetUp() {
		_left = new InMemoryDataCache<int, SelLeft>();
		for (var i = 1; i <= 7; i++)
			_left.AddOrUpdate(i, new SelLeft { Id = i, Name = $"L{i}" });
		_right = new InMemoryDataCache<long, SelRight>();
		_right.AddOrUpdate(1001L, new SelRight { Id = 1001L, Label = "keep" });
		_right.AddOrUpdate(1002L, new SelRight { Id = 1002L, Label = "drop" });

		_books = new InMemoryDataCache<int, RusiBook>();
		for (var i = 1; i <= 7; i++)
			_books.AddOrUpdate(i, new RusiBook { Id = i, Title = $"B{i}" });
		_infos = new InMemoryDataCache<int, RusiBookInfo>();
		_infoByBookKey = _infos.AddKeyValueIndex<long>(static (_, v) => v.BookId);
		_infos.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 1001L, Synopsis = "keep" });
		_infos.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 1002L, Synopsis = "drop" });
	}

	private static string Describe(IEnumerable<JoinResult<SelLeft, SelRight?>> rows)
		=> string.Join(" ", rows.OrderBy(static r => r.Left.Id).Select(static r => $"{r.Left.Id}={r.Right?.Label ?? "-"}"));

	private static string Describe(IEnumerable<JoinResult<RusiBook, RusiBookInfo?>> rows)
		=> string.Join(" ", rows.OrderBy(static r => r.Left.Id).Select(static r => $"{r.Left.Id}={r.Right?.Synopsis ?? "-"}"));

	private readonly struct SelLeftById : IComparer<SelLeft> {
		public int Compare(SelLeft? x, SelLeft? y) => (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	// ── PK-to-PK + non-injective selector ─────────────────────────────────────

	[Test]
	public void PkSelector_Outer_Unfiltered_EveryLeftGetsItsRight() {
		var rows = _left.Query().JoinOne(static (int id) => ToRight(id), _right).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=drop 6=drop 7=-"));
	}

	[Test]
	public void PkSelector_Outer_Filtered_EveryLeftOfAKeptRightGetsIt() {
		var rows = _left.Query()
			.JoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep"))
			.Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=- 6=- 7=-"));
	}

	[Test]
	public void PkSelector_Inner_KeepsEveryLeftWithARight() {
		var rows = _left.Query().InnerJoinOne(static (int id) => ToRight(id), _right).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=drop 6=drop"));
		Assert.That(_left.Query().InnerJoinOne(static (int id) => ToRight(id), _right).Count(), Is.EqualTo(6));
	}

	[Test]
	public void PkSelector_InnerFiltered_KeepsEveryLeftOfAKeptRight() {
		var rows = _left.Query()
			.InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep"))
			.Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep"));
	}

	[Test]
	public void PkSelector_InnerChainedAfterInner_KeepsEveryLeftOfBoth() {
		// The second inner join meets rows the first one created: a shared right must reach each of them.
		var rows = _left.Query()
			.InnerJoinOne(static (int id) => ToRight(id), _right)
			.InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep"))
			.Execute();
		Assert.That(rows.Select(static r => r.Left.Id).OrderBy(static id => id), Is.EqualTo(new[] { 1, 2, 3, 4 }));
		Assert.That(rows.All(static r => r.Right?.Label == "keep" && r.Right2?.Label == "keep"), Is.True);
	}

	[Test]
	public void PkSelector_SortBoundedInner_NarrowOnlyPhase_KeepsEveryLeftOfAKeptRight() {
		// The bounded flow narrows the candidates first (UnsafeNarrowIndexedInner) and fills only the page.
		using var page = _left.Query()
			.SortBounded(new SelLeftById())
			.InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep"))
			.ExecutePooled(1, 2);
		Assert.That(page.TotalCount, Is.EqualTo(4));
		Assert.That(Describe(page), Is.EqualTo("2=keep 3=keep"));
	}

	// ── Right-unique index + non-injective selector ───────────────────────────

	[Test]
	public void RightUniqueSelector_Outer_Filtered_EveryLeftOfAKeptRightGetsIt() {
		var rows = _books.Query()
			.JoinOne(static (int id) => ToRight(id), _infos, _infoByBookKey, static q => q.Where(static i => i.Synopsis == "keep"))
			.Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=- 6=- 7=-"));
	}

	[Test]
	public void RightUniqueSelector_Inner_KeepsEveryLeftWithARight() {
		var rows = _books.Query().InnerJoinOne(static (int id) => ToRight(id), _infos, _infoByBookKey).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=drop 6=drop"));
	}

	[Test]
	public void RightUniqueSelector_InnerFiltered_KeepsEveryLeftOfAKeptRight() {
		var rows = _books.Query()
			.InnerJoinOne(static (int id) => ToRight(id), _infos, _infoByBookKey, static q => q.Where(static i => i.Synopsis == "keep"))
			.Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep"));
	}

	// ── Pool balance: the shared-right arrays are rented during seeding ───────

	[Test]
	public void SharedRights_OuterFiltered_Inner_Bounded_Balanced() =>
		LeakAssert.Balanced(() => {
			using (var outer = _left.Query().JoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep")).ExecutePooled())
				Assert.That(outer.Count, Is.EqualTo(7));
			using (var inner = _left.Query().InnerJoinOne(static (int id) => ToRight(id), _right).ExecutePooled())
				Assert.That(inner.Count, Is.EqualTo(6));
			using (var page = _left.Query().SortBounded(new SelLeftById()).InnerJoinOne(static (int id) => ToRight(id), _right).ExecutePooled(0, 3))
				Assert.That(page.Count, Is.EqualTo(3));
		});

	[Test]
	public void SharedRights_ThrowingFilter_AfterSeeding_Balanced() =>
		LeakAssert.Balanced(() => {
			// The arrays are rented while seeding, before the filter callback runs and throws.
			Assert.Throws<InvalidOperationException>(() => _left.Query()
				.InnerJoinOne(static (int id) => ToRight(id), _right, static q => throw new InvalidOperationException("hostile filter"))
				.ExecutePooled());
			Assert.Throws<InvalidOperationException>(() => _left.Query()
				.JoinOne(static (int id) => ToRight(id), _right, static q => throw new InvalidOperationException("hostile filter"))
				.ExecutePooled());
		});

	[Test]
	public void SharedRights_ThrowingPredicate_DuringPairedRead_Balanced() =>
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => _left.Query()
				.InnerJoinOne(static (int id) => ToRight(id), _right,
					static q => q.Where(static r => r.Label == "drop" ? throw new InvalidOperationException("hostile predicate") : true))
				.ExecutePooled());
		});
}

namespace Prague.Core.Tests.Join;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// ── A struct right value: no null tells a miss from a match ──────────────────
internal struct ShrStructRight : ICacheEquatable<ShrStructRight>, ICacheClonable<ShrStructRight> {
	public long Id { get; init; }
	public string Label { get; init; }

	public bool CacheEquals(ShrStructRight other) => other.Id == Id && other.Label == Label;
	public int CacheGetHashCode() => HashCode.Combine(Id, Label);
	public ShrStructRight Clone() => this;
}

// Shared rights with a STRUCT right: the inner phase must create rows for matches only — a default struct is
// not null, so a row created for a left the execute never matched would survive RetainNonNullSlots.
[TestFixture]
public class JoinOneSharedRightStructCoreTests {
	private InMemoryDataCache<int, SelLeft> _left = null!;
	private InMemoryDataCache<long, ShrStructRight> _right = null!;

	// Lefts 1..4 → 1001 ("keep"), 5..6 → 1002 ("drop"), 7 → 1003 (absent).
	private static long ToRight(int id) => id <= 4 ? 1001L : id <= 6 ? 1002L : 1003L;

	[SetUp]
	public void SetUp() {
		_left = new InMemoryDataCache<int, SelLeft>();
		for (var i = 1; i <= 7; i++)
			_left.AddOrUpdate(i, new SelLeft { Id = i, Name = $"L{i}" });
		_right = new InMemoryDataCache<long, ShrStructRight>();
		_right.AddOrUpdate(1001L, new ShrStructRight { Id = 1001L, Label = "keep" });
		_right.AddOrUpdate(1002L, new ShrStructRight { Id = 1002L, Label = "drop" });
	}

	private static string Describe(IEnumerable<JoinResult<SelLeft, ShrStructRight>> rows)
		=> string.Join(" ", rows.OrderBy(static r => r.Left.Id).Select(static r => $"{r.Left.Id}={r.Right.Label ?? "-"}"));

	private readonly struct SelLeftById : IComparer<SelLeft> {
		public int Compare(SelLeft? x, SelLeft? y) => (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	[Test]
	public void Inner_FilterRejectsEverything_NoRows() {
		var rows = _left.Query().InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static _ => false)).Execute();
		Assert.That(rows, Is.Empty);
	}

	[Test]
	public void Inner_Unfiltered_OnlyLeftsWithARight() {
		var rows = _left.Query().InnerJoinOne(static (int id) => ToRight(id), _right).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=drop 6=drop"));
	}

	[Test]
	public void Inner_Filtered_OnlyLeftsOfAKeptRight() {
		var rows = _left.Query().InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep")).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep"));
	}

	[Test]
	public void Outer_Filtered_RejectedAndMissingKeepTheDefault() {
		var rows = _left.Query().JoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static r => r.Label == "keep")).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=keep 2=keep 3=keep 4=keep 5=- 6=- 7=-"));
	}

	[Test]
	public void SortBoundedInner_FilterRejectsEverything_EmptyPage() {
		using var page = _left.Query()
			.SortBounded(new SelLeftById())
			.InnerJoinOne(static (int id) => ToRight(id), _right, static q => q.Where(static _ => false))
			.ExecutePooled(0, 3);
		Assert.That(page.Count, Is.Zero);
		Assert.That(page.TotalCount, Is.Zero);
	}
}

// ── Left-symmetric with a non-injective selector ─────────────────────────────
internal sealed class ShrBook : ICacheEquatable<ShrBook>, ICacheClonable<ShrBook> {
	public int Id { get; init; }
	public int Code { get; init; }

	public bool CacheEquals(ShrBook? other) => other is not null && other.Id == Id && other.Code == Code;
	public int CacheGetHashCode() => HashCode.Combine(Id, Code);
	public ShrBook Clone() => new() { Id = Id, Code = Code };
}

internal sealed class ShrCountry : ICacheEquatable<ShrCountry>, ICacheClonable<ShrCountry> {
	public int Id { get; init; }
	public int Numeric { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(ShrCountry? other) => other is not null && other.Id == Id && other.Numeric == Numeric && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Numeric, Name);
	public ShrCountry Clone() => new() { Id = Id, Numeric = Numeric, Name = Name };
}

// Index values 11 and 12 both select right 1 (k / 10): two buckets, one pair. Before this change the second
// bucket lost the right on every pair-set path — a filtered outer join and an inner one. The filtered outer
// join now fills its rows by pair slot (both buckets' rows share the slot); the inner one chains the second
// bucket to the pair (JoinOneSharedRights).
[TestFixture]
public class JoinOneLeftSymSharedRightCoreTests {
	private InMemoryDataCache<int, ShrBook> _books = null!;
	private CacheSymmetricKeyValueListIndex<int, ShrBook, int> _byCode = null!;
	private InMemoryDataCache<int, ShrCountry> _countries = null!;
	private CacheUniqueIndex<int, ShrCountry, int> _byNumeric = null!;

	[SetUp]
	public void SetUp() {
		_books = new InMemoryDataCache<int, ShrBook>();
		_byCode = _books.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Code);
		// Codes 11 (books 1, 2) and 12 (books 3, 4) → right 1; 21 (book 5) → right 2; 99 (book 6) → right 9, absent.
		_books.AddOrUpdate(1, new ShrBook { Id = 1, Code = 11 });
		_books.AddOrUpdate(2, new ShrBook { Id = 2, Code = 11 });
		_books.AddOrUpdate(3, new ShrBook { Id = 3, Code = 12 });
		_books.AddOrUpdate(4, new ShrBook { Id = 4, Code = 12 });
		_books.AddOrUpdate(5, new ShrBook { Id = 5, Code = 21 });
		_books.AddOrUpdate(6, new ShrBook { Id = 6, Code = 99 });

		_countries = new InMemoryDataCache<int, ShrCountry>();
		_byNumeric = _countries.AddKeyValueIndex<int>(static (_, v) => v.Numeric);
		_countries.AddOrUpdate(1, new ShrCountry { Id = 1, Numeric = 100, Name = "One" });
		_countries.AddOrUpdate(2, new ShrCountry { Id = 2, Numeric = 200, Name = "Two" });
	}

	private static string Describe(IEnumerable<JoinResult<ShrBook, ShrCountry?>> rows)
		=> string.Join(" ", rows.OrderBy(static r => r.Left.Id).Select(static r => $"{r.Left.Id}={r.Right?.Name ?? "-"}"));

	// ── Shape A: lookup key → selector → right primary key ──

	[Test]
	public void ShapeA_Outer_Unfiltered() {
		var rows = _books.Query().JoinOne(_byCode, static (int k) => k / 10, _countries).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One 5=Two 6=-"));
	}

	[Test]
	public void ShapeA_Outer_Filtered_EveryBucketGetsTheSharedRight() {
		var rows = _books.Query().JoinOne(_byCode, static (int k) => k / 10, _countries, static q => q.Where(static _ => true)).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One 5=Two 6=-"));
	}

	[Test]
	public void ShapeA_Inner_KeepsEveryBucketOfTheSharedRight() {
		var rows = _books.Query().InnerJoinOne(_byCode, static (int k) => k / 10, _countries).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One 5=Two"));
	}

	[Test]
	public void ShapeA_InnerFiltered_KeepsEveryBucketOfAKeptRight() {
		var rows = _books.Query().InnerJoinOne(_byCode, static (int k) => k / 10, _countries, static q => q.Where(static c => c.Name == "One")).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One"));
	}

	// ── Shape B: lookup key → selector → right unique index → right primary key ──

	[Test]
	public void ShapeB_Outer_Filtered_EveryBucketGetsTheSharedRight() {
		var rows = _books.Query().JoinOne(_byCode, static (int k) => k / 10 * 100, _countries, _byNumeric, static q => q.Where(static _ => true)).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One 5=Two 6=-"));
	}

	[Test]
	public void ShapeB_Inner_KeepsEveryBucketOfTheSharedRight() {
		var rows = _books.Query().InnerJoinOne(_byCode, static (int k) => k / 10 * 100, _countries, _byNumeric).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=One 2=One 3=One 4=One 5=Two"));
	}

	[Test]
	public void SharedBuckets_FilteredOuter_Inner_ThrowingFilter_Balanced() =>
		LeakAssert.Balanced(() => {
			using (var outer = _books.Query().JoinOne(_byCode, static (int k) => k / 10, _countries, static q => q.Where(static _ => true)).ExecutePooled())
				Assert.That(outer.Count, Is.EqualTo(6));
			using (var inner = _books.Query().InnerJoinOne(_byCode, static (int k) => k / 10 * 100, _countries, _byNumeric).ExecutePooled())
				Assert.That(inner.Count, Is.EqualTo(5));
			Assert.Throws<InvalidOperationException>(() => _books.Query()
				.InnerJoinOne(_byCode, static (int k) => k / 10, _countries, static q => throw new InvalidOperationException("hostile filter"))
				.ExecutePooled());
		});
}

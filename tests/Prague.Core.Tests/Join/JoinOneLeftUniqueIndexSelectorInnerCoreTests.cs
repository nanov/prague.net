namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// LusiAuthor (left, int Id, long BookId) — author carries a long BookId and is
// indexed by a SYMMETRIC unique index keyed on long. LusiBook (right, int Id).
// The selector maps long (the index/Reverse lookup) → int (the right PK), so
// these tests exercise the left-unique-index + key-selector InnerJoinOne
// shape across all 6 overloads (S1..S6).

internal sealed class LusiAuthor : ICacheEquatable<LusiAuthor>, ICacheClonable<LusiAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public long BookId { get; init; }

	public bool CacheEquals(LusiAuthor? other) => other is not null && other.Id == Id && other.Name == Name && other.BookId == BookId;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, BookId);
	public LusiAuthor Clone() => new() { Id = Id, Name = Name, BookId = BookId };
}

internal sealed class LusiBook : ICacheEquatable<LusiBook>, ICacheClonable<LusiBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public string Genre { get; init; } = "";

	public bool CacheEquals(LusiBook? other) => other is not null && other.Id == Id && other.Title == Title && other.Genre == Genre;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, Genre);
	public LusiBook Clone() => new() { Id = Id, Title = Title, Genre = Genre };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneLeftUniqueIndexSelectorInnerCoreTests {
	private InMemoryDataCache<int, LusiAuthor> _authorCache = null!;
	private InMemoryDataCache<int, LusiBook> _bookCache = null!;
	// SYMMETRIC unique index keyed by long (BookId).
	private CacheSymmetricUniqueIndex<int, LusiAuthor, long> _authorBookIdIndex = null!;

	// Selector: long bookId → int bookPk via `(int)(bookId - 7000L)`. Authors
	// with BookId == 7001/7002/7003 map to right PKs 1/2/3.
	private static int MapLongToInt(long bookId) => (int)(bookId - 7000L);
	private static int MapLongToIntWithArg(long bookId, long origin) => (int)(bookId - origin);

	[SetUp]
	public void SetUp() {
		_authorCache = new InMemoryDataCache<int, LusiAuthor>();
		_bookCache = new InMemoryDataCache<int, LusiBook>();
		_authorBookIdIndex = _authorCache.AddSymmetricKeyValueIndex<long>((_, v) => v.BookId);
	}

	private void SeedThreeAuthors() {
		_authorCache.AddOrUpdate(1, new LusiAuthor { Id = 1, Name = "Alice", BookId = 7001L });
		_authorCache.AddOrUpdate(2, new LusiAuthor { Id = 2, Name = "Bob",   BookId = 7002L });
		_authorCache.AddOrUpdate(3, new LusiAuthor { Id = 3, Name = "Carol", BookId = 7003L });
	}

	// ── S1: no selectorArg, no filter ─────────────────────────────────────────

	[Test]
	public void InnerJoinOne_S1_NoSelectorArg_NoFilter_DropsUnmatched() {
		// Books 1, 2 present; book 3 missing → Carol must drop.
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(2, new LusiBook { Id = 2, Title = "Book B", Genre = "history" });
		SeedThreeAuthors();

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, (long k) => MapLongToInt(k), _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Carol's mapped book is missing — must be dropped");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	// ── S2: with selectorArg, no filter ───────────────────────────────────────

	[Test]
	public void InnerJoinOne_S2_WithSelectorArg_NoFilter_DropsUnmatched() {
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(3, new LusiBook { Id = 3, Title = "Book C", Genre = "fiction" });
		// Book 2 missing → Bob drops.
		SeedThreeAuthors();

		const long origin = 7000L;
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex,
				static (long k, long o) => MapLongToIntWithArg(k, o), origin,
				_bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	// ── S3: no selectorArg, with filter ───────────────────────────────────────

	[Test]
	public void InnerJoinOne_S3_NoSelectorArg_WithFilter_DropsFilteredOut() {
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(2, new LusiBook { Id = 2, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(3, new LusiBook { Id = 3, Title = "Book C", Genre = "fiction" });
		SeedThreeAuthors();

		// Filter to "fiction" — Bob's book is "history", drops.
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex,
				(long k) => MapLongToInt(k),
				_bookCache,
				q => q.Where(b => b.Genre == "fiction"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	// ── S4: with selectorArg, with filter ─────────────────────────────────────

	[Test]
	public void InnerJoinOne_S4_WithSelectorArg_WithFilter_DropsFilteredOut() {
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(2, new LusiBook { Id = 2, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(3, new LusiBook { Id = 3, Title = "Book C", Genre = "fiction" });
		SeedThreeAuthors();

		const long origin = 7000L;
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex,
				static (long k, long o) => MapLongToIntWithArg(k, o), origin,
				_bookCache,
				q => q.Where(b => b.Genre == "fiction"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	// ── S5: no selectorArg, filter + TFilterArg static lambda ─────────────────

	[Test]
	public void InnerJoinOne_S5_NoSelectorArg_FilterWithArg_StaticLambda() {
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(2, new LusiBook { Id = 2, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(3, new LusiBook { Id = 3, Title = "Book C", Genre = "fiction" });
		SeedThreeAuthors();

		const string targetGenre = "history";
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex,
				(long k) => MapLongToInt(k),
				_bookCache,
				static (q, g) => q.Where(b => b.Genre == g),
				targetGenre)
			.Execute();

		// Only Bob's book is "history".
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right, Is.Not.Null);
		Assert.That(results[0].Right!.Title, Is.EqualTo("Book B"));
	}

	// ── S6: with selectorArg + filter + TFilterArg static lambdas ─────────────

	[Test]
	public void InnerJoinOne_S6_WithSelectorArg_FilterWithArg_StaticLambdas() {
		_bookCache.AddOrUpdate(1, new LusiBook { Id = 1, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(2, new LusiBook { Id = 2, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(3, new LusiBook { Id = 3, Title = "Book C", Genre = "fiction" });
		SeedThreeAuthors();

		const long origin = 7000L;
		const string targetGenre = "fiction";
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex,
				static (long k, long o) => MapLongToIntWithArg(k, o), origin,
				_bookCache,
				static (q, g) => q.Where(b => b.Genre == g),
				targetGenre)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}
}

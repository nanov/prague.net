namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// LuAuthor (left, has BookId FK as a SYMMETRIC unique index) and LuBook (right,
// PK only). All hand-rolled. The left side's symmetric unique index exposes the
// .Reverse direction (leftKey → rightKey) the resolver needs.

internal sealed class LuAuthor : ICacheEquatable<LuAuthor>, ICacheClonable<LuAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public int BookId { get; init; }

	public bool CacheEquals(LuAuthor? other) => other is not null && other.Id == Id && other.Name == Name && other.BookId == BookId;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, BookId);
	public LuAuthor Clone() => new() { Id = Id, Name = Name, BookId = BookId };
}

internal sealed class LuBook : ICacheEquatable<LuBook>, ICacheClonable<LuBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public string Genre { get; init; } = "";

	public bool CacheEquals(LuBook? other) => other is not null && other.Id == Id && other.Title == Title && other.Genre == Genre;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, Genre);
	public LuBook Clone() => new() { Id = Id, Title = Title, Genre = Genre };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneLeftUniqueIndexCoreTests {
	private InMemoryDataCache<int, LuAuthor> _authorCache = null!;
	private InMemoryDataCache<int, LuBook> _bookCache = null!;
	// SYMMETRIC unique index — exposes .Reverse so the left-unique-index resolver
	// can map authorId → bookId in O(1).
	private CacheSymmetricUniqueIndex<int, LuAuthor, int> _authorBookIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_authorCache = new InMemoryDataCache<int, LuAuthor>();
		_bookCache = new InMemoryDataCache<int, LuBook>();
		_authorBookIdIndex = _authorCache.AddSymmetricKeyValueIndex<int>((_, v) => v.BookId);
	}

	// ── Test 1: full match ────────────────────────────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_MatchExists_AttachesBook() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(103, new LuBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Title, Is.EqualTo("Book A"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Title, Is.EqualTo("Book B"));
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[3].Right!.Title, Is.EqualTo("Book C"));
	}

	// ── Test 2: book missing → Right is null ─────────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_NoMatch_ReturnsNullRight() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "Book A", Genre = "fiction" });
		// 102 intentionally NOT added
		_bookCache.AddOrUpdate(103, new LuBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "Author 2's BookId=102 is not in the book cache");
		Assert.That(byId[3].Right, Is.Not.Null);
	}

	// ── Test 3: filter narrows right candidates ──────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_WithFilter_NarrowsRight() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(103, new LuBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache,
				q => q.Where(b => b.Genre == "fiction"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "Book B is history — filtered out");
		Assert.That(byId[3].Right, Is.Not.Null);
	}

	// ── Test 4: filter drops everything ──────────────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_FilterDropsAll_AllRightsNull() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "Book B", Genre = "history" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });

		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache,
				q => q.Where(b => b.Genre == "nonexistent"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right == null), Is.True);
	}

	// ── Test 5: filter + TArg static lambda ──────────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_WithFilterAndArg_StaticLambda() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(103, new LuBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 103 });

		const string targetGenre = "fiction";
		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache,
				static (q, g) => q.Where(b => b.Genre == g),
				targetGenre)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "history book filtered out");
		Assert.That(byId[3].Right, Is.Not.Null);
	}

	// ── Inner tests ───────────────────────────────────────────────────────────

	[Test]
	public void InnerJoinOne_AuthorWithoutBook_Dropped() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "A" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "B" });
		// no book 999

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 999 });

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Carol's book is missing — must be dropped");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void InnerJoinOne_AllMatched_KeepsAll() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "A" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "B" });
		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	[Test]
	public void InnerJoinOne_NoneMatched_EmptyResult() {
		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 999 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 998 });
		// _bookCache empty

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinOne_WithFilter_DropsFilteredOut() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "B", Genre = "history" });
		_bookCache.AddOrUpdate(103, new LuBook { Id = 103, Title = "C", Genre = "fiction" });

		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(3, new LuAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache,
				q => q.Where(b => b.Genre == "fiction"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void InnerJoinOne_WithFilterArg_StaticLambda() {
		_bookCache.AddOrUpdate(101, new LuBook { Id = 101, Title = "A", Genre = "fiction" });
		_bookCache.AddOrUpdate(102, new LuBook { Id = 102, Title = "B", Genre = "history" });
		_authorCache.AddOrUpdate(1, new LuAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(2, new LuAuthor { Id = 2, Name = "Bob",   BookId = 102 });

		const string genre = "history";
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache,
				static (q, g) => q.Where(b => b.Genre == g), genre)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
	}
}

namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Phase 4 chained-level verification for JoinMany. Exercises both
// JoinManyRightListIndexResolver identity (level 1) and the LeftSym variant
// (level 1) chained behind a level-0 join.
//
// Authors → Books (Many via Books.AuthorId list index)
// Books   → Reviews (Many via Reviews.BookId list index)
// Also exercises a level-0 JoinOne (Author → Profile) feeding a level-1
// JoinMany (Author → Books).

internal sealed class CnAuthor : ICacheEquatable<CnAuthor>, ICacheClonable<CnAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public bool CacheEquals(CnAuthor? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public CnAuthor Clone() => new() { Id = Id, Name = Name };
}

internal sealed class CnProfile : ICacheEquatable<CnProfile>, ICacheClonable<CnProfile> {
	public int Id { get; init; }
	public string Bio { get; init; } = "";
	public bool CacheEquals(CnProfile? other) => other is not null && other.Id == Id && other.Bio == Bio;
	public int CacheGetHashCode() => HashCode.Combine(Id, Bio);
	public CnProfile Clone() => new() { Id = Id, Bio = Bio };
}

internal sealed class CnBook : ICacheEquatable<CnBook>, ICacheClonable<CnBook> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string Title { get; init; } = "";
	public bool CacheEquals(CnBook? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, Title);
	public CnBook Clone() => new() { Id = Id, AuthorId = AuthorId, Title = Title };
}

internal sealed class CnReview : ICacheEquatable<CnReview>, ICacheClonable<CnReview> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string Country { get; init; } = "";
	public string Comment { get; init; } = "";
	public bool CacheEquals(CnReview? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.Country == Country && other.Comment == Comment;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, Country, Comment);
	public CnReview Clone() => new() { Id = Id, AuthorId = AuthorId, Country = Country, Comment = Comment };
}

[TestFixture]
public class JoinManyChainedCoreTests {
	private InMemoryDataCache<int, CnAuthor> _authors = null!;
	private InMemoryDataCache<int, CnProfile> _profiles = null!;
	private InMemoryDataCache<int, CnBook> _books = null!;
	private InMemoryDataCache<int, CnReview> _reviews = null!;

	private CacheKeyValueListIndex<int, CnBook, int> _bookAuthorIdIdx = null!;
	private CacheKeyValueListIndex<int, CnReview, int> _reviewAuthorIdIdx = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, CnAuthor>();
		_profiles = new InMemoryDataCache<int, CnProfile>();
		_books = new InMemoryDataCache<int, CnBook>();
		_reviews = new InMemoryDataCache<int, CnReview>();

		_bookAuthorIdIdx = _books.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
		_reviewAuthorIdIdx = _reviews.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);

		_authors.AddOrUpdate(1, new CnAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(2, new CnAuthor { Id = 2, Name = "Asimov" });
		_authors.AddOrUpdate(3, new CnAuthor { Id = 3, Name = "Orphan" });

		_profiles.AddOrUpdate(1, new CnProfile { Id = 1, Bio = "UK fantasy" });
		_profiles.AddOrUpdate(2, new CnProfile { Id = 2, Bio = "US sci-fi" });
		// no profile for 3

		_books.AddOrUpdate(101, new CnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_books.AddOrUpdate(102, new CnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_books.AddOrUpdate(201, new CnBook { Id = 201, AuthorId = 2, Title = "Foundation" });
		// no books for 3

		_reviews.AddOrUpdate(1001, new CnReview { Id = 1001, AuthorId = 1, Country = "UK", Comment = "great" });
		_reviews.AddOrUpdate(1002, new CnReview { Id = 1002, AuthorId = 1, Country = "US", Comment = "ok" });
		_reviews.AddOrUpdate(2001, new CnReview { Id = 2001, AuthorId = 2, Country = "US", Comment = "classic" });
	}

	// ── Test 1: JoinOne → JoinMany (level-0 One then level-1 Many) ────────

	[Test]
	public void JoinOne_Then_JoinMany_RightListIndex() {
		var results = _authors.Query()
			.JoinOne(_profiles)
			.JoinMany(_books, _bookAuthorIdIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		// Right (profile)
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(byId[3].Right, Is.Null);

		// Right2 (books QueryResults)
		Assert.That(byId[1].Right2.Count, Is.EqualTo(2));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 2: JoinMany → JoinOne (level-0 Many then level-1 One) ────────

	[Test]
	public void JoinMany_Then_JoinOne() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdIdx)
			.JoinOne(_profiles)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2, Is.Not.Null);
		Assert.That(byId[1].Right2!.Bio, Is.EqualTo("UK fantasy"));

		Assert.That(byId[2].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right2!.Bio, Is.EqualTo("US sci-fi"));

		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
		Assert.That(byId[3].Right2, Is.Null);
	}

	// ── Test 3: JoinMany → JoinMany (chained Many-then-Many) ───────────
	//
	// Authors → Books (level 0) → Reviews (level 1). Reviews are indexed by
	// AuthorId, not BookId, so it isn't truly a "books-then-their-reviews"
	// nested join — instead it's two parallel many-relationships off the same
	// PK. That's still the right surface to exercise: level-1
	// JoinManyRightListIndexResolver chained behind level-0 JoinMany.

	[Test]
	public void JoinMany_Then_JoinMany_RightListIndex() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdIdx)
			.JoinMany(_reviews, _reviewAuthorIdIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(2));
		Assert.That(byId[2].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 4: JoinMany → InnerJoinMany (mixed outer/inner chain) ────

	[Test]
	public void JoinMany_Then_InnerJoinMany_DropsAuthorsWithNoReviews() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdIdx)
			.InnerJoinMany(_reviews, _reviewAuthorIdIdx)
			.Execute();

		// Author 3 has no reviews → dropped by InnerJoin at level 1.
		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId.ContainsKey(3), Is.False, "Orphan author should be dropped by InnerJoin at level 1");
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(2));
	}

	// ── Test 5: chained JoinMany with filter on the second join ──────────

	[Test]
	public void JoinMany_Then_JoinMany_WithFilterOnSecond() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdIdx)
			.JoinMany(_reviews, _reviewAuthorIdIdx,
				q => q.Where(r => r.Country == "US"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		// Author 1 had two reviews (UK, US); filter keeps only US.
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[1].Right2.Single().Country, Is.EqualTo("US"));
		// Author 2 had one US review.
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		// Author 3 has no reviews regardless of filter.
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 6: LeftSym chained behind a level-0 JoinOne ─────────────────────
	//
	// Exercises the level-1 LeftSym JoinMany overload. The executor is
	// still Authors (the chain never re-roots), but the sym index is on
	// Authors and matches the right-side review index via Country.

	[Test]
	public void JoinOne_Then_JoinMany_LeftSym() {
		// Fresh caches — order matters: define the sym index BEFORE adding
		// authors so it populates incrementally.
		var authors = new InMemoryDataCache<int, CnAuthor>();
		var profiles = new InMemoryDataCache<int, CnProfile>();
		var reviews = new InMemoryDataCache<int, CnReview>();

		// Use Name as a stand-in for Country since CnAuthor lacks Country —
		// the prefix substring works as the lookup key.
		var authorSymIdx = authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Name);
		var reviewCountryIdx = reviews.CacheKeyValueListIndex<string>((_, v) => v.Country);

		authors.AddOrUpdate(1, new CnAuthor { Id = 1, Name = "UK" });
		authors.AddOrUpdate(2, new CnAuthor { Id = 2, Name = "US" });
		authors.AddOrUpdate(3, new CnAuthor { Id = 3, Name = "FR" });

		profiles.AddOrUpdate(1, new CnProfile { Id = 1, Bio = "british" });
		profiles.AddOrUpdate(2, new CnProfile { Id = 2, Bio = "american" });

		reviews.AddOrUpdate(1001, new CnReview { Id = 1001, AuthorId = 0, Country = "UK", Comment = "uk1" });
		reviews.AddOrUpdate(1002, new CnReview { Id = 1002, AuthorId = 0, Country = "US", Comment = "us1" });
		reviews.AddOrUpdate(2001, new CnReview { Id = 2001, AuthorId = 0, Country = "US", Comment = "us2" });

		var results = authors.Query()
			.JoinOne(profiles)
			.JoinMany(authorSymIdx, reviews, reviewCountryIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		// Author 1 ("UK") → 1 review with Country=UK.
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[1].Right2.Single().Country, Is.EqualTo("UK"));
		// Author 2 ("US") → 2 reviews with Country=US.
		Assert.That(byId[2].Right2.Count, Is.EqualTo(2));
		// Author 3 ("FR") → no reviews.
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 7: JoinOne → JoinMany with Func selector (chained level 1) ───

	[Test]
	public void JoinOne_Then_JoinMany_RightListIndex_FuncSelector() {
		// Selector is identity (int → int) — exercises the level-1 selector overload
		// emission path even though the mapping is trivial.
		var results = _authors.Query()
			.JoinOne(_profiles)
			.JoinMany(k => k, _books, _bookAuthorIdIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2.Count, Is.EqualTo(2));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 8: JoinMany → JoinMany with Func+arg selector on second ───

	[Test]
	public void JoinMany_Then_JoinMany_RightListIndex_FuncArgSelector() {
		// selectorArg is 0 — adds nothing, but exercises the Func+arg surface.
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdIdx)
			.JoinMany((k, _) => k, 0, _reviews, _reviewAuthorIdIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(2));
		Assert.That(byId[2].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Test 9: JoinMany → JoinMany LeftSym with Func selector ─────────

	[Test]
	public void JoinMany_Then_JoinMany_LeftSym_FuncSelector() {
		var authors = new InMemoryDataCache<int, CnAuthor>();
		var books = new InMemoryDataCache<int, CnBook>();
		var reviews = new InMemoryDataCache<int, CnReview>();

		var authorSymIdx = authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Name);
		var bookAuthorIdIdx = books.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
		var reviewCountryIdx = reviews.CacheKeyValueListIndex<string>((_, v) => v.Country);

		authors.AddOrUpdate(1, new CnAuthor { Id = 1, Name = "UK" });
		authors.AddOrUpdate(2, new CnAuthor { Id = 2, Name = "US" });

		books.AddOrUpdate(101, new CnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		books.AddOrUpdate(201, new CnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		reviews.AddOrUpdate(1001, new CnReview { Id = 1001, AuthorId = 0, Country = "UK", Comment = "uk1" });
		reviews.AddOrUpdate(1002, new CnReview { Id = 1002, AuthorId = 0, Country = "US", Comment = "us1" });
		reviews.AddOrUpdate(2001, new CnReview { Id = 2001, AuthorId = 0, Country = "US", Comment = "us2" });

		// LeftSym chained at level 1 — selector is identity (string → string), but exercises
		// the level-1 LSS1 selector emission path.
		var results = authors.Query()
			.JoinMany(books, bookAuthorIdIdx)
			.JoinMany(authorSymIdx, s => s, reviews, reviewCountryIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[1].Right2.Single().Country, Is.EqualTo("UK"));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(2));
	}

	// ── Test 10: chained InnerJoinMany LeftSym with selector + filter ─────

	[Test]
	public void JoinMany_Then_InnerJoinMany_LeftSym_FuncSelector_WithFilter() {
		var authors = new InMemoryDataCache<int, CnAuthor>();
		var books = new InMemoryDataCache<int, CnBook>();
		var reviews = new InMemoryDataCache<int, CnReview>();

		var authorSymIdx = authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Name);
		var bookAuthorIdIdx = books.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
		var reviewCountryIdx = reviews.CacheKeyValueListIndex<string>((_, v) => v.Country);

		authors.AddOrUpdate(1, new CnAuthor { Id = 1, Name = "UK" });
		authors.AddOrUpdate(2, new CnAuthor { Id = 2, Name = "US" });
		authors.AddOrUpdate(3, new CnAuthor { Id = 3, Name = "FR" });

		books.AddOrUpdate(101, new CnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		books.AddOrUpdate(201, new CnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		reviews.AddOrUpdate(1001, new CnReview { Id = 1001, AuthorId = 0, Country = "UK", Comment = "uk1" });
		reviews.AddOrUpdate(1002, new CnReview { Id = 1002, AuthorId = 0, Country = "US", Comment = "rejected" });
		reviews.AddOrUpdate(2001, new CnReview { Id = 2001, AuthorId = 0, Country = "US", Comment = "us2" });

		// InnerJoinMany LeftSym + selector + filter: FR has no reviews; UK keeps Comment != "rejected".
		var results = authors.Query()
			.JoinMany(books, bookAuthorIdIdx)
			.InnerJoinMany(authorSymIdx, s => s, reviews, reviewCountryIdx,
				q => q.Where(r => r.Comment != "rejected"))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		// FR (id=3) is dropped (no reviews at all).
		Assert.That(byId.ContainsKey(3), Is.False);
		// UK keeps its UK review.
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
		// US: one review filtered out, one kept.
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right2.Single().Comment, Is.EqualTo("us2"));
	}
}

namespace Prague.Generated.Tests.Join;
#if false

[TestFixture]
public class JoinMapWhereTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<AuthorCache>()
			.Register<BookCache>()
			.Register<AuthorProfileCache>()
			.Register<BookReviewCache>()
			.Register<AuthorAwardCache>()
			.Register<AuthorPublisherCache>()
			.Register<AuthorEventCache>()
			.Build();
		_authorCache = _registry.GetCache<AuthorCache>();
		_bookCache = _registry.GetCache<BookCache>();
		_authorProfileCache = _registry.GetCache<AuthorProfileCache>();
		_authorAwardCache = _registry.GetCache<AuthorAwardCache>();
		_authorPublisherCache = _registry.GetCache<AuthorPublisherCache>();
		_authorEventCache = _registry.GetCache<AuthorEventCache>();

		SeedData();
	}

	private DataCacheRegistry _registry = null!;
	private AuthorCache _authorCache = null!;
	private BookCache _bookCache = null!;
	private AuthorProfileCache _authorProfileCache = null!;
	private AuthorAwardCache _authorAwardCache = null!;
	private AuthorPublisherCache _authorPublisherCache = null!;
	private AuthorEventCache _authorEventCache = null!;

	private void SeedData() {
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "J.K. Rowling", Country = "UK" });
		_authorCache.AddOrUpdate(new Author { Id = 3, Name = "George Orwell", Country = "UK" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Harry Potter", AuthorId = 2, Year = 1997 });
		_bookCache.AddOrUpdate(new Book { Id = 4, Title = "1984", AuthorId = 3, Year = 1949 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile { Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });
		_authorProfileCache.AddOrUpdate(new AuthorProfile { Id = 2, AuthorId = 2, Bio = "Fantasy author", Website = "www.jkrowling.com" });

		_authorAwardCache.AddOrUpdate(new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward { Id = 2, AuthorId = 1, AwardName = "World Fantasy", Year = 1982 });
		_authorAwardCache.AddOrUpdate(new AuthorAward { Id = 3, AuthorId = 2, AwardName = "Hugo", Year = 2001 });

		_authorPublisherCache.AddOrUpdate(new AuthorPublisher { Id = 1, AuthorId = 1, PublisherName = "Viking Press", Country = "USA" });
		_authorPublisherCache.AddOrUpdate(new AuthorPublisher { Id = 2, AuthorId = 2, PublisherName = "Bloomsbury", Country = "UK" });
		_authorPublisherCache.AddOrUpdate(new AuthorPublisher { Id = 3, AuthorId = 3, PublisherName = "Secker & Warburg", Country = "UK" });

		_authorEventCache.AddOrUpdate(new AuthorEvent { Id = 1, AuthorId = 1, EventName = "Book Signing", Location = "NYC" });
		_authorEventCache.AddOrUpdate(new AuthorEvent { Id = 2, AuthorId = 1, EventName = "Comic Con", Location = "San Diego" });
		_authorEventCache.AddOrUpdate(new AuthorEvent { Id = 3, AuthorId = 2, EventName = "Book Fair", Location = "London" });
	}

	// ───────────────── Mappers ─────────────────

	// OneToMany: Author -> Book
	private struct AuthorBookCountMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>>, AuthorBookSummary> {
		public CacheMapResult<AuthorBookSummary> MapOrFilter(JoinResult<Author, QueryResults<Book>> value) {
			if (value.Right.Count == 0)
				return CacheMapResult<AuthorBookSummary>.Skip();
			return CacheMapResult<AuthorBookSummary>.Ok(new AuthorBookSummary {
				AuthorName = value.Left.Name,
				BookCount = value.Right.Count
			});
		}
	}

	// OneToOne: Author -> AuthorProfile
	private struct AuthorWithProfileMapper : ICacheWhereMapper<JoinResult<Author, AuthorProfile?>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, AuthorProfile?> value) {
			if (value.Right == null)
				return CacheMapResult<string>.Skip();
			return CacheMapResult<string>.Ok($"{value.Left.Name}: {value.Right.Bio}");
		}
	}

	// Filter by country on joined result
	private struct UsaAuthorBookMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, QueryResults<Book>> value) {
			if (value.Left.Country != "USA")
				return CacheMapResult<string>.Skip();
			return CacheMapResult<string>.Ok(value.Left.Name);
		}
	}

	// Skip all
	private struct SkipAllMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, QueryResults<Book>> value) {
			return CacheMapResult<string>.Skip();
		}
	}

	// Pass all
	private struct PassAllMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, QueryResults<Book>> value) {
			return CacheMapResult<string>.Ok(value.Left.Name);
		}
	}

	// Multi-join mapper: Author -> Book + AuthorProfile
	private struct MultiJoinMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>, AuthorProfile>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, QueryResults<Book>, AuthorProfile> value) {
			if (value.Right.Count == 0 || value.Right2 == null)
				return CacheMapResult<string>.Skip();
			return CacheMapResult<string>.Ok($"{value.Left.Name} ({value.Right.Count} books, bio: {value.Right2.Bio})");
		}
	}

	// 3-join mapper: Author -> Book + AuthorProfile + AuthorAward
	private struct ThreeJoinMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>, AuthorProfile, QueryResults<AuthorAward>>, string> {
		public CacheMapResult<string> MapOrFilter(JoinResult<Author, QueryResults<Book>, AuthorProfile, QueryResults<AuthorAward>> value) {
			if (value.Right3.Count == 0)
				return CacheMapResult<string>.Skip();
			return CacheMapResult<string>.Ok($"{value.Left.Name}: {value.Right3.Count} awards");
		}
	}

	// DTO
	private struct AuthorBookSummary {
		public string AuthorName;
		public int BookCount;
	}

	// ───────────────── Tests: OneToMany join ─────────────────

	[Test]
	public void MapWhere_OneToMany_FiltersAndMaps() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Execute();

		// Orwell has 1 book, so all 3 authors have books => 3 results
		Assert.That(results.Count, Is.EqualTo(3));
		var king = results.FirstOrDefault(r => r.AuthorName == "Stephen King");
		Assert.That(king.BookCount, Is.EqualTo(2));
	}

	[Test]
	public void MapWhere_OneToMany_FiltersByCountry() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, UsaAuthorBookMapper>(new UsaAuthorBookMapper())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0], Is.EqualTo("Stephen King"));
	}

	[Test]
	public void MapWhere_OneToMany_SkipAll_ReturnsEmpty() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, SkipAllMapper>(new SkipAllMapper())
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_OneToMany_PassAll_ReturnsAll() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
	}

	[Test]
	public void MapWhere_OneToMany_WithPagination() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.Execute(skip: 1, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	[Test]
	public void MapWhere_OneToMany_WithPooled() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(3));
		results.Dispose();
	}

	[Test]
	public void MapWhere_OneToMany_PooledWithPagination() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.ExecutePooled(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
		results.Dispose();
	}

	[Test]
	public void MapWhere_OneToMany_SkipBeyondCount_ReturnsEmpty() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.Execute(skip: 100);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	// ───────────────── Tests: OneToOne join ─────────────────

	[Test]
	public void MapWhere_OneToOne_FiltersAndMaps() {
		var results = _authorCache.Query()
			.JoinWithAuthorProfile()
			.MapWhere<string, AuthorWithProfileMapper>(new AuthorWithProfileMapper())
			.Execute();

		// Orwell has no profile => skipped, 2 results
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results, Does.Contain("Stephen King: Horror master"));
		Assert.That(results, Does.Contain("J.K. Rowling: Fantasy author"));
	}

	// ───────────────── Tests: Multi-level joins ─────────────────

	[Test]
	public void MapWhere_TwoLevelJoin_FiltersAndMaps() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.Execute();

		// Orwell has no profile => skipped
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results, Does.Contain("Stephen King (2 books, bio: Horror master)"));
		Assert.That(results, Does.Contain("J.K. Rowling (1 books, bio: Fantasy author)"));
	}

	[Test]
	public void MapWhere_ThreeLevelJoin_FiltersAndMaps() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.JoinWithAuthorAward()
			.MapWhere<string, ThreeJoinMapper>(new ThreeJoinMapper())
			.Execute();

		// Orwell has 0 awards => skipped
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results, Does.Contain("Stephen King: 2 awards"));
		Assert.That(results, Does.Contain("J.K. Rowling: 1 awards"));
	}

	[Test]
	public void MapWhere_ThreeLevelJoin_WithPooled() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.JoinWithAuthorAward()
			.MapWhere<string, ThreeJoinMapper>(new ThreeJoinMapper())
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(2));
		results.Dispose();
	}

	[Test]
	public void MapWhere_MultiLevelJoin_WithPagination() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.Execute(skip: 0, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
	}

	// ───────────────── Tests: With Where filter on left query ─────────────────

	[Test]
	public void MapWhere_WithWhereOnLeftQuery() {
		var results = _authorCache.Query()
			.Where(a => a.Country == "UK")
			.JoinWithBook()
			.MapWhere<string, PassAllMapper>(new PassAllMapper())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results, Does.Contain("J.K. Rowling"));
		Assert.That(results, Does.Contain("George Orwell"));
		Assert.That(results, Does.Not.Contain("Stephen King"));
	}

	// ───────────────── Tests: With filter on right query ─────────────────

	[Test]
	public void MapWhere_WithFilterOnRightQuery() {
		// Only join books from before 1990
		var results = _authorCache.Query()
			.JoinWithBook(filter: q => q.Where(b => b.Year < 1990))
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Execute();

		// King has 2 pre-1990 books, Orwell has 1 pre-1990 book, Rowling has 0
		var king = results.FirstOrDefault(r => r.AuthorName == "Stephen King");
		Assert.That(king.BookCount, Is.EqualTo(2));

		// Rowling's only book is 1997, so skipped by the mapper (0 books after filter)
		Assert.That(results.Any(r => r.AuthorName == "J.K. Rowling"), Is.False);
	}

	// ───────────────── Tests: ExecuteKeepJoinedValues ─────────────────

	// Mapper that captures references to the joined QueryResults
	private struct BookReferenceCaptureMapper : ICacheWhereMapper<JoinResult<Author, QueryResults<Book>>, (string Name, QueryResults<Book> Books)> {
		public CacheMapResult<(string Name, QueryResults<Book> Books)> MapOrFilter(JoinResult<Author, QueryResults<Book>> value) {
			if (value.Right.Count == 0)
				return CacheMapResult<(string, QueryResults<Book>)>.Skip();
			// Capture reference to the internal QueryResults - this is only valid if we use ExecuteKeepJoinedValues
			return CacheMapResult<(string, QueryResults<Book>)>.Ok((value.Left.Name, value.Right));
		}
	}

	[Test]
	public void MapWhere_ExecuteKeepJoinedValues_KeepsInternalBuffersAlive() {
		using var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<(string Name, QueryResults<Book> Books), BookReferenceCaptureMapper>(new BookReferenceCaptureMapper())
			.ExecuteKeepJoinedValues();

		Assert.That(results.Count, Is.EqualTo(3));

		// The captured QueryResults should still be valid because we used ExecuteKeepJoinedValues
		var king = results.FirstOrDefault(r => r.Name == "Stephen King");
		Assert.That(king.Books.Count, Is.EqualTo(2));
		Assert.That(king.Books[0].Title, Is.EqualTo("The Shining").Or.EqualTo("It"));
	}

	[Test]
	public void MapWhere_ExecuteKeepJoinedValues_WithPagination() {
		using var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<(string Name, QueryResults<Book> Books), BookReferenceCaptureMapper>(new BookReferenceCaptureMapper())
			.ExecuteKeepJoinedValues(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
	}

	[Test]
	public void MapWhere_ExecuteKeepJoinedValues_SkipBeyondCount_ReturnsEmpty() {
		using var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<(string Name, QueryResults<Book> Books), BookReferenceCaptureMapper>(new BookReferenceCaptureMapper())
			.ExecuteKeepJoinedValues(skip: 100);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_ExecuteKeepJoinedValues_MultiLevelJoin() {
		using var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.ExecuteKeepJoinedValues();

		Assert.That(results.Count, Is.EqualTo(2));
	}

	// ───────────────── Tests: Sort ─────────────────

	// Comparer for AuthorBookSummary - sorts by BookCount descending
	private struct BookCountDescendingComparer : IComparer<AuthorBookSummary> {
		public int Compare(AuthorBookSummary x, AuthorBookSummary y) => y.BookCount.CompareTo(x.BookCount);
	}

	// Comparer for AuthorBookSummary - sorts by AuthorName ascending
	private struct AuthorNameAscendingComparer : IComparer<AuthorBookSummary> {
		public int Compare(AuthorBookSummary x, AuthorBookSummary y) => string.Compare(x.AuthorName, y.AuthorName, StringComparison.Ordinal);
	}

	// Comparer for string - sorts descending
	private struct StringDescendingComparer : IComparer<string> {
		public int Compare(string? x, string? y) => string.Compare(y, x, StringComparison.Ordinal);
	}

	[Test]
	public void MapWhere_Sort_SortsResultsByBookCountDescending() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new BookCountDescendingComparer())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		// Stephen King has 2 books, others have 1 each
		Assert.That(results[0].AuthorName, Is.EqualTo("Stephen King"));
		Assert.That(results[0].BookCount, Is.EqualTo(2));
		Assert.That(results[1].BookCount, Is.EqualTo(1));
		Assert.That(results[2].BookCount, Is.EqualTo(1));
	}

	[Test]
	public void MapWhere_Sort_SortsResultsByNameAscending() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new AuthorNameAscendingComparer())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		// Alphabetical: George Orwell, J.K. Rowling, Stephen King
		Assert.That(results[0].AuthorName, Is.EqualTo("George Orwell"));
		Assert.That(results[1].AuthorName, Is.EqualTo("J.K. Rowling"));
		Assert.That(results[2].AuthorName, Is.EqualTo("Stephen King"));
	}

	[Test]
	public void MapWhere_Sort_WithPagination_SortsThenPaginates() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new AuthorNameAscendingComparer())
			.Execute(skip: 1, take: 1);

		// After sorting alphabetically and taking 1 after skipping 1: J.K. Rowling
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].AuthorName, Is.EqualTo("J.K. Rowling"));
	}

	[Test]
	public void MapWhere_Sort_WithPooled() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new BookCountDescendingComparer())
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results[0].AuthorName, Is.EqualTo("Stephen King"));
		results.Dispose();
	}

	[Test]
	public void MapWhere_Sort_PooledWithPagination() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new AuthorNameAscendingComparer())
			.ExecutePooled(skip: 0, take: 2);

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results[0].AuthorName, Is.EqualTo("George Orwell"));
		Assert.That(results[1].AuthorName, Is.EqualTo("J.K. Rowling"));
		results.Dispose();
	}

	[Test]
	public void MapWhere_Sort_ExecuteKeepJoinedValues() {
		using var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<(string Name, QueryResults<Book> Books), BookReferenceCaptureMapper>(new BookReferenceCaptureMapper())
			.Sort(new CapturedResultNameComparer())
			.ExecuteKeepJoinedValues();

		Assert.That(results.Count, Is.EqualTo(3));
		// Alphabetically sorted
		Assert.That(results[0].Name, Is.EqualTo("George Orwell"));
		Assert.That(results[1].Name, Is.EqualTo("J.K. Rowling"));
		Assert.That(results[2].Name, Is.EqualTo("Stephen King"));
		// The captured QueryResults should still be valid
		Assert.That(results[2].Books.Count, Is.EqualTo(2));
	}

	// Comparer for (string Name, QueryResults<Book> Books) tuple - sorts by Name ascending
	private struct CapturedResultNameComparer : IComparer<(string Name, QueryResults<Book> Books)> {
		public int Compare((string Name, QueryResults<Book> Books) x, (string Name, QueryResults<Book> Books) y)
			=> string.Compare(x.Name, y.Name, StringComparison.Ordinal);
	}

	[Test]
	public void MapWhere_Sort_MultiLevelJoin() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.Sort(new StringDescendingComparer())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		// Sorted descending: Stephen King comes before J.K. Rowling
		Assert.That(results[0], Does.StartWith("Stephen King"));
		Assert.That(results[1], Does.StartWith("J.K. Rowling"));
	}

	[Test]
	public void MapWhere_Sort_SkipBeyondCount_ReturnsEmpty() {
		var results = _authorCache.Query()
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new BookCountDescendingComparer())
			.Execute(skip: 100);

		Assert.That(results.Count, Is.EqualTo(0));
	}

	// ───────────────── Tests: Empty left query (no results from left side) ─────────────────

	[Test]
	public void MapWhere_EmptyLeftQuery_Execute_ReturnsEmpty() {
		// Query for non-existent author - left query returns no results
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999) // No author with this ID
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_EmptyLeftQuery_ExecutePooled_ReturnsEmpty() {
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(0));
		results.Dispose();
	}

	[Test]
	public void MapWhere_EmptyLeftQuery_ExecuteKeepJoinedValues_ReturnsEmpty() {
		using var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.MapWhere<(string Name, QueryResults<Book> Books), BookReferenceCaptureMapper>(new BookReferenceCaptureMapper())
			.ExecuteKeepJoinedValues();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_EmptyLeftQuery_WithSort_ReturnsEmpty() {
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.MapWhere<AuthorBookSummary, AuthorBookCountMapper>(new AuthorBookCountMapper())
			.Sort(new BookCountDescendingComparer())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_EmptyLeftQuery_MultiLevelJoin_ReturnsEmpty() {
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_EmptyLeftQuery_MultiLevelJoin_ExecuteKeepJoinedValues_ReturnsEmpty() {
		using var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.MapWhere<string, MultiJoinMapper>(new MultiJoinMapper())
			.ExecuteKeepJoinedValues();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinExecute_EmptyLeftQuery_ReturnsEmpty() {
		// Test the raw join Execute (not MapWhere) with empty left query
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinExecute_EmptyLeftQuery_MultiLevel_ReturnsEmpty() {
		var results = _authorCache.Query()
			.Where(a => a.Id == 9999)
			.JoinWithBook()
			.JoinWithAuthorProfile()
			.JoinWithAuthorAward()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}
}
#endif
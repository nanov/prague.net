// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[TestFixture]
public class ForeignKeyJoinTests {
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
		_bookReviewCache = _registry.GetCache<BookReviewCache>();
		_authorAwardCache = _registry.GetCache<AuthorAwardCache>();
		_authorPublisherCache = _registry.GetCache<AuthorPublisherCache>();
		_authorEventCache = _registry.GetCache<AuthorEventCache>();
	}

	private DataCacheRegistry _registry = null!;
	private AuthorCache _authorCache = null!;
	private BookCache _bookCache = null!;
	private AuthorProfileCache _authorProfileCache = null!;
	private BookReviewCache _bookReviewCache = null!;
	private AuthorAwardCache _authorAwardCache = null!;
	private AuthorPublisherCache _authorPublisherCache = null!;
	private AuthorEventCache _authorEventCache = null!;

	[Test]
	public void AutoCreated_Index_Works_For_OneToMany_ForeignKey() {
		// Arrange - Book has [DataCacheForeignKey<Author>(OneToMany)] on AuthorId
		// This should auto-create a Many index on AuthorId
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Author 1", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "Book 1", AuthorId = 1, Year = 2020 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "Book 2", AuthorId = 1, Year = 2021 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Book 3", AuthorId = 1, Year = 2022 });

		// Act - Use the auto-created AuthorIdIndex
		var books = _bookCache.Query().WithAuthorId(1).Execute();
		// var s = _authorCache.Query().JoinOne(_bookCache, b => b.E);

		// Assert
		Assert.That(books.Count, Is.EqualTo(3));
	}

	[Test]
	public void AutoCreated_Index_Works_For_OneToOne_ForeignKey() {
		// Arrange - AuthorProfile has [DataCacheForeignKey<Author>(OneToOne)] on AuthorId
		// This should auto-create a Unique index on AuthorId
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Author 1", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "Author 2", Country = "UK" });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Bio 1", Website = "www.author1.com" });
		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 2, AuthorId = 2, Bio = "Bio 2", Website = "www.author2.com" });

		// Act - Use the auto-created AuthorIdIndex
		var profile = _authorProfileCache.Query().WithAuthorId(1).Execute();
		// _bookCache.Query().JoinOne(_authorProfileCache.Query(), a => a.WithAuthorId(2));

		// Assert
		Assert.That(profile.Count, Is.EqualTo(1));
		Assert.That(profile[0].Bio, Is.EqualTo("Bio 1"));
	}

	[Test]
	public void JoinWithBook_Works_On_AuthorCache() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "J.K. Rowling", Country = "UK" });
		_authorCache.AddOrUpdate(new Author { Id = 3, Name = "George Orwell", Country = "UK" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Harry Potter", AuthorId = 2, Year = 1997 });

		// Act - Join Author with Books (one-to-many)
		var __b1 = _authorCache.Query().JoinWithBook();
		var results = __b1.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(3));

		var stephenKing = results.FirstOrDefault(r => r.Left.Name == "Stephen King");
		Assert.That(stephenKing, Is.Not.Null);
		Assert.That(stephenKing.Right.Count, Is.EqualTo(2));

		var rowling = results.FirstOrDefault(r => r.Left.Name == "J.K. Rowling");
		Assert.That(rowling, Is.Not.Null);
		Assert.That(rowling.Right.Count, Is.EqualTo(1));

		var orwell = results.FirstOrDefault(r => r.Left.Name == "George Orwell");
		Assert.That(orwell, Is.Not.Null);
		Assert.That(orwell.Right.Count, Is.EqualTo(0)); // No books
	}

	[Test]
	public void JoinWithAuthorProfile_Works_On_AuthorCache() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "J.K. Rowling", Country = "UK" });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		// Act - Join Author with AuthorProfile (one-to-one)
		var __b1 = _authorCache.Query().JoinWithAuthorProfile();
		var results = __b1.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));

		var stephenKing = results.FirstOrDefault(r => r.Left.Name == "Stephen King");
		Assert.That(stephenKing, Is.Not.Null);
		Assert.That(stephenKing.Right, Is.Not.Null);
		Assert.That(stephenKing.Right.Bio, Is.EqualTo("Horror master"));

		var rowling = results.FirstOrDefault(r => r.Left.Name == "J.K. Rowling");
		Assert.That(rowling, Is.Not.Null);
		Assert.That(rowling.Right, Is.Null); // No profile
	}

	[Test]
	public void JoinWithBook_With_Filter_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Doctor Sleep", AuthorId = 1, Year = 2013 });

		// Act - Join with filter on the joined entity
		var __b1 = _authorCache.Query().JoinWithBook(q => q.Where(book => book.Year >= 2000));
		var results = __b1.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(1));
		Assert.That(results[0].Right[0].Title, Is.EqualTo("Doctor Sleep"));
	}

	[Test]
	public void JoinWithBookReview_Works_On_BookCache() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });

		_bookReviewCache.AddOrUpdate(new BookReview { Id = 1, BookId = 1, Rating = 5, Comment = "Amazing!" });
		_bookReviewCache.AddOrUpdate(new BookReview { Id = 2, BookId = 1, Rating = 4, Comment = "Great book" });
		_bookReviewCache.AddOrUpdate(new BookReview { Id = 3, BookId = 1, Rating = 5, Comment = "Scary!" });

		// Act - Join Book with Reviews (one-to-many)
		var __b1 = _bookCache.Query().JoinWithBookReview();
		var results = __b1.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(3));
	}

	[Test]
	public void Chained_JoinMany_Then_JoinOne_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "J.K. Rowling", Country = "UK" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Harry Potter", AuthorId = 2, Year = 1997 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });
		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 2, AuthorId = 2, Bio = "Fantasy writer", Website = "www.jkrowling.com" });

		// Act - Chain multiple joins: Author -> Book (Many) -> AuthorProfile (One)
		var __b1 = _authorCache.Query().JoinWithBook();
		var __b2 = __b1.JoinWithAuthorProfile();
		var results = __b2.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));

		var stephenKing = results.FirstOrDefault(r => r.Left.Name == "Stephen King");
		Assert.That(stephenKing, Is.Not.Null);
		Assert.That(stephenKing.Right.Count, Is.EqualTo(2)); // 2 books
		Assert.That(stephenKing.Right2, Is.Not.Null); // Has profile
		Assert.That(stephenKing.Right2!.Bio, Is.EqualTo("Horror master"));

		var rowling = results.FirstOrDefault(r => r.Left.Name == "J.K. Rowling");
		Assert.That(rowling, Is.Not.Null);
		Assert.That(rowling.Right.Count, Is.EqualTo(1)); // 1 book
		Assert.That(rowling.Right2, Is.Not.Null); // Has profile
		Assert.That(rowling.Right2!.Bio, Is.EqualTo("Fantasy writer"));
	}

	[Test]
	public void Chained_JoinOne_Then_JoinMany_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });

		// Act - Chain: Author -> AuthorProfile (One) -> Book (Many)
		var __b1 = _authorCache.Query().JoinWithAuthorProfile();
		var __b2 = __b1.JoinWithBook();
		var results = __b2.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Stephen King"));
		Assert.That(results[0].Right, Is.Not.Null); // Has profile
		Assert.That(results[0].Right!.Bio, Is.EqualTo("Horror master"));
		Assert.That(results[0].Right2.Count, Is.EqualTo(2)); // 2 books
	}

	// ==========================================
	// 5-Level Chained Join Tests
	// All joins reference the LEFT (root) Author entity
	// ==========================================

	[Test]
	public void Chained_3Level_JoinMany_JoinOne_JoinMany_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward
			{ Id = 2, AuthorId = 1, AwardName = "World Fantasy Award", Year = 2004 });

		// Act - 3-level chain: Author -> Book (Many) -> AuthorProfile (One) -> AuthorAward (Many)
		var __b1 = _authorCache.Query().JoinWithBook();
		var __b2 = __b1.JoinWithAuthorProfile();
		var __b3 = __b2.JoinWithAuthorAward();
		var results = __b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Stephen King"));
		Assert.That(results[0].Right.Count, Is.EqualTo(2)); // 2 books
		Assert.That(results[0].Right2, Is.Not.Null); // Has profile
		Assert.That(results[0].Right3.Count, Is.EqualTo(2)); // 2 awards
	}

	[Test]
	public void Chained_4Level_JoinMany_JoinOne_JoinMany_JoinOne_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });
		_authorCache.AddOrUpdate(new Author { Id = 2, Name = "J.K. Rowling", Country = "UK" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "Harry Potter", AuthorId = 2, Year = 1997 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });
		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 2, AuthorId = 2, Bio = "Fantasy writer", Website = "www.jkrowling.com" });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward { Id = 2, AuthorId = 2, AwardName = "Hugo Award", Year = 2001 });

		_authorPublisherCache.AddOrUpdate(new AuthorPublisher
			{ Id = 1, AuthorId = 1, PublisherName = "Scribner", Country = "USA" });
		_authorPublisherCache.AddOrUpdate(new AuthorPublisher
			{ Id = 2, AuthorId = 2, PublisherName = "Bloomsbury", Country = "UK" });

		// Act - 4-level chain: Author -> Book (Many) -> AuthorProfile (One) -> AuthorAward (Many) -> AuthorPublisher (One)
		var __b1 = _authorCache.Query().JoinWithBook();
		var __b2 = __b1.JoinWithAuthorProfile();
		var __b3 = __b2.JoinWithAuthorAward();
		var __b4 = __b3.JoinWithAuthorPublisher();
		var results = __b4.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(2));

		var stephenKing = results.FirstOrDefault(r => r.Left.Name == "Stephen King");
		Assert.That(stephenKing, Is.Not.Null);
		Assert.That(stephenKing.Right.Count, Is.EqualTo(1)); // 1 book
		Assert.That(stephenKing.Right2, Is.Not.Null); // Has profile
		Assert.That(stephenKing.Right3.Count, Is.EqualTo(1)); // 1 award
		Assert.That(stephenKing.Right4, Is.Not.Null); // Has publisher
		Assert.That(stephenKing.Right4!.PublisherName, Is.EqualTo("Scribner"));

		var rowling = results.FirstOrDefault(r => r.Left.Name == "J.K. Rowling");
		Assert.That(rowling, Is.Not.Null);
		Assert.That(rowling.Right4, Is.Not.Null);
		Assert.That(rowling.Right4!.PublisherName, Is.EqualTo("Bloomsbury"));
	}

	[Test]
	public void Chained_5Level_Full_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Doctor Sleep", AuthorId = 1, Year = 2013 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward
			{ Id = 2, AuthorId = 1, AwardName = "World Fantasy Award", Year = 2004 });

		_authorPublisherCache.AddOrUpdate(new AuthorPublisher
			{ Id = 1, AuthorId = 1, PublisherName = "Scribner", Country = "USA" });

		_authorEventCache.AddOrUpdate(new AuthorEvent
			{ Id = 1, AuthorId = 1, EventName = "Book Signing 2023", Location = "New York" });
		_authorEventCache.AddOrUpdate(new AuthorEvent
			{ Id = 2, AuthorId = 1, EventName = "Comic Con 2023", Location = "San Diego" });

		// Act - 5-level chain: Author -> Book -> AuthorProfile -> AuthorAward -> AuthorPublisher -> AuthorEvent
		var __b1 = _authorCache.Query().JoinWithBook();
		var __b2 = __b1.JoinWithAuthorProfile();
		var __b3 = __b2.JoinWithAuthorAward();
		var __b4 = __b3.JoinWithAuthorPublisher();
		var __b5 = __b4.JoinWithAuthorEvent();
		var results = __b5.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Stephen King"));
		Assert.That(results[0].Right.Count, Is.EqualTo(3)); // 3 books
		Assert.That(results[0].Right2, Is.Not.Null); // Has profile
		Assert.That(results[0].Right2!.Bio, Is.EqualTo("Horror master"));
		Assert.That(results[0].Right3.Count, Is.EqualTo(2)); // 2 awards
		Assert.That(results[0].Right4, Is.Not.Null); // Has publisher
		Assert.That(results[0].Right4!.PublisherName, Is.EqualTo("Scribner"));
		Assert.That(results[0].Right5.Count, Is.EqualTo(2)); // 2 events
	}

	[Test]
	public void Chained_5Level_AllOne_Works() {
		// Arrange - Create scenario where all joins are OneToOne
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_authorPublisherCache.AddOrUpdate(new AuthorPublisher
			{ Id = 1, AuthorId = 1, PublisherName = "Scribner", Country = "USA" });

		// Act - Chain only OneToOne relationships (repeat for 5 levels)
		var __b1 = _authorCache.Query().JoinWithAuthorProfile();
		var __b2 = __b1.JoinWithAuthorPublisher();
		var results = __b2.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Stephen King"));
		Assert.That(results[0].Right, Is.Not.Null);
		Assert.That(results[0].Right!.Bio, Is.EqualTo("Horror master"));
		Assert.That(results[0].Right2, Is.Not.Null);
		Assert.That(results[0].Right2!.PublisherName, Is.EqualTo("Scribner"));
	}

	[Test]
	public void Chained_5Level_AllMany_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward
			{ Id = 2, AuthorId = 1, AwardName = "World Fantasy Award", Year = 2004 });

		_authorEventCache.AddOrUpdate(new AuthorEvent
			{ Id = 1, AuthorId = 1, EventName = "Book Signing", Location = "New York" });

		// Act - Chain only OneToMany relationships
		var __b1 = _authorCache.Query().JoinWithBook();
		var __b2 = __b1.JoinWithAuthorAward();
		var __b3 = __b2.JoinWithAuthorEvent();
		var results = __b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Name, Is.EqualTo("Stephen King"));
		Assert.That(results[0].Right.Count, Is.EqualTo(2)); // 2 books
		Assert.That(results[0].Right2.Count, Is.EqualTo(2)); // 2 awards
		Assert.That(results[0].Right3.Count, Is.EqualTo(1)); // 1 event
	}

	[Test]
	public void Chained_5Level_WithFilters_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });
		_bookCache.AddOrUpdate(new Book { Id = 2, Title = "It", AuthorId = 1, Year = 1986 });
		_bookCache.AddOrUpdate(new Book { Id = 3, Title = "Doctor Sleep", AuthorId = 1, Year = 2013 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });
		_authorAwardCache.AddOrUpdate(new AuthorAward
			{ Id = 2, AuthorId = 1, AwardName = "World Fantasy Award", Year = 2004 });
		_authorAwardCache.AddOrUpdate(new AuthorAward
			{ Id = 3, AuthorId = 1, AwardName = "Medal for Distinguished Contribution", Year = 2015 });

		// Act - 3-level chain with filter on joined entity
		var __b1 = _authorCache.Query().JoinWithBook(q => q.Where(b => b.Year >= 2000));
		var __b2 = __b1.JoinWithAuthorProfile();
		var __b3 = __b2.JoinWithAuthorAward(q => q.Where(a => a.Year >= 2000));
		var results = __b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(1)); // Only Doctor Sleep (2013)
		Assert.That(results[0].Right[0].Title, Is.EqualTo("Doctor Sleep"));
		Assert.That(results[0].Right3.Count, Is.EqualTo(2)); // World Fantasy Award (2004) and Medal (2015)
	}

	[Test]
	public void Chained_DifferentOrderSameEntities_Works() {
		// Arrange
		_authorCache.AddOrUpdate(new Author { Id = 1, Name = "Stephen King", Country = "USA" });

		_bookCache.AddOrUpdate(new Book { Id = 1, Title = "The Shining", AuthorId = 1, Year = 1977 });

		_authorProfileCache.AddOrUpdate(new AuthorProfile
			{ Id = 1, AuthorId = 1, Bio = "Horror master", Website = "www.stephenking.com" });

		_authorAwardCache.AddOrUpdate(
			new AuthorAward { Id = 1, AuthorId = 1, AwardName = "Bram Stoker Award", Year = 1988 });

		// Act - Different order: AuthorProfile -> Book -> AuthorAward
		var __b1 = _authorCache.Query().JoinWithAuthorProfile();
		var __b2 = __b1.JoinWithBook();
		var __b3 = __b2.JoinWithAuthorAward();
		var results = __b3.Execute();

		// Assert
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right, Is.Not.Null); // AuthorProfile
		Assert.That(results[0].Right2.Count, Is.EqualTo(1)); // 1 book
		Assert.That(results[0].Right3.Count, Is.EqualTo(1)); // 1 award
	}
}

#endif

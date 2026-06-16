namespace Prague.Generated.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// FK-attribute sugar for nested joins:
//   Author → JoinWithBook (reverse 1:N) → each Book → JoinWithBookReview (reverse 1:N)
// proving the codegen-emitted nesting overload composes TOther's own JoinWith sugar inside the
// continuation, and that overload resolution picks the nesting overload (not the filter one).

[TestFixture]
public class NestedFkSugarTests {
	private DataCacheRegistry _registry = null!;
	private AuthorCache _authors = null!;
	private BookCache _books = null!;
	private BookReviewCache _reviews = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<AuthorCache>()
			.Register<BookCache>()
			.Register<BookReviewCache>()
			.Build();
		_authors = _registry.GetCache<AuthorCache>();
		_books = _registry.GetCache<BookCache>();
		_reviews = _registry.GetCache<BookReviewCache>();

		_authors.AddOrUpdate(new Author { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(new Author { Id = 2, Name = "Asimov" });
		_authors.AddOrUpdate(new Author { Id = 3, Name = "Orphan" }); // no books

		_books.AddOrUpdate(new Book { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_books.AddOrUpdate(new Book { Id = 102, AuthorId = 1, Title = "LOTR" });
		_books.AddOrUpdate(new Book { Id = 201, AuthorId = 2, Title = "Foundation" });

		_reviews.AddOrUpdate(new BookReview { Id = 1001, BookId = 101, Rating = 5, Comment = "great" });
		_reviews.AddOrUpdate(new BookReview { Id = 1002, BookId = 101, Rating = 4, Comment = "good" });
		_reviews.AddOrUpdate(new BookReview { Id = 1003, BookId = 102, Rating = 5, Comment = "epic" });
		// book 201 has no reviews
	}

	[Test]
	public void JoinWithBook_NestedJoinWithBookReview() {
		// QueryResults<JoinResult<Author, QueryResults<JoinResult<Book, QueryResults<BookReview>>>>>
		var results = _authors.Query()
			.JoinWithBook(b => b.JoinWithBookReview())
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byAuthor = results.ToDictionary(r => r.Left.Id);

		// Author 1: 2 books; book 101 → 2 reviews, book 102 → 1 review.
		Assert.That(byAuthor[1].Right.Count, Is.EqualTo(2));
		var a1books = byAuthor[1].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(a1books[101].Right.Count, Is.EqualTo(2));
		Assert.That(a1books[102].Right.Count, Is.EqualTo(1));

		// Author 2: 1 book; book 201 → 0 reviews.
		Assert.That(byAuthor[2].Right.Count, Is.EqualTo(1));
		Assert.That(byAuthor[2].Right.ToDictionary(jr => jr.Left.Id)[201].Right.Count, Is.EqualTo(0));

		// Author 3: no books → empty inner collection (outer JoinWith keeps the author).
		Assert.That(byAuthor[3].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinWithBook_Nested_DropsAuthorsWithNoBooks() {
		var results = _authors.Query()
			.InnerJoinWithBook(b => b.JoinWithBookReview())
			.Execute();

		// Author 3 (no books) dropped; authors 1 and 2 survive.
		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void JoinWithBook_Nested_InnerJoinWithBookReview() {
		// Exactly the requested shape: JoinWith{Bar}(b => b.InnerJoinWith{FooBar}()).
		// Inner InnerJoinWithBookReview drops books with no reviews (book 201); outer JoinWithBook
		// keeps the author with an empty inner collection.
		var results = _authors.Query()
			.JoinWithBook(b => b.InnerJoinWithBookReview())
			.Execute();

		var byAuthor = results.ToDictionary(r => r.Left.Id);
		Assert.That(byAuthor[1].Right.Count, Is.EqualTo(2), "both of author 1's books have reviews");
		Assert.That(byAuthor[2].Right.Count, Is.EqualTo(0), "author 2's only book (201) has no reviews → dropped");
		Assert.That(byAuthor[3].Right.Count, Is.EqualTo(0), "author 3 has no books");
	}

	[Test]
	public void JoinWithBook_FilterOverload_StillResolves() {
		// The flat filter overload must still work (the nesting overload is additive).
		var results = _authors.Query()
			.JoinWithBook(b => b.Where(book => book.Title == "Hobbit"))
			.Execute();

		var byAuthor = results.ToDictionary(r => r.Left.Id);
		Assert.That(byAuthor[1].Right.Select(b => b.Title), Is.EquivalentTo(new[] { "Hobbit" }));
		Assert.That(byAuthor[2].Right.Count, Is.EqualTo(0));
	}
}

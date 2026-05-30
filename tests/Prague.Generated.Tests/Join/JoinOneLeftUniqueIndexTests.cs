namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// LeftUq{Author,Book} — each Author owns exactly one Book (1:1). The unique FK
// is on the LEFT side (Author.BookId), mirror of the right-unique-index case.

[DataCache]
public partial class LeftUqAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	/// <summary>
	/// FK pointing to <see cref="LeftUqBook.Id"/>. Symmetric = true means codegen emits
	/// <see cref="LeftUqAuthorCache.BookIdIndex"/> as <c>CacheSymmetricUniqueIndex</c>
	/// (public <c>Reverse</c>).
	/// </summary>
	[DataCacheIndex(DataCacheIndexType.Unique, Symmetric = true)]
	public int BookId { get; set; }
}

[DataCache]
public partial class LeftUqBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";
	public string Genre { get; set; } = "";
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneLeftUniqueIndexTests {
	private DataCacheRegistry _registry = null!;
	private LeftUqAuthorCache _authorCache = null!;
	private LeftUqBookCache _bookCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<LeftUqAuthorCache>()
			.Register<LeftUqBookCache>()
			.Build();
		_authorCache = _registry.GetCache<LeftUqAuthorCache>();
		_bookCache = _registry.GetCache<LeftUqBookCache>();
	}

	// ── Test 1: full match ────────────────────────────────────────────────────

	[Test]
	public void JoinOne_AuthorToBook_MatchExists_AttachesBook() {
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Cache.Query()
			.JoinOne(_authorCache.BookIdIndex, _bookCache)
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
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 101, Title = "Book A", Genre = "fiction" });
		// 102 intentionally NOT added to book cache.
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Cache.Query()
			.JoinOne(_authorCache.BookIdIndex, _bookCache)
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
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 3, Name = "Carol", BookId = 103 });

		var results = _authorCache.Cache.Query()
			.JoinOne(_authorCache.BookIdIndex, _bookCache,
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
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 102, Title = "Book B", Genre = "history" });

		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 2, Name = "Bob",   BookId = 102 });

		var results = _authorCache.Cache.Query()
			.JoinOne(_authorCache.BookIdIndex, _bookCache,
				q => q.Where(b => b.Genre == "nonexistent"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right == null), Is.True);
	}

	// ── Test 5: filter + TArg static lambda (zero-alloc) ─────────────────────

	[Test]
	public void JoinOne_AuthorToBook_WithFilterAndArg_StaticLambda() {
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 101, Title = "Book A", Genre = "fiction" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 102, Title = "Book B", Genre = "history" });
		_bookCache.AddOrUpdate(new LeftUqBook { Id = 103, Title = "Book C", Genre = "fiction" });

		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 1, Name = "Alice", BookId = 101 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 2, Name = "Bob",   BookId = 102 });
		_authorCache.AddOrUpdate(new LeftUqAuthor { Id = 3, Name = "Carol", BookId = 103 });

		const string targetGenre = "fiction";
		var results = _authorCache.Cache.Query()
			.JoinOne(_authorCache.BookIdIndex, _bookCache,
				static (q, g) => q.Where(b => b.Genre == g),
				targetGenre)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "history book filtered out");
		Assert.That(byId[3].Right, Is.Not.Null);
	}
}

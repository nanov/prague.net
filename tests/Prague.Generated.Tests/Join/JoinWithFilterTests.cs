namespace Prague.Generated.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Models exercising the codegen-emitted filter overloads on JoinWith{X} / InnerJoinWith{X}.
//   JwAuthor — right side of the forward ManyToOne join; Tier is indexed so WithTier exists.
//   JwBook   — left side; AuthorId is a raw ManyToOne FK to JwAuthor. Genre is indexed so
//              WithGenre exists on the reverse (JwAuthor → JoinWithJwBook) join.

[DataCache]
public partial class JwAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	[DataCacheIndex(DataCacheIndexType.Many)] public string Tier { get; set; } = string.Empty;
}

[DataCache]
public partial class JwBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;
	[DataCacheIndex(DataCacheIndexType.Many)] public string Genre { get; set; } = string.Empty;

	[DataCacheForeignKey<JwAuthor>(DataCacheJoinType.ManyToOne)]
	public int AuthorId { get; set; }
}

[TestFixture]
public class JoinWithFilterTests {
	private DataCacheRegistry _registry = null!;
	private JwAuthorCache _authors = null!;
	private JwBookCache _books = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<JwAuthorCache>()
			.Register<JwBookCache>()
			.Build();
		_authors = _registry.GetCache<JwAuthorCache>();
		_books = _registry.GetCache<JwBookCache>();

		// Authors: 1 gold, 2 silver, 3 silver (3 has only non-fantasy books).
		_authors.AddOrUpdate(new JwAuthor { Id = 1, Name = "Tolkien", Tier = "gold" });
		_authors.AddOrUpdate(new JwAuthor { Id = 2, Name = "Lewis", Tier = "silver" });
		_authors.AddOrUpdate(new JwAuthor { Id = 3, Name = "Sayers", Tier = "silver" });

		// Books (5 by gold/silver authors + one for author 3).
		_books.AddOrUpdate(new JwBook { Id = 10, Title = "LotR", Genre = "fantasy", AuthorId = 1 });
		_books.AddOrUpdate(new JwBook { Id = 11, Title = "Hobbit", Genre = "fantasy", AuthorId = 1 });
		_books.AddOrUpdate(new JwBook { Id = 12, Title = "Letters", Genre = "nonfiction", AuthorId = 1 });
		_books.AddOrUpdate(new JwBook { Id = 13, Title = "Narnia", Genre = "fantasy", AuthorId = 2 });
		_books.AddOrUpdate(new JwBook { Id = 14, Title = "Mere", Genre = "nonfiction", AuthorId = 2 });
		_books.AddOrUpdate(new JwBook { Id = 15, Title = "Essay", Genre = "nonfiction", AuthorId = 3 });
	}

	// ── Forward ManyToOne (path 3: JoinOne via JoinOneLeftSymResolver) ─────────

	[Test]
	public void Forward_JoinWith_Filter_NullsOutFilteredRight() {
		// Keep only gold authors; books by silver authors get a null right (outer).
		using var results = _books.Query()
			.JoinWithJwAuthor(q => q.WithTier("gold"))
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(6));
		var nonNull = results.Where(r => r.Right is not null).ToList();
		Assert.That(nonNull.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 10, 11, 12 }));
		Assert.That(nonNull.All(r => r.Right!.Tier == "gold"), Is.True);
		Assert.That(results.Where(r => r.Right is null).Select(r => r.Left.Id).OrderBy(x => x),
			Is.EqualTo(new[] { 13, 14, 15 }));
	}

	[Test]
	public void Forward_JoinWith_FilterArg_NullsOutFilteredRight() {
		var tier = "gold";
		using var results = _books.Query()
			.JoinWithJwAuthor(static (q, t) => q.WithTier(t), tier)
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(6));
		Assert.That(results.Count(r => r.Right is not null), Is.EqualTo(3));
		Assert.That(results.Where(r => r.Right is not null).All(r => r.Right!.Tier == "gold"), Is.True);
	}

	[Test]
	public void Forward_InnerJoinWith_Filter_DropsFilteredLefts() {
		// Inner: lefts whose (filtered) right is rejected are dropped entirely.
		using var results = _books.Query()
			.InnerJoinWithJwAuthor(q => q.WithTier("gold"))
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 10, 11, 12 }));
		Assert.That(results.All(r => r.Right is not null && r.Right.Tier == "gold"), Is.True);
	}

	[Test]
	public void Forward_InnerJoinWith_FilterArg_DropsFilteredLefts() {
		var tier = "silver";
		using var results = _books.Query()
			.InnerJoinWithJwAuthor(static (q, t) => q.WithTier(t), tier)
			.ExecutePooled();

		// Silver authors are 2 and 3 → their books: 13, 14 (author 2), 15 (author 3).
		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 13, 14, 15 }));
		Assert.That(results.All(r => r.Right is not null && r.Right.Tier == "silver"), Is.True);
	}

	// ── Reverse ManyToOne (path 1: JoinMany via JoinManyRightListIndexResolver) ─

	[Test]
	public void Reverse_JoinWith_Filter_NarrowsPerLeftCollection() {
		// Each author's joined-in books are narrowed to fantasy; author with no fantasy
		// keeps an empty collection (outer).
		using var results = _authors.Query()
			.JoinWithJwBook(q => q.WithGenre("fantasy"))
			.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			var fantasy = row.Right!;
			Assert.That(fantasy.All(b => b.Genre == "fantasy"), Is.True);
			var expected = row.Left.Id switch {
				1 => 2, // LotR + Hobbit
				2 => 1, // Narnia
				3 => 0, // none
				_ => -1
			};
			Assert.That(fantasy.Count, Is.EqualTo(expected), $"author {row.Left.Id}");
		}
	}

	[Test]
	public void Reverse_InnerJoinWith_Filter_DropsLeftsWithEmptyCollection() {
		// Inner: authors whose filtered book collection is empty are dropped.
		using var results = _authors.Query()
			.InnerJoinWithJwBook(q => q.WithGenre("fantasy"))
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
		foreach (var row in results)
			Assert.That(row.Right!.Count, Is.GreaterThan(0));
	}

	[Test]
	public void Reverse_InnerJoinWith_FilterArg_DropsLeftsWithEmptyCollection() {
		var genre = "fantasy";
		using var results = _authors.Query()
			.InnerJoinWithJwBook(static (q, g) => q.WithGenre(g), genre)
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
	}
}

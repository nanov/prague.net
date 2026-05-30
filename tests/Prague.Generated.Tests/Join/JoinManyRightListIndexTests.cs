namespace Prague.Generated.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
//   JmnAuthor (PK only)
//   JmnBook   (PK + ManyToOne FK to JmnAuthor.Id via AuthorId)
//
// The ManyToOne FK declaration causes codegen to emit a
// CacheSymmetricKeyValueListIndex on JmnBook.AuthorIdIndex. JoinMany uses
// this directly via the extension that takes CacheKeyValueListIndex (the base
// class — symmetric is a subclass).

[DataCache]
public partial class JmnAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class JmnBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;
	public string Genre { get; set; } = string.Empty;

	[DataCacheForeignKey<JmnAuthor>(DataCacheJoinType.ManyToOne)]
	public int AuthorId { get; set; }
}

[TestFixture]
public class JoinManyRightListIndexTests {
	private DataCacheRegistry _registry = null!;
	private JmnAuthorCache _authors = null!;
	private JmnBookCache _books = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<JmnAuthorCache>()
			.Register<JmnBookCache>()
			.Build();
		_authors = _registry.GetCache<JmnAuthorCache>();
		_books = _registry.GetCache<JmnBookCache>();

		_authors.AddOrUpdate(new JmnAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(new JmnAuthor { Id = 2, Name = "OrphanAuthor" });
		_authors.AddOrUpdate(new JmnAuthor { Id = 3, Name = "Asimov" });

		_books.AddOrUpdate(new JmnBook { Id = 101, AuthorId = 1, Title = "Hobbit", Genre = "Fantasy" });
		_books.AddOrUpdate(new JmnBook { Id = 102, AuthorId = 1, Title = "LOTR", Genre = "Fantasy" });
		_books.AddOrUpdate(new JmnBook { Id = 103, AuthorId = 1, Title = "Silmarillion", Genre = "Fantasy" });
		_books.AddOrUpdate(new JmnBook { Id = 301, AuthorId = 3, Title = "Foundation", Genre = "SciFi" });
		// Author 2 has no books.
	}

	// ── Outer ────────────────────────────────────────────────────────────────

	[Test]
	public void JoinMany_Codegen_OuterFullMatch_FansOut() {
		var results = _authors.Query().JoinMany(_books, _books.AuthorIdIndex).Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(3));
		Assert.That(byId[2].Right.Count, Is.EqualTo(0), "Orphan author");
		Assert.That(byId[3].Right.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_Codegen_FilterNarrowsBooks_PerAuthor() {
		var results = _authors.Query()
			.JoinMany(_books, _books.AuthorIdIndex,
				q => q.Where(b => b.Title.StartsWith("H") || b.Title.StartsWith("L")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		var tolkienTitles = byId[1].Right.Select(b => b.Title).OrderBy(t => t).ToArray();
		Assert.That(tolkienTitles, Is.EqualTo(new[] { "Hobbit", "LOTR" }));
		Assert.That(byId[2].Right.Count, Is.EqualTo(0));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0), "Foundation filtered out");
	}

	[Test]
	public void JoinMany_Codegen_FilterWithArg_StaticLambda() {
		const string genre = "Fantasy";
		var results = _authors.Query()
			.JoinMany(_books, _books.AuthorIdIndex,
				static (q, g) => q.Where(b => b.Genre == g),
				genre)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(3));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0), "Foundation is SciFi, filtered");
	}

	// ── Inner ────────────────────────────────────────────────────────────────

	[Test]
	public void InnerJoinMany_Codegen_DropsOrphans() {
		var results = _authors.Query().InnerJoinMany(_books, _books.AuthorIdIndex).Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void InnerJoinMany_Codegen_FilterDropsAuthorsWithNoMatches() {
		var results = _authors.Query()
			.InnerJoinMany(_books, _books.AuthorIdIndex,
				q => q.Where(b => b.Genre == "SciFi"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(3));
		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void InnerJoinMany_Codegen_FilterAndArg_StaticLambda() {
		const string prefix = "Hob";
		var results = _authors.Query()
			.InnerJoinMany(_books, _books.AuthorIdIndex,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(1));
		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Hobbit"));
	}
}

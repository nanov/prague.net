namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── ManyToOne models (smoke tests for the codegen emission) ────────────────
//   M2OAuthor    (PK)
//   M2OPublisher (PK)
//   M2OBook      → AuthorId, PublisherId (both ManyToOne) — two forward FKs to test forward chains.
//   M2OAward     → AuthorId (ManyToOne) — second reverse-referencer of Author for reverse chains.

[DataCache]
public partial class M2OAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class M2OPublisher {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class M2OBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;

	[DataCacheForeignKey<M2OAuthor>(DataCacheJoinType.ManyToOne)]
	public int AuthorId { get; set; }

	[DataCacheForeignKey<M2OPublisher>(DataCacheJoinType.ManyToOne)]
	public int PublisherId { get; set; }
}

[DataCache]
public partial class M2OAward {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;

	[DataCacheForeignKey<M2OAuthor>(DataCacheJoinType.ManyToOne)]
	public int AuthorId { get; set; }
}

[TestFixture]
public class ManyToOneCodegenTests {
	private DataCacheRegistry _registry = null!;
	private M2OAuthorCache _authors = null!;
	private M2OPublisherCache _publishers = null!;
	private M2OBookCache _books = null!;
	private M2OAwardCache _awards = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<M2OAuthorCache>()
			.Register<M2OPublisherCache>()
			.Register<M2OBookCache>()
			.Register<M2OAwardCache>()
			.Build();
		_authors = _registry.GetCache<M2OAuthorCache>();
		_publishers = _registry.GetCache<M2OPublisherCache>();
		_books = _registry.GetCache<M2OBookCache>();
		_awards = _registry.GetCache<M2OAwardCache>();

		_authors.AddOrUpdate(new M2OAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(new M2OAuthor { Id = 2, Name = "Lewis" });
		_publishers.AddOrUpdate(new M2OPublisher { Id = 100, Name = "Allen & Unwin" });
		_publishers.AddOrUpdate(new M2OPublisher { Id = 101, Name = "Geoffrey Bles" });
		_books.AddOrUpdate(new M2OBook { Id = 10, Title = "LotR", AuthorId = 1, PublisherId = 100 });
		_books.AddOrUpdate(new M2OBook { Id = 11, Title = "Hobbit", AuthorId = 1, PublisherId = 100 });
		_books.AddOrUpdate(new M2OBook { Id = 12, Title = "Narnia", AuthorId = 2, PublisherId = 101 });
		_awards.AddOrUpdate(new M2OAward { Id = 1000, Title = "Hugo", AuthorId = 1 });
		_awards.AddOrUpdate(new M2OAward { Id = 1001, Title = "Carnegie", AuthorId = 2 });
	}

	// ── Index shape ──────────────────────────────────────────────────────────

	[Test]
	public void ManyToOneFk_AutoEmitsSymmetricListIndex() {
		Assert.That(_books.AuthorIdIndex.GetType().Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
		Assert.That(_books.PublisherIdIndex.GetType().Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
		Assert.That(_awards.AuthorIdIndex.GetType().Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
	}

	// ── Level 0 ──────────────────────────────────────────────────────────────

	[Test]
	public void Level0_Forward_JoinWithAuthor() {
		using var results = _books.Query().JoinWithM2OAuthor().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results)
			Assert.That(row.Right, Is.Not.Null);
	}

	[Test]
	public void Level0_Forward_InnerJoinWithAuthor_DropsOrphans() {
		_books.AddOrUpdate(new M2OBook { Id = 999, Title = "Orphan", AuthorId = 9999, PublisherId = 100 });
		using var results = _books.Query().InnerJoinWithM2OAuthor().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results)
			Assert.That(row.Right, Is.Not.Null);
	}

	[Test]
	public void Level0_Reverse_JoinWithBook() {

		using var results = _authors.Query().JoinWithM2OBook().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			var books = row.Right!;
			if (row.Left.Id == 1) Assert.That(books.Count, Is.EqualTo(2));
			else Assert.That(books.Count, Is.EqualTo(1));
		}

	}

	// ── Level 1 chains ───────────────────────────────────────────────────────

	[Test]
	public void Level1_Forward_BookJoinAuthorJoinPublisher() {
		using var results = _books.Query().JoinWithM2OAuthor().JoinWithM2OPublisher().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null, "Author");
			Assert.That(row.Right2, Is.Not.Null, "Publisher");
		}
	}

	[Test]
	public void Level1_Forward_BookJoinPublisherJoinAuthor_OrderIrrelevant() {
		using var results = _books.Query().JoinWithM2OPublisher().JoinWithM2OAuthor().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null, "Publisher");
			Assert.That(row.Right2, Is.Not.Null, "Author");
		}
	}

	[Test]
	public void Level1_Reverse_AuthorJoinBookJoinAward() {

		using var results = _authors.Query().JoinWithM2OBook().JoinWithM2OAward().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null, "Books");
			Assert.That(row.Right2, Is.Not.Null, "Awards");
			Assert.That(row.Right!.Count, Is.GreaterThan(0));
			Assert.That(row.Right2!.Count, Is.EqualTo(1));
		}

	}

	[Test]
	public void Level1_Mixed_AuthorJoinBookForward_HopsBack_Inapplicable() {

		// Reverse JoinMany returns QueryResults<Book>; further chains stay on AuthorCache's
		// joinable surface (Author's relationships). Books-of-an-author can't be re-joined to
		// Book's outbound FKs from within the Author-discriminated builder.
		// This test just documents that chained reverse stays Author-centric.
		using var results = _authors.Query().JoinWithM2OBook().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));

	}

	// ── Level 2+ chains (sanity — same emission logic per level) ─────────────

	[Test]
	public void Level2_Reverse_AuthorJoinBookJoinAwardJoinBookAgain() {

		// Author → Books (level 0) → Awards (level 1) → Books again (level 2).
		// Demonstrates that codegen emits JoinWith at level 2+ as well.
		using var results = _authors.Query()
			.JoinWithM2OBook()
			.JoinWithM2OAward()
			.JoinWithM2OBook()
			.ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			Assert.That(row.Right, Is.Not.Null);
			Assert.That(row.Right2, Is.Not.Null);
			Assert.That(row.Right3, Is.Not.Null);
		}

	}
}

namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Dual-form FK models ─────────────────────────────────────────────────────
//   The FK lives on DfBook.AuthorId, but it's DECLARED on DfAuthor.Id via the
//   dual-form attribute: [DataCacheForeignKey<DfBook>(OneToMany, nameof(DfBook.AuthorId))]
//   Codegen should treat this exactly like placing
//   [DataCacheForeignKey<DfAuthor>(ManyToOne)] on DfBook.AuthorId.

[DataCache]
public partial class DfAuthor {
	[DataCacheKey]
	[DataCacheForeignKey<DfBook>(DataCacheJoinType.OneToMany, nameof(DfBook.AuthorId))]
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class DfBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;
	public int AuthorId { get; set; }
}

[TestFixture]
public class DualFormForeignKeyTests {
	private DataCacheRegistry _registry = null!;
	private DfAuthorCache _authors = null!;
	private DfBookCache _books = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<DfAuthorCache>()
			.Register<DfBookCache>()
			.Build();
		_authors = _registry.GetCache<DfAuthorCache>();
		_books = _registry.GetCache<DfBookCache>();

		_authors.AddOrUpdate(new DfAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(new DfAuthor { Id = 2, Name = "Lewis" });
		_books.AddOrUpdate(new DfBook { Id = 10, Title = "LotR", AuthorId = 1 });
		_books.AddOrUpdate(new DfBook { Id = 11, Title = "Hobbit", AuthorId = 1 });
		_books.AddOrUpdate(new DfBook { Id = 12, Title = "Narnia", AuthorId = 2 });
	}

	[Test]
	public void DualForm_AutoEmitsSymmetricListIndexOnFkSide() {
		// Index lives on the FK property side (DfBook.AuthorId), NOT on the anchor (DfAuthor.Id).
		Assert.That(_books.AuthorIdIndex.GetType().Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
	}

	[Test]
	public void DualForm_ForwardJoinWithAuthor_EmittedOnBookCache() {
		// JoinWithDfAuthor() should appear on DfBookCache.Query() exactly as if the direct-form
		// [DataCacheForeignKey<DfAuthor>(ManyToOne)] had been placed on DfBook.AuthorId.
		using var results = _books.Query().JoinWithDfAuthor().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results)
			Assert.That(row.Right, Is.Not.Null);
	}

	[Test]
	public void DualForm_ForwardInnerJoinWithAuthor_DropsOrphans() {
		_books.AddOrUpdate(new DfBook { Id = 999, Title = "Orphan", AuthorId = 9999 });
		using var results = _books.Query().InnerJoinWithDfAuthor().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results)
			Assert.That(row.Right, Is.Not.Null);
	}

	[Test]
	public void DualForm_ReverseJoinWithBook_EmittedOnAuthorCache() {
		var comparer = Comparer<DfAuthor>.Create((a, b) => a.Id.CompareTo(b.Id));

		// JoinWithDfBook() should appear on DfAuthorCache.Query() — the dual-form attr is placed
		// on Author.Id but the joinable inference should still find that DfBook is reachable.
		// Also exercises Sort→JoinWith on a sorted builder (regression: the codegen-emitted
		// JoinWith{Other} must accept a SortedQuery<TInner>-wrapped discriminator).
		using var results = _authors
			.Query()
			.Sort(comparer)
			.JoinWithDfBook()
			.ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			var books = row.Right!;
			if (row.Left.Id == 1) Assert.That(books.Count, Is.EqualTo(2));
			else Assert.That(books.Count, Is.EqualTo(1));
		}

	}
}

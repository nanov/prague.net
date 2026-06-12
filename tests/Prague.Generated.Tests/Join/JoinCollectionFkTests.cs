namespace Prague.Generated.Tests.Join;

using System.Collections.Generic;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Layer 3: [DataCacheForeignKey<CfkTag>(ManyToOne)] on a COLLECTION property (List<int> TagIds).
//   Forward  — CfkBook.JoinWithCfkTag()  → each book joined with its referenced tags (QueryResults).
//   Reverse  — CfkTag.JoinWithCfkBook()  → each tag joined with the books referencing it.
// Both directions go through the auto-created symmetric collection index TagIdsIndex on the book cache.
// Tests use Execute() (materialized) since each JoinResult's per-left QueryResults is stashed in a
// dictionary and re-read after enumeration — a lifetime the pooled path intentionally doesn't support.

[DataCache]
public partial class CfkTag {
	[DataCacheKey] public int Id { get; set; }
	[DataCacheIndex(DataCacheIndexType.Many)] public string Name { get; set; } = "";
}

[DataCache]
public partial class CfkBook {
	[DataCacheKey] public int Id { get; set; }
	[DataCacheIndex(DataCacheIndexType.Many)] public string Title { get; set; } = "";

	[DataCacheForeignKey<CfkTag>(DataCacheJoinType.ManyToOne)]
	public List<int> TagIds { get; set; } = new();
}

[TestFixture]
public class JoinCollectionFkTests {
	private DataCacheRegistry _registry = null!;
	private CfkTagCache _tags = null!;
	private CfkBookCache _books = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CfkTagCache>()
			.Register<CfkBookCache>()
			.Build();
		_tags = _registry.GetCache<CfkTagCache>();
		_books = _registry.GetCache<CfkBookCache>();

		// Tags 10 / 20 / 30.
		_tags.AddOrUpdate(new CfkTag { Id = 10, Name = "fantasy" });
		_tags.AddOrUpdate(new CfkTag { Id = 20, Name = "classic" });
		_tags.AddOrUpdate(new CfkTag { Id = 30, Name = "scifi" });

		// Book 1 → {10, 20}; Book 2 → {10} (shares tag 10 with book 1); Book 3 → {} (untagged).
		_books.AddOrUpdate(new CfkBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 20 } });
		_books.AddOrUpdate(new CfkBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 10 } });
		_books.AddOrUpdate(new CfkBook { Id = 3, Title = "Untagged", TagIds = new List<int>() });
	}

	// ── Forward: CfkBook → tags ───────────────────────────────────────────────

	[Test]
	public void Forward_JoinWithCfkTag_JoinsEachBookWithItsTags() {
		var results = _books.Query()
			.JoinWithCfkTag()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byBook = results.ToDictionary(r => r.Left.Id);

		var book1 = byBook[1].Right.Select(t => t.Name).OrderBy(n => n).ToArray();
		Assert.That(book1, Is.EqualTo(new[] { "classic", "fantasy" }));

		// Shared tag (10 = fantasy) appears for both owning books.
		var book2 = byBook[2].Right.Select(t => t.Name).ToArray();
		Assert.That(book2, Is.EqualTo(new[] { "fantasy" }));

		// Empty TagIds → empty QueryResults.
		Assert.That(byBook[3].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void Forward_InnerJoinWithCfkTag_DropsBooksWithNoTags() {
		var results = _books.Query()
			.InnerJoinWithCfkTag()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Forward_JoinWithCfkTag_Filter_NarrowsTags() {
		// Keep only the "fantasy" tag on the joined-in right side.
		var results = _books.Query()
			.JoinWithCfkTag(q => q.WithName("fantasy"))
			.Execute();

		var byBook = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBook[1].Right.Select(t => t.Name).ToArray(), Is.EqualTo(new[] { "fantasy" }));
		Assert.That(byBook[2].Right.Select(t => t.Name).ToArray(), Is.EqualTo(new[] { "fantasy" }));
		Assert.That(byBook[3].Right.Count, Is.EqualTo(0));
	}

	// ── Reverse: CfkTag → books ───────────────────────────────────────────────

	[Test]
	public void Reverse_JoinWithCfkBook_JoinsEachTagWithReferencingBooks() {
		var results = _tags.Query()
			.JoinWithCfkBook()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byTag = results.ToDictionary(r => r.Left.Id);

		// Tag 10 (fantasy) is referenced by BOTH book 1 and book 2.
		Assert.That(byTag[10].Right.Select(b => b.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
		// Tag 20 (classic) only by book 1.
		Assert.That(byTag[20].Right.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 1 }));
		// Tag 30 (scifi) by nobody.
		Assert.That(byTag[30].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void Reverse_InnerJoinWithCfkBook_DropsTagsWithNoBooks() {
		var results = _tags.Query()
			.InnerJoinWithCfkBook()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 10, 20 }));
	}

	[Test]
	public void Reverse_JoinWithCfkBook_Filter_NarrowsBooks() {
		var results = _tags.Query()
			.JoinWithCfkBook(q => q.WithTitle("Hobbit"))
			.Execute();

		var byTag = results.ToDictionary(r => r.Left.Id);
		// Only book 1 ("Hobbit") survives the filter; it carries tags 10 and 20.
		Assert.That(byTag[10].Right.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 1 }));
		Assert.That(byTag[20].Right.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 1 }));
		Assert.That(byTag[30].Right.Count, Is.EqualTo(0));
	}

	// ── Index maintenance: update a book's TagIds, re-query both directions ────

	[Test]
	public void IndexMaintenance_UpdateTagIds_ReflectedBothDirections() {
		// Move book 2 from tag 10 to tag 30.
		_books.AddOrUpdate(new CfkBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 30 } });

		// Forward: book 2 now joins to scifi only.
		var forward = _books.Query().JoinWithCfkTag().Execute();
		var byBook = forward.ToDictionary(r => r.Left.Id);
		Assert.That(byBook[2].Right.Select(t => t.Name).ToArray(), Is.EqualTo(new[] { "scifi" }));

		// Reverse: tag 10 now only by book 1; tag 30 now by book 2.
		var reverse = _tags.Query().JoinWithCfkBook().Execute();
		var byTag = reverse.ToDictionary(r => r.Left.Id);
		Assert.That(byTag[10].Right.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 1 }));
		Assert.That(byTag[30].Right.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 2 }));
	}
}

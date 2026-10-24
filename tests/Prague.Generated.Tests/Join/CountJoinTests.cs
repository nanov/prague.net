namespace Prague.Generated.Tests.Join;

using System.Collections.Generic;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Generated FK CountJoinWith{T} / InnerCountJoinWith{T}: emitted next to every JoinWith{T} whose right side
// is a QueryResults (reverse one-to-many, collection reverse, collection forward). Pinned against the
// JoinWith{T} twin's Right.Count, eager and frozen (the frozen twin takes the pipeline). Fixtures:
// JmnAuthor / JmnBook (ManyToOne FK AuthorId), CfkTag / CfkBook (collection FK TagIds).
[TestFixture]
public class CountJoinTests {
	private DataCacheRegistry _registry = null!;
	private JmnAuthorCache _authors = null!;
	private JmnBookCache _books = null!;
	private CfkTagCache _tags = null!;
	private CfkBookCache _taggedBooks = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<JmnAuthorCache>()
			.Register<JmnBookCache>()
			.Register<CfkTagCache>()
			.Register<CfkBookCache>()
			.Build();
		_authors = _registry.GetCache<JmnAuthorCache>();
		_books = _registry.GetCache<JmnBookCache>();
		_tags = _registry.GetCache<CfkTagCache>();
		_taggedBooks = _registry.GetCache<CfkBookCache>();

		_authors.AddOrUpdate(new JmnAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(new JmnAuthor { Id = 2, Name = "OrphanAuthor" });
		_authors.AddOrUpdate(new JmnAuthor { Id = 3, Name = "Asimov" });
		_books.AddOrUpdate(new JmnBook { Id = 101, AuthorId = 1, Title = "Hobbit", Genre = "Fantasy" });
		_books.AddOrUpdate(new JmnBook { Id = 102, AuthorId = 1, Title = "LOTR", Genre = "Fantasy" });
		_books.AddOrUpdate(new JmnBook { Id = 103, AuthorId = 1, Title = "Letters", Genre = "Biography" });
		_books.AddOrUpdate(new JmnBook { Id = 301, AuthorId = 3, Title = "Foundation", Genre = "SciFi" });

		_tags.AddOrUpdate(new CfkTag { Id = 10, Name = "fantasy" });
		_tags.AddOrUpdate(new CfkTag { Id = 20, Name = "classic" });
		_tags.AddOrUpdate(new CfkTag { Id = 30, Name = "scifi" });
		// Book 3 references tag 99, which does not exist: it is not counted.
		_taggedBooks.AddOrUpdate(new CfkBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 20 } });
		_taggedBooks.AddOrUpdate(new CfkBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 10 } });
		_taggedBooks.AddOrUpdate(new CfkBook { Id = 3, Title = "Ghost", TagIds = new List<int> { 99 } });
		_taggedBooks.AddOrUpdate(new CfkBook { Id = 4, Title = "Untagged", TagIds = new List<int>() });
	}

	private static Dictionary<int, int> Counts<TLeft>(QueryResults<JoinResult<TLeft, int>> results, Func<TLeft, int> id) {
		var map = new Dictionary<int, int>();
		foreach (var r in results)
			map.Add(id(r.Left), r.Right);
		return map;
	}

	private static Dictionary<int, int> ManyCounts<TLeft, TRight>(QueryResults<JoinResult<TLeft, QueryResults<TRight>>> results, Func<TLeft, int> id) {
		var map = new Dictionary<int, int>();
		foreach (var r in results)
			map.Add(id(r.Left), r.Right.Count);
		return map;
	}

	// ── Reverse one-to-many ─────────────────────────────────────────────────────

	[Test]
	public void CountJoinWith_ReverseOneToMany_OuterInner() {
		var outer = Counts(_authors.Query().CountJoinWithJmnBook().Execute(), static a => a.Id);
		var inner = Counts(_authors.Query().InnerCountJoinWithJmnBook().Execute(), static a => a.Id);

		Assert.That(outer, Is.EqualTo(new Dictionary<int, int> { [1] = 3, [2] = 0, [3] = 1 }));
		Assert.That(inner, Is.EqualTo(new Dictionary<int, int> { [1] = 3, [3] = 1 }));
		Assert.That(outer, Is.EqualTo(ManyCounts(_authors.Query().JoinWithJmnBook().Execute(), static a => a.Id)));
	}

	[Test]
	public void CountJoinWith_ReverseOneToMany_Filter_And_FilterWithArg() {
		var filtered = Counts(_authors.Query().CountJoinWithJmnBook(static q => q.Where(static b => b.Genre == "Fantasy")).Execute(), static a => a.Id);
		var innerArg = Counts(_authors.Query().InnerCountJoinWithJmnBook(static (q, g) => q.Where(b => b.Genre == g), "SciFi").Execute(), static a => a.Id);

		Assert.That(filtered, Is.EqualTo(new Dictionary<int, int> { [1] = 2, [2] = 0, [3] = 0 }));
		Assert.That(innerArg, Is.EqualTo(new Dictionary<int, int> { [3] = 1 }));
	}

	[Test]
	public void CountJoinWith_FrozenTwin_TakesPipeline_LikeEager() {
		var outer = _authors.Prepare<int>().WithId(static id => id).CountJoinWithJmnBook().BuildFrozen();
		var inner = _authors.Prepare<int>().WithId(static id => id).InnerCountJoinWithJmnBook().BuildFrozen();

		Assert.That(outer.Explain(), Does.Contain("executor: Pipeline"));
		Assert.That(inner.Explain(), Does.Contain("executor: Pipeline"));
		foreach (var id in new[] { 1, 2, 3, 4 }) {
			Assert.That(Counts(outer.Execute(in id), static a => a.Id), Is.EqualTo(Counts(_authors.Query().WithId(id).CountJoinWithJmnBook().Execute(), static a => a.Id)), "outer " + id);
			Assert.That(Counts(inner.Execute(in id), static a => a.Id), Is.EqualTo(Counts(_authors.Query().WithId(id).InnerCountJoinWithJmnBook().Execute(), static a => a.Id)), "inner " + id);
		}
	}

	// ── Collection: reverse (tag → books) and forward (book → tags) ─────────────

	[Test]
	public void CountJoinWith_CollectionReverse_And_Forward_MatchJoinWith() {
		var reverse = Counts(_tags.Query().CountJoinWithCfkBook().Execute(), static t => t.Id);
		var forward = Counts(_taggedBooks.Query().CountJoinWithCfkTag().Execute(), static b => b.Id);
		var forwardInner = Counts(_taggedBooks.Query().InnerCountJoinWithCfkTag().Execute(), static b => b.Id);

		Assert.That(reverse, Is.EqualTo(new Dictionary<int, int> { [10] = 2, [20] = 1, [30] = 0 }));
		Assert.That(forward, Is.EqualTo(new Dictionary<int, int> { [1] = 2, [2] = 1, [3] = 0, [4] = 0 }));
		Assert.That(forwardInner, Is.EqualTo(new Dictionary<int, int> { [1] = 2, [2] = 1 }));
		Assert.That(forward, Is.EqualTo(ManyCounts(_taggedBooks.Query().JoinWithCfkTag().Execute(), static b => b.Id)));
		Assert.That(reverse, Is.EqualTo(ManyCounts(_tags.Query().JoinWithCfkBook().Execute(), static t => t.Id)));
	}
}

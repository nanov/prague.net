namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// Use "JoinBook" / "JoinBookInfo" to avoid collision with existing "Book" and "SymBook".

[DataCache]
public partial class JoinBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";
}

[DataCache]
public partial class JoinBookInfo {
	[DataCacheKey] public int Id { get; set; }

	/// <summary>
	/// FK pointing to <see cref="JoinBook.Id"/>.
	/// Codegen emits <see cref="JoinBookInfoCache.BookIdIndex"/> as
	/// <c>CacheUniqueIndex&lt;int, JoinBookInfo, int&gt;</c>.
	/// </summary>
	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int BookId { get; set; }

	public string Synopsis { get; set; } = "";
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneRightUniqueIndexTests {
	private DataCacheRegistry _registry = null!;
	private JoinBookCache _bookCache = null!;
	private JoinBookInfoCache _bookInfoCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<JoinBookCache>()
			.Register<JoinBookInfoCache>()
			.Build();
		_bookCache = _registry.GetCache<JoinBookCache>();
		_bookInfoCache = _registry.GetCache<JoinBookInfoCache>();
	}

	// ── Test 1: full match ────────────────────────────────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_MatchExists_AttachesInfo() {
		_bookCache.AddOrUpdate(new JoinBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 3, Title = "Book Three" });

		// BookInfo.Id is its own PK; BookId points back to JoinBook.Id.
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 103, BookId = 3, Synopsis = "Synopsis 3" });

		var results = _bookCache.Cache.Query()
			.JoinOne(_bookInfoCache, _bookInfoCache.BookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("Synopsis 1"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Synopsis, Is.EqualTo("Synopsis 2"));
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[3].Right!.Synopsis, Is.EqualTo("Synopsis 3"));
	}

	// ── Test 2: partial match ─────────────────────────────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_NoMatchInIndex_ReturnsNull() {
		_bookCache.AddOrUpdate(new JoinBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 3, Title = "Book Three" });

		// Only BookId 1 and 2 have info; book 3 has none.
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });

		var results = _bookCache.Cache.Query()
			.JoinOne(_bookInfoCache, _bookInfoCache.BookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[3].Right, Is.Null, "Book 3 has no matching BookInfo");
	}

	// ── Test 3: filter narrows right side ─────────────────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_WithFilter_NarrowsRight() {
		_bookCache.AddOrUpdate(new JoinBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 101, BookId = 1, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 102, BookId = 2, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 103, BookId = 3, Synopsis = "Historical drama" });

		// Filter: only include infos whose Synopsis contains "Historical".
		// UseIndex is not available on Synopsis (no index), so we use Where on the right query.
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookInfoCache, _bookInfoCache.BookIdIndex,
				q => q.Where(info => info.Synopsis.Contains("Historical")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null, "Book 1 has a Historical synopsis");
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("Historical novel"));
		Assert.That(byId[2].Right, Is.Null, "Book 2's info was filtered out (Science fiction)");
		Assert.That(byId[3].Right, Is.Not.Null, "Book 3 has a Historical synopsis");
		Assert.That(byId[3].Right!.Synopsis, Is.EqualTo("Historical drama"));
	}

	// ── Test 4: filter + TArg (static lambda, zero-alloc) ────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_WithFilterAndArg_StaticLambda() {
		_bookCache.AddOrUpdate(new JoinBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 101, BookId = 1, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 102, BookId = 2, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 103, BookId = 3, Synopsis = "Historical drama" });

		const string keyword = "Science";
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookInfoCache, _bookInfoCache.BookIdIndex,
				static (q, kw) => q.Where(info => info.Synopsis.Contains(kw)),
				keyword)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[2].Right, Is.Not.Null, "Book 2's info matches 'Science'");
		Assert.That(byId[2].Right!.Synopsis, Is.EqualTo("Science fiction"));
		Assert.That(byId[1].Right, Is.Null, "Book 1's info was filtered out");
		Assert.That(byId[3].Right, Is.Null, "Book 3's info was filtered out");
	}

	// ── Test 5: chained with left-side index filter ───────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_ChainedWithLeftFilter() {
		_bookCache.AddOrUpdate(new JoinBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(new JoinBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		_bookInfoCache.AddOrUpdate(new JoinBookInfo { Id = 103, BookId = 3, Synopsis = "Synopsis 3" });

		// Restrict the left side to only books {1, 2} via the key index, then join.
		// Book 3 should not appear in the results.
		var leftKeys = new List<int> { 1, 2 };
		var results = _bookCache.Cache.Query()
			.UseIndex(_bookCache.Cache.KeyIndex, leftKeys)
			.JoinOne(_bookInfoCache, _bookInfoCache.BookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Only books 1 and 2 should appear");
		Assert.That(results.All(r => r.Left.Id != 3), Is.True, "Book 3 was excluded by the left-side filter");
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Not.Null);
	}
}

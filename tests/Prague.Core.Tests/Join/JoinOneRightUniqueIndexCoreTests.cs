namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// RuBook (left, PK only) and RuBookInfo (right, has a unique BookId FK back to
// the left's PK). All hand-rolled — no [DataCache], no codegen. The right-side
// unique index is added manually via AddKeyValueIndex.

internal sealed class RuBook : ICacheEquatable<RuBook>, ICacheClonable<RuBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";

	public bool CacheEquals(RuBook? other) => other is not null && other.Id == Id && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public RuBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class RuBookInfo : ICacheEquatable<RuBookInfo>, ICacheClonable<RuBookInfo> {
	public int Id { get; init; }
	public int BookId { get; init; }
	public string Synopsis { get; init; } = "";

	public bool CacheEquals(RuBookInfo? other) => other is not null && other.Id == Id && other.BookId == BookId && other.Synopsis == Synopsis;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Synopsis);
	public RuBookInfo Clone() => new() { Id = Id, BookId = BookId, Synopsis = Synopsis };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneRightUniqueIndexCoreTests {
	private InMemoryDataCache<int, RuBook> _bookCache = null!;
	private InMemoryDataCache<int, RuBookInfo> _bookInfoCache = null!;
	// Unique right-side index keyed by RuBookInfo.BookId — translates a left PK to a right PK.
	private CacheUniqueIndex<int, RuBookInfo, int> _bookIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, RuBook>();
		_bookInfoCache = new InMemoryDataCache<int, RuBookInfo>();
		_bookIdIndex = _bookInfoCache.AddKeyValueIndex<int>((_, v) => v.BookId);
	}

	// ── Test 1: full match ────────────────────────────────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_MatchExists_AttachesInfo() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "Synopsis 3" });

		var results = _bookCache.Query()
			.JoinOne(_bookInfoCache, _bookIdIndex)
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
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		// no info for book 3

		var results = _bookCache.Query()
			.JoinOne(_bookInfoCache, _bookIdIndex)
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
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "Historical drama" });

		var results = _bookCache.Query()
			.JoinOne(_bookInfoCache, _bookIdIndex,
				q => q.Where(info => info.Synopsis.Contains("Historical")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("Historical novel"));
		Assert.That(byId[2].Right, Is.Null, "Science-fiction synopsis filtered out");
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[3].Right!.Synopsis, Is.EqualTo("Historical drama"));
	}

	// ── Test 4: filter + TArg static lambda ──────────────────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_WithFilterAndArg_StaticLambda() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "Historical drama" });

		const string keyword = "Science";
		var results = _bookCache.Query()
			.JoinOne(_bookInfoCache, _bookIdIndex,
				static (q, kw) => q.Where(info => info.Synopsis.Contains(kw)),
				keyword)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Synopsis, Is.EqualTo("Science fiction"));
		Assert.That(byId[1].Right, Is.Null);
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Test 5: chained with left-side key-index filter ──────────────────────

	[Test]
	public void JoinOne_BookToBookInfo_ChainedWithLeftFilter() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "Synopsis 3" });

		var leftKeys = new List<int> { 1, 2 };
		var results = _bookCache.Query()
			.UseIndex(_bookCache.KeyIndex, leftKeys)
			.JoinOne(_bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Only books 1 and 2 should appear");
		Assert.That(results.All(r => r.Left.Id != 3), Is.True);
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Not.Null);
	}

	// ── Inner tests ───────────────────────────────────────────────────────────

	[Test]
	public void InnerJoinOne_DropsBooksWithoutInfo() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "Book One" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "Book Two" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "Book Three" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Synopsis 1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Synopsis 2" });
		// Book 3 has no info

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Book 3 must be dropped (Inner semantic)");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void InnerJoinOne_AllMatched_KeepsAll() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "B2" });
		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "S1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "S2" });

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	[Test]
	public void InnerJoinOne_NoneMatched_EmptyResult() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "B2" });
		// no infos at all

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinOne_WithFilter_DropsFilteredOut() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "B3" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "Historical drama" });

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookIdIndex,
				q => q.Where(info => info.Synopsis.Contains("Historical")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Only Historical synopses kept; the rest dropped");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void InnerJoinOne_WithFilterArg_StaticLambda() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "B2" });
		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "Adventure" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "Mystery" });

		const string keyword = "Adventure";
		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookIdIndex,
				static (q, kw) => q.Where(info => info.Synopsis == kw),
				keyword)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Synopsis, Is.EqualTo("Adventure"));
	}

	[Test]
	public void InnerJoinOne_ChainedWithLeftFilter_NarrowsBeforeJoin() {
		_bookCache.AddOrUpdate(1, new RuBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RuBook { Id = 3, Title = "B3" });

		_bookInfoCache.AddOrUpdate(101, new RuBookInfo { Id = 101, BookId = 1, Synopsis = "S1" });
		_bookInfoCache.AddOrUpdate(102, new RuBookInfo { Id = 102, BookId = 2, Synopsis = "S2" });
		_bookInfoCache.AddOrUpdate(103, new RuBookInfo { Id = 103, BookId = 3, Synopsis = "S3" });

		var results = _bookCache.Query()
			.UseIndex(_bookCache.KeyIndex, new[] { 1, 3 })
			.InnerJoinOne(_bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}
}

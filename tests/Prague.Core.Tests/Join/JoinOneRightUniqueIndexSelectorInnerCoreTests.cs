namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// RusiBook (left, int PK) and RusiBookInfo (right, int PK with a long BookId FK
// back to the left). The unique right-side index is keyed by long (BookId), so
// the selector must map left's int PK → long index key. Exercises cross-key-type
// joins through InnerJoinOne + keySelector overloads (S1-S6 inner variants).

internal sealed class RusiBook : ICacheEquatable<RusiBook>, ICacheClonable<RusiBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";

	public bool CacheEquals(RusiBook? other) => other is not null && other.Id == Id && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public RusiBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class RusiBookInfo : ICacheEquatable<RusiBookInfo>, ICacheClonable<RusiBookInfo> {
	public int Id { get; init; }
	public long BookId { get; init; }
	public string Synopsis { get; init; } = "";

	public bool CacheEquals(RusiBookInfo? other) => other is not null && other.Id == Id && other.BookId == BookId && other.Synopsis == Synopsis;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Synopsis);
	public RusiBookInfo Clone() => new() { Id = Id, BookId = BookId, Synopsis = Synopsis };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneRightUniqueIndexSelectorInnerCoreTests {
	private InMemoryDataCache<int, RusiBook> _bookCache = null!;
	private InMemoryDataCache<int, RusiBookInfo> _bookInfoCache = null!;
	// Unique right-side index keyed by long — selector maps int (Book.Id) → long (index key).
	private CacheKeyValueIndex<int, RusiBookInfo, long> _bookIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, RusiBook>();
		_bookInfoCache = new InMemoryDataCache<int, RusiBookInfo>();
		_bookIdIndex = _bookInfoCache.AddKeyValueIndex<long>((_, v) => v.BookId);
	}

	// Convenience: selector int → long offset by 1000.
	private static long ToBookKey(int id) => 1000L + id;

	// ── S1: InnerJoinOne(keySelector, rightCache, rightIndex) ─────────────

	[Test]
	public void InnerJoinOne_S1_KeySelector_DropsMissingRight() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 1001L, Synopsis = "S1" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 1002L, Synopsis = "S2" });
		// no info for book 3 (1003L missing)

		var results = _bookCache.Query()
			.InnerJoinOne((int id) => ToBookKey(id), _bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Book 3 must be dropped (Inner)");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	// ── S2: InnerJoinOne(keySelector, selectorArg, rightCache, rightIndex) ─

	[Test]
	public void InnerJoinOne_S2_KeySelectorWithArg_StaticLambda_DropsMissing() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 2001L, Synopsis = "S1" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 2002L, Synopsis = "S2" });
		// no 2003L

		const long offset = 2000L;
		var results = _bookCache.Query()
			.InnerJoinOne(static (int id, long off) => off + id, offset, _bookInfoCache, _bookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── S3: InnerJoinOne(keySelector, rightCache, rightIndex, filter) ─────

	[Test]
	public void InnerJoinOne_S3_KeySelectorWithFilter_DropsMissingAndFiltered() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });
		_bookCache.AddOrUpdate(4, new RusiBook { Id = 4, Title = "B4" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 1001L, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 1002L, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(103, new RusiBookInfo { Id = 103, BookId = 1003L, Synopsis = "Historical drama" });
		// no info for book 4 → missing right
		// book 2's synopsis will be filtered out

		var results = _bookCache.Query()
			.InnerJoinOne((int id) => ToBookKey(id), _bookInfoCache, _bookIdIndex,
				q => q.Where(info => info.Synopsis.Contains("Historical")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 kept; book 2 filtered out; book 4 missing");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── S4: InnerJoinOne(keySelector, selectorArg, rightCache, rightIndex, filter) ─

	[Test]
	public void InnerJoinOne_S4_KeySelectorArg_WithFilter_DropsMissingAndFiltered() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });
		_bookCache.AddOrUpdate(4, new RusiBook { Id = 4, Title = "B4" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 3001L, Synopsis = "Adventure" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 3002L, Synopsis = "Mystery" });
		_bookInfoCache.AddOrUpdate(103, new RusiBookInfo { Id = 103, BookId = 3003L, Synopsis = "Adventure" });
		// no info for book 4

		const long offset = 3000L;
		var results = _bookCache.Query()
			.InnerJoinOne(static (int id, long off) => off + id, offset, _bookInfoCache, _bookIdIndex,
				q => q.Where(info => info.Synopsis == "Adventure"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 kept; 2 filtered out; 4 missing");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Synopsis == "Adventure"), Is.True);
	}

	// ── S5: InnerJoinOne(keySelector, rightCache, rightIndex, filter, filterArg) ─

	[Test]
	public void InnerJoinOne_S5_KeySelectorWithFilterArg_StaticLambda_DropsMissingAndFiltered() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });
		_bookCache.AddOrUpdate(4, new RusiBook { Id = 4, Title = "B4" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 1001L, Synopsis = "Historical novel" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 1002L, Synopsis = "Science fiction" });
		_bookInfoCache.AddOrUpdate(103, new RusiBookInfo { Id = 103, BookId = 1003L, Synopsis = "Historical drama" });
		// no info for book 4

		const string keyword = "Historical";
		var results = _bookCache.Query()
			.InnerJoinOne((int id) => ToBookKey(id), _bookInfoCache, _bookIdIndex,
				static (q, kw) => q.Where(info => info.Synopsis.Contains(kw)),
				keyword)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 kept; 2 filtered out; 4 missing");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right is not null && r.Right.Synopsis.Contains("Historical")), Is.True);
	}

	// ── S6: InnerJoinOne(keySelector, selectorArg, rightCache, rightIndex, filter, filterArg) ─

	[Test]
	public void InnerJoinOne_S6_KeySelectorArg_WithFilterArg_StaticLambdas_DropsMissingAndFiltered() {
		_bookCache.AddOrUpdate(1, new RusiBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RusiBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RusiBook { Id = 3, Title = "B3" });
		_bookCache.AddOrUpdate(4, new RusiBook { Id = 4, Title = "B4" });

		_bookInfoCache.AddOrUpdate(101, new RusiBookInfo { Id = 101, BookId = 5001L, Synopsis = "Adventure" });
		_bookInfoCache.AddOrUpdate(102, new RusiBookInfo { Id = 102, BookId = 5002L, Synopsis = "Mystery" });
		_bookInfoCache.AddOrUpdate(103, new RusiBookInfo { Id = 103, BookId = 5003L, Synopsis = "Adventure" });
		// no info for book 4

		const long offset = 5000L;
		const string keyword = "Adventure";
		var results = _bookCache.Query()
			.InnerJoinOne(static (int id, long off) => off + id, offset, _bookInfoCache, _bookIdIndex,
				static (q, kw) => q.Where(info => info.Synopsis == kw),
				keyword)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 kept; 2 filtered out; 4 missing");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Synopsis == "Adventure"), Is.True);
	}
}

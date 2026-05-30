namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Phase B-2 chained selector tests: RightUniqueIndex + key selector at chained levels.
// Domain: Book (int Id) joined to BookInfo (right cache with unique long-FK index)
// at the first hop, then to Review (right cache with unique long-FK index) at the
// second hop. Selector maps int → long at both hops.

internal sealed class RuSelChainBook : ICacheEquatable<RuSelChainBook>, ICacheClonable<RuSelChainBook> {
	public int Id { get; init; }
	public string? Title { get; init; }
	public bool CacheEquals(RuSelChainBook? o) => o is not null && o.Id == Id && o.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public RuSelChainBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class RuSelChainBookInfo : ICacheEquatable<RuSelChainBookInfo>, ICacheClonable<RuSelChainBookInfo> {
	public int Id { get; init; }
	public long BookId { get; init; }
	public string? Synopsis { get; init; }
	public bool CacheEquals(RuSelChainBookInfo? o) => o is not null && o.Id == Id && o.BookId == BookId && o.Synopsis == Synopsis;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Synopsis);
	public RuSelChainBookInfo Clone() => new() { Id = Id, BookId = BookId, Synopsis = Synopsis };
}

internal sealed class RuSelChainReview : ICacheEquatable<RuSelChainReview>, ICacheClonable<RuSelChainReview> {
	public int Id { get; init; }
	public long BookId { get; init; }
	public int Stars { get; init; }
	public bool CacheEquals(RuSelChainReview? o) => o is not null && o.Id == Id && o.BookId == BookId && o.Stars == Stars;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Stars);
	public RuSelChainReview Clone() => new() { Id = Id, BookId = BookId, Stars = Stars };
}

[TestFixture]
public class JoinOneRightUniqueIndexSelectorChainedCoreTests {
	private InMemoryDataCache<int, RuSelChainBook> _bookCache = null!;
	private InMemoryDataCache<int, RuSelChainBookInfo> _bookInfoCache = null!;
	private InMemoryDataCache<int, RuSelChainReview> _reviewCache = null!;
	private CacheKeyValueIndex<int, RuSelChainBookInfo, long> _bookInfoByBookIdIndex = null!;
	private CacheKeyValueIndex<int, RuSelChainReview, long> _reviewByBookIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, RuSelChainBook>();
		_bookInfoCache = new InMemoryDataCache<int, RuSelChainBookInfo>();
		_reviewCache = new InMemoryDataCache<int, RuSelChainReview>();
		_bookInfoByBookIdIndex = _bookInfoCache.AddKeyValueIndex<long>(static (_, v) => v.BookId);
		_reviewByBookIdIndex = _reviewCache.AddKeyValueIndex<long>(static (_, v) => v.BookId);
	}

	private void SeedFullChain() {
		_bookCache.AddOrUpdate(1, new RuSelChainBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuSelChainBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RuSelChainBook { Id = 3, Title = "B3" });
		_bookInfoCache.AddOrUpdate(100, new RuSelChainBookInfo { Id = 100, BookId = 1L, Synopsis = "S1" });
		_bookInfoCache.AddOrUpdate(200, new RuSelChainBookInfo { Id = 200, BookId = 2L, Synopsis = "S2" });
		_bookInfoCache.AddOrUpdate(300, new RuSelChainBookInfo { Id = 300, BookId = 3L, Synopsis = "S3" });
		_reviewCache.AddOrUpdate(1000, new RuSelChainReview { Id = 1000, BookId = 1L, Stars = 5 });
		_reviewCache.AddOrUpdate(2000, new RuSelChainReview { Id = 2000, BookId = 2L, Stars = 3 });
		_reviewCache.AddOrUpdate(3000, new RuSelChainReview { Id = 3000, BookId = 3L, Stars = 4 });
	}

	[Test]
	public void Chained_S1Outer_AllMatched() {
		SeedFullChain();
		var results = _bookCache.Query()
			.JoinOne(static k => (long)k, _bookInfoCache, _bookInfoByBookIdIndex)   // level-0 S1
			.JoinOne(static k => (long)k, _reviewCache, _reviewByBookIdIndex)       // level-1 S1 chained
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2!.Stars, Is.EqualTo(5));
	}

	[Test]
	public void Chained_S1Inner_DropsUnmatchedAtSecondHop() {
		SeedFullChain();
		_reviewCache.Remove(2000);
		var results = _bookCache.Query()
			.InnerJoinOne(static k => (long)k, _bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(static k => (long)k, _reviewCache, _reviewByBookIdIndex)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S3Inner_WithFilter_DropsPredicateReject() {
		SeedFullChain();
		var results = _bookCache.Query()
			.InnerJoinOne(static k => (long)k, _bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(static k => (long)k, _reviewCache, _reviewByBookIdIndex,
				static q => q.Where(r => r.Stars >= 4))
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S5Inner_WithFilterArg_StaticLambdaZeroAlloc() {
		SeedFullChain();
		const int minStars = 4;
		var results = _bookCache.Query()
			.InnerJoinOne(static k => (long)k, _bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(static k => (long)k, _reviewCache, _reviewByBookIdIndex,
				static (q, min) => q.Where(r => r.Stars >= min),
				minStars)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
	}
}

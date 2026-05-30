namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain: 1 outer cache, 2 right caches each with a unique FK index back ──
// to the outer cache's key.
//   Book ←(BookInfo.BookId unique)─ BookInfo
//   Book ←(Review.BookId unique)── Review
//
// Chained shape: bookCache.Query()
//                  .InnerJoinOne(bookInfoCache, bookInfoByBookIdIndex)
//                  .InnerJoinOne(reviewCache,   reviewByBookIdIndex)
//                  .Execute();
//
// Both hops join against Book.Id via a right-side unique FK index. The first hop
// is level-0 (hand-written). The second hop is level-1 (T4-emitted RightUnique).
// Validates the chained-inner narrow design end-to-end for the RightUniqueIndex
// shape across the seed-drop + predicate-reject + filter-arg miss-callback paths.

internal sealed class RuxBook : ICacheEquatable<RuxBook>, ICacheClonable<RuxBook> {
	public int Id { get; init; }
	public string? Title { get; init; }
	public bool CacheEquals(RuxBook? o) => o is not null && o.Id == Id && o.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public RuxBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class RuxBookInfo : ICacheEquatable<RuxBookInfo>, ICacheClonable<RuxBookInfo> {
	public int Id { get; init; }
	public int BookId { get; init; }
	public string? Synopsis { get; init; }
	public bool CacheEquals(RuxBookInfo? o) => o is not null && o.Id == Id && o.BookId == BookId && o.Synopsis == Synopsis;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Synopsis);
	public RuxBookInfo Clone() => new() { Id = Id, BookId = BookId, Synopsis = Synopsis };
}

internal sealed class RuxReview : ICacheEquatable<RuxReview>, ICacheClonable<RuxReview> {
	public int Id { get; init; }
	public int BookId { get; init; }
	public int Stars { get; init; }
	public bool CacheEquals(RuxReview? o) => o is not null && o.Id == Id && o.BookId == BookId && o.Stars == Stars;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookId, Stars);
	public RuxReview Clone() => new() { Id = Id, BookId = BookId, Stars = Stars };
}

[TestFixture]
public class JoinOneRightUniqueIndexChainedCoreTests {
	private InMemoryDataCache<int, RuxBook> _bookCache = null!;
	private InMemoryDataCache<int, RuxBookInfo> _bookInfoCache = null!;
	private InMemoryDataCache<int, RuxReview> _reviewCache = null!;
	private CacheKeyValueIndex<int, RuxBookInfo, int> _bookInfoByBookIdIndex = null!;
	private CacheKeyValueIndex<int, RuxReview, int> _reviewByBookIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, RuxBook>();
		_bookInfoCache = new InMemoryDataCache<int, RuxBookInfo>();
		_reviewCache = new InMemoryDataCache<int, RuxReview>();
		_bookInfoByBookIdIndex = _bookInfoCache.AddKeyValueIndex<int>(static (_, v) => v.BookId);
		_reviewByBookIdIndex = _reviewCache.AddKeyValueIndex<int>(static (_, v) => v.BookId);
	}

	private void SeedFullChain() {
		_bookCache.AddOrUpdate(1, new RuxBook { Id = 1, Title = "B1" });
		_bookCache.AddOrUpdate(2, new RuxBook { Id = 2, Title = "B2" });
		_bookCache.AddOrUpdate(3, new RuxBook { Id = 3, Title = "B3" });

		_bookInfoCache.AddOrUpdate(100, new RuxBookInfo { Id = 100, BookId = 1, Synopsis = "Adventure" });
		_bookInfoCache.AddOrUpdate(200, new RuxBookInfo { Id = 200, BookId = 2, Synopsis = "Mystery" });
		_bookInfoCache.AddOrUpdate(300, new RuxBookInfo { Id = 300, BookId = 3, Synopsis = "Adventure" });

		_reviewCache.AddOrUpdate(1000, new RuxReview { Id = 1000, BookId = 1, Stars = 5 });
		_reviewCache.AddOrUpdate(2000, new RuxReview { Id = 2000, BookId = 2, Stars = 3 });
		_reviewCache.AddOrUpdate(3000, new RuxReview { Id = 3000, BookId = 3, Stars = 4 });
	}

	[Test]
	public void Chained_AllMatched() {
		SeedFullChain();

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("Adventure"));
		Assert.That(byId[1].Right2!.Stars, Is.EqualTo(5));
		Assert.That(byId[2].Right2!.Stars, Is.EqualTo(3));
		Assert.That(byId[3].Right2!.Stars, Is.EqualTo(4));
	}

	[Test]
	public void Chained_SecondHopMisses_AreDropped_ViaSeedNarrow() {
		// Review missing for book 2 → second-hop seed (_reviewByBookIdIndex.IntersectValues)
		// doesn't emit a pair for book 2. Step-3 narrow (candidates.IntersectWithValueSet
		// against post-Filter pairs) removes book 2 from candidates.
		SeedFullChain();
		_reviewCache.Remove(2000);

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_FirstHopMisses_AreDropped() {
		// BookInfo missing for book 2 → first hop drops book 2 from candidates.
		// Second hop walks the narrowed candidates {1, 3} — book 2 never reached.
		SeedFullChain();
		_bookInfoCache.Remove(200);

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_SecondHopWithFilter_RejectsViaPredicate() {
		// Predicate filter on Review: keep Stars >= 4 → drops book 2 (Stars=3).
		// Tests the Filtered miss-callback path (predicate-reject inside the walk,
		// not the seed-drop path which is step-3).
		SeedFullChain();

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex,
				static q => q.Where(r => r.Stars >= 4))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_SecondHopWithFilterArg_StaticLambdaZeroAlloc() {
		// Same Filtered-callback path but via the filter+arg overload — zero-alloc
		// with static lambda + captured arg.
		SeedFullChain();
		const int minStars = 4;

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex,
				static (q, min) => q.Where(r => r.Stars >= min),
				minStars)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2!.Stars, Is.EqualTo(5));
		Assert.That(byId[3].Right2!.Stars, Is.EqualTo(4));
	}

	[Test]
	public void Chained_OuterThenInner_OuterKeepsNullFirstHop_InnerDropsSecondHop() {
		// First hop OUTER → book 3 kept with Right=null (no BookInfo).
		// Second hop INNER → book 3 dropped if no Review.
		// Books: BookInfo missing for 3, Review missing for 2.
		// Expected: only book 1 survives (Right=BookInfo, Right2=Review).
		// Book 2: BookInfo present, but Review missing → INNER drops.
		// Book 3: BookInfo missing (Right=null), Review present → INNER walks fine
		//         and KEEPS book 3 (Right=null, Right2=Review). So 2 results.
		SeedFullChain();
		_bookInfoCache.Remove(300);
		_reviewCache.Remove(2000);

		var results = _bookCache.Query()
			.JoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.InnerJoinOne(_reviewCache, _reviewByBookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right2, Is.Not.Null);
		Assert.That(byId[3].Right, Is.Null);
		Assert.That(byId[3].Right2!.Stars, Is.EqualTo(4));
	}

	[Test]
	public void Chained_InnerThenOuter_FirstDropsBeforeSecond() {
		// First hop INNER → book 3 dropped (no BookInfo).
		// Second hop OUTER → book 2 kept with Right2=null.
		SeedFullChain();
		_bookInfoCache.Remove(300);
		_reviewCache.Remove(2000);

		var results = _bookCache.Query()
			.InnerJoinOne(_bookInfoCache, _bookInfoByBookIdIndex)
			.JoinOne(_reviewCache, _reviewByBookIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2!.Stars, Is.EqualTo(5));
		Assert.That(byId[2].Right2, Is.Null);
	}
}

namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain: outer Author with two symmetric unique indexes (FKs) to two right
// caches (Book, Publisher). Both joins are LeftUnique shape, sharing the same
// outer cache. First hop is level-0; second hop is level-1 T4-emitted LeftUnique.
//
// For LeftUnique specifically: the seed (LeftIndex.Reverse.IntersectValues) does
// NOT check right-cache existence — it only consults the Reverse mapping
// (leftKey → indexValue). That means seed pairs may include lefts whose FK
// points to a non-existent right entity. ExecutePairedInner's NotFound callback
// is the only path that catches those misses (vs the seed-narrow step for
// PK-to-PK/RightUnique). Exercising that path is the point of these tests.

internal sealed class LuxAuthor : ICacheEquatable<LuxAuthor>, ICacheClonable<LuxAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public int BookId { get; init; }
	public int PublisherId { get; init; }
	public bool CacheEquals(LuxAuthor? o) => o is not null && o.Id == Id && o.Name == Name && o.BookId == BookId && o.PublisherId == PublisherId;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, BookId, PublisherId);
	public LuxAuthor Clone() => new() { Id = Id, Name = Name, BookId = BookId, PublisherId = PublisherId };
}

internal sealed class LuxBook : ICacheEquatable<LuxBook>, ICacheClonable<LuxBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public bool CacheEquals(LuxBook? o) => o is not null && o.Id == Id && o.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public LuxBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class LuxPublisher : ICacheEquatable<LuxPublisher>, ICacheClonable<LuxPublisher> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public int FoundedYear { get; init; }
	public bool CacheEquals(LuxPublisher? o) => o is not null && o.Id == Id && o.Name == Name && o.FoundedYear == FoundedYear;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, FoundedYear);
	public LuxPublisher Clone() => new() { Id = Id, Name = Name, FoundedYear = FoundedYear };
}

[TestFixture]
public class JoinOneLeftUniqueIndexChainedCoreTests {
	private InMemoryDataCache<int, LuxAuthor> _authorCache = null!;
	private InMemoryDataCache<int, LuxBook> _bookCache = null!;
	private InMemoryDataCache<int, LuxPublisher> _publisherCache = null!;
	private CacheSymmetricUniqueIndex<int, LuxAuthor, int> _authorBookIdIndex = null!;
	private CacheSymmetricUniqueIndex<int, LuxAuthor, int> _authorPublisherIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_authorCache = new InMemoryDataCache<int, LuxAuthor>();
		_bookCache = new InMemoryDataCache<int, LuxBook>();
		_publisherCache = new InMemoryDataCache<int, LuxPublisher>();
		_authorBookIdIndex = _authorCache.AddSymmetricKeyValueIndex<int>((_, v) => v.BookId);
		_authorPublisherIdIndex = _authorCache.AddSymmetricKeyValueIndex<int>((_, v) => v.PublisherId);
	}

	private void SeedFullChain() {
		_bookCache.AddOrUpdate(101, new LuxBook { Id = 101, Title = "Book A" });
		_bookCache.AddOrUpdate(102, new LuxBook { Id = 102, Title = "Book B" });
		_bookCache.AddOrUpdate(103, new LuxBook { Id = 103, Title = "Book C" });

		_publisherCache.AddOrUpdate(201, new LuxPublisher { Id = 201, Name = "P1", FoundedYear = 1900 });
		_publisherCache.AddOrUpdate(202, new LuxPublisher { Id = 202, Name = "P2", FoundedYear = 1950 });
		_publisherCache.AddOrUpdate(203, new LuxPublisher { Id = 203, Name = "P3", FoundedYear = 2000 });

		_authorCache.AddOrUpdate(1, new LuxAuthor { Id = 1, Name = "Alice", BookId = 101, PublisherId = 201 });
		_authorCache.AddOrUpdate(2, new LuxAuthor { Id = 2, Name = "Bob",   BookId = 102, PublisherId = 202 });
		_authorCache.AddOrUpdate(3, new LuxAuthor { Id = 3, Name = "Carol", BookId = 103, PublisherId = 203 });
	}

	[Test]
	public void Chained_AllMatched() {
		SeedFullChain();

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Title, Is.EqualTo("Book A"));
		Assert.That(byId[1].Right2!.Name, Is.EqualTo("P1"));
		Assert.That(byId[2].Right2!.Name, Is.EqualTo("P2"));
		Assert.That(byId[3].Right2!.Name, Is.EqualTo("P3"));
	}

	[Test]
	public void Chained_SecondHopMisses_AreDropped_ViaNotFoundCallback() {
		// Author 2's Publisher (id 202) is missing. The LeftUnique seed for the second
		// hop walks the Reverse index (author→publisherId) and emits a pair (2, 202)
		// regardless — the right-cache existence check happens INSIDE ExecutePairedInner
		// via the NotFound callback, which removes author 2 from candidates.
		SeedFullChain();
		_publisherCache.Remove(202);

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_FirstHopMisses_AreDropped() {
		// Author 2's Book (id 102) is missing → first hop drops via NotFound.
		// Second hop never visits author 2.
		SeedFullChain();
		_bookCache.Remove(102);

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_SecondHopWithFilter_RejectsViaPredicate() {
		// Filter on Publisher: keep FoundedYear < 2000 → drops Carol (P3 = 2000).
		// Tests the Filtered miss-callback path through the chained-inner walk.
		SeedFullChain();

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache,
				static q => q.Where(p => p.FoundedYear < 2000))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Chained_SecondHopWithFilterArg_StaticLambdaZeroAlloc() {
		// Same Filtered-callback path, but via filter+arg overload (static lambda).
		SeedFullChain();
		const int cutoffYear = 2000;

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache,
				static (q, cutoff) => q.Where(p => p.FoundedYear < cutoff),
				cutoffYear)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
	}

	[Test]
	public void Chained_OuterThenInner_OuterKeepsNullFirstHop_InnerDropsSecondHop() {
		// First hop OUTER. Carol's book is missing → Right=null but kept.
		// Bob's publisher is missing → second-hop INNER drops Bob.
		// Carol's publisher exists → INNER keeps Carol (Right=null, Right2=P3).
		// Alice is fully matched.
		SeedFullChain();
		_bookCache.Remove(103);
		_publisherCache.Remove(202);

		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, _publisherCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Title, Is.EqualTo("Book A"));
		Assert.That(byId[1].Right2!.Name, Is.EqualTo("P1"));
		Assert.That(byId[3].Right, Is.Null);
		Assert.That(byId[3].Right2!.Name, Is.EqualTo("P3"));
	}

	[Test]
	public void Chained_InnerThenOuter_FirstDropsBeforeSecond() {
		// First hop INNER. Carol's book is missing → dropped.
		// Second hop OUTER. Bob's publisher is missing → kept with Right2=null.
		SeedFullChain();
		_bookCache.Remove(103);
		_publisherCache.Remove(202);

		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, _bookCache)
			.JoinOne(_authorPublisherIdIndex, _publisherCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2!.Name, Is.EqualTo("P1"));
		Assert.That(byId[2].Right2, Is.Null);
	}
}

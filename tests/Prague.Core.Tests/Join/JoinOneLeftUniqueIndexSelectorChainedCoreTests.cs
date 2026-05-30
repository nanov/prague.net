namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Phase B-3 chained selector tests: LeftUniqueIndex + key selector at chained levels.
// Outer Author cache has two symmetric unique indexes (BookId long, PublisherId long).
// Selector maps long → int at both hops.

internal sealed class LuSelChainAuthor : ICacheEquatable<LuSelChainAuthor>, ICacheClonable<LuSelChainAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public long BookId { get; init; }
	public long PublisherId { get; init; }
	public bool CacheEquals(LuSelChainAuthor? o) => o is not null && o.Id == Id && o.Name == Name && o.BookId == BookId && o.PublisherId == PublisherId;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, BookId, PublisherId);
	public LuSelChainAuthor Clone() => new() { Id = Id, Name = Name, BookId = BookId, PublisherId = PublisherId };
}

internal sealed class LuSelChainBook : ICacheEquatable<LuSelChainBook>, ICacheClonable<LuSelChainBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public bool CacheEquals(LuSelChainBook? o) => o is not null && o.Id == Id && o.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public LuSelChainBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class LuSelChainPublisher : ICacheEquatable<LuSelChainPublisher>, ICacheClonable<LuSelChainPublisher> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public int FoundedYear { get; init; }
	public bool CacheEquals(LuSelChainPublisher? o) => o is not null && o.Id == Id && o.Name == Name && o.FoundedYear == FoundedYear;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, FoundedYear);
	public LuSelChainPublisher Clone() => new() { Id = Id, Name = Name, FoundedYear = FoundedYear };
}

[TestFixture]
public class JoinOneLeftUniqueIndexSelectorChainedCoreTests {
	private InMemoryDataCache<int, LuSelChainAuthor> _authorCache = null!;
	private InMemoryDataCache<int, LuSelChainBook> _bookCache = null!;
	private InMemoryDataCache<int, LuSelChainPublisher> _publisherCache = null!;
	private CacheSymmetricUniqueIndex<int, LuSelChainAuthor, long> _authorBookIdIndex = null!;
	private CacheSymmetricUniqueIndex<int, LuSelChainAuthor, long> _authorPublisherIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_authorCache = new InMemoryDataCache<int, LuSelChainAuthor>();
		_bookCache = new InMemoryDataCache<int, LuSelChainBook>();
		_publisherCache = new InMemoryDataCache<int, LuSelChainPublisher>();
		_authorBookIdIndex = _authorCache.AddSymmetricKeyValueIndex<long>(static (_, v) => v.BookId);
		_authorPublisherIdIndex = _authorCache.AddSymmetricKeyValueIndex<long>(static (_, v) => v.PublisherId);
	}

	private void SeedFullChain() {
		_authorCache.AddOrUpdate(1, new LuSelChainAuthor { Id = 1, Name = "Alice", BookId = 101L, PublisherId = 201L });
		_authorCache.AddOrUpdate(2, new LuSelChainAuthor { Id = 2, Name = "Bob", BookId = 102L, PublisherId = 202L });
		_authorCache.AddOrUpdate(3, new LuSelChainAuthor { Id = 3, Name = "Carol", BookId = 103L, PublisherId = 203L });
		_bookCache.AddOrUpdate(101, new LuSelChainBook { Id = 101, Title = "B1" });
		_bookCache.AddOrUpdate(102, new LuSelChainBook { Id = 102, Title = "B2" });
		_bookCache.AddOrUpdate(103, new LuSelChainBook { Id = 103, Title = "B3" });
		_publisherCache.AddOrUpdate(201, new LuSelChainPublisher { Id = 201, Name = "P1", FoundedYear = 1900 });
		_publisherCache.AddOrUpdate(202, new LuSelChainPublisher { Id = 202, Name = "P2", FoundedYear = 1950 });
		_publisherCache.AddOrUpdate(203, new LuSelChainPublisher { Id = 203, Name = "P3", FoundedYear = 2000 });
	}

	[Test]
	public void Chained_S1Outer_AllMatched() {
		SeedFullChain();
		var results = _authorCache.Query()
			.JoinOne(_authorBookIdIndex, static idx => (int)idx, _bookCache)       // level-0 S1
			.JoinOne(_authorPublisherIdIndex, static idx => (int)idx, _publisherCache) // level-1 S1 chained
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Title, Is.EqualTo("B1"));
		Assert.That(byId[1].Right2!.Name, Is.EqualTo("P1"));
	}

	[Test]
	public void Chained_S1Inner_DropsUnmatchedAtSecondHop() {
		SeedFullChain();
		_publisherCache.Remove(202);
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, static idx => (int)idx, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, static idx => (int)idx, _publisherCache)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S3Inner_WithFilter_DropsPredicateReject() {
		SeedFullChain();
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, static idx => (int)idx, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, static idx => (int)idx, _publisherCache,
				static q => q.Where(p => p.FoundedYear < 2000))
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Chained_S5Inner_WithFilterArg_StaticLambdaZeroAlloc() {
		SeedFullChain();
		const int cutoff = 2000;
		var results = _authorCache.Query()
			.InnerJoinOne(_authorBookIdIndex, static idx => (int)idx, _bookCache)
			.InnerJoinOne(_authorPublisherIdIndex, static idx => (int)idx, _publisherCache,
				static (q, c) => q.Where(p => p.FoundedYear < c),
				cutoff)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
	}
}

namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Forward direction (owner → referenced): the driving cache OWNS the collection FK
// (MnFwBook.TagIds) and joins to the referenced cache to fetch each element's entity. M:N — a tag
// is shared by many books — exercised by a tag referenced by two books. Driven by
// JoinManyCollectionForward / InnerJoinManyCollectionForward over the same symmetric collection index.

internal sealed class MnFwTag : ICacheEquatable<MnFwTag>, ICacheClonable<MnFwTag> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(MnFwTag? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public MnFwTag Clone() => new() { Id = Id, Name = Name };
}

internal sealed class MnFwBook : ICacheEquatable<MnFwBook>, ICacheClonable<MnFwBook> {
	public int Id { get; init; }
	public List<int> TagIds { get; init; } = new();
	public string Title { get; init; } = "";

	public bool CacheEquals(MnFwBook? other) =>
		other is not null && other.Id == Id && other.Title == Title && other.TagIds.SequenceEqual(TagIds);
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public MnFwBook Clone() => new() { Id = Id, Title = Title, TagIds = new List<int>(TagIds) };
}

[TestFixture]
public class JoinManyCollectionForwardCoreTests {
	private InMemoryDataCache<int, MnFwTag> _tagCache = null!;
	private InMemoryDataCache<int, MnFwBook> _bookCache = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnFwBook, int> _index = null!;

	[SetUp]
	public void SetUp() {
		_tagCache = new InMemoryDataCache<int, MnFwTag>();
		_bookCache = new InMemoryDataCache<int, MnFwBook>();
		_index = _bookCache.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
	}

	[Test]
	public void JoinManyForward_BookToTags_FlattensCollection() {
		_tagCache.AddOrUpdate(10, new MnFwTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(20, new MnFwTag { Id = 20, Name = "classic" });

		_bookCache.AddOrUpdate(1, new MnFwBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 20 } });
		_bookCache.AddOrUpdate(2, new MnFwBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 10 } });

		var results = _bookCache.Query()
			.JoinManyCollectionForward(_tagCache, _index)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byBook = results.ToDictionary(r => r.Left.Id);

		var hobbit = byBook[1].Right.Select(t => t.Name).OrderBy(n => n).ToArray();
		Assert.That(hobbit, Is.EqualTo(new[] { "classic", "fantasy" }));

		// Shared tag (10) appears for BOTH books — the M:N fan-out.
		var lotr = byBook[2].Right.Select(t => t.Name).ToArray();
		Assert.That(lotr, Is.EqualTo(new[] { "fantasy" }));
	}

	[Test]
	public void JoinManyForward_EmptyCollection_GetsEmptyQueryResults() {
		_tagCache.AddOrUpdate(10, new MnFwTag { Id = 10, Name = "fantasy" });

		_bookCache.AddOrUpdate(1, new MnFwBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });
		_bookCache.AddOrUpdate(2, new MnFwBook { Id = 2, Title = "Untagged", TagIds = new List<int>() });

		var results = _bookCache.Query()
			.JoinManyCollectionForward(_tagCache, _index)
			.Execute();

		var byBook = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBook[1].Right.Count, Is.EqualTo(1));
		Assert.That(byBook[2].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinManyForward_MissingReferencedEntity_Skipped() {
		_tagCache.AddOrUpdate(10, new MnFwTag { Id = 10, Name = "fantasy" });
		// tag 999 is referenced but not present in the tag cache.

		_bookCache.AddOrUpdate(1, new MnFwBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 999 } });

		var results = _bookCache.Query()
			.JoinManyCollectionForward(_tagCache, _index)
			.Execute();

		var hobbit = results.Single();
		Assert.That(hobbit.Right.Count, Is.EqualTo(1));
		Assert.That(hobbit.Right.First().Name, Is.EqualTo("fantasy"));
	}

	[Test]
	public void InnerJoinManyForward_DropsBooksWithNoTags() {
		_tagCache.AddOrUpdate(10, new MnFwTag { Id = 10, Name = "fantasy" });

		_bookCache.AddOrUpdate(1, new MnFwBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });
		_bookCache.AddOrUpdate(2, new MnFwBook { Id = 2, Title = "Untagged", TagIds = new List<int>() });

		var results = _bookCache.Query()
			.InnerJoinManyCollectionForward(_tagCache, _index)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinManyForward_WithFilter_NarrowsTags() {
		_tagCache.AddOrUpdate(10, new MnFwTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(20, new MnFwTag { Id = 20, Name = "classic" });

		_bookCache.AddOrUpdate(1, new MnFwBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 20 } });

		var results = _bookCache.Query()
			.JoinManyCollectionForward(_tagCache, _index, q => q.Where(t => t.Name.StartsWith("f")))
			.Execute();

		var hobbit = results.Single();
		Assert.That(hobbit.Right.Count, Is.EqualTo(1));
		Assert.That(hobbit.Right.First().Name, Is.EqualTo("fantasy"));
	}
}

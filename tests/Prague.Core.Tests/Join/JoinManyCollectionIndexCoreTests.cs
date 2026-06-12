namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Reverse direction (element -> owners): an M:N reverse-collection join over a
// CacheCollectionSymmetricKeyValueListIndex (built over MnTaggedBook.TagIds), driven by
// JoinManyCollection / InnerJoinManyCollection. A book shared by two tags appears under BOTH —
// the M:N fan-out the plain JoinMany right-list resolver could not do. All hand-rolled, no codegen.

internal sealed class MnTag : ICacheEquatable<MnTag>, ICacheClonable<MnTag> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(MnTag? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public MnTag Clone() => new() { Id = Id, Name = Name };
}

internal sealed class MnTaggedBook : ICacheEquatable<MnTaggedBook>, ICacheClonable<MnTaggedBook> {
	public int Id { get; init; }
	public List<int> TagIds { get; init; } = new();
	public string Title { get; init; } = "";

	public bool CacheEquals(MnTaggedBook? other) =>
		other is not null && other.Id == Id && other.Title == Title && other.TagIds.SequenceEqual(TagIds);
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public MnTaggedBook Clone() => new() { Id = Id, Title = Title, TagIds = new List<int>(TagIds) };
}

[TestFixture]
public class JoinManyCollectionIndexCoreTests {
	private InMemoryDataCache<int, MnTag> _tagCache = null!;
	private InMemoryDataCache<int, MnTaggedBook> _bookCache = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnTaggedBook, int> _index = null!;

	[SetUp]
	public void SetUp() {
		_tagCache = new InMemoryDataCache<int, MnTag>();
		_bookCache = new InMemoryDataCache<int, MnTaggedBook>();
		_index = _bookCache.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
	}

	[Test]
	public void JoinMany_TagToBooks_FansOutAcrossElements() {
		_tagCache.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(20, new MnTag { Id = 20, Name = "classic" });

		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10, 20 } });
		_bookCache.AddOrUpdate(2, new MnTaggedBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 10 } });
		_bookCache.AddOrUpdate(3, new MnTaggedBook { Id = 3, Title = "Dune", TagIds = new List<int> { 20 } });

		var results = _tagCache.Query()
			.JoinManyCollection(_bookCache, _index)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byTag = results.ToDictionary(r => r.Left.Id);

		var fantasy = byTag[10].Right.Select(b => b.Title).OrderBy(t => t).ToArray();
		Assert.That(fantasy, Is.EqualTo(new[] { "Hobbit", "LOTR" }));

		var classic = byTag[20].Right.Select(b => b.Title).OrderBy(t => t).ToArray();
		Assert.That(classic, Is.EqualTo(new[] { "Dune", "Hobbit" }));
	}

	[Test]
	public void JoinMany_TagWithNoBooks_GetsEmptyQueryResults() {
		_tagCache.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(99, new MnTag { Id = 99, Name = "unused" });

		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });

		var results = _tagCache.Query()
			.JoinManyCollection(_bookCache, _index)
			.Execute();

		var byTag = results.ToDictionary(r => r.Left.Id);
		Assert.That(byTag[10].Right.Count, Is.EqualTo(1));
		Assert.That(byTag[99].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinMany_DropsTagsWithNoBooks() {
		_tagCache.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(99, new MnTag { Id = 99, Name = "unused" });

		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });

		var results = _tagCache.Query()
			.InnerJoinManyCollection(_bookCache, _index)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(10));
	}

	[Test]
	public void JoinMany_WithFilter_NarrowsBooks() {
		_tagCache.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });

		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });
		_bookCache.AddOrUpdate(2, new MnTaggedBook { Id = 2, Title = "LOTR", TagIds = new List<int> { 10 } });

		var results = _tagCache.Query()
			.JoinManyCollection(_bookCache, _index, q => q.Where(b => b.Title.StartsWith("H")))
			.Execute();

		var fantasy = results.Single();
		Assert.That(fantasy.Right.Count, Is.EqualTo(1));
		Assert.That(fantasy.Right.First().Title, Is.EqualTo("Hobbit"));
	}

	[Test]
	public void JoinMany_AfterBookTagsUpdated_ReflectsNewMembership() {
		_tagCache.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });
		_tagCache.AddOrUpdate(20, new MnTag { Id = 20, Name = "classic" });

		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 10 } });
		// Re-tag: drop 10, add 20.
		_bookCache.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = new List<int> { 20 } });

		var results = _tagCache.Query()
			.JoinManyCollection(_bookCache, _index)
			.Execute();

		var byTag = results.ToDictionary(r => r.Left.Id);
		Assert.That(byTag[10].Right.Count, Is.EqualTo(0));
		Assert.That(byTag[20].Right.Count, Is.EqualTo(1));
		Assert.That(byTag[20].Right.First().Title, Is.EqualTo("Hobbit"));
	}
}

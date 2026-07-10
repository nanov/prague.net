namespace Prague.Core.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

// Layer 1: collection-mode CacheKeyValueListIndex — the owner is registered under EACH element of its
// collection property (inverted index: element -> {owner keys}).
[TestFixture]
public class CacheCollectionKeyValueListIndexTests {
	private class ListEntity : ICacheEquatable<ListEntity>, ICacheClonable<ListEntity> {
		public int Id { get; set; }
		public List<int> TagIds { get; set; } = new();

		public bool CacheEquals(ListEntity? other) =>
			other is not null && Id == other.Id && TagIds.SequenceEqual(other.TagIds);

		public int CacheGetHashCode() => Id;

		public ListEntity Clone() => new() { Id = Id, TagIds = new List<int>(TagIds) };
	}

	private class ArrayEntity : ICacheEquatable<ArrayEntity>, ICacheClonable<ArrayEntity> {
		public int Id { get; set; }
		public int[] TagIds { get; set; } = Array.Empty<int>();

		public bool CacheEquals(ArrayEntity? other) =>
			other is not null && Id == other.Id && TagIds.SequenceEqual(other.TagIds);

		public int CacheGetHashCode() => Id;

		public ArrayEntity Clone() => new() { Id = Id, TagIds = (int[])TagIds.Clone() };
	}

	private InMemoryDataCache<int, ListEntity> _cache = null!;
	private CacheKeyValueListIndex<int, ListEntity, int> _index = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, ListEntity>();
		_index = _cache.CacheCollectionKeyValueListIndex<int>((_, e) => e.TagIds);
	}

	[Test]
	public void Add_FansOutOwnerUnderEachElement() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 20, 30 } });

		Assert.That(_index.GetValues(10), Has.Member(1));
		Assert.That(_index.GetValues(20), Has.Member(1));
		Assert.That(_index.GetValues(30), Has.Member(1));
	}

	[Test]
	public void Add_SharedElement_CollectsAllOwners() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 20 } });
		_cache.AddOrUpdate(2, new ListEntity { Id = 2, TagIds = new List<int> { 20 } });

		Assert.That(_index.GetValues(20), Has.Count.EqualTo(2));
		Assert.That(_index.GetValues(20), Has.Member(1).And.Member(2));
		Assert.That(_index.GetValues(10), Has.Count.EqualTo(1));
	}

	[Test]
	public void Add_DuplicateElements_DedupedNoMiscount() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 10, 20 } });

		Assert.That(_index.GetValues(10), Has.Count.EqualTo(1));
		Assert.That(_index.GetValues(10), Has.Member(1));
		// keysSize counts distinct index keys (10, 20); ApproximateCount counts (key,owner) edges.
		var keysSize = _index.GetCounters(out var edges);
		Assert.That(keysSize, Is.EqualTo(2));
		Assert.That(edges, Is.EqualTo(2));
	}

	[Test]
	public void Add_EmptyCollection_IndexesNothing() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int>() });

		Assert.That(_index.GetCounters(out _), Is.EqualTo(0));
		Assert.That(_index.GetValues(0), Is.Empty);
	}

	[Test]
	public void Remove_DropsOwnerFromEveryElementBucket() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 20 } });
		_cache.AddOrUpdate(2, new ListEntity { Id = 2, TagIds = new List<int> { 20 } });

		_cache.Remove(1);

		Assert.That(_index.GetValues(10), Is.Empty);
		Assert.That(_index.GetValues(20), Has.Count.EqualTo(1));
		Assert.That(_index.GetValues(20), Has.Member(2));
	}

	[Test]
	public void Update_DiffsElementSets() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 20 } });

		// 10,20 -> 20,30 : bucket 10 loses owner, 20 keeps, 30 gains.
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 20, 30 } });

		Assert.That(_index.GetValues(10), Is.Empty);
		Assert.That(_index.GetValues(20), Has.Member(1));
		Assert.That(_index.GetValues(30), Has.Member(1));
	}

	[Test]
	public void Update_WithDuplicates_StaysConsistent() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10, 10 } });
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10 } });

		Assert.That(_index.GetValues(10), Has.Count.EqualTo(1));
		Assert.That(_index.GetValues(10), Has.Member(1));
		Assert.That(_index.GetCounters(out _), Is.EqualTo(1));
	}

	[Test]
	public void GetValues_AbsentElement_ReturnsEmpty() {
		_cache.AddOrUpdate(1, new ListEntity { Id = 1, TagIds = new List<int> { 10 } });

		Assert.That(_index.GetValues(999), Is.Empty);
	}

	[Test]
	public void ArrayBacked_FansOutUnderEachElement() {
		var cache = new InMemoryDataCache<int, ArrayEntity>();
		var index = cache.CacheCollectionKeyValueListIndex<int>((_, e) => e.TagIds);

		cache.AddOrUpdate(1, new ArrayEntity { Id = 1, TagIds = new[] { 10, 20 } });
		cache.AddOrUpdate(2, new ArrayEntity { Id = 2, TagIds = new[] { 20 } });

		Assert.That(index.GetValues(10), Has.Member(1));
		Assert.That(index.GetValues(20), Has.Count.EqualTo(2));
	}
}

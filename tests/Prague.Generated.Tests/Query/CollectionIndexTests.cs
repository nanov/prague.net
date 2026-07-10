namespace Prague.Generated.Tests.Query;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Layer 2: [DataCacheIndex(Many)] on a collection property emits a symmetric collection index
// (element → {owners} + reverse), and WithXxx queries by a single element against the forward half.

[DataCache]
public partial class CiArticle {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = string.Empty;
	[DataCacheIndex(DataCacheIndexType.Many)] public List<int> TagIds { get; set; } = new();
}

[TestFixture]
public class CollectionIndexTests {
	private DataCacheRegistry _registry = null!;
	private CiArticleCache _articles = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder().Register<CiArticleCache>().Build();
		_articles = _registry.GetCache<CiArticleCache>();

		_articles.AddOrUpdate(new CiArticle { Id = 1, Title = "A", TagIds = new List<int> { 10, 20 } });
		_articles.AddOrUpdate(new CiArticle { Id = 2, Title = "B", TagIds = new List<int> { 10 } });
		_articles.AddOrUpdate(new CiArticle { Id = 3, Title = "C", TagIds = new List<int> { 20, 30 } });
	}

	[Test]
	public void WithTagIds_ReturnsOwnersContainingElement() {
		var byTag10 = _articles.Query().WithTagIds(10).Execute().Select(a => a.Id).OrderBy(i => i).ToArray();
		Assert.That(byTag10, Is.EqualTo(new[] { 1, 2 }));

		var byTag20 = _articles.Query().WithTagIds(20).Execute().Select(a => a.Id).OrderBy(i => i).ToArray();
		Assert.That(byTag20, Is.EqualTo(new[] { 1, 3 }));

		var byTag30 = _articles.Query().WithTagIds(30).Execute().Select(a => a.Id).ToArray();
		Assert.That(byTag30, Is.EqualTo(new[] { 3 }));
	}

	[Test]
	public void WithTagIds_AbsentElement_ReturnsEmpty() {
		Assert.That(_articles.Query().WithTagIds(999).Execute().Count, Is.EqualTo(0));
	}

	[Test]
	public void WithTagIds_AfterUpdate_ReflectsNewMembership() {
		// Re-tag article 1: drop 10, add 30.
		_articles.AddOrUpdate(new CiArticle { Id = 1, Title = "A", TagIds = new List<int> { 20, 30 } });

		var byTag10 = _articles.Query().WithTagIds(10).Execute().Select(a => a.Id).ToArray();
		Assert.That(byTag10, Is.EqualTo(new[] { 2 }));

		var byTag30 = _articles.Query().WithTagIds(30).Execute().Select(a => a.Id).OrderBy(i => i).ToArray();
		Assert.That(byTag30, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithTagIds_SpanOverload_UnionsAcrossElements() {
		var byTags = _articles.Query().WithTagIds(new[] { 30, 10 }).Execute().Select(a => a.Id).OrderBy(i => i).ToArray();
		Assert.That(byTags, Is.EqualTo(new[] { 1, 2, 3 }));
	}
}

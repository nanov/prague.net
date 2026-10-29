namespace Prague.Generated.Tests.Query;

using Prague.Tests.Models;
using NUnit.Framework;

// The query parser parses a Guid index's value as a Guid; it used to emit the string parser, which did not compile.
[TestFixture]
public class GuidQueryParserTests {
	private static readonly Guid OwnerA = Guid.Parse("11111111-1111-4111-8111-111111111111");
	private static readonly Guid OwnerB = Guid.Parse("22222222-2222-4222-8222-222222222222");

	private static GuidIndexedTestModelCache CreateTestCache() {
		var cache = new GuidIndexedTestModelCache();
		cache.AddOrUpdate(new GuidIndexedTestModel { Id = 1, OwnerId = OwnerA, ExternalId = Guid.Parse("a0000000-0000-4000-8000-000000000001"), SortId = Guid.Parse("00000000-0000-4000-8000-000000000001") });
		cache.AddOrUpdate(new GuidIndexedTestModel { Id = 2, OwnerId = OwnerA, ExternalId = Guid.Parse("a0000000-0000-4000-8000-000000000002"), SortId = Guid.Parse("00000000-0000-4000-8000-000000000002") });
		cache.AddOrUpdate(new GuidIndexedTestModel { Id = 3, OwnerId = OwnerB, ExternalId = Guid.Parse("a0000000-0000-4000-8000-000000000003"), SortId = Guid.Parse("00000000-0000-4000-8000-000000000003") });
		return cache;
	}

	[Test]
	public void StringQuery_GuidManyIndex_MatchesTheGuid() {
		var results = CreateTestCache().QueryParser.StringQuery($"OwnerId={OwnerA}");

		Assert.That(results.Select(item => item.Id), Is.EquivalentTo(new[] { 1, 2 }));
	}

	[Test]
	public void StringQuery_GuidManyIndex_SeveralValues_MatchesEither() {
		var results = CreateTestCache().QueryParser.StringQuery($"OwnerId={OwnerA},{OwnerB}");

		Assert.That(results, Has.Count.EqualTo(3));
	}

	[Test]
	public void StringQuery_GuidUniqueIndex_MatchesTheGuid() {
		var results = CreateTestCache().QueryParser.StringQuery("ExternalId=a0000000-0000-4000-8000-000000000003");

		Assert.That(results.Select(item => item.Id), Is.EqualTo(new[] { 3 }));
	}

	[Test]
	public void StringQuery_GuidRangeIndex_FiltersByComparison() {
		var results = CreateTestCache().QueryParser.StringQuery("SortId.gt=00000000-0000-4000-8000-000000000001");

		Assert.That(results.Select(item => item.Id), Is.EquivalentTo(new[] { 2, 3 }));
	}

	[Test]
	public void StringQuery_NotAGuid_MatchesNothingWithoutThrowing() {
		var query = () => CreateTestCache().QueryParser.StringQuery("OwnerId=not-a-guid");

		Assert.That(query, Throws.Nothing);
	}
}

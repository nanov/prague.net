namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Join;
using NUnit.Framework;
using static PreparedParity;

// A prepared execution through the generated WithXxx / JoinWith surface must cost no more bytes than
// the equivalent eager generated query: the generated prepared overloads only append a recorded link at
// build time and forward to the same hand-written narrowers the Core allocation pins already cover.
// Measured relative to eager (same pattern as Prague.Core.Tests.Prepared.PreparedQueryAllocationTests).
[TestFixture]
[NonParallelizable]
public class PreparedGeneratedAllocationTests {
	private const int Iterations = 20_000;

	private ProductListingCache _listings = null!;
	private DataCacheRegistry _registry = null!;
	private AuthorCache _authors = null!;
	private BookCache _books = null!;

	[OneTimeSetUp]
	public void SetUp() {
		_listings = new ProductListingCache();
		for (var i = 0; i < 5_000; i++)
			_listings.AddOrUpdate(MakeListing(i));

		_registry = new DataCacheRegistryBuilder().Register<AuthorCache>().Register<BookCache>().Build();
		_authors = _registry.GetCache<AuthorCache>();
		_books = _registry.GetCache<BookCache>();
		for (var a = 0; a < 40; a++)
			_authors.AddOrUpdate(new Author { Id = a, Name = $"A{a}", Country = "UK" });
		for (var b = 0; b < 2_000; b++)
			_books.AddOrUpdate(new Book { Id = b, Title = $"B{b}", AuthorId = b % 40, Year = 2000 });
	}

	private static long Measure(Action body) {
		for (var i = 0; i < 1_000; i++) body();
		var best = long.MaxValue;
		for (var pass = 0; pass < 3; pass++) {
			var before = GC.GetAllocatedBytesForCurrentThread();
			for (var i = 0; i < Iterations; i++) body();
			best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
		}

		return best;
	}

	[Test]
	public void GeneratedUnique_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _listings.Prepare<long>().WithId(static id => id).Build();
		var id = 1042L;

		var eager = Measure(() => _listings.Query().WithId(id).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(id).Dispose());

		TestContext.Out.WriteLine($"generated unique: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void GeneratedMany_PooledParameterizedWithWhere_AllocatesNoMoreThanEager() {
		var prepared = _listings.Prepare<long>().WithDepartmentId(static d => d).Where(static v => v.IsFeatured).Build();
		var dept = 3L;

		var eager = Measure(() => _listings.Query().WithDepartmentId(dept).Where(static v => v.IsFeatured).ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(dept).Dispose());

		TestContext.Out.WriteLine($"generated many+where: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}

	[Test]
	public void GeneratedJoinWith_PooledParameterized_AllocatesNoMoreThanEager() {
		var prepared = _authors.Prepare<int>().WithId(static id => id).JoinWithBook().Build();
		var id = 7;

		var eager = Measure(() => _authors.Query().WithId(id).JoinWithBook().ExecutePooled().Dispose());
		var command = Measure(() => prepared.ExecutePooled(id).Dispose());

		TestContext.Out.WriteLine($"generated joinWith: eager {(double)eager / Iterations:F1} B/op, prepared {(double)command / Iterations:F1} B/op");
		Assert.That(command, Is.LessThanOrEqualTo(eager + Iterations / 100));
	}
}

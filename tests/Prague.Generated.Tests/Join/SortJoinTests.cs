namespace Prague.Generated.Tests.Join;

using System.Collections.Generic;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Sort × JoinWith matrix coverage at the codegen layer ──────────────────────
//
// Reuses the existing Author / Book / AuthorProfile / AuthorAward models from
// ForeignKeyJoinModels.cs. Exercises every junction in the Sort × JoinWith
// matrix that codegen emits:
//   * Sort at level 0 (no joins after) + every Execute variant + Skip/Take
//   * JoinWith{X} → Sort  (Sort at level 1+; existing path)
//   * JoinWith{X} → JoinWith{Y} → Sort  (Sort at level 2+)
//   * Sort → JoinWith{X}  (SortedQuery<TInner>-wrapped JoinWith emission)
//   * Sort → JoinWith{X} → JoinWith{Y}  (chained after Sort)
//   * JoinWith{X} → Sort → JoinWith{Y}  (Sort interleaved between joins)
//   * Reverse OneToOne via JoinWithAuthorProfile (right-unique-index)

internal sealed class AuthorByIdDesc : IComparer<Author> {
	public int Compare(Author? x, Author? y) => (y?.Id ?? 0).CompareTo(x?.Id ?? 0);
}

internal sealed class AuthorJoinedByLeftIdDesc<T> : IComparer<JoinResult<Author, T>> {
	public int Compare(JoinResult<Author, T> x, JoinResult<Author, T> y) => y.Left.Id.CompareTo(x.Left.Id);
}

internal sealed class AuthorJoinedByLeftIdDesc2<T1, T2> : IComparer<JoinResult<Author, T1, T2>> {
	public int Compare(JoinResult<Author, T1, T2> x, JoinResult<Author, T1, T2> y) => y.Left.Id.CompareTo(x.Left.Id);
}

[TestFixture]
public class SortJoinTests {
	private DataCacheRegistry _registry = null!;
	private AuthorCache _authors = null!;
	private BookCache _books = null!;
	private AuthorAwardCache _awards = null!;
	private AuthorProfileCache _profilesCache = null!;

	[SetUp]
	public void SetUp() {
		// Single registry shape per fixture — always includes AuthorProfileCache so
		// any test in the matrix can use JoinWithAuthorProfile without rebuilding
		// the registry mid-test (which caused stale-cache failures previously).
		_registry = new DataCacheRegistryBuilder()
			.Register<AuthorCache>()
			.Register<BookCache>()
			.Register<AuthorAwardCache>()
			.Register<AuthorProfileCache>()
			.Build();

		_authors = _registry.GetCache<AuthorCache>();
		_books = _registry.GetCache<BookCache>();
		_awards = _registry.GetCache<AuthorAwardCache>();
		_profilesCache = _registry.GetCache<AuthorProfileCache>();

		_authors.AddOrUpdate(new Author { Id = 1, Name = "Tolkien", Country = "UK" });
		_authors.AddOrUpdate(new Author { Id = 2, Name = "Asimov", Country = "US" });
		_authors.AddOrUpdate(new Author { Id = 3, Name = "Lewis", Country = "UK" });

		_books.AddOrUpdate(new Book { Id = 101, AuthorId = 1, Title = "Hobbit", Year = 1937 });
		_books.AddOrUpdate(new Book { Id = 102, AuthorId = 1, Title = "LOTR", Year = 1954 });
		_books.AddOrUpdate(new Book { Id = 201, AuthorId = 2, Title = "Foundation", Year = 1951 });

		_awards.AddOrUpdate(new AuthorAward { Id = 901, AuthorId = 1, AwardName = "Hugo", Year = 1966 });
		_awards.AddOrUpdate(new AuthorAward { Id = 902, AuthorId = 2, AwardName = "Nebula", Year = 1973 });

		_profilesCache.AddOrUpdate(new AuthorProfile { Id = 1, AuthorId = 1, Bio = "UK fantasy", Website = "" });
		_profilesCache.AddOrUpdate(new AuthorProfile { Id = 2, AuthorId = 2, Bio = "US sci-fi", Website = "" });
		// no profile for author 3
	}

	// ── Sort only (level 0) ───────────────────────────────────────────────────

	[Test]
	public void Sort_NoJoins_Execute() {
		var results = _authors.Query().Sort(new AuthorByIdDesc()).Execute();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecutePooled() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooled();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecuteCloned() {
		var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecuteCloned();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecutePooledCloned() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooledCloned();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_SkipTake() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooled(skip: 1, take: 1);
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.First().Id, Is.EqualTo(2));
	}

	// ── JoinWith{X} → Sort (Sort at level 1) ─────────────────────────────────

	[Test]
	public void JoinWithBook_Then_Sort_AtLevel1_Execute() {
		var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(2));
		Assert.That(results.First(r => r.Left.Id == 3).Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinWithBook_Then_Sort_AtLevel1_ExecutePooled() {
		using var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinWithBook_Then_Sort_AtLevel1_ExecuteCloned() {
		var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.ExecuteCloned();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinWithBook_Then_Sort_AtLevel1_ExecutePooledCloned() {
		using var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.ExecutePooledCloned();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinWithBook_Then_Sort_SkipTake_AtLevel1() {
		using var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.ExecutePooled(skip: 1, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.First().Left.Id, Is.EqualTo(2));
	}

	// ── JoinWith{X} → JoinWith{Y} → Sort (Sort at level 2) ───────────────────

	[Test]
	public void JoinWithBook_Then_JoinWithAward_Then_Sort_AtLevel2_Execute() {
		var results = _authors.Query()
			.JoinWithBook()
			.JoinWithAuthorAward()
			.Sort(new AuthorJoinedByLeftIdDesc2<QueryResults<Book>, QueryResults<AuthorAward>>())
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinWithBook_Then_JoinWithAward_Then_Sort_AtLevel2_ExecutePooled() {
		using var results = _authors.Query()
			.JoinWithBook()
			.JoinWithAuthorAward()
			.Sort(new AuthorJoinedByLeftIdDesc2<QueryResults<Book>, QueryResults<AuthorAward>>())
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
	}

	// ── Sort → JoinWith{X} ────────────────────────────────────────────────────

	[Test]
	public void Sort_Then_JoinWithBook_Execute() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinWithBook()
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(2));
		Assert.That(results.First(r => r.Left.Id == 3).Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void Sort_Then_JoinWithBook_ExecutePooled() {
		using var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinWithBook()
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_Then_JoinWithBook_Then_JoinWithAward() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinWithBook()
			.JoinWithAuthorAward()
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinWithBook_Then_Sort_Then_JoinWithAward() {
		var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.JoinWithAuthorAward()
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
	}

	// ── Reverse OneToOne (new — JoinWithAuthorProfile) ───────────────────────

	[Test]
	public void Baseline_JoinWithAuthorProfile_NoSort() {
		var results = _authors.Query()
			.JoinWithAuthorProfile()
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.First(r => r.Left.Id == 1).Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(results.First(r => r.Left.Id == 2).Right!.Bio, Is.EqualTo("US sci-fi"));
		Assert.That(results.First(r => r.Left.Id == 3).Right, Is.Null);
	}

	[Test]
	public void Sort_Then_JoinWithAuthorProfile_Execute() {
		// AuthorProfile.AuthorId is OneToOne → reverse JoinOne (right-unique-index).
		// Author 3 has no profile → Right is null (nullable single value, no fan-out).
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinWithAuthorProfile()
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(results.First(r => r.Left.Id == 2).Right!.Bio, Is.EqualTo("US sci-fi"));
		Assert.That(results.First(r => r.Left.Id == 3).Right, Is.Null);
	}

	[Test]
	public void JoinWithBook_Then_Sort_Then_JoinWithAuthorProfile() {
		var results = _authors.Query()
			.JoinWithBook()
			.Sort(new AuthorJoinedByLeftIdDesc<QueryResults<Book>>())
			.JoinWithAuthorProfile()
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(byId[3].Right2, Is.Null);
	}
}

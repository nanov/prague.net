namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Author has a symmetric many index on CountryCode (left side); Book has a
// many index on Country (right side). Joining Author → Books via shared
// CountryCode pulls every Book in the same country.
//
//   MlsAuthor (PK Id, string Country) — sym list index on Country
//   MlsBook   (PK Id, string Country) — list index on Country

internal sealed class MlsAuthor : ICacheEquatable<MlsAuthor>, ICacheClonable<MlsAuthor> {
	public int Id { get; init; }
	public string Country { get; init; } = "";
	public string Name { get; init; } = "";

	public bool CacheEquals(MlsAuthor? other) => other is not null && other.Id == Id && other.Country == Country && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Country, Name);
	public MlsAuthor Clone() => new() { Id = Id, Country = Country, Name = Name };
}

internal sealed class MlsBook : ICacheEquatable<MlsBook>, ICacheClonable<MlsBook> {
	public int Id { get; init; }
	public string Country { get; init; } = "";
	public string Title { get; init; } = "";

	public bool CacheEquals(MlsBook? other) => other is not null && other.Id == Id && other.Country == Country && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Country, Title);
	public MlsBook Clone() => new() { Id = Id, Country = Country, Title = Title };
}

[TestFixture]
public class JoinManyLeftSymCoreTests {
	private InMemoryDataCache<int, MlsAuthor> _authors = null!;
	private InMemoryDataCache<int, MlsBook> _books = null!;
	private CacheSymmetricKeyValueListIndex<int, MlsAuthor, string> _authorCountrySymIdx = null!;
	private CacheKeyValueListIndex<int, MlsBook, string> _bookCountryIdx = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, MlsAuthor>();
		_books = new InMemoryDataCache<int, MlsBook>();
		_authorCountrySymIdx = _authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_bookCountryIdx = _books.CacheKeyValueListIndex<string>((_, v) => v.Country);

		_authors.AddOrUpdate(1, new MlsAuthor { Id = 1, Country = "UK", Name = "Tolkien" });
		_authors.AddOrUpdate(2, new MlsAuthor { Id = 2, Country = "UK", Name = "Lewis" });
		_authors.AddOrUpdate(3, new MlsAuthor { Id = 3, Country = "US", Name = "Asimov" });
		_authors.AddOrUpdate(4, new MlsAuthor { Id = 4, Country = "FR", Name = "OrphanFrench" });

		_books.AddOrUpdate(101, new MlsBook { Id = 101, Country = "UK", Title = "Hobbit" });
		_books.AddOrUpdate(102, new MlsBook { Id = 102, Country = "UK", Title = "Narnia" });
		_books.AddOrUpdate(201, new MlsBook { Id = 201, Country = "US", Title = "Foundation" });
		_books.AddOrUpdate(202, new MlsBook { Id = 202, Country = "US", Title = "I, Robot" });
		// No books for FR.
	}

	// ── Outer ─────────────────────────────────────────────────────────────────

	[Test]
	public void JoinMany_LeftSym_OuterFullMatch_FansOutByCountry() {
		var results = _authors.Query()
			.JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		var byId = results.ToDictionary(r => r.Left.Id);

		// Both UK authors get both UK books.
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right.Select(b => b.Title).OrderBy(t => t),
			Is.EqualTo(new[] { "Hobbit", "Narnia" }));
		Assert.That(byId[2].Right.Count, Is.EqualTo(2));

		// US author gets US books.
		Assert.That(byId[3].Right.Count, Is.EqualTo(2));

		// FR author has no books.
		Assert.That(byId[4].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_LeftSym_WithFilter_NarrowsRights() {
		var results = _authors.Query()
			.JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx,
				q => q.Where(b => b.Title.StartsWith("H") || b.Title.StartsWith("F")))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[3].Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void JoinMany_LeftSym_WithFilterAndArg_StaticLambda() {
		const string prefix = "Hob";
		var results = _authors.Query()
			.JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[2].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
	}

	// ── Inner ─────────────────────────────────────────────────────────────────

	[Test]
	public void InnerJoinMany_LeftSym_DropsOrphanFrenchAuthor() {
		var results = _authors.Query()
			.InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx)
			.Execute();

		var ids = results.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
	}

	[Test]
	public void InnerJoinMany_LeftSym_FilterDropsLeftsWithNoMatches() {
		// Filter keeps only books starting with "Found" — only US books match
		var results = _authors.Query()
			.InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx,
				q => q.Where(b => b.Title.StartsWith("Found")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(3));
		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void InnerJoinMany_LeftSym_FilterAndArg_StaticLambda() {
		const string prefix = "Nar";
		var results = _authors.Query()
			.InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		// Only UK has Narnia — both UK authors get it.
		var ids = results.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right.Single().Title == "Narnia"), Is.True);
	}

	// ── Func selector ─────────────────────────────────────────────────────────
	//
	// Authors store lowercase country codes ("uk", "us", "fr"); books store
	// uppercase ("UK", "US", "FR"). A Func<string,string> uppercase selector
	// bridges the left lookupKey → right indexKey.

	private InMemoryDataCache<int, MlsAuthor> _lcAuthors = null!;
	private InMemoryDataCache<int, MlsBook> _ucBooks = null!;
	private CacheSymmetricKeyValueListIndex<int, MlsAuthor, string> _lcAuthorCountrySymIdx = null!;
	private CacheKeyValueListIndex<int, MlsBook, string> _ucBookCountryIdx = null!;

	private void SetUpCaseMismatchedCaches() {
		_lcAuthors = new InMemoryDataCache<int, MlsAuthor>();
		_ucBooks = new InMemoryDataCache<int, MlsBook>();
		_lcAuthorCountrySymIdx = _lcAuthors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_ucBookCountryIdx = _ucBooks.CacheKeyValueListIndex<string>((_, v) => v.Country);

		_lcAuthors.AddOrUpdate(1, new MlsAuthor { Id = 1, Country = "uk", Name = "Tolkien" });
		_lcAuthors.AddOrUpdate(2, new MlsAuthor { Id = 2, Country = "uk", Name = "Lewis" });
		_lcAuthors.AddOrUpdate(3, new MlsAuthor { Id = 3, Country = "us", Name = "Asimov" });
		_lcAuthors.AddOrUpdate(4, new MlsAuthor { Id = 4, Country = "fr", Name = "OrphanFrench" });

		_ucBooks.AddOrUpdate(101, new MlsBook { Id = 101, Country = "UK", Title = "Hobbit" });
		_ucBooks.AddOrUpdate(102, new MlsBook { Id = 102, Country = "UK", Title = "Narnia" });
		_ucBooks.AddOrUpdate(201, new MlsBook { Id = 201, Country = "US", Title = "Foundation" });
		_ucBooks.AddOrUpdate(202, new MlsBook { Id = 202, Country = "US", Title = "I, Robot" });
	}

	[Test]
	public void JoinMany_LeftSym_FuncSelector_OuterFullMatch() {
		SetUpCaseMismatchedCaches();
		var results = _lcAuthors.Query()
			.JoinMany(_lcAuthorCountrySymIdx, (string lc) => lc.ToUpperInvariant(), _ucBooks, _ucBookCountryIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[2].Right.Count, Is.EqualTo(2));
		Assert.That(byId[3].Right.Count, Is.EqualTo(2));
		Assert.That(byId[4].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_LeftSym_FuncSelector_WithFilter_NarrowsRights() {
		SetUpCaseMismatchedCaches();
		var results = _lcAuthors.Query()
			.JoinMany(_lcAuthorCountrySymIdx, (string lc) => lc.ToUpperInvariant(), _ucBooks, _ucBookCountryIdx,
				q => q.Where(b => b.Title.StartsWith("H") || b.Title.StartsWith("F")))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[3].Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void JoinMany_LeftSym_FuncSelector_WithFilterAndArg_StaticLambda() {
		SetUpCaseMismatchedCaches();
		const string prefix = "Hob";
		var results = _lcAuthors.Query()
			.JoinMany(_lcAuthorCountrySymIdx, (string lc) => lc.ToUpperInvariant(), _ucBooks, _ucBookCountryIdx,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[2].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinMany_LeftSym_FuncSelector_WithFilter_DropsLefts() {
		SetUpCaseMismatchedCaches();
		var results = _lcAuthors.Query()
			.InnerJoinMany(_lcAuthorCountrySymIdx, (string lc) => lc.ToUpperInvariant(), _ucBooks, _ucBookCountryIdx,
				q => q.Where(b => b.Title.StartsWith("Found")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(3));
		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void JoinMany_LeftSym_FuncArgSelector_OuterFullMatch() {
		SetUpCaseMismatchedCaches();
		const string prefix = "pre-";
		// Storage in left index is plain lowercase. Selector argument is a prefix
		// that gets prepended then stripped — exercises the TSelectorArg path while
		// still producing the same uppercased code.
		var results = _lcAuthors.Query()
			.JoinMany(_lcAuthorCountrySymIdx,
				static (string lc, string p) => (p + lc).Substring(p.Length).ToUpperInvariant(),
				prefix,
				_ucBooks, _ucBookCountryIdx)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[3].Right.Count, Is.EqualTo(2));
		Assert.That(byId[4].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinMany_LeftSym_FuncArgSelector_WithFilterAndArg() {
		SetUpCaseMismatchedCaches();
		const string sep = "_";
		const string titlePrefix = "Nar";
		// Selector arg is a separator; we synthesize "uk_" then strip it back to
		// "uk" and uppercase. Combines TSelectorArg + TFilterArg in one call.
		var results = _lcAuthors.Query()
			.InnerJoinMany(_lcAuthorCountrySymIdx,
				static (string lc, string s) => (lc + s).Split(s)[0].ToUpperInvariant(),
				sep,
				_ucBooks, _ucBookCountryIdx,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				titlePrefix)
			.Execute();

		var ids = results.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right.Single().Title == "Narnia"), Is.True);
	}
}

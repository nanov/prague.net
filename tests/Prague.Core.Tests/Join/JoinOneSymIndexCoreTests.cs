namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// Hand-rolled POCOs — no [DataCache], no codegen. The left side carries a
// Country code that joins to the right side's Code PK via a manually-built
// CacheSymmetricKeyValueListIndex. Distinct names ("SymBook"/"SymCountry"
// is fine — different namespace from the Generated.Tests version).

internal sealed class SymBook : ICacheEquatable<SymBook>, ICacheClonable<SymBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public string Country { get; init; } = "";

	public bool CacheEquals(SymBook? other) => other is not null && other.Id == Id && other.Title == Title && other.Country == Country;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, Country);
	public SymBook Clone() => new() { Id = Id, Title = Title, Country = Country };
}

internal sealed class SymCountry : ICacheEquatable<SymCountry>, ICacheClonable<SymCountry> {
	public string Code { get; init; } = "";
	public string Name { get; init; } = "";
	public int Numeric { get; init; }

	public bool CacheEquals(SymCountry? other) => other is not null && other.Code == Code && other.Name == Name && other.Numeric == Numeric;
	public int CacheGetHashCode() => HashCode.Combine(Code, Name, Numeric);
	public SymCountry Clone() => new() { Code = Code, Name = Name, Numeric = Numeric };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneSymIndexCoreTests {
	private InMemoryDataCache<int, SymBook> _bookCache = null!;
	private InMemoryDataCache<string, SymCountry> _countryCache = null!;
	private CacheSymmetricKeyValueListIndex<int, SymBook, string> _countryIndex = null!;
	// Right unique index over country PK keyed by Numeric — used by Shape B tests.
	private CacheUniqueIndex<string, SymCountry, int> _countryNumericIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, SymBook>();
		_countryCache = new InMemoryDataCache<string, SymCountry>();
		_countryIndex = _bookCache.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_countryNumericIndex = _countryCache.AddKeyValueIndex<int>((_, v) => v.Numeric);

		// Countries
		_countryCache.AddOrUpdate("GB", new SymCountry { Code = "GB", Name = "United Kingdom", Numeric = 44 });
		_countryCache.AddOrUpdate("US", new SymCountry { Code = "US", Name = "United States", Numeric = 1 });

		// Books — 1 and 3 share GB, 2 is US, 4 is XX (no country entry)
		_bookCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "Book GB 1", Country = "GB" });
		_bookCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "Book US",   Country = "US" });
		_bookCache.AddOrUpdate(3, new SymBook { Id = 3, Title = "Book GB 2", Country = "GB" });
		_bookCache.AddOrUpdate(4, new SymBook { Id = 4, Title = "Book XX",   Country = "XX" });
	}

	// ── Shape A: leftSymIndex + rightCache (no rightIndex) ───────────────────

	[Test]
	public void JoinOne_SymShapeA_MatchExists_AttachesCountry() {
		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[3].Right!.Name, Is.EqualTo("United Kingdom"));
	}

	[Test]
	public void JoinOne_SymShapeA_NoMatchInRightCache_ReturnsNullRight() {
		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[4].Right, Is.Null, "Book id=4 has Country=XX which is not in the country cache");
	}

	[Test]
	public void JoinOne_SymShapeA_FanOut_MultipleBooksShareCountry() {
		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Code, Is.EqualTo("GB"));
		Assert.That(byId[3].Right!.Code, Is.EqualTo("GB"));
		Assert.That(byId[1].Right!.Name, Is.EqualTo(byId[3].Right!.Name),
			"Both GB books should receive identical Country values via fan-out");
	}

	[Test]
	public void JoinOne_SymShapeA_WithFilter_NarrowsRightSide() {
		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache, q => q.Where(c => c.Code == "US"))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[2].Right, Is.Not.Null, "US book resolves");
		Assert.That(byId[2].Right!.Code, Is.EqualTo("US"));
		Assert.That(byId[1].Right, Is.Null, "GB book filtered out");
		Assert.That(byId[3].Right, Is.Null, "GB book filtered out");
	}

	[Test]
	public void JoinOne_SymShapeA_WithFilterAndArg_StaticLambda() {
		const string allowedCode = "GB";
		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache,
				static (q, code) => q.Where(c => c.Code == code),
				allowedCode)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "US book filtered out by static-lambda predicate");
	}

	// ── Shape B: leftSymIndex + rightCache + rightIndex (unique 1:1 translation) ──

	[Test]
	public void JoinOne_SymShapeB_ExplicitRightIndex_MatchExists() {
		// Bridge book.Country (string) → country.Numeric (int) → ... but in this fixture
		// the index lookup value type (TLookupKey) and the right-side index key must
		// match. We need a sym-list index whose value type is what the right-side unique
		// index is keyed by. Build a per-test bookCache with a numeric Country index.
		var bookNumCache = new InMemoryDataCache<int, SymBook>();
		var bookCountryNumericIndex = bookNumCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.Country switch {
			"GB" => 44,
			"US" => 1,
			_ => 999,
		});

		bookNumCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "Book GB 1", Country = "GB" });
		bookNumCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "Book US",   Country = "US" });
		bookNumCache.AddOrUpdate(3, new SymBook { Id = 3, Title = "Book GB 2", Country = "GB" });
		bookNumCache.AddOrUpdate(4, new SymBook { Id = 4, Title = "Book XX",   Country = "XX" });

		var results = bookNumCache.Query()
			.JoinOne(bookCountryNumericIndex, _countryCache, _countryNumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
		Assert.That(byId[3].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[4].Right, Is.Null, "Numeric 999 has no country entry");
	}

	[Test]
	public void JoinOne_SymShapeB_WithFilter_NarrowsRight() {
		var bookNumCache = new InMemoryDataCache<int, SymBook>();
		var bookCountryNumericIndex = bookNumCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.Country switch {
			"GB" => 44,
			"US" => 1,
			_ => 999,
		});

		bookNumCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "Book GB 1", Country = "GB" });
		bookNumCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "Book US",   Country = "US" });
		bookNumCache.AddOrUpdate(3, new SymBook { Id = 3, Title = "Book GB 2", Country = "GB" });

		var results = bookNumCache.Query()
			.JoinOne(bookCountryNumericIndex, _countryCache, _countryNumericIndex,
				q => q.Where(c => c.Code == "GB"))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Code, Is.EqualTo("GB"));
		Assert.That(byId[3].Right, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "US book filtered out");
	}

	[Test]
	public void JoinOne_SymShapeB_WithFilterAndArg_StaticLambda() {
		var bookNumCache = new InMemoryDataCache<int, SymBook>();
		var bookCountryNumericIndex = bookNumCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.Country switch {
			"GB" => 44,
			"US" => 1,
			_ => 999,
		});

		bookNumCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "Book GB", Country = "GB" });
		bookNumCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "Book US", Country = "US" });

		const string allowedCode = "US";
		var results = bookNumCache.Query()
			.JoinOne(bookCountryNumericIndex, _countryCache, _countryNumericIndex,
				static (q, code) => q.Where(c => c.Code == code),
				allowedCode)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Code, Is.EqualTo("US"));
		Assert.That(byId[1].Right, Is.Null, "GB book filtered out by static lambda");
	}

	// ── Inner tests ───────────────────────────────────────────────────────────

	[Test]
	public void InnerJoinOne_ShapeA_DropsBooksWithUnknownCountry() {
		// Book 4 has country "XX" which doesn't exist in _countryCache → must be dropped.
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3), "Book 4 dropped (XX unmapped)");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
	}

	[Test]
	public void InnerJoinOne_ShapeA_FanOut_BothBooksWithSameCountryKept() {
		// Books 1 and 3 share GB — both must survive and both attach the same SymCountry.
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.Execute();

		var gbBooks = results.Where(r => r.Left.Country == "GB").ToList();
		Assert.That(gbBooks.Count, Is.EqualTo(2));
		Assert.That(gbBooks.All(r => r.Right!.Code == "GB"), Is.True);
	}

	[Test]
	public void InnerJoinOne_ShapeA_AllUnmatched_EmptyResult() {
		var bookCache = new InMemoryDataCache<int, SymBook>();
		var countryIndex = bookCache.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		bookCache.AddOrUpdate(99, new SymBook { Id = 99, Title = "Orphan", Country = "ZZ" });

		var results = bookCache.Query()
			.InnerJoinOne(countryIndex, _countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void InnerJoinOne_ShapeA_WithFilter_DropsFilteredOut() {
		// Filter: only allow code == "US"
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache,
				q => q.Where(c => c.Code == "US"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1), "Only Book 2 (US) survives the filter");
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Code, Is.EqualTo("US"));
	}

	[Test]
	public void InnerJoinOne_ShapeB_WithRightIndex_DropsUnmatched() {
		// Shape B: book has Numeric country code; resolve via _countryNumericIndex.
		var bookNumCache = new InMemoryDataCache<int, SymBook>();
		var bookCountryNumericIndex = bookNumCache.CacheSymmetricKeyValueListIndex<int>((_, v) => int.Parse(v.Country));

		bookNumCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "GB Book", Country = "44" });   // matches GB.Numeric=44
		bookNumCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "US Book", Country = "1"  });   // matches US.Numeric=1
		bookNumCache.AddOrUpdate(3, new SymBook { Id = 3, Title = "ZZ Book", Country = "999" });  // no country with Numeric=999

		var results = bookNumCache.Query()
			.InnerJoinOne(bookCountryNumericIndex, _countryCache, _countryNumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void InnerJoinOne_ShapeB_WithFilterArg_StaticLambda() {
		var bookNumCache = new InMemoryDataCache<int, SymBook>();
		var bookCountryNumericIndex = bookNumCache.CacheSymmetricKeyValueListIndex<int>((_, v) => int.Parse(v.Country));

		bookNumCache.AddOrUpdate(1, new SymBook { Id = 1, Title = "GB Book", Country = "44" });
		bookNumCache.AddOrUpdate(2, new SymBook { Id = 2, Title = "US Book", Country = "1"  });

		const string allowedCode = "US";
		var results = bookNumCache.Query()
			.InnerJoinOne(bookCountryNumericIndex, _countryCache, _countryNumericIndex,
				static (q, code) => q.Where(c => c.Code == code), allowedCode)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Code, Is.EqualTo("US"));
	}
}

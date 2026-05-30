namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// SelLeft / SelRight have different PK types (int vs long) so a selector is
// required to bridge them. Used by PK-to-PK + selector tests.

[DataCache]
public partial class SelLeft {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";
}

[DataCache]
public partial class SelRight {
	[DataCacheKey] public long Id { get; set; }
	public string Label { get; set; } = "";
}

// SelSymBook / SelSymCountry — for sym-index selector tests. Country is keyed
// by string (e.g. "GB"); the symIndex stores int CountryCode (1, 44) on each
// book and the selector translates int → string.

[DataCache]
public partial class SelSymBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";

	[DataCacheIndex(DataCacheIndexType.Many, Symmetric = true)]
	public int CountryCode { get; set; }
}

[DataCache]
public partial class SelSymCountry {
	[DataCacheKey] public string Code { get; set; } = "";
	public string Name { get; set; } = "";

	// Used for Shape-B + selector tests: rightIndex keyed by int (the selector's output).
	[DataCacheIndex(DataCacheIndexType.Unique)]
	public int Numeric { get; set; }
}

// SelRuBook / SelRuBookInfo — right-unique-index + selector. BookInfo.BookKey is
// a long unique index (the selector's output type); the left's int Id is mapped
// to long via the selector.

[DataCache]
public partial class SelRuBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";
}

[DataCache]
public partial class SelRuBookInfo {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Unique)]
	public long BookKey { get; set; }

	public string Synopsis { get; set; } = "";
}

// SelLuAuthor / SelLuBook — left-unique-index + selector. Author's BookId is a
// long (the index value); the selector translates long → int (book's PK).

[DataCache]
public partial class SelLuAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	[DataCacheIndex(DataCacheIndexType.Unique, Symmetric = true)]
	public long BookKey { get; set; }
}

[DataCache]
public partial class SelLuBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneKeySelectorTests {
	private DataCacheRegistry _registry = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<SelLeftCache>()
			.Register<SelRightCache>()
			.Register<SelSymBookCache>()
			.Register<SelSymCountryCache>()
			.Register<SelRuBookCache>()
			.Register<SelRuBookInfoCache>()
			.Register<SelLuAuthorCache>()
			.Register<SelLuBookCache>()
			.Build();
	}

	// ── PK-to-PK + selector ──────────────────────────────────────────────────

	[Test]
	public void JoinOne_PkToPk_WithSelector_MapsIntToLong() {
		var leftCache = _registry.GetCache<SelLeftCache>();
		var rightCache = _registry.GetCache<SelRightCache>();

		leftCache.AddOrUpdate(new SelLeft { Id = 1, Name = "L1" });
		leftCache.AddOrUpdate(new SelLeft { Id = 2, Name = "L2" });
		leftCache.AddOrUpdate(new SelLeft { Id = 3, Name = "L3" });
		rightCache.AddOrUpdate(new SelRight { Id = 1001L, Label = "R1" });
		rightCache.AddOrUpdate(new SelRight { Id = 1002L, Label = "R2" });
		// no right with Id = 1003

		var results = leftCache.Cache.Query()
			.JoinOne((int lk) => 1000L + lk, rightCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Label, Is.EqualTo("R1"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Label, Is.EqualTo("R2"));
		Assert.That(byId[3].Right, Is.Null, "Right.Id=1003 not present");
	}

	[Test]
	public void JoinOne_PkToPk_WithSelectorAndArg_MapsViaCapturedOffset() {
		var leftCache = _registry.GetCache<SelLeftCache>();
		var rightCache = _registry.GetCache<SelRightCache>();

		leftCache.AddOrUpdate(new SelLeft { Id = 1, Name = "L1" });
		leftCache.AddOrUpdate(new SelLeft { Id = 2, Name = "L2" });
		rightCache.AddOrUpdate(new SelRight { Id = 2001L, Label = "RA" });
		rightCache.AddOrUpdate(new SelRight { Id = 2002L, Label = "RB" });

		var offset = 2000L;
		var results = leftCache.Cache.Query()
			.JoinOne(static (int lk, long o) => o + lk, offset, rightCache)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Label, Is.EqualTo("RA"));
		Assert.That(byId[2].Right!.Label, Is.EqualTo("RB"));
	}

	// ── Sym-index Shape A + selector ─────────────────────────────────────────

	[Test]
	public void JoinOne_SymShapeA_WithSelector_MapsIntToString() {
		var bookCache = _registry.GetCache<SelSymBookCache>();
		var countryCache = _registry.GetCache<SelSymCountryCache>();

		countryCache.AddOrUpdate(new SelSymCountry { Code = "GB", Name = "United Kingdom", Numeric = 44 });
		countryCache.AddOrUpdate(new SelSymCountry { Code = "US", Name = "United States",  Numeric = 1 });

		bookCache.AddOrUpdate(new SelSymBook { Id = 1, Title = "Book GB", CountryCode = 44 });
		bookCache.AddOrUpdate(new SelSymBook { Id = 2, Title = "Book US", CountryCode = 1 });
		bookCache.AddOrUpdate(new SelSymBook { Id = 3, Title = "Book ZZ", CountryCode = 999 });

		// Shape A: selector maps the index value (CountryCode, int) directly to the
		// right PK (Code, string).
		var results = bookCache.Cache.Query()
			.JoinOne(bookCache.CountryCodeIndex, (int code) => code switch {
				44 => "GB",
				1 => "US",
				_ => "??",
			}, countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Sym-index Shape B + selector ─────────────────────────────────────────

	[Test]
	public void JoinOne_SymShapeB_WithSelector_MapsToRightIndexKey() {
		var bookCache = _registry.GetCache<SelSymBookCache>();
		var countryCache = _registry.GetCache<SelSymCountryCache>();

		countryCache.AddOrUpdate(new SelSymCountry { Code = "GB", Name = "United Kingdom", Numeric = 44 });
		countryCache.AddOrUpdate(new SelSymCountry { Code = "US", Name = "United States",  Numeric = 1 });

		bookCache.AddOrUpdate(new SelSymBook { Id = 1, Title = "Book GB", CountryCode = 44 });
		bookCache.AddOrUpdate(new SelSymBook { Id = 2, Title = "Book US", CountryCode = 1 });
		bookCache.AddOrUpdate(new SelSymBook { Id = 3, Title = "Book ZZ", CountryCode = 999 });

		// Shape B: selector maps CountryCode (int) → int (identity here, but rightIndex
		// is keyed by int — distinct from the country's string PK). The Numeric unique
		// index on Country translates the int index key → string PK → country record.
		var results = bookCache.Cache.Query()
			.JoinOne(bookCache.CountryCodeIndex, (int code) => code, countryCache, countryCache.NumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Right-unique-index + selector ────────────────────────────────────────

	[Test]
	public void JoinOne_RightUniqueIndex_WithSelector_MapsIntToLong() {
		var bookCache = _registry.GetCache<SelRuBookCache>();
		var infoCache = _registry.GetCache<SelRuBookInfoCache>();

		bookCache.AddOrUpdate(new SelRuBook { Id = 1, Title = "Book One" });
		bookCache.AddOrUpdate(new SelRuBook { Id = 2, Title = "Book Two" });
		bookCache.AddOrUpdate(new SelRuBook { Id = 3, Title = "Book Three" });

		infoCache.AddOrUpdate(new SelRuBookInfo { Id = 101, BookKey = 5001L, Synopsis = "S1" });
		infoCache.AddOrUpdate(new SelRuBookInfo { Id = 102, BookKey = 5002L, Synopsis = "S2" });
		// no info for book 3

		// Selector outputs long, matching infoCache.BookKeyIndex's TIndexKey.
		var results = bookCache.Cache.Query()
			.JoinOne((int id) => 5000L + id, infoCache, infoCache.BookKeyIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("S1"));
		Assert.That(byId[2].Right!.Synopsis, Is.EqualTo("S2"));
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Left-unique-index + selector ─────────────────────────────────────────

	[Test]
	public void JoinOne_LeftUniqueIndex_WithSelector_MapsLongToInt() {
		var authorCache = _registry.GetCache<SelLuAuthorCache>();
		var bookCache = _registry.GetCache<SelLuBookCache>();

		bookCache.AddOrUpdate(new SelLuBook { Id = 1, Title = "Book A" });
		bookCache.AddOrUpdate(new SelLuBook { Id = 2, Title = "Book B" });
		// no book Id = 3

		authorCache.AddOrUpdate(new SelLuAuthor { Id = 10, Name = "Alice", BookKey = 7001L });
		authorCache.AddOrUpdate(new SelLuAuthor { Id = 20, Name = "Bob",   BookKey = 7002L });
		authorCache.AddOrUpdate(new SelLuAuthor { Id = 30, Name = "Carol", BookKey = 7003L });

		// Selector maps the long BookKey (leftIndex value) to int (book's PK).
		var results = authorCache.Cache.Query()
			.JoinOne(authorCache.BookKeyIndex, (long k) => (int)(k - 7000L), bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[10].Right!.Title, Is.EqualTo("Book A"));
		Assert.That(byId[20].Right!.Title, Is.EqualTo("Book B"));
		Assert.That(byId[30].Right, Is.Null);
	}
}

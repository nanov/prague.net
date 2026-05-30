namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// Use "SymBook" / "SymCountry" to avoid collision with the existing "Book" in ForeignKeyJoinModels.cs.

[DataCache]
public partial class SymBook {
	[DataCacheKey] public int Id { get; set; }
	public string Title { get; set; } = "";

	/// <summary>
	/// Country code of this book's category. Used as the join key to <see cref="SymCountry.Code"/>.
	/// <c>Symmetric = true</c> emits a <c>CacheSymmetricKeyValueListIndex</c> so that
	/// the reverse map (bookId → countryCode) is maintained automatically.
	/// </summary>
	[DataCacheIndex(DataCacheIndexType.Many, Symmetric = true)]
	public string Country { get; set; } = "";
}

[DataCache]
public partial class SymCountry {
	[DataCacheKey] public string Code { get; set; } = "";
	public string Name { get; set; } = "";
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneSymIndexTests {
	private DataCacheRegistry _registry = null!;
	private SymBookCache _bookCache = null!;
	private SymCountryCache _countryCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<SymBookCache>()
			.Register<SymCountryCache>()
			.Build();

		_bookCache = _registry.GetCache<SymBookCache>();
		_countryCache = _registry.GetCache<SymCountryCache>();

		// Countries
		_countryCache.AddOrUpdate(new SymCountry { Code = "GB", Name = "United Kingdom" });
		_countryCache.AddOrUpdate(new SymCountry { Code = "US", Name = "United States" });

		// Books: books 1 and 3 are in GB, book 2 is in US, book 4 has no country entry
		_bookCache.AddOrUpdate(new SymBook { Id = 1, Title = "Book GB 1", Country = "GB" });
		_bookCache.AddOrUpdate(new SymBook { Id = 2, Title = "Book US",   Country = "US" });
		_bookCache.AddOrUpdate(new SymBook { Id = 3, Title = "Book GB 2", Country = "GB" });
		_bookCache.AddOrUpdate(new SymBook { Id = 4, Title = "Book XX",   Country = "XX" });
	}

	[Test]
	public void JoinOne_BookCountry_MatchExists_AttachesCountry() {
		// SymBook.Country = "GB" should resolve to countryCache["GB"] = { Code="GB", Name="United Kingdom" }
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookCache.CountryIndex, _countryCache)
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
	public void JoinOne_BookCountry_NoMatchInCountryCache_ReturnsNull() {
		// SymBook id=4 has Country="XX" which has no entry in countryCache.
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookCache.CountryIndex, _countryCache)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[4].Right, Is.Null);
	}

	[Test]
	public void JoinOne_BookCountry_MultipleBooksShareCountry() {
		// Books 1 and 3 both map to "GB"; both should receive the same Country value.
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookCache.CountryIndex, _countryCache)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Code, Is.EqualTo("GB"));
		Assert.That(byId[3].Right!.Code, Is.EqualTo("GB"));
		Assert.That(byId[1].Right!.Name, Is.EqualTo(byId[3].Right!.Name),
			"Both GB books should receive identical Country name");
	}

	[Test]
	public void JoinOne_BookCountry_WithFilter_NarrowsRightSide() {
		// Only include right-side countries whose code is "US"; GB books should get null.
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookCache.CountryIndex, _countryCache,
				q => q.UseIndex(_countryCache.Cache.KeyIndex, "US"))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[2].Right, Is.Not.Null, "US book should still resolve");
		Assert.That(byId[2].Right!.Code, Is.EqualTo("US"));
		Assert.That(byId[1].Right, Is.Null, "GB book filtered out");
		Assert.That(byId[3].Right, Is.Null, "GB book filtered out");
	}

	[Test]
	public void JoinOne_BookCountry_WithFilterAndArg_StaticLambda() {
		// Same as above but using the TArg overload with a static lambda (zero-alloc).
		const string allowedCode = "GB";
		var results = _bookCache.Cache.Query()
			.JoinOne(_bookCache.CountryIndex, _countryCache,
				static (q, code) => q.UseIndex(q._leftQuery._dataCache.KeyIndex, code),
				allowedCode)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null, "GB book 1 should resolve");
		Assert.That(byId[3].Right, Is.Not.Null, "GB book 3 should resolve");
		Assert.That(byId[2].Right, Is.Null, "US book filtered out");
	}

	// ── Phase α smoke test ────────────────────────────────────────────────────

	[Test]
	public void UseIndexAsPairs_ProducesPairedCandidates_ExecuteReturnsMatchingBooks() {
		// The SetUp already has: GB×2, US×1, XX×1.
		// Add two more books so the fixture totals are unambiguous.
		_bookCache.AddOrUpdate(new SymBook { Id = 5, Title = "Book US-2", Country = "US" });

		var lookups = new[] { "US" };
		var result = _bookCache.Cache.Query()
			.UseIndexAsPairs(_bookCache.CountryIndex, lookups.AsSpan())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2), "Should return exactly the two US books");
		Assert.That(result.All(b => b.Country == "US"), Is.True, "All returned books should have Country=US");
	}
}

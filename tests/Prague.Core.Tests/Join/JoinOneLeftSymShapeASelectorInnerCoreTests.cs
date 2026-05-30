namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// LeftSym Shape A + key-selector Inner: the left book carries a string country
// code (TLookupKey) that the selector maps to an int numeric code (TRightKey).
// Multiple books may share the same lookup value to exercise fan-out semantics:
// a missing right drops the WHOLE fan-out group; a filter-rejected right drops
// just the books mapped to that right. POCOs scoped to this file (SelSymInner*)
// to avoid clashing with the rest of the Core.Tests Join suite.

internal sealed class SelSymInnerBook : ICacheEquatable<SelSymInnerBook>, ICacheClonable<SelSymInnerBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public string Country { get; init; } = "";

	public bool CacheEquals(SelSymInnerBook? other) => other is not null && other.Id == Id && other.Title == Title && other.Country == Country;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, Country);
	public SelSymInnerBook Clone() => new() { Id = Id, Title = Title, Country = Country };
}

internal sealed class SelSymInnerCountry : ICacheEquatable<SelSymInnerCountry>, ICacheClonable<SelSymInnerCountry> {
	public int Numeric { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(SelSymInnerCountry? other) => other is not null && other.Numeric == Numeric && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Numeric, Name);
	public SelSymInnerCountry Clone() => new() { Numeric = Numeric, Name = Name };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneLeftSymShapeASelectorInnerCoreTests {
	private InMemoryDataCache<int, SelSymInnerBook> _bookCache = null!;
	private InMemoryDataCache<int, SelSymInnerCountry> _countryCache = null!;
	private CacheSymmetricKeyValueListIndex<int, SelSymInnerBook, string> _countryIndex = null!;

	private static int MapCountry(string code) => code switch {
		"GB" => 44,
		"US" => 1,
		_ => 999, // unmapped — never resolves
	};

	private static int MapCountryWithArg(string code, int unmappedSentinel) => code switch {
		"GB" => 44,
		"US" => 1,
		_ => unmappedSentinel,
	};

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, SelSymInnerBook>();
		_countryCache = new InMemoryDataCache<int, SelSymInnerCountry>();
		_countryIndex = _bookCache.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);

		// Countries — only 44 (GB) and 1 (US) exist; 999 (the selector's unmapped sink) is intentionally absent.
		_countryCache.AddOrUpdate(44, new SelSymInnerCountry { Numeric = 44, Name = "United Kingdom" });
		_countryCache.AddOrUpdate(1,  new SelSymInnerCountry { Numeric = 1,  Name = "United States" });

		// Books — 1 and 3 share GB (fan-out group), 2 is US, 4 is XX (whole fan-out group drops on miss).
		_bookCache.AddOrUpdate(1, new SelSymInnerBook { Id = 1, Title = "Book GB 1", Country = "GB" });
		_bookCache.AddOrUpdate(2, new SelSymInnerBook { Id = 2, Title = "Book US",   Country = "US" });
		_bookCache.AddOrUpdate(3, new SelSymInnerBook { Id = 3, Title = "Book GB 2", Country = "GB" });
		_bookCache.AddOrUpdate(4, new SelSymInnerBook { Id = 4, Title = "Book XX",   Country = "XX" });
	}

	// ── S1: NoFilter + KeySelector ─────────────────────────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_Selector_NoFilter_DropsUnmapped() {
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, (string code) => MapCountry(code), _countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3), "Book 4 (XX) drops — selector maps to 999, not in country cache");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
		// Fan-out: books 1 and 3 both kept (both map via GB → 44).
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[3].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
	}

	// ── S2: NoFilter + KeySelectorWithArg ──────────────────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_SelectorArg_NoFilter_DropsUnmapped() {
		const int unmappedSentinel = 999;
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex,
				static (string code, int sentinel) => MapCountryWithArg(code, sentinel),
				unmappedSentinel,
				_countryCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3), "Book 4 (XX) drops — selector maps to 999");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── S3: JoinFilter + KeySelector ────────────────────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_Selector_WithFilter_DropsFilteredOut() {
		// Only allow Numeric == 1 (US). Books 1, 3 (GB → 44) get filter-rejected; book 4 (XX → 999) misses.
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, (string code) => MapCountry(code), _countryCache,
				q => q.Where(c => c.Numeric == 1))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1), "Only Book 2 (US) survives the filter");
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("United States"));
	}

	// ── S4: JoinFilter + KeySelectorWithArg ────────────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_SelectorArg_WithFilter_DropsFilteredOut() {
		const int unmappedSentinel = 999;
		// Filter keeps only GB (Numeric == 44). Books 1 and 3 survive (fan-out); book 2 and book 4 drop.
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex,
				static (string code, int sentinel) => MapCountryWithArg(code, sentinel),
				unmappedSentinel,
				_countryCache,
				q => q.Where(c => c.Numeric == 44))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 (GB) survive; book 2 (US) filter-rejected, book 4 (XX) miss");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Name == "United Kingdom"), Is.True);
	}

	// ── S5: JoinFilterWithArg + KeySelector ─────────────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_Selector_WithFilterArg_StaticLambda() {
		const int allowedNumeric = 1; // US
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, (string code) => MapCountry(code), _countryCache,
				static (q, n) => q.Where(c => c.Numeric == n),
				allowedNumeric)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Name, Is.EqualTo("United States"));
	}

	// ── S6: JoinFilterWithArg + KeySelectorWithArg ──────────────────────────

	[Test]
	public void InnerJoinOne_SymShapeA_SelectorArg_WithFilterArg_StaticLambdas() {
		const int unmappedSentinel = 999;
		const int allowedNumeric = 44; // GB
		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex,
				static (string code, int sentinel) => MapCountryWithArg(code, sentinel),
				unmappedSentinel,
				_countryCache,
				static (q, n) => q.Where(c => c.Numeric == n),
				allowedNumeric)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Books 1 and 3 (GB fan-out) survive");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Numeric == 44), Is.True);
	}
}

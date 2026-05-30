namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// LeftSym Shape B + key selector Inner variant tests. Outer Book carries an
// int CountryCode resolved via CacheSymmetricKeyValueListIndex<int, Book, int>;
// right Country PK is string, with a unique long secondary index. The selector
// maps int (TLookupKey from the sym list index) → long (TRightIndexKey on the
// right cache), exercising the cross-type Shape B + selector path.
//
// Names are scoped with the `SbS` prefix to avoid colliding with the other
// Core.Tests Join fixtures (which already define SymBook, SelSymBook, etc).

internal sealed class SbSBook : ICacheEquatable<SbSBook>, ICacheClonable<SbSBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public int CountryCode { get; init; }

	public bool CacheEquals(SbSBook? other) => other is not null && other.Id == Id && other.Title == Title && other.CountryCode == CountryCode;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, CountryCode);
	public SbSBook Clone() => new() { Id = Id, Title = Title, CountryCode = CountryCode };
}

internal sealed class SbSCountry : ICacheEquatable<SbSCountry>, ICacheClonable<SbSCountry> {
	public string Code { get; init; } = "";
	public string Name { get; init; } = "";
	public long Numeric { get; init; }

	public bool CacheEquals(SbSCountry? other) => other is not null && other.Code == Code && other.Name == Name && other.Numeric == Numeric;
	public int CacheGetHashCode() => HashCode.Combine(Code, Name, Numeric);
	public SbSCountry Clone() => new() { Code = Code, Name = Name, Numeric = Numeric };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneLeftSymShapeBSelectorInnerCoreTests {
	private InMemoryDataCache<int, SbSBook> _bookCache = null!;
	private InMemoryDataCache<string, SbSCountry> _countryCache = null!;
	private CacheSymmetricKeyValueListIndex<int, SbSBook, int> _bookCountryIndex = null!;
	private CacheUniqueIndex<string, SbSCountry, long> _countryNumericIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, SbSBook>();
		_countryCache = new InMemoryDataCache<string, SbSCountry>();
		_bookCountryIndex = _bookCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.CountryCode);
		_countryNumericIndex = _countryCache.AddKeyValueIndex<long>((_, v) => v.Numeric);

		// Countries — Numeric is the right-side unique long index.
		_countryCache.AddOrUpdate("GB", new SbSCountry { Code = "GB", Name = "United Kingdom", Numeric = 44L });
		_countryCache.AddOrUpdate("US", new SbSCountry { Code = "US", Name = "United States",  Numeric = 1L  });

		// Books — 1 and 3 share CountryCode=44 (GB → fan-out), 2 is US, 4 is 999 (no country).
		_bookCache.AddOrUpdate(1, new SbSBook { Id = 1, Title = "Book GB 1", CountryCode = 44  });
		_bookCache.AddOrUpdate(2, new SbSBook { Id = 2, Title = "Book US",   CountryCode = 1   });
		_bookCache.AddOrUpdate(3, new SbSBook { Id = 3, Title = "Book GB 2", CountryCode = 44  });
		_bookCache.AddOrUpdate(4, new SbSBook { Id = 4, Title = "Book ZZ",   CountryCode = 999 });
	}

	// ── Inner S1: selector, no filter ────────────────────────────────────────

	[Test]
	public void InnerJoinOne_ShapeB_Selector_NoFilter_DropsUnmatched() {
		// Selector: int → long (widen). Book 4 (CountryCode=999) has no Numeric=999 country → dropped.
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex, (int code) => (long)code, _countryCache, _countryNumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3), "Book 4 dropped (Numeric=999 unmapped)");
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
		// Fan-out preserved: both GB books survive and attach the same country.
		var gb = results.Where(r => r.Left.CountryCode == 44).ToList();
		Assert.That(gb.Count, Is.EqualTo(2));
		Assert.That(gb.All(r => r.Right!.Code == "GB"), Is.True);
	}

	// ── Inner S2: selector + selectorArg, no filter ──────────────────────────

	[Test]
	public void InnerJoinOne_ShapeB_SelectorWithArg_NoFilter_DropsUnmatched() {
		const long offset = 0L;
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex,
				static (int code, long off) => off + code,
				offset,
				_countryCache, _countryNumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── Inner S3: selector, with filter ──────────────────────────────────────

	[Test]
	public void InnerJoinOne_ShapeB_Selector_WithFilter_DropsFilteredOut() {
		// Filter to GB only — US and the unmatched ZZ should both be dropped.
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex, (int code) => (long)code, _countryCache, _countryNumericIndex,
				q => q.Where(c => c.Code == "GB"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Only the two GB books survive");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Code == "GB"), Is.True);
	}

	// ── Inner S4: selector + selectorArg, with filter ───────────────────────

	[Test]
	public void InnerJoinOne_ShapeB_SelectorWithArg_WithFilter_DropsFilteredOut() {
		const long offset = 0L;
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex,
				static (int code, long off) => off + code,
				offset,
				_countryCache, _countryNumericIndex,
				q => q.Where(c => c.Code == "US"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1), "Only Book 2 (US) survives the filter");
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Code, Is.EqualTo("US"));
	}

	// ── Inner S5: selector, with filter + filterArg ─────────────────────────

	[Test]
	public void InnerJoinOne_ShapeB_Selector_WithFilterArg_DropsFilteredOut() {
		const string allowedCode = "US";
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex, (int code) => (long)code, _countryCache, _countryNumericIndex,
				static (q, code) => q.Where(c => c.Code == code), allowedCode)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right!.Code, Is.EqualTo("US"));
	}

	// ── Inner S6: selector + selectorArg, with filter + filterArg ───────────

	[Test]
	public void InnerJoinOne_ShapeB_SelectorWithArg_WithFilterArg_DropsFilteredOut() {
		const long offset = 0L;
		const string allowedCode = "GB";
		var results = _bookCache.Query()
			.InnerJoinOne(_bookCountryIndex,
				static (int code, long off) => off + code,
				offset,
				_countryCache, _countryNumericIndex,
				static (q, code) => q.Where(c => c.Code == code),
				allowedCode)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "Only GB books survive — fan-out preserved across both");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right!.Code == "GB"), Is.True);
	}
}

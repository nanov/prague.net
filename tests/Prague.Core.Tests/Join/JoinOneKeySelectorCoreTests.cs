namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// One test per JoinOne shape — each demonstrates the keySelector path with
// hand-rolled POCOs and manually-constructed indexes. POCO names are scoped
// to this file (Sel*) to avoid collision with the other Core.Tests Join files.

internal sealed class SelLeft : ICacheEquatable<SelLeft>, ICacheClonable<SelLeft> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(SelLeft? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public SelLeft Clone() => new() { Id = Id, Name = Name };
}

internal sealed class SelRight : ICacheEquatable<SelRight>, ICacheClonable<SelRight> {
	public long Id { get; init; }
	public string Label { get; init; } = "";

	public bool CacheEquals(SelRight? other) => other is not null && other.Id == Id && other.Label == Label;
	public int CacheGetHashCode() => HashCode.Combine(Id, Label);
	public SelRight Clone() => new() { Id = Id, Label = Label };
}

internal sealed class SelSymBook : ICacheEquatable<SelSymBook>, ICacheClonable<SelSymBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public int CountryCode { get; init; }

	public bool CacheEquals(SelSymBook? other) => other is not null && other.Id == Id && other.Title == Title && other.CountryCode == CountryCode;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, CountryCode);
	public SelSymBook Clone() => new() { Id = Id, Title = Title, CountryCode = CountryCode };
}

internal sealed class SelSymCountry : ICacheEquatable<SelSymCountry>, ICacheClonable<SelSymCountry> {
	public string Code { get; init; } = "";
	public string Name { get; init; } = "";
	public int Numeric { get; init; }

	public bool CacheEquals(SelSymCountry? other) => other is not null && other.Code == Code && other.Name == Name && other.Numeric == Numeric;
	public int CacheGetHashCode() => HashCode.Combine(Code, Name, Numeric);
	public SelSymCountry Clone() => new() { Code = Code, Name = Name, Numeric = Numeric };
}

internal sealed class SelRuBook : ICacheEquatable<SelRuBook>, ICacheClonable<SelRuBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";

	public bool CacheEquals(SelRuBook? other) => other is not null && other.Id == Id && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public SelRuBook Clone() => new() { Id = Id, Title = Title };
}

internal sealed class SelRuBookInfo : ICacheEquatable<SelRuBookInfo>, ICacheClonable<SelRuBookInfo> {
	public int Id { get; init; }
	public long BookKey { get; init; }
	public string Synopsis { get; init; } = "";

	public bool CacheEquals(SelRuBookInfo? other) => other is not null && other.Id == Id && other.BookKey == BookKey && other.Synopsis == Synopsis;
	public int CacheGetHashCode() => HashCode.Combine(Id, BookKey, Synopsis);
	public SelRuBookInfo Clone() => new() { Id = Id, BookKey = BookKey, Synopsis = Synopsis };
}

internal sealed class SelLuAuthor : ICacheEquatable<SelLuAuthor>, ICacheClonable<SelLuAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public long BookKey { get; init; }

	public bool CacheEquals(SelLuAuthor? other) => other is not null && other.Id == Id && other.Name == Name && other.BookKey == BookKey;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, BookKey);
	public SelLuAuthor Clone() => new() { Id = Id, Name = Name, BookKey = BookKey };
}

internal sealed class SelLuBook : ICacheEquatable<SelLuBook>, ICacheClonable<SelLuBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";

	public bool CacheEquals(SelLuBook? other) => other is not null && other.Id == Id && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title);
	public SelLuBook Clone() => new() { Id = Id, Title = Title };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneKeySelectorCoreTests {

	// ── PK-to-PK + selector (int → long) ─────────────────────────────────────

	[Test]
	public void JoinOne_PkToPk_WithSelector_MapsIntToLong() {
		var leftCache = new InMemoryDataCache<int, SelLeft>();
		var rightCache = new InMemoryDataCache<long, SelRight>();

		leftCache.AddOrUpdate(1, new SelLeft { Id = 1, Name = "L1" });
		leftCache.AddOrUpdate(2, new SelLeft { Id = 2, Name = "L2" });
		leftCache.AddOrUpdate(3, new SelLeft { Id = 3, Name = "L3" });
		rightCache.AddOrUpdate(1001L, new SelRight { Id = 1001L, Label = "R1" });
		rightCache.AddOrUpdate(1002L, new SelRight { Id = 1002L, Label = "R2" });
		// no 1003

		var results = leftCache.Query()
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

	// ── Sym-index Shape A + selector (int → string) ──────────────────────────

	[Test]
	public void JoinOne_SymShapeA_WithSelector_MapsIntToString() {
		var bookCache = new InMemoryDataCache<int, SelSymBook>();
		var countryCache = new InMemoryDataCache<string, SelSymCountry>();
		var bookCountryIndex = bookCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.CountryCode);

		countryCache.AddOrUpdate("GB", new SelSymCountry { Code = "GB", Name = "United Kingdom", Numeric = 44 });
		countryCache.AddOrUpdate("US", new SelSymCountry { Code = "US", Name = "United States",  Numeric = 1 });

		bookCache.AddOrUpdate(1, new SelSymBook { Id = 1, Title = "Book GB", CountryCode = 44 });
		bookCache.AddOrUpdate(2, new SelSymBook { Id = 2, Title = "Book US", CountryCode = 1 });
		bookCache.AddOrUpdate(3, new SelSymBook { Id = 3, Title = "Book ZZ", CountryCode = 999 });

		var results = bookCache.Query()
			.JoinOne(bookCountryIndex, (int code) => code switch {
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

	// ── Sym-index Shape B + selector (int → int via identity, rightIndex translates int → string PK) ──

	[Test]
	public void JoinOne_SymShapeB_WithSelector_MapsToRightIndexKey() {
		var bookCache = new InMemoryDataCache<int, SelSymBook>();
		var countryCache = new InMemoryDataCache<string, SelSymCountry>();
		var bookCountryIndex = bookCache.CacheSymmetricKeyValueListIndex<int>((_, v) => v.CountryCode);
		var countryNumericIndex = countryCache.AddKeyValueIndex<int>((_, v) => v.Numeric);

		countryCache.AddOrUpdate("GB", new SelSymCountry { Code = "GB", Name = "United Kingdom", Numeric = 44 });
		countryCache.AddOrUpdate("US", new SelSymCountry { Code = "US", Name = "United States",  Numeric = 1 });

		bookCache.AddOrUpdate(1, new SelSymBook { Id = 1, Title = "Book GB", CountryCode = 44 });
		bookCache.AddOrUpdate(2, new SelSymBook { Id = 2, Title = "Book US", CountryCode = 1 });
		bookCache.AddOrUpdate(3, new SelSymBook { Id = 3, Title = "Book ZZ", CountryCode = 999 });

		var results = bookCache.Query()
			.JoinOne(bookCountryIndex, (int code) => code, countryCache, countryNumericIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("United Kingdom"));
		Assert.That(byId[2].Right!.Name, Is.EqualTo("United States"));
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Right-unique-index + selector (int → long) ───────────────────────────

	[Test]
	public void JoinOne_RightUniqueIndex_WithSelector_MapsIntToLong() {
		var bookCache = new InMemoryDataCache<int, SelRuBook>();
		var infoCache = new InMemoryDataCache<int, SelRuBookInfo>();
		// BookKey is long — unique index keyed by long (which is the selector's output type).
		var bookKeyIndex = infoCache.AddKeyValueIndex<long>((_, v) => v.BookKey);

		bookCache.AddOrUpdate(1, new SelRuBook { Id = 1, Title = "Book One" });
		bookCache.AddOrUpdate(2, new SelRuBook { Id = 2, Title = "Book Two" });
		bookCache.AddOrUpdate(3, new SelRuBook { Id = 3, Title = "Book Three" });

		infoCache.AddOrUpdate(101, new SelRuBookInfo { Id = 101, BookKey = 5001L, Synopsis = "S1" });
		infoCache.AddOrUpdate(102, new SelRuBookInfo { Id = 102, BookKey = 5002L, Synopsis = "S2" });
		// no info for book 3

		var results = bookCache.Query()
			.JoinOne((int id) => 5000L + id, infoCache, bookKeyIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Synopsis, Is.EqualTo("S1"));
		Assert.That(byId[2].Right!.Synopsis, Is.EqualTo("S2"));
		Assert.That(byId[3].Right, Is.Null);
	}

	// ── Left-unique-index + selector (long → int) ────────────────────────────

	[Test]
	public void JoinOne_LeftUniqueIndex_WithSelector_MapsLongToInt() {
		var authorCache = new InMemoryDataCache<int, SelLuAuthor>();
		var bookCache = new InMemoryDataCache<int, SelLuBook>();
		// Symmetric so .Reverse is available; index value is long (BookKey).
		var authorBookKeyIndex = authorCache.AddSymmetricKeyValueIndex<long>((_, v) => v.BookKey);

		bookCache.AddOrUpdate(1, new SelLuBook { Id = 1, Title = "Book A" });
		bookCache.AddOrUpdate(2, new SelLuBook { Id = 2, Title = "Book B" });
		// no book Id = 3

		authorCache.AddOrUpdate(10, new SelLuAuthor { Id = 10, Name = "Alice", BookKey = 7001L });
		authorCache.AddOrUpdate(20, new SelLuAuthor { Id = 20, Name = "Bob",   BookKey = 7002L });
		authorCache.AddOrUpdate(30, new SelLuAuthor { Id = 30, Name = "Carol", BookKey = 7003L });

		var results = authorCache.Query()
			.JoinOne(authorBookKeyIndex, (long k) => (int)(k - 7000L), bookCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[10].Right!.Title, Is.EqualTo("Book A"));
		Assert.That(byId[20].Right!.Title, Is.EqualTo("Book B"));
		Assert.That(byId[30].Right, Is.Null);
	}
}

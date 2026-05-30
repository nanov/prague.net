namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain: outer Book cache with TWO symmetric list indexes (fan-out shape):
//   Book ←(Country index, multi-book per country)─ Country
//   Book ←(Genre   index, multi-book per genre  )─ Genre
//
// Chained shape: bookCache.Query()
//                  .InnerJoinOne(countryIndex, countryCache)
//                  .InnerJoinOne(genreIndex,   genreCache)
//                  .Execute();
//
// Both hops are LeftSym Shape A (no rightIndex). First hop level-0 (hand-written),
// second hop level-1 (T4-emitted minimal LeftSym Inner). Validates the
// `accessor.RetainNonNullSlots<TRightValue>(ref candidates)` fix end-to-end for the
// fan-out resolver — without it, stale slots from the first hop (books that didn't
// match THIS resolver) would surface in the result with Left=null.

internal sealed class LsxBook : ICacheEquatable<LsxBook>, ICacheClonable<LsxBook> {
	public int Id { get; init; }
	public string Title { get; init; } = "";
	public string Country { get; init; } = "";
	public string Genre { get; init; } = "";
	public bool CacheEquals(LsxBook? o) => o is not null && o.Id == Id && o.Title == Title && o.Country == Country && o.Genre == Genre;
	public int CacheGetHashCode() => HashCode.Combine(Id, Title, Country, Genre);
	public LsxBook Clone() => new() { Id = Id, Title = Title, Country = Country, Genre = Genre };
}

internal sealed class LsxCountry : ICacheEquatable<LsxCountry>, ICacheClonable<LsxCountry> {
	public string Code { get; init; } = "";
	public string Name { get; init; } = "";
	public bool CacheEquals(LsxCountry? o) => o is not null && o.Code == Code && o.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Code, Name);
	public LsxCountry Clone() => new() { Code = Code, Name = Name };
}

internal sealed class LsxGenre : ICacheEquatable<LsxGenre>, ICacheClonable<LsxGenre> {
	public string Code { get; init; } = "";
	public string Description { get; init; } = "";
	public bool CacheEquals(LsxGenre? o) => o is not null && o.Code == Code && o.Description == Description;
	public int CacheGetHashCode() => HashCode.Combine(Code, Description);
	public LsxGenre Clone() => new() { Code = Code, Description = Description };
}

[TestFixture]
public class JoinOneLeftSymChainedCoreTests {
	private InMemoryDataCache<int, LsxBook> _bookCache = null!;
	private InMemoryDataCache<string, LsxCountry> _countryCache = null!;
	private InMemoryDataCache<string, LsxGenre> _genreCache = null!;
	private CacheSymmetricKeyValueListIndex<int, LsxBook, string> _countryIndex = null!;
	private CacheSymmetricKeyValueListIndex<int, LsxBook, string> _genreIndex = null!;

	[SetUp]
	public void SetUp() {
		_bookCache = new InMemoryDataCache<int, LsxBook>();
		_countryCache = new InMemoryDataCache<string, LsxCountry>();
		_genreCache = new InMemoryDataCache<string, LsxGenre>();
		_countryIndex = _bookCache.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_genreIndex = _bookCache.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Genre);
	}

	private void SeedFullChain() {
		_countryCache.AddOrUpdate("GB", new LsxCountry { Code = "GB", Name = "UK" });
		_countryCache.AddOrUpdate("US", new LsxCountry { Code = "US", Name = "USA" });
		_countryCache.AddOrUpdate("FR", new LsxCountry { Code = "FR", Name = "France" });

		_genreCache.AddOrUpdate("FIC", new LsxGenre { Code = "FIC", Description = "Fiction" });
		_genreCache.AddOrUpdate("HIS", new LsxGenre { Code = "HIS", Description = "History" });

		// Multiple books share a country/genre to exercise FAN-OUT in both hops.
		_bookCache.AddOrUpdate(1, new LsxBook { Id = 1, Title = "B1", Country = "GB", Genre = "FIC" });
		_bookCache.AddOrUpdate(2, new LsxBook { Id = 2, Title = "B2", Country = "GB", Genre = "HIS" });
		_bookCache.AddOrUpdate(3, new LsxBook { Id = 3, Title = "B3", Country = "US", Genre = "FIC" });
		_bookCache.AddOrUpdate(4, new LsxBook { Id = 4, Title = "B4", Country = "FR", Genre = "FIC" });
	}

	[Test]
	public void Chained_AllMatched() {
		SeedFullChain();

		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(4));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("UK"));
		Assert.That(byId[1].Right2!.Description, Is.EqualTo("Fiction"));
		Assert.That(byId[2].Right2!.Description, Is.EqualTo("History"));
		Assert.That(byId[3].Right2!.Description, Is.EqualTo("Fiction"));
		Assert.That(byId[4].Right2!.Description, Is.EqualTo("Fiction"));
	}

	[Test]
	public void Chained_SecondHopMisses_FanOutLeftsDropped_ViaRetainNonNullSlots() {
		// Remove the History genre. Book 2 (Genre=HIS) should be dropped at the
		// second hop. The first hop matched it (Country=GB), creating a slot in
		// _results. After the second hop's fan-out, slot's Right2 stays null → the
		// RetainNonNullSlots filter drops the stale slot, narrows candidates.
		// This is the chained-inner LeftSym bug-fix scenario.
		SeedFullChain();
		_genreCache.Remove("HIS");

		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3, 4 }));
		Assert.That(results.All(r => r.Right is not null && r.Right2 is not null), Is.True);
	}

	[Test]
	public void Chained_FirstHopMisses_FanOutLeftsDropped() {
		// Remove France. Book 4 (Country=FR) dropped at first hop → never reaches second.
		// Also remove History — Book 2 should still be dropped at second hop.
		SeedFullChain();
		_countryCache.Remove("FR");
		_genreCache.Remove("HIS");

		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_OuterThenInner_OuterKeepsNullFirstHop_InnerDropsSecondHop() {
		// First hop OUTER: book 4 (Country=FR, FR missing) kept with Right=null.
		// Second hop INNER: book 4 → no genre match for FR? Book 4 has Genre=FIC which exists.
		//   So book 4 should be kept with Right=null, Right2=Fiction.
		// Book 2 (Genre=HIS, HIS missing): INNER drops.
		SeedFullChain();
		_countryCache.Remove("FR");
		_genreCache.Remove("HIS");

		var results = _bookCache.Query()
			.JoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("UK"));
		Assert.That(byId[3].Right!.Name, Is.EqualTo("USA"));
		Assert.That(byId[4].Right, Is.Null);
		Assert.That(byId[4].Right2!.Description, Is.EqualTo("Fiction"));
	}

	[Test]
	public void Chained_SecondHopWithFilter_RejectsViaPredicate() {
		// Second-hop genre filter rejects Description=="Fiction" → drops books 1, 3, 4.
		// Only book 2 (Genre=HIS=History) survives.
		// Validates the Inner-A2 chained emission (LeftSym Shape A inner with filter).
		SeedFullChain();

		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache,
				static q => q.Where(g => g.Description != "Fiction"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
		Assert.That(results[0].Right2!.Description, Is.EqualTo("History"));
	}

	[Test]
	public void Chained_SecondHopWithFilterArg_StaticLambdaZeroAlloc() {
		// Filter+arg variant (Inner-A3 chained emission).
		SeedFullChain();
		const string rejectDescription = "Fiction";

		var results = _bookCache.Query()
			.InnerJoinOne(_countryIndex, _countryCache)
			.InnerJoinOne(_genreIndex, _genreCache,
				static (q, reject) => q.Where(g => g.Description != reject),
				rejectDescription)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(2));
	}
}

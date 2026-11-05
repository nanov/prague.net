namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   Eager <c>JoinOne</c> across the four resolver families and six left sizes, written for the change that
///   (1) fills an unfiltered OUTER <c>JoinOne</c> with one point lookup per row instead of seeding a pair set and
///   running a paired read — for the left-symmetric family that retires <c>OuterFanOutContainer</c>, which walked
///   the whole index bucket of every right the page reached — (2) seeds the pair set of the filtered outer, inner
///   and bounded narrow-only inner paths through <c>JoinOneSharedRights</c>, which chains every joined key whose
///   right is already paired to that pair's slot and delivers the right to all of them, instead of the bulk
///   <c>IntersectValues</c> primitives, (3) drops the unmatched rows of an inner join with a STRUCT right by
///   key, where the null-slot post-walk kept them, (4) fills the rows of a FILTERED outer left-symmetric join by
///   pair slot instead of walking every left of each right's bucket, and (5) lets the first inner join of a chain
///   only narrow the candidates to its row keys instead of filtering the rows it just made. Each scenario is its
///   own category with one method, so a run reads as a size series; the A/B is this class on the old build
///   against this class on the new one.
///   <list type="bullet">
///     <item><c>JoinOne_LeftSym_Page</c> — <c>SortBounded</c> page of 200 over every fixture → left-symmetric join to the sport type (30 values, the top one ~40% of the rows).</item>
///     <item><c>JoinOne_LeftSym_Page_Three</c> — the same page → sport type, country (900) and league (2 700), three left-symmetric joins.</item>
///     <item><c>JoinOne_LeftSym_Full</c> — no page: one bucket (N / 5 fixtures) → left-symmetric join to the sport type over all of them.</item>
///     <item><c>JoinOne_LeftSym_Filtered_Page</c> / <c>JoinOne_LeftSym_IndexFiltered_Page</c> — the <c>JoinOne_LeftSym_Page</c> page with a filter callback: a value predicate (<c>Where</c>, half the sport types pass) / an index narrowing (<c>UseIndex</c> on a flag index over the sport types, registered before their rows — a list index registered on a filled cache starts empty).</item>
///     <item><c>JoinOne_PkIdentity_Full</c> / <c>JoinOne_PkIdentity_Page</c> — outer unfiltered PK-to-PK identity join to a detail row (nine fixtures in ten), unpaged over a bucket / paged over every fixture.</item>
///     <item><c>InnerJoinOne_Selector_Page</c> — the production reader shape: one channel of a <c>(long EventId, JosChannel Channel)</c> key (N / 5 odds, injective) → <c>SortBounded</c> page of 200 → <c>InnerJoinOne(k => k.EventId, games)</c> → <c>JoinOne(k => k.EventId, live)</c> → <c>JoinMany(k => k.EventId, markets)</c>.</item>
///     <item><c>InnerJoinOne_Selector_Full</c> — the same channel, unpaged, the inner selector join alone: the classic indexed-inner pair seeding over every candidate.</item>
///     <item><c>JoinOne_Selector_Filtered</c> — the same channel, an outer selector join with a <c>Where</c> filter callback (two games in three pass), injective.</item>
///     <item><c>LeftUnique_Inner</c> / <c>LeftUnique_Filtered</c> — one bucket of fixtures → forward one-to-one through a symmetric unique left index, inner / outer with a <c>Where</c> filter, injective.</item>
///     <item><c>SharedRight_Selector_Inner</c> / <c>SharedRight_Selector_Filtered</c> — one event group across all five channels (N / 5 odds, five per event): a NON-injective selector, inner / outer filtered. The old build drops the shared lefts (inner) or leaves them without a right (outer).</item>
///     <item><c>LeftSym_SharedRight_ManyBuckets_Outer</c> / <c>_Inner</c> — one bucket of fixtures → left-symmetric join through the league index with a selector folding a hundred leagues onto one region (27 regions): filtered outer (<c>Where(_ => true)</c>, so it takes the pair-set path) / inner. The old build gives the region to one league bucket per region only.</item>
///     <item><c>StructRight_ChainedInner</c> — one bucket of fixtures → <c>InnerJoinOne</c> to a reference right every fixture has → <c>InnerJoinOne</c> to a STRUCT right present for half of them, both PK-to-PK identity. The old build keeps every row of the first join.</item>
///   </list>
///   Data: N fixtures, sport type / country / league drawn Zipf(1.4 / 1.0 / 1.0) over 30 / 900 / 2 700 values,
///   every value present on the right; a PK-to-PK detail and a stats row (reached through the symmetric unique
///   index) for nine fixtures in ten; the unpaged fixture scenarios read one bucket of five. N odds: N / 5 events,
///   each in all five channels; a game for nine events in ten (two in three active), live info for one in three,
///   two markets for three in four. Every body executes pooled and disposes. The setups are split by target, so a
///   case builds only the data its scenario reads. Rows the two builds disagree on are the bug the change fixes,
///   not noise: compare those scenarios' times knowing the old build returns less.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class JoinOneSharedRightBenchmarks {
	private const int Page = 200;
	private const int SportTypeCount = 30;
	private const int CountryCount = 900;
	private const int LeagueCount = 2_700;
	private const int Buckets = 5;
	private const int ChannelCount = 5;
	private const int StatsOffset = 10_000_000;
	private const int LeagueFold = 100;
	private const long EventBase = 5_000_000_000L;

	[Params(100, 1_000, 5_000, 7_000, 10_000, 100_000)]
	public int N { get; set; }

	private InMemoryDataCache<int, JosFixture> _fixtures = null!;
	private CacheKeyValueListIndex<int, JosFixture, int> _fixtureByBucket = null!;
	private CacheSymmetricKeyValueListIndex<int, JosFixture, int> _bySportType = null!;
	private CacheSymmetricKeyValueListIndex<int, JosFixture, int> _byCountry = null!;
	private CacheSymmetricKeyValueListIndex<int, JosFixture, int> _byLeague = null!;
	private CacheSymmetricUniqueIndex<int, JosFixture, int> _byStats = null!;
	private InMemoryDataCache<int, JosRef> _sportTypes = null!;
	private CacheKeyValueListIndex<int, JosRef, bool> _sportTypeByFlag = null!;
	private InMemoryDataCache<int, JosRef> _countries = null!;
	private InMemoryDataCache<int, JosRef> _leagues = null!;
	private InMemoryDataCache<int, JosRef> _details = null!;
	private InMemoryDataCache<int, JosRef> _stats = null!;
	private InMemoryDataCache<int, JosRef> _regions = null!;
	private InMemoryDataCache<int, JosRef> _owners = null!;
	private InMemoryDataCache<int, JosStructRight> _structRights = null!;

	private InMemoryDataCache<(long EventId, JosChannel Channel), JosOdds> _odds = null!;
	private CacheKeyValueListIndex<(long EventId, JosChannel Channel), JosOdds, int> _oddsByChannel = null!;
	private CacheKeyValueListIndex<(long EventId, JosChannel Channel), JosOdds, int> _oddsByGroup = null!;
	private InMemoryDataCache<long, JosGame> _games = null!;
	private InMemoryDataCache<long, JosGame> _live = null!;
	private InMemoryDataCache<long, JosMarket> _markets = null!;
	private CacheKeyValueListIndex<long, JosMarket, long> _marketByEvent = null!;

	// Arguments are fields, not constants, so no side gets a constant folded into the query.
	private int _bucket = 3;
	private int _channel = (int)JosChannel.Mobile;
	private int _group = 3;

	[GlobalSetup(Targets = new[] {
		nameof(JoinOne_LeftSym_Page), nameof(JoinOne_LeftSym_Page_Three), nameof(JoinOne_LeftSym_Full),
		nameof(JoinOne_PkIdentity_Full), nameof(JoinOne_PkIdentity_Page), nameof(LeftUnique_Inner), nameof(LeftUnique_Filtered),
		nameof(LeftSym_SharedRight_ManyBuckets_Outer), nameof(LeftSym_SharedRight_ManyBuckets_Inner),
		nameof(JoinOne_LeftSym_Filtered_Page), nameof(JoinOne_LeftSym_IndexFiltered_Page),
	})]
	public void SetupFixtures() {
		_fixtures = new InMemoryDataCache<int, JosFixture>();
		_fixtureByBucket = _fixtures.CacheKeyValueListIndex<int>(static (_, v) => v.Bucket);
		_bySportType = _fixtures.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.SportTypeId);
		_byCountry = _fixtures.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CountryId);
		_byLeague = _fixtures.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.LeagueId);
		_byStats = _fixtures.AddSymmetricKeyValueIndex<int>(static (_, v) => v.StatsId);
		// The index first: a list index registered on a filled cache starts empty (no backfill).
		_sportTypes = new InMemoryDataCache<int, JosRef>();
		_sportTypeByFlag = _sportTypes.CacheKeyValueListIndex<bool>(static (_, v) => v.Flag);
		Fill(_sportTypes, SportTypeCount);
		_countries = Dimension(CountryCount);
		_leagues = Dimension(LeagueCount);
		_regions = Dimension(LeagueCount / LeagueFold);
		_details = new InMemoryDataCache<int, JosRef>();
		_stats = new InMemoryDataCache<int, JosRef>();

		var sportCdf = ZipfCdf(SportTypeCount, 1.4);
		var countryCdf = ZipfCdf(CountryCount, 1.0);
		var leagueCdf = ZipfCdf(LeagueCount, 1.0);
		var rng = new Random(1234);
		for (var i = 0; i < N; i++) {
			_fixtures.AddOrUpdate(i, new JosFixture {
				Id = i, Bucket = i % Buckets, SportTypeId = Draw(sportCdf, rng), CountryId = Draw(countryCdf, rng), LeagueId = Draw(leagueCdf, rng),
				StatsId = StatsOffset + i, Score = rng.Next(int.MaxValue),
			});
			// Nine in ten: i / Buckets keeps the misses inside every bucket instead of in one of them.
			if (i / Buckets % 10 == 9)
				continue;
			_details.AddOrUpdate(i, new JosRef { Id = i, Name = "d", Flag = i % 2 == 0 });
			_stats.AddOrUpdate(StatsOffset + i, new JosRef { Id = StatsOffset + i, Name = "s", Flag = i / Buckets % 3 != 0 });
		}
	}

	[GlobalSetup(Targets = new[] { nameof(StructRight_ChainedInner) })]
	public void SetupStructRight() {
		_fixtures = new InMemoryDataCache<int, JosFixture>();
		_fixtureByBucket = _fixtures.CacheKeyValueListIndex<int>(static (_, v) => v.Bucket);
		_owners = new InMemoryDataCache<int, JosRef>();
		_structRights = new InMemoryDataCache<int, JosStructRight>();
		var rng = new Random(1234);
		for (var i = 0; i < N; i++) {
			_fixtures.AddOrUpdate(i, new JosFixture { Id = i, Bucket = i % Buckets, StatsId = StatsOffset + i, Score = rng.Next(int.MaxValue) });
			_owners.AddOrUpdate(i, new JosRef { Id = i, Name = "o", Flag = true });
			// Half, inside every bucket. Value is never 0, so a default struct reads as a miss.
			if (i / Buckets % 2 == 0)
				_structRights.AddOrUpdate(i, new JosStructRight { Id = i, Value = 1 + i % 97 });
		}
	}

	[GlobalSetup(Targets = new[] {
		nameof(InnerJoinOne_Selector_Page), nameof(InnerJoinOne_Selector_Full), nameof(JoinOne_Selector_Filtered),
		nameof(SharedRight_Selector_Inner), nameof(SharedRight_Selector_Filtered),
	})]
	public void SetupOdds() {
		_odds = new InMemoryDataCache<(long EventId, JosChannel Channel), JosOdds>();
		_oddsByChannel = _odds.CacheKeyValueListIndex<int>(static (_, v) => (int)v.Channel);
		_oddsByGroup = _odds.CacheKeyValueListIndex<int>(static (_, v) => (int)(v.EventId % ChannelCount));
		_games = new InMemoryDataCache<long, JosGame>();
		_live = new InMemoryDataCache<long, JosGame>();
		_markets = new InMemoryDataCache<long, JosMarket>();
		_marketByEvent = _markets.CacheKeyValueListIndex<long>(static (_, v) => v.EventId);

		var rng = new Random(1234);
		var events = N / ChannelCount;
		for (var e = 0; e < events; e++) {
			var eventId = EventBase + e;
			for (var c = 0; c < ChannelCount; c++)
				_odds.AddOrUpdate((eventId, (JosChannel)c), new JosOdds { EventId = eventId, Channel = (JosChannel)c, Score = rng.Next(int.MaxValue) });
			// Keyed off e / ChannelCount: the group narrowing reads every fifth event, so a property keyed off e
			// directly would be constant inside the group.
			var k = e / ChannelCount;
			if (k % 10 != 9)
				_games.AddOrUpdate(eventId, new JosGame { Id = eventId, Active = k % 3 != 0 });
			if (k % 3 == 1)
				_live.AddOrUpdate(eventId, new JosGame { Id = eventId, Active = true });
			if (e % 4 == 0)
				continue;
			_markets.AddOrUpdate(2 * eventId, new JosMarket { Id = 2 * eventId, EventId = eventId });
			_markets.AddOrUpdate(2 * eventId + 1, new JosMarket { Id = 2 * eventId + 1, EventId = eventId });
		}
	}

	// ── Left-symmetric, outer unfiltered: the per-row fill replaces the whole-bucket fan-out ────────────

	[BenchmarkCategory("JoinOne_LeftSym_Page"), Benchmark]
	public int JoinOne_LeftSym_Page() {
		using var r = _fixtures.Query().SortBounded(new JosFixtureByScore()).JoinOne(_bySportType, _sportTypes).ExecutePooled(0, Page);
		return r.Count;
	}

	[BenchmarkCategory("JoinOne_LeftSym_Page_Three"), Benchmark]
	public int JoinOne_LeftSym_Page_Three() {
		using var r = _fixtures.Query().SortBounded(new JosFixtureByScore())
			.JoinOne(_bySportType, _sportTypes).JoinOne(_byCountry, _countries).JoinOne(_byLeague, _leagues).ExecutePooled(0, Page);
		return r.Count;
	}

	[BenchmarkCategory("JoinOne_LeftSym_Full"), Benchmark]
	public int JoinOne_LeftSym_Full() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).JoinOne(_bySportType, _sportTypes).ExecutePooled();
		return r.Count;
	}

	// ── Left-symmetric, outer FILTERED on a page: the page rows by pair slot replace the whole-bucket fan-out ──

	[BenchmarkCategory("JoinOne_LeftSym_Filtered_Page"), Benchmark]
	public int JoinOne_LeftSym_Filtered_Page() {
		using var r = _fixtures.Query().SortBounded(new JosFixtureByScore())
			.JoinOne(_bySportType, _sportTypes, static q => q.Where(static s => s.Flag)).ExecutePooled(0, Page);
		return r.Count;
	}

	[BenchmarkCategory("JoinOne_LeftSym_IndexFiltered_Page"), Benchmark]
	public int JoinOne_LeftSym_IndexFiltered_Page() {
		using var r = _fixtures.Query().SortBounded(new JosFixtureByScore())
			.JoinOne(_bySportType, _sportTypes, static (q, byFlag) => q.UseIndex(byFlag, true), _sportTypeByFlag).ExecutePooled(0, Page);
		return r.Count;
	}

	// ── PK-to-PK identity, outer unfiltered ──────────────────────────────────────────────────────────────

	[BenchmarkCategory("JoinOne_PkIdentity_Full"), Benchmark]
	public int JoinOne_PkIdentity_Full() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).JoinOne(_details).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOne_PkIdentity_Page"), Benchmark]
	public int JoinOne_PkIdentity_Page() {
		using var r = _fixtures.Query().SortBounded(new JosFixtureByScore()).JoinOne(_details).ExecutePooled(0, Page);
		return r.Count;
	}

	// ── PK-to-PK with a key selector over a tuple key, injective (one channel) ──────────────────────────

	[BenchmarkCategory("InnerJoinOne_Selector_Page"), Benchmark]
	public int InnerJoinOne_Selector_Page() {
		using var r = _odds.Query().UseIndex(_oddsByChannel, _channel).SortBounded(new JosOddsByScore())
			.InnerJoinOne(static k => k.EventId, _games).JoinOne(static k => k.EventId, _live).JoinMany(static k => k.EventId, _markets, _marketByEvent)
			.ExecutePooled(0, Page);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinOne_Selector_Full"), Benchmark]
	public int InnerJoinOne_Selector_Full() {
		using var r = _odds.Query().UseIndex(_oddsByChannel, _channel).InnerJoinOne(static k => k.EventId, _games).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOne_Selector_Filtered"), Benchmark]
	public int JoinOne_Selector_Filtered() {
		using var r = _odds.Query().UseIndex(_oddsByChannel, _channel).JoinOne(static k => k.EventId, _games, static q => q.Where(static g => g.Active)).ExecutePooled();
		return r.Count;
	}

	// ── Left-unique (symmetric unique left index), injective ─────────────────────────────────────────────

	[BenchmarkCategory("LeftUnique_Inner"), Benchmark]
	public int LeftUnique_Inner() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).InnerJoinOne(_byStats, _stats).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("LeftUnique_Filtered"), Benchmark]
	public int LeftUnique_Filtered() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).JoinOne(_byStats, _stats, static q => q.Where(static s => s.Flag)).ExecutePooled();
		return r.Count;
	}

	// ── Non-injective selector: five channels share one event's game ────────────────────────────────────

	[BenchmarkCategory("SharedRight_Selector_Inner"), Benchmark]
	public int SharedRight_Selector_Inner() {
		using var r = _odds.Query().UseIndex(_oddsByGroup, _group).InnerJoinOne(static k => k.EventId, _games).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("SharedRight_Selector_Filtered"), Benchmark]
	public int SharedRight_Selector_Filtered() {
		using var r = _odds.Query().UseIndex(_oddsByGroup, _group).JoinOne(static k => k.EventId, _games, static q => q.Where(static g => g.Active)).ExecutePooled();
		return r.Count;
	}

	// ── Left-symmetric with a non-injective selector: a hundred league buckets share one region ─────────

	[BenchmarkCategory("LeftSym_SharedRight_ManyBuckets_Outer"), Benchmark]
	public int LeftSym_SharedRight_ManyBuckets_Outer() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket)
			.JoinOne(_byLeague, static (int l) => l / LeagueFold, _regions, static q => q.Where(static _ => true)).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("LeftSym_SharedRight_ManyBuckets_Inner"), Benchmark]
	public int LeftSym_SharedRight_ManyBuckets_Inner() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).InnerJoinOne(_byLeague, static (int l) => l / LeagueFold, _regions).ExecutePooled();
		return r.Count;
	}

	// ── Chained inner joins, the second to a struct right present for half the lefts ────────────────────

	[BenchmarkCategory("StructRight_ChainedInner"), Benchmark]
	public int StructRight_ChainedInner() {
		using var r = _fixtures.Query().UseIndex(_fixtureByBucket, _bucket).InnerJoinOne(_owners).InnerJoinOne(_structRights).ExecutePooled();
		return r.Count;
	}

	private static InMemoryDataCache<int, JosRef> Dimension(int count) {
		var cache = new InMemoryDataCache<int, JosRef>();
		Fill(cache, count);
		return cache;
	}

	private static void Fill(InMemoryDataCache<int, JosRef> cache, int count) {
		for (var i = 0; i < count; i++)
			cache.AddOrUpdate(i, new JosRef { Id = i, Name = "r" + i, Flag = i % 2 == 0 });
	}

	/// <summary>Cumulative Zipf(<paramref name="s"/>) over <paramref name="count"/> ranks; rank 0 is the most frequent.</summary>
	private static double[] ZipfCdf(int count, double s) {
		var cdf = new double[count];
		var sum = 0.0;
		for (var k = 0; k < count; k++)
			cdf[k] = sum += 1.0 / Math.Pow(k + 1, s);
		for (var k = 0; k < count; k++)
			cdf[k] /= sum;
		return cdf;
	}

	private static int Draw(double[] cdf, Random rng) {
		var at = Array.BinarySearch(cdf, rng.NextDouble());
		return Math.Min(at < 0 ? ~at : at, cdf.Length - 1);
	}
}

// ── Models ────────────────────────────────────────────────────────────────────────────────────────────

public enum JosChannel : byte {
	Web,
	Mobile,
	Retail,
	Kiosk,
	Partner,
}

public sealed class JosFixture : ICacheEquatable<JosFixture>, ICacheClonable<JosFixture> {
	public int Id { get; init; }
	public int Bucket { get; init; }
	public int SportTypeId { get; init; }
	public int CountryId { get; init; }
	public int LeagueId { get; init; }
	public int StatsId { get; init; }
	public int Score { get; init; }

	public bool CacheEquals(JosFixture? other) => other is not null && other.Id == Id && other.Bucket == Bucket && other.SportTypeId == SportTypeId
		&& other.CountryId == CountryId && other.LeagueId == LeagueId && other.StatsId == StatsId && other.Score == Score;

	public int CacheGetHashCode() => HashCode.Combine(Id, Bucket, SportTypeId, CountryId, LeagueId, StatsId, Score);

	public JosFixture Clone() => new() {
		Id = Id, Bucket = Bucket, SportTypeId = SportTypeId, CountryId = CountryId, LeagueId = LeagueId, StatsId = StatsId, Score = Score,
	};
}

/// <summary>A right-side row keyed by <c>int</c>: sport type, country, league, fixture detail, fixture stats.</summary>
public sealed class JosRef : ICacheEquatable<JosRef>, ICacheClonable<JosRef> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public bool Flag { get; init; }
	public bool CacheEquals(JosRef? other) => other is not null && other.Id == Id && other.Name == Name && other.Flag == Flag;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name, Flag);
	public JosRef Clone() => new() { Id = Id, Name = Name, Flag = Flag };
}

/// <summary>A price row keyed by <c>(EventId, Channel)</c> — one event is offered in every channel.</summary>
public sealed class JosOdds : ICacheEquatable<JosOdds>, ICacheClonable<JosOdds> {
	public long EventId { get; init; }
	public JosChannel Channel { get; init; }
	public int Score { get; init; }
	public bool CacheEquals(JosOdds? other) => other is not null && other.EventId == EventId && other.Channel == Channel && other.Score == Score;
	public int CacheGetHashCode() => HashCode.Combine(EventId, Channel, Score);
	public JosOdds Clone() => new() { EventId = EventId, Channel = Channel, Score = Score };
}

/// <summary>A per-event right row keyed by the event id: the game, the live info.</summary>
public sealed class JosGame : ICacheEquatable<JosGame>, ICacheClonable<JosGame> {
	public long Id { get; init; }
	public bool Active { get; init; }
	public bool CacheEquals(JosGame? other) => other is not null && other.Id == Id && other.Active == Active;
	public int CacheGetHashCode() => HashCode.Combine(Id, Active);
	public JosGame Clone() => new() { Id = Id, Active = Active };
}

/// <summary>A struct right value: an inner join cannot tell its miss by a null slot.</summary>
public readonly struct JosStructRight : ICacheEquatable<JosStructRight>, ICacheClonable<JosStructRight> {
	public int Id { get; init; }
	public int Value { get; init; }
	public bool CacheEquals(JosStructRight other) => other.Id == Id && other.Value == Value;
	public int CacheGetHashCode() => HashCode.Combine(Id, Value);
	public JosStructRight Clone() => this;
}

public sealed class JosMarket : ICacheEquatable<JosMarket>, ICacheClonable<JosMarket> {
	public long Id { get; init; }
	public long EventId { get; init; }
	public bool CacheEquals(JosMarket? other) => other is not null && other.Id == Id && other.EventId == EventId;
	public int CacheGetHashCode() => HashCode.Combine(Id, EventId);
	public JosMarket Clone() => new() { Id = Id, EventId = EventId };
}

/// <summary>Struct comparer, carried as a type parameter by the bounded sort.</summary>
public readonly struct JosFixtureByScore : IComparer<JosFixture> {
	public int Compare(JosFixture? x, JosFixture? y) => (x?.Score ?? 0).CompareTo(y?.Score ?? 0);
}

public readonly struct JosOddsByScore : IComparer<JosOdds> {
	public int Compare(JosOdds? x, JosOdds? y) => (x?.Score ?? 0).CompareTo(y?.Score ?? 0);
}

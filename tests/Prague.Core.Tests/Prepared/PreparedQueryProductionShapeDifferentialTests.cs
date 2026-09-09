namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryDifferentialTests;
using static PreparedQueryJoinDifferentialTests;

// The two production hot shapes, pinned eager == prepared == frozen row for row so the pipeline steps
// that will take them over (small-probe seed §3.4, SortBounded feed §8, JoinOne fusion §7) have their
// parity check ready. Both sort before joining, so only the page rows are joined; SortBounded breaks
// ties by encounter order, hence the pages-concatenate-to-the-whole check.
//   A. three list-index narrowers (largest bucket declared first) → SortBounded(page) → JoinOne 1:1 by PK.
//   B. "newer than T" (last-updated index; range-on-timestamp twin) → two list indexes →
//      SortBounded(page) → two chained JoinOnes 1:1 by FK.
// In this step both replay under BuildFrozen(); the tests assert that too, so the switch to a
// specialized executor in a later step is a deliberate edit here.
[TestFixture]
public class PreparedQueryProductionShapeDifferentialTests {
	// ── A: items ──────────────────────────────────────────────────────────────────
	private const int Items = 2400;
	private const int Groups = 8;

	private InMemoryDataCache<int, PqItem> _items = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byBand = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byLane = null!;
	private InMemoryDataCache<int, PqCustomer> _details = null!;

	// Group = Id % 8 (300 rows), Band = Group + 8 * (Id / 24 % 3) (100 rows, inside a group),
	// Lane = Group + 8 * (Id / 24 % 9) (33 rows, inside the band whose lane / 8 % 3 == band / 8).
	private static int Band(int id) => id % Groups + Groups * (id / 24 % 3);

	private static int Lane(int id) => id % Groups + Groups * (id / 24 % 9);

	// Ties on purpose: five distinct sort keys over 30 rows.
	private readonly struct ByIdMod5 : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => ((x?.Id ?? 0) % 5).CompareTo((y?.Id ?? 0) % 5);
	}

	// ── B: records ────────────────────────────────────────────────────────────────
	internal sealed class PqRecord : ICacheEquatable<PqRecord>, ICacheClonable<PqRecord> {
		public int Id { get; init; }
		public int KeyA { get; init; }
		public int KeyB { get; init; }
		public long Ts { get; init; }
		public int CustomerId { get; init; }
		public int ProductId { get; init; }
		public int Score { get; init; }

		public bool CacheEquals(PqRecord? other)
			=> other is not null && other.Id == Id && other.KeyA == KeyA && other.KeyB == KeyB && other.Ts == Ts && other.CustomerId == CustomerId && other.ProductId == ProductId && other.Score == Score;

		public int CacheGetHashCode() => HashCode.Combine(Id, KeyA, KeyB, Ts, CustomerId, ProductId, Score);

		public PqRecord Clone() => new() { Id = Id, KeyA = KeyA, KeyB = KeyB, Ts = Ts, CustomerId = CustomerId, ProductId = ProductId, Score = Score };
	}

	private const int Records = 10_000;
	private const long Base = 1_700_000_000_000L;
	private const long Hour = 3_600_000L;

	private InMemoryDataCache<int, PqRecord> _records = null!;
	private LastUpdatedIndex<int> _recordsUpdated = null!;
	private CacheRangeIndex<int, PqRecord, long> _tsRange = null!;
	private CacheKeyValueListIndex<int, PqRecord, int> _byKeyA = null!;
	private CacheKeyValueListIndex<int, PqRecord, int> _byKeyB = null!;
	private CacheSymmetricKeyValueListIndex<int, PqRecord, int> _recByCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqRecord, int> _recByProduct = null!;
	private InMemoryDataCache<int, PqCustomer> _customers = null!;
	private InMemoryDataCache<int, PqProduct> _products = null!;

	// Skewed keys (a hot value in each) and timestamps spread over one hour by a bijective shuffle:
	// "newer than T" at 85% of the hour selects the newest ~15%, i.e. ~100 rows of the hot pair.
	private static int KeyA(int i) => i % 3 == 0 ? 0 : 1 + i % 19;

	private static int KeyB(int i) => i % 5 == 0 ? 0 : 1 + i % 49;

	private static long Ts(int i) => Base + (long)(i * 7919 % Records) * Hour / Records;

	private readonly struct ByScoreTies : IComparer<PqRecord> {
		public int Compare(PqRecord? x, PqRecord? y) => ((x?.Score ?? 0) & 7).CompareTo((y?.Score ?? 0) & 7);
	}

	[OneTimeSetUp]
	public void SetUp() {
		_items = new InMemoryDataCache<int, PqItem>();
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byBand = _items.CacheKeyValueListIndex<int>(static (_, v) => Band(v.Id));
		_byLane = _items.CacheKeyValueListIndex<int>(static (_, v) => Lane(v.Id));
		_details = new InMemoryDataCache<int, PqCustomer>();
		for (var i = 0; i < Items; i++) {
			_items.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % Groups, Flag = i % 3 == 0 });
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		_records = new InMemoryDataCache<int, PqRecord>();
		_recordsUpdated = new LastUpdatedIndex<int>();
		_records.CacheLastUpdatedIndex(_recordsUpdated, static (id, _) => id);
		_tsRange = _records.CacheRangeIndex<long>(static (_, v) => v.Ts);
		_byKeyA = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyA);
		_byKeyB = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyB);
		_recByCustomer = _records.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_recByProduct = _records.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_customers = new InMemoryDataCache<int, PqCustomer>();
		_products = new InMemoryDataCache<int, PqProduct>();
		for (var c = 0; c < 2000; c++)
			if (c % 7 != 0) _customers.AddOrUpdate(c, new PqCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var p = 0; p < 500; p++)
			_products.AddOrUpdate(p, new PqProduct { Id = p, Category = "c" + p % 9 });
		var rng = new Random(42);
		for (var i = 0; i < Records; i++)
			_records.AddOrUpdate(i, new PqRecord { Id = i, KeyA = KeyA(i), KeyB = KeyB(i), Ts = Ts(i), CustomerId = i % 2000, ProductId = i % 500, Score = rng.Next(1 << 20) }, Ts(i));
	}

	private static string Detail(JoinResult<PqItem, PqCustomer?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}";

	private static string Two(JoinResult<PqRecord, PqCustomer?, PqProduct?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id)}|{(r.Right2 is null ? "-" : r.Right2.Id)}";

	private static readonly (int skip, int take)[] Pages = [(0, 5), (5, 5), (20, 20), (0, int.MaxValue), (3, int.MaxValue), (29, 5), (100, 5)];

	// ── A ─────────────────────────────────────────────────────────────────────────

	[Test]
	public void A_ListListList_SortBounded_JoinOne_EveryPage_LikeEagerAndPrepared_ThreeArgSets() {
		var prepared = _items.Prepare<int, PqItem, (int group, int band, int lane)>()
			.UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new ByIdMod5()).JoinOne(_details).Build();
		var frozen = _items.Prepare<int, PqItem, (int group, int band, int lane)>()
			.UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new ByIdMod5()).JoinOne(_details).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"), "joined + sorted: replay in this step");
		Assert.That(frozen.Plan.IsSorted, Is.True);
		// (group, band inside it, lane inside the band) → 33 rows; a lane outside the band → 0; a missing group → 0.
		foreach (var args in new[] { (3, 3 + 8 * 1, 3 + 8 * 4), (0, 0, 0), (5, 5 + 8 * 2, 5 + 8 * 5), (5, 5 + 8 * 2, 5 + 8 * 6), (9, 1, 1) }) {
			var (g, b, l) = args;
			foreach (var (skip, take) in Pages) {
				AssertSameJoined(_items.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byLane, l).SortBounded(new ByIdMod5()).JoinOne(_details).Execute(skip, take), frozen.Execute(args, skip, take), Detail);
				AssertSameJoined(_items.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byLane, l).SortBounded(new ByIdMod5()).JoinOne(_details).ExecutePooled(skip, take), frozen.ExecutePooled(args, skip, take), Detail);
				AssertSameJoined(prepared.ExecutePooledCloned(args, skip, take), frozen.ExecutePooledCloned(args, skip, take), Detail);
			}

			var count = _items.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byLane, l).SortBounded(new ByIdMod5()).JoinOne(_details).Count();
			Assert.That(frozen.Count(args), Is.EqualTo(count), "Count " + args);
			Assert.That(prepared.Count(args), Is.EqualTo(count));
		}
	}

	[Test]
	public void A_Pages_ConcatenateToTheWholeResult() {
		var args = (group: 3, band: 3 + 8 * 1, lane: 3 + 8 * 4);
		using var whole = _items.Query().UseIndex(_byGroup, args.group).UseIndex(_byBand, args.band).UseIndex(_byLane, args.lane).SortBounded(new ByIdMod5()).JoinOne(_details).ExecutePooled(0, Items);
		Assert.That(whole.Count, Is.EqualTo(33));
		var expected = new string[whole.Count];
		for (var i = 0; i < whole.Count; i++) expected[i] = Detail(whole[i]);
		foreach (var q in new[] {
			_items.Prepare<int, PqItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane).SortBounded(new ByIdMod5()).JoinOne(_details).Build(),
			_items.Prepare<int, PqItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane).SortBounded(new ByIdMod5()).JoinOne(_details).BuildFrozen(),
		}) {
			var paged = new List<string>();
			for (var skip = 0; skip < whole.Count; skip += 7) {
				using var page = q.ExecutePooled(args, skip, 7);
				for (var i = 0; i < page.Count; i++) paged.Add(Detail(page[i]));
			}

			Assert.That(paged, Is.EqualTo(expected).AsCollection);
		}
	}

	// ── B ─────────────────────────────────────────────────────────────────────────

	private static readonly (long t, int keyA, int keyB)[] ArgSets = [(Base + Hour * 85 / 100, 0, 0), (Base + Hour * 50 / 100, 0, 7), (Base + Hour * 95 / 100, 4, 0), (Base + Hour, 0, 0)];

	[Test]
	public void B_LastUpdated_ListList_SortBounded_JoinTwo_EveryPage_LikeEagerAndPrepared_ArgSets() {
		var prepared = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Build();
		var frozen = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		Assert.That(frozen.Plan.Narrowers[0].Kind, Is.EqualTo(NarrowerKind.LastUpdatedAfter));
		foreach (var args in ArgSets) {
			var (t, a, b) = args;
			foreach (var (skip, take) in Pages) {
				AssertSameJoined(_records.Query().UseIndex(_recordsUpdated, t).UseIndex(_byKeyA, a).UseIndex(_byKeyB, b).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Execute(skip, take),
					frozen.Execute(args, skip, take), Two);
				AssertSameJoined(prepared.ExecutePooled(args, skip, take), frozen.ExecutePooled(args, skip, take), Two);
			}

			var count = _records.Query().UseIndex(_recordsUpdated, t).UseIndex(_byKeyA, a).UseIndex(_byKeyB, b).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Count();
			Assert.That(frozen.Count(args), Is.EqualTo(count), "Count " + args);
			Assert.That(prepared.Count(args), Is.EqualTo(count));
		}

		// The hot pair in the newest 15% is a real page.
		Assert.That(frozen.Count(ArgSets[0]), Is.GreaterThan(40));
	}

	[Test]
	public void B_RangeOnTimestamp_ListList_SortBounded_JoinTwo_EveryPage_LikeEagerAndPrepared_ArgSets() {
		var prepared = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t)).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Build();
		var frozen = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t)).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Replay"));
		foreach (var args in ArgSets) {
			var (t, a, b) = args;
			foreach (var (skip, take) in Pages) {
				AssertSameJoined(_records.Query().UseIndex(_tsRange, static (rb, x) => rb.Gt(x.t), args).UseIndex(_byKeyA, a).UseIndex(_byKeyB, b).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Execute(skip, take),
					frozen.Execute(args, skip, take), Two);
				AssertSameJoined(prepared.ExecutePooled(args, skip, take), frozen.ExecutePooled(args, skip, take), Two);
			}

			Assert.That(frozen.Count(args), Is.EqualTo(_records.Query().UseIndex(_tsRange, static (rb, x) => rb.Gt(x.t), args).UseIndex(_byKeyA, a).UseIndex(_byKeyB, b).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Count()));
		}

		// Both time-index kinds select the same rows.
		var byTime = _records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).Build();
		foreach (var args in ArgSets)
			AssertSameJoined(byTime.Execute(args, 20, 20), frozen.Execute(args, 20, 20), Two);
	}

	[Test]
	public void B_Pages_ConcatenateToTheWholeResult_BothTimeIndexKinds() {
		var args = ArgSets[0];
		using var whole = _records.Query().UseIndex(_recordsUpdated, args.t).UseIndex(_byKeyA, args.keyA).UseIndex(_byKeyB, args.keyB).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).ExecutePooled(0, Records);
		Assert.That(whole.Count, Is.GreaterThan(40));
		var expected = new string[whole.Count];
		for (var i = 0; i < whole.Count; i++) expected[i] = Two(whole[i]);
		foreach (var q in new[] {
			_records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).BuildFrozen(),
			_records.Prepare<int, PqRecord, (long t, int keyA, int keyB)>().UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t)).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).SortBounded(new ByScoreTies()).JoinOne(_recByCustomer, _customers).JoinOne(_recByProduct, _products).BuildFrozen(),
		}) {
			var paged = new List<string>();
			for (var skip = 0; skip < whole.Count; skip += 20) {
				using var page = q.ExecutePooled(args, skip, 20);
				for (var i = 0; i < page.Count; i++) paged.Add(Two(page[i]));
			}

			Assert.That(paged, Is.EqualTo(expected).AsCollection);
		}
	}
}

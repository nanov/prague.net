namespace Prague.Core.Tests.Prepared;

using Prague.Core;

// #83's spike (docs/superpowers/specs/2026-09-12-timerange-index-spike.md): before deciding whether a
// time-bucketed index is worth building, settle whether the last-updated step is the seed at all on the
// two shapes the frozen rows measure. Both fixtures are the benchmark's own — shape A = 100k rows, one
// list bucket of 1k, timestamps 1 ms apart; shape B = 10k rows over an hour, two skewed list buckets
// (keyA 3334, keyB 2000) — and the four windows are the spike benchmark's.
//
// What this pins, and it was the spike's main result: the winner used to be decided by declaration
// order, not by selectivity. PipelineCore.ChooseSmallest took the FIRST exact signal over any inexact
// incumbent unconditionally (`!exact || value < signal`) and only afterwards let the 2x rule win an
// estimate back, so shape B — which declares its time window first — could not seed on it at any
// window, not even at an estimate of 2 against a list of 2000, while shape A's list-first spelling of
// the same thing did. #97 made the rule symmetric (an exact signal displaces an estimate only when it
// is smaller than twice that estimate), and what these tests pin now is the other side of it: both
// spellings of shape B choose the same seed at every window, and shape A is untouched.
//
// Note on shape A's one-second row: the B+tree estimate under-reports on dense ascending keys (451 for
// a true 1000), which is why its narrow window seeds on time at all. The symmetric rule does not depend
// on that — shape A declares its list first, so its arm of the rule is the one that did not change.
[TestFixture]
public class TimeRangeSeedChoiceProbeTests {
	private const int N = 100_000;
	private const int Buckets = 100;
	private const long ABase = 1_000_000L;
	private const int RecordCount = 10_000;
	private const long RecordBase = 1_700_000_000_000L;
	private const long Hour = 3_600_000L;

	internal sealed class TrItem : ICacheEquatable<TrItem>, ICacheClonable<TrItem> {
		public int Id { get; init; }
		public int Group { get; init; }
		public int KeyA { get; init; }
		public int KeyB { get; init; }

		public bool CacheEquals(TrItem? other)
			=> other is not null && other.Id == Id && other.Group == Group && other.KeyA == KeyA && other.KeyB == KeyB;

		public int CacheGetHashCode() => HashCode.Combine(Id, Group, KeyA, KeyB);

		public TrItem Clone() => new() { Id = Id, Group = Group, KeyA = KeyA, KeyB = KeyB };
	}

	private InMemoryDataCache<int, TrItem> _items = null!;
	private CacheKeyValueListIndex<int, TrItem, int> _byGroup = null!;
	private LastUpdatedIndex<int> _itemsUpdated = null!;

	private InMemoryDataCache<int, TrItem> _records = null!;
	private CacheKeyValueListIndex<int, TrItem, int> _byKeyA = null!;
	private CacheKeyValueListIndex<int, TrItem, int> _byKeyB = null!;
	private LastUpdatedIndex<int> _recordsUpdated = null!;

	[OneTimeSetUp]
	public void SetUp() {
		_items = new InMemoryDataCache<int, TrItem>();
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_itemsUpdated = new LastUpdatedIndex<int>();
		_items.CacheLastUpdatedIndex(_itemsUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_items.AddOrUpdate(i, new TrItem { Id = i, Group = i % Buckets }, ABase + i);

		_records = new InMemoryDataCache<int, TrItem>();
		_byKeyA = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyA);
		_byKeyB = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyB);
		_recordsUpdated = new LastUpdatedIndex<int>();
		_records.CacheLastUpdatedIndex(_recordsUpdated, static (id, _) => id);
		for (var i = 0; i < RecordCount; i++) {
			var ts = RecordBase + (long)(i * 7919 % RecordCount) * Hour / RecordCount;
			_records.AddOrUpdate(i, new TrItem { Id = i, KeyA = i % 3 == 0 ? 0 : 1 + i % 19, KeyB = i % 5 == 0 ? 0 : 1 + i % 49 }, ts);
		}
	}

	private static string Decision(string explain) {
		var lines = explain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return lines[^1];
	}

	// The four windows the spike's benchmark uses: one second, one minute, the shape's own production
	// argument, and T = 0 (before any horizon a ring would carry).
	private static (string Label, long After)[] Windows(long newest, long production) => [
		("second", newest - 1_000),
		("minute", newest - 60_000),
		("production", production),
		("all", 0L),
	];

	// List declared first: the 1 k bucket is the exact incumbent, and the 2x rule lets the last-updated
	// estimate win the one-second window back off it. Every wider window stays on the list.
	[Test]
	public void ShapeA_ListFirst_TheTimeStepSeedsOnlyTheNarrowestWindow() {
		var frozen = _items.Prepare<int, TrItem, (int group, long after)>()
			.UseIndex(_byGroup, static a => a.group).UseIndex(_itemsUpdated, static a => a.after).BuildFrozen();

		var seeds = new List<string>();
		foreach (var (label, after) in Windows(ABase + N - 1, ABase + N / 2)) {
			using var rows = frozen.ExecutePooled((13, after));
			var decision = Decision(frozen.Explain());
			TestContext.Out.WriteLine($"A {label,-10} after={after} rows={rows.Count,6} estimate={_itemsUpdated.EstimateCount(after),6} listBucket=1000 | {decision}");
			seeds.Add(decision);
		}

		Assert.Multiple(() => {
			Assert.That(seeds[0], Is.EqualTo("last seed: step 1 LastUpdatedAfter (signal 451), free: smallest signal"), "one second: 2 x 451 < 1000");
			Assert.That(seeds[1], Does.Contain("step 0 ListEq (signal 1000)"), "one minute");
			Assert.That(seeds[2], Does.Contain("step 0 ListEq (signal 1000)"), "production");
			Assert.That(seeds[3], Does.Contain("step 0 ListEq (signal 1000)"), "whole range");
		});
	}

	// Time declared first: the estimate is the incumbent when the first exact signal arrives, and the
	// symmetric rule now weighs it — 2000 does not displace an estimate of 2, or of 166. Before #97 every
	// window here seeded on step 2 ListEq (signal 2000); the two narrow ones moved to the smaller signal
	// and the two wide ones did not move at all.
	[Test]
	public void ShapeB_TimeFirst_TheTimeStepSeedsTheNarrowWindows() {
		var frozen = _records.Prepare<int, TrItem, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).BuildFrozen();

		var seeds = new List<string>();
		foreach (var (label, after) in Windows(RecordBase + Hour, RecordBase + Hour * 85 / 100)) {
			using var rows = frozen.ExecutePooled((after, 0, 0));
			var decision = Decision(frozen.Explain());
			TestContext.Out.WriteLine($"B time-first  {label,-10} after={after} rows={rows.Count,6} estimate={_recordsUpdated.EstimateCount(after),6} keyA=3334 keyB=2000 | {decision}");
			seeds.Add(decision);
		}

		Assert.Multiple(() => {
			Assert.That(seeds[0], Does.Contain("step 0 LastUpdatedAfter"), "one second: 2000 is not < 2 x 2");
			Assert.That(seeds[1], Does.Contain("step 0 LastUpdatedAfter"), "one minute: 2000 is not < 2 x 166");
			Assert.That(seeds[2], Does.Contain("step 2 ListEq (signal 2000)"), "production: 2000 < 2 x 1231");
			Assert.That(seeds[3], Does.Contain("step 2 ListEq (signal 2000)"), "whole range");
		});
	}

	// The same three steps, list-first: now the estimate is measured against an exact incumbent and the
	// 2x rule applies, so the narrow windows seed on time. Nothing about the index changed.
	[Test]
	public void ShapeB_ListFirst_TheTimeStepSeedsTheNarrowWindows() {
		var frozen = _records.Prepare<int, TrItem, (long t, int keyA, int keyB)>()
			.UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).UseIndex(_recordsUpdated, static a => a.t).BuildFrozen();

		var seeds = new List<string>();
		foreach (var (label, after) in Windows(RecordBase + Hour, RecordBase + Hour * 85 / 100)) {
			using var rows = frozen.ExecutePooled((after, 0, 0));
			var decision = Decision(frozen.Explain());
			TestContext.Out.WriteLine($"B list-first  {label,-10} after={after} rows={rows.Count,6} estimate={_recordsUpdated.EstimateCount(after),6} keyA=3334 keyB=2000 | {decision}");
			seeds.Add(decision);
		}

		Assert.Multiple(() => {
			Assert.That(seeds[0], Does.Contain("LastUpdatedAfter"), "one second: 2 x 2 < 2000");
			Assert.That(seeds[1], Does.Contain("LastUpdatedAfter"), "one minute: 2 x 166 < 2000");
			Assert.That(seeds[2], Does.Contain("step 1 ListEq (signal 2000)"), "production: 2 x 1231 > 2000");
			Assert.That(seeds[3], Does.Contain("step 1 ListEq (signal 2000)"), "whole range");
		});
	}

	// The step index is the one thing the two spellings cannot agree on; the narrower and its signal are.
	private static string SeedWithoutStepIndex(string decision) {
		var at = decision.IndexOf("step ", StringComparison.Ordinal);
		var end = decision.IndexOf(' ', at + 5);
		return decision.Remove(at, end + 1 - at);
	}

	private static int[] SortedIds(IEnumerable<TrItem> rows) {
		var ids = rows.Select(static v => v.Id).ToArray();
		Array.Sort(ids);
		return ids;
	}

	// #97's differential: the seed is a property of the query, not of the order its narrowings were
	// written in. The same three steps in both spellings, at every window — the same seed, the same rows
	// as eager (a multiset: an unsorted frozen result has no row-order guarantee) and the same Count.
	[Test]
	public void ShapeB_BothDeclarationOrders_ChooseTheSameSeed_AndEagersRows() {
		var timeFirst = _records.Prepare<int, TrItem, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).BuildFrozen();
		var listFirst = _records.Prepare<int, TrItem, (long t, int keyA, int keyB)>()
			.UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).UseIndex(_recordsUpdated, static a => a.t).BuildFrozen();

		foreach (var (label, after) in Windows(RecordBase + Hour, RecordBase + Hour * 85 / 100)) {
			var args = (after, 0, 0);
			using var eager = _records.Query().UseIndex(_byKeyA, 0).UseIndex(_byKeyB, 0).UseIndex(_recordsUpdated, after).Execute();
			var expected = SortedIds(eager);
			using var timeRows = timeFirst.ExecutePooled(args);
			var timeSeed = SeedWithoutStepIndex(Decision(timeFirst.Explain()));
			using var listRows = listFirst.ExecutePooled(args);
			var listSeed = SeedWithoutStepIndex(Decision(listFirst.Explain()));
			TestContext.Out.WriteLine($"B both {label,-10} rows={expected.Length,6} | time-first {timeSeed} | list-first {listSeed}");
			Assert.Multiple(() => {
				Assert.That(timeSeed, Is.EqualTo(listSeed), $"the seed must not depend on declaration order / {label}");
				Assert.That(SortedIds(timeRows), Is.EqualTo(expected).AsCollection, $"time-first rows / {label}");
				Assert.That(SortedIds(listRows), Is.EqualTo(expected).AsCollection, $"list-first rows / {label}");
				Assert.That(timeFirst.Count(args), Is.EqualTo(expected.Length), $"time-first count / {label}");
				Assert.That(listFirst.Count(args), Is.EqualTo(expected.Length), $"list-first count / {label}");
			});
		}
	}
}

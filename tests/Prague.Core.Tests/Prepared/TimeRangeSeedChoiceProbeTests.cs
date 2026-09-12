namespace Prague.Core.Tests.Prepared;

using Prague.Core;

// #83's spike (docs/superpowers/specs/2026-09-12-timerange-index-spike.md): before deciding whether a
// time-bucketed index is worth building, settle whether the last-updated step is the seed at all on the
// two shapes the frozen rows measure. Both fixtures are the benchmark's own — shape A = 100k rows, one
// list bucket of 1k, timestamps 1 ms apart; shape B = 10k rows over an hour, two skewed list buckets
// (keyA 3334, keyB 2000) — and the four windows are the spike benchmark's.
//
// What this pins, and it is the spike's main result: the winner is decided by declaration order, not by
// selectivity. PipelineExecutor.cs:377-391 walks the steps in plan order and takes the FIRST exact
// signal over any inexact incumbent unconditionally (`!exact || value < signal`); only afterwards does
// the 2x rule let an inexact signal win back. So shape A, which declares the list first, lets the
// last-updated estimate seed the one-second window (451 vs 1000, 2 x 451 < 1000) — while shape B, which
// declares the time window first, cannot seed on it at any window, not even when the estimate is 2
// against a list of 2000. Declaring the same three steps list-first is enough to flip it.
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

	// Time declared first: the estimate is the incumbent when the first exact signal arrives, and that
	// signal takes the seed unconditionally. An estimate of 2 loses to a list of 2000.
	[Test]
	public void ShapeB_TimeFirst_TheTimeStepNeverSeeds_EvenAtTwoKeys() {
		var frozen = _records.Prepare<int, TrItem, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).BuildFrozen();

		foreach (var (label, after) in Windows(RecordBase + Hour, RecordBase + Hour * 85 / 100)) {
			using var rows = frozen.ExecutePooled((after, 0, 0));
			var decision = Decision(frozen.Explain());
			TestContext.Out.WriteLine($"B time-first  {label,-10} after={after} rows={rows.Count,6} estimate={_recordsUpdated.EstimateCount(after),6} keyA=3334 keyB=2000 | {decision}");
			Assert.That(decision, Does.Contain("step 2 ListEq (signal 2000)"), $"shape B / {label}");
		}
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
}

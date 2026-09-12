namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Prague.Core;

/// <summary>
///   What #83's narrow-window win is actually blocked on today, measured end to end. The seed chooser
///   (<c>PipelineExecutor.cs:377-391</c>) walks the plan's steps in declaration order and lets the first
///   <b>exact</b> signal displace any <b>inexact</b> incumbent unconditionally; only after an exact
///   incumbent is in hand does the 2x rule let an estimate win the seed back. Shape B's production query
///   declares the time window first, so its last-updated estimate is always the incumbent when the first
///   list arrives — and loses, at every window, even when the estimate is 2 against a list of 2000
///   (pinned in <c>TimeRangeSeedChoiceProbeTests</c>).
///   The two methods are the same three narrowings over the same fixture, differing only in the order
///   they are declared in. No index changes, no new structure: this is the headroom a bucketed index
///   would be credited with if the order were not deciding it first.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
public class TimeRangeSeedOrderBenchmarks {
	private const string Category = "TimeRangeSeedOrder";
	private const int RecordCount = 10_000;
	private const long RecordBase = 1_700_000_000_000L;
	private const long Hour = 3_600_000L;

	[Params(SeedWindow.Second, SeedWindow.Minute, SeedWindow.Production, SeedWindow.All)]
	public SeedWindow Window { get; set; }

	private InMemoryDataCache<int, PqbRecord> _records = null!;
	private CacheKeyValueListIndex<int, PqbRecord, int> _byKeyA = null!;
	private CacheKeyValueListIndex<int, PqbRecord, int> _byKeyB = null!;
	private LastUpdatedIndex<int> _recordsUpdated = null!;

	private FrozenQuery<(long t, int keyA, int keyB), PqbRecord> _timeFirst = null!;
	private FrozenQuery<(long t, int keyA, int keyB), PqbRecord> _listFirst = null!;
	private (long t, int keyA, int keyB) _args;

	[GlobalSetup]
	public void Setup() {
		_records = new InMemoryDataCache<int, PqbRecord>();
		_recordsUpdated = new LastUpdatedIndex<int>();
		_records.CacheLastUpdatedIndex(_recordsUpdated, static (id, _) => id);
		_byKeyA = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyA);
		_byKeyB = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyB);
		var rng = new Random(1234);
		for (var i = 0; i < RecordCount; i++) {
			var ts = RecordBase + (long)(i * 7919 % RecordCount) * Hour / RecordCount;
			_records.AddOrUpdate(i, new PqbRecord {
				Id = i, KeyA = i % 3 == 0 ? 0 : 1 + i % 19, KeyB = i % 5 == 0 ? 0 : 1 + i % 49,
				Ts = ts, CustomerId = i % 2000, ProductId = i % 500, Score = rng.Next(1 << 20),
			}, ts);
		}

		_timeFirst = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).BuildFrozen();
		_listFirst = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>()
			.UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB).UseIndex(_recordsUpdated, static a => a.t).BuildFrozen();

		_args = (Window switch {
			SeedWindow.Second => RecordBase + Hour - 1_000,
			SeedWindow.Minute => RecordBase + Hour - 60_000,
			SeedWindow.Production => RecordBase + Hour * 85 / 100,
			_ => 0L,
		}, 0, 0);

		if (TimeFirst() != ListFirst())
			throw new InvalidOperationException("the two declaration orders must return the same rows");
	}

	[BenchmarkCategory(Category), Benchmark(Baseline = true)]
	public int TimeFirst() {
		using var r = _timeFirst.ExecutePooled(_args);
		return r.Count;
	}

	[BenchmarkCategory(Category), Benchmark]
	public int ListFirst() {
		using var r = _listFirst.ExecutePooled(_args);
		return r.Count;
	}
}

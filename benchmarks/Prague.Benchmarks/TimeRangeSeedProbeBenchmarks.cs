namespace Prague.Benchmarks;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Prague.Core;
using Prague.Core.Collections;
using Bucket = Prague.Core.Collections.PooledSet<int, Prague.Core.Collections.DefaultKeyComparer<int>>;

/// <summary>Which fixture the case runs on: A = 100k keys, 1 ms apart over 100 s; B = 10k keys shuffled over one hour.</summary>
public enum SeedShape {
	A,
	B,
}

/// <summary>How far back <c>T</c> reaches. <c>Production</c> is the argument the shape's own row in <see cref="FrozenQueryBenchmarks" /> uses; <c>All</c> is <c>T = 0</c>, before the ring's horizon.</summary>
public enum SeedWindow {
	Second,
	Minute,
	Production,
	All,
}

/// <summary>
///   #83's one open question, measured before the index is built: is "everything since T" slow because
///   of the B+tree walk, or is the walk not the cost? Attribution of the last-updated step's seed path
///   against a hand-built bucket ring, on the two fixtures the frozen rows already use.
///   <list type="bullet">
///     <item><b>TreeWalk</b> — <c>GetValuesGt</c> with a counting aggregator: the descent plus the leaf
///       chain, with nothing written out. The walk alone.</item>
///     <item><b>TreeSeed</b> (baseline) — the same walk into <c>SeedKeys</c> through
///       <c>SeedAggregators.Plain</c>: byte for byte what <c>LastUpdatedStep.Seed</c> runs today.</item>
///     <item><b>RingFineCopy</b> / <b>RingCoarseCopy</b> — the union of whole buckets through the bulk
///       <c>PooledSet.CopyKeysTo</c>, boundary bucket copied unfiltered. Not a correct answer; it is the
///       ring's floor.</item>
///     <item><b>RingFineFilter</b> — plus the exact per-key filter on the boundary bucket
///       (<c>TryGetLastUpdated</c> + one compare), which is what makes the bucket granularity invisible
///       and the signal exact.</item>
///     <item><b>RingFineDedupe</b> / <b>RingCoarseDedupe</b> — plus the <c>ValueSet</c> dedupe every key
///       pays. Mandatory: a writer's Remove and Add are not atomic as a pair, so a key can be seen in
///       two buckets. This is the honest ring number.</item>
///   </list>
///   Granularity is per shape, chosen so each fixture gets one ring far finer than the windows and one
///   far coarser: A = 1 s (100 buckets of 1000) and 1 min (2 buckets of 60k / 40k); B = 1 min (60 buckets
///   of ~167) and 1 h (one bucket holding everything). Buckets are lazy — a span with no key allocates
///   no bucket — and indexed by division from a fixed base; wrap-around is not exercised, every
///   timestamp falls inside one horizon. Each bucket copy takes its own <c>ReaderGate</c> pin, which a
///   real ring would hoist to one pin for the whole union; that overhead is bounded by the bucket count
///   and lands only on the wide windows, where the ring is already losing.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
public unsafe class TimeRangeSeedProbeBenchmarks {
	private const string Category = "TimeRangeSeedProbe";
	private const int N = 100_000;
	private const long ABase = 1_000_000L;
	private const int RecordCount = 10_000;
	private const long RecordBase = 1_700_000_000_000L;
	private const long Hour = 3_600_000L;

	[Params(SeedShape.A, SeedShape.B)] public SeedShape Shape { get; set; }

	[Params(SeedWindow.Second, SeedWindow.Minute, SeedWindow.Production, SeedWindow.All)]
	public SeedWindow Window { get; set; }

	private LastUpdatedIndex<int> _index = null!;
	private long[] _timestamps = null!;
	private Bucket?[] _fine = null!;
	private Bucket?[] _coarse = null!;
	private int _keys;
	private long _ringBase;
	private long _fineGranularity;
	private long _coarseGranularity;
	private long _after;

	[GlobalSetup]
	public void Setup() {
		_index = new LastUpdatedIndex<int>();
		if (Shape is SeedShape.A) {
			_keys = N;
			_ringBase = ABase;
			_fineGranularity = 1_000;
			_coarseGranularity = 60_000;
			_timestamps = new long[N];
			for (var i = 0; i < N; i++)
				_timestamps[i] = ABase + i;
			_after = Window switch {
				SeedWindow.Second => ABase + N - 1 - 1_000,
				SeedWindow.Minute => ABase + N - 1 - 60_000,
				SeedWindow.Production => ABase + N / 2,
				_ => 0L,
			};
		} else {
			_keys = RecordCount;
			_ringBase = RecordBase;
			_fineGranularity = 60_000;
			_coarseGranularity = Hour;
			_timestamps = new long[RecordCount];
			for (var i = 0; i < RecordCount; i++)
				_timestamps[i] = RecordBase + (long)(i * 7919 % RecordCount) * Hour / RecordCount;
			_after = Window switch {
				SeedWindow.Second => RecordBase + Hour - 1_000,
				SeedWindow.Minute => RecordBase + Hour - 60_000,
				SeedWindow.Production => RecordBase + Hour * 85 / 100,
				_ => 0L,
			};
		}

		for (var i = 0; i < _keys; i++)
			_index.Add(i, _timestamps[i]);

		_fine = BuildRing(_fineGranularity);
		_coarse = BuildRing(_coarseGranularity);

		var expected = TreeSeed();
		Check(TreeWalk(), expected, "tree walk");
		Check(RingFineFilter(), expected, "ring fine, filtered");
		Check(RingFineDedupe(), expected, "ring fine, filtered + deduped");
		Check(RingFineDedupePresized(), expected, "ring fine, filtered + deduped, pre-sized");
		Check(RingCoarseDedupe(), expected, "ring coarse, filtered + deduped");
		if (RingFineCopy() < expected)
			throw new InvalidOperationException("the unfiltered copy must be a superset of the window");
	}

	private static void Check(int actual, int expected, string what) {
		if (actual != expected)
			throw new InvalidOperationException($"{what}: {actual} keys, expected {expected}");
	}

	private Bucket?[] BuildRing(long granularity) {
		var span = 0L;
		for (var i = 0; i < _keys; i++)
			if (_timestamps[i] - _ringBase > span)
				span = _timestamps[i] - _ringBase;

		var ring = new Bucket?[(int)(span / granularity) + 1];
		for (var i = 0; i < _keys; i++)
			(ring[(int)((_timestamps[i] - _ringBase) / granularity)] ??= new Bucket()).Add(i);
		return ring;
	}

	/// <summary>The first bucket the window touches; it holds <c>T</c> itself unless <c>T</c> predates the ring base.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int FirstBucket(long granularity) => _after <= _ringBase ? 0 : (int)((_after - _ringBase) / granularity);

	// ───────────────────── the seed as it runs today ─────────────────────

	[BenchmarkCategory(Category), Benchmark(Baseline = true)]
	public int TreeSeed() {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var seed = SeedKeys<int>.Over(stack);
		try {
			var agg = new SeedAggregators<int, long>.Plain(ref seed);
			_index.GetValuesGt(_after, ref agg);
			return seed.Count;
		} finally {
			seed.Dispose();
		}
	}

	[BenchmarkCategory(Category), Benchmark]
	public int TreeWalk() {
		var agg = new Counting();
		_index.GetValuesGt(_after, ref agg);
		return agg.Count;
	}

	// ───────────────────── the ring ─────────────────────

	[BenchmarkCategory(Category), Benchmark]
	public int RingFineCopy() => Copy(_fine, _fineGranularity);

	[BenchmarkCategory(Category), Benchmark]
	public int RingFineFilter() => Filter(_fine, _fineGranularity);

	[BenchmarkCategory(Category), Benchmark]
	public int RingFineDedupe() => Dedupe(_fine, _fineGranularity);

	[BenchmarkCategory(Category), Benchmark]
	public int RingFineDedupePresized() => DedupePresized(_fine, _fineGranularity);

	[BenchmarkCategory(Category), Benchmark]
	public int RingCoarseCopy() => Copy(_coarse, _coarseGranularity);

	[BenchmarkCategory(Category), Benchmark]
	public int RingCoarseDedupe() => Dedupe(_coarse, _coarseGranularity);

	private int Copy(Bucket?[] ring, long granularity) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var seed = SeedKeys<int>.Over(stack);
		try {
			var plain = new PlainSink(ref seed);
			for (var i = FirstBucket(granularity); i < ring.Length; i++)
				if (ring[i] is { } bucket)
					bucket.CopyKeysTo(ref plain);
			return seed.Count;
		} finally {
			seed.Dispose();
		}
	}

	private int Filter(Bucket?[] ring, long granularity) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var seed = SeedKeys<int>.Over(stack);
		try {
			var first = FirstBucket(granularity);
			if (_after > _ringBase && first < ring.Length) {
				if (ring[first] is { } boundary) {
					var exact = new FilterSink(ref seed, _index, _after);
					boundary.CopyKeysTo(ref exact);
				}

				first++;
			}

			var plain = new PlainSink(ref seed);
			for (var i = first; i < ring.Length; i++)
				if (ring[i] is { } bucket)
					bucket.CopyKeysTo(ref plain);
			return seed.Count;
		} finally {
			seed.Dispose();
		}
	}

	private int Dedupe(Bucket?[] ring, long granularity) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var seed = SeedKeys<int>.Over(stack);
		var seen = new ValueSet<int, DefaultKeyComparer<int>>();
		try {
			var first = FirstBucket(granularity);
			if (_after > _ringBase && first < ring.Length) {
				if (ring[first] is { } boundary) {
					var exact = new FilterDedupeSink(ref seed, ref seen, _index, _after);
					boundary.CopyKeysTo(ref exact);
				}

				first++;
			}

			var plain = new DedupeSink(ref seed, ref seen);
			for (var i = first; i < ring.Length; i++)
				if (ring[i] is { } bucket)
					bucket.CopyKeysTo(ref plain);
			return seed.Count;
		} finally {
			seen.Dispose();
			seed.Dispose();
		}
	}

	/// <summary>
	///   <see cref="Dedupe" /> with the dedupe set pre-sized from the buckets' own counts — the one
	///   thing a real ring can do that the query path cannot, since every bucket knows its exact
	///   <c>Count</c>. Removes the rehash cascade from 47 slots up to the window size.
	/// </summary>
	private int DedupePresized(Bucket?[] ring, long granularity) {
		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var seed = SeedKeys<int>.Over(stack);
		var first = FirstBucket(granularity);
		var capacity = 0;
		for (var i = first; i < ring.Length; i++)
			if (ring[i] is { } sized)
				capacity += sized.Count;

		var seen = new ValueSet<int, DefaultKeyComparer<int>>(capacity);
		try {
			if (_after > _ringBase && first < ring.Length) {
				if (ring[first] is { } boundary) {
					var exact = new FilterDedupeSink(ref seed, ref seen, _index, _after);
					boundary.CopyKeysTo(ref exact);
				}

				first++;
			}

			var plain = new DedupeSink(ref seed, ref seen);
			for (var i = first; i < ring.Length; i++)
				if (ring[i] is { } bucket)
					bucket.CopyKeysTo(ref plain);
			return seed.Count;
		} finally {
			seen.Dispose();
			seed.Dispose();
		}
	}

	// ───────────────────── sinks ─────────────────────
	// A ref field to a ref struct is CS9050; the pointer is laundered through void* exactly as
	// SeedAggregators does, and no sink outlives the copy it is passed to.

	private struct Counting : PooledBTree<long, int>.IResultAggregator {
		internal int Count;

		public void Add(long index, int value) => Count++;

		public void Dispose() { }
	}

	private ref struct PlainSink(ref SeedKeys<int> seed) : IKeySink<int> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);

		public void Add(int key) => Unsafe.AsRef<SeedKeys<int>>(_seed).Add(key);
	}

	private ref struct DedupeSink(ref SeedKeys<int> seed, ref ValueSet<int, DefaultKeyComparer<int>> seen) : IKeySink<int> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly void* _seen = Unsafe.AsPointer(ref seen);

		public void Add(int key) {
			if (Unsafe.AsRef<ValueSet<int, DefaultKeyComparer<int>>>(_seen).Add(key))
				Unsafe.AsRef<SeedKeys<int>>(_seed).Add(key);
		}
	}

	private ref struct FilterSink(ref SeedKeys<int> seed, LastUpdatedIndex<int> index, long after) : IKeySink<int> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly LastUpdatedIndex<int> _index = index;
		private readonly long _after = after;

		public void Add(int key) {
			if (_index.TryGetLastUpdated(key, out var timestampMs) && timestampMs > _after)
				Unsafe.AsRef<SeedKeys<int>>(_seed).Add(key);
		}
	}

	private ref struct FilterDedupeSink(ref SeedKeys<int> seed, ref ValueSet<int, DefaultKeyComparer<int>> seen, LastUpdatedIndex<int> index, long after) : IKeySink<int> {
		private readonly void* _seed = Unsafe.AsPointer(ref seed);
		private readonly void* _seen = Unsafe.AsPointer(ref seen);
		private readonly LastUpdatedIndex<int> _index = index;
		private readonly long _after = after;

		public void Add(int key) {
			if (_index.TryGetLastUpdated(key, out var timestampMs) && timestampMs > _after
				&& Unsafe.AsRef<ValueSet<int, DefaultKeyComparer<int>>>(_seen).Add(key))
				Unsafe.AsRef<SeedKeys<int>>(_seed).Add(key);
		}
	}
}

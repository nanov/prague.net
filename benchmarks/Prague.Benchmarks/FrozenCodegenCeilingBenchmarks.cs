namespace Prague.Benchmarks;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;
using Prague.Core.Collections;
using Args = (int group, int band, int lane);
using Row = Prague.Core.JoinResult<PqbItem, PqbCustomer?>;
using Pair = (int Key, PqbItem Left, int Ordinal);
using Sorter = Prague.Core.SortResolver<int, PqbItem, PqbItem, PqbByScoreTies>;

/// <summary>
///   The codegen ceiling for production shape A (<c>ListListListSortBoundedJoinOne</c>: three list-index
///   narrowings → <c>SortBounded(page)</c> → <c>JoinOne</c>), task brief
///   <c>docs/superpowers/plans/2026-09-11-codegen-ceiling-task.md</c>, tracker #82. The <c>_Eager</c> and
///   <c>_Frozen</c> bodies are shape A's own (<see cref="FrozenQueryBenchmarks" />), on the same fixture
///   and arguments; the rest is, hand-written once, exactly what a source generator would emit for this
///   one <c>Prepare()…BuildFrozen()</c> call site, at the brief's four levels:
///   <list type="number">
///     <item><b>Level 1</b> — the whole plan as one straight line: the three buckets looked up, the
///       smallest seeded, the other two probed as field compares on the fetched value, the bounded page
///       selected with a comparer that calls the user comparer directly, the join one right-store
///       <c>TryGet</c> per page row written straight into the result. No step objects, no bindings, no
///       delegates, no resolver chain, no joined container.</item>
///     <item><b>Level 2</b> — the pipeline's structure kept (steps bound and probed through an interface,
///       a probe list, the frozen top-k container through the real <c>SortResolver</c>, the page
///       materialized into a <c>ValueDictionary</c> and filled in a second loop) with only the two
///       per-row costs a small generator change would target removed: the index <c>KeySelector</c>
///       delegate and the type-erased <c>StepBinding</c> read behind every value-side probe.</item>
///     <item><b>Level 3a / 3b</b> — level 1 with the join as the right store's hash lookup (3a: the level-1
///       method itself, measured twice so the pair also reads the noise floor) and as one array read over
///       a right-slot array built in <c>Setup</c> (3b: a writer-maintained materialized join).</item>
///     <item><b>Count, level 1</b> — the seed walk with the two field compares and no container.</item>
///   </list>
///   Every method executes pooled and disposes, allocates nothing per operation and returns the frozen
///   query's rows, <c>Count</c> and <c>TotalCount</c>; <see cref="Setup" /> asserts all of that once.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrozenCodegenCeilingBenchmarks {
	private const int N = 100_000;
	private const int Buckets = 100;
	private const int Skip = 20;
	private const int Take = 20;

	private InMemoryDataCache<int, PqbItem> _items = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byBand = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byLane = null!;
	private InMemoryDataCache<int, PqbCustomer> _details = null!;
	// 3b: the right side as a writer-maintained slot array — one PqbCustomer? per item id.
	private PqbCustomer?[] _rightSlots = null!;

	private FrozenQuery<Args, Row> _frozen = null!;
	// Level 2's step objects and sorter: what the generator would emit per UseIndex / SortBounded call.
	private ICeilingStep[] _steps = null!;
	private Sorter _sorter;
	private PqbByScoreTies _ties;

	// A field, not a constant, so no side gets a constant folded into the query.
	private Args _args = (13, 113, 413);

	[GlobalSetup]
	public void Setup() {
		_items = new();
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byBand = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Band);
		_byLane = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Lane);
		_details = new();
		var rng = new Random(1234);
		for (var i = 0; i < N; i++) {
			_items.AddOrUpdate(i, new PqbItem {
				Id = i, Code = 1000 + i, Group = i % Buckets, Tier = i % (Buckets * 10), Band = i % Buckets + Buckets * (i / 300 % 3), Lane = i % Buckets + Buckets * (i / 300 % 9),
				Flag = i % 3 == 0, Score = rng.Next(int.MaxValue),
			}, 1_000_000L + i);
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqbCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		_rightSlots = new PqbCustomer?[N];
		for (var i = 0; i < N; i++)
			_rightSlots[i] = _details.TryGet(i, out var right) ? right : null;

		_frozen = _items.Prepare<int, PqbItem, Args>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new PqbByScoreTies()).JoinOne(_details).BuildFrozen();
		_steps = [new GroupStep(_byGroup), new BandStep(_byBand), new LaneStep(_byLane)];
		_sorter = new(new PqbByScoreTies(), allowBounded: true);
		_ties = new();

		AssertParity();
	}

	// ── the reference pair, shape A's own rows ──────────────────────────────────

	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark(Baseline = true)]
	public int ShapeA_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _args.group).UseIndex(_byBand, _args.band).UseIndex(_byLane, _args.lane)
			.SortBounded(new PqbByScoreTies()).JoinOne(_details).ExecutePooled(Skip, Take);
		return r.Count;
	}

	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Frozen() {
		using var r = _frozen.ExecutePooled(_args, Skip, Take);
		return r.Count;
	}

	// ── the ceiling ─────────────────────────────────────────────────────────────

	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Level1() {
		using var r = StraightLine(in _args, Skip, Take, new StoreLookup(_details));
		return r.Count;
	}

	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Level2() {
		using var r = PipelinePieces(in _args, Skip, Take, new TopKSorterPairComparer<int, PqbItem, Sorter>(_sorter));
		return r.Count;
	}

	// The probe the ceiling's §3 left open: level 2 with the container's pair comparer calling the user
	// comparer directly instead of hopping IJoinResolver.CompareLeftValues on the sorter. Level2 minus this
	// row is the comparer hop on shape A's real page path.
	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Level2_DirectComparer() {
		using var r = PipelinePieces(in _args, Skip, Take, new TiesThenOrdinal(_ties));
		return r.Count;
	}

	// 3a is level 1 by the brief's definition (the join as today's hash lookups); the second measurement of
	// the same body is the run's noise-floor reading.
	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Level3a() {
		using var r = StraightLine(in _args, Skip, Take, new StoreLookup(_details));
		return r.Count;
	}

	[BenchmarkCategory("FrozenCodegenCeiling"), Benchmark]
	public int ShapeA_Level3b() {
		using var r = StraightLine(in _args, Skip, Take, new SlotLookup(_rightSlots));
		return r.Count;
	}

	// ── Count ───────────────────────────────────────────────────────────────────

	[BenchmarkCategory("Count_FrozenCodegenCeiling", "FrozenCodegenCeiling"), Benchmark(Baseline = true)]
	public int Count_Eager()
		=> _items.Query().UseIndex(_byGroup, _args.group).UseIndex(_byBand, _args.band).UseIndex(_byLane, _args.lane).SortBounded(new PqbByScoreTies()).JoinOne(_details).Count();

	[BenchmarkCategory("Count_FrozenCodegenCeiling", "FrozenCodegenCeiling"), Benchmark]
	public int Count_Frozen() => _frozen.Count(_args);

	[BenchmarkCategory("Count_FrozenCodegenCeiling", "FrozenCodegenCeiling"), Benchmark]
	public int Count_Level1() => CountStraightLine(in _args);

	// ── attribution probes: level 1 cut after each stage ──────────────────────

	// Bind, seed, the walk and the collect into the rented buffer; no page, no join, no result. Against
	// Count_Level1 this is the buffer rent and the 111 triple appends.
	[BenchmarkCategory("Probe_FrozenCodegenCeiling", "FrozenCodegenCeiling"), Benchmark]
	public int Probe_NarrowCollect() => Probe(in _args, Skip, Take, select: false);

	// As above plus the page selection; no join, no result. Against Probe_NarrowCollect this is the introselect
	// and its comparer calls; against ShapeA_Level1 the remainder is the join and the result buffer.
	[BenchmarkCategory("Probe_FrozenCodegenCeiling", "FrozenCodegenCeiling"), Benchmark]
	public int Probe_NarrowCollectSelect() => Probe(in _args, Skip, Take, select: true);

	// ── Level 1: the whole plan as one straight line ────────────────────────────

	// What the generator would emit for this call site. Plan decisions a generator cannot make at build —
	// which bucket is smallest, heap-or-collect for the page — stay as the same two compares the runtime
	// makes; everything else is fixed: the three indexes, the two field compares behind the probes, the
	// comparer type, the join family. SkipLocalsInit: the seed buffer is written before it is read.
	[SkipLocalsInit]
	private QueryResults<Row> StraightLine<TRight>(in Args args, int skip, int take, TRight right) where TRight : struct, IRightLookup {
		// Bind: one bucket lookup per step. A missing or empty bucket is an exact zero signal — the empty result.
		if (!_byGroup.TryGetBucket(args.group, out var groups) || !_byBand.TryGetBucket(args.band, out var bands) || !_byLane.TryGetBucket(args.lane, out var lanes))
			return QueryResults<Row>.Empty;
		var groupCount = groups.Count;
		var bandCount = bands.Count;
		var laneCount = lanes.Count;
		// The free seed: the smallest exact signal, ties to the earliest declared.
		var seed = groupCount <= bandCount ? groupCount <= laneCount ? 0 : 2 : bandCount <= laneCount ? 1 : 2;
		if ((seed == 0 ? groupCount : seed == 1 ? bandCount : laneCount) == 0)
			return QueryResults<Row>.Empty;

		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var keys = SeedKeys<int>.Over(stack);
		Pair[]? heap = null;
		try {
			(seed == 0 ? groups : seed == 1 ? bands : lanes).CopyKeysTo(ref keys);
			var seedKeys = keys.Keys;
			// The eager bounded container's plan choice: a heap of K for a small prefix, collect and select near the full size.
			var k = skip + take;
			var collectAll = k > 0 && (long)k * 4 >= seedKeys.Length;
			var capacity = collectAll ? seedKeys.Length : Math.Min(k, seedKeys.Length);
			if (capacity > 0)
				heap = PragueArrayPool<Pair>.Pool.Rent(capacity);
			var comparer = new TiesThenOrdinal(_ties);
			var count = 0;
			var heapified = false;
			var admitted = Narrow(in args, seed, seedKeys, heap, collectAll, k, ref count, ref heapified, comparer);

			// The page: introselect over everything collected, or the heap drained ascending.
			var page = 0;
			if (heap is not null) {
				if (collectAll) {
					page = TopKSelect.SelectPage(heap.AsSpan(0, count), skip, take, comparer);
				} else {
					var kept = TopKSelect.DrainAscending(heap, ref count, ref heapified, comparer);
					page = Math.Max(kept - skip, 0);
				}
			}

			if (page == 0)
				return QueryResults<Row>.EmptyWithTotalCount(admitted);

			// The join, one lookup per page row, written straight into the result: no joined container.
			var results = new QueryResults<Row>(page, true);
			for (var i = 0; i < page; i++) {
				ref readonly var pair = ref heap![skip + i];
				right.TryGet(pair.Key, out var rightValue);
				results.UnsafeAdd(new(pair.Left, rightValue));
			}

			results.UnsafeSetTotal(admitted);
			return results;
		} finally {
			if (heap is not null)
				PragueArrayPool<Pair>.Pool.Return(heap, true);
			keys.Dispose();
		}
	}

	// The walk: one loop per seed choice, the other two steps as field compares on the fetched value (the
	// value-side probe: the row is judged by the value it returns). Inlined into its callers, so each is the
	// straight line a generator would emit; returns the rows admitted — the eager Seal count.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int Narrow(in Args args, int seed, ReadOnlySpan<int> seedKeys, Pair[]? heap, bool collectAll, int k, ref int count, ref bool heapified, TiesThenOrdinal comparer) {
		var items = _items;
		var admitted = 0;
		switch (seed) {
			case 0:
				for (var i = 0; i < seedKeys.Length; i++) {
					var key = seedKeys[i];
					if (!items.TryGet(key, out var value) || value.Band != args.band || value.Lane != args.lane)
						continue;
					Feed(heap, collectAll, k, ref count, ref heapified, (key, value, admitted++), comparer);
				}

				break;
			case 1:
				for (var i = 0; i < seedKeys.Length; i++) {
					var key = seedKeys[i];
					if (!items.TryGet(key, out var value) || value.Group != args.group || value.Lane != args.lane)
						continue;
					Feed(heap, collectAll, k, ref count, ref heapified, (key, value, admitted++), comparer);
				}

				break;
			default:
				for (var i = 0; i < seedKeys.Length; i++) {
					var key = seedKeys[i];
					if (!items.TryGet(key, out var value) || value.Group != args.group || value.Band != args.band)
						continue;
					Feed(heap, collectAll, k, ref count, ref heapified, (key, value, admitted++), comparer);
				}

				break;
		}

		return admitted;
	}

	// Level 1 cut after the collect (select: false) or after the page selection (select: true): no join, no
	// result buffer. Returns the rows admitted or the page size, so nothing is dead code.
	[SkipLocalsInit]
	private int Probe(in Args args, int skip, int take, bool select) {
		if (!_byGroup.TryGetBucket(args.group, out var groups) || !_byBand.TryGetBucket(args.band, out var bands) || !_byLane.TryGetBucket(args.lane, out var lanes))
			return 0;
		var groupCount = groups.Count;
		var bandCount = bands.Count;
		var laneCount = lanes.Count;
		var seed = groupCount <= bandCount ? groupCount <= laneCount ? 0 : 2 : bandCount <= laneCount ? 1 : 2;
		if ((seed == 0 ? groupCount : seed == 1 ? bandCount : laneCount) == 0)
			return 0;

		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var keys = SeedKeys<int>.Over(stack);
		Pair[]? heap = null;
		try {
			(seed == 0 ? groups : seed == 1 ? bands : lanes).CopyKeysTo(ref keys);
			var seedKeys = keys.Keys;
			var k = skip + take;
			var collectAll = k > 0 && (long)k * 4 >= seedKeys.Length;
			var capacity = collectAll ? seedKeys.Length : Math.Min(k, seedKeys.Length);
			if (capacity > 0)
				heap = PragueArrayPool<Pair>.Pool.Rent(capacity);
			var comparer = new TiesThenOrdinal(_ties);
			var count = 0;
			var heapified = false;
			var admitted = Narrow(in args, seed, seedKeys, heap, collectAll, k, ref count, ref heapified, comparer);
			if (!select || heap is null)
				return admitted;
			if (collectAll)
				return TopKSelect.SelectPage(heap.AsSpan(0, count), skip, take, comparer);
			var kept = TopKSelect.DrainAscending(heap, ref count, ref heapified, comparer);
			return Math.Max(kept - skip, 0);
		} finally {
			if (heap is not null)
				PragueArrayPool<Pair>.Pool.Return(heap, true);
			keys.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Feed(Pair[]? heap, bool collectAll, int k, ref int count, ref bool heapified, Pair item, TiesThenOrdinal comparer) {
		if (heap is null)
			return;
		if (collectAll)
			heap[count++] = item;
		else
			TopKSelect.Push(heap, ref count, ref heapified, k, item, comparer);
	}

	// Count at level 1: the seed walk and the two field compares, no container. The frozen count runs the
	// same walk through the step interface (two value-side probes, each a KeySelector delegate call and a
	// binding read).
	[SkipLocalsInit]
	private int CountStraightLine(in Args args) {
		if (!_byGroup.TryGetBucket(args.group, out var groups) || !_byBand.TryGetBucket(args.band, out var bands) || !_byLane.TryGetBucket(args.lane, out var lanes))
			return 0;
		var groupCount = groups.Count;
		var bandCount = bands.Count;
		var laneCount = lanes.Count;
		var seed = groupCount <= bandCount ? groupCount <= laneCount ? 0 : 2 : bandCount <= laneCount ? 1 : 2;
		if ((seed == 0 ? groupCount : seed == 1 ? bandCount : laneCount) == 0)
			return 0;

		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var keys = SeedKeys<int>.Over(stack);
		try {
			(seed == 0 ? groups : seed == 1 ? bands : lanes).CopyKeysTo(ref keys);
			var seedKeys = keys.Keys;
			var items = _items;
			var count = 0;
			switch (seed) {
				case 0:
					for (var i = 0; i < seedKeys.Length; i++)
						if (items.TryGet(seedKeys[i], out var value) && value.Band == args.band && value.Lane == args.lane)
							count++;
					break;
				case 1:
					for (var i = 0; i < seedKeys.Length; i++)
						if (items.TryGet(seedKeys[i], out var value) && value.Group == args.group && value.Lane == args.lane)
							count++;
					break;
				default:
					for (var i = 0; i < seedKeys.Length; i++)
						if (items.TryGet(seedKeys[i], out var value) && value.Group == args.group && value.Band == args.band)
							count++;
					break;
			}

			return count;
		} finally {
			keys.Dispose();
		}
	}

	// ── Level 2: the pipeline's structure, the two per-row costs removed ────────

	// Bind through the step interface, choose the seed from the signals, probe through the interface over a
	// probe list, feed the real frozen top-k container through the real SortResolver, materialize the page
	// into a ValueDictionary and fill the right slots in a second loop — the frozen executor's shape. The
	// steps are generated: their probe is a field compare against the argument (no KeySelector delegate,
	// no type-erased binding read), and their bind reads the argument field (no selector delegate).
	[SkipLocalsInit]
	private QueryResults<Row> PipelinePieces<TPairComparer>(in Args args, int skip, int take, TPairComparer comparer)
		where TPairComparer : struct, IComparer<Pair> {
		var steps = _steps;
		var buckets = default(BucketBindings);
		var seed = -1;
		var signal = int.MaxValue;
		for (var i = 0; i < steps.Length; i++) {
			var bucket = steps[i].Bind(in args);
			buckets[i] = bucket;
			var count = bucket?.Count ?? 0;
			if (count < signal) {
				signal = count;
				seed = i;
			}
		}

		if (signal == 0)
			return QueryResults<Row>.Empty;
		Span<byte> probes = stackalloc byte[PipelineLimits.MaxSteps];
		var probeCount = 0;
		for (var i = 0; i < steps.Length; i++)
			if (i != seed)
				probes[probeCount++] = (byte)i;
		var probeList = probes[..probeCount];

		Span<long> stack = stackalloc long[PipelineLimits.SeedStackLongs];
		var keys = SeedKeys<int>.Over(stack);
		var topK = new FrozenTopKJoinedContainer<int, PqbItem, TPairComparer>(comparer, skip, take);
		var rows = default(ValueDictionary<int, Row, DefaultKeyComparer<int>>);
		var handedOff = false;
		try {
			buckets[seed]!.CopyKeysTo(ref keys);
			var seedKeys = keys.Keys;
			topK.Init(seedKeys.Length);
			var items = _items;
			var actual = 0;
			for (var i = 0; i < seedKeys.Length; i++) {
				var key = seedKeys[i];
				if (!items.TryGet(key, out var value) || !PassesValueProbes(steps, value, probeList, in args))
					continue;
				topK.Add(key, value);
				actual++;
			}

			topK.Seal(actual);
			var kept = topK.Drain();
			var page = Math.Max(kept - skip, 0);
			if (page == 0)
				return QueryResults<Row>.EmptyWithTotalCount(topK.TotalCount);

			// MaterializeTopK: the page rows into the keyed container in final order.
			rows = new(true, page);
			var buffer = topK.Buffer;
			for (var i = 0; i < page; i++) {
				ref readonly var pair = ref buffer[skip + i];
				ref var row = ref rows.GetValueRefOrAddDefault(pair.Key, out _);
				Unsafe.AsRef(in row.Left) = pair.Left;
			}

			// The fused fill: one loop over the rows, one right lookup per row, one slot write.
			var rowKeys = rows.Keys;
			var values = rows.ValuesMutable;
			var details = _details;
			for (var i = 0; i < rowKeys.Length; i++) {
				details.TryGet(rowKeys[i], out var rightValue);
				Unsafe.AsRef(in values[i].Right) = rightValue;
			}

			var results = QueryResults<Row>.FromArray(rows.ValuesArray, rows.Offset, rows.Count, topK.TotalCount, true);
			handedOff = true;
			return results;
		} finally {
			topK.Dispose();
			if (rows.IsInitialized)
				rows.Dispose(withValues: !handedOff);
			keys.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesValueProbes(ICeilingStep[] steps, PqbItem value, scoped ReadOnlySpan<byte> probes, in Args args) {
		for (var p = 0; p < probes.Length; p++)
			if (!steps[probes[p]].ProbeValue(value, in args))
				return false;
		return true;
	}

	// ── the Setup assertion ─────────────────────────────────────────────────────

	private void AssertParity() {
		using var reference = _frozen.ExecutePooled(_args, Skip, Take);
		AssertSameRows(in reference, StraightLine(in _args, Skip, Take, new StoreLookup(_details)), "level 1");
		AssertSameRows(in reference, PipelinePieces(in _args, Skip, Take, new TopKSorterPairComparer<int, PqbItem, Sorter>(_sorter)), "level 2");
		AssertSameRows(in reference, PipelinePieces(in _args, Skip, Take, new TiesThenOrdinal(_ties)), "level 2, direct comparer");
		AssertSameRows(in reference, StraightLine(in _args, Skip, Take, new SlotLookup(_rightSlots)), "level 3b");
		var expectedCount = _frozen.Count(_args);
		var actualCount = CountStraightLine(in _args);
		if (actualCount != expectedCount)
			throw new InvalidOperationException($"Count level 1 returned {actualCount}, the frozen query {expectedCount}.");
		if (reference.Count == 0 || reference.TotalCount != expectedCount)
			throw new InvalidOperationException($"The fixture no longer produces shape A's page: Count {reference.Count}, TotalCount {reference.TotalCount}, Count() {expectedCount}.");

		// Every ceiling body is 0 B per operation once the pools are warm.
		AssertZeroAlloc(() => ShapeA_Level1(), "level 1");
		AssertZeroAlloc(() => ShapeA_Level2(), "level 2");
		AssertZeroAlloc(() => ShapeA_Level2_DirectComparer(), "level 2, direct comparer");
		AssertZeroAlloc(() => ShapeA_Level3b(), "level 3b");
		AssertZeroAlloc(() => Count_Level1(), "Count level 1");
		AssertZeroAlloc(() => Probe_NarrowCollect(), "probe: narrow + collect");
		AssertZeroAlloc(() => Probe_NarrowCollectSelect(), "probe: narrow + collect + select");
		if (Probe_NarrowCollect() != expectedCount || Probe_NarrowCollectSelect() != reference.Count)
			throw new InvalidOperationException("The attribution probes do not agree with the frozen query's TotalCount / page.");
	}

	private static void AssertSameRows(in QueryResults<Row> expected, QueryResults<Row> actual, string level) {
		using (actual) {
			if (actual.Count != expected.Count || actual.TotalCount != expected.TotalCount)
				throw new InvalidOperationException($"{level}: Count {actual.Count} / TotalCount {actual.TotalCount}, the frozen query {expected.Count} / {expected.TotalCount}.");
			// The comparer ties, so a tie may reorder: compare the multiset of left keys, and each row's right against its own left.
			var expectedKeys = new int[expected.Count];
			var actualKeys = new int[actual.Count];
			for (var i = 0; i < expected.Count; i++) {
				expectedKeys[i] = expected[i].Left.Id;
				actualKeys[i] = actual[i].Left.Id;
				var row = actual[i];
				var rightOk = row.Left.Id % 4 == 0 ? row.Right is null : row.Right is { } right && right.Id == row.Left.Id;
				if (!rightOk)
					throw new InvalidOperationException($"{level}: row {i} (left {row.Left.Id}) carries the wrong right.");
			}

			Array.Sort(expectedKeys);
			Array.Sort(actualKeys);
			if (!expectedKeys.AsSpan().SequenceEqual(actualKeys))
				throw new InvalidOperationException($"{level}: the page holds different rows than the frozen query.");
		}
	}

	private static void AssertZeroAlloc(Func<int> body, string level) {
		for (var i = 0; i < 32; i++)
			body();
		var before = GC.GetAllocatedBytesForCurrentThread();
		body();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		if (allocated != 0)
			throw new InvalidOperationException($"{level} allocated {allocated} B per operation.");
	}

	// ── what the generator would emit ───────────────────────────────────────────

	/// <summary>Orders the page by the user comparer, then by encounter ordinal — the bounded tie rule — calling the comparer directly.</summary>
	private readonly struct TiesThenOrdinal(PqbByScoreTies ties) : IComparer<Pair> {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(Pair x, Pair y) {
			var order = ties.Compare(x.Left, y.Left);
			return order != 0 ? order : x.Ordinal.CompareTo(y.Ordinal);
		}
	}

	private interface IRightLookup {
		bool TryGet(int key, out PqbCustomer? right);
	}

	/// <summary>3a: the join as today — the right store's hash lookup.</summary>
	private readonly struct StoreLookup(InMemoryDataCache<int, PqbCustomer> store) : IRightLookup {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGet(int key, out PqbCustomer? right) {
			var found = store.TryGet(key, out var value);
			right = value;
			return found;
		}
	}

	/// <summary>3b: the join as one array read over a writer-maintained right-slot array.</summary>
	private readonly struct SlotLookup(PqbCustomer?[] slots) : IRightLookup {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGet(int key, out PqbCustomer? right) {
			right = slots[key];
			return right is not null;
		}
	}

	/// <summary>Level 2's step: bound and probed through an interface as today, but the argument is read as a field and the probe is a field compare.</summary>
	private interface ICeilingStep {
		PooledSet<int, DefaultKeyComparer<int>>? Bind(in Args args);

		bool ProbeValue(PqbItem value, in Args args);
	}

	private sealed class GroupStep(CacheKeyValueListIndex<int, PqbItem, int> index) : ICeilingStep {
		public PooledSet<int, DefaultKeyComparer<int>>? Bind(in Args args) => index.TryGetBucket(args.group, out var bucket) ? bucket : null;

		public bool ProbeValue(PqbItem value, in Args args) => value.Group == args.group;
	}

	private sealed class BandStep(CacheKeyValueListIndex<int, PqbItem, int> index) : ICeilingStep {
		public PooledSet<int, DefaultKeyComparer<int>>? Bind(in Args args) => index.TryGetBucket(args.band, out var bucket) ? bucket : null;

		public bool ProbeValue(PqbItem value, in Args args) => value.Band == args.band;
	}

	private sealed class LaneStep(CacheKeyValueListIndex<int, PqbItem, int> index) : ICeilingStep {
		public PooledSet<int, DefaultKeyComparer<int>>? Bind(in Args args) => index.TryGetBucket(args.lane, out var bucket) ? bucket : null;

		public bool ProbeValue(PqbItem value, in Args args) => value.Lane == args.lane;
	}

	/// <summary>One bucket reference per step for the execution — the typed twin of the frame's bindings.</summary>
	[InlineArray(3)]
	private struct BucketBindings {
		private PooledSet<int, DefaultKeyComparer<int>>? _element0;
	}
}

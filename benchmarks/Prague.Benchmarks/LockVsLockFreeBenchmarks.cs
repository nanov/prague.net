namespace Prague.Benchmarks;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Prague.Core.Collections;

/// <summary>
///   The frozen pipeline's three read primitives (seed copy-out, bucket probe, store lookup) and
///   their composition, modelled side by side on the engine's lock-free structures
///   (<see cref="PooledSet{T,TKeyComparer}" /> under a <see cref="ReaderGate" /> pin,
///   <see cref="ConcurrentCacheStore{TKey,TValue}" />) and on plain BCL collections behind a
///   <see cref="Lock" /> (a <see cref="HashSet{T}" /> / <see cref="List{T}" /> bucket copied out
///   under the lock, a <see cref="Dictionary{TKey,TValue}" /> store read under the lock, per call
///   and batched). 100k store rows, a 1k / 300 / 100 seed bucket, a 300-key probe bucket that hits
///   30% of the seed. The measurement backs the "plain collections + one lock per operation could
///   beat the lock-free machinery" question — see RESULTS.MD, "Lock vs lock-free".
///
///   Single-reader rows: <see cref="LockVsLockFreeBenchmarks" /> (BDN, `--inProcess`).
///   8 readers ± 1 writer: <see cref="LockVsLockFreeConcurrencyBenchmarks" /> (wall-clock harness
///   hosted in BDN like <see cref="HeavyJoinPooledBenchmarks" />; aggregate reads/s, per-pass
///   p50 / p99, writer ops/s — median and spread over the runs).
///
///   Run with: dotnet run -c Release -- --filter "*LockVsLockFree*" --inProcess
/// </summary>
internal sealed class LockVsLockFreeWorld {
	public const int StoreRows = 100_000;
	public const int SeedStride = 100; // seed key i is i * SeedStride: the seed spans the whole store
	public const int SeedSize1k = 1000;
	public const int ProbeSize = 300;
	// Seed keys plus slack: a concurrent remove + re-add can surface one key twice in a walk.
	public const int BufferSize = SeedSize1k + 128;

	private static readonly Func<int, PqbItem, PqbItem, bool> AlwaysUpdate = static (_, _, _) => true;

	public readonly PooledSet<int, DefaultKeyComparer<int>> Seed1k = new(default, SeedSize1k);
	public readonly PooledSet<int, DefaultKeyComparer<int>> Seed300 = new(default, 300);
	public readonly PooledSet<int, DefaultKeyComparer<int>> Seed100 = new(default, 100);
	public readonly PooledSet<int, DefaultKeyComparer<int>> Probe300 = new(default, ProbeSize);
	public readonly ConcurrentCacheStore<int, PqbItem> Store = new(Environment.ProcessorCount, StoreRows);

	public readonly HashSet<int> SeedSet1k = new(SeedSize1k);
	public readonly HashSet<int> SeedSet300 = new(300);
	public readonly HashSet<int> SeedSet100 = new(100);
	public readonly HashSet<int> ProbeSet300 = new(ProbeSize);
	public readonly List<int> SeedList1k = new(SeedSize1k);
	public readonly List<int> SeedList300 = new(300);
	public readonly List<int> SeedList100 = new(100);
	public readonly Dictionary<int, PqbItem> Dict = new(StoreRows);

	public readonly Lock SeedLock = new();
	public readonly Lock ProbeLock = new();
	public readonly Lock StoreLock = new();
	public SpinLock SeedSpinLock = new(false);

	public readonly int[] SeedKeys1k = new int[SeedSize1k];
	public readonly int[] ProbeKeys = new int[ProbeSize];
	// Two alternating payloads per seed key so the writer never allocates while measuring.
	public readonly PqbItem[] PayloadA = new PqbItem[SeedSize1k];
	public readonly PqbItem[] PayloadB = new PqbItem[SeedSize1k];

	public LockVsLockFreeWorld() {
		for (var id = 0; id < StoreRows; id++) {
			var item = new PqbItem { Id = id, Code = 1000 + id, Group = id % 100, Score = id & 7 };
			Store.AddOrUpdate(id, item, AlwaysUpdate);
			Dict[id] = item;
		}

		var p = 0;
		for (var i = 0; i < SeedSize1k; i++) {
			var key = i * SeedStride;
			SeedKeys1k[i] = key;
			Seed1k.Add(key);
			SeedSet1k.Add(key);
			SeedList1k.Add(key);
			if (i < 300) {
				Seed300.Add(key);
				SeedSet300.Add(key);
				SeedList300.Add(key);
			}

			if (i < 100) {
				Seed100.Add(key);
				SeedSet100.Add(key);
				SeedList100.Add(key);
			}

			if (i % 10 < 3) {
				ProbeKeys[p++] = key;
				Probe300.Add(key);
				ProbeSet300.Add(key);
			}

			PayloadA[i] = new PqbItem { Id = key, Code = 1000 + key, Group = key % 100, Score = 1 };
			PayloadB[i] = new PqbItem { Id = key, Code = 1000 + key, Group = key % 100, Score = 2 };
		}
	}

	// ───────────────────── 1. seed copy-out ─────────────────────
	// Every locked form is `lock { return Core(...) }` with the loop in a NoInlining core: a loop
	// written inside the lock's try region pays the JIT's EH-write-thru spills on its locals
	// (measured: the Dictionary batch at 12.4 µs vs 9.6 µs per-call before this split). The split is
	// the same one PooledSet.Contains uses for its gate wrapper, so both sides get the same codegen.

	public static int CopySeedPooled(PooledSet<int, DefaultKeyComparer<int>> seed, Span<int> into) {
		var n = 0;
		foreach (var key in seed) {
			if (n == into.Length)
				break;

			into[n++] = key;
		}

		return n;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CopySeedHashSetCore(HashSet<int> seed, Span<int> into) {
		var n = 0;
		foreach (var key in seed) {
			if (n == into.Length)
				break;

			into[n++] = key;
		}

		return n;
	}

	public static int CopySeedHashSetLocked(HashSet<int> seed, Lock gate, Span<int> into) {
		lock (gate)
			return CopySeedHashSetCore(seed, into);
	}

	public static int CopySeedHashSetSpinLocked(HashSet<int> seed, ref SpinLock gate, Span<int> into) {
		var taken = false;
		try {
			gate.Enter(ref taken);
			return CopySeedHashSetCore(seed, into);
		} finally {
			if (taken)
				gate.Exit(false);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CopySeedListCore(List<int> seed, Span<int> into) {
		var span = CollectionsMarshal.AsSpan(seed);
		var n = Math.Min(span.Length, into.Length);
		span[..n].CopyTo(into);
		return n;
	}

	public static int CopySeedListLocked(List<int> seed, Lock gate, Span<int> into) {
		lock (gate)
			return CopySeedListCore(seed, into);
	}

	// ───────────────────── 2. probe ─────────────────────

	public static int ProbePooled(ReadOnlySpan<int> keys, PooledSet<int, DefaultKeyComparer<int>> probe, Span<int> hits) {
		var h = 0;
		foreach (var key in keys)
			if (probe.Contains(key))
				hits[h++] = key;

		return h;
	}

	public static int ProbeHashSetLockPerCall(ReadOnlySpan<int> keys, HashSet<int> probe, Lock gate, Span<int> hits) {
		var h = 0;
		foreach (var key in keys) {
			bool hit;
			lock (gate)
				hit = probe.Contains(key);

			if (hit)
				hits[h++] = key;
		}

		return h;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ProbeHashSetCore(ReadOnlySpan<int> keys, HashSet<int> probe, Span<int> hits) {
		var h = 0;
		foreach (var key in keys)
			if (probe.Contains(key))
				hits[h++] = key;

		return h;
	}

	public static int ProbeHashSetLockBatch(ReadOnlySpan<int> keys, HashSet<int> probe, Lock gate, Span<int> hits) {
		lock (gate)
			return ProbeHashSetCore(keys, probe, hits);
	}

	// ───────────────────── 3. store lookup ─────────────────────

	public static int GetStore(ReadOnlySpan<int> keys, ConcurrentCacheStore<int, PqbItem> store) {
		var sum = 0;
		foreach (var key in keys)
			if (store.TryGetValue(key, out var value))
				sum += value.Score;

		return sum;
	}

	public static int GetDictLockPerCall(ReadOnlySpan<int> keys, Dictionary<int, PqbItem> dict, Lock gate) {
		var sum = 0;
		foreach (var key in keys) {
			PqbItem? value;
			bool found;
			lock (gate)
				found = dict.TryGetValue(key, out value);

			if (found)
				sum += value!.Score;
		}

		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GetDictCore(ReadOnlySpan<int> keys, Dictionary<int, PqbItem> dict) {
		var sum = 0;
		foreach (var key in keys)
			if (dict.TryGetValue(key, out var value))
				sum += value.Score;

		return sum;
	}

	public static int GetDictLockBatch(ReadOnlySpan<int> keys, Dictionary<int, PqbItem> dict, Lock gate) {
		lock (gate)
			return GetDictCore(keys, dict);
	}

	// ───────────────────── 4. composed pass: seed → probe → store ─────────────────────

	public int ComposedLockFree(PooledSet<int, DefaultKeyComparer<int>> seed, Span<int> seedBuf, Span<int> hitBuf) {
		var n = CopySeedPooled(seed, seedBuf);
		var h = ProbePooled(seedBuf[..n], Probe300, hitBuf);
		return GetStore(hitBuf[..h], Store);
	}

	public int ComposedHashSetLockBatch(HashSet<int> seed, Span<int> seedBuf, Span<int> hitBuf) {
		var n = CopySeedHashSetLocked(seed, SeedLock, seedBuf);
		var h = ProbeHashSetLockBatch(seedBuf[..n], ProbeSet300, ProbeLock, hitBuf);
		return GetDictLockBatch(hitBuf[..h], Dict, StoreLock);
	}

	public int ComposedListLockBatch(List<int> seed, Span<int> seedBuf, Span<int> hitBuf) {
		var n = CopySeedListLocked(seed, SeedLock, seedBuf);
		var h = ProbeHashSetLockBatch(seedBuf[..n], ProbeSet300, ProbeLock, hitBuf);
		return GetDictLockBatch(hitBuf[..h], Dict, StoreLock);
	}

	// ───────────────────── writer churn (S3) ─────────────────────
	// One iteration = remove + re-add one seed key, remove + re-add one probe key, overwrite the
	// seed key's store row: five mutating calls, each counted as one writer op.

	public int WriteLockFree(int i) {
		var key = SeedKeys1k[i % SeedSize1k];
		var probeKey = ProbeKeys[i % ProbeSize];
		Seed1k.Remove(key);
		Seed1k.Add(key);
		Probe300.Remove(probeKey);
		Probe300.Add(probeKey);
		Store.AddOrUpdate(key, (i & 1) == 0 ? PayloadA[i % SeedSize1k] : PayloadB[i % SeedSize1k], AlwaysUpdate);
		return 5;
	}

	public int WriteLockedHashSet(int i) {
		var key = SeedKeys1k[i % SeedSize1k];
		var probeKey = ProbeKeys[i % ProbeSize];
		lock (SeedLock)
			SeedSet1k.Remove(key);
		lock (SeedLock)
			SeedSet1k.Add(key);
		lock (ProbeLock)
			ProbeSet300.Remove(probeKey);
		lock (ProbeLock)
			ProbeSet300.Add(probeKey);
		lock (StoreLock)
			Dict[key] = (i & 1) == 0 ? PayloadA[i % SeedSize1k] : PayloadB[i % SeedSize1k];
		return 5;
	}

	public int WriteLockedList(int i) {
		var key = SeedKeys1k[i % SeedSize1k];
		var probeKey = ProbeKeys[i % ProbeSize];
		// A plain array bucket has no O(1) remove: the writer pays the IndexOf scan under the lock.
		lock (SeedLock)
			SeedList1k.Remove(key);
		lock (SeedLock)
			SeedList1k.Add(key);
		lock (ProbeLock)
			ProbeSet300.Remove(probeKey);
		lock (ProbeLock)
			ProbeSet300.Add(probeKey);
		lock (StoreLock)
			Dict[key] = (i & 1) == 0 ? PayloadA[i % SeedSize1k] : PayloadB[i % SeedSize1k];
		return 5;
	}

	public int WriteLockedSpinHashSet(int i) {
		var key = SeedKeys1k[i % SeedSize1k];
		var taken = false;
		try {
			SeedSpinLock.Enter(ref taken);
			SeedSet1k.Remove(key);
		} finally {
			if (taken)
				SeedSpinLock.Exit(false);
		}

		taken = false;
		try {
			SeedSpinLock.Enter(ref taken);
			SeedSet1k.Add(key);
		} finally {
			if (taken)
				SeedSpinLock.Exit(false);
		}

		return 2;
	}

	public void Dispose() {
		Seed1k.Dispose();
		Seed300.Dispose();
		Seed100.Dispose();
		Probe300.Dispose();
	}
}

/// <summary>Single reader, no writer (S1). Baseline per category = the engine's lock-free form.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.Declared)]
public class LockVsLockFreeBenchmarks {
	private LockVsLockFreeWorld _world = null!;
	private PooledSet<int, DefaultKeyComparer<int>> _seed = null!;
	private HashSet<int> _seedSet = null!;
	private List<int> _seedList = null!;
	private int[] _keys = null!;
	private readonly int[] _seedBuffer = new int[LockVsLockFreeWorld.BufferSize];
	private readonly int[] _hitBuffer = new int[LockVsLockFreeWorld.BufferSize];

	[Params(1000, 300, 100)] public int SeedSize { get; set; }

	[GlobalSetup]
	public void Setup() {
		_world = new LockVsLockFreeWorld();
		(_seed, _seedSet, _seedList) = SeedSize switch {
			1000 => (_world.Seed1k, _world.SeedSet1k, _world.SeedList1k),
			300 => (_world.Seed300, _world.SeedSet300, _world.SeedList300),
			_ => (_world.Seed100, _world.SeedSet100, _world.SeedList100),
		};
		_keys = _world.SeedKeys1k[..SeedSize];
	}

	[GlobalCleanup]
	public void Cleanup() => _world.Dispose();

	// 1. seed copy-out (SeedSize keys)

	[BenchmarkCategory("SeedCopy"), Benchmark(Baseline = true)]
	public int SeedCopy_PooledSet() => LockVsLockFreeWorld.CopySeedPooled(_seed, _seedBuffer);

	[BenchmarkCategory("SeedCopy"), Benchmark]
	public int SeedCopy_HashSet_Lock() => LockVsLockFreeWorld.CopySeedHashSetLocked(_seedSet, _world.SeedLock, _seedBuffer);

	[BenchmarkCategory("SeedCopy"), Benchmark]
	public int SeedCopy_HashSet_SpinLock() => LockVsLockFreeWorld.CopySeedHashSetSpinLocked(_seedSet, ref _world.SeedSpinLock, _seedBuffer);

	[BenchmarkCategory("SeedCopy"), Benchmark]
	public int SeedCopy_List_Lock() => LockVsLockFreeWorld.CopySeedListLocked(_seedList, _world.SeedLock, _seedBuffer);

	// 2. probe SeedSize keys against the 300-bucket (30% hit)

	[BenchmarkCategory("Probe"), Benchmark(Baseline = true)]
	public int Probe_PooledSet() => LockVsLockFreeWorld.ProbePooled(_keys, _world.Probe300, _hitBuffer);

	[BenchmarkCategory("Probe"), Benchmark]
	public int Probe_HashSet_LockPerCall() => LockVsLockFreeWorld.ProbeHashSetLockPerCall(_keys, _world.ProbeSet300, _world.ProbeLock, _hitBuffer);

	[BenchmarkCategory("Probe"), Benchmark]
	public int Probe_HashSet_LockBatch() => LockVsLockFreeWorld.ProbeHashSetLockBatch(_keys, _world.ProbeSet300, _world.ProbeLock, _hitBuffer);

	// 3. store lookup of SeedSize keys (all hit)

	[BenchmarkCategory("Store"), Benchmark(Baseline = true)]
	public int Store_ConcurrentCacheStore() => LockVsLockFreeWorld.GetStore(_keys, _world.Store);

	[BenchmarkCategory("Store"), Benchmark]
	public int Store_Dictionary_LockPerCall() => LockVsLockFreeWorld.GetDictLockPerCall(_keys, _world.Dict, _world.StoreLock);

	[BenchmarkCategory("Store"), Benchmark]
	public int Store_Dictionary_LockBatch() => LockVsLockFreeWorld.GetDictLockBatch(_keys, _world.Dict, _world.StoreLock);

	// 4. composed: copy the seed → probe every key against the 300-bucket → fetch the hits

	[BenchmarkCategory("Composed"), Benchmark(Baseline = true)]
	public int Composed_LockFree() => _world.ComposedLockFree(_seed, _seedBuffer, _hitBuffer);

	[BenchmarkCategory("Composed"), Benchmark]
	public int Composed_HashSet_LockBatch() => _world.ComposedHashSetLockBatch(_seedSet, _seedBuffer, _hitBuffer);

	[BenchmarkCategory("Composed"), Benchmark]
	public int Composed_List_LockBatch() => _world.ComposedListLockBatch(_seedList, _seedBuffer, _hitBuffer);
}

/// <summary>
///   8 readers, no writer (S2, <c>Writers = 0</c>) and 8 readers + 1 writer at max rate (S3,
///   <c>Writers = 1</c>). Every reader runs one pass in a loop for <see cref="DurationMs" /> and
///   stamps each pass with <see cref="Stopwatch" />; the columns report aggregate passes/s, the
///   merged per-pass p50 / p99 and the writer's ops/s, each as the median over the last five runs
///   with the min–max spread. The writer churns the 1k seed, the 300 probe and the store rows the
///   readers are reading (single-writer contract on the lock-free side).
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
[HideColumns(Column.Error, Column.StdDev, Column.Gen0, Column.Gen1, Column.Gen2, Column.Allocated)]
[Config(typeof(Config))]
public class LockVsLockFreeConcurrencyBenchmarks {
	public const int ReaderThreads = 8; // M4 Pro: 8 performance cores; the writer takes a ninth
	public const int DurationMs = 2000;
	private const int SampleCap = 1 << 19; // per reader; passes past the cap are counted, not sampled
	private const int ReportedRuns = 5;

	public static readonly ConcurrentDictionary<string, List<RunSample>> Results = new();

	private LockVsLockFreeWorld _world = null!;
	private int[][] _seedBuffers = null!;
	private int[][] _hitBuffers = null!;
	private long[][] _samples = null!;
	private long[] _merged = null!;
	private long[] _passes = null!;
	private int[] _recorded = null!;
	private int[] _sinks = null!;
	private readonly ManualResetEventSlim _start = new(false);
	private volatile bool _running;

	[Params(0, 1)] public int Writers { get; set; }

	public readonly record struct RunSample(double ReadsPerSec, double P50Us, double P99Us, double WriterOpsPerSec);

	[GlobalSetup]
	public void Setup() {
		_world = new LockVsLockFreeWorld();
		_seedBuffers = new int[ReaderThreads][];
		_hitBuffers = new int[ReaderThreads][];
		_samples = new long[ReaderThreads][];
		for (var i = 0; i < ReaderThreads; i++) {
			_seedBuffers[i] = new int[LockVsLockFreeWorld.BufferSize];
			_hitBuffers[i] = new int[LockVsLockFreeWorld.BufferSize];
			_samples[i] = new long[SampleCap];
		}

		_merged = new long[ReaderThreads * SampleCap];
		_passes = new long[ReaderThreads];
		_recorded = new int[ReaderThreads];
		_sinks = new int[ReaderThreads];
	}

	[GlobalCleanup]
	public void Cleanup() => _world.Dispose();

	// 1. seed copy-out (1k)

	[Benchmark(Baseline = true)]
	public string SeedCopy_PooledSet() => Run<SeedCopyPooledPass>(nameof(SeedCopy_PooledSet), WriterKind.LockFree);

	[Benchmark]
	public string SeedCopy_HashSet_Lock() => Run<SeedCopyHashSetLockPass>(nameof(SeedCopy_HashSet_Lock), WriterKind.LockedHashSet);

	[Benchmark]
	public string SeedCopy_HashSet_SpinLock() => Run<SeedCopyHashSetSpinLockPass>(nameof(SeedCopy_HashSet_SpinLock), WriterKind.SpinLockedHashSet);

	[Benchmark]
	public string SeedCopy_List_Lock() => Run<SeedCopyListLockPass>(nameof(SeedCopy_List_Lock), WriterKind.LockedList);

	// 2. probe 1k keys against the 300-bucket

	[Benchmark]
	public string Probe_PooledSet() => Run<ProbePooledPass>(nameof(Probe_PooledSet), WriterKind.LockFree);

	[Benchmark]
	public string Probe_HashSet_LockPerCall() => Run<ProbeHashSetLockPerCallPass>(nameof(Probe_HashSet_LockPerCall), WriterKind.LockedHashSet);

	[Benchmark]
	public string Probe_HashSet_LockBatch() => Run<ProbeHashSetLockBatchPass>(nameof(Probe_HashSet_LockBatch), WriterKind.LockedHashSet);

	// 3. store lookup of 1k keys

	[Benchmark]
	public string Store_ConcurrentCacheStore() => Run<StoreGetPass>(nameof(Store_ConcurrentCacheStore), WriterKind.LockFree);

	[Benchmark]
	public string Store_Dictionary_LockPerCall() => Run<DictLockPerCallPass>(nameof(Store_Dictionary_LockPerCall), WriterKind.LockedHashSet);

	[Benchmark]
	public string Store_Dictionary_LockBatch() => Run<DictLockBatchPass>(nameof(Store_Dictionary_LockBatch), WriterKind.LockedHashSet);

	// 4. composed pass

	[Benchmark]
	public string Composed_LockFree() => Run<ComposedLockFreePass>(nameof(Composed_LockFree), WriterKind.LockFree);

	[Benchmark]
	public string Composed_HashSet_LockBatch() => Run<ComposedHashSetLockBatchPass>(nameof(Composed_HashSet_LockBatch), WriterKind.LockedHashSet);

	[Benchmark]
	public string Composed_List_LockBatch() => Run<ComposedListLockBatchPass>(nameof(Composed_List_LockBatch), WriterKind.LockedList);

	private string Run<TPass>(string name, WriterKind writerKind) where TPass : struct, IPass {
		Array.Clear(_passes);
		Array.Clear(_recorded);
		_start.Reset();
		_running = true;

		var readers = new Thread[ReaderThreads];
		for (var i = 0; i < ReaderThreads; i++) {
			var index = i;
			readers[i] = new Thread(() => ReaderLoop<TPass>(index)) { IsBackground = true };
			readers[i].Start();
		}

		long writerOps = 0;
		Thread? writer = null;
		if (Writers > 0) {
			writer = new Thread(() => writerOps = WriterLoop(writerKind)) { IsBackground = true };
			writer.Start();
		}

		var stopwatch = Stopwatch.StartNew();
		_start.Set();
		Thread.Sleep(DurationMs);
		_running = false;
		foreach (var reader in readers)
			reader.Join();
		writer?.Join();
		stopwatch.Stop();

		var seconds = stopwatch.Elapsed.TotalSeconds;
		long passes = 0;
		var merged = 0;
		for (var i = 0; i < ReaderThreads; i++) {
			passes += _passes[i];
			Array.Copy(_samples[i], 0, _merged, merged, _recorded[i]);
			merged += _recorded[i];
		}

		Array.Sort(_merged, 0, merged);
		var toMicros = 1_000_000.0 / Stopwatch.Frequency;
		var sample = new RunSample(
			passes / seconds,
			_merged[(int)(merged * 0.50)] * toMicros,
			_merged[(int)(merged * 0.99)] * toMicros,
			writerOps / seconds);
		Results.GetOrAdd(Key(name, Writers), static _ => new List<RunSample>()).Add(sample);
		return $"R/s:{sample.ReadsPerSec:N0} p50:{sample.P50Us:F2}us p99:{sample.P99Us:F2}us W/s:{sample.WriterOpsPerSec:N0}";
	}

	private void ReaderLoop<TPass>(int index) where TPass : struct, IPass {
		var seedBuf = _seedBuffers[index];
		var hitBuf = _hitBuffers[index];
		var samples = _samples[index];
		var world = _world;
		long passes = 0;
		var recorded = 0;
		var sink = 0;
		_start.Wait();
		while (_running) {
			var t0 = Stopwatch.GetTimestamp();
			sink += TPass.Run(world, seedBuf, hitBuf);
			var dt = Stopwatch.GetTimestamp() - t0;
			if (recorded < samples.Length)
				samples[recorded++] = dt;
			passes++;
		}

		_passes[index] = passes;
		_recorded[index] = recorded;
		_sinks[index] = sink;
	}

	private long WriterLoop(WriterKind kind) {
		var world = _world;
		long ops = 0;
		var i = 0;
		_start.Wait();
		switch (kind) {
			case WriterKind.LockFree:
				while (_running)
					ops += world.WriteLockFree(i++);
				break;
			case WriterKind.LockedHashSet:
				while (_running)
					ops += world.WriteLockedHashSet(i++);
				break;
			case WriterKind.LockedList:
				while (_running)
					ops += world.WriteLockedList(i++);
				break;
			case WriterKind.SpinLockedHashSet:
				while (_running)
					ops += world.WriteLockedSpinHashSet(i++);
				break;
		}

		return ops;
	}

	private static string Key(string name, int writers) => name + "|W" + writers;

	private enum WriterKind {
		LockFree,
		LockedHashSet,
		LockedList,
		SpinLockedHashSet,
	}

	private interface IPass {
		static abstract int Run(LockVsLockFreeWorld world, Span<int> seedBuf, Span<int> hitBuf);
	}

	private readonly struct SeedCopyPooledPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.CopySeedPooled(w.Seed1k, seedBuf);
	}

	private readonly struct SeedCopyHashSetLockPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.CopySeedHashSetLocked(w.SeedSet1k, w.SeedLock, seedBuf);
	}

	private readonly struct SeedCopyHashSetSpinLockPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.CopySeedHashSetSpinLocked(w.SeedSet1k, ref w.SeedSpinLock, seedBuf);
	}

	private readonly struct SeedCopyListLockPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.CopySeedListLocked(w.SeedList1k, w.SeedLock, seedBuf);
	}

	private readonly struct ProbePooledPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.ProbePooled(w.SeedKeys1k, w.Probe300, hitBuf);
	}

	private readonly struct ProbeHashSetLockPerCallPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.ProbeHashSetLockPerCall(w.SeedKeys1k, w.ProbeSet300, w.ProbeLock, hitBuf);
	}

	private readonly struct ProbeHashSetLockBatchPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.ProbeHashSetLockBatch(w.SeedKeys1k, w.ProbeSet300, w.ProbeLock, hitBuf);
	}

	private readonly struct StoreGetPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.GetStore(w.SeedKeys1k, w.Store);
	}

	private readonly struct DictLockPerCallPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.GetDictLockPerCall(w.SeedKeys1k, w.Dict, w.StoreLock);
	}

	private readonly struct DictLockBatchPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => LockVsLockFreeWorld.GetDictLockBatch(w.SeedKeys1k, w.Dict, w.StoreLock);
	}

	private readonly struct ComposedLockFreePass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => w.ComposedLockFree(w.Seed1k, seedBuf, hitBuf);
	}

	private readonly struct ComposedHashSetLockBatchPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => w.ComposedHashSetLockBatch(w.SeedSet1k, seedBuf, hitBuf);
	}

	private readonly struct ComposedListLockBatchPass : IPass {
		public static int Run(LockVsLockFreeWorld w, Span<int> seedBuf, Span<int> hitBuf) => w.ComposedListLockBatch(w.SeedList1k, seedBuf, hitBuf);
	}

	private class Config : ManualConfig {
		public Config() {
			AddJob(Job.Default
				.WithToolchain(InProcessNoEmitToolchain.Instance)
				.WithInvocationCount(1)
				.WithUnrollFactor(1)
				.WithWarmupCount(1)
				.WithIterationCount(ReportedRuns));
			AddColumn(new StatColumn("Reads/s", 0, "Aggregate passes per second over 8 readers (median, min–max of the last 5 runs)",
				static s => s.ReadsPerSec, static v => v.ToString("N0")));
			AddColumn(new StatColumn("p50", 1, "Per-pass latency p50 in µs, merged over readers (median of runs)",
				static s => s.P50Us, static v => v.ToString("F2") + " us"));
			AddColumn(new StatColumn("p99", 2, "Per-pass latency p99 in µs, merged over readers (median of runs)",
				static s => s.P99Us, static v => v.ToString("F2") + " us"));
			AddColumn(new StatColumn("Writer ops/s", 3, "Writer mutating calls per second (median, min–max); 0 without a writer",
				static s => s.WriterOpsPerSec, static v => v.ToString("N0")));
		}
	}

	private sealed class StatColumn : IColumn {
		private readonly Func<RunSample, double> _select;
		private readonly Func<double, string> _format;

		public StatColumn(string name, int priority, string legend, Func<RunSample, double> select, Func<double, string> format) {
			ColumnName = name;
			PriorityInCategory = priority;
			Legend = legend;
			_select = select;
			_format = format;
		}

		public string Id => ColumnName;
		public string ColumnName { get; }
		public bool AlwaysShow => true;
		public ColumnCategory Category => ColumnCategory.Custom;
		public int PriorityInCategory { get; }
		public bool IsNumeric => true;
		public UnitType UnitType => UnitType.Dimensionless;
		public string Legend { get; }

		public string GetValue(Summary summary, BenchmarkCase benchmarkCase) {
			var writers = (int)benchmarkCase.Parameters["Writers"];
			if (!Results.TryGetValue(Key(benchmarkCase.Descriptor.WorkloadMethod.Name, writers), out var runs) || runs.Count == 0)
				return "N/A";

			var take = Math.Min(ReportedRuns, runs.Count);
			var values = new double[take];
			for (var i = 0; i < take; i++)
				values[i] = _select(runs[runs.Count - take + i]);
			Array.Sort(values);
			var median = values[take / 2];
			return take == 1 ? _format(median) : _format(median) + " [" + _format(values[0]) + ".." + _format(values[^1]) + "]";
		}

		public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
		public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
		public bool IsAvailable(Summary summary) => true;
	}
}

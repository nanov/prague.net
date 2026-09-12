namespace Prague.Benchmarks;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Prague.Core;
using Prague.Core.Collections;
using Bucket = Prague.Core.Collections.PooledSet<int, Prague.Core.Collections.DefaultKeyComparer<int>>;

/// <summary>
///   #83's ingestion bar: the B+tree-backed <see cref="LastUpdatedIndex{TKey}" /> against
///   <see cref="SpikeRingLastUpdatedIndex" />, which is the same index with the
///   <c>CacheRangeIndex</c> swapped for a bucket ring and nothing else changed — same
///   <c>ConcurrentCacheStore</c>, same refcounted group keys, same max-timestamp rule. Three shapes,
///   100k operations each:
///   <list type="bullet">
///     <item><b>AppendAscending</b> — 100k fresh keys, strictly ascending timestamps: the tree's
///       O(1) ascending-append fast path, which is the bar the issue sets.</item>
///     <item><b>Churn</b> — over a populated index, one <c>Remove</c> + one <c>Add</c> per key: the
///       shape that moves a key out of one bucket and into another (and, in the tree, does a delete
///       and an insert away from the append edge).</item>
///     <item><b>Backwards</b> — 100k fresh keys, strictly descending timestamps: every insert lands
///       before every key already in the structure, so the tree's append fast path never fires. The
///       ring is indifferent — a bucket index is arithmetic either way.</item>
///   </list>
///   The fixtures are rebuilt in <see cref="IterationSetup" />, outside the measured region. Both
///   sides take one uncontended lock per index mutation (the tree's inside <c>PooledBTree</c>, the
///   ring's around the two <c>PooledSet</c> calls, the <c>CacheKeySetIndex</c> model).
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
public class TimeRangeWriteBenchmarks {
	private const string Category = "TimeRangeWrite";
	private const int N = 100_000;
	private const long Base = 1_700_000_000_000L;
	private const long Granularity = 1_000;

	private LastUpdatedIndex<int> _treeEmpty = null!;
	private SpikeRingLastUpdatedIndex _ringEmpty = null!;
	private LastUpdatedIndex<int> _treeFull = null!;
	private SpikeRingLastUpdatedIndex _ringFull = null!;

	[GlobalSetup]
	public void Setup() {
		IterationSetup();
		// One pass of every shape against both structures, asserting they agree on what the window holds.
		Check(TreeAppendAscending(), RingAppendAscending(), "append");
		IterationSetup();
		Check(TreeChurn(), RingChurn(), "churn");
		IterationSetup();
		Check(TreeBackwards(), RingBackwards(), "backwards");
		IterationSetup();
	}

	private static void Check(int tree, int ring, string what) {
		if (tree != ring)
			throw new InvalidOperationException($"{what}: tree wrote {tree} keys, ring wrote {ring}");
	}

	[IterationSetup]
	public void IterationSetup() {
		_treeEmpty = new LastUpdatedIndex<int>();
		_ringEmpty = new SpikeRingLastUpdatedIndex(Granularity, Base);
		_treeFull = new LastUpdatedIndex<int>();
		_ringFull = new SpikeRingLastUpdatedIndex(Granularity, Base);
		for (var i = 0; i < N; i++) {
			_treeFull.Add(i, Base + i);
			_ringFull.Add(i, Base + i);
		}
	}

	[BenchmarkCategory(Category), Benchmark(Baseline = true)]
	public int TreeAppendAscending() {
		var index = _treeEmpty;
		for (var i = 0; i < N; i++)
			index.Add(i, Base + i);
		return index.GetEntitiesCount(N - 1);
	}

	[BenchmarkCategory(Category), Benchmark]
	public int RingAppendAscending() {
		var index = _ringEmpty;
		for (var i = 0; i < N; i++)
			index.Add(i, Base + i);
		return index.GetEntitiesCount(N - 1);
	}

	[BenchmarkCategory(Category), Benchmark]
	public int TreeChurn() {
		var index = _treeFull;
		for (var i = 0; i < N; i++) {
			index.Remove(i, Base + N + i);
			index.Add(i, Base + N + i);
		}

		return index.GetEntitiesCount(N - 1);
	}

	[BenchmarkCategory(Category), Benchmark]
	public int RingChurn() {
		var index = _ringFull;
		for (var i = 0; i < N; i++) {
			index.Remove(i, Base + N + i);
			index.Add(i, Base + N + i);
		}

		return index.GetEntitiesCount(N - 1);
	}

	[BenchmarkCategory(Category), Benchmark]
	public int TreeBackwards() {
		var index = _treeEmpty;
		for (var i = 0; i < N; i++)
			index.Add(i, Base + N - i);
		return index.GetEntitiesCount(N - 1);
	}

	[BenchmarkCategory(Category), Benchmark]
	public int RingBackwards() {
		var index = _ringEmpty;
		for (var i = 0; i < N; i++)
			index.Add(i, Base + N - i);
		return index.GetEntitiesCount(N - 1);
	}
}

/// <summary>
///   <see cref="LastUpdatedIndex{TKey}" /> with the B+tree replaced by a ring of lazily created
///   <c>PooledSet</c> buckets plus one cold fold set for timestamps older than the horizon. The store
///   half is copied verbatim from the real index — same factory and updater lambdas, same refcount,
///   same "keep the max timestamp" rule — so the only difference this benchmark measures is the
///   structure the key is placed into. Single-writer: mutations take the index's own lock, the
///   <c>CacheKeySetIndex</c> model, since <c>PooledSet</c> is strictly single-writer.
/// </summary>
internal sealed class SpikeRingLastUpdatedIndex {
	/// <summary>Power of two so the wrap is one AND; horizon = <c>RingLength × granularity</c>.</summary>
	private const int RingLength = 4096;
	private const int RingMask = RingLength - 1;

	private readonly ConcurrentCacheStore<int, (long Value, int Count)> _store = new();
	private readonly Bucket?[] _ring = new Bucket?[RingLength];
	private readonly Bucket _cold = new();
	private readonly object _lock = new();
	private readonly long _granularity;
	private readonly long _base;

	internal SpikeRingLastUpdatedIndex(long granularity, long ringBase) {
		_granularity = granularity;
		_base = ringBase;
	}

	internal void Add(int key, long timestampMs) {
		var r = _store.AddOrUpdate(key,
			static (_, v) => (v, 1),
			static (_, existing, v) => v > existing.Value
				? (v, existing.Count + 1)
				: (existing.Value, existing.Count + 1),
			timestampMs);

		switch (r.Operation) {
			case AddOrUpdateOperation.Add:
				Place(key, r.Value.Value);
				return;
			case AddOrUpdateOperation.Update:
				Move(key, r.OldValue.Value, r.Value.Value);
				return;
			default:
				return;
		}
	}

	internal void Remove(int key, long updateTimestampMs) {
		var result = _store.UpdateOrRemove(key,
			static (_, existing, newTs) => existing.Count > 1
				? (true, (newTs, existing.Count - 1))
				: (false, default),
			updateTimestampMs);

		switch (result.Operation) {
			case UpdateOrRemoveOperation.Update:
				Move(key, result.OldValue.Value, result.NewValue.Value);
				break;
			case UpdateOrRemoveOperation.Remove:
				Displace(key, result.OldValue.Value);
				break;
		}
	}

	internal int GetEntitiesCount(int key) => _store.TryGetValue(key, out var entry) ? entry.Count : 0;

	private void Place(int key, long timestampMs) {
		lock (_lock)
			BucketFor(timestampMs).Add(key);
	}

	private void Move(int key, long oldTimestampMs, long newTimestampMs) {
		if (oldTimestampMs == newTimestampMs)
			return;
		lock (_lock) {
			BucketFor(oldTimestampMs).Remove(key);
			BucketFor(newTimestampMs).Add(key);
		}
	}

	private void Displace(int key, long timestampMs) {
		lock (_lock)
			BucketFor(timestampMs).Remove(key);
	}

	/// <summary>The bucket a timestamp belongs to, created on first use; everything older than the horizon folds into one cold set.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Bucket BucketFor(long timestampMs) {
		if (timestampMs < _base)
			return _cold;
		var slot = (int)((timestampMs - _base) / _granularity) & RingMask;
		return _ring[slot] ??= new Bucket();
	}
}

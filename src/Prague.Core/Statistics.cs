namespace Prague.Core;

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Collections;

public static class DataCacheStatisticsMarshall {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(DataCacheStatistics stats, DateTimeOffset timestmap, TimeSpan periodLength) {
		stats.TakeSnapshot(timestmap, periodLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(DataCacheStatistics stats, DateTimeOffset timestmap) {
		stats.TakeSnapshot(timestmap);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(DataCacheStatistics stats) {
		stats.TakeSnapshot(DateTimeOffset.UtcNow);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(IReadOnlyDictionary<string, DataCacheStatistics> stats) {
		foreach (var entry in stats.Values)
			entry.TakeSnapshot();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(ReadOnlySpan<DataCacheStatistics> stats, DateTimeOffset timestmap) {
		foreach (var entry in stats)
			entry.TakeSnapshot(timestmap);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(ReadOnlySpan<DataCacheStatistics> stats, DateTimeOffset timestmap,
		TimeSpan periodLength) {
		foreach (var entry in stats)
			entry.TakeSnapshot(timestmap, periodLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(IReadOnlyDictionary<string, DataCacheStatistics> stats, DateTimeOffset timestmap) {
		foreach (var entry in stats.Values)
			entry.TakeSnapshot(timestmap);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void TakeSnapshot(IReadOnlyDictionary<string, DataCacheStatistics> stats, DateTimeOffset timestmap,
		TimeSpan periodLength) {
		foreach (var entry in stats.Values)
			entry.TakeSnapshot(timestmap, periodLength);
	}

	// [MethodImpl(MethodImplOptions.AggressiveInlining)]
	// public static void TakeSnapshot(IDataCacheRegistry registry,  DateTimeOffset timestmap, TimeSpan periodLength) {
	// 	TakeSnapshot(registry.Statistics, timestmap, periodLength);
	// }
	public static void AddIndex(DataCacheStatistics stats, string name, DataCacheIndexType type,
		ICountableCacheIndex index) {
		stats.AddIndex(name, type, index);
	}
}

public sealed class DataCacheStatistics {
	private readonly DataCacheStatisticsCollector _collector;

	private long _collectedAtUnixMs;
	public DateTimeOffset CollectedAt;

	public FrozenDictionary<string, DataCacheIndexStatistics> Indexes =
		FrozenDictionary<string, DataCacheIndexStatistics>.Empty;

	public TimeSpan PeriodLength;

	/// <summary>
	/// Snapshot size taken at the last snapshot time.
	/// </summary>
	public ulong Size;

	/// <summary>
	/// Live size - current size of the cache (not snapshot).
	/// </summary>
	public ulong LiveSize => _collector.CurrentSize;

	public DataCacheStatistics(DataCacheStatisticsCollector collector) {
		_collector = collector;
	}

	public static DataCacheStatistics Create<TKey, TValue>(InMemoryDataCache<TKey, TValue> cache)
		where TKey : IEquatable<TKey> where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		return new DataCacheStatistics(cache.StatisticsCollector);
	}

	internal void AddIndex(string name, DataCacheIndexType type, ICountableCacheIndex index) {
		var dict = Indexes.ToDictionary();
		ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, name, out var exists);
		entry = new DataCacheIndexStatistics(type, index);
		Indexes = dict.ToFrozenDictionary();
	}

	internal void TakeSnapshot(DateTimeOffset timestmap, TimeSpan periodLength) {
		ExchangeTimestamp(timestmap);
		ExchangePeriod(periodLength);
		SnapShotValues();
	}

	private DateTimeOffset ExchangeTimestamp(DateTimeOffset timestmap) {
		Interlocked.Exchange(ref _collectedAtUnixMs, timestmap.ToUnixTimeMilliseconds());
		var was = CollectedAt;
		CollectedAt = DateTimeOffset.FromUnixTimeMilliseconds(_collectedAtUnixMs);
		return was;
	}

	private void ExchangePeriod(TimeSpan span) {
		Interlocked.Exchange(ref Unsafe.As<TimeSpan, long>(ref PeriodLength), span.Ticks);
	}

	internal void TakeSnapshot(DateTimeOffset timestmap) {
		ExchangePeriod(timestmap - ExchangeTimestamp(timestmap));
		SnapShotValues();
	}

	internal void TakeSnapshot() {
		TakeSnapshot(DateTimeOffset.UtcNow);
	}

	private void SnapShotValues() {
		Interlocked.Exchange(ref Size, _collector.Collect());
		foreach (var index in Indexes.Values)
			index.TakeSnapshot();
	}
}

public sealed class DataCacheIndexStatistics {
	private readonly ICountableCacheIndex _index;
	public readonly DataCacheIndexType Type;

	/// <summary>
	/// Snapshot keys size taken at the last snapshot time.
	/// </summary>
	public ulong KeysSize;

	/// <summary>
	/// Snapshot values size taken at the last snapshot time.
	/// </summary>
	public ulong ValuesSize;

	/// <summary>
	/// Live keys size - current number of unique keys in the index (not snapshot).
	/// </summary>
	public ulong LiveKeysSize => _index.GetCounters(out _);

	/// <summary>
	/// Live values size - current number of values in the index (not snapshot).
	/// </summary>
	public ulong LiveValuesSize {
		get {
			_index.GetCounters(out var valuesSize);
			return valuesSize;
		}
	}

	internal DataCacheIndexStatistics(DataCacheIndexType type, ICountableCacheIndex index) {
		(Type, _index) = (type, index);
	}

	public void TakeSnapshot() {
		var keysCount = _index.GetCounters(out var valuesSize);
		Interlocked.Exchange(ref KeysSize, keysCount);
		Interlocked.Exchange(ref ValuesSize, valuesSize);
	}
}

public abstract class DataCacheStatisticsCollector {
	public abstract ulong CurrentSize { get; }
	public abstract void Performed(AddOrUpdateOperation operation);
	public abstract void Added();
	public abstract void Updated();
	public abstract void Removed();
	public abstract ulong Collect();
}

public sealed class DataCacheNoOpStatisticsCollector : DataCacheStatisticsCollector {
	public static readonly DataCacheNoOpStatisticsCollector Default = new();

	public override ulong CurrentSize => 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Performed(AddOrUpdateOperation operation) {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Added() {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Updated() {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Removed() {
	}

	public override ulong Collect() => 0;
}

public sealed class DataCacheCountersStatisticsCollector : DataCacheStatisticsCollector {
	private ulong _size;

	public override ulong CurrentSize => _size;

	public override void Performed(AddOrUpdateOperation operation) {
		switch (operation) {
			case AddOrUpdateOperation.Add:
				Added();
				return;
			case AddOrUpdateOperation.Update:
				Updated();
				return;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Added() {
		_size++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Updated() {
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override void Removed() {
		_size--;
	}

	public override ulong Collect() => _size;
}

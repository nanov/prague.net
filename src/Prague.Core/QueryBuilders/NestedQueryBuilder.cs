namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Collections;

public interface ICandidatesFilterer<TKey, TValue> where TKey : notnull, IEquatable<TKey> {
	#region UseIndex methods (internal — exposed via extension methods constrained on IBaseFilterable)

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal<TIndexKey>(
		CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal<TIndexKey>(CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values)
		where TIndexKey : notnull;

	internal void UseIndexInternal<TIndexKey>(CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values)
		where TIndexKey: notnull;

	internal void UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value)
		where TIndexKey : notnull;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values) where TIndexKey : notnull;

	internal void UseIndexInternal<TIndexKey, TOtherValue>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TOtherValue> values,
		Func<TOtherValue, TIndexKey> keySelector)
		where TIndexKey : notnull;

	internal void UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values)
		where TIndexKey : notnull;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		out long max);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		out long max);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max);

	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive);

	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive);

	internal void UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive);

	internal void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive);

	internal void UseIndexInternal<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder)
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey>;

	internal void UseIndexInternal<TIndexKey, TQueryBuilder, TArgs>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index,
		Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder, TArgs args)
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey>;

	internal void UseIndexInternal(CacheKeySetIndex<TKey, TValue> index);

	internal void WhereInternal(Predicate<TValue> predicate);

	#endregion
}

public interface IUnsafeCandidatesExecutor {
	[UnscopedRef]
	internal ref ValueSet<TKey, DefaultKeyComparer<TKey>> GetCandidates<TKey>() => throw new InvalidOperationException();

}
public interface ICandidatesExecutor<TLeftKey, TLeftValue> : IUnsafeCandidatesExecutor where TLeftKey : notnull {
	internal void ExecuteBase<TContainer>(ref TContainer c)
		where TContainer : struct, IResultContainerInitializer<TLeftKey, TLeftValue>, allows ref struct;

	internal int CountBase();

	[UnscopedRef]
	internal ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> Candidates { get; }
}




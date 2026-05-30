namespace Prague.Core;

using System.Runtime.CompilerServices;
using Utils;

public interface IJoinResult<TLeft> : IJoinResult {
	internal ref TLeft Left { get; }
	static abstract int Size { get; }

}


/// <summary>
/// Cloner for JoinResult<TLeft, T1> with pluggable cloners for each field.
/// </summary>
public struct JoinResultCloner<TLeft, T1, TLeftCloner, T1Cloner> : ICloner<JoinResult<TLeft, T1>>
	where TLeftCloner : struct, ICloner<TLeft>
	where T1Cloner : struct, ICloner<T1> {
	public static int ResultCount => 2;

	private TLeftCloner _leftCloner;
	private T1Cloner _t1Cloner;

	public JoinResultCloner(TLeftCloner leftCloner, T1Cloner t1Cloner) {
		_leftCloner = leftCloner;
		_t1Cloner = t1Cloner;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref JoinResult<TLeft, T1> value) {
		_leftCloner.Clone(ref Unsafe.AsRef(in value.Left));
		_t1Cloner.Clone(ref Unsafe.AsRef(in value.Right)!);
	}
}



// #region Level 1 Extension Methods
//
// public static class JoinQueryBuilderLevel1SortExtensions {
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<SortedQuery<TDiscriminator>, TLeftQuery, TLeftKey, TLeftValue, SortedResolverChain<TLeftKey, TLeftValue, TResolverChain, TComparer, T1>, T1>
// 		Sort<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TComparer>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			TComparer comparer)
// 		where TDiscriminator : struct, ISortable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TComparer : IComparer<JoinResult<TLeftValue, T1>>
// 	{
// 		var sorted = new SortedResolverChain<TLeftKey, TLeftValue, TResolverChain, TComparer, T1>(builder._resolverChain, comparer);
// 		return new(new SortedQuery<TDiscriminator>(builder._discriminator), builder._leftQuery, sorted, builder._manyCount);
// 	}
// }
//
// public static class JoinQueryBuilderLevel1JoinExtensions {
//
// 	/// <summary>Join one-to-one via reverse index.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		JoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Join one-to-one via forward foreign key selector.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		JoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TLeftValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Join one-to-many via reverse list index.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, QueryResults<TRightValue>>,
// 		T1, QueryResults<TRightValue>>
// 		JoinMany<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, QueryResults<TRightValue>>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount + 1);
// 	}
// }
//
// public static class JoinQueryBuilderLevel1InnerJoinExtensions {
//
// 	/// <summary>Inner join one-to-one via reverse index.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		InnerJoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward foreign key selector.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		InnerJoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TLeftValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter, inner: true);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward foreign key selector with predicate.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		InnerJoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TLeftValue, TRightKey> foreignKeySelector,
// 			Func<TLeftValue, TRightValue, bool> predicate)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, predicate);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward foreign key selector with predicate and args.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>,
// 		T1, TRightValue?>
// 		InnerJoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue, TArg>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TLeftValue, TRightKey> foreignKeySelector,
// 			Func<TLeftValue, TRightValue, TArg, bool> predicate,
// 			TArg arg)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, (TLeftValue left, TRightValue right) => predicate(left, right, arg));
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, TRightValue?>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Indexed inner join one-to-one via left index + foreign key selector.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TIndexKey, TRightKey, TRightValue>, T1, TRightValue>,
// 		T1, TRightValue>
// 		InnerJoinOne<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TIndexKey, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			CacheKeyValueIndex<TIndexKey, TLeftKey, TLeftKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 		where TIndexKey : notnull
// 	{
// 		var resolver = new JoinOneResolver<TLeftKey, TLeftValue, TIndexKey, TRightKey, TRightValue>(
// 			rightCache, leftIndex, foreignKeySelector, rightQueryFilter, inner: true);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, JoinOneResolver<TLeftKey, TLeftValue, TIndexKey, TRightKey, TRightValue>, T1, TRightValue>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount);
// 	}
//
// 	/// <summary>Inner join one-to-many via reverse list index.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue,
// 		ResolverChain<TLeftKey, TLeftValue, TResolverChain, ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, QueryResults<TRightValue>>,
// 		T1, QueryResults<TRightValue>>
// 		InnerJoinMany<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1, TRightKey, TRightValue>(
// 			this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TLeftKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
// 	{
// 		var resolver = new ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		var chain = new ResolverChain<TLeftKey, TLeftValue, TResolverChain, ManyResolver<TLeftKey, TLeftValue, TRightKey, TRightValue>, T1, QueryResults<TRightValue>>(builder._resolverChain, resolver);
// 		return new(builder._discriminator, builder._leftQuery, chain, builder._manyCount + 1);
// 	}
// }
//
// public static class JoinQueryBuilderLevel1ExecuteExtensions {
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> Execute<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(false, false, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecuteCloned<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(false, true, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecutePooled<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(true, false, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecutePooledCloned<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(true, true, skip, take);
// }
//
// public static class JoinQueryBuilderLevel1SortedExecuteExtensions {
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> Execute<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<SortedQuery<TDiscriminator>, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(false, false, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecuteCloned<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<SortedQuery<TDiscriminator>, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(false, true, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecutePooled<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<SortedQuery<TDiscriminator>, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(true, false, skip, take);
//
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static QueryResults<JoinResult<TLeftValue, T1>> ExecutePooledCloned<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1>(
// 		this in JoinQueryBuilder<SortedQuery<TDiscriminator>, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, T1> builder,
// 		int skip = 0, int take = int.MaxValue)
// 		where TDiscriminator : struct, IExecutableQuery
// 		where TLeftKey : notnull, IEquatable<TLeftKey>
// 		where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
// 		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
// 		where TResolverChain : struct, IResolverChain<TLeftKey, TLeftValue, T1>, ISortableResolverChain<TLeftKey, TLeftValue, T1>
// 		=> Unsafe.AsRef(in builder).ExecuteCore(true, true, skip, take);
// }
//
// #endregion

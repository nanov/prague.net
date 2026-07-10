namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

public static class CacheQueryBuilder_JoinManyCollectionForward_Extensions {

	// ════════════════════════════════════════════════════════════════════════════
	// JoinManyCollection — M:N forward-collection join (3 outer + 3 inner = 6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinManyCollection(rightCache, symmetricIndex, [filter, [arg]])
	// Driving cache (left = element, e.g. Tag) → owners-of-an-element (right = owner,
	// e.g. Book whose List<int> TagIds contains the tag). The symmetric collection
	// index over the right cache exposes Forward (tagId → {bookKeys}) and Reverse
	// (bookKey → {tagIds}); each owner fans out into every element's slot via a
	// LeftKeySetView over its Reverse element-set — so a book shared by two tags
	// appears under BOTH. Identity element-key only (no key selector for v1).

	// ── Shape C1: no filter ──────────────────────────────────────────────────

	/// <summary>
	/// M:N forward-collection join — no filter. For each left (element) key,
	/// <paramref name="index"/>.Forward maps it to the owner keys; each owner is
	/// written into every one of its element slots (Reverse fan-out).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>(
			index.Reverse, index.Forward, (TRightCache)rightCache, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape C2: with filter ────────────────────────────────────────────────

	/// <summary>
	/// M:N forward-collection join — with a filter on the right (owner) cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>(
			index.Reverse, index.Forward, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape C3: with filter + arg ──────────────────────────────────────────

	/// <summary>
	/// M:N forward-collection join — with a filter and captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>(
			index.Reverse, index.Forward, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// InnerJoinManyCollection — drops lefts (elements) with no owners (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Same M:N fan-out, but lefts whose per-left QueryResults<TRightValue> stays empty
	// (no owner, or filter rejected them all) are dropped via post-walk
	// RetainNonEmptyManySlots — see resolver UnsafeExecuteIndexedInner.

	// ── Shape CI1: no filter ─────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>(
			index.Reverse, index.Forward, (TRightCache)rightCache, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape CI2: with filter ───────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>(
			index.Reverse, index.Forward, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape CI3: with filter + arg ─────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinManyCollectionForward<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheCollectionSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> index,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>(
			index.Reverse, index.Forward, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TLeftValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>>>(
				builder._resolverChain, resolver),
			true);
	}
}
